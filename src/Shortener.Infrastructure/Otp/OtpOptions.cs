namespace Shortener.Infrastructure.Otp;

public sealed class OtpOptions
{
    public int Length { get; set; } = 6;
    public int TtlSeconds { get; set; } = 120;
    public int ResendCooldownSeconds { get; set; } = 90;
    public int MaxRequestsPerLinkPerHour { get; set; } = 3;
    public int MaxRequestsPerIpPerHour { get; set; } = 10;
    public int MaxVerifyAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}

public sealed class DownloadOptions
{
    public int TokenTtlSeconds { get; set; } = 300;
}
