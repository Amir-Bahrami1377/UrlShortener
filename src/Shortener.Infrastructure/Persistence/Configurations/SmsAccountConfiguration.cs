using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shortener.Domain.Entities;

namespace Shortener.Infrastructure.Persistence.Configurations;

public class SmsAccountConfiguration : IEntityTypeConfiguration<SmsAccount>
{
    public void Configure(EntityTypeBuilder<SmsAccount> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.ApiKeyEnc).HasMaxLength(500);
        b.Property(x => x.UsernameEnc).HasMaxLength(500);
        b.Property(x => x.PasswordEnc).HasMaxLength(500);
        b.Property(x => x.SenderNumber).HasMaxLength(30);
        b.Property(x => x.BaseUrl).HasMaxLength(500);
        b.Property(x => x.SettingsJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.RatePerMinute).HasDefaultValue(3000);

        b.HasIndex(x => new { x.ClientId, x.Purpose })
            .HasDatabaseName("UX_SmsAccounts_Client_Purpose_Default")
            .IsUnique()
            .HasFilter("[IsDefault] = 1");

        b.HasOne(x => x.Client)
            .WithMany()
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.SmsProvider)
            .WithMany()
            .HasForeignKey(x => x.SmsProviderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
