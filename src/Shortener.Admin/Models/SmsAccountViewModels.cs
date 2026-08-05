using System.ComponentModel.DataAnnotations;

namespace Shortener.Admin.Models;

public static class SmsAccountFormConstants
{
    /// <summary>Sentinel shown in place of an encrypted value on the edit form; submitting it unchanged
    /// means "leave the stored credential as-is" (§M2.4).</summary>
    public const string MaskedPlaceholder = "••••••••";
}

public sealed class SmsAccountListViewModel
{
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public List<SmsAccountListItemViewModel> Accounts { get; set; } = [];
}

public sealed class SmsAccountListItemViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public string? SenderNumber { get; set; }
}

public sealed class SmsAccountFormViewModel
{
    public int? Id { get; set; }
    public int ClientId { get; set; }

    [Required(ErrorMessage = "عنوان الزامی است.")]
    [Display(Name = "عنوان")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "انتخاب Provider الزامی است.")]
    [Display(Name = "Provider پیامکی")]
    public int SmsProviderId { get; set; }

    [Required(ErrorMessage = "انتخاب هدف الزامی است.")]
    [Display(Name = "هدف حساب")]
    public string Purpose { get; set; } = "Bulk";

    [Display(Name = "کلید API")]
    public string? ApiKey { get; set; }

    [Display(Name = "نام کاربری")]
    public string? Username { get; set; }

    [Display(Name = "رمز عبور")]
    public string? Password { get; set; }

    [Display(Name = "شماره فرستنده")]
    public string? SenderNumber { get; set; }

    [Display(Name = "آدرس پایه (اختیاری)")]
    public string? BaseUrl { get; set; }

    [Display(Name = "نرخ ارسال در دقیقه")]
    [Range(1, 100000)]
    public int RatePerMinute { get; set; } = 3000;

    [Display(Name = "حساب پیش‌فرض برای این هدف")]
    public bool IsDefault { get; set; }

    [Display(Name = "فعال")]
    public bool IsActive { get; set; } = true;

    public List<(int Id, string Code, string Name)> Providers { get; set; } = [];

    public bool HasExistingApiKey { get; set; }
    public bool HasExistingUsername { get; set; }
    public bool HasExistingPassword { get; set; }
}
