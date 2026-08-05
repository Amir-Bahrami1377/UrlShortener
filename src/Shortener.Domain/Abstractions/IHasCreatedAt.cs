namespace Shortener.Domain.Abstractions;

public interface IHasCreatedAt
{
    DateTime CreatedAt { get; set; }
}
