using Shortener.Domain.Enums;

namespace Shortener.Application.Abstractions;

/// <summary>Fire-and-forget access logging (§M4.7) — never written on the request path.</summary>
public interface ILinkAccessLogger
{
    void Enqueue(LinkAccessLogEntry entry);
}

public sealed record LinkAccessLogEntry(
    long ShortLinkId, LinkAccessType AccessType, string IpAddress, string? UserAgent, bool IsSuccess, string? ErrorCode);
