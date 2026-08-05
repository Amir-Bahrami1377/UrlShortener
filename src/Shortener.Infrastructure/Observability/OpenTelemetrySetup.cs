using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace Shortener.Infrastructure.Observability;

/// <summary>§M8.2 — registers the Shortener Meter with OpenTelemetry's SDK and exposes it via a
/// Prometheus-format scrape endpoint. No external collector/backend is available in this sandbox,
/// so Prometheus's pull model (a plain HTTP endpoint, nothing to stand up or configure) is what's
/// actually verifiable here; swapping in an OTLP exporter for a real backend later is a one-line change.</summary>
public static class OpenTelemetrySetup
{
    public static IServiceCollection AddShortenerMetrics(this IServiceCollection services)
    {
        services.AddSingleton<ShortenerMetrics>();
        services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(ShortenerMetrics.MeterName)
                .AddPrometheusExporter());

        return services;
    }

    public static void MapShortenerMetrics(this IEndpointRouteBuilder app) =>
        app.MapPrometheusScrapingEndpoint("/metrics").AllowAnonymous();
}
