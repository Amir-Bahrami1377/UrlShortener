using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shortener.Admin.Models;
using Shortener.Application.Services;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;

namespace Shortener.Admin.Controllers;

public partial class ReportsController
{
    [HttpGet]
    public async Task<IActionResult> Sms(SmsReportFilter filter, CancellationToken ct)
    {
        var query = BuildSmsQuery(filter);
        var totalCount = await query.CountAsync(ct);

        var aggregates = await query
            .GroupBy(m => new { ClientName = m.SmsAccount!.Client!.Name, m.MessageType })
            .Select(g => new SmsAggregateRow
            {
                ClientName = g.Key.ClientName,
                MessageType = g.Key.MessageType.ToString(),
                Count = g.Count(),
                TotalCost = g.Sum(m => m.Cost) ?? 0,
            })
            .OrderBy(a => a.ClientName).ThenBy(a => a.MessageType)
            .ToListAsync(ct);

        var rows = await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(PageSize)
            .Select(m => new
            {
                m.Id,
                ClientName = m.SmsAccount!.Client!.Name,
                m.MessageType,
                m.PhoneNumber,
                m.Status,
                m.Cost,
                m.CreatedAt,
                m.SentAt,
                m.DeliveredAt,
                m.LastError,
            })
            .ToListAsync(ct);

        var model = new SmsReportViewModel
        {
            Filter = filter,
            Clients = await db.Clients.OrderBy(c => c.Name).Select(c => new ValueTuple<int, string>(c.Id, c.Name)).ToListAsync(ct),
            TotalCount = totalCount,
            Aggregates = aggregates,
            Rows = rows.Select(r => new SmsReportRow
            {
                Id = r.Id,
                ClientName = r.ClientName,
                MessageType = r.MessageType.ToString(),
                PhoneNumber = MaskPhone(r.PhoneNumber),
                Status = r.Status.ToString(),
                Cost = r.Cost,
                CreatedAt = r.CreatedAt,
                SentAt = r.SentAt,
                DeliveredAt = r.DeliveredAt,
                LastError = r.LastError,
                CanResend = r.Status == SmsStatus.Failed && r.MessageType == SmsMessageType.DownloadLink,
            }).ToList(),
        };

        return View(model);
    }

    /// <summary>
    /// §M6.3 — manual resend, DownloadLink only (resending an Otp is meaningless: its code has
    /// already expired). Since the Outbox publisher/BulkSmsWorker aren't built yet this session,
    /// this queues the message the same way OtpSmsWorker's stream entries look, so whichever
    /// BulkSmsWorker eventually consumes sms:bulk will pick it straight up.
    /// </summary>
    [Authorize(Policy = AdminPolicies.OperatorOrAbove)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendSms(long id, CancellationToken ct)
    {
        var message = await db.SmsMessages.FindAsync([id], ct);
        if (message is null)
        {
            return NotFound();
        }

        if (message.Status != SmsStatus.Failed || message.MessageType != SmsMessageType.DownloadLink)
        {
            TempData["StatusMessage"] = "فقط پیامک‌های لینک با وضعیت ناموفق قابل ارسال مجدد هستند.";
            return RedirectToAction(nameof(Sms));
        }

        message.Status = SmsStatus.Queued;
        message.LastError = null;
        await db.SaveChangesAsync(ct);

        await redis.GetDatabase().StreamAddAsync("sms:bulk", "smsMessageId", message.Id);

        TempData["StatusMessage"] = "پیام برای ارسال مجدد در صف قرار گرفت.";
        return RedirectToAction(nameof(Sms));
    }

    private IQueryable<SmsMessage> BuildSmsQuery(SmsReportFilter filter)
    {
        var query = db.SmsMessages.AsQueryable();

        var fromUtc = PersianDateHelper.ParsePersianDate(filter.FromDate);
        var toUtc = PersianDateHelper.ParsePersianDate(filter.ToDate);
        if (fromUtc is not null)
        {
            query = query.Where(m => m.CreatedAt >= fromUtc);
        }

        if (toUtc is not null)
        {
            var toExclusive = toUtc.Value.AddDays(1);
            query = query.Where(m => m.CreatedAt < toExclusive);
        }

        if (filter.ClientId is not null)
        {
            query = query.Where(m => m.SmsAccount!.ClientId == filter.ClientId);
        }

        if (filter.SmsAccountId is not null)
        {
            query = query.Where(m => m.SmsAccountId == filter.SmsAccountId);
        }

        if (!string.IsNullOrWhiteSpace(filter.MessageType) && Enum.TryParse<SmsMessageType>(filter.MessageType, out var type))
        {
            query = query.Where(m => m.MessageType == type);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status) && Enum.TryParse<SmsStatus>(filter.Status, out var status))
        {
            query = query.Where(m => m.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.PhoneNumber))
        {
            query = query.Where(m => m.PhoneNumber == filter.PhoneNumber);
        }

        return query;
    }
}
