using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shortener.Admin.Models;
using Shortener.Application.Services;
using Shortener.Domain.Enums;

namespace Shortener.Admin.Controllers;

public partial class ReportsController
{
    [HttpGet]
    public async Task<IActionResult> Storage(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-12);
        var files = await db.StoredFiles
            .Where(f => f.Status != FileStatus.Deleted && f.StoredAt >= cutoff)
            .Select(f => new { f.ClientId, f.StoredAt, f.SizeBytes })
            .ToListAsync(ct);

        var clientNames = await db.Clients.ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        // Grouping by Persian month has to happen in memory — PersianCalendar can't be translated to SQL.
        var usageByClientAndMonth = files
            .GroupBy(f => (f.ClientId, Month: PersianDateHelper.ToPersianDate(f.StoredAt)[..7]))
            .Select(g => new ClientMonthUsage(
                clientNames.GetValueOrDefault(g.Key.ClientId, "؟"), g.Key.Month, g.Sum(f => f.SizeBytes), g.Count()))
            .OrderByDescending(u => u.PersianMonth).ThenBy(u => u.ClientName)
            .ToList();

        var countByStatus = (await db.StoredFiles
                .GroupBy(f => f.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(g => g.Status.ToString(), g => g.Count);

        var now = DateTime.UtcNow;
        var forecastFiles = await db.StoredFiles
            .Where(f => f.Status == FileStatus.Active && f.FileExpiresAt >= now && f.FileExpiresAt <= now.AddDays(30))
            .Select(f => f.SizeBytes)
            .ToListAsync(ct);

        var recentDeletions = await db.FileDeletionLogs
            .OrderByDescending(l => l.CreatedAt)
            .Take(50)
            .Select(l => new DeletionLogRow(l.CreatedAt, l.StorageKey, l.SizeBytes, l.Reason.ToString(), l.PhysicalDeleteOk))
            .ToListAsync(ct);

        return View(new StorageReportViewModel
        {
            UsageByClientAndMonth = usageByClientAndMonth,
            CountByStatus = countByStatus,
            ForecastFileCount30Days = forecastFiles.Count,
            ForecastBytes30Days = forecastFiles.Sum(),
            RecentDeletions = recentDeletions,
        });
    }
}
