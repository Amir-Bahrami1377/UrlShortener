using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;
using Shortener.Infrastructure.Queueing;
using StackExchange.Redis;

namespace Shortener.IntegrationTests;

/// <summary>§M5.4 / §M8.4b DoD — "XAUTOCLAIM recovery after killing the Worker," previously only ever
/// verified by manually killing a live Worker process during M5. Each test uses its own throwaway
/// stream/group name so it can't collide with the real sms:bulk/sms:otp traffic other tests in this
/// shared-fixture collection generate, and a directly-constructed StreamClaimService (rather than one
/// resolved via DI) so ClaimIdleMinutes/MaxDeliveryAttempts can be overridden per test the same way
/// RetentionJobRunnerTests overrides its job window — real Redis and real SQL Server either way, just
/// deterministic timing instead of sleeping past a multi-minute default threshold.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class StreamClaimServiceTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task ClaimEligibleAsync_EntryPendingPastIdleThreshold_IsReclaimedUnderTheNewConsumer()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var redisDb = redis.GetDatabase();

        var smsMessageId = await SeedQueuedSmsMessageAsync(db);
        var (stream, group) = await CreateStreamWithGroupAsync(redisDb);
        await redisDb.StreamAddAsync(stream, "smsMessageId", smsMessageId);

        // "worker-a" picks it up (delivery count -> 1) and then, per the scenario, crashes before XACK.
        await redisDb.StreamReadGroupAsync(stream, group, "worker-a", ">", count: 1);

        var claimService = BuildClaimService(db, redis, claimIdleMinutes: 0, maxDeliveryAttempts: 5);

        var result = await claimService.ClaimEligibleAsync(stream, group, "worker-b-claimer", CancellationToken.None);

        result.DeadLettered.Should().Be(0);
        result.ToReprocess.Should().ContainSingle(e => e.Values.Any(v => v.Name == "smsMessageId" && v.Value == smsMessageId.ToString()));

        var pending = await redisDb.StreamPendingMessagesAsync(stream, group, 10, RedisValue.Null, "-", "+");
        pending.Should().ContainSingle(p => p.ConsumerName == "worker-b-claimer");
    }

    [Fact]
    public async Task ClaimEligibleAsync_EntryStillWithinIdleThreshold_IsLeftPendingUnderTheOriginalConsumer()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var redisDb = redis.GetDatabase();

        var smsMessageId = await SeedQueuedSmsMessageAsync(db);
        var (stream, group) = await CreateStreamWithGroupAsync(redisDb);
        await redisDb.StreamAddAsync(stream, "smsMessageId", smsMessageId);
        await redisDb.StreamReadGroupAsync(stream, group, "worker-a", ">", count: 1);

        // A generous idle threshold: the entry above is only milliseconds old, so it must not be eligible.
        var claimService = BuildClaimService(db, redis, claimIdleMinutes: 30, maxDeliveryAttempts: 5);

        var result = await claimService.ClaimEligibleAsync(stream, group, "worker-b-claimer", CancellationToken.None);

        result.DeadLettered.Should().Be(0);
        result.ToReprocess.Should().BeEmpty();

        var pending = await redisDb.StreamPendingMessagesAsync(stream, group, 10, RedisValue.Null, "-", "+");
        pending.Should().ContainSingle(p => p.ConsumerName == "worker-a");
    }

    [Fact]
    public async Task ClaimEligibleAsync_EntryOverMaxDeliveryAttempts_IsDeadLetteredAndMarkedFailed_NotReprocessed()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var redisDb = redis.GetDatabase();

        var smsMessageId = await SeedQueuedSmsMessageAsync(db);
        var (stream, group) = await CreateStreamWithGroupAsync(redisDb);
        var deadStream = $"test:dead:{Guid.NewGuid():N}";
        await redisDb.StreamAddAsync(stream, "smsMessageId", smsMessageId);

        // Delivery count starts at 1 on the first read, then +1 per XCLAIM — three crashed consumers
        // in a row (2 XCLAIMs on top of the initial read) pushes it past a MaxDeliveryAttempts of 2.
        var entries = await redisDb.StreamReadGroupAsync(stream, group, "worker-a", ">", count: 1);
        var entryId = entries[0].Id;
        await redisDb.StreamClaimAsync(stream, group, "worker-b", minIdleTimeInMs: 0, [entryId]);
        await redisDb.StreamClaimAsync(stream, group, "worker-c", minIdleTimeInMs: 0, [entryId]);

        var claimService = BuildClaimService(db, redis, claimIdleMinutes: 0, maxDeliveryAttempts: 2, deadStream);

        var result = await claimService.ClaimEligibleAsync(stream, group, "worker-d-claimer", CancellationToken.None);

        result.DeadLettered.Should().Be(1);
        result.ToReprocess.Should().BeEmpty();

        var reloaded = await db.SmsMessages.AsNoTracking().FirstAsync(m => m.Id == smsMessageId);
        reloaded.Status.Should().Be(SmsStatus.Failed);
        reloaded.LastError.Should().Contain("MaxDeliveryAttempts");

        var deadEntries = await redisDb.StreamRangeAsync(deadStream, "-", "+");
        deadEntries.Should().ContainSingle(e => e.Values.Any(v => v.Name == "smsMessageId" && v.Value == smsMessageId.ToString()));

        // Dead-lettered entries are acked off the original stream, not left pending forever.
        var pending = await redisDb.StreamPendingMessagesAsync(stream, group, 10, RedisValue.Null, "-", "+");
        pending.Should().BeEmpty();
    }

    private static StreamClaimService BuildClaimService(
        AppDbContext db, IConnectionMultiplexer redis, int claimIdleMinutes, int maxDeliveryAttempts, string? deadStream = null) =>
        new(redis, db,
            Options.Create(new QueueOptions
            {
                ClaimIdleMinutes = claimIdleMinutes,
                MaxDeliveryAttempts = maxDeliveryAttempts,
                DeadStream = deadStream ?? $"test:dead:{Guid.NewGuid():N}",
            }),
            NullLogger<StreamClaimService>.Instance);

    private static async Task<(string Stream, string Group)> CreateStreamWithGroupAsync(IDatabase redisDb)
    {
        var stream = $"test:claim:{Guid.NewGuid():N}";
        const string group = "test-group";
        await redisDb.StreamCreateConsumerGroupAsync(stream, group, "0-0", createStream: true);
        return (stream, group);
    }

    private static async Task<long> SeedQueuedSmsMessageAsync(AppDbContext db)
    {
        var (_, linkId, _) = await LinkSmsOutboxResolverTests.SeedLinkAsync(db, providerCode: null, settingsJsonMode: null);
        var link = await db.ShortLinks.FirstAsync(l => l.Id == linkId);

        var fakeProvider = await db.SmsProviders.SingleAsync(p => p.Code == "fake");
        var account = new SmsAccount
        {
            ClientId = link.ClientId,
            SmsProviderId = fakeProvider.Id,
            Title = "Claim Test Account",
            Purpose = SmsAccountPurpose.Bulk,
            RatePerMinute = 3000,
            IsDefault = true,
            IsActive = true,
        };
        db.SmsAccounts.Add(account);
        await db.SaveChangesAsync();

        var message = new SmsMessage
        {
            ShortLinkId = linkId,
            SmsAccountId = account.Id,
            MessageType = SmsMessageType.DownloadLink,
            PhoneNumber = "09121234567",
            Body = "claim test body",
            Status = SmsStatus.Queued,
        };
        db.SmsMessages.Add(message);
        await db.SaveChangesAsync();

        return message.Id;
    }
}
