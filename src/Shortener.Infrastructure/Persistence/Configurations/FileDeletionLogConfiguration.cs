using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shortener.Domain.Entities;

namespace Shortener.Infrastructure.Persistence.Configurations;

public class FileDeletionLogConfiguration : IEntityTypeConfiguration<FileDeletionLog>
{
    public void Configure(EntityTypeBuilder<FileDeletionLog> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.StorageKey).HasMaxLength(400).IsRequired();
        b.Property(x => x.TriggeredBy).HasMaxLength(100).IsRequired();
        b.Property(x => x.ErrorMessage).HasMaxLength(1000);

        b.HasOne(x => x.StoredFile)
            .WithMany()
            .HasForeignKey(x => x.StoredFileId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
