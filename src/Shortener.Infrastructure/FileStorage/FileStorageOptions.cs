namespace Shortener.Infrastructure.FileStorage;

public sealed class FileStorageOptions
{
    public required string RootPath { get; set; }
    public long MaxFileSizeBytes { get; set; } = 3 * 1024 * 1024;
    public List<string> AllowedExtensions { get; set; } = [];
    public int TempFileMaxAgeHours { get; set; } = 6;
    public double DiskWarningThresholdPercent { get; set; } = 70;
    public double DiskRejectThresholdPercent { get; set; } = 90;
}
