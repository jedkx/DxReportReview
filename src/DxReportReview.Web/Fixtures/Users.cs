namespace DxReportReview.Web.Fixtures;

public enum UserRole
{
    Designer,
    Approver
}

public record AppUser(int Id, string DisplayName, string Username, UserRole Role);

public static class Users
{
    public static readonly AppUser Alice = new(1, "Alice", "alice", UserRole.Designer);
    public static readonly AppUser Bob = new(2, "Bob", "bob", UserRole.Approver);
    public static readonly List<AppUser> All = [Alice, Bob];

    public static AppUser? FindByUsername(string username)
    {
        var u = username.Trim();
        return All.FirstOrDefault(x =>
            string.Equals(x.Username, u, StringComparison.OrdinalIgnoreCase));
    }
}
