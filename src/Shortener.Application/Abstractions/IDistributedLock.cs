namespace Shortener.Application.Abstractions;

/// <summary>§M7.6 — `SET job:{name} {instanceId} NX EX ttl` so multiple Worker instances never run
/// the same scheduled job concurrently.</summary>
public interface IDistributedLock
{
    /// <summary>Returns null if another instance already holds the lock; otherwise a handle that
    /// releases the lock when disposed.</summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string name, TimeSpan ttl, CancellationToken ct);
}
