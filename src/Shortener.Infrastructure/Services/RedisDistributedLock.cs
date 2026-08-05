using Shortener.Application.Abstractions;
using StackExchange.Redis;

namespace Shortener.Infrastructure.Services;

public sealed class RedisDistributedLock(IConnectionMultiplexer redis) : IDistributedLock
{
    private static readonly string InstanceId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    // Only delete the key if it still holds our token — otherwise we might release a lock that
    // expired and was already re-acquired by another instance.
    private const string ReleaseScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    public async Task<IAsyncDisposable?> TryAcquireAsync(string name, TimeSpan ttl, CancellationToken ct)
    {
        var key = $"job:{name}";
        var db = redis.GetDatabase();
        var acquired = await db.StringSetAsync(key, InstanceId, ttl, When.NotExists);
        return acquired ? new LockHandle(db, key) : null;
    }

    private sealed class LockHandle(IDatabase db, string key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await db.ScriptEvaluateAsync(ReleaseScript, [key], [InstanceId]);
            }
            catch (RedisException)
            {
                // Best-effort release — if Redis is unreachable, the TTL alone will expire the lock.
            }
        }
    }
}
