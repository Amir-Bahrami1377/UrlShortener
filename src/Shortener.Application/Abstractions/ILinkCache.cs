using Shortener.Domain.Enums;

namespace Shortener.Application.Abstractions;

/// <summary>Cache-aside over `link:{code}` (§M4.1). A null result means "read from SQL" —
/// callers can't tell a true cache miss from Redis being down, and shouldn't need to.</summary>
public interface ILinkCache
{
    Task<LinkCacheEntry?> GetAsync(string code, CancellationToken ct);

    Task SetAsync(string code, LinkCacheEntry entry, CancellationToken ct);

    Task InvalidateAsync(string code, CancellationToken ct);
}

public sealed record LinkCacheEntry(
    long ShortLinkId,
    long StoredFileId,
    string ReportName,
    string PhoneNumber,
    DateTime ExpiresAt,
    bool IsActive,
    FileStatus FileStatus,
    string StorageKey,
    string ContentType,
    string Extension,
    int ClientId);
