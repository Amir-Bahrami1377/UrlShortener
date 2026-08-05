using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Shortener.Application.Abstractions;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;
using Shortener.PublicWeb.Common;

namespace Shortener.PublicWeb.Pages;

[EnableRateLimiting(RateLimitPolicies.PublicLink)]
public class ShortLinkModel(
    IShortLinkStateResolver stateResolver, AppDbContext db, ILinkAccessLogger accessLogger, EnumerationGuard enumerationGuard) : PageModel
{
    public string Code { get; private set; } = string.Empty;
    public string? ErrorTitle { get; private set; }
    public string? ReportName { get; private set; }
    public string? MaskedPhone { get; private set; }
    public DateTime? LockedUntilUtc { get; private set; }
    public bool IsHealthy => ErrorTitle is null;

    public async Task<IActionResult> OnGetAsync(string code, CancellationToken ct)
    {
        Code = code;
        var ip = GetClientIp();

        // §M8.5 — 20 404s from the same IP within 5 minutes blocks further /s/ probing outright,
        // without even resolving the requested code (so a blocked IP learns nothing more either way).
        if (await enumerationGuard.IsBlockedAsync(ip))
        {
            ErrorTitle = "به دلیل تلاش‌های ناموفق مکرر، دسترسی شما موقتاً مسدود شده است.";
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Page();
        }

        var state = await stateResolver.ResolveAsync(code, ct);

        if (state is null)
        {
            await enumerationGuard.RecordNotFoundAsync(ip);
            ErrorTitle = "لینک نامعتبر است یا وجود ندارد.";
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }

        var health = state.GetHealthStatus(DateTime.UtcNow);
        if (health != LinkHealthStatus.Healthy)
        {
            ErrorTitle = health switch
            {
                LinkHealthStatus.Inactive => "این لینک غیرفعال شده است.",
                LinkHealthStatus.Expired => "مهلت دانلود این لینک به پایان رسیده است.",
                LinkHealthStatus.FileDeleted => "فایل به دلیل اتمام مهلت نگهداری حذف شده است.",
                _ => "این لینک در دسترس نیست.",
            };
            Response.StatusCode = StatusCodes.Status410Gone;
            return Page();
        }

        var lockedUntil = await db.ShortLinks
            .Where(l => l.Id == state.ShortLinkId)
            .Select(l => l.LockedUntil)
            .FirstOrDefaultAsync(ct);

        if (lockedUntil > DateTime.UtcNow)
        {
            ErrorTitle = "به دلیل تلاش‌های ناموفق مکرر، این لینک موقتاً قفل شده است.";
            LockedUntilUtc = lockedUntil;
            return Page();
        }

        ReportName = state.ReportName;
        MaskedPhone = MaskPhone(state.PhoneNumber);

        accessLogger.Enqueue(new LinkAccessLogEntry(
            state.ShortLinkId, LinkAccessType.View, GetClientIp(), Request.Headers.UserAgent.ToString(), true, null));

        return Page();
    }

    private string GetClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string MaskPhone(string phone) =>
        phone.Length > 4 ? phone[..4] + new string('*', phone.Length - 4) : phone;
}
