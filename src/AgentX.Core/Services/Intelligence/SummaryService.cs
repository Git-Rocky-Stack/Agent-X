using System.Text;
using System.Text.RegularExpressions;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Intelligence;

/// <summary>
/// AI-powered implementation of <see cref="ISummaryService"/> that provides document
/// summarization, key-point extraction, and text translation using the active AI provider.
/// All operations use low temperature (0.3) for factual accuracy and stream responses
/// for efficiency.
/// </summary>
public class SummaryService : ISummaryService
{
    private readonly IAiService _aiService;
    private readonly AgentXDbContext _db;
    private readonly IHierarchicalSummaryService _hierarchicalSummaryService;
    private readonly ILogger _log;

    /// <summary>
    /// Maximum number of characters to extract from document chunks for AI context.
    /// Keeps the prompt within typical context window limits.
    /// </summary>
    private const int MaxDocumentChars = 8000;

    /// <summary>
    /// Most characters sent in one translation request. Longer text is translated in parts.
    /// </summary>
    internal const int MaxTranslationChars = 4000;

    /// <summary>
    /// Chat options configured for factual, deterministic AI output.
    /// </summary>
    private static readonly ChatOptions FactualChatOptions = new()
    {
        Temperature = 0.3,
        MaxTokens = 2048,
    };

    public SummaryService(
        IAiService aiService,
        AgentXDbContext db,
        ILogger logger,
        IHierarchicalSummaryService? hierarchicalSummaryService = null)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _hierarchicalSummaryService = hierarchicalSummaryService ?? new HierarchicalSummaryService(aiService, logger);
        _log = logger?.ForContext<SummaryService>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> SummarizeDocumentAsync(long documentId, CancellationToken ct = default)
    {
        _log.Information("Starting document summarization for document {DocumentId}", documentId);

        var document = await LoadDocumentAsync(documentId, ct).ConfigureAwait(false);
        var summaryResult = await _hierarchicalSummaryService
            .BuildSummaryAsync(document.FileName, GetDocumentSections(document), ct)
            .ConfigureAwait(false);

        _log.Information(
            "Completed summarization for document {DocumentId} '{FileName}' ({SummaryLength} chars, {IncludedSections}/{TotalSections} sections)",
            documentId, document.FileName, summaryResult.DocumentSummary.Length,
            summaryResult.SectionsIncluded, summaryResult.TotalSections);

        return summaryResult.DocumentSummary;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ExtractKeyPointsAsync(long documentId, CancellationToken ct = default)
    {
        _log.Information("Starting key-point extraction for document {DocumentId}", documentId);

        var document = await LoadDocumentAsync(documentId, ct).ConfigureAwait(false);
        var summaryResult = await _hierarchicalSummaryService
            .BuildSummaryAsync(document.FileName, GetDocumentSections(document), ct)
            .ConfigureAwait(false);

        _log.Information(
            "Extracted {Count} key points from document {DocumentId} '{FileName}'",
            summaryResult.KeyPoints.Count, documentId, document.FileName);

        return summaryResult.KeyPoints;
    }

    /// <inheritdoc />
    public async Task<string> TranslateTextAsync(string text, string targetLanguage, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text to translate must not be null or empty.", nameof(text));
        }

        if (string.IsNullOrWhiteSpace(targetLanguage))
        {
            throw new ArgumentException("Target language must not be null or empty.", nameof(targetLanguage));
        }

        _log.Information(
            "Starting translation to {TargetLanguage} ({InputLength} chars)",
            targetLanguage, text.Length);

        string translation;
        if (text.Length <= MaxTranslationChars)
        {
            translation = await TranslatePartAsync(text, targetLanguage, ct).ConfigureAwait(false);
        }
        else
        {
            // Everything past the limit used to be cut off without a word to the user. Longer text
            // is translated in parts that end at a paragraph, line or sentence break where there
            // is one, and the parts are joined in order with the breaks between them.
            var parts = SplitForTranslation(text, MaxTranslationChars);
            _log.Information(
                "Translating {InputLength} chars in {PartCount} parts of at most {MaxLength}",
                text.Length, parts.Count, MaxTranslationChars);

            var joined = new StringBuilder(text.Length);
            foreach (var part in parts)
            {
                ct.ThrowIfCancellationRequested();
                joined.Append(string.IsNullOrWhiteSpace(part.Text)
                    ? part.Text
                    : await TranslatePartAsync(part.Text, targetLanguage, ct).ConfigureAwait(false));
                joined.Append(part.Separator);
            }

            translation = joined.ToString().Trim();
        }

        _log.Information(
            "Completed translation to {TargetLanguage} ({OutputLength} chars)",
            targetLanguage, translation.Length);

        return translation;
    }

