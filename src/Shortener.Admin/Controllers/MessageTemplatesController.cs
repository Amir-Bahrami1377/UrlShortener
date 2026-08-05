using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shortener.Admin.Models;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;

namespace Shortener.Admin.Controllers;

[Authorize(Policy = AdminPolicies.OperatorOrAbove)]
public class MessageTemplatesController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index(int clientId, CancellationToken ct)
    {
        var client = await db.Clients.FindAsync([clientId], ct);
        if (client is null)
        {
            return NotFound();
        }

        var templates = await db.MessageTemplates
            .Where(t => t.ClientId == clientId)
            .OrderBy(t => t.TemplateType).ThenBy(t => t.ReportId)
            .Select(t => new MessageTemplateListItemViewModel
            {
                Id = t.Id,
                Title = t.Title,
                TemplateType = t.TemplateType.ToString(),
                ReportId = t.ReportId,
                IsActive = t.IsActive,
            })
            .ToListAsync(ct);

        return View(new MessageTemplateListViewModel { ClientId = clientId, ClientName = client.Name, Templates = templates });
    }

    [HttpGet]
    public async Task<IActionResult> Create(int clientId, CancellationToken ct)
    {
        if (await db.Clients.FindAsync([clientId], ct) is null)
        {
            return NotFound();
        }

        return View("Form", new MessageTemplateFormViewModel { ClientId = clientId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MessageTemplateFormViewModel model, CancellationToken ct)
    {
        ValidatePlaceholders(model);
        if (!ModelState.IsValid)
        {
            return View("Form", model);
        }

        var type = Enum.Parse<TemplateType>(model.TemplateType);
        db.MessageTemplates.Add(new MessageTemplate
        {
            ClientId = model.ClientId,
            ReportId = type == TemplateType.Otp ? null : model.ReportId,
            TemplateType = type,
            Title = model.Title,
            Body = model.Body,
            PatternCode = model.PatternCode,
            IsActive = model.IsActive,
        });
        await db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = "قالب ایجاد شد.";
        return RedirectToAction(nameof(Index), new { clientId = model.ClientId });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var template = await db.MessageTemplates.FindAsync([id], ct);
        if (template is null)
        {
            return NotFound();
        }

        return View("Form", new MessageTemplateFormViewModel
        {
            Id = template.Id,
            ClientId = template.ClientId,
            Title = template.Title,
            TemplateType = template.TemplateType.ToString(),
            ReportId = template.ReportId,
            Body = template.Body,
            PatternCode = template.PatternCode,
            IsActive = template.IsActive,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(MessageTemplateFormViewModel model, CancellationToken ct)
    {
        ValidatePlaceholders(model);
        if (!ModelState.IsValid)
        {
            return View("Form", model);
        }

        var template = await db.MessageTemplates.FindAsync([model.Id], ct);
        if (template is null)
        {
            return NotFound();
        }

        var type = Enum.Parse<TemplateType>(model.TemplateType);
        template.Title = model.Title;
        template.TemplateType = type;
        template.ReportId = type == TemplateType.Otp ? null : model.ReportId;
        template.Body = model.Body;
        template.PatternCode = model.PatternCode;
        template.IsActive = model.IsActive;
        await db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = "تغییرات ذخیره شد.";
        return RedirectToAction(nameof(Index), new { clientId = template.ClientId });
    }

    private void ValidatePlaceholders(MessageTemplateFormViewModel model)
    {
        if (model.TemplateType == nameof(TemplateType.Otp) && !model.Body.Contains("{otp}", StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(model.Body), "قالب OTP حتماً باید شامل {otp} باشد.");
        }

        if (model.TemplateType == nameof(TemplateType.DownloadLink) &&
            !model.Body.Contains("{shortUrl}", StringComparison.Ordinal) && !model.Body.Contains("{code}", StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(model.Body), "قالب لینک حتماً باید شامل {shortUrl} یا {code} باشد.");
        }
    }
}
