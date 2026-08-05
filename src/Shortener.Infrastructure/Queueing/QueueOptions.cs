namespace Shortener.Infrastructure.Queueing;

public sealed class QueueOptions
{
    public string BulkStream { get; set; } = "sms:bulk";
    public string OtpStream { get; set; } = "sms:otp";
    public string DeadStream { get; set; } = "sms:dead";
    public string BulkConsumerGroup { get; set; } = "bulk-workers";
    public string OtpConsumerGroup { get; set; } = "otp-workers";
    public int ClaimIdleMinutes { get; set; } = 5;
    public int MaxDeliveryAttempts { get; set; } = 5;
    public int BulkDefaultRatePerMinute { get; set; } = 3000;
    public int StreamMaxLen { get; set; } = 100000;
}
