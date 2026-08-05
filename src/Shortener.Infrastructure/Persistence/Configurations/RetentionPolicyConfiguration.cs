using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shortener.Domain.Entities;

namespace Shortener.Infrastructure.Persistence.Configurations;

public class RetentionPolicyConfiguration : IEntityTypeConfiguration<RetentionPolicy>
{
    public void Configure(EntityTypeBuilder<RetentionPolicy> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.BatchTag).HasMaxLength(64);
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.RetentionDays).HasDefaultValue(90);
        b.Property(x => x.GraceDays).HasDefaultValue(7);
        b.Property(x => x.IsActive).HasDefaultValue(true);

        b.HasOne(x => x.Client)
            .WithMany()
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
