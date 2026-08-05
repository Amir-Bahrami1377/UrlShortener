using Shortener.Domain.Enums;

namespace Shortener.Domain.Entities;

public class StoredFile
{
    public long Id { get; set; }
    public Guid FileGuid { get; set; }

    public int ClientId { get; set; }
    public Client? Client { get; set; }

    public int? RetentionPolicyId { get; set; }
    public RetentionPolicy? RetentionPolicy { get; set; }

    /// <summary>Path relative to FileStorage:RootPath, e.g. {yyyy}/{MM}/{dd}/{shard}/{guid}.bin</summary>
    public required string StorageKey { get; set; }

    public required string OriginalFileName { get; set; }
    public required string ContentType { get; set; }
    public required string Extension { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>SHA256 hex digest computed while streaming the upload to disk.</summary>
    public required string Sha256 { get; set; }

    public FileStatus Status { get; set; }
    public DateTime StoredAt { get; set; }
    public DateTime FileExpiresAt { get; set; }
    public DateTime? PendingDeleteAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
