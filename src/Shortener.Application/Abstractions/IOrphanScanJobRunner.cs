namespace Shortener.Application.Abstractions;

/// <summary>§M7.4 — weekly bidirectional disk/DB reconciliation plus stale .tmp cleanup.</summary>
public interface IOrphanScanJobRunner
{
    Task<OrphanScanSummary> RunAsync(CancellationToken ct);
}

public sealed record OrphanScanSummary(
    int OrphanFilesDeleted,
    int MissingOnDiskFlagged,
    int TempFilesDeleted);
