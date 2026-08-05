using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shortener.Application.Abstractions;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.SmsProviders;

namespace Shortener.UnitTests.SmsProviders;

public sealed class KavenegarProviderTests
{
    private static readonly SmsSendRequest Request = new("09121234567", "hello", null, null, SmsMessageType.DownloadLink);

    [Fact]
    public async Task SendAsync_SuccessfulResponse_ReturnsSuccessWithProviderMessageIdAndCost()
    {
        var json = """{"return":{"status":200,"message":"OK"},"entries":[{"messageid":123456,"status":1,"cost":150}]}""";
        var factory = MockHttp.CreateFactory("sms-kavenegar", "https://api.kavenegar.com", HttpStatusCode.OK, json);
        var provider = new KavenegarProvider(factory, NullLogger<KavenegarProvider>.Instance);

        var result = await provider.SendAsync(
            new SmsAccountConfig("test-api-key", null, null, "1000", null, null), Request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.ProviderMessageId.Should().Be("123456");
        result.Cost.Should().Be(150);
        result.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_AuthenticationError_ReturnsNonRetryableFailure()
    {
        var json = """{"return":{"status":401,"message":"Invalid API Key"}}""";
        var factory = MockHttp.CreateFactory("sms-kavenegar", "https://api.kavenegar.com", HttpStatusCode.OK, json);
        var provider = new KavenegarProvider(factory, NullLogger<KavenegarProvider>.Instance);

        var result = await provider.SendAsync(
            new SmsAccountConfig("bad-key", null, null, "1000", null, null), Request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().BeFalse();
        result.ProviderStatusCode.Should().Be("KAVENEGAR_401");
    }

    [Fact]
    public async Task SendAsync_ProviderSideOutageStatus_ReturnsRetryableFailure()
    {
        var json = """{"return":{"status":500,"message":"Internal error"}}""";
        var factory = MockHttp.CreateFactory("sms-kavenegar", "https://api.kavenegar.com", HttpStatusCode.OK, json);
        var provider = new KavenegarProvider(factory, NullLogger<KavenegarProvider>.Instance);

        var result = await provider.SendAsync(
            new SmsAccountConfig("test-api-key", null, null, "1000", null, null), Request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_MissingApiKey_FailsWithoutMakingAnHttpCall()
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var provider = new KavenegarProvider(factory.Object, NullLogger<KavenegarProvider>.Instance);

        var result = await provider.SendAsync(
            new SmsAccountConfig(null, null, null, "1000", null, null), Request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_MapsDeliveredAndFailedCodesCorrectly()
    {
        var json = """{"return":{"status":200,"message":"OK"},"entries":[{"messageid":1,"status":6},{"messageid":2,"status":5}]}""";
        var factory = MockHttp.CreateFactory("sms-kavenegar", "https://api.kavenegar.com", HttpStatusCode.OK, json);
        var provider = new KavenegarProvider(factory, NullLogger<KavenegarProvider>.Instance);

        var statuses = await provider.GetStatusAsync(
            new SmsAccountConfig("test-api-key", null, null, null, null, null), ["1", "2"], CancellationToken.None);

        statuses.Should().ContainSingle(s => s.ProviderMessageId == "1" && s.Status == SmsStatus.Delivered);
        statuses.Should().ContainSingle(s => s.ProviderMessageId == "2" && s.Status == SmsStatus.Failed);
    }
}
