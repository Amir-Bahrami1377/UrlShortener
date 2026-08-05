using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;
using Shortener.Infrastructure.Queueing;
using StackExchange.Redis;

namespace Shortener.IntegrationTests;

/// <summary>§M5.2 DoD — resolving a LinkSms outbox intent into a real, correctly-rendered SmsMessage.</summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class LinkSmsOutboxResolverTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task ResolveAsync_FreshLink_CreatesQueuedSmsMessageWithRenderedBody_AndXaddsToBulkStream()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var resolver = scope.ServiceProvider.GetRequiredService<LinkSmsOutboxResolver>();

        var (_, linkId, code) = await SeedLinkAsync(db, TestControlledSmsProvider.Code, settingsJsonMode: null);
        var link = await db.ShortLinks.FirstAsync(l => l.Id == linkId);

        var (outcome, error) = await resolver.ResolveAsync($$"""{"ShortLinkId":{{linkId}}}""", CancellationToken.None);

        outcome.Should().Be(LinkSmsResolveOutcome.Done);
        error.Should().BeNull();

        var smsMessage = await db.SmsMessages.SingleAsync(m => m.ShortLinkId == linkId && m.MessageType == SmsMessageType.DownloadLink);
        smsMessage.Status.Should().Be(SmsStatus.Queued);
        smsMessage.Body.Should().Contain(code);
        smsMessage.Body.Should().Contain(link.ReportName);
        smsMessage.Body.Should().Contain($"/s/{code}");

        var db2 = redis.GetDatabase();
        var entries = await db2.StreamRangeAsync("sms:bulk", "-", "+");
        entries.Should().Contain(e => e.Values.Any(v => v.Name == "smsMessageId" && v.Value == smsMessage.Id.ToString()));
    }

    [Fact]
    public async Task ResolveAsync_CalledTwiceForTheSameLink_DoesNotCreateADuplicateSmsMessage()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var resolver = scope.ServiceProvider.GetRequiredService<LinkSmsOutboxResolver>();

        var (_, linkId, _) = await SeedLinkAsync(db, TestControlledSmsProvider.Code, settingsJsonMode: null);
        var payload = $$"""{"ShortLinkId":{{linkId}}}""";

        var first = await resolver.ResolveAsync(payload, CancellationToken.None);
        var second = await resolver.ResolveAsync(payload, CancellationToken.None);

        first.Outcome.Should().Be(LinkSmsResolveOutcome.Done);
        second.Outcome.Should().Be(LinkSmsResolveOutcome.Done);

        var count = await db.SmsMessages.CountAsync(m => m.ShortLinkId == linkId && m.MessageType == SmsMessageType.DownloadLink);
        count.Should().Be(1);
    }

    [Fact]
    public async Task ResolveAsync_ClientWithNoBulkAccountConfigured_ReturnsFailedWithoutCreatingAnything()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var resolver = scope.ServiceProvider.GetRequiredService<LinkSmsOutboxResolver>();

        var (_, linkId, _) = await SeedLinkAsync(db, providerCode: null, settingsJsonMode: null);

        var (outcome, error) = await resolver.ResolveAsync($$"""{"ShortLinkId":{{linkId}}}""", CancellationToken.None);

        outcome.Should().Be(LinkSmsResolveOutcome.Failed);
        error.Should().Be("ERR_BULK_SMS_NOT_CONFIGURED");
        (await db.SmsMessages.AnyAsync(m => m.ShortLinkId == linkId)).Should().BeFalse();
    }

    /// <summary>Creates a dedicated Client (+ optionally a Bulk SmsAccount/DownloadLink template) and
    /// a ShortLink pointing at a synthetic StoredFile — no real upload needed since the resolver
    /// never touches the physical file.</summary>
    internal static async Task<(int ClientId, long ShortLinkId, string Code)> SeedLinkAsync(
        AppDbContext db, string? providerCode, string? settingsJsonMode)
    {
        var client = new Client { Name = "M5 Test Client", Code = $"M5T-{Guid.NewGuid():N}"[..20], IsActive = true };
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        if (providerCode is not null)
        {
            var provider = await db.SmsProviders.FirstOrDefaultAsync(p => p.Code == providerCode);
            if (provider is null)
            {
                provider = new SmsProvider { Code = providerCode, Name = providerCode, IsActive = true };
                db.SmsProviders.Add(provider);
                await db.SaveChangesAsync();
            }

            db.SmsAccounts.Add(new SmsAccount
            {
                ClientId = client.Id,
                SmsProviderId = provider.Id,
                Title = "Test Bulk Account",
                Purpose = SmsAccountPurpose.Bulk,
                RatePerMinute = 3000,
                IsDefault = true,
                IsActive = true,
                SettingsJson = settingsJsonMode,
            });
            db.MessageTemplates.Add(new MessageTemplate
            {
                ClientId = client.Id,
                ReportId = null,
                TemplateType = TemplateType.DownloadLink,
                Title = "Test DownloadLink template",
                Body = "دانلود {reportName}: {shortUrl} (کد {code}, تا {expireDate})",
                IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        var storedFile = new StoredFile
        {
            FileGuid = Guid.CreateVersion7(),
            ClientId = client.Id,
            StorageKey = $"test/{Guid.NewGuid()}.pdf",
            OriginalFileName = "test.pdf",
            ContentType = "application/pdf",
            Extension = ".pdf",
            SizeBytes = 10,
            Sha256 = new string('0', 64),
            Status = FileStatus.Active,
            StoredAt = DateTime.UtcNow,
            FileExpiresAt = DateTime.UtcNow.AddDays(90),
        };
        db.StoredFiles.Add(storedFile);
        await db.SaveChangesAsync();

        var code = $"T{Guid.NewGuid():N}"[..6];
        var link = new ShortLink
        {
            Code = code,
            RequestId = Guid.CreateVersion7(),
            StoredFileId = storedFile.Id,
            ClientId = client.Id,
            Shop = "S1",
            Shod = "S2",
            Radif = "R1",
            ReportId = 1,
            ReportName = "Test Report " + Guid.NewGuid().ToString("N")[..6],
            PhoneNumber = "09121234567",
            ExpiresAt = DateTime.UtcNow.AddDays(90),
            IsActive = true,
        };
        db.ShortLinks.Add(link);
        await db.SaveChangesAsync();

        return (client.Id, link.Id, code);
    }
}
