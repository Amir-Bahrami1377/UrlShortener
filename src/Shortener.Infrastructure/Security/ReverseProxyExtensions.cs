using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Shortener.Infrastructure.Security;

public static class ReverseProxyExtensions
{
    /// <summary>Trusts X-Forwarded-For/-Proto from any upstream — safe here because the only path
    /// into these hosts in the Docker Compose deployment is the Caddy reverse proxy on the same
    /// internal network (the app containers no longer publish host ports directly; see
    /// docker-compose.yml). Must run before UseHsts/UseHttpsRedirection, or they'll see every
    /// request as plain HTTP (Caddy always forwards over HTTP internally) and redirect-loop.</summary>
    public static IApplicationBuilder UseForwardedHeadersForReverseProxy(this IApplicationBuilder app)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        return app.UseForwardedHeaders(options);
    }
}
