namespace Shortener.Infrastructure.Retention;

/// <summary>Pure, testable window math shared by RetentionJobRunner (mid-run re-checks) and the
/// Worker's MaintenanceScheduler (deciding when to sleep vs. attempt a run).</summary>
public static class JobWindow
{
    public static bool IsWithin(DateTime tehranLocalNow, RetentionJobOptions options) =>
        tehranLocalNow.Hour >= options.JobWindowStartHour && tehranLocalNow.Hour < options.JobWindowEndHour;

    /// <summary>How long to sleep from <paramref name="nowLocal"/> until the next window start.
    /// Always at least 1 second, so callers never hand Task.Delay a non-positive duration.</summary>
    public static TimeSpan TimeUntilNextWindow(DateTime nowLocal, int windowStartHour)
    {
        var todayStart = nowLocal.Date.AddHours(windowStartHour);
        var target = nowLocal < todayStart ? todayStart : todayStart.AddDays(1);
        var delay = target - nowLocal;
        return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
    }
}
