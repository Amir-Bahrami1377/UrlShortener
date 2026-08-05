using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Shortener.Domain.Exceptions;
using Shortener.Infrastructure.FileStorage;

namespace Shortener.UnitTests.FileStorage;

public sealed class FileSystemStorageTests : IDisposable
{
    private readonly string _root;
    private readonly FileSystemStorage _sut;

    public FileSystemStorageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "shortener-tests-" + Guid.NewGuid());
        _sut = new FileSystemStorage(Options.Create(new FileStorageOptions
        {
            RootPath = _root,
            MaxFileSizeBytes = 3 * 1024 * 1024,
        }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_ThenOpenReadAsync_RoundTripsContentExactly()
    {
        var content = "hello persian sandbox: سلام"u8.ToArray();
        using var source = new MemoryStream(content);

        var result = await _sut.SaveAsync(source, ".pdf", CancellationToken.None);

        result.SizeBytes.Should().Be(content.Length);

        using var read = await _sut.OpenReadAsync(result.StorageKey, CancellationToken.None);
        using var buffer = new MemoryStream();
        await read.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(content);
    }

    [Fact]
    public async Task SaveAsync_ComputesCorrectSha256_MatchingIndependentComputation()
    {
        var content = new byte[50_000];
        Random.Shared.NextBytes(content);
        using var source = new MemoryStream(content);

        var result = await _sut.SaveAsync(source, ".bin", CancellationToken.None);

        var expected = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        result.Sha256.Should().Be(expected);
    }

    [Fact]
    public async Task SaveAsync_StorageKey_ShardsByDateAndGuidPrefix()
    {
        using var source = new MemoryStream("x"u8.ToArray());

        var result = await _sut.SaveAsync(source, ".pdf", CancellationToken.None);

        var now = DateTime.UtcNow;
        result.StorageKey.Should().StartWith($"{now:yyyy}/{now:MM}/{now:dd}/");
        result.StorageKey.Should().EndWith($"{result.FileGuid}.pdf");
    }

    [Fact]
    public async Task SaveAsync_RejectsFileLargerThanMax_AndThrowsFileTooLargeException()
    {
        var oversized = new byte[3 * 1024 * 1024 + 1];
        using var source = new MemoryStream(oversized);

        var act = () => _sut.SaveAsync(source, ".pdf", CancellationToken.None);

        await act.Should().ThrowAsync<FileTooLargeException>();
    }

    [Fact]
    public async Task SaveAsync_WhenTooLarge_DoesNotLeaveAnyTempOrFinalFileBehind()
    {
        var oversized = new byte[3 * 1024 * 1024 + 1];
        using var source = new MemoryStream(oversized);

        try
        {
            await _sut.SaveAsync(source, ".pdf", CancellationToken.None);
        }
        catch (FileTooLargeException)
        {
            // expected
        }

        if (Directory.Exists(_root))
        {
            Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile_AndExistsAsyncReflectsRemoval()
    {
        using var source = new MemoryStream("content"u8.ToArray());
        var result = await _sut.SaveAsync(source, ".pdf", CancellationToken.None);

        (await _sut.ExistsAsync(result.StorageKey, CancellationToken.None)).Should().BeTrue();

        var deleted = await _sut.DeleteAsync(result.StorageKey, CancellationToken.None);

        deleted.Should().BeTrue();
        (await _sut.ExistsAsync(result.StorageKey, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task CleanupTempFilesAsync_DeletesOnlyTempFilesOlderThanMaxAge()
    {
        var dir = Path.Combine(_root, "2026", "01", "01", "ab");
        Directory.CreateDirectory(dir);

        var oldTmp = Path.Combine(dir, "old.tmp");
        var freshTmp = Path.Combine(dir, "fresh.tmp");
        await File.WriteAllTextAsync(oldTmp, "x");
        await File.WriteAllTextAsync(freshTmp, "x");
        File.SetLastWriteTimeUtc(oldTmp, DateTime.UtcNow.AddHours(-10));
        File.SetLastWriteTimeUtc(freshTmp, DateTime.UtcNow);

        var deletedCount = await _sut.CleanupTempFilesAsync(TimeSpan.FromHours(6), CancellationToken.None);

        deletedCount.Should().Be(1);
        File.Exists(oldTmp).Should().BeFalse();
        File.Exists(freshTmp).Should().BeTrue();
    }

    [Fact]
    public async Task EnumerateStoredFiles_ReturnsSavedFiles_ButExcludesTempFiles()
    {
        using var first = new MemoryStream("a"u8.ToArray());
        using var second = new MemoryStream("bb"u8.ToArray());
        var saved1 = await _sut.SaveAsync(first, ".pdf", CancellationToken.None);
        var saved2 = await _sut.SaveAsync(second, ".pdf", CancellationToken.None);

        var strayTmpDir = Path.Combine(_root, "2026", "01", "01", "cd");
        Directory.CreateDirectory(strayTmpDir);
        await File.WriteAllTextAsync(Path.Combine(strayTmpDir, "stray.tmp"), "x");

        var onDisk = _sut.EnumerateStoredFiles().ToList();

        onDisk.Select(f => f.StorageKey).Should().BeEquivalentTo([saved1.StorageKey, saved2.StorageKey]);
        onDisk.Single(f => f.StorageKey == saved2.StorageKey).SizeBytes.Should().Be(2);
    }

    [Fact]
    public void CleanupEmptyDirectories_RemovesEmptyShardDayMonth_ForANonCurrentMonth()
    {
        var emptyShardDir = Path.Combine(_root, "2020", "05", "17", "ab");
        Directory.CreateDirectory(emptyShardDir);

        var removed = _sut.CleanupEmptyDirectories();

        removed.Should().Be(3); // shard, day, month — never the year level.
        Directory.Exists(Path.Combine(_root, "2020", "05")).Should().BeFalse();
        Directory.Exists(Path.Combine(_root, "2020")).Should().BeTrue();
    }

    [Fact]
    public void CleanupEmptyDirectories_NeverRemovesTheCurrentMonthDirectory_EvenIfEmpty()
    {
        var now = DateTime.UtcNow;
        var currentMonthShardDir = Path.Combine(_root, now.ToString("yyyy"), now.ToString("MM"), now.ToString("dd"), "ab");
        Directory.CreateDirectory(currentMonthShardDir);

        _sut.CleanupEmptyDirectories();

        Directory.Exists(Path.Combine(_root, now.ToString("yyyy"), now.ToString("MM"))).Should().BeTrue();
        Directory.Exists(currentMonthShardDir).Should().BeFalse(); // the empty shard/day below it still get cleaned up.
    }

    [Fact]
    public async Task CleanupEmptyDirectories_DoesNotRemoveDirectoriesThatStillContainFiles()
    {
        using var source = new MemoryStream("keep-me"u8.ToArray());
        var saved = await _sut.SaveAsync(source, ".pdf", CancellationToken.None);
        var nonCurrentEmptyDir = Path.Combine(_root, "2020", "05", "17", "cd");
        Directory.CreateDirectory(nonCurrentEmptyDir);

        _sut.CleanupEmptyDirectories();

        (await _sut.ExistsAsync(saved.StorageKey, CancellationToken.None)).Should().BeTrue();
        Directory.Exists(nonCurrentEmptyDir).Should().BeFalse();
    }
}
