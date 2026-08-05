using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Shortener.Infrastructure.Security;

/// <summary>§M8.5 — CSP on PublicWeb specifically (the internet-facing surface). Uses a per-request
/// nonce for the small number of inline &lt;script&gt;/&lt;style&gt; blocks in ShortLink.cshtml and
/// _Layout.cshtml rather than 'unsafe-inline', which would defeat most of CSP's XSS protection.
/// Views read the nonce via HttpContext.Items[NonceItemKey] (see CspNonce Razor helper).</summary>
public sealed class ContentSecurityPolicyMiddleware(RequestDelegate next)
{
    public const string NonceItemKey = "CspNonce";

    public async Task InvokeAsync(HttpContext context)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        context.Items[NonceItemKey] = nonce;

        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            $"script-src 'self' 'nonce-{nonce}'; " +
            $"style-src 'self' 'nonce-{nonce}'; " +
            "img-src 'self' data:; " +
            "font-src 'self'; " +
            "frame-ancestors 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self';";

        await next(context);
    }
}

public static class ContentSecurityPolicyMiddlewareExtensions
{
    public static IApplicationBuilder UseContentSecurityPolicy(this IApplicationBuilder app) =>
        app.UseMiddleware<ContentSecurityPolicyMiddleware>();
}
