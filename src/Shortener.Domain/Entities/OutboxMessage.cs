using Shortener.Domain.Abstractions;
using Shortener.Domain.Enums;

namespace Shortener.Domain.Entities;

public class OutboxMessage : IHasCreatedAt
{
    /// <summary>GUID v7 so ids are roughly time-ordered.</summary>
    public Guid Id { get; set; }

    public required string Type { get; set; }
    public required string PayloadJson { get; set; }
    public OutboxStatus Status { get; set; }
    public int TryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? Error { get; set; }
}
