using System.Security.Claims;
using DxReportReview.Web.Domain;
using DxReportReview.Web.Infrastructure;
using DxReportReview.Web.Models;
using DxReportReview.Web.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DxReportReview.Web.Controllers;

[Route("review")]
[Authorize]
public class ReviewController(
    IReviewRepository reviewRepository,
    IReportStorage reportStorage,
    TimeProvider time) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string? filter)
    {
        var all = await reviewRepository.GetAllAsync();
        var uid = CurrentUserId();

        if (User.IsInRole(Roles.Approver))
            all = all.Where(r => r.ApproverId == uid).ToList();
        else if (User.IsInRole(Roles.Designer))
            all = all.Where(r => r.SubmitterId == uid).ToList();

        var pendingCount = all.Count(r => r.Status == ReviewStatus.Pending);

        filter = string.IsNullOrWhiteSpace(filter) ? "all" : filter.Trim().ToLowerInvariant();
        IEnumerable<ReviewSubmission> query = all;
        query = filter switch
        {
            "pending" => query.Where(r => r.Status == ReviewStatus.Pending),
            "approved" => query.Where(r => r.Status == ReviewStatus.Approved),
            "rejected" => query.Where(r => r.Status == ReviewStatus.Rejected),
            _ => query
        };

        var items = query
            .OrderByDescending(r => r.SubmittedAt)
            .Select(r => new ReviewListItemModel
            {
                Id = r.Id,
                ReportDisplayName = r.ReportDisplayName,
                SubmitterDisplayName = r.SubmitterDisplayName,
                ApproverDisplayName = r.ApproverDisplayName,
                Status = r.Status,
                SubmittedAt = r.SubmittedAt,
                Description = r.Description,
                HasConflict = ReviewRules.HasConflict(r, all)
            })
            .ToList();

        ViewBag.Filter = filter;
        ViewBag.PendingCount = pendingCount;
        return View(items);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id) => PartialOrFull(await BuildDetail(id));

    [HttpGet("{id:int}/detail")]
    public async Task<IActionResult> DetailPartial(int id) => await BuildDetailPartial(id);

    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = Roles.Approver)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, [FromForm] string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return BadRequest(new { error = "A note is required." });

        var submission = await reviewRepository.GetByIdAsync(id);
        if (submission is null) return NotFound();

        var uid = CurrentUserId();
        if (submission.ApproverId != uid) return Forbid();

        if (submission.Status != ReviewStatus.Pending)
            return Conflict(new { error = "This submission is no longer pending." });

        var all = await reviewRepository.GetAllAsync();
        if (ReviewRules.HasConflict(submission, all))
            return Conflict(new { error = "The live report changed after this submission. Reject and re-submit." });

        if (submission.IsNewReport && submission.ReportId == 0)
        {
            _ = reportStorage.AddNewReport(submission.ReportDisplayName, submission.ProposedLayout);
        }
        else
        {
            var current = reportStorage.GetLayout(submission.ReportId);
            if (ReviewRules.HasConflict(submission, current))
                return Conflict(new { error = "The live report changed after this submission. Reject and re-submit." });

            reportStorage.SaveLayout(submission.ReportId, submission.ProposedLayout);
        }

        ReviewRules.Approve(submission, note, time.GetUtcNow().UtcDateTime);
        await reviewRepository.UpdateAsync(submission);

        if (IsAjax())
            return Json(new { ok = true, status = "Approved" });

        TempData["Message"] = "Submission approved and the report layout was updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = Roles.Approver)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, [FromForm] string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return BadRequest(new { error = "A note is required." });

        var submission = await reviewRepository.GetByIdAsync(id);
        if (submission is null) return NotFound();

        var uid = CurrentUserId();
        if (submission.ApproverId != uid) return Forbid();

        if (submission.Status != ReviewStatus.Pending)
            return Conflict(new { error = "This submission is no longer pending." });

        ReviewRules.Reject(submission, note, time.GetUtcNow().UtcDateTime);
        await reviewRepository.UpdateAsync(submission);

        if (IsAjax())
            return Json(new { ok = true, status = "Rejected" });

        TempData["Message"] = "Submission rejected.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/reedit")]
    [Authorize(Roles = Roles.Designer)]
    public async Task<IActionResult> ReEdit(int id)
    {
        var submission = await reviewRepository.GetByIdAsync(id);
        if (submission is null) return NotFound();

        var uid = CurrentUserId();
        if (submission.SubmitterId != uid) return Forbid();

        if (submission.Status != ReviewStatus.Rejected)
        {
            TempData["Error"] = "Only rejected reviews can be re-edited.";
            return RedirectToAction(nameof(Index));
        }

        var all = await reviewRepository.GetAllAsync();
        var block = ReviewRules.GetBlockReason(submission, all);
        if (block is not null)
        {
            TempData["Error"] = ReviewRules.BlockReasonText(block) ?? "This review cannot be re-edited.";
            return RedirectToAction(nameof(Index));
        }

        HttpContext.Session.SetString(ReviewSessionKeys.ParentReviewId, id.ToString());
        return RedirectToAction(
            nameof(ReportController.Designer), "Report",
            new { id = submission.ReportId, reedit = id });
    }

    private async Task<IActionResult> BuildDetailPartial(int id)
    {
        var model = await BuildDetail(id);
        if (model is null) return NotFound();
        return PartialView("_Detail", model);
    }

    private async Task<ReviewDetailModel?> BuildDetail(int id)
    {
        var submission = await reviewRepository.GetByIdAsync(id);
        if (submission is null) return null;

        var uid = CurrentUserId();
        if (User.IsInRole(Roles.Approver) && submission.ApproverId != uid) return null;
        if (User.IsInRole(Roles.Designer) && submission.SubmitterId != uid) return null;

        var all = await reviewRepository.GetAllAsync();
        var conflict = ReviewRules.HasConflict(submission, all);
        var block = ReviewRules.GetBlockReason(submission, all);
        var childId = all.FirstOrDefault(r => r.ParentReviewId == submission.Id)?.Id;

        var canAct = User.IsInRole(Roles.Approver)
                     && submission.ApproverId == uid
                     && submission.Status == ReviewStatus.Pending
                     && !conflict;

        return new ReviewDetailModel
        {
            Id = submission.Id,
            ReportId = submission.ReportId,
            ReportDisplayName = submission.ReportDisplayName,
            SubmitterDisplayName = submission.SubmitterDisplayName,
            ApproverDisplayName = submission.ApproverDisplayName,
            Description = submission.Description,
            Note = submission.Note,
            Status = submission.Status,
            SubmittedAt = submission.SubmittedAt,
            ProcessedAt = submission.ProcessedAt,
            CurrentViewerUrl = Url.Action(
                nameof(ReportController.ViewerFrame), "Report",
                new { url = $"REVIEW_CURRENT_{submission.Id}" })!,
            ProposedViewerUrl = Url.Action(
                nameof(ReportController.ViewerFrame), "Report",
                new { url = $"REVIEW_PROPOSED_{submission.Id}" })!,
            CanAct = canAct,
            HasConflict = conflict,
            BlockReason = ReviewRules.BlockReasonText(block),
            ParentReviewId = submission.ParentReviewId,
            ChildReviewId = childId,
            IsSubmitter = submission.SubmitterId == uid
        };
    }

    private IActionResult PartialOrFull(ReviewDetailModel? model)
    {
        if (model is null) return NotFound();
        if (IsAjax()) return PartialView("_Detail", model);
        return View(model);
    }

    private int CurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (claim is null || !int.TryParse(claim, out var id))
            throw new UnauthorizedAccessException("Missing or invalid identity claim.");
        return id;
    }

    private bool IsAjax() =>
        string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
}
