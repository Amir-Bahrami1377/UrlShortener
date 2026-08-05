using Shortener.Infrastructure.Queueing;

namespace Shortener.Worker.Workers;

/// <summary>§M5.6 — every 5 minutes, delegates a poll pass to DeliveryStatusPoller. The doc's
/// "optional DLR webhook" (POST /api/v1/sms/dlr/{providerCode}) isn't built — no live provider
/// account exists to register a callback URL with, and polling alone already satisfies the DoD
/// ("delivery status query correctly records Delivered").</summary>
public sealed class DeliveryStatusPollerService(
    IServiceScopeFactory scopeFactory, ILogger<DeliveryStatusPollerService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var poller = scope.ServiceProvider.GetRequiredService<DeliveryStatusPoller>();
                await poller.PollOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DeliveryStatusPollerService tick failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
