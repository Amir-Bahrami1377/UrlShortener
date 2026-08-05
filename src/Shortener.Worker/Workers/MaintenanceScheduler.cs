using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shortener.Application.Abstractions;
using Shortener.Application.Services;
using Shortener.Infrastructure.Retention;

namespace Shortener.Worker.Workers;

/// <summary>
/// §M7.6 — sleeps until the next low-load window instead of polling, per the doc's explicit
/// "no need for Hangfire" guidance. Mutual exclusion across multiple Worker instances is the
/// Redis NX/EX lock (IDistributedLock); "already ran today/this week" is tracked in-memory per
/// instance, which is enough here since each job's own DB queries are idempotent (a re-run finds
/// nothing left to do) — a mid-window restart double-triggering is harmless, not incorrect.
/// </summary>
public sealed class MaintenanceScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<RetentionJobOptions> options,
    ILogger<MaintenanceScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan LockTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan InWindowPollInterval = TimeSpan.FromMinutes(5);

    private DateOnly? _lastRetentionRunDate;
    private DateOnly? _lastOrphanScanRunDate;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = options.Value;
            var nowLocal = PersianDateHelper.Now();

            if (JobWindow.IsWithin(nowLocal, opts))
            {
                var today = DateOnly.FromDateTime(nowLocal);

                if (_lastRetentionRunDate != today)
                {
                    await TryRunAsync("retention-job", RunRetentionAsync, stoppingToken);
                    _lastRetentionRunDate = today;
                }

                if (nowLocal.DayOfWeek == opts.OrphanScanDayOfWeek && _lastOrphanScanRunDate != today)
                {
                    await TryRunAsync("orphan-scan-job", RunOrphanScanAsync, stoppingToken);
                    _lastOrphanScanRunDate = today;
                }

                await Task.Delay(InWindowPollInterval, stoppingToken);
            }
            else
            {
                await Task.Delay(JobWindow.TimeUntilNextWindow(nowLocal, opts.JobWindowStartHour), stoppingToken);
            }
        }
    }

    private async Task TryRunAsync(
        string lockName, Func<IServiceProvider, CancellationToken, Task> run, CancellationToken ct)
    {
        // IDistributedLock is Scoped (it's registered alongside the DbContext-dependent services),
        // so it — like the job runners themselves — has to be resolved from a scope rather than
        // injected into this Singleton BackgroundService's own constructor.
        using var scope = scopeFactory.CreateScope();
        var distributedLock = scope.ServiceProvider.GetRequiredService<IDistributedLock>();

        var handle = await distributedLock.TryAcquireAsync(lockName, LockTtl, ct);
        if (handle is null)
        {
            logger.LogInformation("Skipping {LockName}: another Worker instance already holds the lock", lockName);
            return;
        }

        await using (handle)
        {
            try
            {
                logger.LogInformation("Starting {LockName}", lockName);
                await run(scope.ServiceProvider, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{LockName} failed", lockName);
            }
        }
    }

    private static Task RunRetentionAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IRetentionJobRunner>().RunAsync(ct);

    private static Task RunOrphanScanAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IOrphanScanJobRunner>().RunAsync(ct);
}
