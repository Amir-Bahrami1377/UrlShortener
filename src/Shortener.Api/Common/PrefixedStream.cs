namespace Shortener.Api.Common;

/// <summary>
/// Replays bytes already read off a stream (e.g. for a magic-number peek) before continuing to
/// read from it, so the header buffer doesn't have to be read twice or buffered separately (§M3.2.5).
/// </summary>
public sealed class PrefixedStream(ReadOnlyMemory<byte> prefix, Stream inner) : Stream
{
    private int _prefixPosition;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_prefixPosition < prefix.Length)
        {
            var remaining = prefix.Length - _prefixPosition;
            var toCopy = Math.Min(remaining, buffer.Length);
            prefix.Span.Slice(_prefixPosition, toCopy).CopyTo(buffer.Span);
            _prefixPosition += toCopy;
            return toCopy;
        }

        return await inner.ReadAsync(buffer, cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
