using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Shortener.Application.Abstractions;
using Shortener.Domain.Exceptions;

namespace Shortener.Infrastructure.FileStorage;

public sealed class FileSystemStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    private const int BufferSize = 81920;
    private readonly FileStorageOptions _options = options.Value;

    public async Task<StoredFileResult> SaveAsync(Stream source, string extension, CancellationToken ct)
    {
        var fileGuid = Guid.CreateVersion7();
        var shard = Convert.ToHexString(SHA256.HashData(fileGuid.ToByteArray()))[..2].ToLowerInvariant();

        var now = DateTime.UtcNow;
        var relativeDir = $"{now:yyyy}/{now:MM}/{now:dd}/{shard}";
        var absoluteDir = Path.Combine(_options.RootPath, now.ToString("yyyy"), now.ToString("MM"), now.ToString("dd"), shard);
        Directory.CreateDirectory(absoluteDir);

        var fileName = $"{fileGuid}{extension}";
        var tmpPath = Path.Combine(absoluteDir, $"{fileGuid}.tmp");
        var finalPath = Path.Combine(absoluteDir, fileName);

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        long totalBytes = 0;

        try
        {
            await using (var fileStream = new FileStream(
                             tmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             bufferSize: BufferSize, useAsync: true))
            {
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer, ct)) > 0)
                {
                    totalBytes += bytesRead;
                    if (totalBytes > _options.MaxFileSizeBytes)
                    {
                        throw new FileTooLargeException(totalBytes, _options.MaxFileSizeBytes);
                    }

                    hasher.AppendData(buffer, 0, bytesRead);
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                }
            }
        }
        catch
        {
            TryDeleteFile(tmpPath);
            throw;
        }

        File.Move(tmpPath, finalPath);

        var sha256 = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        var storageKey = $"{relativeDir}/{fileName}";
        return new StoredFileResult(fileGuid, storageKey, totalBytes, sha256);
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct)
    {
        Stream stream = new FileStream(
            ResolvePath(storageKey), FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: BufferSize, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task<bool> DeleteAsync(string storageKey, CancellationToken ct)
    {
        return Task.FromResult(TryDeleteFile(ResolvePath(storageKey)));
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken ct)
    {
        return Task.FromResult(File.Exists(ResolvePath(storageKey)));
    }

    public Task<int> CleanupTempFilesAsync(TimeSpan maxAge, CancellationToken ct)
    {
        if (!Directory.Exists(_options.RootPath))
        {
            return Task.FromResult(0);
        }

        var cutoff = DateTime.UtcNow - maxAge;
        var deleted = 0;

        foreach (var tmpFile in Directory.EnumerateFiles(_options.RootPath, "*.tmp", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            if (File.GetLastWriteTimeUtc(tmpFile) < cutoff && TryDeleteFile(tmpFile))
            {
                deleted++;
            }
        }

        return Task.FromResult(deleted);
    }

    public DiskSpaceInfo GetDiskSpace()
    {
        var root = Path.GetPathRoot(Path.GetFullPath(_options.RootPath))!;
        var drive = new DriveInfo(root);
        var total = drive.TotalSize;
        var free = drive.AvailableFreeSpace;
        var usedPercent = total == 0 ? 0 : (total - free) * 100.0 / total;
        return new DiskSpaceInfo(total, free, usedPercent);
    }

    public IEnumerable<StoredFileOnDisk> EnumerateStoredFiles()
    {
        if (!Directory.Exists(_options.RootPath))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(_options.RootPath, "*", SearchOption.AllDirectories))
        {
            if (path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(_options.RootPath, path).Replace(Path.DirectorySeparatorChar, '/');
            var info = new FileInfo(path);
            yield return new StoredFileOnDisk(relative, info.LastWriteTimeUtc, info.Length);
        }
    }

    public int CleanupEmptyDirectories()
    {
        if (!Directory.Exists(_options.RootPath))
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        var currentMonthRelative = $"{now:yyyy}{Path.DirectorySeparatorChar}{now:MM}";

        // Depth relative to RootPath: yyyy=1, yyyy/MM=2, yyyy/MM/dd=3, yyyy/MM/dd/shard=4.
        // Only shard/day/month are ever removed (doc §M7.3); the year level is left untouched.
        var candidates = Directory.EnumerateDirectories(_options.RootPath, "*", SearchOption.AllDirectories)
            .Select(dir => (Path: dir, Relative: Path.GetRelativePath(_options.RootPath, dir)))
            .Select(x => (x.Path, x.Relative, Depth: x.Relative.Count(c => c == Path.DirectorySeparatorChar) + 1))
            .Where(x => x.Depth is 2 or 3 or 4)
            .OrderByDescending(x => x.Depth) // shard, then day, then month — deepest first so parents empty out correctly.
            .ToList();

        var removed = 0;
        foreach (var (path, relative, depth) in candidates)
        {
            if (depth == 2 && relative.Equals(currentMonthRelative, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                {
                    Directory.Delete(path);
                    removed++;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }

    private string ResolvePath(string storageKey) =>
        Path.Combine(_options.RootPath, storageKey.Replace('/', Path.DirectorySeparatorChar));

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
