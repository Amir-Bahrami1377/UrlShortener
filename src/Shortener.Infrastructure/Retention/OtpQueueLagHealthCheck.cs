using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Shortener.Infrastructure.Queueing;
using StackExchange.Redis;

namespace Shortener.Infrastructure.Retention;

/// <summary>§M8.3 — "OTP queue lag, critical if > 60s." A distinct threshold from §M5.7's 30s
/// log-level critical alert (QueueMaintenanceService.CheckLagAsync) — that one is an operational
/// alert tuned to page someone quickly; this one is the harder line for "stop routing traffic here."</summary>
public sealed class OtpQueueLagHealthCheck(IConnectionMultiplexer redis, IOptions<QueueOptions> queueOptions) : IHealthCheck
{
    private const double CriticalSeconds = 60;
    private readonly QueueOptions _queue = queueOptions.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = redis.GetDatabase();
            var pending = await db.StreamPendingMessagesAsync(_queue.OtpStream, _queue.OtpConsumerGroup, 1, RedisValue.Null, "-", "+");
            if (pending.Length == 0)
            {
                return HealthCheckResult.Healthy("صف OTP بدون پیام معلق است.");
            }

            var lagSeconds = pending.Max(p => p.IdleTimeInMilliseconds) / 1000.0;
            var data = new Dictionary<string, object> { ["lagSeconds"] = Math.Round(lagSeconds, 1) };

            return lagSeconds > CriticalSeconds
                ? HealthCheckResult.Unhealthy($"تأخیر صف OTP {lagSeconds:N1} ثانیه است.", data: data)
                : HealthCheckResult.Healthy($"تأخیر صف OTP {lagSeconds:N1} ثانیه است.", data);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("NOGROUP"))
        {
            // No OTP consumer group yet means no OTP traffic has ever flowed — not a failure.
            return HealthCheckResult.Healthy("گروه مصرف‌کننده OTP هنوز ایجاد نشده است.");
        }
        catch (RedisException ex)
        {
            return HealthCheckResult.Unhealthy("امکان بررسی تأخیر صف OTP نبود.", ex);
        }
    }
}
