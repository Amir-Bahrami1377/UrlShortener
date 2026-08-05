namespace Shortener.Domain.Enums;

public enum SmsStatus : byte
{
    /// <summary>Waiting for the explicit dispatch command (sendSmsImmediately=false).</summary>
    Pending = 0,
    Queued = 1,
    Sending = 2,
    Sent = 3,
    Delivered = 4,
    Failed = 5,
    Undelivered = 6,
    Cancelled = 7
}
