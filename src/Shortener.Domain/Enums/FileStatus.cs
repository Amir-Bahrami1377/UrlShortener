namespace Shortener.Domain.Enums;

public enum FileStatus : byte
{
    Active = 0,
    Expired = 1,
    PendingDelete = 2,
    Deleted = 3
}
