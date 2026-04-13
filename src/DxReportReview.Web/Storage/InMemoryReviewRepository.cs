using DxReportReview.Web.Domain;

namespace DxReportReview.Web.Storage;

public sealed class InMemoryReviewRepository : IReviewRepository
{
    private readonly List<ReviewSubmission> _items = [];
    private readonly object _lock = new();
    private int _nextId = 1;

    public Task<List<ReviewSubmission>> GetAllAsync()
    {
        lock (_lock)
            return Task.FromResult(_items.OrderByDescending(r => r.SubmittedAt).ToList());
    }

    public Task<ReviewSubmission?> GetByIdAsync(int id)
    {
        lock (_lock)
            return Task.FromResult(_items.FirstOrDefault(r => r.Id == id));
    }

    public ReviewSubmission? GetById(int id)
    {
        lock (_lock)
            return _items.FirstOrDefault(r => r.Id == id);
    }

    public Task<ReviewSubmission> AddAsync(ReviewSubmission submission)
    {
        lock (_lock)
        {
            submission.Id = _nextId++;
            _items.Add(submission);
            return Task.FromResult(submission);
        }
    }

    public Task UpdateAsync(ReviewSubmission submission)
    {
        lock (_lock)
        {
            var idx = _items.FindIndex(r => r.Id == submission.Id);
            if (idx < 0)
                throw new InvalidOperationException("Submission not found.");
            _items[idx] = submission;
            return Task.CompletedTask;
        }
    }

    public Task<bool> HasPendingAsync(int reportId)
    {
        lock (_lock)
            return Task.FromResult(_items.Any(r => r.ReportId == reportId && r.Status == ReviewStatus.Pending));
    }
}
