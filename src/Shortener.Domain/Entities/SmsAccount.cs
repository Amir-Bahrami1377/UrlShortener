using Shortener.Domain.Enums;

namespace Shortener.Domain.Entities;

/// <summary>
/// A client's credentials for one SMS provider, scoped to one purpose. Every client must have
/// exactly one default account per purpose (Bulk / Otp) so the OTP path never shares a line with bulk sends.
/// </summary>
public class SmsAccount
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client? Client { get; set; }

    public int SmsProviderId { get; set; }
    public SmsProvider? SmsProvider { get; set; }

    public required string Title { get; set; }
    public SmsAccountPurpose Purpose { get; set; }

    /// <summary>Encrypted with IDataProtector (purpose "SmsAccountCredentials"). Null when the provider doesn't need it.</summary>
    public string? ApiKeyEnc { get; set; }
    public string? UsernameEnc { get; set; }
    public string? PasswordEnc { get; set; }

    public string? SenderNumber { get; set; }
    public string? BaseUrl { get; set; }

    /// <summary>Provider-specific extra settings, as JSON.</summary>
    public string? SettingsJson { get; set; }

    public int RatePerMinute { get; set; } = 3000;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}
