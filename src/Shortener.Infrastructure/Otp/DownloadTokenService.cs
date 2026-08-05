using System.Text.Json;
using Shortener.Application.Abstractions;
using Shortener.Application.Contracts;
using StackExchange.Redis;

namespace Shortener.Infrastructure.Otp;

public sealed class DownloadTokenService(IConnectionMultiplexer redis) : IDownloadTokenService
{
    public async Task<DownloadTokenPayload?> ConsumeAsync(string token, CancellationToken ct)
    {
        var value = await redis.GetDatabase().StringGetDeleteAsync($"dl:{token}");
        return value.HasValue ? JsonSerializer.Deserialize<DownloadTokenPayload>((string)value!) : null;
    }
}
