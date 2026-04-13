using DxReportReview.Web.Domain;

namespace DxReportReview.Web.Models;

public sealed class ReviewDetailModel
{
    public int Id { get; init; }
    public string ReportDisplayName { get; init; } = "";
    public string SubmitterDisplayName { get; init; } = "";
    public string ApproverDisplayName { get; init; } = "";
    public string? Description { get; init; }
    public string? Note { get; init; }
    public ReviewStatus Status { get; init; }
    public DateTime SubmittedAt { get; init; }
    public DateTime? ProcessedAt { get; init; }
    public string CurrentViewerUrl { get; init; } = "";
    public string ProposedViewerUrl { get; init; } = "";
    public bool CanAct { get; init; }
    public bool HasConflict { get; init; }
    public string? BlockReason { get; init; }
    public int? ParentReviewId { get; init; }
    public int? ChildReviewId { get; init; }
    public bool IsSubmitter { get; init; }
    public int ReportId { get; init; }
}
