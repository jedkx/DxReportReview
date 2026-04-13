namespace DxReportReview.Web.Models;

public sealed class SetApproverRequest
{
    public int ApproverId { get; set; }
    public string? Description { get; set; }
    public bool IsNewReport { get; set; }
    public int? ParentReviewId { get; set; }
}
