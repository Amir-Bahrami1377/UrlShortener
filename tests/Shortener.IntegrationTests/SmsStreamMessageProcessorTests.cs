using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shortener.Application.Abstractions;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;
using Shortener.Infrastructure.Queueing;

namespace Shortener.IntegrationTests;

/// <summary>§M5.3 steps 1-8 DoD — idempotency, retry backoff, and dead-lettering, exercised directly
/// against the real dev SQL Server via TestControlledSmsProvider (no live gateway needed).</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SmsStreamMessageProcessorTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task ProcessAsync_QueuedMessage_SendsSuccessfully_AndRecordsHistory()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<SmsStreamMessageProcessor>();

        var message = await SeedQueuedMessageAsync(db, settingsJsonMode: null);

        var outcome = await processor.ProcessAsync(message.Id, CancellationToken.None);

        outcome.Should().Be(SmsProcessOutcome.Sent);

        var reloaded = await db.SmsMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        reloaded.Status.Should().Be(SmsStatus.Sent);
        reloaded.ProviderMessageId.Should().NotBeNullOrEmpty();
        reloaded.SentAt.Should().NotBeNull();

        var history = await db.SmsStatusHistories.Where(h => h.SmsMessageId == message.Id).ToListAsync();
        history.Should().ContainSingle(h => h.Status == SmsStatus.Sent);
    }

    [Fact]
    public async Task ProcessAsync_MessageWithPatternCode_PassesPatternCodeAndTokensToProvider()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<SmsStreamMessageProcessor>();

        var message = await SeedQueuedMessageAsync(db, settingsJsonMode: null);
        message.PatternCode = "12345";
        message.PatternTokensJson = """{"otp":"654321","otpMinutes":"2"}""";
        await db.SaveChangesAsync();

        var outcome = await processor.ProcessAsync(message.Id, CancellationToken.None);

        outcome.Should().Be(SmsProcessOutcome.Sent);

        var provider = (TestControlledSmsProvider)scope.ServiceProvider
            .GetRequiredKeyedService<ISmsProvider>(TestControlledSmsProvider.Code);
        provider.LastRequest.Should().NotBeNull();
        provider.LastRequest!.PatternCode.Should().Be("12345");
        provider.LastRequest.PatternTokens.Should().ContainKey("otp").WhoseValue.Should().Be("654321");
        provider.LastRequest.PatternTokens.Should().ContainKey("otpMinutes").WhoseValue.Should().Be("2");
    }

    [Fact]
    public async Task ProcessAsync_MessageAlreadySent_IsANoOp()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<SmsStreamMessageProcessor>();

        var message = await SeedQueuedMessageAsync(db, settingsJsonMode: null);
        message.Status = SmsStatus.Sent;
        message.ProviderMessageId = "already-sent-id";
        await db.SaveChangesAsync();

        var outcome = await processor.ProcessAsync(message.Id, CancellationToken.None);

        outcome.Should().Be(SmsProcessOutcome.AlreadyHandled);
        var reloaded = await db.SmsMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        reloaded.ProviderMessageId.Should().Be("already-sent-id"); // untouched, not overwritten by a second send
    }

    [Fact]
    public async Task ProcessAsync_RetryableFailure_RequeuesWithBackoff_AndDoesNotDeadLetter()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<SmsStreamMessageProcessor>();

        var message = await SeedQueuedMessageAsync(db, TestControlledSmsProvider.Retryable);

        var outcome = await processor.ProcessAsync(message.Id, CancellationToken.None);

        outcome.Should().Be(SmsProcessOutcome.RetryLater);

        var reloaded = await db.SmsMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        reloaded.Status.Should().Be(SmsStatus.Queued);
        reloaded.TryCount.Should().Be(1);
        reloaded.NextRetryAt.Should().NotBeNull();
        // 2^1 = 2 minutes out; a generous tolerance since this only needs to confirm the right
        // ballpark (not accidentally seconds/hours), and CI can run many tests in parallel against
        // the same SQL Server container.
        reloaded.NextRetryAt!.Value.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(2), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ProcessAsync_PermanentFailure_DeadLettersImmediately_RegardlessOfTryCount()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<SmsStreamMessageProcessor>();

        var message = await SeedQueuedMessageAsync(db, TestControlledSmsProvider.Permanent);

        var outcome = await processor.ProcessAsync(message.Id, CancellationToken.None);

        outcome.Should().Be(SmsProcessOutcome.PermanentlyFailed);

        var reloaded = await db.SmsMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        reloaded.Status.Should().Be(SmsStatus.Failed);
        reloaded.LastError.Should().NotBeNullOrEmpty();

        var history = await db.SmsStatusHistories.Where(h => h.SmsMessageId == message.Id).ToListAsync();
        history.Should().ContainSingle(h => h.Status == SmsStatus.Failed);
    }

    [Fact]
    public async Task ProcessAsync_RetryableFailure_AtTheDeliveryAttemptCap_DeadLettersInsteadOfRetrying()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<SmsStreamMessageProcessor>();

        // QueueOptions.MaxDeliveryAttempts is 5 in every appsettings.json in this repo; TryCount=4
        // means this is the 5th attempt, which must be the last one regardless of IsRetryable.
        var message = await SeedQueuedMessageAsync(db, TestControlledSmsProvider.Retryable);
        message.TryCount = 4;
        await db.SaveChangesAsync();

        var outcome = await processor.ProcessAsync(message.Id, CancellationToken.None);

        outcome.Should().Be(SmsProcessOutcome.PermanentlyFailed);
        var reloaded = await db.SmsMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        reloaded.Status.Should().Be(SmsStatus.Failed);
    }

    private static async Task<SmsMessage> SeedQueuedMessageAsync(AppDbContext db, string? settingsJsonMode)
    {
        var (clientId, linkId, _) = await LinkSmsOutboxResolverTests.SeedLinkAsync(db, TestControlledSmsProvider.Code, settingsJsonMode);
        var account = await db.SmsAccounts.FirstAsync(a => a.ClientId == clientId);

        var message = new SmsMessage
        {
            ShortLinkId = linkId,
            SmsAccountId = account.Id,
            MessageType = SmsMessageType.DownloadLink,
            PhoneNumber = "09121234567",
            Body = "test body",
            Status = SmsStatus.Queued,
        };
        db.SmsMessages.Add(message);
        await db.SaveChangesAsync();
        return message;
    }
}
