namespace DxReportReview.Web.Storage;

public interface IReportStorage
{
    byte[] GetLayout(int reportId);
    void SaveLayout(int reportId, byte[] layout);
    List<(int Id, string DisplayName)> GetAllReports();

    int AddNewReport(string displayName, byte[] layout);
}
