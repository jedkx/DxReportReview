using System.Security.Claims;
using System.Text.RegularExpressions;
using DevExpress.XtraReports.UI;
using DevExpress.XtraReports.Web.ClientControls;
using DevExpress.XtraReports.Web.Extensions;
using DxReportReview.Web.Domain;
using DxReportReview.Web.Fixtures;
using DxReportReview.Web.Infrastructure;
using DxReportReview.Web.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DxReportReview.Web.Reporting;

public sealed class ReviewAwareReportStorage(
    IHttpContextAccessor httpContextAccessor,
    IReportStorage reportStorage,
    IReviewRepository reviewRepository,
    ILogger<ReviewAwareReportStorage> logger) : ReportStorageWebExtension
{
    private static readonly Regex ProposedPattern = new(@"^REVIEW_PROPOSED_(\d+)$", RegexOptions.Compiled);
    private static readonly Regex CurrentPattern = new(@"^REVIEW_CURRENT_(\d+)$", RegexOptions.Compiled);
    private static readonly Regex ReEditPattern = new(@"^REEDIT_(\d+)$", RegexOptions.Compiled);

    private HttpContext? Http => httpContextAccessor.HttpContext;

    public override bool CanSetData(string url) => IsValidUrl(url);

    public override bool IsValidUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        if (ProposedPattern.IsMatch(url) || CurrentPattern.IsMatch(url) || ReEditPattern.IsMatch(url))
            return true;
        return int.TryParse(url, out _);
    }

    public override byte[] GetData(string url)
    {
        var m = ProposedPattern.Match(url);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var reviewId))
        {
            var sub = reviewRepository.GetById(reviewId)
                      ?? throw new FaultException($"Review '{reviewId}' was not found.");
            EnsureReviewAccess(sub);
            return BuildHighlightedLayout(sub.ProposedLayout, sub.BaselineLayout, isBase: false, logger);
        }

        m = CurrentPattern.Match(url);
        if (m.Success && int.TryParse(m.Groups[1].Value, out reviewId))
        {
            var sub = reviewRepository.GetById(reviewId)
                      ?? throw new FaultException($"Review '{reviewId}' was not found.");
            EnsureReviewAccess(sub);
            return BuildHighlightedLayout(sub.BaselineLayout, sub.ProposedLayout, isBase: true, logger);
        }

        m = ReEditPattern.Match(url);
        if (m.Success && int.TryParse(m.Groups[1].Value, out reviewId))
        {
            var sub = reviewRepository.GetById(reviewId)
                      ?? throw new FaultException($"Review '{reviewId}' was not found.");
            EnsureReviewAccess(sub);
            return (byte[])sub.ProposedLayout.Clone();
        }

        if (!int.TryParse(url, out var reportId))
            throw new FaultException($"Invalid report url '{url}'.");

        try
        {
            return reportStorage.GetLayout(reportId);
        }
        catch (InvalidOperationException)
        {
            throw new FaultException($"Could not find report '{url}'.");
        }
    }

    public override Dictionary<string, string> GetUrls()
    {
        return reportStorage.GetAllReports()
            .ToDictionary(x => x.Id.ToString(), x => x.DisplayName);
    }

    public override void SetData(XtraReport report, string url)
    {
        var reportId = ResolveReportId(url);

        if (TryConsumeReviewMode(out var ctx))
        {
            if (ReviewRules.HasBlockingPendingReview(GetAllSubmissionsSync(), reportId))
                throw new FaultException("A pending review already exists for this report.");

            using var ms = new MemoryStream();
            report.SaveLayoutToXml(ms);
            var proposed = ms.ToArray();
            var baseline = reportStorage.GetLayout(reportId);

            _ = reviewRepository.AddAsync(new ReviewSubmission
            {
                ReportId = reportId,
                ReportDisplayName = ResolveDisplayName(reportId),
                SubmitterId = ctx.SubmitterId,
                SubmitterDisplayName = ctx.SubmitterDisplayName,
                ApproverId = ctx.ApproverId,
                ApproverDisplayName = ctx.ApproverDisplayName,
                Description = ctx.Description,
                Status = ReviewStatus.Pending,
                ProposedLayout = proposed,
                BaselineLayout = (byte[])baseline.Clone(),
                SubmittedAt = DateTime.UtcNow,
                ParentReviewId = ctx.ParentReviewId,
                IsNewReport = ctx.IsNewReport
            }).GetAwaiter().GetResult();

            ClearReviewSession();
            return;
        }

        using (var ms = new MemoryStream())
        {
            report.SaveLayoutToXml(ms);
            reportStorage.SaveLayout(reportId, ms.ToArray());
        }
    }

    public override string SetNewData(XtraReport report, string defaultUrl)
    {
        using var ms = new MemoryStream();
        report.SaveLayoutToXml(ms);
        var proposed = ms.ToArray();
        var displayName = string.IsNullOrWhiteSpace(defaultUrl) ? "Untitled" : defaultUrl.Trim();

        if (TryConsumeReviewMode(out var ctx))
        {
            _ = reviewRepository.AddAsync(new ReviewSubmission
            {
                ReportId = 0,
                ReportDisplayName = displayName,
                SubmitterId = ctx.SubmitterId,
                SubmitterDisplayName = ctx.SubmitterDisplayName,
                ApproverId = ctx.ApproverId,
                ApproverDisplayName = ctx.ApproverDisplayName,
                Description = ctx.Description,
                Status = ReviewStatus.Pending,
                ProposedLayout = proposed,
                BaselineLayout = (byte[])ReportDiffService.GetEmptyLayoutTemplateBytes().Clone(),
                SubmittedAt = DateTime.UtcNow,
                IsNewReport = true
            }).GetAwaiter().GetResult();

            ClearReviewSession();
            return "0";
        }

        var id = reportStorage.AddNewReport(displayName, proposed);
        return id.ToString();
    }

    private static byte[] BuildHighlightedLayout(byte[] primary, byte[] compare, bool isBase, ILogger logger)
    {
        // [] isn't valid layout XML (new-report submissions used to save empty baseline).
        if (primary.Length == 0)
            primary = (byte[])ReportDiffService.GetEmptyLayoutTemplateBytes().Clone();
        if (compare.Length == 0)
            compare = (byte[])ReportDiffService.GetEmptyLayoutTemplateBytes().Clone();

        try
        {
            var primaryReport = ReportDiffService.DeserializeReport(primary);
            var compareReport = ReportDiffService.DeserializeReport(compare);

            var diff = ReportDiffService.ComputeDiff(
                isBase ? primaryReport : compareReport,
                isBase ? compareReport : primaryReport);

            if (diff.Count == 0)
                return (byte[])primary.Clone();

            ReportDiffService.ApplyHighlights(primaryReport, diff, isBase);
            return ReportDiffService.SerializeReport(primaryReport);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Review diff highlight failed; serving layout without diff borders.");
            return (byte[])primary.Clone();
        }
    }

    private void EnsureReviewAccess(ReviewSubmission sub)
    {
        var http = Http;
        if (http?.User.Identity?.IsAuthenticated != true)
            throw new FaultException("Authentication required.");

        var sid = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(sid, out var uid))
            throw new FaultException("Authentication required.");

        if (sub.SubmitterId != uid && sub.ApproverId != uid)
            throw new FaultException("Access denied.");
    }

    private int ResolveReportId(string url)
    {
        if (int.TryParse(url, out var id))
            return id;

        var m = ReEditPattern.Match(url);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var reviewId))
        {
            var sub = reviewRepository.GetById(reviewId);
            if (sub is not null)
                return sub.ReportId;
        }

        throw new FaultException($"Cannot resolve report id from url '{url}'.");
    }

    private List<ReviewSubmission> GetAllSubmissionsSync() =>
        reviewRepository.GetAllAsync().GetAwaiter().GetResult();

    private string ResolveDisplayName(int reportId)
    {
        foreach (var (id, name) in reportStorage.GetAllReports())
            if (id == reportId)
                return name;
        return $"Report {reportId}";
    }

    private bool TryConsumeReviewMode(out ReviewSaveContext ctx)
    {
        ctx = default!;
        var http = Http;
        if (http?.User.Identity?.IsAuthenticated != true)
            return false;

        if (!http.Items.TryGetValue(ReviewSessionKeys.ApproverId, out var raw) || raw is not int approverId)
            return false;

        var approver = Users.All.FirstOrDefault(u => u.Id == approverId);
        if (approver is null)
            return false;

        var sid = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(sid, out var submitterId))
            return false;

        ctx = new ReviewSaveContext(
            approverId,
            approver.DisplayName,
            submitterId,
            http.User.FindFirst(ClaimTypes.Name)?.Value ?? "User",
            http.Session.GetString(ReviewSessionKeys.Description) is { Length: > 0 } d ? d : null,
            http.Session.GetString(ReviewSessionKeys.IsNewReport) == "1",
            int.TryParse(http.Session.GetString(ReviewSessionKeys.ParentReviewId), out var p) ? p : null);
        return true;
    }

    private void ClearReviewSession()
    {
        var http = Http;
        if (http is null) return;

        http.Items.Remove(ReviewSessionKeys.ApproverId);
        if (http.Session is { IsAvailable: true } session)
        {
            session.Remove(ReviewSessionKeys.ApproverId);
            session.Remove(ReviewSessionKeys.Description);
            session.Remove(ReviewSessionKeys.IsNewReport);
            session.Remove(ReviewSessionKeys.ParentReviewId);
        }
    }

    private readonly record struct ReviewSaveContext(
        int ApproverId, string ApproverDisplayName,
        int SubmitterId, string SubmitterDisplayName,
        string? Description, bool IsNewReport, int? ParentReviewId);
}
