using System.Text;
using AgentX.Core.Services.Export.Models;
using AgentX.Core.Services.Settings;

namespace AgentX.Core.Services.Export;

/// <summary>
/// File-system utility methods for export operations: path resolution,
/// directory management, file-name sanitization, and extension mapping.
/// </summary>
internal static class ExportPathUtility
{
    /// <summary>
    /// Resolves the final output file path from the options, falling back to a
    /// timestamped file in the configured export directory when
    /// <see cref="ExportOptions.OutputPath"/> is not set.
    /// </summary>
    internal static async Task<string> ResolveOutputPathAsync(
        ExportOptions options, string title, ExportFormat format,
        ISettingsService settingsService)
    {
        if (!string.IsNullOrWhiteSpace(options.OutputPath))
            return options.OutputPath;

        var exportDir = await GetExportDirectoryAsync(settingsService);
        var extension = GetFileExtension(format);
        var sanitizedTitle = SanitizeFileName(title);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        return Path.Combine(exportDir, $"{sanitizedTitle}_{timestamp}{extension}");
    }

    /// <summary>
    /// Returns the export directory, creating it if necessary.
    /// </summary>
    internal static async Task<string> GetExportDirectoryAsync(ISettingsService settingsService)
    {
        var settings = await settingsService.GetSettingsAsync();
        var exportDir = Path.Combine(settings.StoragePath, "Exports");
        Directory.CreateDirectory(exportDir);
        return exportDir;
    }

    /// <summary>
    /// Ensures the parent directory of <paramref name="filePath"/> exists.
    /// </summary>
    internal static void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    /// <summary>
    /// Maps an <see cref="ExportFormat"/> to its canonical file extension
    /// (including the leading dot).
    /// </summary>
    internal static string GetFileExtension(ExportFormat format)
    {
        return format switch
        {
            ExportFormat.Markdown => ".md",
            ExportFormat.Html => ".html",
            ExportFormat.Pdf => ".pdf",
            ExportFormat.Json => ".json",
            ExportFormat.PlainText => ".txt",
            ExportFormat.Csv => ".csv",
            ExportFormat.Docx => ".docx",
            ExportFormat.Pptx => ".pptx",
            _ => ".txt",
        };
    }

    /// <summary>
    /// Replaces invalid file-name characters with underscores and truncates
    /// to 100 characters.
    /// </summary>
    internal static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "export";

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder(name.Length);

        foreach (var c in name)
            sanitized.Append(Array.IndexOf(invalidChars, c) >= 0 ? '_' : c);

        var result = sanitized.ToString().Trim();
        if (result.Length > 100)
            result = result[..100];

        return string.IsNullOrWhiteSpace(result) ? "export" : result;
    }
}
