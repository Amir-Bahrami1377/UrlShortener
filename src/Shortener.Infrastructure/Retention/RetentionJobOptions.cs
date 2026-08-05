namespace Shortener.Infrastructure.Retention;

/// <summary>§M7.1-M7.6 job tuning — bound from the "Retention" config section.</summary>
public sealed class RetentionJobOptions
{
    public int DefaultRetentionDays { get; set; } = 90;
    public int DefaultGraceDays { get; set; } = 7;
    public int DeleteBatchSize { get; set; } = 5000;
    public int DeleteBatchDelayMs { get; set; } = 500;
    public int JobWindowStartHour { get; set; } = 1;
    public int JobWindowEndHour { get; set; } = 5;

    /// <summary>Not in the doc's sample config — the doc only says OrphanScanJob runs "weekly"
    /// without naming a day. Friday is Iran's weekend, so it's the lowest-traffic choice.</summary>
    public DayOfWeek OrphanScanDayOfWeek { get; set; } = DayOfWeek.Friday;
}
