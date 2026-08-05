namespace Shortener.Admin.Models;

public sealed class DashboardViewModel
{
    public int LinksCreated { get; set; }
    public double ClickRatePercent { get; set; }
    public double DownloadSuccessRatePercent { get; set; }
    public int LinkSmsSent { get; set; }
    public int OtpSmsSent { get; set; }
    public double DeliveryRatePercent { get; set; }
    public int FailedSmsCount { get; set; }
    public long SpaceUsedBytes { get; set; }
    public int FilesNearingExpiry { get; set; }
    public double? OtpQueueLagSeconds { get; set; }
    public int OtpQueuePendingCount { get; set; }
    public double DiskUsedPercent { get; set; }
    public double DiskWarningThresholdPercent { get; set; }
    public double DiskRejectThresholdPercent { get; set; }
    public int MissingOnDiskAlerts { get; set; }
    public List<DailyTrendPoint> Trend { get; set; } = [];
}

public sealed record DailyTrendPoint(DateTime DateUtc, int LinksCreated, int Downloads);
