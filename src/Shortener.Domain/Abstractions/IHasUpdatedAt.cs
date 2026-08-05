namespace Shortener.Domain.Abstractions;

public interface IHasUpdatedAt
{
    DateTime UpdatedAt { get; set; }
}
