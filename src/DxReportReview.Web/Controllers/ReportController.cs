using DxReportReview.Web.Infrastructure;
using DxReportReview.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DxReportReview.Web.Controllers;

[Route("report")]
[Authorize]
public class ReportController : Controller
{
    [HttpGet("designer/{id:int}")]
    [Authorize(Roles = Roles.Designer)]
    public IActionResult Designer(int id, [FromQuery] int? reedit)
    {
        var key = reedit.HasValue ? $"REEDIT_{reedit.Value}" : id.ToString();
        return View(new ReportDesignerPageModel { ReportKey = key });
    }

    [HttpGet("viewer/{id:int?}")]
    public IActionResult Viewer(int? id, [FromQuery] string? url)
    {
        var key = !string.IsNullOrWhiteSpace(url) ? url.Trim() : id?.ToString() ?? "1";
        return View(new ReportViewerPageModel { ReportKey = key });
    }

    [HttpGet("viewer-frame/{id:int?}")]
    public IActionResult ViewerFrame(int? id, [FromQuery] string? url)
    {
        var key = !string.IsNullOrWhiteSpace(url) ? url.Trim() : id?.ToString() ?? "1";
        ViewData["UseFrameLayout"] = true;
        return View("Viewer", new ReportViewerPageModel { ReportKey = key });
    }

    [HttpPost("set-approver")]
    [Authorize(Roles = Roles.Designer)]
    [ValidateAntiForgeryToken]
    public IActionResult SetApprover([FromBody] SetApproverRequest body)
    {
        if (body is null || body.ApproverId <= 0)
            return BadRequest(new { error = "approverId is required." });

        HttpContext.Session.SetInt32(ReviewSessionKeys.ApproverId, body.ApproverId);
        HttpContext.Session.SetString(ReviewSessionKeys.Description, body.Description ?? "");
        HttpContext.Session.SetString(ReviewSessionKeys.IsNewReport, body.IsNewReport ? "1" : "0");
        if (body.ParentReviewId is { } p)
            HttpContext.Session.SetString(ReviewSessionKeys.ParentReviewId, p.ToString());
        else
            HttpContext.Session.Remove(ReviewSessionKeys.ParentReviewId);

        return Ok(new { ok = true });
    }
}
