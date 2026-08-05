using Shortener.Domain.Abstractions;
using Shortener.Domain.Enums;

namespace Shortener.Domain.Entities;

public class LinkAccessLog : IHasCreatedAt
{
    public long Id { get; set; }

    public long ShortLinkId { get; set; }
    public ShortLink? ShortLink { get; set; }

    public LinkAccessType AccessType { get; set; }
    public required string IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime CreatedAt { get; set; }
}
