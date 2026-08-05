using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shortener.Admin.Models;
using Shortener.Domain.Entities;
using Shortener.Infrastructure.Persistence;

namespace Shortener.Admin.Controllers;

[Authorize(Policy = AdminPolicies.OperatorOrAbove)]
public class PrintDefinitionsController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index(int clientId, CancellationToken ct)
    {
        var client = await db.Clients.FindAsync([clientId], ct);
        if (client is null)
        {
            return NotFound();
        }

        var items = await db.PrintDefinitions
            .Where(p => p.ClientId == clientId)
            .OrderBy(p => p.Code)
            .Select(p => new PrintDefinitionListItemViewModel { Id = p.Id, Code = p.Code, Name = p.Name })
            .ToListAsync(ct);

        return View(new PrintDefinitionListViewModel { ClientId = clientId, ClientName = client.Name, Items = items });
    }

    [HttpGet]
    public async Task<IActionResult> Create(int clientId, CancellationToken ct)
    {
        if (await db.Clients.FindAsync([clientId], ct) is null)
        {
            return NotFound();
        }

        return View("Form", new PrintDefinitionFormViewModel { ClientId = clientId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PrintDefinitionFormViewModel model, CancellationToken ct)
    {
        await ValidateUniqueCodeAsync(model, ct);
        if (!ModelState.IsValid)
        {
            return View("Form", model);
        }

        db.PrintDefinitions.Add(new PrintDefinition { ClientId = model.ClientId, Code = model.Code, Name = model.Name });
        await db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = "اسم چاپ ایجاد شد.";
        return RedirectToAction(nameof(Index), new { clientId = model.ClientId });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var item = await db.PrintDefinitions.FindAsync([id], ct);
        if (item is null)
        {
            return NotFound();
        }

        return View("Form", new PrintDefinitionFormViewModel
        {
            Id = item.Id,
            ClientId = item.ClientId,
            Code = item.Code,
            Name = item.Name,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(PrintDefinitionFormViewModel model, CancellationToken ct)
    {
        await ValidateUniqueCodeAsync(model, ct);
        if (!ModelState.IsValid)
        {
            return View("Form", model);
        }

        var item = await db.PrintDefinitions.FindAsync([model.Id], ct);
        if (item is null)
        {
            return NotFound();
        }

        item.Code = model.Code;
        item.Name = model.Name;
        await db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = "تغییرات ذخیره شد.";
        return RedirectToAction(nameof(Index), new { clientId = item.ClientId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, int clientId, CancellationToken ct)
    {
        var item = await db.PrintDefinitions.FindAsync([id], ct);
        if (item is null)
        {
            return NotFound();
        }

        db.PrintDefinitions.Remove(item);
        await db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = "اسم چاپ حذف شد.";
        return RedirectToAction(nameof(Index), new { clientId });
    }

    private async Task ValidateUniqueCodeAsync(PrintDefinitionFormViewModel model, CancellationToken ct)
    {
        var isDuplicate = await db.PrintDefinitions.AnyAsync(
            p => p.ClientId == model.ClientId && p.Code == model.Code && p.Id != model.Id, ct);
        if (isDuplicate)
        {
            ModelState.AddModelError(nameof(model.Code), "این کد چاپ قبلاً برای این کلاینت تعریف شده است.");
        }
    }
}
