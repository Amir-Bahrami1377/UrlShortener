using System.ComponentModel.DataAnnotations;

namespace Shortener.Admin.Models;

public sealed class ClientListItemViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool HasBulkAccount { get; set; }
    public bool HasOtpAccount { get; set; }
}

public sealed class ClientCreateViewModel
{
    [Required(ErrorMessage = "نام کلاینت الزامی است.")]
    [Display(Name = "نام کلاینت")]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "کد کلاینت الزامی است.")]
    [Display(Name = "کد کلاینت")]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;
}

public sealed class ClientEditViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "نام کلاینت الزامی است.")]
    [Display(Name = "نام کلاینت")]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "کد کلاینت")]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "فعال")]
    public bool IsActive { get; set; }
}
