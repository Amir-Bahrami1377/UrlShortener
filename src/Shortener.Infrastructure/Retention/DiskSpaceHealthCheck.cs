using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Shortener.Application.Abstractions;
using Shortener.Infrastructure.FileStorage;

namespace Shortener.Infrastructure.Retention;

/// <summary>§M7.5 — mirrors the >90% reject threshold already enforced synchronously on upload
/// (LinksEndpoints returns 507 directly); this is the same data surfaced through /health.</summary>
public sealed class DiskSpaceHealthCheck(IFileStorage fileStorage, IOptions<FileStorageOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var space = fileStorage.GetDiskSpace();
        var opts = options.Value;
        var data = new Dictionary<string, object>
        {
            ["usedPercent"] = Math.Round(space.UsedPercent, 1),
            ["freeBytes"] = space.FreeBytes,
        };

        if (space.UsedPercent > opts.DiskRejectThresholdPercent)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy($"فضای دیسک {space.UsedPercent:0.0}٪ پر است.", data: data));
        }

        if (space.UsedPercent >= opts.DiskWarningThresholdPercent)
        {
            return Task.FromResult(HealthCheckResult.Degraded($"فضای دیسک {space.UsedPercent:0.0}٪ پر است.", data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy($"فضای دیسک {space.UsedPercent:0.0}٪ استفاده شده.", data));
    }
}
