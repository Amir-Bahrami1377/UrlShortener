using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shortener.Application.Abstractions;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.SmsProviders;

namespace Shortener.UnitTests.SmsProviders;

public sealed class AfeProviderTests
{
    private static readonly SmsSendRequest Request = new("09121234567", "hello", null, null, SmsMessageType.DownloadLink);

    private const string SendMessageSuccessBody = """
        <?xml version="1.0" encoding="utf-8"?>
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
          <soap:Body>
            <SendMessageResponse xmlns="http://www.afe.ir/">
              <SendMessageResult xmlns:a="http://www.afe.ir/">
                <a:string>98765</a:string>
              </SendMessageResult>
            </SendMessageResponse>
          </soap:Body>
        </soap:Envelope>
        """;

    private const string FaultBody = """
        <?xml version="1.0" encoding="utf-8"?>
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
          <soap:Body>
            <soap:Fault>
              <faultcode>soap:Server</faultcode>
              <faultstring>Invalid username or password</faultstring>
            </soap:Fault>
          </soap:Body>
        </soap:Envelope>
        """;

    [Fact]
    public async Task SendAsync_SuccessResponse_ReturnsSuccessWithMessageId()
    {
        var factory = MockHttp.CreateFactory("sms-afe", "https://www.afe.ir", HttpStatusCode.OK, SendMessageSuccessBody);
        var provider = new AfeProvider(factory, NullLogger<AfeProvider>.Instance);

        var result = await provider.SendAsync(
            new SmsAccountConfig(null, "user", "pass", "1000", null, null), Request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.ProviderMessageId.Should().Be("98765");
    }

    [Fact]
    public async Task SendAsync_SoapFault_ReturnsNonRetryableFailureWithFaultString()
    {
        // ASMX/basicHttpBinding services return SOAP Faults with HTTP 500.
        var factory = MockHttp.CreateFactory("sms-afe", "https://www.afe.ir", HttpStatusCode.InternalServerError, FaultBody);
        var provider = new AfeProvider(factory, NullLogger<AfeProvider>.Instance);

        var result = await provider.SendAsync(
            new SmsAccountConfig(null, "user", "wrong-pass", "1000", null, null), Request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().BeFalse();
        result.ErrorMessage.Should().Be("Invalid username or password");
    }

    [Fact]
    public async Task SendAsync_MissingCredentials_FailsWithoutMakingAnHttpCall()
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var provider = new AfeProvider(factory.Object, NullLogger<AfeProvider>.Instance);

        var result = await provider.SendAsync(
            new SmsAccountConfig(null, null, null, "1000", null, null), Request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_PostsToTheConfirmedEndpoint_WithSoapActionAndEnvelopeFields()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var factory = MockHttp.CreateFactory("sms-afe", "https://www.afe.ir", HttpStatusCode.OK, SendMessageSuccessBody, req =>
        {
            captured = req;
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
        });
        var provider = new AfeProvider(factory, NullLogger<AfeProvider>.Instance);

        await provider.SendAsync(
            new SmsAccountConfig(null, "user1", "pass1", "1000", null, null), Request, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.RequestUri!.AbsolutePath.Should().Be("/WebService/V7/BoxService.asmx");
        captured.Method.Should().Be(HttpMethod.Post);
        captured.Headers.GetValues("SOAPAction").Should().ContainSingle().Which.Should().Be("\"http://www.afe.ir/SendMessage\"");

        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("user1").And.Contain("pass1").And.Contain("1000").And.Contain("09121234567").And.Contain("hello");
    }

    [Fact]
    public async Task GetStatusAsync_MissingCredentials_ReturnsEmptyWithoutMakingAnHttpCall()
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var provider = new AfeProvider(factory.Object, NullLogger<AfeProvider>.Instance);

        var result = await provider.GetStatusAsync(
            new SmsAccountConfig(null, null, null, "1000", null, null), ["1"], CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStatusAsync_DeliveredKeywordInResult_MapsToDelivered()
    {
        const string body = """
            <?xml version="1.0" encoding="utf-8"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body>
                <GetMessagesStatusResponse xmlns="http://www.afe.ir/">
                  <GetMessagesStatusResult xmlns:a="http://www.afe.ir/">
                    <a:string>Delivered</a:string>
                  </GetMessagesStatusResult>
                </GetMessagesStatusResponse>
              </soap:Body>
            </soap:Envelope>
            """;
        var factory = MockHttp.CreateFactory("sms-afe", "https://www.afe.ir", HttpStatusCode.OK, body);
        var provider = new AfeProvider(factory, NullLogger<AfeProvider>.Instance);

        var result = await provider.GetStatusAsync(
            new SmsAccountConfig(null, "user", "pass", "1000", null, null), ["98765"], CancellationToken.None);

        result.Should().ContainSingle(s => s.ProviderMessageId == "98765" && s.Status == SmsStatus.Delivered);
    }

    [Fact]
    public async Task GetCreditAsync_NumericResult_ReturnsParsedDecimal()
    {
        const string body = """
            <?xml version="1.0" encoding="utf-8"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body>
                <GetRemainingCreditResponse xmlns="http://www.afe.ir/">
                  <GetRemainingCreditResult>15000</GetRemainingCreditResult>
                </GetRemainingCreditResponse>
              </soap:Body>
            </soap:Envelope>
            """;
        var factory = MockHttp.CreateFactory("sms-afe", "https://www.afe.ir", HttpStatusCode.OK, body);
        var provider = new AfeProvider(factory, NullLogger<AfeProvider>.Instance);

        var result = await provider.GetCreditAsync(
            new SmsAccountConfig(null, "user", "pass", "1000", null, null), CancellationToken.None);

        result.Should().Be(15000m);
    }
}
