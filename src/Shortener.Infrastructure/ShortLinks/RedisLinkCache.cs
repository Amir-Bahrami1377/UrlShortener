using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shortener.Application.Abstractions;
using StackExchange.Redis;

namespace Shortener.Infrastructure.ShortLinks;

public sealed class RedisLinkCache(
    IConnectionMultiplexer redis, IOptions<ShortLinkOptions> options, ILogger<RedisLinkCache> logger) : ILinkCache
{
    public async Task<LinkCacheEntry?> GetAsync(string code, CancellationToken ct)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync($"link:{code}");
            return value.HasValue ? JsonSerializer.Deserialize<LinkCacheEntry>((string)value!) : null;
        }
        catch (RedisConnectionException ex)
        {
            logger.LogWarning(ex, "Redis unavailable reading link cache for {Code} — caller should fall back to SQL", code);
            return null;
        }
    }

    public async Task SetAsync(string code, LinkCacheEntry entry, CancellationToken ct)
    {
        try
        {
            await redis.GetDatabase().StringSetAsync(
                $"link:{code}", JsonSerializer.Serialize(entry), TimeSpan.FromMinutes(options.Value.CacheTtlMinutes));
        }
        catch (RedisConnectionException ex)
        {
            logger.LogWarning(ex, "Redis unavailable writing link cache for {Code}", code);
        }
    }

    public async Task InvalidateAsync(string code, CancellationToken ct)
    {
        try
        {
            await redis.GetDatabase().KeyDeleteAsync($"link:{code}");
        }
        catch (RedisConnectionException ex)
        {
            logger.LogWarning(ex, "Redis unavailable invalidating link cache for {Code}", code);
        }
    }
}
