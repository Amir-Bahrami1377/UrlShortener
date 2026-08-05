using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace Shortener.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class UploadLinkEndpointTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task Upload_ValidPdf_ReturnsCreatedWithSixCharacterCode()
    {
        var client = await fixture.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync("/api/v1/links", BuildMultipart(NewMetadata(), CreatePdfBytes()));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().HaveLength(6);
        body.GetProperty("isDuplicate").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Upload_SameBusinessKeyTwice_ReturnsIsDuplicateTrueWithTheSameCode()
    {
        var client = await fixture.CreateAuthenticatedClientAsync();
        var metadata = NewMetadata();

        var first = await client.PostAsync("/api/v1/links", BuildMultipart(metadata, CreatePdfBytes()));
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstCode = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

        var second = await client.PostAsync("/api/v1/links", BuildMultipart(metadata, CreatePdfBytes()));

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        secondBody.GetProperty("isDuplicate").GetBoolean().Should().BeTrue();
        secondBody.GetProperty("code").GetString().Should().Be(firstCode);
    }

    [Fact]
    public async Task Upload_FileLargerThanThreeMegabytes_Returns413WithFileTooLargeErrorCode()
    {
        var client = await fixture.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            "/api/v1/links", BuildMultipart(NewMetadata(), CreatePdfBytes(3 * 1024 * 1024 + 100)));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errorCode").GetString().Should().Be("ERR_FILE_TOO_LARGE");
    }

    [Fact]
    public async Task Upload_ContentDoesNotMatchDeclaredPdfExtension_Returns415()
    {
        var client = await fixture.CreateAuthenticatedClientAsync();
        var notActuallyAPdf = "this is plain text, not a pdf"u8.ToArray();

        var response = await client.PostAsync("/api/v1/links", BuildMultipart(NewMetadata(), notActuallyAPdf));

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errorCode").GetString().Should().Be("ERR_INVALID_FILE_TYPE");
    }

    [Fact]
    public async Task Upload_WithoutApiKeyHeader_Returns401()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsync("/api/v1/links", BuildMultipart(NewMetadata(), CreatePdfBytes()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Upload_WithBogusApiKey_Returns401()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "sk_this_key_does_not_exist");

        var response = await client.PostAsync("/api/v1/links", BuildMultipart(NewMetadata(), CreatePdfBytes()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Upload_SendSmsImmediatelyFalse_ReportsSmsStatusPending()
    {
        var client = await fixture.CreateAuthenticatedClientAsync();
        var metadata = NewMetadata(sendSmsImmediately: false);

        var response = await client.PostAsync("/api/v1/links", BuildMultipart(metadata, CreatePdfBytes()));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("smsStatus").GetString().Should().Be("Pending");
    }

    [Fact]
    public async Task Upload_100ConcurrentDistinctRequests_AllSucceedWithUniqueCodes()
    {
        var client = await fixture.CreateAuthenticatedClientAsync();
        var runId = Guid.NewGuid().ToString("N")[..8];

        var responses = await Task.WhenAll(Enumerable.Range(0, 100).Select(i =>
            client.PostAsync("/api/v1/links", BuildMultipart(NewMetadata(shop: $"C{runId}-{i}"), CreatePdfBytes()))));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);

        var codes = await Task.WhenAll(responses.Select(async r =>
            (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()));
        codes.Should().OnlyHaveUniqueItems();
    }

    private static UploadMetadataDto NewMetadata(string? shop = null, bool sendSmsImmediately = true) => new(
        Shop: shop ?? Guid.NewGuid().ToString("N")[..10],
        Shod: Guid.NewGuid().ToString("N")[..10],
        Radif: Guid.NewGuid().ToString("N")[..10],
        ReportId: 1,
        ReportName: "Test Report",
        PhoneNumber: "09121234567",
        ClientRequestId: null,
        BatchTag: null,
        SendSmsImmediately: sendSmsImmediately);

    private static MultipartFormDataContent BuildMultipart(UploadMetadataDto metadata, byte[] fileBytes)
    {
        var content = new MultipartFormDataContent();

        var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        content.Add(new StringContent(metadataJson, Encoding.UTF8, "application/json"), "metadata");

        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "test.pdf");

        return content;
    }

    private static byte[] CreatePdfBytes(int approxSize = 200)
    {
        var header = "%PDF-1.4\n"u8.ToArray();
        if (approxSize <= header.Length)
        {
            return header;
        }

        var padding = new byte[approxSize - header.Length];
        Array.Fill(padding, (byte)'A');
        return [.. header, .. padding];
    }

    private sealed record UploadMetadataDto(
        string Shop, string Shod, string Radif, int ReportId, string ReportName,
        string PhoneNumber, string? ClientRequestId, string? BatchTag, bool SendSmsImmediately);
}
