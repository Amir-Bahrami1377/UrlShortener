using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Shortener.Admin.Models;

namespace Shortener.Admin.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => RedirectToAction("Dashboard", "Reports");

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
