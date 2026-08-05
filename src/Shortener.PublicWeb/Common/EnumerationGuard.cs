using Shortener.Application.Services;
using StackExchange.Redis;

namespace Shortener.PublicWeb.Common;

/// <summary>§M8.5 — "block an IP after 20 404s on /s/ within 5 minutes," to blunt short-code
/// enumeration. Uses the rl:404:{ipHash} key already named in the doc's own Appendix A.</summary>
public sealed class EnumerationGuard(IConnectionMultiplexer redis)
{
    private const long MaxNotFoundCount = 20;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public async Task<bool> IsBlockedAsync(string ipAddress)
    {
        var db = redis.GetDatabase();
        var count = await db.StringGetAsync(Key(ipAddress));
        return count.HasValue && (long)count >= MaxNotFoundCount;
    }

    public async Task RecordNotFoundAsync(string ipAddress)
    {
        var db = redis.GetDatabase();
        var key = Key(ipAddress);
        var count = await db.StringIncrementAsync(key);
        if (count == 1)
        {
            await db.KeyExpireAsync(key, Window);
        }
    }

    private static string Key(string ipAddress) => $"rl:404:{RequestFingerprint.Hash(ipAddress)}";
}
