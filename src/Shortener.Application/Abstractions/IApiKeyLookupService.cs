namespace Shortener.Application.Abstractions;

public interface IApiKeyLookupService
{
    /// <summary>
    /// Validates a raw API key. Distinguishes an invalid/expired/revoked key (401) from a valid
    /// key whose Client has been deactivated (403) since those map to different error codes.
    /// On success, asynchronously queues an ApiKey.LastUsedAt update (never awaited inline).
    /// </summary>
    Task<ApiKeyLookupResult> ValidateAsync(string rawApiKey, CancellationToken ct);
}

public enum ApiKeyValidationStatus
{
    Invalid,
    ClientInactive,
    Valid,
}

public sealed record ApiKeyLookupResult(ApiKeyValidationStatus Status, int? ApiKeyId = null, int? ClientId = null);
