using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Formatting.Compact;

namespace Shortener.Infrastructure.Observability;

/// <summary>§M8.1 — one shared configuration for every host: Console (JSON in non-Development) +
/// daily-rolling File with 30-day retention, enriched with MachineName and (per-request, via
/// CorrelationIdMiddleware) CorrelationId/ClientId. Centralized so all four hosts stay consistent
/// instead of re-deriving slightly different setups.</summary>
public static class SerilogSetup
{
    private const int RetainedDays = 30;

    public static LoggerConfiguration Configure(
        LoggerConfiguration config, IConfiguration configuration, IHostEnvironment environment, string applicationName)
    {
        config
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithProperty("Application", applicationName);

        if (environment.IsDevelopment())
        {
            config.WriteTo.Console();
        }
        else
        {
            config.WriteTo.Console(new CompactJsonFormatter());
        }

        var logDirectory = configuration["Serilog:LogDirectory"];
        if (!string.IsNullOrWhiteSpace(logDirectory))
        {
            config.WriteTo.File(
                Path.Combine(logDirectory, $"{applicationName}-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedDays,
                shared: true);
        }

        return config;
    }
}
