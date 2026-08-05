namespace Shortener.Domain.Enums;

public enum OutboxStatus : byte
{
    Pending = 0,
    Published = 1,
    Failed = 2
}
