namespace Shortener.Admin.Models;

public sealed class LinksReportFilter
{
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public int? ClientId { get; set; }
    public int? ReportId { get; set; }
    public string? BatchTag { get; set; }
    public string? Status { get; set; }
    public string? RequestId { get; set; }
    public string? ClientRequestId { get; set; }
    public string? Shop { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Code { get; set; }
}

public sealed class LinksReportViewModel
{
    public LinksReportFilter Filter { get; set; } = new();
    public List<(int Id, string Name)> Clients { get; set; } = [];
    public List<LinksReportRow> Rows { get; set; } = [];
    public int TotalCount { get; set; }
}

public sealed class LinksReportRow
{
    public string Code { get; set; } = string.Empty;
    public Guid RequestId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string Shop { get; set; } = string.Empty;
    public string Shod { get; set; } = string.Empty;
    public string Radif { get; set; } = string.Empty;
    public string ReportName { get; set; } = string.Empty;
    public string MaskedPhone { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int OtpRequestCount { get; set; }
    public int DownloadCount { get; set; }
    public string SmsStatus { get; set; } = string.Empty;
    public string FileStatus { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class LinkDetailViewModel
{
    public LinksReportRow Summary { get; set; } = null!;
    public List<TimelineEntry> Timeline { get; set; } = [];
}

public sealed record TimelineEntry(DateTime AtUtc, string Kind, string Description, bool IsSuccess);
