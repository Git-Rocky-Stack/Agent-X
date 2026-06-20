using System.Text;
using AgentX.Core.Configuration;
using AgentX.Core.Documents.Models;
using Serilog;

namespace AgentX.Core.Documents;

/// <summary>
/// Adaptive chunking service that analyzes content structure and adjusts
/// chunk sizes based on content type (code, tables, prose, etc.).
/// Provides better context preservation by respecting natural boundaries.
/// </summary>
public sealed class AdaptiveChunkingService : IAdaptiveChunkingService
{
    private readonly IRagConfiguration _configuration;
    private readonly ILogger _log;

    // Content type patterns
    private const string CodePattern = @"^\s*```|\b(function|class|def|public|private|if\s*\(|for\s*\(|while\s*\()";
    private const string TablePattern = @"^\|.*\|$|^[\s\-+]+\|";
    private const string ListItemPattern = @"^\s*[-*•]\s+|^\s*\d+[.)]\s+";

    public AdaptiveChunkingService(IRagConfiguration configuration, ILogger log)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _log = log?.ForContext<AdaptiveChunkingService>() ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public AdaptiveChunkInfo AnalyzeContent(string text, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new AdaptiveChunkInfo();

        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var info = new AdaptiveChunkInfo
        {
            ContentType = DetectContentType(lines, fileName),
            AverageLineLength = lines.Length > 0 ? (int)lines.Average(l => l.Length) : 0,
            HasStructure = HasStructuralElements(lines),
            LineCount = lines.Length
        };

        // Determine recommended chunk size based on content type
        info.RecommendedChunkSize = CalculateOptimalChunkSize(info);

        _log.Debug("Content analysis: Type={Type}, Lines={Lines}, RecommendedChunkSize={Size}",
            info.ContentType, info.LineCount, info.RecommendedChunkSize);

        return info;
    }

    /// <inheritdoc />
    public int GetOptimalChunkSize(ContentType contentType, int averageLineLength)
    {
        return (contentType, averageLineLength) switch
        {
            // Code: Smaller chunks preserve function/class boundaries
            (ContentType.Code, _) => 256,

            // Tables: Larger chunks to keep table rows together
            (ContentType.Table, _) => 1024,

            // Lists: Medium chunks
            (ContentType.List, _) => 384,

            // Prose: Use configuration default
            (ContentType.Prose, _) => _configuration.DefaultChunkSize,

            // Mixed: Slightly larger than default
            (ContentType.Mixed, _) => (int)(_configuration.DefaultChunkSize * 1.2),

            _ => _configuration.DefaultChunkSize
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<int> DetectNaturalBoundaries(string text, int targetChunkSize)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<int>();

        var boundaries = new List<int>();
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        int currentLength = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            currentLength += lines[i].Length + 1; // +1 for newline

            // Check for structural boundaries
            if (IsStructuralBoundary(lines[i]))
            {
                if (currentLength >= targetChunkSize * 0.5) // Not too early
                {
                    boundaries.Add(i);
                    currentLength = 0;
                }
            }
            else if (currentLength >= targetChunkSize)
            {
                boundaries.Add(i + 1);
                currentLength = 0;
            }
        }

        return boundaries;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Private helpers
    // ═══════════════════════════════════════════════════════════════════

    private static ContentType DetectContentType(string[] lines, string? fileName)
    {
        // FU-2: removed dead `hasCode/hasTable/hasList/hasProse` locals — the actual
        // content type is decided below from `*LineCount` totals, not from boolean
        // flags. The booleans were assigned-but-never-read (CS0219).

        // File extension hint
        if (!string.IsNullOrEmpty(fileName))
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (ext is ".cs" or ".js" or ".ts" or ".py" or ".java" or ".cpp" or ".h")
                return ContentType.Code;
            if (ext is ".md" or ".txt")
                return ContentType.Prose;
        }

        // Analyze first 20 lines to detect content type
        var sampleLines = lines.Take(Math.Min(20, lines.Length));
        var codeLineCount = 0;
        var tableLineCount = 0;
        var listLineCount = 0;
        var proseLineCount = 0;

        foreach (var line in sampleLines)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(line, CodePattern))
                codeLineCount++;
            else if (System.Text.RegularExpressions.Regex.IsMatch(line, TablePattern))
                tableLineCount++;
            else if (System.Text.RegularExpressions.Regex.IsMatch(line, ListItemPattern))
                listLineCount++;
            else if (line.Length > 20) // Prose tends to be longer
                proseLineCount++;
        }

        // Determine dominant type
        var maxCount = Math.Max(Math.Max(codeLineCount, tableLineCount), Math.Max(listLineCount, proseLineCount));

        if (maxCount == codeLineCount && codeLineCount > 2)
            return ContentType.Code;
        if (maxCount == tableLineCount && tableLineCount > 2)
            return ContentType.Table;
        if (maxCount == listLineCount && listLineCount > 2)
            return ContentType.List;

        // Check if mixed content
        var typeCount = new[] { codeLineCount, tableLineCount, listLineCount }.Count(c => c > 0);
        if (typeCount >= 2)
            return ContentType.Mixed;

        return ContentType.Prose;
    }

    private static bool HasStructuralElements(string[] lines)
    {
        foreach (var line in lines.Take(50))
        {
            if (IsStructuralBoundary(line))
                return true;
        }
        return false;
    }

    private static bool IsStructuralBoundary(string line)
    {
        string trimmed = line.Trim();

        // Headers (Markdown)
        if (trimmed.StartsWith('#'))
            return true;

        // Code blocks
        if (trimmed.StartsWith("```"))
            return true;

        // Horizontal rules
        if (trimmed.StartsWith("---") || trimmed.StartsWith("___"))
            return true;

        // List markers
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, ListItemPattern))
            return true;

        return false;
    }

    private int CalculateOptimalChunkSize(AdaptiveChunkInfo info)
    {
        var baseSize = _configuration.DefaultChunkSize;
        var minSize = _configuration.MinChunkSize;
        var maxSize = _configuration.MaxChunkSize;

        int adjustedSize = info.ContentType switch
        {
            ContentType.Code => Math.Min(baseSize * 3 / 4, maxSize), // Smaller for code
            ContentType.Table => Math.Min(baseSize * 3 / 2, maxSize), // Larger for tables
            ContentType.List => baseSize,
            ContentType.Mixed => baseSize,
            ContentType.Prose => baseSize,
            _ => baseSize
        };

        // Adjust for line length (dense content = smaller chunks)
        if (info.AverageLineLength > 150)
            adjustedSize = Math.Max(adjustedSize * 4 / 5, minSize);
        else if (info.AverageLineLength < 50)
            adjustedSize = Math.Min(adjustedSize * 6 / 5, maxSize);

        // Respect bounds
        return Math.Max(minSize, Math.Min(maxSize, adjustedSize));
    }
}

