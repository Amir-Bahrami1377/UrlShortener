using System.ComponentModel.DataAnnotations;

namespace Shortener.Admin.Models;

public sealed class ApiKeyListViewModel
{
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public List<ApiKeyListItemViewModel> Keys { get; set; } = [];
}

public sealed class ApiKeyListItemViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ApiKeyCreateViewModel
{
    public int ClientId { get; set; }

    [Required(ErrorMessage = "عنوان کلید الزامی است.")]
    [Display(Name = "عنوان کلید")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "تاریخ انقضا (اختیاری)")]
    [DataType(DataType.Date)]
    public DateTime? ExpiresAt { get; set; }
}

public sealed class ApiKeyCreatedViewModel
{
    public int ClientId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string RawKey { get; set; } = string.Empty;
}
