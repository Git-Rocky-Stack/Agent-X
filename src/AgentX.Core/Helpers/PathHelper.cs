namespace AgentX.Core.Helpers;

/// <summary>
/// Provides standardized path resolution for all Agent-X application directories and files.
/// All paths are rooted under %LOCALAPPDATA%/AgentX/ to follow Windows conventions.
/// </summary>
public static class PathHelper
{
    private const string AppFolderName = "AgentX";
    private const string DatabaseFileName = "agentx.db";
    private const string LogsFolderName = "Logs";
    private const string SettingsFileName = "settings.json";
    private const string ThumbnailsFolderName = "Thumbnails";
    private const string TempFolderName = "Temp";

    private static readonly string AppDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    /// <summary>
    /// Gets the root application data path (%LOCALAPPDATA%/AgentX/).
    /// Creates the directory if it does not exist.
    /// </summary>
    public static string GetAppDataPath()
    {
        return EnsureDirectoryExists(AppDataRoot);
    }

    /// <summary>
    /// Gets the full path to the SQLite database file (%LOCALAPPDATA%/AgentX/agentx.db).
    /// Ensures the parent directory exists.
    /// </summary>
    public static string GetDatabasePath()
    {
        EnsureDirectoryExists(AppDataRoot);
        return Path.Combine(AppDataRoot, DatabaseFileName);
    }

    /// <summary>
    /// Gets the path to the log file directory (%LOCALAPPDATA%/AgentX/Logs/).
    /// Creates the directory if it does not exist.
    /// </summary>
    public static string GetLogsPath()
    {
        return EnsureDirectoryExists(Path.Combine(AppDataRoot, LogsFolderName));
    }

    /// <summary>
    /// Gets the full path to the settings JSON file (%LOCALAPPDATA%/AgentX/settings.json).
    /// Ensures the parent directory exists.
    /// </summary>
    public static string GetSettingsPath()
    {
        EnsureDirectoryExists(AppDataRoot);
        return Path.Combine(AppDataRoot, SettingsFileName);
    }

    /// <summary>
    /// Gets the path to the thumbnails cache directory (%LOCALAPPDATA%/AgentX/Thumbnails/).
    /// Creates the directory if it does not exist.
    /// </summary>
    public static string GetThumbnailsPath()
    {
        return EnsureDirectoryExists(Path.Combine(AppDataRoot, ThumbnailsFolderName));
    }

    /// <summary>
    /// Gets the path to the temporary files directory (%LOCALAPPDATA%/AgentX/Temp/).
    /// Creates the directory if it does not exist.
    /// </summary>
    public static string GetTempPath()
    {
        return EnsureDirectoryExists(Path.Combine(AppDataRoot, TempFolderName));
    }

    /// <summary>
    /// Removes characters that are invalid in file names, replacing them with underscores.
    /// Also trims leading/trailing whitespace and dots, and clamps length to 255 characters.
    /// </summary>
    /// <param name="fileName">The raw file name to sanitize.</param>
    /// <returns>A sanitized file name safe for use on Windows file systems.</returns>
    public static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "_";

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new char[fileName.Length];

        for (int i = 0; i < fileName.Length; i++)
        {
            sanitized[i] = Array.IndexOf(invalidChars, fileName[i]) >= 0
                ? '_'
                : fileName[i];
        }

        var result = new string(sanitized).Trim().Trim('.');

        if (string.IsNullOrWhiteSpace(result))
            return "_";

        // Windows maximum file name length is 255 characters
        const int maxFileNameLength = 255;
        if (result.Length > maxFileNameLength)
            result = result[..maxFileNameLength];

