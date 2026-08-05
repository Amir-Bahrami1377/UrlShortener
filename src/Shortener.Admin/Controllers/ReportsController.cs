using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shortener.Admin.Models;
using Shortener.Application.Abstractions;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.FileStorage;
using Shortener.Infrastructure.Persistence;
using Shortener.Infrastructure.Retention;
using StackExchange.Redis;

namespace Shortener.Admin.Controllers;

/// <summary>§M6 — every authenticated role (including Viewer) can read reports; only specific
/// write actions (e.g. Sms/Resend) are further restricted to Operator/SuperAdmin.</summary>
[Authorize]
public partial class ReportsController(
    AppDbContext db, IConnectionMultiplexer redis, IFileStorage fileStorage, IOptions<FileStorageOptions> fileStorageOptions) : Controller
{
    private const string OtpStream = "sms:otp";
    private const string OtpConsumerGroup = "otp-workers";

    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        var periodStart = DateTime.UtcNow.AddDays(-7);
        var now = DateTime.UtcNow;

        var linksCreated = await db.ShortLinks.CountAsync(l => l.CreatedAt >= periodStart, ct);
        var viewCount = await db.LinkAccessLogs.CountAsync(a => a.CreatedAt >= periodStart && a.AccessType == LinkAccessType.View, ct);
        var downloadCount = await db.LinkAccessLogs.CountAsync(a => a.CreatedAt >= periodStart && a.AccessType == LinkAccessType.Download, ct);

        var smsByType = await db.SmsMessages
            .Where(m => m.CreatedAt >= periodStart)
            .GroupBy(m => m.MessageType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var sentOrBeyond = await db.SmsMessages.CountAsync(
            m => m.CreatedAt >= periodStart && m.Status >= SmsStatus.Sent && m.Status != SmsStatus.Cancelled, ct);
        var deliveredCount = await db.SmsMessages.CountAsync(m => m.CreatedAt >= periodStart && m.Status == SmsStatus.Delivered, ct);
        var failedCount = await db.SmsMessages.CountAsync(m => m.CreatedAt >= periodStart && m.Status == SmsStatus.Failed, ct);

        var spaceUsed = await db.StoredFiles.Where(f => f.Status != FileStatus.Deleted).SumAsync(f => (long?)f.SizeBytes, ct) ?? 0;
        var nearingExpiry = await db.StoredFiles.CountAsync(
            f => f.Status == FileStatus.Active && f.FileExpiresAt >= now && f.FileExpiresAt <= now.AddDays(7), ct);

        var trend = await BuildTrendAsync(periodStart, ct);
        var (lagSeconds, pendingCount) = await GetOtpQueueLagAsync();

        var diskSpace = fileStorage.GetDiskSpace();
        var missingOnDiskAlerts = await db.AuditLogs.CountAsync(
            a => a.Action == OrphanScanJobRunner.MissingOnDiskAuditAction && a.CreatedAt >= now.AddDays(-8), ct);

        var model = new DashboardViewModel
        {
            LinksCreated = linksCreated,
            ClickRatePercent = Percent(viewCount, linksCreated),
            DownloadSuccessRatePercent = Percent(downloadCount, viewCount),
            LinkSmsSent = smsByType.FirstOrDefault(s => s.Type == SmsMessageType.DownloadLink)?.Count ?? 0,
            OtpSmsSent = smsByType.FirstOrDefault(s => s.Type == SmsMessageType.Otp)?.Count ?? 0,
            DeliveryRatePercent = Percent(deliveredCount, sentOrBeyond),
            FailedSmsCount = failedCount,
            SpaceUsedBytes = spaceUsed,
            FilesNearingExpiry = nearingExpiry,
            OtpQueueLagSeconds = lagSeconds,
            OtpQueuePendingCount = pendingCount,
            DiskUsedPercent = diskSpace.UsedPercent,
            DiskWarningThresholdPercent = fileStorageOptions.Value.DiskWarningThresholdPercent,
            DiskRejectThresholdPercent = fileStorageOptions.Value.DiskRejectThresholdPercent,
            MissingOnDiskAlerts = missingOnDiskAlerts,
            Trend = trend,
        };

        return View(model);
    }

    private async Task<List<DailyTrendPoint>> BuildTrendAsync(DateTime periodStart, CancellationToken ct)
    {
        var trendStart = DateTime.UtcNow.Date.AddDays(-29);

        var links = await db.ShortLinks.Where(l => l.CreatedAt >= trendStart)
            .Select(l => l.CreatedAt.Date).ToListAsync(ct);
        var downloads = await db.LinkAccessLogs.Where(a => a.CreatedAt >= trendStart && a.AccessType == LinkAccessType.Download)
            .Select(a => a.CreatedAt.Date).ToListAsync(ct);

        _ = periodStart;
        return Enumerable.Range(0, 30)
            .Select(offset => trendStart.AddDays(offset))
            .Select(day => new DailyTrendPoint(day, links.Count(d => d == day), downloads.Count(d => d == day)))
            .ToList();
    }

    private async Task<(double? LagSeconds, int PendingCount)> GetOtpQueueLagAsync()
    {
        try
        {
            var db2 = redis.GetDatabase();
            var pending = await db2.StreamPendingMessagesAsync(OtpStream, OtpConsumerGroup, 1, RedisValue.Null, "-", "+");
            if (pending.Length == 0)
            {
                return (0, 0);
            }

            var summary = await db2.StreamPendingAsync(OtpStream, OtpConsumerGroup);
            return (pending[0].IdleTimeInMilliseconds / 1000.0, (int)summary.PendingMessageCount);
        }
        catch (RedisServerException)
        {
            // Consumer group doesn't exist yet (Worker never started against this Redis) — no data, not an error.
            return (null, 0);
        }
    }

    private static double Percent(int numerator, int denominator) => denominator == 0 ? 0 : Math.Round(numerator * 100.0 / denominator, 1);
}
