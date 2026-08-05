using Shortener.Domain.Abstractions;

namespace Shortener.Domain.Entities;

public class RetentionPolicy : IHasCreatedAt
{
    public int Id { get; set; }

    /// <summary>Null means global (applies to every client).</summary>
    public int? ClientId { get; set; }
    public Client? Client { get; set; }

    /// <summary>Null means every report type.</summary>
    public int? ReportId { get; set; }

    /// <summary>Matches a bulk-upload batch tag; takes precedence over Client/ReportId matches.</summary>
    public string? BatchTag { get; set; }

    public required string Title { get; set; }
    public int RetentionDays { get; set; } = 90;
    public int GraceDays { get; set; } = 7;

    /// <summary>Higher wins on ties within the same match level.</summary>
    public int Priority { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
