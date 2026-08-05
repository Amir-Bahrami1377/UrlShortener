using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Shortener.Admin.Models;
using Shortener.Infrastructure.Identity;

namespace Shortener.Admin.Controllers;

public class AccountController(SignInManager<AppUser> signInManager, UserManager<AppUser> userManager) : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToLocal(returnUrl);
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByNameAsync(model.Username);
        if (user is null || !user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "نام کاربری یا رمز عبور نادرست است.");
            return View(model);
        }

        var result = await signInManager.PasswordSignInAsync(user, model.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "به دلیل تلاش‌های ناموفق مکرر، حساب شما تا ۱۵ دقیقه دیگر قفل است.");
            return View(model);
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "نام کاربری یا رمز عبور نادرست است.");
            return View(model);
        }

        user.LastLoginAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        if (user.MustChangePassword)
        {
            return RedirectToAction(nameof(ChangePassword), new { forced = true });
        }

        return RedirectToLocal(model.ReturnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied() => View();

    [HttpGet]
    public IActionResult ChangePassword(bool forced = false) => View(new ChangePasswordViewModel { Forced = forced });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            model.Forced = user.MustChangePassword;
            return View(model);
        }

        user.MustChangePassword = false;
        await userManager.UpdateAsync(user);
        await signInManager.RefreshSignInAsync(user);

        TempData["StatusMessage"] = "رمز عبور با موفقیت تغییر کرد.";
        return RedirectToAction("Index", "Home");
    }

    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToAction("Index", "Home");
}