/// <summary>
/// Interface for adaptive chunking that adjusts chunk sizes based on content.
/// </summary>
public interface IAdaptiveChunkingService
{
    /// <summary>
    /// Analyzes content to determine optimal chunking strategy.
    /// </summary>
    AdaptiveChunkInfo AnalyzeContent(string text, string? fileName = null);

    /// <summary>
    /// Gets the optimal chunk size for a specific content type.
    /// </summary>
    int GetOptimalChunkSize(ContentType contentType, int averageLineLength);

    /// <summary>
    /// Detects natural boundaries in text for chunk splitting.
    /// </summary>
    IReadOnlyList<int> DetectNaturalBoundaries(string text, int targetChunkSize);
}

/// <summary>
/// Information about content used for adaptive chunking decisions.
/// </summary>
public class AdaptiveChunkInfo
{
    /// <summary>Detected content type.</summary>
    public ContentType ContentType { get; set; } = ContentType.Prose;

    /// <summary>Average line length in characters.</summary>
    public int AverageLineLength { get; set; }

    /// <summary>Whether the content has structural elements (headers, lists, etc.).</summary>
    public bool HasStructure { get; set; }

    /// <summary>Total number of lines in the content.</summary>
    public int LineCount { get; set; }

    /// <summary>Recommended chunk size based on content analysis.</summary>
    public int RecommendedChunkSize { get; set; }
}

/// <summary>
/// Classification of content types for adaptive chunking.
/// </summary>
public enum ContentType
{
    /// <summary>Regular prose/narrative text.</summary>
    Prose,

    /// <summary>Programming code or structured data.</summary>
    Code,

    /// <summary>Tabular data (Markdown tables, CSV, etc.).</summary>
    Table,

    /// <summary>Bulleted or numbered lists.</summary>
    List,

    /// <summary>Mixed content with multiple types.</summary>
    Mixed
}
