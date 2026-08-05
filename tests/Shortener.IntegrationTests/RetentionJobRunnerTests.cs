using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shortener.Application.Abstractions;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;
using Shortener.Infrastructure.Retention;

namespace Shortener.IntegrationTests;

/// <summary>§M7.1-M7.3 DoD, exercised against the real dev SQL Server/Redis/disk via a real
/// upload so there is a genuine file to expire, deactivate, and physically delete.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class RetentionJobRunnerTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task RunAsync_ActiveFileWithPastExpiry_BecomesExpired_DeactivatesItsLink_AndEvictsTheCache()
    {
        var code = await CreateTestLinkAsync();
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var linkCache = scope.ServiceProvider.GetRequiredService<ILinkCache>();
        var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var runner = scope.ServiceProvider.GetRequiredService<IRetentionJobRunner>();

        var link = await db.ShortLinks.FirstAsync(l => l.Code == code);
        var storedFileId = link.StoredFileId;
        var storageKey = (await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == storedFileId)).StorageKey;

        // Upload itself already warms link:{code} (§M3.2), so overwrite with a known value here —
        // the point isn't proving the cache is populated, it's proving the job evicts it.
        await linkCache.SetAsync(code, new LinkCacheEntry(
            link.Id, storedFileId, "Test Report", link.PhoneNumber, link.ExpiresAt, true,
            FileStatus.Active, storageKey, "application/pdf", ".pdf", link.ClientId), CancellationToken.None);

        await db.StoredFiles.Where(f => f.Id == storedFileId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.FileExpiresAt, DateTime.UtcNow.AddDays(-1)), CancellationToken.None);

        var summary = await runner.RunAsync(CancellationToken.None);

        summary.Expired.Should().BeGreaterThanOrEqualTo(1);
        summary.LinksDeactivated.Should().BeGreaterThanOrEqualTo(1);

        var reloadedFile = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == storedFileId);
        reloadedFile.Status.Should().Be(FileStatus.Expired);
        reloadedFile.PendingDeleteAt.Should().NotBeNull();
        reloadedFile.PendingDeleteAt!.Value.Should().BeAfter(DateTime.UtcNow); // grace period, not elapsed yet.

        var reloadedLink = await db.ShortLinks.AsNoTracking().FirstAsync(l => l.Id == link.Id);
        reloadedLink.IsActive.Should().BeFalse();

        (await linkCache.GetAsync(code, CancellationToken.None)).Should().BeNull();

        // Grace period hasn't elapsed, so the file must still be physically present.
        (await fileStorage.ExistsAsync(storageKey, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_ExpiredFileWhoseGraceHasElapsed_IsPhysicallyDeleted_ButDbRowsSurvive()
    {
        var code = await CreateTestLinkAsync();
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        var link = await db.ShortLinks.FirstAsync(l => l.Code == code);
        var storedFileId = link.StoredFileId;
        var storageKey = (await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == storedFileId)).StorageKey;

        (await fileStorage.ExistsAsync(storageKey, CancellationToken.None)).Should().BeTrue();

        // Simulate a file whose grace period already elapsed on a previous night's run.
        await db.StoredFiles.Where(f => f.Id == storedFileId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.Status, FileStatus.Expired)
                .SetProperty(f => f.FileExpiresAt, DateTime.UtcNow.AddDays(-10))
                .SetProperty(f => f.PendingDeleteAt, DateTime.UtcNow.AddMinutes(-1)), CancellationToken.None);

        // The real config's window is 1-5 AM Tehran (doc §M7.1), which real test-run wall-clock
        // time won't reliably fall inside — build the runner directly with an always-open window so
        // physical deletion is deterministic regardless of when this test actually executes.
        var runner = BuildRunner(scope, alwaysOpenWindow: true);
        var summary = await runner.RunAsync(CancellationToken.None);

        summary.GraduatedToPendingDelete.Should().BeGreaterThanOrEqualTo(1);
        summary.PhysicallyDeleted.Should().BeGreaterThanOrEqualTo(1);
        summary.PhysicalDeleteFailures.Should().Be(0);
        summary.StoppedEarlyDueToWindow.Should().BeFalse();

        var reloadedFile = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == storedFileId);
        reloadedFile.Status.Should().Be(FileStatus.Deleted);
        reloadedFile.DeletedAt.Should().NotBeNull();
        reloadedFile.DeletedBy.Should().Be("system:retention");

        (await fileStorage.ExistsAsync(storageKey, CancellationToken.None)).Should().BeFalse();

        var log = await db.FileDeletionLogs.OrderByDescending(l => l.Id).FirstAsync(l => l.StoredFileId == storedFileId);
        log.Reason.Should().Be(FileDeletionReason.RetentionPolicy);
        log.PhysicalDeleteOk.Should().BeTrue();
        log.TriggeredBy.Should().Be("system:retention");

        // The DB rows themselves are never deleted — only Status changes (doc §M7.2 point d).
        (await db.StoredFiles.AnyAsync(f => f.Id == storedFileId)).Should().BeTrue();
        (await db.ShortLinks.AnyAsync(l => l.Id == link.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_OutsideTheLowLoadWindow_StopsBeforePhysicallyDeletingAnything()
    {
        var code = await CreateTestLinkAsync();
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        var link = await db.ShortLinks.FirstAsync(l => l.Code == code);
        var storedFileId = link.StoredFileId;
        var storageKey = (await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == storedFileId)).StorageKey;

        await db.StoredFiles.Where(f => f.Id == storedFileId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.Status, FileStatus.Expired)
                .SetProperty(f => f.PendingDeleteAt, DateTime.UtcNow.AddMinutes(-1)), CancellationToken.None);

        // Window that can never contain "now" (start == end), per doc §M7.2 point 4: outside the
        // window, the job must stop and let the next run's window pick up where it left off.
        var runner = BuildRunner(scope, alwaysOpenWindow: false);
        var summary = await runner.RunAsync(CancellationToken.None);

        summary.StoppedEarlyDueToWindow.Should().BeTrue();
        summary.PhysicallyDeleted.Should().Be(0);
        (await fileStorage.ExistsAsync(storageKey, CancellationToken.None)).Should().BeTrue();

        // The graduation step (Expired -> PendingDelete) isn't window-gated, so it still ran —
        // only the physical-deletion loop respects the window.
        var reloadedFile = await db.StoredFiles.AsNoTracking().FirstAsync(f => f.Id == storedFileId);
        reloadedFile.Status.Should().Be(FileStatus.PendingDelete);
    }

    private static RetentionJobRunner BuildRunner(IServiceScope scope, bool alwaysOpenWindow)
    {
        var windowOptions = alwaysOpenWindow
            ? new RetentionJobOptions { JobWindowStartHour = 0, JobWindowEndHour = 24, DeleteBatchDelayMs = 0 }
            : new RetentionJobOptions { JobWindowStartHour = 0, JobWindowEndHour = 0, DeleteBatchDelayMs = 0 };

        return new RetentionJobRunner(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<IFileStorage>(),
            scope.ServiceProvider.GetRequiredService<ILinkCache>(),
            Options.Create(windowOptions),
            scope.ServiceProvider.GetRequiredService<ILogger<RetentionJobRunner>>());
    }

    private async Task<string> CreateTestLinkAsync()
    {
        var client = await fixture.CreateAuthenticatedClientAsync();

        var content = new MultipartFormDataContent();
        var metadata = new
        {
            Shop = Guid.NewGuid().ToString("N")[..10],
            Shod = Guid.NewGuid().ToString("N")[..10],
            Radif = Guid.NewGuid().ToString("N")[..10],
            ReportId = 1,
            ReportName = "Retention Test Report",
            PhoneNumber = "09121234567",
        };
        var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        content.Add(new StringContent(metadataJson, Encoding.UTF8, "application/json"), "metadata");

        var header = "%PDF-1.4\n"u8.ToArray();
        var fileContent = new ByteArrayContent(header);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "test.pdf");

        var response = await client.PostAsync("/api/v1/links", content);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("code").GetString()!;
    }
}
