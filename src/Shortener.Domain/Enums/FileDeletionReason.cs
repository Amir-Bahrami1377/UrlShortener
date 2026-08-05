namespace Shortener.Domain.Enums;

public enum FileDeletionReason : byte
{
    RetentionPolicy = 0,
    Orphan = 1,
    Manual = 2
}
