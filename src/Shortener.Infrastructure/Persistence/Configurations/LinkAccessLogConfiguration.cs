using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shortener.Domain.Entities;

namespace Shortener.Infrastructure.Persistence.Configurations;

public class LinkAccessLogConfiguration : IEntityTypeConfiguration<LinkAccessLog>
{
    public void Configure(EntityTypeBuilder<LinkAccessLog> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.IpAddress).HasMaxLength(45).IsRequired();
        b.Property(x => x.UserAgent).HasMaxLength(500);
        b.Property(x => x.ErrorCode).HasMaxLength(50);

        b.HasIndex(x => x.ShortLinkId);

        b.HasOne(x => x.ShortLink)
            .WithMany()
            .HasForeignKey(x => x.ShortLinkId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