        return result;
    }

    /// <summary>
    /// Computes a safe relative path from <paramref name="basePath"/> to <paramref name="fullPath"/>.
    /// Returns the original <paramref name="fullPath"/> if it is not rooted under <paramref name="basePath"/>.
    /// </summary>
    /// <param name="fullPath">The absolute path to make relative.</param>
    /// <param name="basePath">The base directory path to compute relative to.</param>
    /// <returns>A relative path string, or the original full path if it cannot be made relative.</returns>
    public static string GetRelativePath(string fullPath, string basePath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(basePath))
            return fullPath;

        try
        {
            // Normalize both paths to ensure consistent comparison
            var normalizedFull = Path.GetFullPath(fullPath);
            var normalizedBase = Path.GetFullPath(basePath);

            // Ensure base path ends with directory separator for correct relative path computation
            if (!normalizedBase.EndsWith(Path.DirectorySeparatorChar))
                normalizedBase += Path.DirectorySeparatorChar;

            // Check that fullPath is actually under basePath
            if (!normalizedFull.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                return fullPath;

            return Path.GetRelativePath(normalizedBase, normalizedFull);
        }
        catch (Exception)
        {
            // If path computation fails for any reason, return the original path
            return fullPath;
        }
    }

    // ── Path containment (security boundary) ───────────────────────────────

    /// <summary>
    /// Synthetic, disk-agnostic base used purely for normalization when checking that an
    /// archive-relative entry does not escape its intended directory. <see cref="Path.GetFullPath(string)"/>
    /// performs string normalization only (no filesystem access), so this directory need not exist.
    /// </summary>
    private static readonly string TraversalProbeBase =
        Path.Combine(Path.GetTempPath(), "agentx-entry-probe") + Path.DirectorySeparatorChar;

    /// <summary>
    /// Resolves <paramref name="relativePath"/> against <paramref name="baseDirectory"/> and
    /// guarantees the result stays strictly inside the base directory. Throws
    /// <see cref="UnauthorizedAccessException"/> when the entry would escape — via <c>..</c>
    /// segments, an absolute/rooted path, or alternate separators. This is the canonical guard
    /// for writing untrusted archive entries or loading plugin assemblies; unlike
    /// <see cref="GetRelativePath"/>, it fails closed rather than returning the original path.
    /// </summary>
    /// <param name="baseDirectory">The directory the resolved path must remain within.</param>
    /// <param name="relativePath">An untrusted, base-relative path (e.g. a ZIP entry name).</param>
    /// <returns>The fully-qualified, normalized target path, guaranteed to be under the base.</returns>
    public static string ResolveContainedPath(string baseDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentException("Base directory must not be empty.", nameof(baseDirectory));
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Relative path must not be empty.", nameof(relativePath));

        // Rooted/absolute entries ignore the base directory entirely — reject outright.
        if (Path.IsPathRooted(relativePath))
            throw new UnauthorizedAccessException(
                $"Refusing rooted path '{relativePath}' under base '{baseDirectory}'.");

        string normalizedBase;
        string resolved;
        try
        {
            normalizedBase = Path.GetFullPath(baseDirectory);
            resolved = Path.GetFullPath(Path.Combine(normalizedBase, relativePath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new UnauthorizedAccessException($"Rejected malformed path '{relativePath}'.", ex);
        }

        var baseWithSeparator = normalizedBase.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedBase
            : normalizedBase + Path.DirectorySeparatorChar;

        if (!resolved.StartsWith(baseWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Path traversal blocked: '{relativePath}' resolves outside '{normalizedBase}'.");

        return resolved;
    }

    /// <summary>
    /// Non-throwing variant of <see cref="ResolveContainedPath"/>. Returns <c>true</c> and the
    /// safe resolved path when the entry is contained; <c>false</c> otherwise.
    /// </summary>
    public static bool TryResolveContainedPath(string baseDirectory, string relativePath, out string resolvedPath)
    {
        try
        {
            resolvedPath = ResolveContainedPath(baseDirectory, relativePath);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException)
        {
            resolvedPath = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Returns <c>true</c> when an untrusted, base-relative entry path (e.g. a ZIP entry name)
    /// is non-rooted and does not escape its directory. Disk-agnostic — uses a synthetic base
    /// for normalization — so it can validate archive entries without knowing the final target root.
    /// </summary>
    public static bool IsSafeRelativeEntry(string relativePath)
        => !string.IsNullOrWhiteSpace(relativePath)
           && !Path.IsPathRooted(relativePath)
           && TryResolveContainedPath(TraversalProbeBase, relativePath, out _);

    /// <summary>
    /// Returns <c>true</c> when <paramref name="fileName"/> is a single file name with no directory
    /// component: not rooted, free of path separators, and containing no invalid file-name
    /// characters. Used to confirm a manifest-supplied entry assembly cannot redirect assembly
    /// loading outside its plugin directory (e.g. <c>..\\other.dll</c> or <c>C:\\evil.dll</c>).
    /// </summary>
    public static bool IsBareFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;
        if (fileName is "." or "..")
            return false;
        if (Path.IsPathRooted(fileName))
            return false;
        if (fileName.IndexOf('/') >= 0 || fileName.IndexOf('\\') >= 0)
            return false;
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        return string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="candidatePath"/> (after normalization) is located
    /// strictly inside <paramref name="baseDirectory"/>. Used to confirm a stored absolute path
    /// (e.g. a plugin install directory) has not drifted outside its expected root before a
    /// destructive operation such as a recursive delete or assembly load.
    /// </summary>
    public static bool IsPathContained(string baseDirectory, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory) || string.IsNullOrWhiteSpace(candidatePath))
            return false;

        try
        {
            var normalizedBase = Path.GetFullPath(baseDirectory);
            var baseWithSeparator = normalizedBase.EndsWith(Path.DirectorySeparatorChar)
                ? normalizedBase
                : normalizedBase + Path.DirectorySeparatorChar;
            var normalizedCandidate = Path.GetFullPath(candidatePath);

            return normalizedCandidate.StartsWith(baseWithSeparator, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates the specified directory if it does not already exist and returns the path.
    /// </summary>
    /// <param name="path">The directory path to ensure exists.</param>
    /// <returns>The same <paramref name="path"/> that was passed in.</returns>
    public static string EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);

        return path;
    }
}
