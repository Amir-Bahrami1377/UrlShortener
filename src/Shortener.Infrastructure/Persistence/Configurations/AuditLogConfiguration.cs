using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shortener.Domain.Entities;

namespace Shortener.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        b.Property(x => x.EntityName).HasMaxLength(200).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(100).IsRequired();
        b.Property(x => x.Action).HasMaxLength(50).IsRequired();
        b.Property(x => x.OldValueJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.NewValueJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.IpAddress).HasMaxLength(45);
    }
}
