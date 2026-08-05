using System.ComponentModel.DataAnnotations;

namespace Shortener.Admin.Models;

public sealed class MessageTemplateListViewModel
{
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public List<MessageTemplateListItemViewModel> Templates { get; set; } = [];
}

public sealed class MessageTemplateListItemViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string TemplateType { get; set; } = string.Empty;
    public int? ReportId { get; set; }
    public bool IsActive { get; set; }
}

public sealed class MessageTemplateFormViewModel
{
    public int? Id { get; set; }
    public int ClientId { get; set; }

    [Required(ErrorMessage = "عنوان الزامی است.")]
    [Display(Name = "عنوان")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "نوع قالب الزامی است.")]
    [Display(Name = "نوع قالب")]
    public string TemplateType { get; set; } = "DownloadLink";

    [Display(Name = "اسم چاپ — فقط برای قالب لینک")]
    public int? ReportId { get; set; }

    public List<(int Code, string Name)> PrintDefinitions { get; set; } = [];

    [Required(ErrorMessage = "متن قالب الزامی است.")]
    [Display(Name = "متن قالب")]
    [StringLength(1000)]
    public string Body { get; set; } = string.Empty;

    [Display(Name = "کد الگو در پنل پیامکی (اختیاری)")]
    public string? PatternCode { get; set; }

    [Display(Name = "فعال")]
    public bool IsActive { get; set; } = true;
}
