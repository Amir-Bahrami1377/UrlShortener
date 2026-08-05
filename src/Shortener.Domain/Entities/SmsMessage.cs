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
