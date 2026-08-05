using Shortener.Domain.Abstractions;
using Shortener.Domain.Enums;

namespace Shortener.Domain.Entities;

public class MessageTemplate : IHasCreatedAt, IHasUpdatedAt
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client? Client { get; set; }

    /// <summary>Null means global for the client (used for the Otp template type).</summary>
    public int? ReportId { get; set; }

    public TemplateType TemplateType { get; set; }
    public required string Title { get; set; }

    /// <summary>Body containing placeholders such as {shortUrl}, {code}, {otp}, {otpMinutes}, ...</summary>
    public required string Body { get; set; }

    public string? PatternCode { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
