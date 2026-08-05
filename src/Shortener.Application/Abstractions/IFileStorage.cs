namespace Shortener.Application.Abstractions;

public interface IFileStorage
{
    Task<StoredFileResult> SaveAsync(Stream source, string extension, CancellationToken ct);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct);

    Task<bool> DeleteAsync(string storageKey, CancellationToken ct);

    Task<bool> ExistsAsync(string storageKey, CancellationToken ct);

    Task<int> CleanupTempFilesAsync(TimeSpan maxAge, CancellationToken ct);

    DiskSpaceInfo GetDiskSpace();

    /// <summary>§M7.4 direction 1 (disk→DB) — every non-.tmp file actually on disk, for orphan comparison
    /// against StoredFiles. Lazily enumerated: the caller decides how much of the tree to materialize.</summary>
    IEnumerable<StoredFileOnDisk> EnumerateStoredFiles();

    /// <summary>§M7.3 — removes empty shard/day/month directories bottom-up. Never removes the current
    /// month's directory, even if it is (temporarily) empty. Returns the number of directories removed.</summary>
    int CleanupEmptyDirectories();
}

public sealed record StoredFileResult(Guid FileGuid, string StorageKey, long SizeBytes, string Sha256);

public sealed record DiskSpaceInfo(long TotalBytes, long FreeBytes, double UsedPercent);

public sealed record StoredFileOnDisk(string StorageKey, DateTime LastWriteUtc, long SizeBytes);
