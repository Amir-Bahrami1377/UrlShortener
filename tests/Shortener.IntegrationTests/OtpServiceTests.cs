using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shortener.Application.Abstractions;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;

namespace Shortener.IntegrationTests;

/// <summary>
/// Exercises IOtpService/IDownloadTokenService directly (§M4.3/M4.4/M4.5's core logic) against the
/// real dev SQL Server + Redis, resolved from ApiTestFixture's own DI container (Shortener.Api also
/// calls AddInfrastructure, so every service under test is already registered there — no need for a
/// second WebApplicationFactory, which would collide on the implicit top-level-statement Program type).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class OtpServiceTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task RequestAsync_ForUnknownCode_ReturnsNotFound()
    {
        using var scope = fixture.Services.CreateScope();
        var otpService = scope.ServiceProvider.GetRequiredService<IOtpService>();

        var result = await otpService.RequestAsync("NOSUCH", "203.0.113.1", CancellationToken.None);

        result.Outcome.Should().Be(OtpRequestOutcome.NotFound);
    }

    [Fact]
    public async Task RequestAsync_HappyPath_QueuesAnOtpSmsMessage()
    {
        var code = await CreateTestLinkAsync();
        using var scope = fixture.Services.CreateScope();
        var otpService = scope.ServiceProvider.GetRequiredService<IOtpService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var result = await otpService.RequestAsync(code, "203.0.113.2", CancellationToken.None);

        result.Outcome.Should().Be(OtpRequestOutcome.Sent);
        result.TtlSeconds.Should().Be(120);
        result.CooldownSecondsRemaining.Should().Be(90);

        var link = await db.ShortLinks.FirstAsync(l => l.Code == code);
        var smsMessage = await db.SmsMessages
            .Where(m => m.ShortLinkId == link.Id && m.MessageType == SmsMessageType.Otp)
            .OrderByDescending(m => m.CreatedAt)
            .FirstAsync();

        smsMessage.Status.Should().Be(SmsStatus.Queued);
        smsMessage.Body.Should().MatchRegex(@"\d{6}");
        link.OtpRequestCount.Should().Be(1);
    }

    [Fact]
    public async Task RequestAsync_CalledAgainImmediately_ReturnsCooldown()
    {
        var code = await CreateTestLinkAsync();
        using var scope = fixture.Services.CreateScope();
        var otpService = scope.ServiceProvider.GetRequiredService<IOtpService>();

        var first = await otpService.RequestAsync(code, "203.0.113.3", CancellationToken.None);
        first.Outcome.Should().Be(OtpRequestOutcome.Sent);

        var second = await otpService.RequestAsync(code, "203.0.113.3", CancellationToken.None);

        second.Outcome.Should().Be(OtpRequestOutcome.Cooldown);
        second.CooldownSecondsRemaining.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RequestAsync_AFourthTimeWithinAnHour_ReturnsLimitPerLink()
    {
        var code = await CreateTestLinkAsync();
        using var scope = fixture.Services.CreateScope();
        var otpService = scope.ServiceProvider.GetRequiredService<IOtpService>();
        var redis = scope.ServiceProvider.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>();

        // Requesting 3 times normally requires waiting out the 90s cooldown between each; instead we
        // drive the same Redis counter the service itself increments, then make one real call to
        // observe the limit trip — this tests the actual limit-check code path without a real 4.5-minute wait.
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ShortLinks.FirstAsync(l => l.Code == code);
        await redis.GetDatabase().StringSetAsync($"otp:cnt:{code}", 3, TimeSpan.FromHours(1));

        var result = await otpService.RequestAsync(code, "203.0.113.4", CancellationToken.None);

        result.Outcome.Should().Be(OtpRequestOutcome.LimitPerLink);
        _ = link;
    }

    [Fact]
    public async Task VerifyAsync_FiveWrongAttempts_LocksTheLinkForFifteenMinutes()
    {
        var code = await CreateTestLinkAsync();
        using var scope = fixture.Services.CreateScope();
        var otpService = scope.ServiceProvider.GetRequiredService<IOtpService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await otpService.RequestAsync(code, "203.0.113.5", CancellationToken.None);

        OtpVerifyResult last = null!;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            last = await otpService.VerifyAsync(code, "000000", "203.0.113.5", "test-agent", CancellationToken.None);
        }

        last.Outcome.Should().Be(OtpVerifyOutcome.TooManyAttempts);

        var link = await db.ShortLinks.FirstAsync(l => l.Code == code);
        link.LockedUntil.Should().NotBeNull();
        link.LockedUntil!.Value.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));

        var whileLocked = await otpService.VerifyAsync(code, "111111", "203.0.113.5", "test-agent", CancellationToken.None);
        whileLocked.Outcome.Should().Be(OtpVerifyOutcome.Locked);
    }

    [Fact]
    public async Task VerifyAsync_WithCorrectOtp_SucceedsAndTheDownloadTokenIsSingleUse()
    {
        var code = await CreateTestLinkAsync();
        using var scope = fixture.Services.CreateScope();
        var otpService = scope.ServiceProvider.GetRequiredService<IOtpService>();
        var tokenService = scope.ServiceProvider.GetRequiredService<IDownloadTokenService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await otpService.RequestAsync(code, "203.0.113.6", CancellationToken.None);

        var link = await db.ShortLinks.FirstAsync(l => l.Code == code);
        var smsMessage = await db.SmsMessages
            .Where(m => m.ShortLinkId == link.Id && m.MessageType == SmsMessageType.Otp)
            .OrderByDescending(m => m.CreatedAt)
            .FirstAsync();
        var otp = Regex.Match(smsMessage.Body, @"\d{6}").Value;

        var result = await otpService.VerifyAsync(code, otp, "203.0.113.6", "test-agent", CancellationToken.None);

        result.Outcome.Should().Be(OtpVerifyOutcome.Success);
        result.DownloadToken.Should().NotBeNullOrEmpty();

        var payload = await tokenService.ConsumeAsync(result.DownloadToken!, CancellationToken.None);
        payload.Should().NotBeNull();
        payload!.ShortLinkId.Should().Be(link.Id);

        var reused = await tokenService.ConsumeAsync(result.DownloadToken!, CancellationToken.None);
        reused.Should().BeNull();

        var reloaded = await db.ShortLinks.AsNoTracking().FirstAsync(l => l.Id == link.Id);
        reloaded.FirstVerifiedAt.Should().NotBeNull();
        reloaded.FailedVerifyCount.Should().Be(0);
    }

    [Fact]
    public async Task VerifyAsync_WithWrongOtpOnce_ReportsRemainingAttempts()
    {
        var code = await CreateTestLinkAsync();
        using var scope = fixture.Services.CreateScope();
        var otpService = scope.ServiceProvider.GetRequiredService<IOtpService>();

        await otpService.RequestAsync(code, "203.0.113.7", CancellationToken.None);
        var result = await otpService.VerifyAsync(code, "000000", "203.0.113.7", "test-agent", CancellationToken.None);

        result.Outcome.Should().Be(OtpVerifyOutcome.Invalid);
        result.RemainingAttempts.Should().Be(4);
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
            ReportName = "OTP Test Report",
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
