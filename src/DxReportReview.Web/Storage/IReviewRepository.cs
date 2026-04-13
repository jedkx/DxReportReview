using DxReportReview.Web.Domain;

namespace DxReportReview.Web.Storage;

public interface IReviewRepository
{
    Task<List<ReviewSubmission>> GetAllAsync();
    Task<ReviewSubmission?> GetByIdAsync(int id);
    ReviewSubmission? GetById(int id);
    Task<ReviewSubmission> AddAsync(ReviewSubmission submission);
    Task UpdateAsync(ReviewSubmission submission);
    Task<bool> HasPendingAsync(int reportId);
}
