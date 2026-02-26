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
