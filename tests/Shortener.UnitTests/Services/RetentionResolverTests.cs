using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shortener.Domain.Entities;
using Shortener.Infrastructure.Persistence;
using Shortener.Infrastructure.Services;

namespace Shortener.UnitTests.Services;

public sealed class RetentionResolverTests
{
    private const int ClientId = 5;
    private const int ReportId = 10;

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static RetentionPolicy Policy(
        string title, int retentionDays, int priority = 0,
        int? clientId = null, int? reportId = null, string? batchTag = null) => new()
    {
        Title = title,
        RetentionDays = retentionDays,
        GraceDays = 7,
        Priority = priority,
        IsActive = true,
        ClientId = clientId,
        ReportId = reportId,
        BatchTag = batchTag,
    };

    [Fact]
    public async Task ResolveAsync_PrefersExactBatchTagMatch_OverEverythingElse()
    {
        await using var db = CreateDb();
        db.RetentionPolicies.AddRange(
            Policy("global", 90),
            Policy("client+report", 60, clientId: ClientId, reportId: ReportId),
            Policy("batch", 15, batchTag: "BATCH-X"));
        await db.SaveChangesAsync();

        var resolver = new RetentionResolver(db);
        var resolved = await resolver.ResolveAsync(ClientId, ReportId, "BATCH-X", CancellationToken.None);

        resolved.Title.Should().Be("batch");
        resolved.RetentionDays.Should().Be(15);
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToClientAndReport_WhenNoBatchTagMatch()
    {
        await using var db = CreateDb();
        db.RetentionPolicies.AddRange(
            Policy("global", 90),
            Policy("client+report", 60, clientId: ClientId, reportId: ReportId));
        await db.SaveChangesAsync();

        var resolver = new RetentionResolver(db);

        var resolvedWithNoBatchTag = await resolver.ResolveAsync(ClientId, ReportId, null, CancellationToken.None);
        resolvedWithNoBatchTag.Title.Should().Be("client+report");

        var resolvedWithUnmatchedBatchTag = await resolver.ResolveAsync(ClientId, ReportId, "NO-SUCH-TAG", CancellationToken.None);
        resolvedWithUnmatchedBatchTag.Title.Should().Be("client+report");
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToClientOnly_WhenNoReportSpecificPolicyExists()
    {
        await using var db = CreateDb();
        db.RetentionPolicies.AddRange(
            Policy("global", 90),
            Policy("client-only", 45, clientId: ClientId, reportId: null));
        await db.SaveChangesAsync();

        var resolver = new RetentionResolver(db);
        var resolved = await resolver.ResolveAsync(ClientId, ReportId, null, CancellationToken.None);

        resolved.Title.Should().Be("client-only");
        resolved.RetentionDays.Should().Be(45);
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToGlobalPolicy_WhenNoClientSpecificPolicyExists()
    {
        await using var db = CreateDb();
        db.RetentionPolicies.Add(Policy("global", 90));
        await db.SaveChangesAsync();

        var resolver = new RetentionResolver(db);
        var resolved = await resolver.ResolveAsync(ClientId, ReportId, null, CancellationToken.None);

        resolved.Title.Should().Be("global");
        resolved.RetentionDays.Should().Be(90);
    }

    [Fact]
    public async Task ResolveAsync_WithinSameMatchLevel_HigherPriorityWins()
    {
        await using var db = CreateDb();
        db.RetentionPolicies.AddRange(
            Policy("global", 90),
            Policy("client-only-low-priority", 30, priority: 1, clientId: ClientId, reportId: null),
            Policy("client-only-high-priority", 20, priority: 5, clientId: ClientId, reportId: null));
        await db.SaveChangesAsync();

        var resolver = new RetentionResolver(db);
        var resolved = await resolver.ResolveAsync(ClientId, ReportId, null, CancellationToken.None);

        resolved.Title.Should().Be("client-only-high-priority");
    }

    [Fact]
    public async Task ResolveAsync_IgnoresInactivePolicies()
    {
        await using var db = CreateDb();
        var inactive = Policy("client-only-inactive", 30, clientId: ClientId, reportId: null);
        inactive.IsActive = false;
        db.RetentionPolicies.AddRange(Policy("global", 90), inactive);
        await db.SaveChangesAsync();

        var resolver = new RetentionResolver(db);
        var resolved = await resolver.ResolveAsync(ClientId, ReportId, null, CancellationToken.None);

        resolved.Title.Should().Be("global");
    }

    [Fact]
    public async Task ResolveAsync_ThrowsWhenNoPolicyCanBeResolvedAtAll()
    {
        await using var db = CreateDb();

        var resolver = new RetentionResolver(db);
        var act = () => resolver.ResolveAsync(ClientId, ReportId, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
