namespace Shortener.Admin.Models;

public sealed class StorageReportViewModel
{
    public List<ClientMonthUsage> UsageByClientAndMonth { get; set; } = [];
    public Dictionary<string, int> CountByStatus { get; set; } = [];
    public int ForecastFileCount30Days { get; set; }
    public long ForecastBytes30Days { get; set; }
    public List<DeletionLogRow> RecentDeletions { get; set; } = [];
}

public sealed record ClientMonthUsage(string ClientName, string PersianMonth, long TotalBytes, int FileCount);

public sealed record DeletionLogRow(DateTime AtUtc, string StorageKey, long SizeBytes, string Reason, bool Ok);
