using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shortener.Application.Contracts;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;
using Shortener.Infrastructure.Queueing;

namespace Shortener.Worker.Workers;

/// <summary>
/// §M5.2. Claims Pending OutboxMessages via an atomic `UPDATE ... OUTPUT` (READPAST/UPDLOCK, so
/// multiple Worker instances never claim the same row) and hands each one to LinkSmsOutboxResolver,
/// which does the actual template/account resolution and XADD. One deviation from the doc's literal
/// text: the recovery job re-publishes stuck rows whose "corresponding SmsMessage is still Queued" —
/// but the Outbox payload only carries a ShortLinkId, not a SmsMessageId, so there's nothing to check
/// "still Queued" against. The doc's own intent (recover rows where the actual publish never
/// completed) is captured here as: Published + stale ProcessedAt + no SmsMessage exists yet for that
/// link → reset to Pending.
/// </summary>
public sealed class OutboxPublisherService(
    IServiceScopeFactory scopeFactory, ILogger<OutboxPublisherService> logger) : BackgroundService
{
    private const int BatchSize = 500;
    private const int MaxTryCount = 5;
    private static readonly TimeSpan PublishInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RecoveryStaleAfter = TimeSpan.FromMinutes(10);

    private DateTime _lastRecoveryRun = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishBatchAsync(stoppingToken);

                if (DateTime.UtcNow - _lastRecoveryRun >= RecoveryInterval)
                {
                    await RecoverStuckRowsAsync(stoppingToken);
                    _lastRecoveryRun = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OutboxPublisherService tick failed");
            }

            await Task.Delay(PublishInterval, stoppingToken);
        }
    }

    private async Task PublishBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var resolver = scope.ServiceProvider.GetRequiredService<LinkSmsOutboxResolver>();

        var claimed = await db.Database.SqlQuery<ClaimedOutboxRow>($"""
            UPDATE TOP ({BatchSize}) OutboxMessages WITH (READPAST, UPDLOCK)
            SET Status = {(byte)OutboxStatus.Published}, ProcessedAt = SYSUTCDATETIME()
            OUTPUT inserted.Id, inserted.Type, inserted.PayloadJson, inserted.TryCount
            WHERE Status = {(byte)OutboxStatus.Pending};
            """).ToListAsync(ct);

        if (claimed.Count == 0)
        {
            return;
        }

        foreach (var row in claimed)
        {
            ct.ThrowIfCancellationRequested();

            if (row.Type != OutboxMessageTypes.LinkSms)
            {
                await MarkFailedAsync(db, row, $"Unknown OutboxMessage.Type '{row.Type}'", ct);
                continue;
            }

            LinkSmsResolveOutcome outcome;
            string? error;
            try
            {
                (outcome, error) = await resolver.ResolveAsync(row.PayloadJson, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A single row's unexpected exception must not abort the rest of this batch, nor
                // leave the row stuck at Published forever with nothing to retry it except the
                // 5-minute recovery sweep looping on the same failure indefinitely — route it
                // through the normal retry/dead-letter accounting like any other failure.
                logger.LogError(ex, "OutboxMessage {Id} threw while resolving", row.Id);
                outcome = LinkSmsResolveOutcome.Failed;
                error = $"ERR_UNEXPECTED_EXCEPTION: {ex.Message}";
            }

            if (outcome == LinkSmsResolveOutcome.Done)
            {
                continue;
            }

            var nextTryCount = row.TryCount + 1;
            if (nextTryCount >= MaxTryCount)
            {
                logger.LogError("OutboxMessage {Id} permanently failed after {TryCount} attempts: {Error}", row.Id, nextTryCount, error);
                await db.OutboxMessages.Where(o => o.Id == row.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(o => o.Status, OutboxStatus.Failed)
                        .SetProperty(o => o.TryCount, nextTryCount)
                        .SetProperty(o => o.Error, error), ct);
            }
            else
            {
                logger.LogWarning("OutboxMessage {Id} publish failed (attempt {TryCount}): {Error}", row.Id, nextTryCount, error);
                await db.OutboxMessages.Where(o => o.Id == row.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(o => o.Status, OutboxStatus.Pending)
                        .SetProperty(o => o.TryCount, nextTryCount)
                        .SetProperty(o => o.Error, error), ct);
            }
        }
    }

    private static Task MarkFailedAsync(AppDbContext db, ClaimedOutboxRow row, string error, CancellationToken ct) =>
        db.OutboxMessages.Where(o => o.Id == row.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OutboxStatus.Failed)
                .SetProperty(o => o.TryCount, row.TryCount + 1)
                .SetProperty(o => o.Error, error), ct);

    private async Task RecoverStuckRowsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cutoff = DateTime.UtcNow - RecoveryStaleAfter;
        var stuckCandidates = await db.OutboxMessages
            .Where(o => o.Status == OutboxStatus.Published && o.ProcessedAt != null && o.ProcessedAt < cutoff && o.Type == OutboxMessageTypes.LinkSms)
            .Select(o => new { o.Id, o.PayloadJson })
            .ToListAsync(ct);

        var recovered = 0;
        foreach (var candidate in stuckCandidates)
        {
            LinkSmsPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<LinkSmsPayload>(candidate.PayloadJson);
            }
            catch (JsonException)
            {
                continue;
            }

            if (payload is null)
            {
                continue;
            }

            var hasSmsMessage = await db.SmsMessages.AnyAsync(
                m => m.ShortLinkId == payload.ShortLinkId && m.MessageType == SmsMessageType.DownloadLink, ct);
            if (hasSmsMessage)
            {
                continue;
            }

            await db.OutboxMessages.Where(o => o.Id == candidate.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OutboxStatus.Pending), ct);
            recovered++;
        }

        if (recovered > 0)
        {
            logger.LogWarning("Outbox recovery: reset {Count} stuck LinkSms rows back to Pending", recovered);
        }
    }

    private sealed record ClaimedOutboxRow(Guid Id, string Type, string PayloadJson, int TryCount);
}
