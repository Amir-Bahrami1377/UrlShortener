using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shortener.Admin.Models;
using Shortener.Application.Abstractions;
using Shortener.Application.Services;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;

namespace Shortener.Admin.Controllers;

[Authorize(Policy = AdminPolicies.OperatorOrAbove)]
public class SmsAccountsController(
    AppDbContext db, ICredentialProtector protector, ISmsProviderFactory providerFactory) : Controller
{
    public async Task<IActionResult> Index(int clientId, CancellationToken ct)
    {
        var client = await db.Clients.FindAsync([clientId], ct);
        if (client is null)
        {
            return NotFound();
        }

        var accounts = await db.SmsAccounts
            .Where(a => a.ClientId == clientId)
            .Include(a => a.SmsProvider)
            .OrderBy(a => a.Purpose)
            .Select(a => new SmsAccountListItemViewModel
            {
                Id = a.Id,
                Title = a.Title,
                ProviderName = a.SmsProvider!.Name,
                Purpose = a.Purpose.ToString(),
                IsDefault = a.IsDefault,
                IsActive = a.IsActive,
                SenderNumber = a.SenderNumber,
            })
            .ToListAsync(ct);

        return View(new SmsAccountListViewModel { ClientId = clientId, ClientName = client.Name, Accounts = accounts });
    }

    [HttpGet]
    public async Task<IActionResult> Create(int clientId, CancellationToken ct)
    {
        if (await db.Clients.FindAsync([clientId], ct) is null)
        {
            return NotFound();
        }

        var model = new SmsAccountFormViewModel { ClientId = clientId, Providers = await GetProvidersAsync(ct) };
        return View("Form", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SmsAccountFormViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            model.Providers = await GetProvidersAsync(ct);
            return View("Form", model);
        }

        var account = new SmsAccount
        {
            ClientId = model.ClientId,
            SmsProviderId = model.SmsProviderId,
            Title = model.Title,
            Purpose = Enum.Parse<SmsAccountPurpose>(model.Purpose),
            ApiKeyEnc = protector.Protect(model.ApiKey),
            UsernameEnc = protector.Protect(model.Username),
            PasswordEnc = protector.Protect(model.Password),
            SenderNumber = model.SenderNumber,
            BaseUrl = model.BaseUrl,
            RatePerMinute = model.RatePerMinute,
            IsDefault = model.IsDefault,
            IsActive = model.IsActive,
        };

        if (model.IsDefault)
        {
            await ClearExistingDefaultAsync(model.ClientId, account.Purpose, ct);
        }

        db.SmsAccounts.Add(account);
        await db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = "حساب پیامکی ایجاد شد.";
        return RedirectToAction(nameof(Index), new { clientId = model.ClientId });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var account = await db.SmsAccounts.FindAsync([id], ct);
        if (account is null)
        {
            return NotFound();
        }

        var model = new SmsAccountFormViewModel
        {
            Id = account.Id,
            ClientId = account.ClientId,
            Title = account.Title,
            SmsProviderId = account.SmsProviderId,
            Purpose = account.Purpose.ToString(),
            ApiKey = account.ApiKeyEnc is null ? null : SmsAccountFormConstants.MaskedPlaceholder,
            Username = account.UsernameEnc is null ? null : SmsAccountFormConstants.MaskedPlaceholder,
            Password = account.PasswordEnc is null ? null : SmsAccountFormConstants.MaskedPlaceholder,
            SenderNumber = account.SenderNumber,
            BaseUrl = account.BaseUrl,
            RatePerMinute = account.RatePerMinute,
            IsDefault = account.IsDefault,
            IsActive = account.IsActive,
            Providers = await GetProvidersAsync(ct),
        };

        return View("Form", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(SmsAccountFormViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            model.Providers = await GetProvidersAsync(ct);
            return View("Form", model);
        }

        var account = await db.SmsAccounts.FindAsync([model.Id], ct);
        if (account is null)
        {
            return NotFound();
        }

        account.Title = model.Title;
        account.SmsProviderId = model.SmsProviderId;
        account.Purpose = Enum.Parse<SmsAccountPurpose>(model.Purpose);
        account.SenderNumber = model.SenderNumber;
        account.BaseUrl = model.BaseUrl;
        account.RatePerMinute = model.RatePerMinute;
        account.IsActive = model.IsActive;

        account.ApiKeyEnc = ResolveCredential(model.ApiKey, account.ApiKeyEnc, protector);
        account.UsernameEnc = ResolveCredential(model.Username, account.UsernameEnc, protector);
        account.PasswordEnc = ResolveCredential(model.Password, account.PasswordEnc, protector);

        if (model.IsDefault && !account.IsDefault)
        {
            await ClearExistingDefaultAsync(account.ClientId, account.Purpose, ct);
        }

        account.IsDefault = model.IsDefault;

        await db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = "تغییرات ذخیره شد.";
        return RedirectToAction(nameof(Index), new { clientId = account.ClientId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestSend(int id, string testPhoneNumber, CancellationToken ct)
    {
        var account = await db.SmsAccounts.Include(a => a.SmsProvider).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (account is null)
        {
            return NotFound();
        }

        try
        {
            var provider = providerFactory.Resolve(account.SmsProvider!.Code);
            var config = new SmsAccountConfig(
                protector.Unprotect(account.ApiKeyEnc), protector.Unprotect(account.UsernameEnc),
                protector.Unprotect(account.PasswordEnc), account.SenderNumber, account.BaseUrl, account.SettingsJson);
            var request = new SmsSendRequest(testPhoneNumber, "پیام آزمایشی از پنل مدیریت.", null, null, SmsMessageType.DownloadLink);

            var result = await provider.SendAsync(config, request, ct);
            TempData["StatusMessage"] = result.IsSuccess
                ? $"ارسال آزمایشی موفق بود (شناسه: {result.ProviderMessageId})."
                : $"ارسال آزمایشی ناموفق: {result.ErrorMessage}";
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            TempData["StatusMessage"] = $"Provider «{account.SmsProvider!.Name}» هنوز در این محیط پیاده‌سازی نشده است.";
        }

        return RedirectToAction(nameof(Index), new { clientId = account.ClientId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckCredit(int id, CancellationToken ct)
    {
        var account = await db.SmsAccounts.Include(a => a.SmsProvider).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (account is null)
        {
            return NotFound();
        }

        try
        {
            var provider = providerFactory.Resolve(account.SmsProvider!.Code);
            var config = new SmsAccountConfig(
                protector.Unprotect(account.ApiKeyEnc), protector.Unprotect(account.UsernameEnc),
                protector.Unprotect(account.PasswordEnc), account.SenderNumber, account.BaseUrl, account.SettingsJson);

            var credit = await provider.GetCreditAsync(config, ct);
            TempData["StatusMessage"] = credit is null ? "اعتبار در دسترس نیست." : $"اعتبار فعلی: {credit:N0}";
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            TempData["StatusMessage"] = $"Provider «{account.SmsProvider!.Name}» هنوز در این محیط پیاده‌سازی نشده است.";
        }

        return RedirectToAction(nameof(Index), new { clientId = account.ClientId });
    }

    private async Task ClearExistingDefaultAsync(int clientId, SmsAccountPurpose purpose, CancellationToken ct)
    {
        var others = await db.SmsAccounts
            .Where(a => a.ClientId == clientId && a.Purpose == purpose && a.IsDefault)
            .ToListAsync(ct);
        foreach (var other in others)
        {
            other.IsDefault = false;
        }
    }

    /// <summary>Blank or the unchanged mask sentinel means "keep what's stored"; anything else is a
    /// real new value that must be encrypted before it touches the database (§M2.4).</summary>
    private static string? ResolveCredential(string? submittedValue, string? existingEncryptedValue, ICredentialProtector protector) =>
        submittedValue switch
        {
            null or "" => existingEncryptedValue,
            SmsAccountFormConstants.MaskedPlaceholder => existingEncryptedValue,
            _ => protector.Protect(submittedValue),
        };

    private async Task<List<(int Id, string Code, string Name)>> GetProvidersAsync(CancellationToken ct) =>
        await db.SmsProviders.Where(p => p.IsActive).OrderBy(p => p.Name)
            .Select(p => new ValueTuple<int, string, string>(p.Id, p.Code, p.Name))
            .ToListAsync(ct);
}
