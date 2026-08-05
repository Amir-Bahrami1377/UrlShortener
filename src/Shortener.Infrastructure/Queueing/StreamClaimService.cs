using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;
using StackExchange.Redis;

namespace Shortener.Infrastructure.Queueing;

public sealed record StreamClaimResult(int DeadLettered, IReadOnlyList<StreamEntry> ToReprocess);

/// <summary>
/// §M5.4 — XAUTOCLAIM recovery, extracted out of the Worker's QueueMaintenanceService BackgroundService
/// loop so the claim/dead-letter mechanics are directly testable (seed a stuck pending entry under one
/// consumer, run this under another, assert it's reclaimed) instead of only verifiable by killing a
/// live Worker process, as it was throughout M5. Dispatching successfully-claimed entries back through
/// the send algorithm stays the caller's job (via SmsStreamEntryHandler, which lives in
/// Shortener.Worker and is already covered by SmsStreamMessageProcessorTests) — this service owns only
/// the Redis-native claim/dead-letter half.
/// </summary>
public sealed class StreamClaimService(
    IConnectionMultiplexer redis, AppDbContext db, IOptions<QueueOptions> queueOptions, ILogger<StreamClaimService> logger)
{
    private readonly QueueOptions _options = queueOptions.Value;

    public async Task<StreamClaimResult> ClaimEligibleAsync(string stream, string group, string claimerName, CancellationToken ct)
    {
        var redisDb = redis.GetDatabase();

        StreamPendingMessageInfo[] pending;
        try
        {
            pending = await redisDb.StreamPendingMessagesAsync(stream, group, 1000, RedisValue.Null, "-", "+");
        }
        catch (RedisServerException ex) when (ex.Message.Contains("NOGROUP"))
        {
            return new StreamClaimResult(0, []); // stream/group doesn't exist yet.
        }

        var idleThresholdMs = (long)TimeSpan.FromMinutes(_options.ClaimIdleMinutes).TotalMilliseconds;
        var eligible = pending.Where(p => p.IdleTimeInMilliseconds >= idleThresholdMs).ToList();
        if (eligible.Count == 0)
        {
            return new StreamClaimResult(0, []);
        }

        var toDeadLetter = eligible.Where(p => p.DeliveryCount > _options.MaxDeliveryAttempts).Select(p => p.MessageId).ToArray();
        var toReprocess = eligible.Where(p => p.DeliveryCount <= _options.MaxDeliveryAttempts).Select(p => p.MessageId).ToArray();

        var deadLettered = 0;
        if (toDeadLetter.Length > 0)
        {
            var claimed = await redisDb.StreamClaimAsync(stream, group, claimerName, idleThresholdMs, toDeadLetter);

            foreach (var entry in claimed)
            {
                var idField = entry.Values.FirstOrDefault(v => v.Name == "smsMessageId");
                if (!idField.Name.IsNullOrEmpty && long.TryParse(idField.Value.ToString(), out var smsMessageId))
                {
                    await db.SmsMessages.Where(m => m.Id == smsMessageId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(m => m.Status, SmsStatus.Failed)
                            .SetProperty(m => m.LastError, "Exceeded MaxDeliveryAttempts (stuck or repeatedly-crashed consumer)"), ct);
                    await redisDb.StreamAddAsync(_options.DeadStream, "smsMessageId", smsMessageId);
                    logger.LogWarning(
                        "SmsMessage {SmsMessageId} on {Stream} exceeded MaxDeliveryAttempts via claimer — dead-lettered", smsMessageId, stream);
                    deadLettered++;
                }

                await redisDb.StreamAcknowledgeAsync(stream, group, entry.Id);
            }
        }

        IReadOnlyList<StreamEntry> toReprocessEntries = [];
        if (toReprocess.Length > 0)
        {
            toReprocessEntries = await redisDb.StreamClaimAsync(stream, group, claimerName, idleThresholdMs, toReprocess);
        }

        return new StreamClaimResult(deadLettered, toReprocessEntries);
    }
}
