using System.Text;
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
    private readonly ILogger _log;

    /// <summary>
    /// Maximum number of characters to extract from document chunks for AI context.
    /// Keeps the prompt within typical context window limits.
    /// </summary>
    private const int MaxDocumentChars = 8000;

    /// <summary>
    /// Maximum number of characters accepted for translation input.
    /// </summary>
    private const int MaxTranslationChars = 4000;

    /// <summary>
    /// Chat options configured for factual, deterministic AI output.
    /// </summary>
    private static readonly ChatOptions FactualChatOptions = new()
    {
        Temperature = 0.3,
        MaxTokens = 2048,
    };

    public SummaryService(IAiService aiService, AgentXDbContext db, ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _log = logger?.ForContext<SummaryService>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> SummarizeDocumentAsync(long documentId, CancellationToken ct = default)
    {
        _log.Information("Starting document summarization for document {DocumentId}", documentId);

        var (document, text) = await LoadDocumentTextAsync(documentId, ct).ConfigureAwait(false);

        var prompt = $"Summarize the following document concisely. Focus on the main topics, key findings, and conclusions.\n\nDOCUMENT TITLE: {document.FileName}\n\n{text}";

        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = prompt }
        };

        var summary = await StreamToStringAsync(messages, ct).ConfigureAwait(false);

        _log.Information(
            "Completed summarization for document {DocumentId} '{FileName}' ({SummaryLength} chars)",
            documentId, document.FileName, summary.Length);

        return summary;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ExtractKeyPointsAsync(long documentId, CancellationToken ct = default)
    {
        _log.Information("Starting key-point extraction for document {DocumentId}", documentId);

        var (document, text) = await LoadDocumentTextAsync(documentId, ct).ConfigureAwait(false);

        var prompt = $"Extract the key points from the following document as a numbered list. Each point should be one concise sentence.\n\nDOCUMENT: {document.FileName}\n\n{text}";

        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = prompt }
        };

        var response = await StreamToStringAsync(messages, ct).ConfigureAwait(false);

        var keyPoints = ParseKeyPoints(response);

        _log.Information(
            "Extracted {Count} key points from document {DocumentId} '{FileName}'",
            keyPoints.Count, documentId, document.FileName);

        return keyPoints;
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

        // Cap input text to prevent exceeding context limits
        var inputText = text.Length > MaxTranslationChars
            ? text[..MaxTranslationChars]
            : text;

        if (text.Length > MaxTranslationChars)
        {
            _log.Warning(
                "Translation input truncated from {OriginalLength} to {MaxLength} characters",
                text.Length, MaxTranslationChars);
        }

        _log.Information(
            "Starting translation to {TargetLanguage} ({InputLength} chars)",
            targetLanguage, inputText.Length);

        var prompt = $"Translate the following text to {targetLanguage}. Provide only the translation, no explanations.\n\nTEXT:\n{inputText}";

        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = prompt }
        };

        var translation = await StreamToStringAsync(messages, ct).ConfigureAwait(false);

        _log.Information(
            "Completed translation to {TargetLanguage} ({OutputLength} chars)",
            targetLanguage, translation.Length);

        return translation;
    }

    // -- Private helpers --------------------------------------------------

    /// <summary>
    /// Loads a document and its chunks from the database, concatenates chunk text
    /// up to <see cref="MaxDocumentChars"/>, and returns the document entity with
    /// the concatenated text.
    /// </summary>
    private async Task<(Data.Entities.DocumentEntity Document, string Text)> LoadDocumentTextAsync(
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

        // Concatenate chunk text, respecting the character limit
        var sb = new StringBuilder(MaxDocumentChars);
        foreach (var chunk in document.Chunks)
        {
            if (sb.Length >= MaxDocumentChars)
                break;

            var remaining = MaxDocumentChars - sb.Length;
            if (chunk.Content.Length <= remaining)
            {
                sb.Append(chunk.Content);
            }
            else
            {
                sb.Append(chunk.Content, 0, remaining);
            }

            // Add a space between chunks to prevent words from merging
            if (sb.Length < MaxDocumentChars)
            {
                sb.Append(' ');
            }
        }

        var text = sb.ToString().TrimEnd();

        _log.Debug(
            "Loaded {ChunkCount} chunks for document {DocumentId} '{FileName}', " +
            "concatenated to {TextLength} chars (max {MaxChars})",
            document.Chunks.Count, documentId, document.FileName, text.Length, MaxDocumentChars);

        return (document, text);
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

    /// <summary>
    /// Parses an AI-generated numbered/bulleted list into individual key point strings.
    /// Handles various formats: "1.", "2.", "-", "*", and plain lines.
    /// Strips numbering prefixes and returns only non-empty entries.
    /// </summary>
    private static List<string> ParseKeyPoints(string response)
    {
        var keyPoints = new List<string>();

        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Strip common numbering prefixes: "1.", "2.", "10.", etc.
            line = StripNumberingPrefix(line);

            // Strip bullet prefixes: "- ", "* ", "-- "
            line = StripBulletPrefix(line);

            line = line.Trim();

            if (!string.IsNullOrWhiteSpace(line))
            {
                keyPoints.Add(line);
            }
        }

        return keyPoints;
    }

    /// <summary>
    /// Removes leading numbering patterns like "1.", "2)", "10. ", etc.
    /// </summary>
    private static string StripNumberingPrefix(string line)
    {
        var i = 0;

        // Skip leading digits
        while (i < line.Length && char.IsDigit(line[i]))
        {
            i++;
        }

        // If we found digits followed by '.' or ')' then strip the prefix
        if (i > 0 && i < line.Length && (line[i] == '.' || line[i] == ')'))
        {
            return line[(i + 1)..];
        }

        return line;
    }

    /// <summary>
    /// Removes leading bullet characters: "-", "*", "--".
    /// </summary>
    private static string StripBulletPrefix(string line)
    {
        if (line.StartsWith("-- ", StringComparison.Ordinal))
            return line[3..];
        if (line.StartsWith("- ", StringComparison.Ordinal))
            return line[2..];
        if (line.StartsWith("* ", StringComparison.Ordinal))
            return line[2..];
        // Handle cases where bullet is not followed by space
        if (line.StartsWith('-') && line.Length > 1 && line[1] != '-')
            return line[1..];
        if (line.StartsWith('*') && line.Length > 1)
            return line[1..];

        return line;
    }
}
