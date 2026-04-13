namespace DxReportReview.Web.Domain;

public enum BlockReason { Conflict, PendingExists, AlreadyReEdited }

public static class ReviewRules
{
    public static bool HasBlockingPendingReview(IEnumerable<ReviewSubmission> all, int reportId)
        => all.Any(r => r.ReportId == reportId && r.Status == ReviewStatus.Pending);

    public static bool HasConflict(ReviewSubmission submission, byte[] currentBaseline)
        => !submission.BaselineLayout.SequenceEqual(currentBaseline);

    public static bool HasConflict(ReviewSubmission submission, IEnumerable<ReviewSubmission> all)
        => all.Any(r =>
            r.Id != submission.Id
            && r.ReportId == submission.ReportId
            && r.Status == ReviewStatus.Approved
            && r.ProcessedAt > submission.SubmittedAt);

    public static bool HasChildReview(ReviewSubmission submission, IEnumerable<ReviewSubmission> all)
        => all.Any(r => r.ParentReviewId == submission.Id);

    public static BlockReason? GetBlockReason(ReviewSubmission submission, IEnumerable<ReviewSubmission> all)
    {
        if (HasConflict(submission, all))
            return BlockReason.Conflict;

        if (submission.Status == ReviewStatus.Rejected && HasChildReview(submission, all))
            return BlockReason.AlreadyReEdited;

        if (submission.Status == ReviewStatus.Rejected
            && HasBlockingPendingReview(all.Where(r => r.Id != submission.Id), submission.ReportId))
            return BlockReason.PendingExists;

        return null;
    }

    public static string? BlockReasonText(BlockReason? reason) => reason switch
    {
        BlockReason.Conflict => "The live report changed after this submission was created.",
        BlockReason.PendingExists => "Another review for this report is already pending.",
        BlockReason.AlreadyReEdited => "This review has already been re-edited.",
        _ => null
    };

    public static void Approve(ReviewSubmission s, string? note, DateTime now)
    {
        if (s.Status != ReviewStatus.Pending)
            throw new InvalidOperationException("Submission is not pending.");
        s.Status = ReviewStatus.Approved;
        s.Note = note;
        s.ProcessedAt = now;
    }

    public static void Reject(ReviewSubmission s, string? note, DateTime now)
    {
        if (s.Status != ReviewStatus.Pending)
            throw new InvalidOperationException("Submission is not pending.");
        s.Status = ReviewStatus.Rejected;
        s.Note = note;
        s.ProcessedAt = now;
    }
}
