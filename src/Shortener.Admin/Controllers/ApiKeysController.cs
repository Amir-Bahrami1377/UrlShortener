using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shortener.Admin.Models;
using Shortener.Application.Abstractions;
using Shortener.Domain.Entities;
using Shortener.Infrastructure.Persistence;
using StackExchange.Redis;

namespace Shortener.Admin.Controllers;

[Authorize(Policy = AdminPolicies.OperatorOrAbove)]
public class ApiKeysController(AppDbContext db, IApiKeyGenerator keyGenerator, IConnectionMultiplexer redis) : Controller
{
    public async Task<IActionResult> Index(int clientId, CancellationToken ct)
    {
        var client = await db.Clients.FindAsync([clientId], ct);
        if (client is null)
        {
            return NotFound();
        }

        var keys = await db.ApiKeys
            .Where(k => k.ClientId == clientId)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyListItemViewModel
            {
                Id = k.Id,
                Title = k.Title,
                KeyPrefix = k.KeyPrefix,
                IsActive = k.IsActive,
                ExpiresAt = k.ExpiresAt,
                LastUsedAt = k.LastUsedAt,
                RevokedAt = k.RevokedAt,
                CreatedAt = k.CreatedAt,
            })
            .ToListAsync(ct);

        return View(new ApiKeyListViewModel { ClientId = clientId, ClientName = client.Name, Keys = keys });
    }

    [HttpGet]
    public async Task<IActionResult> Create(int clientId, CancellationToken ct)
    {
        if (await db.Clients.FindAsync([clientId], ct) is null)
        {
            return NotFound();
        }

        return View(new ApiKeyCreateViewModel { ClientId = clientId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ApiKeyCreateViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var generated = keyGenerator.Generate();
        db.ApiKeys.Add(new ApiKey
        {
            ClientId = model.ClientId,
            KeyHash = generated.KeyHash,
            KeyPrefix = generated.KeyPrefix,
            Title = model.Title,
            ExpiresAt = model.ExpiresAt,
            IsActive = true,
        });
        await db.SaveChangesAsync(ct);

        // Shown exactly once — never persisted or logged in raw form (§M2.3).
        return View("Created", new ApiKeyCreatedViewModel { ClientId = model.ClientId, Title = model.Title, RawKey = generated.RawKey });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(int id, int clientId, CancellationToken ct)
    {
        var apiKey = await db.ApiKeys.FindAsync([id], ct);
        if (apiKey is null)
        {
            return NotFound();
        }

        apiKey.IsActive = false;
        apiKey.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await redis.GetDatabase().KeyDeleteAsync($"apikey:{apiKey.KeyHash}");

        TempData["StatusMessage"] = "کلید ابطال شد.";
        return RedirectToAction(nameof(Index), new { clientId });
    }
}
