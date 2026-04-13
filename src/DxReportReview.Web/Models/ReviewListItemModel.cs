using DxReportReview.Web.Domain;

namespace DxReportReview.Web.Models;

public sealed class ReviewListItemModel
{
    public int Id { get; init; }
    public string ReportDisplayName { get; init; } = "";
    public string SubmitterDisplayName { get; init; } = "";
    public string ApproverDisplayName { get; init; } = "";
    public ReviewStatus Status { get; init; }
    public DateTime SubmittedAt { get; init; }
    public string? Description { get; init; }
    public bool HasConflict { get; init; }
}
