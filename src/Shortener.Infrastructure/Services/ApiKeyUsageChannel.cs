using System.Threading.Channels;

namespace Shortener.Infrastructure.Services;

/// <summary>
/// Decouples "record that this API key was just used" from the request path — ApiKey.LastUsedAt
/// is updated by a background consumer, never inline in an authenticated request.
/// </summary>
public sealed class ApiKeyUsageChannel
{
    private readonly Channel<int> _channel = Channel.CreateBounded<int>(
        new BoundedChannelOptions(1_000) { FullMode = BoundedChannelFullMode.DropWrite });

    public ChannelReader<int> Reader => _channel.Reader;

    public void TryWrite(int apiKeyId) => _channel.Writer.TryWrite(apiKeyId);
}
