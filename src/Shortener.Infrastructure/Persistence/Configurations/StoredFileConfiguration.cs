using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shortener.Domain.Entities;

namespace Shortener.Infrastructure.Persistence.Configurations;

public class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.FileGuid).HasDefaultValueSql("NEWID()");
        b.Property(x => x.StorageKey).HasMaxLength(400).IsRequired();
        b.Property(x => x.OriginalFileName).HasMaxLength(300).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.Extension).HasMaxLength(10).IsRequired();
        b.Property(x => x.Sha256).HasColumnType("char(64)").IsRequired();
        b.Property(x => x.DeletedBy).HasMaxLength(100);

        b.HasIndex(x => x.FileGuid).IsUnique();

        b.HasIndex(x => new { x.Status, x.FileExpiresAt })
            .HasDatabaseName("IX_StoredFiles_Retention")
            .IncludeProperties(x => new { x.StorageKey, x.SizeBytes });

        b.HasIndex(x => new { x.ClientId, x.StoredAt })
            .HasDatabaseName("IX_StoredFiles_Client_StoredAt");

        b.HasOne(x => x.Client)
            .WithMany()
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.RetentionPolicy)
            .WithMany()
            .HasForeignKey(x => x.RetentionPolicyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
