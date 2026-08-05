using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shortener.Application.Abstractions;

namespace Shortener.IntegrationTests;

/// <summary>§M7.6 DoD — "with two Worker instances running, the job only runs once." Each call
/// below issues an independent `SET NX EX` against the real dev Redis, so this exercises the same
/// race multiple concurrent Worker processes would create, without actually starting a second host.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class RedisDistributedLockTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task TryAcquireAsync_WhileHeld_BlocksASecondAcquire_ThenSucceedsAfterRelease()
    {
        using var scope = fixture.Services.CreateScope();
        var lockService = scope.ServiceProvider.GetRequiredService<IDistributedLock>();
        var lockName = $"test-lock-{Guid.NewGuid():N}";

        var first = await lockService.TryAcquireAsync(lockName, TimeSpan.FromSeconds(30), CancellationToken.None);
        first.Should().NotBeNull();

        var second = await lockService.TryAcquireAsync(lockName, TimeSpan.FromSeconds(30), CancellationToken.None);
        second.Should().BeNull();

        await first!.DisposeAsync();

        var third = await lockService.TryAcquireAsync(lockName, TimeSpan.FromSeconds(30), CancellationToken.None);
        third.Should().NotBeNull();
        await third!.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_FiveConcurrentAttempts_OnlyOneEverAcquires()
    {
        using var scope = fixture.Services.CreateScope();
        var lockService = scope.ServiceProvider.GetRequiredService<IDistributedLock>();
        var lockName = $"test-lock-{Guid.NewGuid():N}";

        var results = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => lockService.TryAcquireAsync(lockName, TimeSpan.FromSeconds(30), CancellationToken.None)));

        results.Count(handle => handle is not null).Should().Be(1);

        foreach (var handle in results.Where(handle => handle is not null))
        {
            await handle!.DisposeAsync();
        }
    }
}
