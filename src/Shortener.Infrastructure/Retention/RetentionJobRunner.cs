using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shortener.Application.Abstractions;
using Shortener.Application.Services;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;

namespace Shortener.Infrastructure.Retention;

/// <summary>
/// §M7.1-M7.3. Two deviations from the doc's literal single-statement SQL, both reasoned:
///
/// 1. GraceDays: the doc's ExpirationJob UPDATE uses one bulk @graceDays parameter, but GraceDays
///    is actually a per-RetentionPolicy value (already editable per client/report/batchTag via the
///    M2 RetentionPoliciesController). Using a single global value would silently ignore those
///    per-policy overrides, so this groups expiring files by their resolved RetentionPolicyId and
///    issues one ExecuteUpdateAsync per distinct policy instead of one global statement.
///
/// 2. FileStatus.PendingDelete: the doc's ExpirationJob SQL sets Status=1 (Expired) with a future
///    PendingDeleteAt, but the FileDeletionJob SQL then filters WHERE Status=2 (PendingDelete) — a
///    state nothing in the doc's M7.1 ever produces. The doc's own M6 storage report and M7 DoD both
///    also treat PendingDelete as a real, independently-countable state. The consistent reading is a
///    missing "graduation" step: once an Expired file's grace period elapses, it moves to
///    PendingDelete, becoming eligible for physical deletion. That step is added here as 1b.
/// </summary>
public sealed class RetentionJobRunner(
    AppDbContext db,
    IFileStorage fileStorage,
    ILinkCache linkCache,
    IOptions<RetentionJobOptions> options,
    ILogger<RetentionJobRunner> logger) : IRetentionJobRunner
{
    private readonly RetentionJobOptions _options = options.Value;

    public async Task<RetentionRunSummary> RunAsync(CancellationToken ct)
    {
        var expired = await RunExpirationAsync(ct);
        var (linksDeactivated, graduated) = (expired.LinksDeactivated, expired.Graduated);

        var (deleted, failed, stoppedEarly) = await RunPhysicalDeletionAsync(ct);

        var foldersRemoved = 0;
        if (!stoppedEarly)
        {
            foldersRemoved = fileStorage.CleanupEmptyDirectories();
        }

        logger.LogInformation(
            "Retention job: expired={Expired} linksDeactivated={LinksDeactivated} graduated={Graduated} " +
            "deleted={Deleted} deleteFailures={Failed} foldersRemoved={FoldersRemoved} stoppedEarly={StoppedEarly}",
            expired.Expired, linksDeactivated, graduated, deleted, failed, foldersRemoved, stoppedEarly);

        return new RetentionRunSummary(
            expired.Expired, linksDeactivated, graduated, deleted, failed, foldersRemoved, stoppedEarly);
    }

    private async Task<(int Expired, int LinksDeactivated, int Graduated)> RunExpirationAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Step 1: Active + past FileExpiresAt -> Expired, PendingDeleteAt = now + that file's GraceDays.
        var affectedPolicyIds = await db.StoredFiles
            .Where(f => f.Status == FileStatus.Active && f.FileExpiresAt < now)
            .Select(f => f.RetentionPolicyId)
            .Distinct()
            .ToListAsync(ct);

        var graceDaysByPolicy = affectedPolicyIds.Any(id => id != null)
            ? await db.RetentionPolicies
                .Where(p => affectedPolicyIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.GraceDays, ct)
            : new Dictionary<int, int>();

        var expired = 0;
        foreach (var policyId in affectedPolicyIds)
        {
            var graceDays = policyId.HasValue && graceDaysByPolicy.TryGetValue(policyId.Value, out var g)
                ? g
                : _options.DefaultGraceDays;
            var pendingDeleteAt = now.AddDays(graceDays);

            expired += await db.StoredFiles
                .Where(f => f.Status == FileStatus.Active && f.FileExpiresAt < now && f.RetentionPolicyId == policyId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(f => f.Status, FileStatus.Expired)
                    .SetProperty(f => f.PendingDeleteAt, pendingDeleteAt), ct);
        }

        // Step 2: deactivate links whose file just expired, and evict their Redis cache entries.
        var codesToEvict = await db.ShortLinks
            .Where(l => l.IsActive && l.StoredFile!.Status == FileStatus.Expired)
            .Select(l => l.Code)
            .ToListAsync(ct);

        var linksDeactivated = codesToEvict.Count == 0
            ? 0
            : await db.ShortLinks
                .Where(l => l.IsActive && l.StoredFile!.Status == FileStatus.Expired)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.IsActive, false), ct);

        foreach (var chunk in codesToEvict.Chunk(200))
        {
            await Task.WhenAll(chunk.Select(code => linkCache.InvalidateAsync(code, ct)));
        }

        // Step 1b: graduate grace-elapsed Expired files into PendingDelete (see class doc, deviation 2).
        var graduated = await db.StoredFiles
            .Where(f => f.Status == FileStatus.Expired && f.PendingDeleteAt != null && f.PendingDeleteAt < now)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Status, FileStatus.PendingDelete), ct);

        return (expired, linksDeactivated, graduated);
    }

    private async Task<(int Deleted, int Failed, bool StoppedEarly)> RunPhysicalDeletionAsync(CancellationToken ct)
    {
        var deleted = 0;
        var failed = 0;

        while (true)
        {
            if (!JobWindow.IsWithin(PersianDateHelper.Now(), _options))
            {
                return (deleted, failed, true);
            }

            var now = DateTime.UtcNow;
            var batch = await db.StoredFiles
                .Where(f => f.Status == FileStatus.PendingDelete && f.PendingDeleteAt < now)
                .OrderBy(f => f.Id)
                .Take(_options.DeleteBatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0)
            {
                return (deleted, failed, false);
            }

            foreach (var file in batch)
            {
                var ok = await fileStorage.DeleteAsync(file.StorageKey, ct);

                db.FileDeletionLogs.Add(new FileDeletionLog
                {
                    StoredFileId = file.Id,
                    StorageKey = file.StorageKey,
                    SizeBytes = file.SizeBytes,
                    Reason = FileDeletionReason.RetentionPolicy,
                    TriggeredBy = "system:retention",
                    PhysicalDeleteOk = ok,
                    ErrorMessage = ok ? null : "حذف فیزیکی فایل ناموفق بود (قفل یا عدم دسترسی)؛ در اجرای بعدی دوباره تلاش می‌شود.",
                });

                if (ok)
                {
                    file.Status = FileStatus.Deleted;
                    file.DeletedAt = DateTime.UtcNow;
                    file.DeletedBy = "system:retention";
                    deleted++;
                }
                else
                {
                    // Status left unchanged so the next run's WHERE clause picks it up again.
                    failed++;
                }
            }

            await db.SaveChangesAsync(ct);

            if (batch.Count < _options.DeleteBatchSize)
            {
                return (deleted, failed, false);
            }

            await Task.Delay(_options.DeleteBatchDelayMs, ct);
        }
    }
}
