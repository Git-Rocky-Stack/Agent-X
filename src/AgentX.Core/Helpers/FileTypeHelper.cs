using AgentX.Core.Documents.Models;

namespace AgentX.Core.Helpers;

/// <summary>
/// Provides file type classification, MIME type mapping, icon glyph resolution, and
/// human-readable display names for document file extensions used throughout Agent-X.
/// </summary>
public static class FileTypeHelper
{
    /// <summary>
    /// Maps a file extension to a high-level category name.
    /// </summary>
    /// <param name="extension">The file extension including the leading dot (e.g., ".pdf").</param>
    /// <returns>One of: "PDF", "Document", "Text", "Markdown", "Image", "Code", or "Unknown".</returns>
    public static string GetFileCategory(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return "Unknown";

        var ext = NormalizeExtension(extension);

        if (SupportedFileTypes.Pdf.Contains(ext)) return "PDF";
        if (SupportedFileTypes.Office.Contains(ext)) return "Document";
        if (SupportedFileTypes.Text.Contains(ext)) return "Text";
        if (SupportedFileTypes.Markdown.Contains(ext)) return "Markdown";
        if (SupportedFileTypes.Image.Contains(ext)) return "Image";
        if (SupportedFileTypes.Code.Contains(ext)) return "Code";

        return "Unknown";
    }

    /// <summary>
    /// Returns the MIME type for a given file extension.
    /// Falls back to "application/octet-stream" for unrecognized extensions.
    /// </summary>
    /// <param name="extension">The file extension including the leading dot (e.g., ".pdf").</param>
    /// <returns>A MIME type string.</returns>
    public static string GetMimeType(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return "application/octet-stream";

        var ext = NormalizeExtension(extension);

        return MimeTypes.GetValueOrDefault(ext, "application/octet-stream");
    }

    /// <summary>
    /// Returns a Segoe Fluent Icons glyph string suitable for representing the file type in the UI.
    /// </summary>
    /// <param name="extension">The file extension including the leading dot (e.g., ".pdf").</param>
    /// <returns>A single-character string containing a Segoe Fluent Icons glyph code point.</returns>
    public static string GetIconGlyph(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return "\uE8A5"; // Page (generic document)

        var ext = NormalizeExtension(extension);

        // Check specific extension glyphs first
        if (IconGlyphs.TryGetValue(ext, out var glyph))
            return glyph;

        // Fall back to category-level glyphs
        var category = GetFileCategory(ext);
        return category switch
        {
            "PDF" => "\uEA90",       // PDF
            "Document" => "\uE8A5",  // Page
            "Text" => "\uE8A5",      // Page
            "Markdown" => "\uE70B",  // Edit
            "Image" => "\uEB9F",     // Photo
            "Code" => "\uE943",      // Code
            _ => "\uE8A5"            // Page (generic)
        };
    }

    /// <summary>
    /// Checks whether the given file extension is in the set of supported file types
    /// defined by <see cref="SupportedFileTypes.All"/>.
    /// </summary>
    /// <param name="extension">The file extension including the leading dot (e.g., ".pdf").</param>
    /// <returns><c>true</c> if the extension is supported; otherwise <c>false</c>.</returns>
    public static bool IsSupported(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        return SupportedFileTypes.All.Contains(NormalizeExtension(extension));
    }

    /// <summary>
    /// Returns a human-readable display name for a file extension (e.g., ".pdf" becomes "PDF Document").
    /// </summary>
    /// <param name="extension">The file extension including the leading dot (e.g., ".pdf").</param>
    /// <returns>A human-readable file type name.</returns>
    public static string GetDisplayName(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return "Unknown File";

        var ext = NormalizeExtension(extension);

        return DisplayNames.GetValueOrDefault(ext, $"{ext.TrimStart('.').ToUpperInvariant()} File");
    }

    // ── Private Helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Normalizes an extension to lowercase with a leading dot.
    /// </summary>
    private static string NormalizeExtension(string extension)
    {
        var ext = extension.Trim().ToLowerInvariant();
        if (!ext.StartsWith('.'))
            ext = "." + ext;
        return ext;
    }

    // ── MIME Type Mappings ──────────────────────────────────────────────────

    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // PDF
        [".pdf"] = "application/pdf",

