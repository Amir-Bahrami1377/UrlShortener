using Shortener.Domain.Abstractions;

namespace Shortener.Domain.Entities;

public class AuditLog : IHasCreatedAt
{
    public long Id { get; set; }

    /// <summary>Identity user id (nvarchar 450 to match AspNetUsers.Id).</summary>
    public required string UserId { get; set; }

    public required string EntityName { get; set; }
    public required string EntityId { get; set; }
    public required string Action { get; set; }

    /// <summary>Sensitive fields (credentials, etc.) must be masked before serializing here.</summary>
    public string? OldValueJson { get; set; }
    public string? NewValueJson { get; set; }

    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}
