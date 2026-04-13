using DxReportReview.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DxReportReview.Web.Controllers;

public class HomeController : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated != true)
            return RedirectToAction(nameof(AuthController.Login), "Auth");

        if (User.IsInRole(Roles.Approver))
            return RedirectToAction(nameof(ReviewController.Index), "Review");

        return RedirectToAction(nameof(ReportController.Designer), "Report", new { id = 1 });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();
}
