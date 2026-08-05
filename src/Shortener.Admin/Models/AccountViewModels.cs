using System.ComponentModel.DataAnnotations;

namespace Shortener.Admin.Models;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "نام کاربری الزامی است.")]
    [Display(Name = "نام کاربری")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "رمز عبور الزامی است.")]
    [DataType(DataType.Password)]
    [Display(Name = "رمز عبور")]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

public sealed class ChangePasswordViewModel
{
    public bool Forced { get; set; }

    [Required(ErrorMessage = "رمز عبور فعلی الزامی است.")]
    [DataType(DataType.Password)]
    [Display(Name = "رمز عبور فعلی")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "رمز عبور جدید الزامی است.")]
    [DataType(DataType.Password)]
    [Display(Name = "رمز عبور جدید")]
    [MinLength(10, ErrorMessage = "رمز عبور باید حداقل ۱۰ کاراکتر باشد.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "تکرار رمز عبور الزامی است.")]
    [DataType(DataType.Password)]
    [Display(Name = "تکرار رمز عبور جدید")]
    [Compare(nameof(NewPassword), ErrorMessage = "رمز عبور جدید و تکرار آن یکسان نیستند.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
