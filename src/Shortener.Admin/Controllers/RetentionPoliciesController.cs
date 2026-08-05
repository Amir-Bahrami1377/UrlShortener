using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shortener.Admin.Models;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;

namespace Shortener.Admin.Controllers;

/// <summary>§M2.6 — retention policy management is SuperAdmin-only; the global fallback policy can never be deleted.</summary>
[Authorize(Policy = AdminPolicies.SuperAdminOnly)]
public class RetentionPoliciesController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var policies = await db.RetentionPolicies
            .Include(p => p.Client)
            .OrderByDescending(p => p.Priority)
            .Select(p => new RetentionPolicyListItemViewModel
            {
                Id = p.Id,
                Title = p.Title,
                ClientName = p.Client == null ? null : p.Client.Name,
                ReportId = p.ReportId,
                BatchTag = p.BatchTag,
                RetentionDays = p.RetentionDays,
                GraceDays = p.GraceDays,
                Priority = p.Priority,
                IsActive = p.IsActive,
                IsGlobal = p.ClientId == null,
            })
            .ToListAsync(ct);

        return View(policies);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct) =>
        View("Form", new RetentionPolicyFormViewModel { Clients = await GetClientsAsync(ct) });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RetentionPolicyFormViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            model.Clients = await GetClientsAsync(ct);
            return View("Form", model);
        }

        db.RetentionPolicies.Add(new RetentionPolicy
        {
            ClientId = model.ClientId,
            ReportId = model.ReportId,
            BatchTag = string.IsNullOrWhiteSpace(model.BatchTag) ? null : model.BatchTag,
            Title = model.Title,
            RetentionDays = model.RetentionDays,
            GraceDays = model.GraceDays,
            Priority = model.Priority,
            IsActive = model.IsActive,
        });
        await db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = "سیاست نگهداشت ایجاد شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var policy = await db.RetentionPolicies.FindAsync([id], ct);
        if (policy is null)
        {
            return NotFound();
        }

        return View("Form", new RetentionPolicyFormViewModel
        {
            Id = policy.Id,
            IsGlobal = policy.ClientId is null,
            ClientId = policy.ClientId,
            ReportId = policy.ReportId,
            BatchTag = policy.BatchTag,
            Title = policy.Title,
            RetentionDays = policy.RetentionDays,
            GraceDays = policy.GraceDays,
            Priority = policy.Priority,
            IsActive = policy.IsActive,
            Clients = await GetClientsAsync(ct),
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(RetentionPolicyFormViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            model.Clients = await GetClientsAsync(ct);
            return View("Form", model);
        }

        var policy = await db.RetentionPolicies.FindAsync([model.Id], ct);
        if (policy is null)
        {
            return NotFound();
        }

        // Scope (Client/ReportId/BatchTag) is set once at creation, same as elsewhere in the panel —
        // only the policy's own numbers are editable. The global policy's ClientId stays null.
        policy.Title = model.Title;
        policy.RetentionDays = model.RetentionDays;
        policy.GraceDays = model.GraceDays;
        policy.Priority = model.Priority;
        policy.IsActive = model.IsActive;
        await db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = "تغییرات ذخیره شد.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>AJAX-backed impact preview (§M2.6): how many stored files match this scope, and how
    /// many would become immediately expired if RetentionDays were applied right now.</summary>
    [HttpGet]
    public async Task<IActionResult> PreviewImpact(int? clientId, int? reportId, string? batchTag, int retentionDays, CancellationToken ct)
    {
        var query = db.StoredFiles.Where(f => f.Status == FileStatus.Active).AsQueryable();

        if (clientId is not null)
        {
            query = query.Where(f => f.ClientId == clientId);
        }

        if (reportId is not null || !string.IsNullOrWhiteSpace(batchTag))
        {
            var linkQuery = db.ShortLinks.AsQueryable();
            if (reportId is not null)
            {
                linkQuery = linkQuery.Where(l => l.ReportId == reportId);
            }

            if (!string.IsNullOrWhiteSpace(batchTag))
            {
                linkQuery = linkQuery.Where(l => l.BatchTag == batchTag);
            }

            var storedFileIds = linkQuery.Select(l => l.StoredFileId);
            query = query.Where(f => storedFileIds.Contains(f.Id));
        }

        var affected = await query.Select(f => new { f.StoredAt }).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var expiring = affected.Count(f => f.StoredAt.AddDays(retentionDays) < now);

        return Json(new RetentionImpactResult { AffectedFileCount = affected.Count, ImmediatelyExpiringCount = expiring });
    }

    private async Task<List<(int Id, string Name)>> GetClientsAsync(CancellationToken ct) =>
        await db.Clients.OrderBy(c => c.Name).Select(c => new ValueTuple<int, string>(c.Id, c.Name)).ToListAsync(ct);
}
