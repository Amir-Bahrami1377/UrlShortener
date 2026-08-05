using Shortener.Domain.Abstractions;

namespace Shortener.Domain.Entities;

public class ApiKey : IHasCreatedAt
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client? Client { get; set; }

    /// <summary>SHA256 hex digest of the raw key. The raw key itself is never stored.</summary>
    public required string KeyHash { get; set; }

    /// <summary>First characters of the raw key, kept only for display in the admin panel.</summary>
    public required string KeyPrefix { get; set; }

    public required string Title { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
