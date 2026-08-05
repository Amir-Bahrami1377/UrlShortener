using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;

namespace Shortener.UnitTests.SmsProviders;

/// <summary>Builds a fake IHttpClientFactory that returns a fixed JSON response for the named
/// client, so the three real ISmsProvider implementations (§M5.1) can be unit-tested against their
/// HTTP contract without any live gateway — exactly the "mocked HttpMessageHandler" coverage noted
/// as owed when those providers were written contract-untested.</summary>
internal static class MockHttp
{
    public static IHttpClientFactory CreateFactory(
        string clientName, string baseAddress, HttpStatusCode statusCode, string jsonBody,
        Action<HttpRequestMessage>? captureRequest = null)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                captureRequest?.Invoke(request);
                return new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
                };
            });

        var client = new HttpClient(handler.Object) { BaseAddress = new Uri(baseAddress) };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(clientName)).Returns(client);
        return factory.Object;
    }
}
