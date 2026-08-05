using System.ComponentModel.DataAnnotations;

namespace Shortener.Admin.Models;

public sealed class PrintDefinitionListViewModel
{
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public List<PrintDefinitionListItemViewModel> Items { get; set; } = [];
}

public sealed class PrintDefinitionListItemViewModel
{
    public int Id { get; set; }
    public int Code { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class PrintDefinitionFormViewModel
{
    public int? Id { get; set; }
    public int ClientId { get; set; }

    [Required(ErrorMessage = "کد چاپ الزامی است.")]
    [Display(Name = "کد چاپ")]
    public int Code { get; set; }

    [Required(ErrorMessage = "اسم چاپ الزامی است.")]
    [Display(Name = "اسم چاپ")]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;
}
