using Shortener.Domain.Abstractions;
using Shortener.Domain.Enums;

namespace Shortener.Domain.Entities;

public class FileDeletionLog : IHasCreatedAt
{
    public long Id { get; set; }

    /// <summary>Null for §M7.4 disk→DB orphans — files deleted from disk that never had a StoredFiles row.</summary>
    public long? StoredFileId { get; set; }
    public StoredFile? StoredFile { get; set; }

    public required string StorageKey { get; set; }
    public long SizeBytes { get; set; }
    public FileDeletionReason Reason { get; set; }
    public required string TriggeredBy { get; set; }
    public bool PhysicalDeleteOk { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}
