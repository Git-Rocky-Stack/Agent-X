namespace AgentX.Core.Helpers;

/// <summary>
/// Production <see cref="IAppPathService"/>. Delegates to the static <see cref="PathHelper"/> so the
/// real application keeps a single source of truth for its directory layout and behavior is
/// unchanged; the interface exists purely to give tests a substitution point (AX-QA-011).
/// </summary>
public sealed class AppPathService : IAppPathService
{
    public string GetAppDataPath() => PathHelper.GetAppDataPath();

    public string GetTempPath() => PathHelper.GetTempPath();
}
