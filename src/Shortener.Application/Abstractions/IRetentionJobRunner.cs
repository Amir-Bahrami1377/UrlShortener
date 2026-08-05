namespace Shortener.Application.Abstractions;

/// <summary>§M7.1-M7.3 — nightly expiration, physical deletion, and empty-folder cleanup, run as one
/// unit inside the low-load window so a single distributed-lock acquisition covers all three.</summary>
public interface IRetentionJobRunner
{
    Task<RetentionRunSummary> RunAsync(CancellationToken ct);
}

public sealed record RetentionRunSummary(
    int Expired,
    int LinksDeactivated,
    int GraduatedToPendingDelete,
    int PhysicallyDeleted,
    int PhysicalDeleteFailures,
    int EmptyFoldersRemoved,
    bool StoppedEarlyDueToWindow);
