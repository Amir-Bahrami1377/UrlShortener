using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shortener.Infrastructure.Retention;

namespace Shortener.Infrastructure.Observability;

/// <summary>
/// §M8.3 — /health/live (no dependency checks — just "is the process responding") and
/// /health/ready (every registered "ready"-tagged check). Api and Worker get the full set (SQL,
/// Redis, disk, OTP queue lag, per-client OTP account coverage); Admin and PublicWeb get the core
/// SQL+Redis pair only — the operational checks are Api/Worker's own concerns.
/// </summary>
public static class HealthCheckSetup
{
    private const string ReadyTag = "ready";

    public static IHealthChecksBuilder AddCoreHealthChecks(this IServiceCollection services, IConfiguration configuration) =>
        services.AddHealthChecks()
            .AddSqlServer(configuration.GetConnectionString("Default")!, tags: [ReadyTag])
            .AddRedis(configuration.GetConnectionString("Redis")!, tags: [ReadyTag]);

    public static IHealthChecksBuilder AddOperationalHealthChecks(this IHealthChecksBuilder builder) =>
        builder
            .AddCheck<DiskSpaceHealthCheck>("disk-space", tags: [ReadyTag])
            .AddCheck<OtpQueueLagHealthCheck>("otp-queue-lag", tags: [ReadyTag])
            .AddCheck<OtpAccountCoverageHealthCheck>("otp-account-coverage", tags: [ReadyTag]);

    public static void MapSystemHealthChecks(this IEndpointRouteBuilder app)
    {
        // AllowAnonymous unconditionally: Admin sets a global RequireAuthenticatedUser() fallback
        // policy, and a load balancer probing these endpoints will never carry a session cookie.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) }).AllowAnonymous();
    }
}
