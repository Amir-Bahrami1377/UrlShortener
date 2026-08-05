using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shortener.Application.Abstractions;
using Shortener.Infrastructure.Persistence;
using Shortener.Infrastructure.ShortLinks;
using StackExchange.Redis;

namespace Shortener.IntegrationTests;

/// <summary>§M4.1 DoD — "GET /s/{code} still works with Redis down." ShortLinkModel.OnGetAsync
/// (Shortener.PublicWeb) delegates straight to IShortLinkStateResolver.ResolveAsync, which is exactly
/// what's exercised here, so this proves the same fallback the Razor Page relies on.
///
/// Rather than stopping the shared fixture's Testcontainers Redis (which every other test in this
/// collection also depends on), this points a throwaway RedisLinkCache at an endpoint nothing listens
/// on. That's a deliberate scope choice: StackExchange.Redis's automatic reconnect-after-outage timing
/// turned out to be unpredictable under this test host's load (seconds in isolation, 60s+ and still not
/// recovered when run alongside the full WebApplicationFactory/EF/SqlClient stack) — not worth making
/// this test's pass/fail depend on. A dead endpoint still drives a genuine RedisConnectionException
/// through the real StackExchange.Redis client (no mocking of ILinkCache itself), which is what
/// RedisLinkCache.GetAsync's catch block and the resolver's fallback actually need to prove.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ShortLinkStateResolverTests(ApiTestFixture fixture)
{
    // Port 1 is a reserved, well-known port that nothing binds to — connection attempts fail fast
    // instead of waiting out a full TCP connect timeout against a filtered/silently-dropping port.
    private const string DeadEndpoint = "127.0.0.1:1";

    [Fact]
    public async Task ResolveAsync_RedisUnreachable_FallsBackToSql()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (_, linkId, code) = await LinkSmsOutboxResolverTests.SeedLinkAsync(db, providerCode: null, settingsJsonMode: null);

        var resolver = BuildResolverWithDeadRedis(db);

        var state = await resolver.ResolveAsync(code, CancellationToken.None);

        state.Should().NotBeNull();
        state!.ShortLinkId.Should().Be(linkId);
        state.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_RedisUnreachable_UnknownCode_StillReturnsNull_NotAnException()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var resolver = BuildResolverWithDeadRedis(db);

        var state = await resolver.ResolveAsync("NOPE00", CancellationToken.None);

        state.Should().BeNull();
    }

    private static ShortLinkStateResolver BuildResolverWithDeadRedis(AppDbContext db)
    {
        var deadOptions = ConfigurationOptions.Parse(DeadEndpoint);
        deadOptions.AbortOnConnectFail = false;
        deadOptions.ConnectTimeout = 300;
        deadOptions.ConnectRetry = 0;
        var deadMultiplexer = ConnectionMultiplexer.Connect(deadOptions);

        var linkCache = new RedisLinkCache(
            deadMultiplexer, Options.Create(new ShortLinkOptions { BaseUrl = "https://test.local" }), NullLogger<RedisLinkCache>.Instance);

        return new ShortLinkStateResolver(db, linkCache);
    }
}
