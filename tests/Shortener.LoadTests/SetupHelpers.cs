using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using StackExchange.Redis;

namespace Shortener.LoadTests;

public sealed record ProvisionedLink(string Code, long ShortLinkId);

/// <summary>
/// Everything here is untimed setup/plumbing for the NBomber scenarios in this project — it is
/// deliberately NOT part of what §M8.4d measures. Talks to the real dev docker-compose SQL
/// Server/Redis directly only where there's no other way to observe something an HTTP client
/// can't see (the raw OTP digits, which only ever exist in a log line or the rendered SmsMessage.Body).
/// </summary>
public static class SetupHelpers
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Every load test run gets its own client so re-runs never collide with each other or
    /// with the seeded DEV client's own traffic/history.</summary>
    public static async Task<string> ProvisionClientAndApiKeyAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clientCode = $"LOADTEST-{suffix}";

        var rawKeyBytes = RandomNumberGenerator.GetBytes(32);
        var rawKey = "sk_" + Convert.ToBase64String(rawKeyBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant();

        await using var conn = new SqlConnection(LoadTestConfig.SqlConnectionString);
        await conn.OpenAsync();

        var clientId = (int)(await new SqlCommand(
            """
            INSERT INTO Clients (Name, Code, IsActive, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@name, @code, 1, SYSUTCDATETIME());
            """, conn)
        {
            Parameters = { new SqlParameter("@name", $"Load Test Client {suffix}"), new SqlParameter("@code", clientCode) },
        }.ExecuteScalarAsync())!;

        await new SqlCommand(
            """
            INSERT INTO ApiKeys (ClientId, KeyHash, KeyPrefix, Title, IsActive, CreatedAt)
            VALUES (@clientId, @hash, @prefix, 'Load test key', 1, SYSUTCDATETIME());
            """, conn)
        {
            Parameters =
            {
                new SqlParameter("@clientId", clientId),
                new SqlParameter("@hash", hash),
                new SqlParameter("@prefix", rawKey[..12]),
            },
        }.ExecuteNonQueryAsync();

        var fakeProviderId = (int)(await new SqlCommand("SELECT Id FROM SmsProviders WHERE Code = 'fake'", conn).ExecuteScalarAsync())!;

        foreach (var purpose in new[] { 0, 1 }) // SmsAccountPurpose: Bulk=0, Otp=1
        {
            await new SqlCommand(
                """
                INSERT INTO SmsAccounts (ClientId, SmsProviderId, Title, Purpose, RatePerMinute, IsDefault, IsActive)
                VALUES (@clientId, @providerId, @title, @purpose, 3000, 1, 1);
                """, conn)
            {
                Parameters =
                {
                    new SqlParameter("@clientId", clientId),
                    new SqlParameter("@providerId", fakeProviderId),
                    new SqlParameter("@title", $"Load test account (purpose={purpose})"),
                    new SqlParameter("@purpose", purpose),
                },
            }.ExecuteNonQueryAsync();
        }

        foreach (var (templateType, body) in new[] { (0, "دانلود {reportName}: {shortUrl}"), (1, "کد تایید شما: {otp}") })
        {
            await new SqlCommand(
                """
                INSERT INTO MessageTemplates (ClientId, TemplateType, Title, Body, IsActive, CreatedAt, UpdatedAt)
                VALUES (@clientId, @type, @title, @body, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
                """, conn)
            {
                Parameters =
                {
                    new SqlParameter("@clientId", clientId),
                    new SqlParameter("@type", templateType),
                    new SqlParameter("@title", $"Load test template {templateType}"),
                    new SqlParameter("@body", body),
                },
            }.ExecuteNonQueryAsync();
        }

        Console.WriteLine($"[Setup] Provisioned load-test client '{clientCode}' (Id={clientId}).");
        return rawKey;
    }

    public static HttpClient CreateApiClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(LoadTestConfig.ApiBaseUrl), Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Add("X-Api-Key", LoadTestConfig.ApiKey);
        return client;
    }

    /// <summary>Fixed so seeded download tokens (see SeedDownloadTokenDirectlyAsync) can precompute a
    /// matching UserAgentHash. Loopback traffic always presents IpHash for 127.0.0.1 either way.</summary>
    public const string LoadTestUserAgent = "ShortenerLoadTest/1.0";
    private const string LoopbackIp = "127.0.0.1";

    public static HttpClient CreatePublicWebClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(LoadTestConfig.PublicWebBaseUrl), Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(LoadTestUserAgent);
        return client;
    }

    public static async Task<ProvisionedLink> UploadAsync(HttpClient apiClient, byte[] fileBytes, bool sendSmsImmediately)
    {
        using var content = new MultipartFormDataContent();
        var metadata = new
        {
            Shop = Guid.NewGuid().ToString("N")[..10],
            Shod = Guid.NewGuid().ToString("N")[..10],
            Radif = Guid.NewGuid().ToString("N")[..10],
            ReportId = 1,
            ReportName = "Load Test Report",
            PhoneNumber = "09121234567",
            SendSmsImmediately = sendSmsImmediately,
        };
        content.Add(new StringContent(JsonSerializer.Serialize(metadata, JsonOptions), Encoding.UTF8, "application/json"), "metadata");

        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "loadtest.pdf");

        var response = await apiClient.PostAsync("/api/v1/links", content);
        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync<JsonElement>())!;

        var code = body.GetProperty("code").GetString()!;
        var shortLinkId = await GetShortLinkIdAsync(code);
        return new ProvisionedLink(code, shortLinkId);
    }

    private static async Task<long> GetShortLinkIdAsync(string code)
    {
        await using var conn = new SqlConnection(LoadTestConfig.SqlConnectionString);
        await conn.OpenAsync();
        var cmd = new SqlCommand("SELECT Id FROM ShortLinks WHERE Code = @code", conn);
        cmd.Parameters.AddWithValue("@code", code);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Requests + verifies an OTP for one link, retrieving the raw code from SmsMessages.Body
    /// (the only queryable place it exists — FakeSmsProvider otherwise only logs it) rather than
    /// scraping Worker console output. Requires a real Worker process actively consuming sms:otp.</summary>
    public static async Task<string> GetDownloadTokenViaRealOtpFlowAsync(HttpClient publicWebClient, ProvisionedLink link)
    {
        var requestResponse = await publicWebClient.PostAsync($"/s/{link.Code}/otp/request", content: null);
        requestResponse.EnsureSuccessStatusCode();

        var otp = await PollForOtpAsync(link.ShortLinkId);

        var verifyResponse = await publicWebClient.PostAsJsonAsync($"/s/{link.Code}/otp/verify", new { Otp = otp }, JsonOptions);
        verifyResponse.EnsureSuccessStatusCode();
        var body = (await verifyResponse.Content.ReadFromJsonAsync<JsonElement>())!;
        var downloadUrl = body.GetProperty("downloadUrl").GetString()!;
        return downloadUrl[3..]; // "/d/{token}" -> "{token}"
    }

    private static async Task<string> PollForOtpAsync(long shortLinkId)
    {
        await using var conn = new SqlConnection(LoadTestConfig.SqlConnectionString);
        await conn.OpenAsync();

        for (var attempt = 0; attempt < 50; attempt++)
        {
            var cmd = new SqlCommand(
                "SELECT TOP 1 Body FROM SmsMessages WHERE ShortLinkId = @id AND MessageType = 1 ORDER BY Id DESC", conn);
            cmd.Parameters.AddWithValue("@id", shortLinkId);
            var body = (string?)await cmd.ExecuteScalarAsync();
            if (body is not null)
            {
                var digits = new string(body.Where(char.IsDigit).ToArray());
                if (digits.Length >= 6)
                {
                    return digits[..6];
                }
            }

            await Task.Delay(200);
        }

        throw new InvalidOperationException($"Timed out waiting for the OTP SmsMessage for ShortLinkId={shortLinkId}. Is the Worker running?");
    }

    /// <summary>Bypasses the real OTP request/verify round-trip and writes the dl:{token} Redis entry
    /// directly, matching OtpService.VerifyAsync's exact key/payload contract byte-for-byte
    /// (DownloadTokenService.ConsumeAsync is the only reader, and doesn't care how the key got there).
    /// §M8.4d's download scenario needs ~100 valid one-shot tokens, but PublicLink's 60/min-per-IP
    /// limiter (doc §M8.5) is shared across GET /s/{code}, otp/request, AND otp/verify — 100 real
    /// round-trips from the one real IP this harness runs from would take minutes just to stay under
    /// it. The real flow is still exercised for a couple of samples (see RunAsync) as a sanity check
    /// that this contract hasn't drifted; M4's own test suite is what actually proves OTP verification.</summary>
    public static async Task<string> SeedDownloadTokenDirectlyAsync(IDatabase redisDb, ProvisionedLink link, int tokenTtlSeconds)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = new
        {
            ShortLinkId = link.ShortLinkId,
            IpHash = RequestFingerprint(LoopbackIp),
            UserAgentHash = RequestFingerprint(LoadTestUserAgent),
        };
        // No JsonOptions here deliberately — OtpService.VerifyAsync serializes DownloadTokenPayload
        // with the framework default (PascalCase property names preserved as-is), and this anonymous
        // object's shape must match that byte-for-byte or DownloadTokenService.ConsumeAsync's
        // Deserialize<DownloadTokenPayload> call will silently produce nulls instead of throwing.
        await redisDb.StringSetAsync($"dl:{token}", JsonSerializer.Serialize(payload), TimeSpan.FromSeconds(tokenTtlSeconds));
        return token;
    }

    private static string RequestFingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>The OTP-per-IP rate limit (10/hour, doc §M4.3) is meant to stop one real attacker, but
    /// every simulated user in this harness shares one real source IP — clearing it periodically during
    /// setup is a load-test-harness concession, not something the product itself needs to tolerate.</summary>
    public static async Task ClearOtpIpRateLimitAsync()
    {
        var mux = await ConnectionMultiplexer.ConnectAsync(LoadTestConfig.RedisConnectionString);
        var server = mux.GetServer(mux.GetEndPoints()[0]);
        var db = mux.GetDatabase();
        await foreach (var key in server.KeysAsync(pattern: "otp:ip:*"))
        {
            await db.KeyDeleteAsync(key);
        }

        await mux.CloseAsync();
    }
}
