using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shortener.Application.Abstractions;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.FileStorage;
using Shortener.Infrastructure.Persistence;

namespace Shortener.Infrastructure.Retention;

/// <summary>
/// §M7.4. Direction 1 (disk→DB) deletes files with no matching StoredFiles row — this needed
/// FileDeletionLog.StoredFileId to become nullable (see migration AddNullableStoredFileIdToFileDeletionLog),
/// since a genuine orphan by definition has no StoredFiles row to reference. Direction 2 (DB→disk)
/// never deletes, per the doc — it only logs + flags, since the file's absence could be a transient
/// mount problem rather than real loss.
/// </summary>
public sealed class OrphanScanJobRunner(
    AppDbContext db,
    IFileStorage fileStorage,
    IOptions<FileStorageOptions> fileStorageOptions,
    ILogger<OrphanScanJobRunner> logger) : IOrphanScanJobRunner
{
    public const string MissingOnDiskAuditAction = "OrphanScan:MissingOnDisk";
    private static readonly TimeSpan OrphanMinAge = TimeSpan.FromHours(24);

    public async Task<OrphanScanSummary> RunAsync(CancellationToken ct)
    {
        var tempDeleted = await fileStorage.CleanupTempFilesAsync(
            TimeSpan.FromHours(fileStorageOptions.Value.TempFileMaxAgeHours), ct);

        var orphanDeleted = await RunDiskToDbAsync(ct);
        var missingCount = await RunDbToDiskAsync(ct);

        logger.LogInformation(
            "Orphan scan: orphanFilesDeleted={OrphanDeleted} missingOnDiskFlagged={Missing} tempFilesDeleted={TempDeleted}",
            orphanDeleted, missingCount, tempDeleted);

        return new OrphanScanSummary(orphanDeleted, missingCount, tempDeleted);
    }

    private async Task<int> RunDiskToDbAsync(CancellationToken ct)
    {
        var knownKeys = (await db.StoredFiles.Select(f => f.StorageKey).ToListAsync(ct)).ToHashSet();
        var cutoff = DateTime.UtcNow - OrphanMinAge;
        var deleted = 0;

        foreach (var onDisk in fileStorage.EnumerateStoredFiles())
        {
            ct.ThrowIfCancellationRequested();

            if (onDisk.LastWriteUtc >= cutoff || knownKeys.Contains(onDisk.StorageKey))
            {
                continue;
            }

            var ok = await fileStorage.DeleteAsync(onDisk.StorageKey, ct);
            db.FileDeletionLogs.Add(new FileDeletionLog
            {
                StoredFileId = null,
                StorageKey = onDisk.StorageKey,
                SizeBytes = onDisk.SizeBytes,
                Reason = FileDeletionReason.Orphan,
                TriggeredBy = "system:orphan-scan",
                PhysicalDeleteOk = ok,
                ErrorMessage = ok ? null : "حذف فایل یتیم ناموفق بود.",
            });

            if (ok)
            {
                deleted++;
                logger.LogWarning("Orphan scan: deleted disk file with no StoredFiles row: {StorageKey}", onDisk.StorageKey);
            }
        }

        if (deleted > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return deleted;
    }

    private async Task<int> RunDbToDiskAsync(CancellationToken ct)
    {
        var activeFiles = await db.StoredFiles
            .Where(f => f.Status == FileStatus.Active)
            .Select(f => new { f.Id, f.StorageKey })
            .ToListAsync(ct);

        var missing = 0;
        foreach (var file in activeFiles)
        {
            ct.ThrowIfCancellationRequested();

            if (await fileStorage.ExistsAsync(file.StorageKey, ct))
            {
                continue;
            }

            missing++;
            logger.LogError(
                "Orphan scan: StoredFile {StoredFileId} is Active but missing on disk at {StorageKey}",
                file.Id, file.StorageKey);

            db.AuditLogs.Add(new AuditLog
            {
                UserId = "system:orphan-scan",
                EntityName = nameof(StoredFile),
                EntityId = file.Id.ToString(CultureInfo.InvariantCulture),
                Action = MissingOnDiskAuditAction,
                NewValueJson = JsonSerializer.Serialize(new { file.StorageKey }),
            });
        }

        if (missing > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return missing;
    }
}
