using Microsoft.EntityFrameworkCore;
using Shortener.Infrastructure.Persistence;
using Shortener.Infrastructure.Services;

namespace Shortener.Api.Authentication;

/// <summary>Drains ApiKeyUsageChannel and bulk-updates LastUsedAt out of the request path (§M3.1.7).</summary>
public sealed class ApiKeyUsageUpdaterService(
    ApiKeyUsageChannel channel,
    IServiceScopeFactory scopeFactory,
    ILogger<ApiKeyUsageUpdaterService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var apiKeyId in channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.ApiKeys
                    .Where(k => k.Id == apiKeyId)
                    .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, DateTime.UtcNow), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to update LastUsedAt for ApiKey {ApiKeyId}", apiKeyId);
            }
        }
    }
}
