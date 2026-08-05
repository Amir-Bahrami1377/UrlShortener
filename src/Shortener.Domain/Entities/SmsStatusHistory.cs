using Shortener.Domain.Abstractions;
using Shortener.Domain.Enums;

namespace Shortener.Domain.Entities;

public class SmsStatusHistory : IHasCreatedAt
{
    public long Id { get; set; }

    public long SmsMessageId { get; set; }
    public SmsMessage? SmsMessage { get; set; }

    public SmsStatus Status { get; set; }
    public string? ProviderStatusCode { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}
