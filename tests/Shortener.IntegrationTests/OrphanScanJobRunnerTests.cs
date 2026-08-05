using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shortener.Application.Abstractions;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.FileStorage;
using Shortener.Infrastructure.Persistence;
using Shortener.Infrastructure.Retention;

namespace Shortener.IntegrationTests;

/// <summary>§M7.4 DoD, against the real dev SQL Server + disk.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class OrphanScanJobRunnerTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task RunAsync_DiskFileOlderThanADayWithNoStoredFilesRow_IsDeletedAndLoggedAsOrphan()
    {
        using var scope = fixture.Services.CreateScope();
        var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var runner = scope.ServiceProvider.GetRequiredService<IOrphanScanJobRunner>();
        var rootPath = scope.ServiceProvider.GetRequiredService<IOptions<FileStorageOptions>>().Value.RootPath;

        using var source = new MemoryStream("orphan file, no db row"u8.ToArray());
        var saved = await fileStorage.SaveAsync(source, ".pdf", CancellationToken.None);
        BackdateFile(rootPath, saved.StorageKey, TimeSpan.FromHours(30));

        var summary = await runner.RunAsync(CancellationToken.None);

        summary.OrphanFilesDeleted.Should().BeGreaterThanOrEqualTo(1);
        (await fileStorage.ExistsAsync(saved.StorageKey, CancellationToken.None)).Should().BeFalse();

        var log = await db.FileDeletionLogs.OrderByDescending(l => l.Id).FirstAsync(l => l.StorageKey == saved.StorageKey);
        log.Reason.Should().Be(FileDeletionReason.Orphan);
        log.StoredFileId.Should().BeNull();
        log.PhysicalDeleteOk.Should().BeTrue();
        log.TriggeredBy.Should().Be("system:orphan-scan");
    }

    [Fact]
    public async Task RunAsync_DiskFileYoungerThanADay_IsLeftAlone()
    {
        using var scope = fixture.Services.CreateScope();
        var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var runner = scope.ServiceProvider.GetRequiredService<IOrphanScanJobRunner>();

        using var source = new MemoryStream("fresh orphan, still in grace"u8.ToArray());
        var saved = await fileStorage.SaveAsync(source, ".pdf", CancellationToken.None);

        await runner.RunAsync(CancellationToken.None);

        (await fileStorage.ExistsAsync(saved.StorageKey, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_ActiveStoredFileMissingOnDisk_IsNeverDeleted_ButFlaggedInAuditLog()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var runner = scope.ServiceProvider.GetRequiredService<IOrphanScanJobRunner>();

        var client = await db.Clients.FirstAsync();
        var missingFile = new StoredFile
        {
            ClientId = client.Id,
            StorageKey = $"2020/01/01/zz/{Guid.NewGuid()}.pdf",
            OriginalFileName = "missing.pdf",
            ContentType = "application/pdf",
            Extension = ".pdf",
            SizeBytes = 123,
            Sha256 = new string('0', 64),
            Status = FileStatus.Active,
            StoredAt = DateTime.UtcNow,
            FileExpiresAt = DateTime.UtcNow.AddDays(90),
        };
        db.StoredFiles.Add(missingFile);
        await db.SaveChangesAsync(CancellationToken.None);

        var summary = await runner.RunAsync(CancellationToken.None);

        summary.MissingOnDiskFlagged.Should().BeGreaterThanOrEqualTo(1);

        var reloaded = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == missingFile.Id);
        reloaded.Status.Should().Be(FileStatus.Active); // never touched — could be a transient mount problem.

        var audit = await db.AuditLogs.OrderByDescending(a => a.Id).FirstAsync(
            a => a.Action == OrphanScanJobRunner.MissingOnDiskAuditAction && a.EntityId == missingFile.Id.ToString());
        audit.UserId.Should().Be("system:orphan-scan");
    }

    private static void BackdateFile(string rootPath, string storageKey, TimeSpan age)
    {
        var absolutePath = Path.Combine(rootPath, storageKey.Replace('/', Path.DirectorySeparatorChar));
        File.SetLastWriteTimeUtc(absolutePath, DateTime.UtcNow - age);
    }
}
