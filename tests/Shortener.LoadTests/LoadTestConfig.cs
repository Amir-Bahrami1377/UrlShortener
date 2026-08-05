namespace Shortener.LoadTests;

/// <summary>
/// §M8.4d — points at the persistent dev docker-compose stack (docker-compose.dev.yml), not
/// Testcontainers: a load test needs to hit real, externally-listening HTTP hosts (Api + PublicWeb
/// running via `dotnet run`) to get realistic latency numbers, not an in-process WebApplicationFactory.
/// </summary>
public static class LoadTestConfig
{
    public const string ApiBaseUrl = "http://127.0.0.1:5217";
    public const string PublicWebBaseUrl = "http://127.0.0.1:5260";
    public const string SqlConnectionString =
        "Server=127.0.0.1,1433;Database=UrlShortener;User Id=sa;Password=Dev_Passw0rd!;TrustServerCertificate=True";
    public const string RedisConnectionString = "127.0.0.1:6380";

    public static string ApiKey { get; set; } = string.Empty;
}
