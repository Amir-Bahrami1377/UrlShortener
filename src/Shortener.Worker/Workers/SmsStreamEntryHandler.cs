using Shortener.Infrastructure.Queueing;
using StackExchange.Redis;

namespace Shortener.Worker.Workers;

/// <summary>
/// Shared by every consumer of a message stream — the normal XREADGROUP loops (OtpSmsWorker,
/// BulkSmsWorker) and the XAUTOCLAIM recovery path in QueueMaintenanceService: resolves the
/// smsMessageId field, runs it through SmsStreamMessageProcessor, and applies the resulting
/// XACK/dead-letter outcome identically regardless of which path delivered the entry.
/// </summary>
public sealed class SmsStreamEntryHandler(ILogger<SmsStreamEntryHandler> logger)
{
    /// <summary>Returns false only for RetryLater — the caller must not ack in that case.</summary>
    public async Task<bool> HandleAsync(
        IDatabase db, IServiceProvider services, string stream, string consumerGroup, string deadStream,
        StreamEntry entry, CancellationToken ct)
    {
        var idField = entry.Values.FirstOrDefault(v => v.Name == "smsMessageId");
        if (idField.Name.IsNullOrEmpty || !long.TryParse(idField.Value.ToString(), out var smsMessageId))
        {
            logger.LogWarning("{Stream} entry {EntryId} is missing a valid smsMessageId field; acking and skipping", stream, entry.Id);
            await db.StreamAcknowledgeAsync(stream, consumerGroup, entry.Id);
            return true;
        }

        SmsProcessOutcome outcome;
        try
        {
            var processor = services.GetRequiredService<SmsStreamMessageProcessor>();
            outcome = await processor.ProcessAsync(smsMessageId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process {Stream} entry {EntryId} (smsMessageId={SmsMessageId})", stream, entry.Id, smsMessageId);
            return false; // no XACK — stays pending, recovered later by the claimer.
        }

        switch (outcome)
        {
            case SmsProcessOutcome.RetryLater:
                return false; // no XACK, per doc §M5.3 step 7.

            case SmsProcessOutcome.PermanentlyFailed:
                await db.StreamAddAsync(deadStream, "smsMessageId", smsMessageId);
                await db.StreamAcknowledgeAsync(stream, consumerGroup, entry.Id);
                return true;

            default: // AlreadyHandled, Sent
                await db.StreamAcknowledgeAsync(stream, consumerGroup, entry.Id);
                return true;
        }
    }
}
