using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shortener.Application.Abstractions;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.SmsProviders;

namespace Shortener.UnitTests.SmsProviders;

public sealed class FarazSmsProviderTests
{
    private static readonly SmsSendRequest Request = new("09121234567", "hello", null, null, SmsMessageType.DownloadLink);

    [Fact]
    public async Task SendAsync_StatusOk_ReturnsSuccessWithMessageIdAndCost()
    {
        var json = """{"status":"OK","data":{"message_id":"abc123","cost":75}}""";
        var factory = MockHttp.CreateFactory("sms-farazsms", "https://edge.ippanel.com", HttpStatusCode.OK, json);
        var provider = new FarazSmsProvider(factory, NullLogger<FarazSmsProvider>.Instance);

        var result = await provider.SendAsync(
            new SmsAccountConfig("access-key", null, null, "1000", null, null), Request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.ProviderMessageId.Should().Be("abc123");
        result.Cost.Should().Be(75);
    }

    [Fact]
    public async Task SendAsync_ErrorStatusWithServerErrorHttpCode_ReturnsRetryableFailure()
    {
        var json = """{"status":"ERROR","errorMessage":"internal"}""";
        var factory = MockHttp.CreateFactory("sms-farazsms", "https://edge.ippanel.com", HttpStatusCode.InternalServerError, json);
        var provider = new FarazSmsProvider(factory, NullLogger<FarazSmsProvider>.Instance);

        var result = await provider.SendAsync(
            new SmsAccountConfig("access-key", null, null, "1000", null, null), Request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_ErrorStatusWithClientErrorHttpCode_ReturnsNonRetryableFailure()
    {
        var json = """{"status":"ERROR","errorMessage":"bad sender"}""";
        var factory = MockHttp.CreateFactory("sms-farazsms", "https://edge.ippanel.com", HttpStatusCode.BadRequest, json);
        var provider = new FarazSmsProvider(factory, NullLogger<FarazSmsProvider>.Instance);

        var result = await provider.SendAsync(
            new SmsAccountConfig("access-key", null, null, "1000", null, null), Request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().BeFalse();
        result.ErrorMessage.Should().Be("bad sender");
    }

    [Fact]
    public async Task SendAsync_MissingApiKey_FailsWithoutMakingAnHttpCall()
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var provider = new FarazSmsProvider(factory.Object, NullLogger<FarazSmsProvider>.Instance);

        var result = await provider.SendAsync(
            new SmsAccountConfig(null, null, null, "1000", null, null), Request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().BeFalse();
    }
}
