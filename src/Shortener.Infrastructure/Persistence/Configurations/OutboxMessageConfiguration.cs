using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shortener.Domain.Entities;

namespace Shortener.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Type).HasMaxLength(200).IsRequired();
        b.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.Error).HasMaxLength(1000);

        b.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasDatabaseName("IX_Outbox_Pending")
            .HasFilter("[Status] = 0");
    }
}
