using System.Threading.Channels;
using Shortener.Application.Abstractions;

namespace Shortener.Infrastructure.Services;

/// <summary>Bounded, drop-oldest-writes channel per §M4.7 — under load, logging loses to serving requests.</summary>
public sealed class LinkAccessLogChannel : ILinkAccessLogger
{
    private readonly Channel<LinkAccessLogEntry> _channel = Channel.CreateBounded<LinkAccessLogEntry>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropWrite });

    public ChannelReader<LinkAccessLogEntry> Reader => _channel.Reader;

    public void Enqueue(LinkAccessLogEntry entry) => _channel.Writer.TryWrite(entry);
}
