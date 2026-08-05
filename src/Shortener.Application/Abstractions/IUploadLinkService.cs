using Shortener.Application.Contracts;

namespace Shortener.Application.Abstractions;

/// <summary>Orchestrates §M3.2's upload algorithm (steps 3, 6-11) once the HTTP layer has parsed
/// the multipart request. Split from parsing so multipart mechanics stay in the web project.</summary>
public interface IUploadLinkService
{
    /// <summary>Idempotency check (step 3): looks up (ClientId, Shop, Shod, Radif, ReportId) among active links.</summary>
    Task<LinkOperationResult?> FindExistingAsync(int clientId, UploadLinkMetadata metadata, CancellationToken ct);

    /// <summary>Saves the file and performs the transactional insert (steps 5-11). On any failure after
    /// the file is written, the file is deleted so it never becomes an orphan.</summary>
    Task<LinkOperationResult> CreateAsync(
        int clientId, int? apiKeyId, Guid requestId, UploadLinkMetadata metadata,
        Stream fileStream, string extension, string originalFileName, CancellationToken ct);
}

public sealed record LinkOperationResult(
    Guid RequestId,
    string? ClientRequestId,
    string Code,
    DateTime FileExpiresAt,
    string SmsStatus,
    bool IsDuplicate);
