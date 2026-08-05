namespace Shortener.Admin.Models;

public sealed class SmsReportFilter
{
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public int? ClientId { get; set; }
    public int? SmsAccountId { get; set; }
    public string? MessageType { get; set; }
    public string? Status { get; set; }
    public string? PhoneNumber { get; set; }
}

public sealed class SmsReportViewModel
{
    public SmsReportFilter Filter { get; set; } = new();
    public List<(int Id, string Name)> Clients { get; set; } = [];
    public List<SmsAggregateRow> Aggregates { get; set; } = [];
    public List<SmsReportRow> Rows { get; set; } = [];
    public int TotalCount { get; set; }
}

public sealed class SmsAggregateRow
{
    public string ClientName { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal TotalCost { get; set; }
}

public sealed class SmsReportRow
{
    public long Id { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? Cost { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string? LastError { get; set; }
    public bool CanResend { get; set; }
}
