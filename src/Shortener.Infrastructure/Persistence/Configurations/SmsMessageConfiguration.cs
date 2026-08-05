using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shortener.Domain.Entities;

namespace Shortener.Infrastructure.Persistence.Configurations;

public class SmsMessageConfiguration : IEntityTypeConfiguration<SmsMessage>
{
    public void Configure(EntityTypeBuilder<SmsMessage> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.PhoneNumber).HasMaxLength(15).IsRequired();
        b.Property(x => x.Body).HasMaxLength(1000).IsRequired();
        b.Property(x => x.ProviderMessageId).HasMaxLength(100);
        b.Property(x => x.LastError).HasMaxLength(1000);
        b.Property(x => x.Cost).HasPrecision(18, 2);

        b.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("IX_SmsMessages_Status_Created");
        b.HasIndex(x => x.ShortLinkId).HasDatabaseName("IX_SmsMessages_ShortLink");
        b.HasIndex(x => x.ProviderMessageId)
            .HasDatabaseName("IX_SmsMessages_ProviderMsgId")
            .HasFilter("[ProviderMessageId] IS NOT NULL");

        b.HasOne(x => x.ShortLink)
            .WithMany()
            .HasForeignKey(x => x.ShortLinkId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.SmsAccount)
            .WithMany()
            .HasForeignKey(x => x.SmsAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Template)
            .WithMany()
            .HasForeignKey(x => x.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
