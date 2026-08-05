using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Serilog.Context;

namespace Shortener.Infrastructure.Observability;

/// <summary>§M8.1 — reads or creates X-Correlation-Id and pushes it (plus ClientId, when the caller
/// is authenticated) into Serilog's LogContext for the lifetime of the request. Distinct from the
/// upload endpoint's own business-level RequestId (Items["RequestId"], a permanent tracking id
/// stored on the ShortLink row) — this is a purely observability-level, every-request correlation
/// id. ProblemResponseWriter still prefers Items["RequestId"] when present and falls back to
/// TraceIdentifier otherwise; this middleware doesn't change that.</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing) && !StringValues.IsNullOrEmpty(existing)
            ? existing.ToString()
            : Guid.CreateVersion7().ToString();

        context.Items["CorrelationId"] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            var clientIdScope = TryPushClientId(context);
            try
            {
                await next(context);
            }
            finally
            {
                clientIdScope?.Dispose();
            }
        }
    }

    private static IDisposable? TryPushClientId(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var clientId = context.User.FindFirst("client_id")?.Value;
        return clientId is not null ? LogContext.PushProperty("ClientId", clientId) : null;
    }
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();
}