    /// <summary>Translates text that fits in one request.</summary>
    private Task<string> TranslatePartAsync(string text, string targetLanguage, CancellationToken ct)
    {
        var prompt = $"Translate the following text to {targetLanguage}. Provide only the translation, no explanations.\n\nTEXT:\n{text}";

        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = prompt }
        };

        return StreamToStringAsync(messages, ct);
    }

    /// <summary>
    /// One piece of a text too long to translate in one request: <see cref="Text"/> to translate
    /// and the <see cref="Separator"/> (whitespace) that followed it in the original.
    /// </summary>
    internal sealed record TranslationPart(string Text, string Separator);

    /// <summary>
    /// Breaks, from the most to the least natural place to split a text: blank lines between
    /// paragraphs, line breaks, sentence ends (Latin punctuation followed by whitespace, or CJK
    /// full stops), and whitespace between words.
    /// </summary>
    private static readonly Regex[] TranslationBreaks =
    [
        new(@"(?:\r?\n[ \t]*){2,}", RegexOptions.Compiled),
        new(@"\r?\n", RegexOptions.Compiled),
        new(@"(?<=[.!?])\s+|(?<=[\u3002\uFF01\uFF1F])\s*", RegexOptions.Compiled),
        new(@"\s+", RegexOptions.Compiled),
    ];

    /// <summary>
    /// Splits <paramref name="text"/> into parts of at most <paramref name="maxChars"/> characters,
    /// each ending at the most natural break available, and packs neighbouring pieces together so
    /// as few requests as possible are made. Joining every part's text and separator in order gives
    /// back <paramref name="text"/> exactly; a run with no break at all is cut at the limit.
    /// </summary>
    internal static IReadOnlyList<TranslationPart> SplitForTranslation(string text, int maxChars)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChars, 1);

        var pieces = new List<TranslationPart>();
        SplitAtBreaks(text, string.Empty, 0, maxChars, pieces);

        var parts = new List<TranslationPart>();
        var current = new StringBuilder();
        var separator = string.Empty;
        var started = false;
        foreach (var piece in pieces)
        {
            if (started && current.Length + separator.Length + piece.Text.Length > maxChars)
            {
                parts.Add(new TranslationPart(current.ToString(), separator));
                current.Clear();
                started = false;
            }
            else if (started)
            {
                current.Append(separator);
            }

            current.Append(piece.Text);
            separator = piece.Separator;
            started = true;
        }

        if (started)
        {
            parts.Add(new TranslationPart(current.ToString(), separator));
        }

        return parts;
    }

    private static void SplitAtBreaks(string text, string separator, int level, int maxChars, List<TranslationPart> pieces)
    {
        if (text.Length <= maxChars)
        {
            pieces.Add(new TranslationPart(text, separator));
            return;
        }

        if (level == TranslationBreaks.Length)
        {
            // No break left to split at: cut at the limit.
            for (var start = 0; start < text.Length; start += maxChars)
            {
                var end = Math.Min(start + maxChars, text.Length);
                pieces.Add(new TranslationPart(text[start..end], end == text.Length ? separator : string.Empty));
            }

            return;
        }

        var position = 0;
        foreach (Match match in TranslationBreaks[level].Matches(text))
        {
            if (match.Length == 0 && match.Index == position)
            {
                continue;
            }

            SplitAtBreaks(text[position..match.Index], match.Value, level + 1, maxChars, pieces);
            position = match.Index + match.Length;
        }

        SplitAtBreaks(text[position..], separator, level + 1, maxChars, pieces);
    }

    // -- Private helpers --------------------------------------------------

    /// <summary>
    /// Loads a document and its chunks from the database, concatenates chunk text
    /// up to <see cref="MaxDocumentChars"/>, and returns the document entity with
    /// the concatenated text.
    /// </summary>
    private async Task<Data.Entities.DocumentEntity> LoadDocumentAsync(
        long documentId, CancellationToken ct)
    {
        var document = await _db.Documents
            .Include(d => d.Chunks.OrderBy(c => c.ChunkIndex))
            .FirstOrDefaultAsync(d => d.Id == documentId, ct)
            .ConfigureAwait(false);

        if (document is null)
        {
            _log.Error("Document {DocumentId} not found in database", documentId);
            throw new InvalidOperationException(
                $"Document with ID {documentId} was not found.");
        }

        if (document.Chunks.Count == 0)
        {
            _log.Error(
                "Document {DocumentId} '{FileName}' has no indexed chunks. " +
                "The document must be fully indexed before it can be summarized.",
                documentId, document.FileName);
            throw new InvalidOperationException(
                $"Document '{document.FileName}' (ID: {documentId}) has no indexed chunks. " +
                "Please ensure the document has been fully indexed before requesting a summary.");
        }

        return document;
    }

    private static IReadOnlyList<string> GetDocumentSections(Data.Entities.DocumentEntity document)
    {
        var sb = new StringBuilder(MaxDocumentChars);
        var sections = new List<string>(document.Chunks.Count);

        foreach (var chunk in document.Chunks.OrderBy(c => c.ChunkIndex))
        {
            if (sb.Length >= MaxDocumentChars)
                break;

            var remaining = MaxDocumentChars - sb.Length;
            var chunkText = chunk.Content.Length <= remaining
                ? chunk.Content
                : chunk.Content[..remaining];

            sections.Add(chunkText);
            sb.Append(chunkText);
        }

        return sections;
    }

    /// <summary>
    /// Streams a chat completion from the AI service and collects the full response
    /// into a single string.
    /// </summary>
    private async Task<string> StreamToStringAsync(
        IReadOnlyList<ChatMessage> messages, CancellationToken ct)
    {
        var sb = new StringBuilder(1024);

        await foreach (var token in _aiService.StreamChatAsync(messages, options: FactualChatOptions, ct: ct)
                           .WithCancellation(ct)
                           .ConfigureAwait(false))
        {
            sb.Append(token);
        }

        return sb.ToString().Trim();
    }
}
