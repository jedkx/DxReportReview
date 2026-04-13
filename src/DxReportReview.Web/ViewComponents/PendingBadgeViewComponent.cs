using System.Security.Claims;
using DxReportReview.Web.Domain;
using DxReportReview.Web.Infrastructure;
using DxReportReview.Web.Storage;
using Microsoft.AspNetCore.Mvc;

namespace DxReportReview.Web.ViewComponents;

public class PendingBadgeViewComponent(IReviewRepository reviewRepository) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (UserClaimsPrincipal.Identity?.IsAuthenticated != true)
            return Content(string.Empty);

        var claim = UserClaimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (claim is null || !int.TryParse(claim, out var uid))
            return Content(string.Empty);
        var all = await reviewRepository.GetAllAsync();

        int count;
        if (UserClaimsPrincipal.IsInRole(Roles.Approver))
            count = all.Count(r => r.ApproverId == uid && r.Status == ReviewStatus.Pending);
        else
            count = all.Count(r => r.SubmitterId == uid && r.Status == ReviewStatus.Pending);

        return View(count);
    }
}
