using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;
using Shortener.Infrastructure.Queueing;

namespace Shortener.IntegrationTests;

/// <summary>§M5.6 DoD — "delivery status query correctly records Delivered," plus the 24h Undelivered
/// fallback. TestControlledSmsProvider.GetStatusAsync always reports Delivered.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class DeliveryStatusPollerTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task PollOnceAsync_SentMessage_BecomesDelivered_AndRecordsHistory()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var poller = scope.ServiceProvider.GetRequiredService<DeliveryStatusPoller>();

        var (clientId, linkId, _) = await LinkSmsOutboxResolverTests.SeedLinkAsync(db, TestControlledSmsProvider.Code, settingsJsonMode: null);
        var account = await db.SmsAccounts.FirstAsync(a => a.ClientId == clientId);

        var message = new SmsMessage
        {
            ShortLinkId = linkId,
            SmsAccountId = account.Id,
            MessageType = SmsMessageType.DownloadLink,
            PhoneNumber = "09121234567",
            Body = "test body",
            Status = SmsStatus.Sent,
            ProviderMessageId = $"dlr-test-{Guid.NewGuid():N}",
            SentAt = DateTime.UtcNow.AddMinutes(-5),
        };
        db.SmsMessages.Add(message);
        await db.SaveChangesAsync();

        var updated = await poller.PollOnceAsync(CancellationToken.None);

        updated.Should().BeGreaterThanOrEqualTo(1);
        var reloaded = await db.SmsMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        reloaded.Status.Should().Be(SmsStatus.Delivered);
        reloaded.DeliveredAt.Should().NotBeNull();
        reloaded.StatusCheckedAt.Should().NotBeNull();

        var history = await db.SmsStatusHistories.Where(h => h.SmsMessageId == message.Id).ToListAsync();
        history.Should().ContainSingle(h => h.Status == SmsStatus.Delivered);
    }

    [Fact]
    public async Task PollOnceAsync_MessageSentOverADayAgoWithNoConfirmation_BecomesUndelivered()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var poller = scope.ServiceProvider.GetRequiredService<DeliveryStatusPoller>();

        var (clientId, linkId, _) = await LinkSmsOutboxResolverTests.SeedLinkAsync(db, TestControlledSmsProvider.Code, settingsJsonMode: null);
        var account = await db.SmsAccounts.FirstAsync(a => a.ClientId == clientId);

        var message = new SmsMessage
        {
            ShortLinkId = linkId,
            SmsAccountId = account.Id,
            MessageType = SmsMessageType.DownloadLink,
            PhoneNumber = "09121234567",
            Body = "test body",
            Status = SmsStatus.Sent,
            ProviderMessageId = $"dlr-stale-{Guid.NewGuid():N}",
            SentAt = DateTime.UtcNow.AddHours(-25),
        };
        db.SmsMessages.Add(message);
        await db.SaveChangesAsync();

        await poller.PollOnceAsync(CancellationToken.None);

        var reloaded = await db.SmsMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        reloaded.Status.Should().Be(SmsStatus.Undelivered);
    }
}
