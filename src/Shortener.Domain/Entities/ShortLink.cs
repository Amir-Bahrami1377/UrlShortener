using Shortener.Domain.Abstractions;

namespace Shortener.Domain.Entities;

public class ShortLink : IHasCreatedAt
{
    public long Id { get; set; }

    /// <summary>The public 6-character Base62 code.</summary>
    public required string Code { get; set; }

    /// <summary>Server-assigned idempotent identifier for this upload request (GUID v7).</summary>
    public Guid RequestId { get; set; }

    /// <summary>Caller-supplied correlation id, echoed back in tracking responses.</summary>
    public string? ClientRequestId { get; set; }

    public long StoredFileId { get; set; }
    public StoredFile? StoredFile { get; set; }

    public int ClientId { get; set; }
    public Client? Client { get; set; }

    public int? ApiKeyId { get; set; }
    public ApiKey? ApiKey { get; set; }

    public required string Shop { get; set; }
    public required string Shod { get; set; }
    public required string Radif { get; set; }
    public int ReportId { get; set; }
    public required string ReportName { get; set; }
    public required string PhoneNumber { get; set; }
    public string? BatchTag { get; set; }

    /// <summary>Kept in lockstep with StoredFile.FileExpiresAt.</summary>
    public DateTime ExpiresAt { get; set; }

    public int DownloadCount { get; set; }
    public int OtpRequestCount { get; set; }
    public DateTime? LastOtpRequestedAt { get; set; }
    public int FailedVerifyCount { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? FirstVerifiedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
