using Shortener.Domain.Abstractions;

namespace Shortener.Domain.Entities;

public class Client : IHasCreatedAt
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Code { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
