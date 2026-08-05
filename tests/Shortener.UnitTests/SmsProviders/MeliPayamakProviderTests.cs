using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shortener.Application.Abstractions;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.SmsProviders;

namespace Shortener.UnitTests.SmsProviders;

public sealed class MeliPayamakProviderTests
{
    private static readonly SmsSendRequest Request = new("09121234567", "hello", null, null, SmsMessageType.DownloadLink);

    [Fact]
    public async Task SendAsync_RetStatusOne_ReturnsSuccessWithRecIdAsProviderMessageId()
    {
        var json = """{"Value":"98765","RetStatus":1,"StrRetStatus":"Ok"}""";
        var factory = MockHttp.CreateFactory("sms-melipayamak", "https://rest.payamak-panel.com", HttpStatusCode.OK, json);
        var provider = new MeliPayamakProvider(factory, NullLogger<MeliPayamakProvider>.Instance);

        var result = await provider.SendAsync(
            new SmsAccountConfig(null, "user", "pass", "1000", null, null), Request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.ProviderMessageId.Should().Be("98765");
    }

    [Fact]
    public async Task SendAsync_NonOneRetStatus_ReturnsNonRetryableFailure()
    {
        var json = """{"Value":null,"RetStatus":0,"StrRetStatus":"InvalidCredentials"}""";
        var factory = MockHttp.CreateFactory("sms-melipayamak", "https://rest.payamak-panel.com", HttpStatusCode.OK, json);
        var provider = new MeliPayamakProvider(factory, NullLogger<MeliPayamakProvider>.Instance);

        var result = await provider.SendAsync(
            new SmsAccountConfig(null, "user", "wrong-pass", "1000", null, null), Request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().BeFalse();
        result.ErrorMessage.Should().Be("InvalidCredentials");
    }

    [Fact]
    public async Task SendAsync_MissingCredentials_FailsWithoutMakingAnHttpCall()
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var provider = new MeliPayamakProvider(factory.Object, NullLogger<MeliPayamakProvider>.Instance);

        var result = await provider.SendAsync(
            new SmsAccountConfig(null, null, null, "1000", null, null), Request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_PatternCode_PostsToTheBaseNumberEndpoint()
    {
        HttpRequestMessage? captured = null;
        var json = """{"Value":"1","RetStatus":1,"StrRetStatus":"Ok"}""";
        var factory = MockHttp.CreateFactory(
            "sms-melipayamak", "https://rest.payamak-panel.com", HttpStatusCode.OK, json, req => captured = req);
        var provider = new MeliPayamakProvider(factory, NullLogger<MeliPayamakProvider>.Instance);

        await provider.SendAsync(
            new SmsAccountConfig(null, "user", "pass", "1000", null, null),
            Request with { PatternCode = "42", PatternTokens = new Dictionary<string, string> { ["otp"] = "123456" } },
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.RequestUri!.AbsolutePath.Should().Be("/api/SendSMS/SendByBaseNumber");
    }
}
