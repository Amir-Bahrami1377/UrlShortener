using Shortener.Domain.Enums;

namespace Shortener.Application.Abstractions;

/// <summary>Cache-aside lookup of the mostly-static link fields (§M4.1/M4.2). Deliberately excludes
/// LockedUntil and the OTP/download counters — those are security-critical and mutate often, so
/// callers read them fresh from SQL at the point they matter (§M4.3/M4.4) rather than through a
/// 60-minute cache.</summary>
public interface IShortLinkStateResolver
{
    Task<ShortLinkState?> ResolveAsync(string code, CancellationToken ct);
}

public enum LinkHealthStatus
{
    Inactive,
    Expired,
    FileDeleted,
    Healthy,
}

public sealed record ShortLinkState(
    long ShortLinkId,
    int ClientId,
    string Code,
    string ReportName,
    string PhoneNumber,
    bool IsActive,
    DateTime ExpiresAt,
    FileStatus FileStatus,
    string StorageKey,
    string ContentType,
    string Extension)
{
    /// <summary>Ordered per §M4.2's table — not-found is handled separately since it means no state at all.</summary>
    public LinkHealthStatus GetHealthStatus(DateTime utcNow) =>
        !IsActive ? LinkHealthStatus.Inactive
        : ExpiresAt < utcNow ? LinkHealthStatus.Expired
        : FileStatus == FileStatus.Deleted ? LinkHealthStatus.FileDeleted
        : LinkHealthStatus.Healthy;
}
