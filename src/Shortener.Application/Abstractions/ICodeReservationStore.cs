namespace Shortener.Application.Abstractions;

/// <summary>Short-lived, atomic reservation used to avoid two concurrent requests picking the same short code.</summary>
public interface ICodeReservationStore
{
    /// <summary>Returns true only if this call is the one that reserved the code.</summary>
    Task<bool> TryReserveAsync(string code, TimeSpan ttl, CancellationToken ct);
}
