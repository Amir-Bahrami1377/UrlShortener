using Shortener.Domain.Abstractions;

namespace Shortener.Domain.Entities;

/// <summary>Client-defined catalog of named "print" types (اسم چاپ), each mapped to the numeric
/// ReportId (کد چاپ) that the client's own external system sends on uploads and that
/// <see cref="MessageTemplate.ReportId"/> / <see cref="RetentionPolicy.ReportId"/> scope against.</summary>
public class PrintDefinition : IHasCreatedAt, IHasUpdatedAt
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client? Client { get; set; }

    public int Code { get; set; }
    public required string Name { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
