using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shortener.Admin.Models;
using Shortener.Application.Services;
using Shortener.Domain.Enums;

namespace Shortener.Admin.Controllers;

public partial class ReportsController
{
    // §M6.5 — synchronous export. The doc's >50k-row background-job path isn't built this session:
    // this dev sandbox will never realistically produce that many rows, and building the job
    // queue + polling + download-link infrastructure just for that threshold isn't worth it yet.
    private const int ExportRowLimit = 50_000;

    [HttpGet]
    public async Task<IActionResult> ExportLinks(LinksReportFilter filter, CancellationToken ct)
    {
        var rows = await BuildLinksQuery(filter)
            .OrderByDescending(l => l.CreatedAt)
            .Take(ExportRowLimit)
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
            })
            .ToListAsync(ct);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("لینک‌ها");
        sheet.RightToLeft = true;

        string[] headers = ["کد", "RequestId", "کلاینت", "پرونده", "نام گزارش", "شماره", "تاریخ ایجاد", "تاریخ انقضا", "تعداد OTP", "تعداد دانلود", "وضعیت", "وضعیت فایل"];
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
        }

        var row = 2;
        foreach (var link in rows)
        {
            sheet.Cell(row, 1).Value = link.Code;
            sheet.Cell(row, 2).Value = link.RequestId.ToString();
            sheet.Cell(row, 3).Value = link.ClientName;
            sheet.Cell(row, 4).Value = $"{link.Shop}/{link.Shod}/{link.Radif}";
            sheet.Cell(row, 5).Value = link.ReportName;
            sheet.Cell(row, 6).Value = MaskPhone(link.PhoneNumber);
            sheet.Cell(row, 7).Value = PersianDateHelper.ToPersianDateTime(link.CreatedAt);
            sheet.Cell(row, 8).Value = PersianDateHelper.ToPersianDate(link.ExpiresAt);
            sheet.Cell(row, 9).Value = link.OtpRequestCount;
            sheet.Cell(row, 10).Value = link.DownloadCount;
            sheet.Cell(row, 11).Value = link.IsActive ? "فعال" : "غیرفعال";
            sheet.Cell(row, 12).Value = link.FileStatus.ToString();
            row++;
        }

        sheet.Columns().AdjustToContents();
        return WriteWorkbook(workbook, "links-report");
    }

    [HttpGet]
    public async Task<IActionResult> ExportSms(SmsReportFilter filter, CancellationToken ct)
    {
        var rows = await BuildSmsQuery(filter)
            .OrderByDescending(m => m.CreatedAt)
            .Take(ExportRowLimit)
            .Select(m => new
            {
                ClientName = m.SmsAccount!.Client!.Name,
                m.MessageType,
                m.PhoneNumber,
                m.Status,
                m.Cost,
                m.CreatedAt,
                m.SentAt,
                m.DeliveredAt,
            })
            .ToListAsync(ct);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("پیامک‌ها");
        sheet.RightToLeft = true;

        string[] headers = ["کلاینت", "نوع", "شماره", "وضعیت", "هزینه", "تاریخ ایجاد", "تاریخ ارسال", "تاریخ تحویل"];
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
        }

        var row = 2;
        foreach (var m in rows)
        {
            sheet.Cell(row, 1).Value = m.ClientName;
            sheet.Cell(row, 2).Value = m.MessageType == SmsMessageType.Otp ? "OTP" : "لینک دانلود";
            sheet.Cell(row, 3).Value = MaskPhone(m.PhoneNumber);
            sheet.Cell(row, 4).Value = m.Status.ToString();
            sheet.Cell(row, 5).Value = m.Cost ?? 0;
            sheet.Cell(row, 6).Value = PersianDateHelper.ToPersianDateTime(m.CreatedAt);
            sheet.Cell(row, 7).Value = m.SentAt.ToPersianDateTime();
            sheet.Cell(row, 8).Value = m.DeliveredAt.ToPersianDateTime();
            row++;
        }

        sheet.Columns().AdjustToContents();
        return WriteWorkbook(workbook, "sms-report");
    }

    private FileContentResult WriteWorkbook(XLWorkbook workbook, string fileNamePrefix)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var fileName = $"{fileNamePrefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx";
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}
