using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shortener.Domain.Entities;

namespace Shortener.Infrastructure.Persistence.Configurations;

public class SmsProviderConfiguration : IEntityTypeConfiguration<SmsProvider>
{
    public void Configure(EntityTypeBuilder<SmsProvider> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();

        b.HasIndex(x => x.Code).IsUnique();
    }
}
