namespace Shortener.Domain.Exceptions;

public sealed class FileTooLargeException(long actualBytes, long maxBytes)
    : Exception($"File size {actualBytes} bytes exceeds the maximum of {maxBytes} bytes.")
{
    public long ActualBytes { get; } = actualBytes;
    public long MaxBytes { get; } = maxBytes;
}
