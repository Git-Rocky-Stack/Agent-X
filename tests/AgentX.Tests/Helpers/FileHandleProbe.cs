namespace AgentX.Tests.Helpers;

/// <summary>
/// Detects whether this process still holds a handle on a file. Windows refuses to move, replace
/// or delete a file that SQLite (or any stream) still has open, so tests use this to prove that a
/// code path released every handle before touching the file system.
/// <para>
/// On Linux the answer comes from <c>/proc/self/fd</c>, because Linux lets a second exclusive open
/// succeed. On Windows an exclusive (<see cref="FileShare.None"/>) open fails while another handle
/// exists, which is exactly the condition the production code must avoid.
/// </para>
/// </summary>
internal static class FileHandleProbe
{
    public static bool IsOpenByThisProcess(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (OperatingSystem.IsLinux())
        {
            // A handle on a file that was deleted while open shows as "<path> (deleted)". Linux
            // lets the delete succeed, but Windows would have refused it, so it still counts.
            var deletedTarget = fullPath + " (deleted)";
            foreach (var fd in Directory.EnumerateFiles("/proc/self/fd"))
            {
                try
                {
                    var target = new FileInfo(fd).LinkTarget;
                    if (string.Equals(target, fullPath, StringComparison.Ordinal) ||
                        string.Equals(target, deletedTarget, StringComparison.Ordinal))
                        return true;
                }
                catch (IOException)
                {
                    // The descriptor closed while enumerating; it no longer counts.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return false;
        }

        if (!File.Exists(fullPath))
            return false;

        try
        {
            using var exclusive = new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }
}
