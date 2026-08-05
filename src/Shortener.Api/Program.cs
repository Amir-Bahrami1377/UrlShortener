using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Scalar.AspNetCore;
using Serilog;
using Shortener.Api.Authentication;
using Shortener.Api.Common;
using Shortener.Api.Endpoints;
using Shortener.Application.Common;
using Shortener.Infrastructure;
using Shortener.Infrastructure.Observability;
using Shortener.Infrastructure.Persistence;
using Shortener.Infrastructure.Security;
using Shortener.Infrastructure.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) =>
    SerilogSetup.Configure(config, context.Configuration, context.HostingEnvironment, "Api"));

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddHostedService<ApiKeyUsageUpdaterService>();

// AddInfrastructure's AddIdentity() call above registers Identity's own cookie scheme and sets
// AuthenticationOptions.DefaultScheme; the single-string AddAuthentication(scheme) overload only
// re-sets DefaultScheme, leaving DefaultAuthenticateScheme/DefaultChallengeScheme pointed at the
// cookie scheme (redirecting to /Account/Login instead of hitting our handler). Set every default
// scheme property explicitly so ApiKey unambiguously wins for this API's endpoints.
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = ApiKeyAuthenticationDefaults.SchemeName;
        options.DefaultAuthenticateScheme = ApiKeyAuthenticationDefaults.SchemeName;
        options.DefaultChallengeScheme = ApiKeyAuthenticationDefaults.SchemeName;
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationDefaults.SchemeName, null);
builder.Services.AddAuthorization();

var permitLimitPerMinute = builder.Configuration.GetValue("ApiRateLimit:PermitLimitPerMinute", 6000);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, ct) => new ValueTask(ProblemResponseWriter.WriteAsync(context.HttpContext, ErrorCodes.RateLimited));

    options.AddPolicy(RateLimitPolicies.PerClient, httpContext =>
    {
        var partitionKey = httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.GetClientId().ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimitPerMinute,
            Window = TimeSpan.FromSeconds(60),
        });
    });
});

builder.Services.AddCoreHealthChecks(builder.Configuration).AddOperationalHealthChecks();

var app = builder.Build();

// Eagerly resolved so every ObservableGauge is registered with the Meter before the first scrape,
// not lazily on first use (which for links_created_total/etc. would be the first real request).
app.Services.GetRequiredService<ShortenerMetrics>();

// Roles/admin user/SMS provider catalog/global retention policy are load-bearing baseline data,
// not sample content — every environment needs them (see DbSeeder.SeedBaselineAsync's doc comment).
using (var baselineScope = app.Services.CreateScope())
{
    await DbSeeder.SeedBaselineAsync(baselineScope.ServiceProvider);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();

    using var scope = app.Services.CreateScope();
    await DbSeeder.SeedDevelopmentDataAsync(scope.ServiceProvider);
}
else
{
    app.UseHsts();
}

app.UseCorrelationId();
app.UseSecurityHeaders();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapSystemHealthChecks();
app.MapShortenerMetrics();
app.MapLinksEndpoints();

app.Run();

// Exposed so Shortener.IntegrationTests can boot this app via WebApplicationFactory<Program>.
public partial class Program;
