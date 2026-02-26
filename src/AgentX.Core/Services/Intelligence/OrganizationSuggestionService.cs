using System.Text;
using System.Text.Json;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Intelligence.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Intelligence;

/// <summary>
/// AI-powered implementation of <see cref="IOrganizationSuggestionService"/> that analyzes
/// uncategorized documents and suggests appropriate collections and tags based on content.
/// Uses the active AI provider to generate structured JSON suggestions.
/// </summary>
public class OrganizationSuggestionService : IOrganizationSuggestionService
{
    private readonly IAiService _aiService;
    private readonly AgentXDbContext _db;
    private readonly ICollectionService _collectionService;
    private readonly ILogger _log;

    /// <summary>
    /// Maximum characters to include from the first chunk of each document in the batch prompt.
    /// </summary>
    private const int MaxContentPreviewChars = 100;

    /// <summary>
    /// Chat options configured for structured, deterministic AI output.
    /// </summary>
    private static readonly ChatOptions StructuredChatOptions = new()
    {
        Temperature = 0.3,
        MaxTokens = 4096,
    };

    /// <summary>
    /// JSON serializer options for parsing AI-generated JSON responses.
    /// Configured with lenient settings to handle imperfect AI output.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public OrganizationSuggestionService(
        IAiService aiService,
        AgentXDbContext db,
        ICollectionService collectionService,
        ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _collectionService = collectionService ?? throw new ArgumentNullException(nameof(collectionService));
        _log = logger?.ForContext<OrganizationSuggestionService>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrganizationSuggestion>> SuggestOrganizationAsync(
        int maxDocuments = 20, CancellationToken ct = default)
    {
        if (maxDocuments <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDocuments), maxDocuments,
                "maxDocuments must be a positive integer.");
        }

        _log.Information(
            "Starting organization suggestion analysis (maxDocuments: {MaxDocuments})",
            maxDocuments);

        try
        {
            // Find documents that have no collection associations
            var uncategorizedDocuments = await _db.Documents
                .AsNoTracking()
                .Include(d => d.Chunks.OrderBy(c => c.ChunkIndex).Take(1))
                .Where(d => !_db.DocumentCollections.Any(dc => dc.DocumentId == d.Id))
                .OrderByDescending(d => d.ImportedAt)
                .Take(maxDocuments)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (uncategorizedDocuments.Count == 0)
            {
                _log.Information("No uncategorized documents found. All documents are organized.");
                return Array.Empty<OrganizationSuggestion>();
            }

            _log.Debug(
                "Found {Count} uncategorized documents to analyze",
                uncategorizedDocuments.Count);

            ct.ThrowIfCancellationRequested();

            // Load existing collection names for context
            var existingCollections = await _collectionService.GetAllCollectionsAsync()
                .ConfigureAwait(false);

            var collectionNames = existingCollections
                .Select(c => c.Name)
                .Distinct()
                .ToList();

            var collectionNamesString = collectionNames.Count > 0
                ? string.Join(", ", collectionNames)
                : "none (you may suggest new collection names)";

            // Build the batch prompt with document details
            var promptBuilder = new StringBuilder(4096);
            promptBuilder.AppendLine(
                "Given these existing collections: [" + collectionNamesString + "], " +
                "suggest which collection each document belongs to and 2-3 relevant tags.");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine(
                "For each document, output a JSON object on a separate line with this format:");
            promptBuilder.AppendLine(
                "{\"document_index\": 1, \"suggested_collection\": \"...\", " +
                "\"tags\": [\"tag1\", \"tag2\"], \"reasoning\": \"...\", \"confidence\": 0.85}");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("Output ONLY the JSON objects, one per line. No other text.");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("Documents:");

            for (var i = 0; i < uncategorizedDocuments.Count; i++)
            {
                var doc = uncategorizedDocuments[i];
                var contentPreview = GetContentPreview(doc);
                promptBuilder.AppendLine($"{i + 1}. {doc.FileName}: {contentPreview}");
            }

            var messages = new List<ChatMessage>
            {
                new() { Role = "user", Content = promptBuilder.ToString() }
            };

            ct.ThrowIfCancellationRequested();

            // Stream the AI response
            _log.Debug("Sending batch organization prompt to AI service");

            var responseSb = new StringBuilder(2048);
            await foreach (var token in _aiService
                               .StreamChatAsync(messages, options: StructuredChatOptions, ct: ct)
                               .WithCancellation(ct)
                               .ConfigureAwait(false))
            {
                responseSb.Append(token);
            }

            var response = responseSb.ToString().Trim();

            _log.Debug(
                "Received AI response for organization suggestions ({Length} chars)",
                response.Length);

            // Parse the AI response into OrganizationSuggestion objects
            var suggestions = ParseOrganizationResponse(response, uncategorizedDocuments);

            _log.Information(
                "Organization suggestion analysis complete: generated {Count} suggestions " +
                "for {DocumentCount} uncategorized documents",
                suggestions.Count, uncategorizedDocuments.Count);

            return suggestions.AsReadOnly();
        }
        catch (OperationCanceledException)
        {
            _log.Information("Organization suggestion analysis was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to generate organization suggestions");
            throw;
        }
    }

    // -- Private helpers --------------------------------------------------

    /// <summary>
    /// Extracts a short content preview from the first chunk of a document.
    /// Returns the first <see cref="MaxContentPreviewChars"/> characters of the first chunk,
    /// or a placeholder if no chunks are available.
    /// </summary>
    private static string GetContentPreview(Data.Entities.DocumentEntity document)
    {
        var firstChunk = document.Chunks.FirstOrDefault();
        if (firstChunk is null || string.IsNullOrWhiteSpace(firstChunk.Content))
        {
            return "(no content preview available)";
        }

        var content = firstChunk.Content.Trim();
        if (content.Length <= MaxContentPreviewChars)
        {
            return content;
        }

        return content[..MaxContentPreviewChars] + "...";
    }

    /// <summary>
    /// Parses the AI-generated response into a list of <see cref="OrganizationSuggestion"/> objects.
    /// Handles multiple response formats gracefully:
    /// JSON array, JSON Lines (JSONL), or freeform text with embedded JSON.
    /// Falls back gracefully when the AI output is not perfectly formatted.
    /// </summary>
    private List<OrganizationSuggestion> ParseOrganizationResponse(
        string response,
        List<Data.Entities.DocumentEntity> documents)
    {
        var suggestions = new List<OrganizationSuggestion>();

        // First, try parsing as a JSON array
        var trimmedResponse = response.Trim();
        if (trimmedResponse.StartsWith('['))
        {
            try
            {
                var jsonSuggestions = JsonSerializer.Deserialize<List<AiSuggestionDto>>(
                    trimmedResponse, JsonOptions);

                if (jsonSuggestions is not null)
                {
                    return MapSuggestionsFromDtos(jsonSuggestions, documents);
                }
            }
            catch (JsonException ex)
            {
                _log.Warning(
                    ex,
                    "Failed to parse AI response as JSON array, falling back to line-by-line parsing");
            }
        }

        // Fall back to line-by-line JSON parsing
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Skip empty or non-JSON lines
            if (string.IsNullOrWhiteSpace(line) || !line.Contains('{'))
                continue;

            // Extract JSON from the line (handle lines with leading/trailing text)
            var jsonStart = line.IndexOf('{');
            var jsonEnd = line.LastIndexOf('}');

            if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
                continue;

            var jsonText = line[jsonStart..(jsonEnd + 1)];

            try
            {
                var dto = JsonSerializer.Deserialize<AiSuggestionDto>(jsonText, JsonOptions);
                if (dto is not null)
                {
                    var suggestion = MapSuggestionFromDto(dto, documents);
                    if (suggestion is not null)
                    {
                        suggestions.Add(suggestion);
                    }
                }
            }
            catch (JsonException ex)
            {
                _log.Debug(
                    ex, "Failed to parse JSON from line: {Line}",
                    line.Length > 200 ? line[..200] + "..." : line);
            }
        }

        // If we couldn't parse any suggestions, log a warning
        if (suggestions.Count == 0 && documents.Count > 0)
        {
            _log.Warning(
                "Failed to parse any organization suggestions from AI response. " +
                "Response length: {ResponseLength} chars. First 500 chars: {ResponsePreview}",
                response.Length,
                response.Length > 500 ? response[..500] + "..." : response);
        }

        return suggestions;
    }

    /// <summary>
    /// Maps a list of parsed DTOs to <see cref="OrganizationSuggestion"/> objects,
    /// correlating each DTO to its corresponding document by index.
    /// </summary>
    private List<OrganizationSuggestion> MapSuggestionsFromDtos(
        List<AiSuggestionDto> dtos,
        List<Data.Entities.DocumentEntity> documents)
    {
        var suggestions = new List<OrganizationSuggestion>();

        foreach (var dto in dtos)
        {
            var suggestion = MapSuggestionFromDto(dto, documents);
            if (suggestion is not null)
            {
                suggestions.Add(suggestion);
            }
        }

        return suggestions;
    }

    /// <summary>
    /// Maps a single parsed DTO to an <see cref="OrganizationSuggestion"/>,
    /// correlating it to the corresponding document by the 1-based document_index field.
    /// Returns null if the document index is out of range.
    /// </summary>
    private OrganizationSuggestion? MapSuggestionFromDto(
        AiSuggestionDto dto,
        List<Data.Entities.DocumentEntity> documents)
    {
        // The document_index in the prompt is 1-based
        var documentIndex = dto.DocumentIndex - 1;

        if (documentIndex < 0 || documentIndex >= documents.Count)
        {
            _log.Debug(
                "AI suggestion references out-of-range document_index {Index} (total: {Count})",
                dto.DocumentIndex, documents.Count);
            return null;
        }

        var document = documents[documentIndex];

        return new OrganizationSuggestion
        {
            DocumentId = document.Id,
            FileName = document.FileName,
            SuggestedCollection = dto.SuggestedCollection?.Trim() ?? "Uncategorized",
            SuggestedTags = dto.Tags?
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct()
                .ToList() ?? new List<string>(),
            Reasoning = dto.Reasoning?.Trim() ?? string.Empty,
            Confidence = Math.Clamp(dto.Confidence, 0f, 1f),
        };
    }

    /// <summary>
    /// Internal DTO for deserializing the AI-generated JSON suggestion objects.
    /// Uses <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/> to
    /// support the snake_case property names used in the AI prompt.
    /// </summary>
    private sealed class AiSuggestionDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("document_index")]
        public int DocumentIndex { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("suggested_collection")]
        public string? SuggestedCollection { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("reasoning")]
        public string? Reasoning { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("confidence")]
        public float Confidence { get; set; }
    }
}
