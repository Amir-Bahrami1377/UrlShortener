using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shortener.Domain.Entities;

namespace Shortener.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Implements §M2.7: every Add/Modify/Delete on a sensitive entity gets an AuditLog row, with
/// encrypted credential fields masked in both the old and new snapshots.
/// </summary>
public sealed class AuditSaveChangesInterceptor(IHttpContextAccessor httpContextAccessor) : SaveChangesInterceptor
{
    private static readonly HashSet<Type> AuditedTypes =
    [
        typeof(Client), typeof(ApiKey), typeof(SmsAccount), typeof(MessageTemplate), typeof(RetentionPolicy),
    ];

    private static readonly HashSet<string> MaskedProperties = ["ApiKeyEnc", "UsernameEnc", "PasswordEnc", "KeyHash"];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AppendAuditLogs(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        AppendAuditLogs(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AppendAuditLogs(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var httpContext = httpContextAccessor.HttpContext;
        var userId = httpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        var ip = httpContext?.Connection.RemoteIpAddress?.ToString();

        // Snapshot first: adding AuditLog entries while enumerating ChangeTracker.Entries() would mutate it mid-iteration.
        var entries = context.ChangeTracker.Entries()
            .Where(e => AuditedTypes.Contains(e.Entity.GetType()) && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in entries)
        {
            context.Set<AuditLog>().Add(new AuditLog
            {
                UserId = userId,
                EntityName = entry.Entity.GetType().Name,
                EntityId = GetPrimaryKeyValue(entry),
                Action = entry.State.ToString(),
                OldValueJson = entry.State == EntityState.Added ? null : SerializeMasked(entry.OriginalValues, entry),
                NewValueJson = entry.State == EntityState.Deleted ? null : SerializeMasked(entry.CurrentValues, entry),
                IpAddress = ip,
            });
        }
    }

    private static string GetPrimaryKeyValue(EntityEntry entry) =>
        entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue?.ToString() ?? "unknown";

    private static string SerializeMasked(PropertyValues values, EntityEntry entry)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            dict[name] = MaskedProperties.Contains(name) ? "***MASKED***" : values[name];
        }

        return JsonSerializer.Serialize(dict);
    }
}
