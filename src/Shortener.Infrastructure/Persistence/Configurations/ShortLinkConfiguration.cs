using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shortener.Domain.Entities;

namespace Shortener.Infrastructure.Persistence.Configurations;

public class ShortLinkConfiguration : IEntityTypeConfiguration<ShortLink>
{
    public void Configure(EntityTypeBuilder<ShortLink> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasColumnType("char(6)").IsRequired();
        b.Property(x => x.ClientRequestId).HasMaxLength(64);
        b.Property(x => x.Shop).HasMaxLength(50).IsRequired();
        b.Property(x => x.Shod).HasMaxLength(50).IsRequired();
        b.Property(x => x.Radif).HasMaxLength(50).IsRequired();
        b.Property(x => x.ReportName).HasMaxLength(300).IsRequired();
        b.Property(x => x.PhoneNumber).HasMaxLength(15).IsRequired();
        b.Property(x => x.BatchTag).HasMaxLength(64);
        b.Property(x => x.DownloadCount).HasDefaultValue(0);
        b.Property(x => x.OtpRequestCount).HasDefaultValue(0);
        b.Property(x => x.FailedVerifyCount).HasDefaultValue(0);
        b.Property(x => x.IsActive).HasDefaultValue(true);

        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UX_ShortLinks_Code");
        b.HasIndex(x => x.RequestId).IsUnique().HasDatabaseName("UX_ShortLinks_RequestId");

        b.HasIndex(x => new { x.ClientId, x.Shop, x.Shod, x.Radif, x.ReportId })
            .IsUnique()
            .HasDatabaseName("UX_ShortLinks_Business")
            .HasFilter("[IsActive] = 1");

        b.HasIndex(x => new { x.ClientId, x.ClientRequestId })
            .HasDatabaseName("IX_ShortLinks_ClientRequestId");

        b.HasIndex(x => x.BatchTag)
            .HasDatabaseName("IX_ShortLinks_BatchTag")
            .HasFilter("[BatchTag] IS NOT NULL");

        b.HasIndex(x => new { x.IsActive, x.ExpiresAt })
            .HasDatabaseName("IX_ShortLinks_Expiry");

        b.HasOne(x => x.StoredFile)
            .WithMany()
            .HasForeignKey(x => x.StoredFileId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Client)
            .WithMany()
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.ApiKey)
            .WithMany()
            .HasForeignKey(x => x.ApiKeyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
