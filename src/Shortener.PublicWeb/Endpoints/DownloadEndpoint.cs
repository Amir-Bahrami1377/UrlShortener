using Microsoft.EntityFrameworkCore;
using Shortener.Application.Abstractions;
using Shortener.Application.Common;
using Shortener.Application.Services;
using Shortener.Domain.Enums;
using Shortener.Domain.Exceptions;
using Shortener.Infrastructure.Observability;
using Shortener.Infrastructure.Persistence;

namespace Shortener.PublicWeb.Endpoints;

public static class DownloadEndpoint
{
    public static void MapDownloadEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/d/{token}", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        string token,
        HttpContext httpContext,
        IDownloadTokenService tokenService,
        AppDbContext db,
        IFileStorage fileStorage,
        ILinkAccessLogger accessLogger,
        ShortenerMetrics metrics,
        CancellationToken ct)
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();

        var payload = await tokenService.ConsumeAsync(token, ct);
        if (payload is null)
        {
            throw new AppException(ErrorCodes.TokenUsedOrExpired, "توکن دانلود مصرف شده یا منقضی شده است. لطفاً دوباره تأیید کنید.");
        }

        if (payload.IpHash != RequestFingerprint.Hash(ip) || payload.UserAgentHash != RequestFingerprint.Hash(userAgent))
        {
            throw new AppException(ErrorCodes.TokenMismatch, "این توکن برای مرورگر یا شبکه دیگری صادر شده است.");
        }

        var link = await db.ShortLinks.Include(l => l.StoredFile).FirstOrDefaultAsync(l => l.Id == payload.ShortLinkId, ct);
        if (link is null || link.StoredFile is null)
        {
            throw new AppException(ErrorCodes.LinkNotFound, "لینک مرتبط با این توکن یافت نشد.");
        }

        if (!link.IsActive)
        {
            throw new AppException(ErrorCodes.LinkInactive, "این لینک غیرفعال شده است.");
        }

        if (link.ExpiresAt < DateTime.UtcNow)
        {
            throw new AppException(ErrorCodes.LinkExpired, "مهلت دانلود این لینک به پایان رسیده است.");
        }

        if (link.StoredFile.Status == FileStatus.Deleted)
        {
            throw new AppException(ErrorCodes.FileDeleted, "فایل به دلیل اتمام مهلت نگهداری حذف شده است.");
        }

        if (!await fileStorage.ExistsAsync(link.StoredFile.StorageKey, ct))
        {
            accessLogger.Enqueue(new LinkAccessLogEntry(link.Id, LinkAccessType.DownloadFailed, ip, userAgent, false, ErrorCodes.FileMissing));
            throw new AppException(ErrorCodes.FileMissing, "فایل روی دیسک یافت نشد.");
        }

        await db.ShortLinks
            .Where(l => l.Id == link.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.DownloadCount, l => l.DownloadCount + 1), ct);
        accessLogger.Enqueue(new LinkAccessLogEntry(link.Id, LinkAccessType.Download, ip, userAgent, true, null));
        metrics.Download(link.ClientId);

        httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
        httpContext.Response.Headers["Cache-Control"] = "no-store";

        var stream = await fileStorage.OpenReadAsync(link.StoredFile.StorageKey, ct);
        var fileName = FileNameSanitizer.Sanitize($"{link.ReportName}{link.StoredFile.Extension}");

        return Results.Stream(stream, link.StoredFile.ContentType, fileName, enableRangeProcessing: true);
    }
}