        // Office / Documents
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".doc"] = "application/msword",

        // Text
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".log"] = "text/plain",
        [".xml"] = "application/xml",
        [".json"] = "application/json",

        // Markdown
        [".md"] = "text/markdown",
        [".markdown"] = "text/markdown",

        // Images
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".bmp"] = "image/bmp",
        [".tiff"] = "image/tiff",

        // Code
        [".cs"] = "text/x-csharp",
        [".js"] = "text/javascript",
        [".ts"] = "text/typescript",
        [".py"] = "text/x-python",
        [".java"] = "text/x-java-source",
        [".cpp"] = "text/x-c++src",
        [".c"] = "text/x-csrc",
        [".h"] = "text/x-chdr",
        [".go"] = "text/x-go",
        [".rs"] = "text/x-rust",
        [".swift"] = "text/x-swift",
        [".kt"] = "text/x-kotlin",
        [".rb"] = "text/x-ruby",
        [".php"] = "text/x-php",
        [".html"] = "text/html",
        [".css"] = "text/css",
        [".scss"] = "text/x-scss",
        [".sql"] = "application/sql",
        [".sh"] = "text/x-shellscript",
        [".yaml"] = "text/yaml",
        [".yml"] = "text/yaml",
        [".toml"] = "application/toml",
        [".ini"] = "text/plain",
        [".cfg"] = "text/plain",
        [".xaml"] = "application/xaml+xml",
    };

    // ── Icon Glyph Mappings (Segoe Fluent Icons) ───────────────────────────

    private static readonly Dictionary<string, string> IconGlyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        // PDF
        [".pdf"] = "\uEA90",        // PDF

        // Office / Documents
        [".docx"] = "\uE8A5",       // Page
        [".doc"] = "\uE8A5",        // Page

        // Text
        [".txt"] = "\uE8A5",        // Page
        [".csv"] = "\uE80A",        // BulletedList
        [".log"] = "\uE7BA",        // List
        [".xml"] = "\uE943",        // Code
        [".json"] = "\uE943",       // Code

        // Markdown
        [".md"] = "\uE70B",         // Edit
        [".markdown"] = "\uE70B",   // Edit

        // Images
        [".png"] = "\uEB9F",        // Photo
        [".jpg"] = "\uEB9F",        // Photo
        [".jpeg"] = "\uEB9F",       // Photo
        [".bmp"] = "\uEB9F",        // Photo
        [".tiff"] = "\uEB9F",       // Photo

        // Code — all get the Code glyph
        [".cs"] = "\uE943",
        [".js"] = "\uE943",
        [".ts"] = "\uE943",
        [".py"] = "\uE943",
        [".java"] = "\uE943",
        [".cpp"] = "\uE943",
        [".c"] = "\uE943",
        [".h"] = "\uE943",
        [".go"] = "\uE943",
        [".rs"] = "\uE943",
        [".swift"] = "\uE943",
        [".kt"] = "\uE943",
        [".rb"] = "\uE943",
        [".php"] = "\uE943",
        [".html"] = "\uF6FA",       // Website
        [".css"] = "\uE943",
        [".scss"] = "\uE943",
        [".sql"] = "\uEE94",        // Database
        [".sh"] = "\uE756",         // CommandPrompt
        [".yaml"] = "\uE943",
        [".yml"] = "\uE943",
        [".toml"] = "\uE943",
        [".ini"] = "\uE713",        // Settings
        [".cfg"] = "\uE713",        // Settings
        [".xaml"] = "\uE943",
    };

    // ── Display Name Mappings ──────────────────────────────────────────────

    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // PDF
        [".pdf"] = "PDF Document",

        // Office / Documents
        [".docx"] = "Word Document",
        [".doc"] = "Word Document (Legacy)",

        // Text
        [".txt"] = "Text File",
        [".csv"] = "CSV Spreadsheet",
        [".log"] = "Log File",
        [".xml"] = "XML Document",
        [".json"] = "JSON File",

        // Markdown
        [".md"] = "Markdown Document",
        [".markdown"] = "Markdown Document",

        // Images
        [".png"] = "PNG Image",
        [".jpg"] = "JPEG Image",
        [".jpeg"] = "JPEG Image",
        [".bmp"] = "Bitmap Image",
        [".tiff"] = "TIFF Image",

        // Code
        [".cs"] = "C# Source File",
        [".js"] = "JavaScript File",
        [".ts"] = "TypeScript File",
        [".py"] = "Python Script",
        [".java"] = "Java Source File",
        [".cpp"] = "C++ Source File",
        [".c"] = "C Source File",
        [".h"] = "C/C++ Header File",
        [".go"] = "Go Source File",
        [".rs"] = "Rust Source File",
        [".swift"] = "Swift Source File",
        [".kt"] = "Kotlin Source File",
        [".rb"] = "Ruby Script",
        [".php"] = "PHP Source File",
        [".html"] = "HTML Document",
        [".css"] = "CSS Stylesheet",
        [".scss"] = "SCSS Stylesheet",
        [".sql"] = "SQL Script",
        [".sh"] = "Shell Script",
        [".yaml"] = "YAML Configuration",
        [".yml"] = "YAML Configuration",
        [".toml"] = "TOML Configuration",
        [".ini"] = "INI Configuration",
        [".cfg"] = "Configuration File",
        [".xaml"] = "XAML Markup",
    };
}
