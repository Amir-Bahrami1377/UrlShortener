using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Shortener.Domain.Abstractions;
using Shortener.Domain.Entities;
using Shortener.Infrastructure.Identity;

namespace Shortener.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole, string>(options)
{
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<SmsProvider> SmsProviders => Set<SmsProvider>();
    public DbSet<SmsAccount> SmsAccounts => Set<SmsAccount>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<RetentionPolicy> RetentionPolicies => Set<RetentionPolicy>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<ShortLink> ShortLinks => Set<ShortLink>();
    public DbSet<SmsMessage> SmsMessages => Set<SmsMessage>();
    public DbSet<SmsStatusHistory> SmsStatusHistories => Set<SmsStatusHistory>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<LinkAccessLog> LinkAccessLogs => Set<LinkAccessLog>();
    public DbSet<FileDeletionLog> FileDeletionLogs => Set<FileDeletionLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry is { State: EntityState.Added, Entity: IHasCreatedAt created })
            {
                created.CreatedAt = now;
            }

            if (entry.State is EntityState.Added or EntityState.Modified && entry.Entity is IHasUpdatedAt updated)
            {
                updated.UpdatedAt = now;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
