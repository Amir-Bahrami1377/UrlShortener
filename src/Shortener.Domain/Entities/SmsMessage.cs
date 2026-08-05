using Shortener.Domain.Abstractions;
using Shortener.Domain.Enums;

namespace Shortener.Domain.Entities;

public class SmsMessage : IHasCreatedAt
{
    public long Id { get; set; }

    public long? ShortLinkId { get; set; }
    public ShortLink? ShortLink { get; set; }

    public int SmsAccountId { get; set; }
    public SmsAccount? SmsAccount { get; set; }

    public int? TemplateId { get; set; }
    public MessageTemplate? Template { get; set; }

    public SmsMessageType MessageType { get; set; }
    public required string PhoneNumber { get; set; }

    /// <summary>Fully rendered text (placeholders already substituted).</summary>
    public required string Body { get; set; }

    /// <summary>Snapshot of the template's PatternCode at send-time, same as Body — not a live read of
    /// Template.PatternCode, so an edit to the template can't retroactively change an already-queued
    /// message. Null means send Body as free text.</summary>
    public string? PatternCode { get; set; }

    /// <summary>The raw placeholder values (same dictionary used to render Body), serialized so
    /// pattern-based providers can pass them positionally instead of a rendered string.</summary>
    public string? PatternTokensJson { get; set; }

    public SmsStatus Status { get; set; }
    public string? ProviderMessageId { get; set; }
    public int TryCount { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public string? LastError { get; set; }
    public decimal? Cost { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? StatusCheckedAt { get; set; }
}
