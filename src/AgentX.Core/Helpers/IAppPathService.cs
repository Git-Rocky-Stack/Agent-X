namespace AgentX.Core.Helpers;

/// <summary>
/// Injectable seam over the application's data directories. Production resolves these to
/// <c>%LOCALAPPDATA%/AgentX/…</c> via <see cref="PathHelper"/>; tests substitute an implementation
/// rooted at a disposable per-test directory so production code under test never writes into the real
/// user profile (AX-QA-011). The pure file-name/containment utilities on <see cref="PathHelper"/>
/// stay static — only the directory roots, which differ between production and test, need a seam.
/// </summary>
public interface IAppPathService
{
    /// <summary>The application data root (production: <c>%LOCALAPPDATA%/AgentX/</c>). Created if absent.</summary>
    string GetAppDataPath();

    /// <summary>The temporary-files directory under the app data root. Created if absent.</summary>
    string GetTempPath();
}
