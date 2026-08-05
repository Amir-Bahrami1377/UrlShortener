using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shortener.Application.Abstractions;
using Shortener.Infrastructure.Persistence;
using StackExchange.Redis;

namespace Shortener.Infrastructure.Services;

public sealed class ApiKeyLookupService(AppDbContext db, IConnectionMultiplexer redis, ApiKeyUsageChannel usageChannel)
    : IApiKeyLookupService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task<ApiKeyLookupResult> ValidateAsync(string rawApiKey, CancellationToken ct)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawApiKey))).ToLowerInvariant();
        var cacheKey = $"apikey:{hash}";
        var redisDb = redis.GetDatabase();

        CachedApiKeyInfo? info = null;
        try
        {
            var cached = await redisDb.StringGetAsync(cacheKey);
            if (cached.HasValue)
            {
                info = JsonSerializer.Deserialize<CachedApiKeyInfo>((string)cached!);
            }
        }
        catch (RedisConnectionException)
        {
            // Redis down — fall through to SQL. The API-key path must not depend on cache availability.
        }

        if (info is null)
        {
            var apiKey = await db.ApiKeys.Include(a => a.Client).FirstOrDefaultAsync(a => a.KeyHash == hash, ct);
            if (apiKey is null)
            {
                return new ApiKeyLookupResult(ApiKeyValidationStatus.Invalid);
            }

            info = new CachedApiKeyInfo(
                apiKey.Id, apiKey.ClientId, apiKey.IsActive, apiKey.RevokedAt is not null,
                apiKey.ExpiresAt, apiKey.Client!.IsActive);

            try
            {
                await redisDb.StringSetAsync(cacheKey, JsonSerializer.Serialize(info), CacheTtl);
            }
            catch (RedisConnectionException)
            {
                // Non-fatal: just means the next lookup also hits SQL.
            }
        }

        var keyIsUsable = info.IsActive && !info.IsRevoked && (info.ExpiresAt is not { } exp || exp >= DateTime.UtcNow);
        if (!keyIsUsable)
        {
            return new ApiKeyLookupResult(ApiKeyValidationStatus.Invalid);
        }

        if (!info.ClientIsActive)
        {
            return new ApiKeyLookupResult(ApiKeyValidationStatus.ClientInactive, info.ApiKeyId, info.ClientId);
        }

        usageChannel.TryWrite(info.ApiKeyId);
        return new ApiKeyLookupResult(ApiKeyValidationStatus.Valid, info.ApiKeyId, info.ClientId);
    }

    private sealed record CachedApiKeyInfo(
        int ApiKeyId, int ClientId, bool IsActive, bool IsRevoked, DateTime? ExpiresAt, bool ClientIsActive);
}
