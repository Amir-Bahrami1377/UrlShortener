using Microsoft.AspNetCore.Identity;
using Serilog;
using Shortener.Infrastructure;
using Shortener.Infrastructure.Identity;
using Shortener.Infrastructure.Observability;
using Shortener.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) =>
    SerilogSetup.Configure(config, context.Configuration, context.HostingEnvironment, "Admin"));

builder.Services.AddControllersWithViews();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.AddPolicy(AdminPolicies.SuperAdminOnly, p => p.RequireRole(AdminRoles.SuperAdmin));
    options.AddPolicy(AdminPolicies.OperatorOrAbove, p => p.RequireRole(AdminRoles.SuperAdmin, AdminRoles.Operator));
});

builder.Services.AddCoreHealthChecks(builder.Configuration);

var app = builder.Build();

app.Services.GetRequiredService<ShortenerMetrics>();

// Roles/admin user/SMS provider catalog/global retention policy are load-bearing baseline data,
// not sample content — every environment needs them (see DbSeeder.SeedBaselineAsync's doc comment).
using (var baselineScope = app.Services.CreateScope())
{
    await Shortener.Infrastructure.Persistence.DbSeeder.SeedBaselineAsync(baselineScope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseForwardedHeadersForReverseProxy();
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    using var scope = app.Services.CreateScope();
    await Shortener.Infrastructure.Persistence.DbSeeder.SeedDevelopmentDataAsync(scope.ServiceProvider);
}

app.UseCorrelationId();
app.UseSecurityHeaders();
app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();

// Forces the "change your initial password" flow from §M2.1 — anyone with MustChangePassword=true
// is redirected to the change-password page before they can reach anything else in the panel.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var isExempt = path.StartsWithSegments("/Account") || path.StartsWithSegments("/lib") ||
                   path.StartsWithSegments("/css") || path.StartsWithSegments("/js");

    if (!isExempt && context.User.Identity?.IsAuthenticated == true)
    {
        var userManager = context.RequestServices.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.GetUserAsync(context.User);
        if (user?.MustChangePassword == true)
        {
            context.Response.Redirect("/Account/ChangePassword?forced=true");
            return;
        }
    }

    await next(context);
});

app.UseAuthorization();

app.MapSystemHealthChecks();
app.MapShortenerMetrics();
// MapStaticAssets registers endpoints, unlike the old UseStaticFiles middleware — endpoints fall
// under the global RequireAuthenticatedUser() fallback policy above unless explicitly exempted.
// Without this, even the login page's own CSS/JS 302-redirected to itself (confirmed live: a real
// browser loading /Account/Login got "MIME type text/html" errors for every stylesheet/script, since
// each request bounced through the login redirect instead of serving the file).
app.MapStaticAssets().AllowAnonymous();
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

public static class AdminRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Operator = "Operator";
    public const string Viewer = "Viewer";
}

public static class AdminPolicies
{
    public const string SuperAdminOnly = "SuperAdminOnly";
    public const string OperatorOrAbove = "OperatorOrAbove";
}
