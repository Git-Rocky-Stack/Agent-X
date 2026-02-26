using System.Text.RegularExpressions;
using AgentX.Core.Search.Models;
using Serilog;

namespace AgentX.Core.Search;

/// <summary>
/// Extracts and resolves [N] citation references from AI-generated RAG responses,
/// mapping each reference back to its corresponding source document chunk.
/// </summary>
public sealed partial class CitationService : ICitationService
{
    /// <summary>
    /// Maximum character length for the excerpt extracted from each cited chunk.
    /// </summary>
    private const int ExcerptMaxLength = 100;

    private readonly ILogger _logger;

    /// <summary>
    /// Compiled regex pattern matching citation references in the form [N],
    /// where N is one or more digits representing a positive integer.
    /// Uses source-generated regex for optimal performance on .NET 8.
    /// </summary>
    [GeneratedRegex(@"\[(\d+)\]", RegexOptions.Compiled)]
    private static partial Regex CitationPattern();

    public CitationService(ILogger logger)
    {
        _logger = logger?.ForContext<CitationService>()
                  ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public List<Citation> ExtractCitations(string responseText, IReadOnlyList<RagContextChunk> contextChunks)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            _logger.Debug("Empty response text provided; returning no citations");
            return new List<Citation>();
        }

        if (contextChunks is null || contextChunks.Count == 0)
        {
            _logger.Debug("No context chunks provided; returning no citations");
            return new List<Citation>();
        }

        var matches = CitationPattern().Matches(responseText);
        if (matches.Count == 0)
        {
            _logger.Debug("No [N] citation references found in response text");
            return new List<Citation>();
        }

        // Collect unique citation numbers from the response text.
        // Using a HashSet ensures each citation number is processed only once,
        // even if the AI references the same source multiple times.
        var uniqueNumbers = new HashSet<int>();
        foreach (Match match in matches)
        {
            if (int.TryParse(match.Groups[1].Value, out var number) && number > 0)
            {
                uniqueNumbers.Add(number);
            }
        }

        _logger.Debug("Found {UniqueCount} unique citation references in response text from {TotalMatches} total matches",
            uniqueNumbers.Count, matches.Count);

        var citations = new List<Citation>(uniqueNumbers.Count);

        foreach (var number in uniqueNumbers.Order())
        {
            // Citations use 1-based indexing: [1] refers to contextChunks[0]
            var chunkIndex = number - 1;

            if (chunkIndex < 0 || chunkIndex >= contextChunks.Count)
            {
                _logger.Warning("Citation [{Number}] is out of range (context has {Count} chunks); skipping",
                    number, contextChunks.Count);
                continue;
            }

            var chunk = contextChunks[chunkIndex];
            var excerpt = BuildExcerpt(chunk.ChunkText);

            citations.Add(new Citation
            {
                Number = number,
                DocumentId = chunk.DocumentId,
                ChunkId = chunk.ChunkId,
                FileName = chunk.FileName,
                FilePath = chunk.FilePath,
                PageNumber = chunk.PageNumber,
                ChunkIndex = chunk.ChunkIndex,
                Excerpt = excerpt,
                RelevanceScore = chunk.RelevanceScore
            });
        }

        _logger.Debug("Resolved {Count} valid citations from response", citations.Count);
        return citations;
    }

    /// <summary>
    /// Builds a short, clean excerpt from the chunk text.
    /// Truncates at word boundaries when possible to avoid cutting mid-word,
    /// and normalizes whitespace for display.
    /// </summary>
    private static string BuildExcerpt(string chunkText)
    {
        if (string.IsNullOrWhiteSpace(chunkText))
            return string.Empty;

        // Normalize internal whitespace (newlines, tabs, multi-spaces) to single spaces
        var normalized = Regex.Replace(chunkText.Trim(), @"\s+", " ");

        if (normalized.Length <= ExcerptMaxLength)
            return normalized;

        // Truncate at the last space boundary before the limit to avoid mid-word cuts
        var truncated = normalized[..ExcerptMaxLength];
        var lastSpace = truncated.LastIndexOf(' ');

        if (lastSpace > ExcerptMaxLength / 2)
        {
            // Only break at word boundary if it doesn't lose more than half the excerpt
            truncated = truncated[..lastSpace];
        }

        return truncated + "...";
    }
}
