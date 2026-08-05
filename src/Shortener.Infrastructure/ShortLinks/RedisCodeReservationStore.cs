using Shortener.Application.Abstractions;
using StackExchange.Redis;

namespace Shortener.Infrastructure.ShortLinks;

public sealed class RedisCodeReservationStore(IConnectionMultiplexer redis) : ICodeReservationStore
{
    public Task<bool> TryReserveAsync(string code, TimeSpan ttl, CancellationToken ct) =>
        redis.GetDatabase().StringSetAsync($"link:reserve:{code}", 1, ttl, When.NotExists);
}
