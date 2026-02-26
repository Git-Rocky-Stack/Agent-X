namespace AgentX.Core.Documents.Models;

public class ProcessedDocument
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string ExtractedText { get; set; } = string.Empty;
    public string? ExtractedTitle { get; set; }
    public int PageCount { get; set; }
    public long WordCount { get; set; }
    public string? Language { get; set; }
    public DocumentMetadata Metadata { get; set; } = new();
    public List<DocumentChunk> Chunks { get; set; } = new();
}

public class DocumentChunk
{
    public int Index { get; set; }
    public string Content { get; set; } = string.Empty;
    public int StartCharOffset { get; set; }
    public int EndCharOffset { get; set; }
    public int? PageNumber { get; set; }
    public string? SectionTitle { get; set; }
    public int TokenCount { get; set; }
    public float[]? Embedding { get; set; }
}

public class DocumentMetadata
{
    public string? Author { get; set; }
    public string? Subject { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public Dictionary<string, string> Custom { get; set; } = new();
}

public static class SupportedFileTypes
{
    public static readonly HashSet<string> Pdf = new(StringComparer.OrdinalIgnoreCase) { ".pdf" };
    public static readonly HashSet<string> Office = new(StringComparer.OrdinalIgnoreCase) { ".docx", ".doc" };
    public static readonly HashSet<string> Text = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".csv", ".log", ".xml", ".json" };
    public static readonly HashSet<string> Markdown = new(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown" };
    public static readonly HashSet<string> Image = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".tiff" };
    public static readonly HashSet<string> Code = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".go", ".rs",
        ".swift", ".kt", ".rb", ".php", ".html", ".css", ".scss", ".sql", ".sh",
        ".yaml", ".yml", ".toml", ".ini", ".cfg", ".xaml"
    };

    public static readonly HashSet<string> All = new(
        Pdf.Concat(Office).Concat(Text).Concat(Markdown).Concat(Image).Concat(Code),
        StringComparer.OrdinalIgnoreCase);
}
