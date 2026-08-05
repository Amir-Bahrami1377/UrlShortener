using Microsoft.AspNetCore.Http;
using Shortener.Application.Common;

namespace Shortener.Infrastructure.Web;

/// <summary>Writes the RFC 7807 error shape from IMPLEMENTATION_PLAN.md Appendix B. Shared by every
/// web project (Api, PublicWeb, ...) so the error contract stays identical across the whole system.</summary>
public static class ProblemResponseWriter
{
    public static Task WriteAsync(HttpContext context, string errorCode, string? detail = null)
    {
        var status = ErrorCodes.HttpStatus.GetValueOrDefault(errorCode, StatusCodes.Status500InternalServerError);
        var title = ErrorCodes.DefaultTitle.GetValueOrDefault(errorCode, errorCode);
        var requestId = context.Items.TryGetValue("RequestId", out var rid) && rid is not null
            ? rid.ToString()
            : context.TraceIdentifier;

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        return context.Response.WriteAsJsonAsync(new
        {
            type = $"https://links.example.ir/errors/{errorCode}",
            title,
            status,
            errorCode,
            requestId,
            detail = detail ?? title,
        });
    }
}
