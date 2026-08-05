using System.ComponentModel.DataAnnotations;

namespace Shortener.Admin.Models;

public sealed class RetentionPolicyListItemViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ClientName { get; set; }
    public int? ReportId { get; set; }
    public string? BatchTag { get; set; }
    public int RetentionDays { get; set; }
    public int GraceDays { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; }
    public bool IsGlobal { get; set; }
}

public sealed class RetentionPolicyFormViewModel
{
    public int? Id { get; set; }
    public bool IsGlobal { get; set; }

    [Display(Name = "کلاینت (خالی = سراسری)")]
    public int? ClientId { get; set; }

    [Display(Name = "کد چاپ (ReportId، اختیاری)")]
    public int? ReportId { get; set; }

    [Display(Name = "برچسب دسته (BatchTag، اختیاری)")]
    public string? BatchTag { get; set; }

    [Required(ErrorMessage = "عنوان الزامی است.")]
    [Display(Name = "عنوان")]
    public string Title { get; set; } = string.Empty;

    [Range(1, 3650)]
    [Display(Name = "روزهای نگهداشت")]
    public int RetentionDays { get; set; } = 90;

    [Range(0, 365)]
    [Display(Name = "روزهای مهلت (Grace)")]
    public int GraceDays { get; set; } = 7;

    [Display(Name = "اولویت (عدد بزرگ‌تر = اولویت بالاتر)")]
    public int Priority { get; set; }

    [Display(Name = "فعال")]
    public bool IsActive { get; set; } = true;

    public List<(int Id, string Name)> Clients { get; set; } = [];
}

public sealed class RetentionImpactResult
{
    public int AffectedFileCount { get; set; }
    public int ImmediatelyExpiringCount { get; set; }
}
