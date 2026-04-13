namespace DxReportReview.Web.Storage;

public sealed class InMemoryReportStorage : IReportStorage
{
    private readonly Dictionary<int, (string DisplayName, byte[] Layout)> _reports = new();
    private readonly Lock _lock = new();
    private int _nextId;

    public InMemoryReportStorage(IEnumerable<KeyValuePair<int, (string DisplayName, byte[] Layout)>> seed)
    {
        foreach (var kv in seed.OrderBy(k => k.Key))
        {
            _reports[kv.Key] = kv.Value;
            _nextId = Math.Max(_nextId, kv.Key + 1);
        }
    }

    public byte[] GetLayout(int reportId)
    {
        lock (_lock)
        {
            if (!_reports.TryGetValue(reportId, out var entry))
                throw new InvalidOperationException($"Report {reportId} was not found.");
            return (byte[])entry.Layout.Clone();
        }
    }

    public void SaveLayout(int reportId, byte[] layout)
    {
        lock (_lock)
        {
            if (!_reports.TryGetValue(reportId, out var entry))
                throw new InvalidOperationException($"Report {reportId} was not found.");
            _reports[reportId] = (entry.DisplayName, (byte[])layout.Clone());
        }
    }

    public List<(int Id, string DisplayName)> GetAllReports()
    {
        lock (_lock)
            return _reports.OrderBy(r => r.Key).Select(r => (r.Key, r.Value.DisplayName)).ToList();
    }

    public int AddNewReport(string displayName, byte[] layout)
    {
        lock (_lock)
        {
            var id = _nextId++;
            _reports[id] = (displayName, (byte[])layout.Clone());
            return id;
        }
    }
}
