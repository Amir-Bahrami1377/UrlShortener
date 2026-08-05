using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Identity;

namespace Shortener.Infrastructure.Persistence;

/// <summary>
/// Seeds roles, the bootstrap admin user, the SMS provider catalog, and the global retention
/// policy (§M1.5) — required baseline data with no environment-specific content, safe and necessary
/// to run in every environment including Production — plus a development Client + ApiKey +
/// SmsAccounts + MessageTemplates so the upload/OTP/download flow can be exercised end-to-end before
/// the admin panel (M2) existed; that part is Development-only convenience data.
/// Every step checks for existing data first, so all of this is safe to call repeatedly/from
/// multiple hosts racing at startup.
/// </summary>
public static class DbSeeder
{
    private static readonly string[] Roles = ["SuperAdmin", "Operator", "Viewer"];

    /// <summary>Report id used consistently by the dev download-link template and the manual E2E test script.</summary>
    public const int DevReportId = 1;

    /// <summary>Roles, the bootstrap admin user, the SMS provider catalog, and the global retention
    /// policy — every one of these is load-bearing infrastructure data (e.g. UploadLinkService can't
    /// insert a ShortLink at all without a resolvable retention policy), not sample/demo content, so
    /// this must run in every environment. Call this unconditionally at startup, not just in
    /// Development — see the docs/deployment-iis.md and docs/runbook.md notes on this.</summary>
    public static async Task SeedBaselineAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<AppUser>>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        await SeedRolesAsync(roleManager);
        await SeedAdminUserAsync(userManager, logger);
        await SeedSmsProvidersAsync(db, ct);
        await SeedGlobalRetentionPolicyAsync(db, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A ready-to-use Client/ApiKey/SmsAccounts/MessageTemplates for local development only
    /// — never call this outside Development (it wires the "fake" SMS provider into a real Client).</summary>
    public static Task SeedDevelopmentDataAsync(IServiceProvider services, CancellationToken ct = default) =>
        SeedDevClientAsync(services.GetRequiredService<AppDbContext>(),
            services.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder"), ct);

    /// <summary>Baseline + Development sample data in one call, for the existing Development-only call sites.</summary>
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await SeedBaselineAsync(services, ct);
        await SeedDevelopmentDataAsync(services, ct);
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        foreach (var role in Roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    private static async Task SeedAdminUserAsync(UserManager<AppUser> userManager, ILogger logger)
    {
        const string username = "admin";
        const string initialPassword = "ChangeMe#2026";

        if (await userManager.FindByNameAsync(username) is not null)
        {
            return;
        }

        var admin = new AppUser
        {
            UserName = username,
            Email = "admin@shortener.local",
            EmailConfirmed = true,
            FullName = "System Administrator",
            MustChangePassword = true,
        };

        var result = await userManager.CreateAsync(admin, initialPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Failed to seed admin user: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        await userManager.AddToRoleAsync(admin, "SuperAdmin");

        // §M8.1 — credentials must never enter the structured logging pipeline (Console/File sinks,
        // 30-day retention). This one-time bootstrap secret goes straight to the console instead.
        logger.LogInformation("Seeded admin user '{Username}' (one-time bootstrap credential — must change at first login; see console output for the initial password).", username);
        Console.WriteLine($"[DbSeeder] Initial password for admin user '{username}': {initialPassword}");
    }

    private static async Task SeedSmsProvidersAsync(AppDbContext db, CancellationToken ct)
    {
        SmsProvider[] providers =
        [
            new() { Code = "kavenegar", Name = "Kavenegar", SupportsPattern = true, SupportsDlr = true, IsActive = true },
            new() { Code = "melipayamak", Name = "MeliPayamak", SupportsPattern = true, SupportsDlr = true, IsActive = true },
            new() { Code = "farazsms", Name = "Faraz SMS", SupportsPattern = true, SupportsDlr = true, IsActive = true },
            // No pattern/token sending is confirmed for this provider. GetMessagesStatus is a real,
            // confirmed SOAP operation (unlike before), so SupportsDlr is true even though the
            // returned status vocabulary itself is still a best-effort guess (see AfeProvider's doc).
            new() { Code = "afe", Name = "Afe.ir (واید)", SupportsPattern = false, SupportsDlr = true, IsActive = true },
            new() { Code = "fake", Name = "Fake (development only)", SupportsPattern = false, SupportsDlr = false, IsActive = true },
        ];

        foreach (var provider in providers)
        {
            if (!await db.SmsProviders.AnyAsync(p => p.Code == provider.Code, ct))
            {
                db.SmsProviders.Add(provider);
            }
        }
    }

    private static async Task SeedGlobalRetentionPolicyAsync(AppDbContext db, CancellationToken ct)
    {
        var hasGlobal = await db.RetentionPolicies.AnyAsync(p => p.ClientId == null && p.ReportId == null, ct);
        if (hasGlobal)
        {
            return;
        }

        db.RetentionPolicies.Add(new RetentionPolicy
        {
            ClientId = null,
            ReportId = null,
            Title = "Global default",
            RetentionDays = 90,
            GraceDays = 7,
            Priority = 0,
            IsActive = true,
        });
    }

    private static async Task SeedDevClientAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        const string devClientCode = "DEV";

        if (await db.Clients.AnyAsync(c => c.Code == devClientCode, ct))
        {
            return;
        }

        var client = new Client { Name = "Development Client", Code = devClientCode, IsActive = true };
        db.Clients.Add(client);
        await db.SaveChangesAsync(ct);

        var fakeProvider = await db.SmsProviders.SingleAsync(p => p.Code == "fake", ct);

        var rawKeyBytes = RandomNumberGenerator.GetBytes(32);
        var rawKey = "sk_" + Base64UrlEncode(rawKeyBytes);
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant();

        db.ApiKeys.Add(new ApiKey
        {
            ClientId = client.Id,
            KeyHash = keyHash,
            KeyPrefix = rawKey[..12],
            Title = "Development API Key",
            IsActive = true,
        });

        db.SmsAccounts.AddRange(
            new SmsAccount
            {
                ClientId = client.Id,
                SmsProviderId = fakeProvider.Id,
                Title = "Dev Bulk (fake)",
                Purpose = SmsAccountPurpose.Bulk,
                RatePerMinute = 3000,
                IsDefault = true,
                IsActive = true,
            },
            new SmsAccount
            {
                ClientId = client.Id,
                SmsProviderId = fakeProvider.Id,
                Title = "Dev Otp (fake)",
                Purpose = SmsAccountPurpose.Otp,
                RatePerMinute = 3000,
                IsDefault = true,
                IsActive = true,
            });

        db.MessageTemplates.AddRange(
            new MessageTemplate
            {
                ClientId = client.Id,
                ReportId = DevReportId,
                TemplateType = TemplateType.DownloadLink,
                Title = "Default download link template",
                Body = "سند {reportName} آماده دانلود است: {shortUrl} (تا {expireDate})",
                IsActive = true,
            },
            new MessageTemplate
            {
                ClientId = client.Id,
                ReportId = null,
                TemplateType = TemplateType.Otp,
                Title = "Default OTP template",
                Body = "کد تایید شما: {otp} (اعتبار {otpMinutes} دقیقه)",
                IsActive = true,
            });

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Seeded development Client '{Code}' (Id={ClientId}) with an API key (see console output — shown once, save it now).",
            devClientCode, client.Id);
        Console.WriteLine($"[DbSeeder] API key for development Client '{devClientCode}': {rawKey}");
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
