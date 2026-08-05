using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shortener.Domain.Entities;

namespace Shortener.Infrastructure.Persistence.Configurations;

public class SmsStatusHistoryConfiguration : IEntityTypeConfiguration<SmsStatusHistory>
{
    public void Configure(EntityTypeBuilder<SmsStatusHistory> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.ProviderStatusCode).HasMaxLength(50);
        b.Property(x => x.Description).HasMaxLength(500);

        b.HasOne(x => x.SmsMessage)
            .WithMany()
            .HasForeignKey(x => x.SmsMessageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
