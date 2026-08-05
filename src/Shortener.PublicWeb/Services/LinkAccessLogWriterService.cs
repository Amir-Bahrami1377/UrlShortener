using Microsoft.EntityFrameworkCore;
using Shortener.Application.Abstractions;
using Shortener.Domain.Entities;
using Shortener.Infrastructure.Persistence;
using Shortener.Infrastructure.Services;

namespace Shortener.PublicWeb.Services;

/// <summary>Drains LinkAccessLogChannel and bulk-inserts every 2 seconds or 500 entries (§M4.7).</summary>
public sealed class LinkAccessLogWriterService(
    LinkAccessLogChannel channel, IServiceScopeFactory scopeFactory, ILogger<LinkAccessLogWriterService> logger) : BackgroundService
{
    private const int BatchSize = 500;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<LinkAccessLogEntry>(BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            flushCts.CancelAfter(FlushInterval);

            try
            {
                while (batch.Count < BatchSize)
                {
                    batch.Add(await channel.Reader.ReadAsync(flushCts.Token));
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Flush window elapsed with fewer than BatchSize entries — flush whatever we have.
            }

            if (batch.Count > 0)
            {
                await FlushAsync(batch, stoppingToken);
                batch.Clear();
            }
        }
    }

    private async Task FlushAsync(List<LinkAccessLogEntry> batch, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.LinkAccessLogs.AddRange(batch.Select(e => new LinkAccessLog
            {
                ShortLinkId = e.ShortLinkId,
                AccessType = e.AccessType,
                IpAddress = e.IpAddress,
                UserAgent = e.UserAgent,
                IsSuccess = e.IsSuccess,
                ErrorCode = e.ErrorCode,
            }));

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to flush {Count} LinkAccessLog entries", batch.Count);
        }
    }
}
