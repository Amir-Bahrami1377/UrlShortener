using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using Shortener.Infrastructure;
using Shortener.Infrastructure.Observability;
using Shortener.Infrastructure.Security;
using Shortener.Infrastructure.Web;
using Shortener.PublicWeb.Common;
using Shortener.PublicWeb.Endpoints;
using Shortener.PublicWeb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) =>
    SerilogSetup.Configure(config, context.Configuration, context.HostingEnvironment, "PublicWeb"));

builder.Services.AddRazorPages();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddHostedService<LinkAccessLogWriterService>();
builder.Services.AddSingleton<EnumerationGuard>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, ct) =>
        new ValueTask(ProblemResponseWriter.WriteAsync(context.HttpContext, Shortener.Application.Common.ErrorCodes.RateLimited));

    options.AddPolicy(RateLimitPolicies.PublicLink, httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
        });
    });
});

builder.Services.AddCoreHealthChecks(builder.Configuration);

var app = builder.Build();

app.Services.GetRequiredService<ShortenerMetrics>();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseCorrelationId();
app.UseSecurityHeaders();
app.UseContentSecurityPolicy();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseRouting();
app.UseAuthorization();

app.MapSystemHealthChecks();
app.MapShortenerMetrics();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapOtpEndpoints();
app.MapDownloadEndpoint();

app.Run();
