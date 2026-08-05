namespace Shortener.Application.Abstractions;

/// <summary>Implements §M4.3 (request) and §M4.4 (verify) — the OTP request/verify algorithms,
/// including the direct-to-sms:otp publish that deliberately bypasses the Outbox for latency.</summary>
public interface IOtpService
{
    Task<OtpRequestResult> RequestAsync(string code, string ipAddress, CancellationToken ct);

    Task<OtpVerifyResult> VerifyAsync(string code, string otp, string ipAddress, string userAgent, CancellationToken ct);
}

public enum OtpRequestOutcome
{
    NotFound,
    LinkUnhealthy,
    Locked,
    Cooldown,
    LimitPerLink,
    LimitPerIp,
    NotConfigured,
    Sent,
}

public sealed record OtpRequestResult(
    OtpRequestOutcome Outcome,
    int? CooldownSecondsRemaining = null,
    int? TtlSeconds = null,
    LinkHealthStatus? UnhealthyReason = null);

public enum OtpVerifyOutcome
{
    NotFound,
    LinkUnhealthy,
    Locked,
    Expired,
    Invalid,
    TooManyAttempts,
    Success,
}

public sealed record OtpVerifyResult(
    OtpVerifyOutcome Outcome,
    string? DownloadToken = null,
    int? RemainingAttempts = null,
    LinkHealthStatus? UnhealthyReason = null,
    DateTime? LockedUntil = null);
