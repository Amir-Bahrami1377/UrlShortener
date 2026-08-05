using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shortener.Application.Abstractions;
using Shortener.Application.Contracts;
using Shortener.Application.Services;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Observability;
using Shortener.Infrastructure.Persistence;
using Shortener.Infrastructure.Queueing;
using StackExchange.Redis;

namespace Shortener.Infrastructure.Otp;

public sealed class OtpService(
    AppDbContext db,
    IConnectionMultiplexer redis,
    IShortLinkStateResolver stateResolver,
    ILinkAccessLogger accessLogger,
    IOptions<OtpOptions> otpOptions,
    IOptions<DownloadOptions> downloadOptions,
    IOptions<QueueOptions> queueOptions,
    ShortenerMetrics metrics,
    ILogger<OtpService> logger) : IOtpService
{
    // Dev-only default — real deployments must override Otp:HmacSecret via User Secrets/environment (§2.3).
    private const string DefaultHmacSecret = "dev-only-otp-hmac-secret-change-in-production";

    private readonly OtpOptions _otp = otpOptions.Value;
    private readonly DownloadOptions _download = downloadOptions.Value;
    private readonly QueueOptions _queue = queueOptions.Value;

    public async Task<OtpRequestResult> RequestAsync(string code, string ipAddress, CancellationToken ct)
    {
        var state = await stateResolver.ResolveAsync(code, ct);
        if (state is null)
        {
            return new OtpRequestResult(OtpRequestOutcome.NotFound);
        }

        metrics.OtpRequested(state.ClientId);

        var health = state.GetHealthStatus(DateTime.UtcNow);
        if (health != LinkHealthStatus.Healthy)
        {
            return new OtpRequestResult(OtpRequestOutcome.LinkUnhealthy, UnhealthyReason: health);
        }

        var link = await db.ShortLinks.FirstAsync(l => l.Id == state.ShortLinkId, ct);
        var now = DateTime.UtcNow;
        if (link.LockedUntil > now)
        {
            return new OtpRequestResult(OtpRequestOutcome.Locked);
        }

        var redisDb = redis.GetDatabase();

        var cooldownKey = $"otp:cd:{code}";
        if (await redisDb.KeyExistsAsync(cooldownKey))
        {
            var ttl = await redisDb.KeyTimeToLiveAsync(cooldownKey);
            return new OtpRequestResult(OtpRequestOutcome.Cooldown, CooldownSecondsRemaining: (int?)ttl?.TotalSeconds);
        }

        if (await IncrementAndCheckLimitAsync(redisDb, $"otp:cnt:{code}", _otp.MaxRequestsPerLinkPerHour))
        {
            return new OtpRequestResult(OtpRequestOutcome.LimitPerLink);
        }

        var ipHash = RequestFingerprint.Hash(ipAddress);
        if (await IncrementAndCheckLimitAsync(redisDb, $"otp:ip:{ipHash}", _otp.MaxRequestsPerIpPerHour))
        {
            return new OtpRequestResult(OtpRequestOutcome.LimitPerIp);
        }

        var smsAccount = await db.SmsAccounts
            .FirstOrDefaultAsync(a => a.ClientId == state.ClientId && a.Purpose == SmsAccountPurpose.Otp && a.IsActive, ct);
        var template = await db.MessageTemplates
            .FirstOrDefaultAsync(t => t.ClientId == state.ClientId && t.TemplateType == TemplateType.Otp && t.IsActive, ct);

        if (smsAccount is null || template is null)
        {
            logger.LogWarning(
                "OTP not configured for Client {ClientId}: account={HasAccount}, template={HasTemplate}",
                state.ClientId, smsAccount is not null, template is not null);
            return new OtpRequestResult(OtpRequestOutcome.NotConfigured);
        }

        var otp = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();
        await redisDb.StringSetAsync($"otp:{code}", ComputeHmac(otp), TimeSpan.FromSeconds(_otp.TtlSeconds));
        await redisDb.StringSetAsync(cooldownKey, 1, TimeSpan.FromSeconds(_otp.ResendCooldownSeconds));
        await redisDb.KeyDeleteAsync($"otp:fail:{code}");

        var body = TemplateRenderer.Render(template.Body, new Dictionary<string, string>
        {
            ["otp"] = otp,
            ["otpMinutes"] = (_otp.TtlSeconds / 60).ToString(),
        });

        var smsMessage = new SmsMessage
        {
            ShortLinkId = link.Id,
            SmsAccountId = smsAccount.Id,
            TemplateId = template.Id,
            MessageType = SmsMessageType.Otp,
            PhoneNumber = link.PhoneNumber,
            Body = body,
            Status = SmsStatus.Queued,
        };
        db.SmsMessages.Add(smsMessage);
        await db.SaveChangesAsync(ct);

        await redisDb.StreamAddAsync(_queue.OtpStream, "smsMessageId", smsMessage.Id);

        link.OtpRequestCount += 1;
        link.LastOtpRequestedAt = now;
        await db.SaveChangesAsync(ct);

        accessLogger.Enqueue(new LinkAccessLogEntry(link.Id, LinkAccessType.OtpRequested, ipAddress, null, true, null));

        return new OtpRequestResult(OtpRequestOutcome.Sent, _otp.ResendCooldownSeconds, _otp.TtlSeconds);
    }

    public async Task<OtpVerifyResult> VerifyAsync(string code, string otp, string ipAddress, string userAgent, CancellationToken ct)
    {
        var state = await stateResolver.ResolveAsync(code, ct);
        if (state is null)
        {
            metrics.OtpVerify("not_found");
            return new OtpVerifyResult(OtpVerifyOutcome.NotFound);
        }

        var health = state.GetHealthStatus(DateTime.UtcNow);
        if (health != LinkHealthStatus.Healthy)
        {
            metrics.OtpVerify("link_unhealthy");
            return new OtpVerifyResult(OtpVerifyOutcome.LinkUnhealthy, UnhealthyReason: health);
        }

        var link = await db.ShortLinks.FirstAsync(l => l.Id == state.ShortLinkId, ct);
        var now = DateTime.UtcNow;
        if (link.LockedUntil > now)
        {
            metrics.OtpVerify("locked");
            return new OtpVerifyResult(OtpVerifyOutcome.Locked, LockedUntil: link.LockedUntil);
        }

        var redisDb = redis.GetDatabase();
        var otpKey = $"otp:{code}";
        var storedHmac = await redisDb.StringGetAsync(otpKey);
        if (!storedHmac.HasValue)
        {
            metrics.OtpVerify("expired");
            return new OtpVerifyResult(OtpVerifyOutcome.Expired);
        }

        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(ComputeHmac(otp)), Encoding.UTF8.GetBytes(storedHmac!));

        if (!matches)
        {
            var failCount = await redisDb.StringIncrementAsync($"otp:fail:{code}");

            if (failCount >= _otp.MaxVerifyAttempts)
            {
                await redisDb.KeyDeleteAsync(otpKey);
                link.LockedUntil = now.AddMinutes(_otp.LockoutMinutes);
                link.FailedVerifyCount += 5;
                await db.SaveChangesAsync(ct);

                accessLogger.Enqueue(new LinkAccessLogEntry(link.Id, LinkAccessType.VerifyFailed, ipAddress, userAgent, false, "ERR_TOO_MANY_ATTEMPTS"));
                metrics.OtpVerify("too_many_attempts");
                return new OtpVerifyResult(OtpVerifyOutcome.TooManyAttempts, LockedUntil: link.LockedUntil);
            }

            accessLogger.Enqueue(new LinkAccessLogEntry(link.Id, LinkAccessType.VerifyFailed, ipAddress, userAgent, false, "ERR_OTP_INVALID"));
            metrics.OtpVerify("invalid");
            return new OtpVerifyResult(OtpVerifyOutcome.Invalid, RemainingAttempts: _otp.MaxVerifyAttempts - (int)failCount);
        }

        await redisDb.KeyDeleteAsync(otpKey);
        await redisDb.KeyDeleteAsync($"otp:fail:{code}");

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = new DownloadTokenPayload(link.Id, RequestFingerprint.Hash(ipAddress), RequestFingerprint.Hash(userAgent));
        await redisDb.StringSetAsync($"dl:{token}", JsonSerializer.Serialize(payload), TimeSpan.FromSeconds(_download.TokenTtlSeconds));

        link.FirstVerifiedAt ??= now;
        link.FailedVerifyCount = 0;
        await db.SaveChangesAsync(ct);

        accessLogger.Enqueue(new LinkAccessLogEntry(link.Id, LinkAccessType.VerifySuccess, ipAddress, userAgent, true, null));
        metrics.OtpVerify("success");

        return new OtpVerifyResult(OtpVerifyOutcome.Success, DownloadToken: token);
    }

    /// <summary>Increments the counter, sets a 1-hour expiry only on first creation, and reports whether the limit was exceeded.</summary>
    private static async Task<bool> IncrementAndCheckLimitAsync(IDatabase redisDb, string key, int limit)
    {
        var count = await redisDb.StringIncrementAsync(key);
        if (count == 1)
        {
            await redisDb.KeyExpireAsync(key, TimeSpan.FromHours(1));
        }

        return count > limit;
    }

    private string ComputeHmac(string otp)
    {
        var secret = Environment.GetEnvironmentVariable("OTP_HMAC_SECRET") ?? DefaultHmacSecret;
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(otp));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
