using Microsoft.Extensions.Options;
using Shortener.Infrastructure.Queueing;
using StackExchange.Redis;

namespace Shortener.Worker.Workers;

/// <summary>
/// §M5.4 (XAUTOCLAIM recovery), §M5.5 (stream trim), §M5.7 (queue-lag monitoring) — three small,
/// Redis-only periodic jobs against the same two streams, combined into one BackgroundService rather
/// than three, each on its own internal cadence. §M5.6's DeliveryStatusPollerService stays separate
/// since it does real DB + provider HTTP work, a different weight class from these.
/// </summary>
public sealed class QueueMaintenanceService(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    IOptions<QueueOptions> queueOptions,
    ILogger<QueueMaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan ClaimInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan TrimInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    private const double OtpLagCriticalSeconds = 30;

    private readonly QueueOptions _options = queueOptions.Value;
    private DateTime _lastClaim = DateTime.MinValue;
    private DateTime _lastTrim = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = redis.GetDatabase();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckLagAsync(db);

                if (DateTime.UtcNow - _lastClaim >= ClaimInterval)
                {
                    using (var scope = scopeFactory.CreateScope())
                    {
                        await ClaimStreamAsync(db, scope.ServiceProvider, _options.OtpStream, _options.OtpConsumerGroup, "otp-claimer", stoppingToken);
                        await ClaimStreamAsync(db, scope.ServiceProvider, _options.BulkStream, _options.BulkConsumerGroup, "bulk-claimer", stoppingToken);
                    }

                    _lastClaim = DateTime.UtcNow;
                }

                if (DateTime.UtcNow - _lastTrim >= TrimInterval)
                {
                    await db.StreamTrimAsync(_options.OtpStream, _options.StreamMaxLen, useApproximateMaxLength: true);
                    await db.StreamTrimAsync(_options.BulkStream, _options.StreamMaxLen, useApproximateMaxLength: true);
                    _lastTrim = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "QueueMaintenanceService tick failed");
            }

            await Task.Delay(TickInterval, stoppingToken);
        }
    }

    /// <summary>§M5.4 — messages idle longer than ClaimIdleMinutes are reclaimed under this
    /// instance's own "claimer" consumer name. Ones already delivered more than MaxDeliveryAttempts
    /// times (Redis's own per-message delivery counter) are dead-lettered outright by
    /// StreamClaimService; the rest come back here for actual reprocessing — XAUTOCLAIM/XCLAIM hands
    /// them to a new consumer, but nothing resends them unless something actively runs them back
    /// through the send algorithm.</summary>
    private async Task ClaimStreamAsync(
        IDatabase db, IServiceProvider services, string stream, string group, string claimerName, CancellationToken ct)
    {
        var claimService = services.GetRequiredService<StreamClaimService>();
        var result = await claimService.ClaimEligibleAsync(stream, group, claimerName, ct);

        if (result.ToReprocess.Count == 0)
        {
            return;
        }

        var entryHandler = services.GetRequiredService<SmsStreamEntryHandler>();
        foreach (var entry in result.ToReprocess)
        {
            await entryHandler.HandleAsync(db, services, stream, group, _options.DeadStream, entry, ct);
        }
    }

    /// <summary>§M5.7 — same "oldest pending idle time" signal already surfaced on the Admin
    /// dashboard (ReportsController.GetOtpQueueLagAsync). Emitting this as an OpenTelemetry gauge is
    /// §M8.2's job; this is the log-based critical alert half of §M5.7 only.</summary>
    private async Task CheckLagAsync(IDatabase db)
    {
        try
        {
            var otpPending = await db.StreamPendingMessagesAsync(_options.OtpStream, _options.OtpConsumerGroup, 1, RedisValue.Null, "-", "+");
            if (otpPending.Length == 0)
            {
                return;
            }

            var lagSeconds = otpPending.Max(p => p.IdleTimeInMilliseconds) / 1000.0;
            if (lagSeconds > OtpLagCriticalSeconds)
            {
                logger.LogCritical("OTP queue lag is {LagSeconds:N1}s, exceeding the {ThresholdSeconds}s target", lagSeconds, OtpLagCriticalSeconds);
            }
        }
        catch (RedisServerException ex) when (ex.Message.Contains("NOGROUP"))
        {
            // Consumer group doesn't exist yet — no OTP traffic has flowed through this Redis instance.
        }
    }
}
