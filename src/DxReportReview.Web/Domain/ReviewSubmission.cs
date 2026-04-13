namespace DxReportReview.Web.Domain;

public class ReviewSubmission
{
    public int Id { get; set; }
    public int ReportId { get; set; }
    public string ReportDisplayName { get; set; } = "";
    public int SubmitterId { get; set; }
    public string SubmitterDisplayName { get; set; } = "";
    public int ApproverId { get; set; }
    public string ApproverDisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string? Note { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
    public byte[] ProposedLayout { get; set; } = [];
    public byte[] BaselineLayout { get; set; } = [];
    public DateTime SubmittedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int? ParentReviewId { get; set; }
    public bool IsNewReport { get; set; }
}
