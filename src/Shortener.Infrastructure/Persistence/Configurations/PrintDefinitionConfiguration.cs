using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shortener.Domain.Entities;

namespace Shortener.Infrastructure.Persistence.Configurations;

public class PrintDefinitionConfiguration : IEntityTypeConfiguration<PrintDefinition>
{
    public void Configure(EntityTypeBuilder<PrintDefinition> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();

        b.HasIndex(x => new { x.ClientId, x.Code })
            .HasDatabaseName("UX_PrintDefinitions_ClientCode")
            .IsUnique();

        b.HasOne(x => x.Client)
            .WithMany()
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
