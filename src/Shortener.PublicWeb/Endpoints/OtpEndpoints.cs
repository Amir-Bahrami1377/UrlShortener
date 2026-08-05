using Microsoft.AspNetCore.RateLimiting;
using Shortener.Application.Abstractions;
using Shortener.Application.Common;
using Shortener.Domain.Exceptions;
using Shortener.PublicWeb.Common;
using Shortener.PublicWeb.Contracts;

namespace Shortener.PublicWeb.Endpoints;

public static class OtpEndpoints
{
    public static void MapOtpEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/s/{code}/otp/request", RequestOtpAsync)
            .RequireRateLimiting(RateLimitPolicies.PublicLink);

        app.MapPost("/s/{code}/otp/verify", VerifyOtpAsync)
            .RequireRateLimiting(RateLimitPolicies.PublicLink);
    }

    private static async Task<IResult> RequestOtpAsync(
        string code, HttpContext httpContext, IOtpService otpService, CancellationToken ct)
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await otpService.RequestAsync(code, ip, ct);

        if (result.Outcome != OtpRequestOutcome.Sent)
        {
            throw MapRequestFailure(result);
        }

        return Results.Ok(new RequestOtpResponse(result.CooldownSecondsRemaining!.Value, result.TtlSeconds!.Value));
    }

    private static async Task<IResult> VerifyOtpAsync(
        string code, VerifyOtpRequest body, HttpContext httpContext, IOtpService otpService, CancellationToken ct)
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        var result = await otpService.VerifyAsync(code, body.Otp, ip, userAgent, ct);

        if (result.Outcome != OtpVerifyOutcome.Success)
        {
            throw MapVerifyFailure(result);
        }

        return Results.Ok(new VerifyOtpResponse($"/d/{result.DownloadToken}"));
    }

    private static AppException MapRequestFailure(OtpRequestResult result) => result.Outcome switch
    {
        OtpRequestOutcome.NotFound => new AppException(ErrorCodes.LinkNotFound, "لینکی با این کد یافت نشد."),
        OtpRequestOutcome.LinkUnhealthy => MapUnhealthy(result.UnhealthyReason!.Value),
        OtpRequestOutcome.Locked => new AppException(ErrorCodes.TooManyAttempts, "این لینک موقتاً قفل شده است."),
        OtpRequestOutcome.Cooldown => new AppException(
            ErrorCodes.OtpCooldown, $"لطفاً {result.CooldownSecondsRemaining} ثانیه دیگر دوباره تلاش کنید."),
        OtpRequestOutcome.LimitPerLink => new AppException(ErrorCodes.OtpLimitLink, "سقف درخواست کد برای این لینک پر شده است."),
        OtpRequestOutcome.LimitPerIp => new AppException(ErrorCodes.OtpLimitIp, "سقف درخواست کد برای این IP پر شده است."),
        OtpRequestOutcome.NotConfigured => new AppException(ErrorCodes.OtpNotConfigured, "سرویس پیامک OTP برای این کلاینت پیکربندی نشده است."),
        _ => new AppException(ErrorCodes.Internal, "خطای غیرمنتظره."),
    };

    private static AppException MapVerifyFailure(OtpVerifyResult result) => result.Outcome switch
    {
        OtpVerifyOutcome.NotFound => new AppException(ErrorCodes.LinkNotFound, "لینکی با این کد یافت نشد."),
        OtpVerifyOutcome.LinkUnhealthy => MapUnhealthy(result.UnhealthyReason!.Value),
        OtpVerifyOutcome.Locked or OtpVerifyOutcome.TooManyAttempts =>
            new AppException(ErrorCodes.TooManyAttempts, "به دلیل تلاش‌های ناموفق مکرر، این لینک موقتاً قفل شده است."),
        OtpVerifyOutcome.Expired => new AppException(ErrorCodes.OtpExpired, "کد منقضی شده است؛ کد جدید درخواست کنید."),
        OtpVerifyOutcome.Invalid => new AppException(
            ErrorCodes.OtpInvalid, $"کد نادرست است. {result.RemainingAttempts} تلاش دیگر باقی مانده است."),
        _ => new AppException(ErrorCodes.Internal, "خطای غیرمنتظره."),
    };

    private static AppException MapUnhealthy(LinkHealthStatus reason) => reason switch
    {
        LinkHealthStatus.Inactive => new AppException(ErrorCodes.LinkInactive, "این لینک غیرفعال شده است."),
        LinkHealthStatus.Expired => new AppException(ErrorCodes.LinkExpired, "مهلت دانلود این لینک به پایان رسیده است."),
        LinkHealthStatus.FileDeleted => new AppException(ErrorCodes.FileDeleted, "فایل به دلیل اتمام مهلت نگهداری حذف شده است."),
        _ => new AppException(ErrorCodes.Internal, "خطای غیرمنتظره."),
    };
}
