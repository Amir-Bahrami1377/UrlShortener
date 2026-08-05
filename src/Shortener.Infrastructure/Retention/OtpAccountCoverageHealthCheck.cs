using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;

namespace Shortener.Infrastructure.Retention;

/// <summary>§M8.3 — "at least one active SmsAccount with Purpose=Otp exists for every active
/// client." A gap here means that client's OTP requests will hit ERR_OTP_NOT_CONFIGURED at runtime,
/// so this is treated as a readiness failure rather than a soft degradation.</summary>
public sealed class OtpAccountCoverageHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var activeClientIds = await db.Clients
            .Where(c => c.IsActive)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (activeClientIds.Count == 0)
        {
            return HealthCheckResult.Healthy("هیچ کلاینت فعالی وجود ندارد.");
        }

        var clientsWithOtpAccount = await db.SmsAccounts
            .Where(a => a.IsActive && a.Purpose == SmsAccountPurpose.Otp && activeClientIds.Contains(a.ClientId))
            .Select(a => a.ClientId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var missing = activeClientIds.Except(clientsWithOtpAccount).ToList();
        if (missing.Count == 0)
        {
            return HealthCheckResult.Healthy("همه کلاینت‌های فعال حساب OTP دارند.");
        }

        var data = new Dictionary<string, object> { ["clientIdsMissingOtpAccount"] = missing };
        return HealthCheckResult.Unhealthy($"{missing.Count} کلاینت فعال بدون حساب پیامکی OTP یافت شد.", data: data);
    }
}
