using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shortener.Admin.Models;
using Shortener.Application.Services;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;

namespace Shortener.Admin.Controllers;

public partial class ReportsController
{
    private const int PageSize = 100;

    [HttpGet]
    public async Task<IActionResult> Links(LinksReportFilter filter, CancellationToken ct)
    {
        var query = BuildLinksQuery(filter);
        var totalCount = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(l => l.CreatedAt)
            .Take(PageSize)
            .Select(l => new
            {
                l.Code,
                l.RequestId,
                ClientName = l.Client!.Name,
                l.Shop,
                l.Shod,
                l.Radif,
                l.ReportName,
                l.PhoneNumber,
                l.CreatedAt,
                l.ExpiresAt,
                l.OtpRequestCount,
                l.DownloadCount,
                l.IsActive,
                FileStatus = l.StoredFile!.Status,
                LatestSmsStatus = db.SmsMessages
                    .Where(m => m.ShortLinkId == l.Id && m.MessageType == SmsMessageType.DownloadLink)
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => (SmsStatus?)m.Status)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var model = new LinksReportViewModel
        {
            Filter = filter,
            Clients = await db.Clients.OrderBy(c => c.Name).Select(c => new ValueTuple<int, string>(c.Id, c.Name)).ToListAsync(ct),
            TotalCount = totalCount,
            Rows = rows.Select(r => new LinksReportRow
            {
                Code = r.Code,
                RequestId = r.RequestId,
                ClientName = r.ClientName,
                Shop = r.Shop,
                Shod = r.Shod,
                Radif = r.Radif,
                ReportName = r.ReportName,
                MaskedPhone = MaskPhone(r.PhoneNumber),
                CreatedAt = r.CreatedAt,
                ExpiresAt = r.ExpiresAt,
                OtpRequestCount = r.OtpRequestCount,
                DownloadCount = r.DownloadCount,
                IsActive = r.IsActive,
                FileStatus = r.FileStatus.ToString(),
                SmsStatus = (r.LatestSmsStatus ?? SmsStatus.Pending).ToString(),
            }).ToList(),
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> LinkDetail(Guid requestId, CancellationToken ct)
    {
        var link = await db.ShortLinks.Include(l => l.Client).Include(l => l.StoredFile)
            .FirstOrDefaultAsync(l => l.RequestId == requestId, ct);
        if (link is null)
        {
            return NotFound();
        }

        var smsStatus = await db.SmsMessages
            .Where(m => m.ShortLinkId == link.Id && m.MessageType == SmsMessageType.DownloadLink)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => (SmsStatus?)m.Status)
            .FirstOrDefaultAsync(ct);

        var summary = new LinksReportRow
        {
            Code = link.Code,
            RequestId = link.RequestId,
            ClientName = link.Client!.Name,
            Shop = link.Shop,
            Shod = link.Shod,
            Radif = link.Radif,
            ReportName = link.ReportName,
            MaskedPhone = MaskPhone(link.PhoneNumber),
            CreatedAt = link.CreatedAt,
            ExpiresAt = link.ExpiresAt,
            OtpRequestCount = link.OtpRequestCount,
            DownloadCount = link.DownloadCount,
            IsActive = link.IsActive,
            FileStatus = link.StoredFile!.Status.ToString(),
            SmsStatus = (smsStatus ?? SmsStatus.Pending).ToString(),
        };

        var accessLogs = await db.LinkAccessLogs.Where(a => a.ShortLinkId == link.Id).ToListAsync(ct);
        var smsHistory = await db.SmsStatusHistories
            .Where(h => db.SmsMessages.Where(m => m.ShortLinkId == link.Id).Select(m => m.Id).Contains(h.SmsMessageId))
            .ToListAsync(ct);

        var timeline = accessLogs
            .Select(a => new TimelineEntry(a.CreatedAt, "دسترسی", DescribeAccess(a.AccessType, a.ErrorCode), a.IsSuccess))
            .Concat(smsHistory.Select(h => new TimelineEntry(h.CreatedAt, "پیامک", $"وضعیت: {h.Status} — {h.Description}", true)))
            .OrderBy(t => t.AtUtc)
            .ToList();

        return View(new LinkDetailViewModel { Summary = summary, Timeline = timeline });
    }

    private IQueryable<ShortLink> BuildLinksQuery(LinksReportFilter filter)
    {
        var query = db.ShortLinks.AsQueryable();

        var fromUtc = PersianDateHelper.ParsePersianDate(filter.FromDate);
        var toUtc = PersianDateHelper.ParsePersianDate(filter.ToDate);
        if (fromUtc is not null)
        {
            query = query.Where(l => l.CreatedAt >= fromUtc);
        }

        if (toUtc is not null)
        {
            var toExclusive = toUtc.Value.AddDays(1);
            query = query.Where(l => l.CreatedAt < toExclusive);
        }

        if (filter.ClientId is not null)
        {
            query = query.Where(l => l.ClientId == filter.ClientId);
        }

        if (filter.ReportId is not null)
        {
            query = query.Where(l => l.ReportId == filter.ReportId);
        }

        if (!string.IsNullOrWhiteSpace(filter.BatchTag))
        {
            query = query.Where(l => l.BatchTag == filter.BatchTag);
        }

        if (!string.IsNullOrWhiteSpace(filter.RequestId) && Guid.TryParse(filter.RequestId, out var requestIdValue))
        {
            query = query.Where(l => l.RequestId == requestIdValue);
        }

        if (!string.IsNullOrWhiteSpace(filter.ClientRequestId))
        {
            query = query.Where(l => l.ClientRequestId == filter.ClientRequestId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Shop))
        {
            query = query.Where(l => l.Shop == filter.Shop);
        }

        if (!string.IsNullOrWhiteSpace(filter.PhoneNumber))
        {
            query = query.Where(l => l.PhoneNumber == filter.PhoneNumber);
        }

        if (!string.IsNullOrWhiteSpace(filter.Code))
        {
            query = query.Where(l => l.Code == filter.Code);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query = filter.Status switch
            {
                "Active" => query.Where(l => l.IsActive),
                "Inactive" => query.Where(l => !l.IsActive),
                _ => query,
            };
        }

        return query;
    }

    private static string DescribeAccess(LinkAccessType type, string? errorCode) => type switch
    {
        LinkAccessType.View => "بازدید از صفحه لینک",
        LinkAccessType.OtpRequested => "درخواست کد تأیید",
        LinkAccessType.VerifySuccess => "تأیید موفق کد",
        LinkAccessType.VerifyFailed => $"تأیید ناموفق کد ({errorCode})",
        LinkAccessType.Locked => "قفل موقت",
        LinkAccessType.Download => "دانلود فایل",
        LinkAccessType.DownloadFailed => $"دانلود ناموفق ({errorCode})",
        _ => type.ToString(),
    };

    private static string MaskPhone(string phone) => phone.Length > 4 ? phone[..4] + new string('*', phone.Length - 4) : phone;
}
