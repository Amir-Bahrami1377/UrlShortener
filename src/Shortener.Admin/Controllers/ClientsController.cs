using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shortener.Admin.Models;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;

namespace Shortener.Admin.Controllers;

[Authorize(Policy = AdminPolicies.OperatorOrAbove)]
public class ClientsController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var clients = await db.Clients.OrderBy(c => c.Name).ToListAsync(ct);
        var accountPurposes = await db.SmsAccounts
            .Where(a => a.IsActive)
            .Select(a => new { a.ClientId, a.Purpose })
            .ToListAsync(ct);

        var items = clients.Select(c => new ClientListItemViewModel
        {
            Id = c.Id,
            Name = c.Name,
            Code = c.Code,
            IsActive = c.IsActive,
            HasBulkAccount = accountPurposes.Any(a => a.ClientId == c.Id && a.Purpose == SmsAccountPurpose.Bulk),
            HasOtpAccount = accountPurposes.Any(a => a.ClientId == c.Id && a.Purpose == SmsAccountPurpose.Otp),
        }).ToList();

        return View(items);
    }

    [HttpGet]
    public IActionResult Create() => View(new ClientCreateViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ClientCreateViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (await db.Clients.AnyAsync(c => c.Code == model.Code, ct))
        {
            ModelState.AddModelError(nameof(model.Code), "این کد قبلاً استفاده شده است.");
            return View(model);
        }

        db.Clients.Add(new Client { Name = model.Name, Code = model.Code, IsActive = true });
        await db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = "کلاینت با موفقیت ایجاد شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var client = await db.Clients.FindAsync([id], ct);
        if (client is null)
        {
            return NotFound();
        }

        return View(new ClientEditViewModel { Id = client.Id, Name = client.Name, Code = client.Code, IsActive = client.IsActive });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ClientEditViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var client = await db.Clients.FindAsync([model.Id], ct);
        if (client is null)
        {
            return NotFound();
        }

        client.Name = model.Name;
        client.IsActive = model.IsActive;
        await db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = "تغییرات ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }
}
