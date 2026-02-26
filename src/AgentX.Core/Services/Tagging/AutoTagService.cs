using System.Text.Json;
using System.Text.RegularExpressions;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Tagging;

/// <summary>
/// EF Core-backed implementation of <see cref="IAutoTagService"/>.
/// Provides AI-powered automatic tag generation and manual tag management
/// for the document knowledge base.
/// </summary>
public class AutoTagService : IAutoTagService
{
    private readonly AgentXDbContext _db;
    private readonly IAiService _aiService;
    private readonly ILogger _log;

    /// <summary>
    /// Maximum number of characters sent to the AI model for tag generation.
    /// Truncating to 2000 characters keeps token usage reasonable while still
    /// providing enough context for meaningful tags.
    /// </summary>
    private const int MaxContentLength = 2000;

    /// <summary>
    /// Default confidence score assigned when JSON parsing of the AI response
    /// fails and we fall back to simple text splitting.
    /// </summary>
    private const double FallbackConfidence = 0.7;

    /// <summary>
    /// The system prompt used when calling IAiService.ChatAsync for tag generation.
    /// </summary>
    private const string TagGenerationSystemPrompt =
        "You are a document tagging assistant. You analyze document content and produce " +
        "concise, descriptive tags. Always respond with valid JSON only, no extra text.";

    public AutoTagService(AgentXDbContext db, IAiService aiService, ILogger logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _log = logger?.ForContext<AutoTagService>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(string TagName, double Confidence)>> GenerateTagsAsync(
        string documentContent,
        int maxTags = 5,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(documentContent))
            {
                _log.Warning("Cannot generate tags: document content is empty");
                return Array.Empty<(string, double)>();
            }

            // Truncate content to limit token usage
            var truncatedContent = documentContent.Length > MaxContentLength
                ? documentContent[..MaxContentLength]
                : documentContent;

            // First attempt: use the dedicated GenerateTagsAsync on IAiService
            try
            {
                var aiTags = await _aiService.GenerateTagsAsync(truncatedContent, maxTags, ct);

                if (aiTags is { Count: > 0 })
                {
                    var results = aiTags
                        .Take(maxTags)
                        .Select(tag => (TagName: NormalizeTagName(tag), Confidence: 0.85))
                        .Where(t => !string.IsNullOrWhiteSpace(t.TagName))
                        .ToList();

                    _log.Information(
                        "Generated {TagCount} tags via IAiService.GenerateTagsAsync",
                        results.Count);

                    return results;
                }
            }
            catch (Exception ex)
            {
                _log.Warning(
                    ex,
                    "IAiService.GenerateTagsAsync failed, falling back to ChatAsync prompt");
            }

            // Fallback: use ChatAsync with a structured prompt
            return await GenerateTagsViaChatAsync(truncatedContent, maxTags, ct);
        }
        catch (OperationCanceledException)
        {
            _log.Debug("Tag generation cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to generate tags for document content");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ApplyAutoTagsAsync(long documentId, CancellationToken ct = default)
    {
        try
        {
            // Load the document
            var document = await _db.Documents
                .FirstOrDefaultAsync(d => d.Id == documentId, ct);

            if (document is null)
            {
                _log.Error("Cannot auto-tag: document {DocumentId} not found", documentId);
                throw new InvalidOperationException($"Document {documentId} not found.");
            }

            // Gather content for tag generation: prefer chunks, fall back to file read
            var content = await GetDocumentContentAsync(document, ct);

            if (string.IsNullOrWhiteSpace(content))
            {
                _log.Warning(
                    "Cannot auto-tag document {DocumentId}: no extractable text content",
                    documentId);
                return;
            }

            // Generate tags via AI
            var generatedTags = await GenerateTagsAsync(content, maxTags: 5, ct);

            if (generatedTags.Count == 0)
            {
                _log.Information(
                    "No tags generated for document {DocumentId}", documentId);
                return;
            }

            var appliedCount = 0;
            var skippedCount = 0;

            foreach (var (tagName, confidence) in generatedTags)
            {
                if (string.IsNullOrWhiteSpace(tagName))
                {
                    continue;
                }

                // Find or create the TagEntity (case-insensitive match)
                var normalizedName = NormalizeTagName(tagName);
                var tagEntity = await _db.Tags
                    .FirstOrDefaultAsync(
                        t => t.Name.ToLower() == normalizedName.ToLower(), ct);

                if (tagEntity is null)
                {
                    tagEntity = new TagEntity
                    {
                        Name = normalizedName,
                        IsAutoGenerated = true,
                        CreatedAt = DateTime.UtcNow,
                    };

                    _db.Tags.Add(tagEntity);
                    await _db.SaveChangesAsync(ct);

                    _log.Debug(
                        "Created auto-generated tag '{TagName}' (Id={TagId})",
                        tagEntity.Name, tagEntity.Id);
                }

                // Check if the document-tag association already exists
                var associationExists = await _db.DocumentTags
                    .AnyAsync(
                        dt => dt.DocumentId == documentId && dt.TagId == tagEntity.Id, ct);

                if (associationExists)
                {
                    skippedCount++;
                    continue;
                }

                // Create the association
                var documentTag = new DocumentTagEntity
                {
                    DocumentId = documentId,
                    TagId = tagEntity.Id,
                    Confidence = confidence,
                    AssignedAt = DateTime.UtcNow,
                };

                _db.DocumentTags.Add(documentTag);
                appliedCount++;
            }

            await _db.SaveChangesAsync(ct);

            _log.Information(
                "Auto-tagged document {DocumentId} '{FileName}': applied {AppliedCount} tags, skipped {SkippedCount} duplicates",
                documentId, document.FileName, appliedCount, skippedCount);
        }
        catch (OperationCanceledException)
        {
            _log.Debug("Auto-tagging cancelled for document {DocumentId}", documentId);
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to auto-tag document {DocumentId}", documentId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TagEntity>> GetAllTagsAsync()
    {
        try
        {
            var tags = await _db.Tags
                .OrderBy(t => t.Name)
                .ToListAsync();

            _log.Debug("Retrieved {Count} tags", tags.Count);

            return tags;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get all tags");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<TagEntity> CreateTagAsync(string name, string? colorHex = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Tag name must not be empty.", nameof(name));
            }

            var normalizedName = NormalizeTagName(name);

            // Enforce uniqueness (case-insensitive)
            var existingTag = await _db.Tags
                .FirstOrDefaultAsync(t => t.Name.ToLower() == normalizedName.ToLower());

            if (existingTag is not null)
            {
                throw new InvalidOperationException(
                    $"A tag with the name '{normalizedName}' already exists (Id={existingTag.Id}).");
            }

            var tag = new TagEntity
            {
                Name = normalizedName,
                ColorHex = colorHex?.Trim(),
                IsAutoGenerated = false,
                CreatedAt = DateTime.UtcNow,
            };

            _db.Tags.Add(tag);
            await _db.SaveChangesAsync();

            _log.Information(
                "Created tag {TagId} '{Name}' (color={ColorHex})",
                tag.Id, tag.Name, tag.ColorHex ?? "none");

            return tag;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to create tag '{Name}'", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteTagAsync(long tagId)
    {
        try
        {
            var tag = await _db.Tags.FindAsync(tagId);
            if (tag is null)
            {
                _log.Warning("Cannot delete: tag {TagId} not found", tagId);
                return;
            }

            // DocumentTagEntity entries are cascade-deleted by the DB relationship
            _db.Tags.Remove(tag);
            await _db.SaveChangesAsync();

            _log.Information(
                "Deleted tag {TagId} '{Name}' (cascade removes document associations)",
                tagId, tag.Name);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to delete tag {TagId}", tagId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task AssignTagAsync(long documentId, long tagId)
    {
        try
        {
            // Validate entities exist
            var documentExists = await _db.Documents.AnyAsync(d => d.Id == documentId);
            if (!documentExists)
            {
                throw new InvalidOperationException($"Document {documentId} not found.");
            }

            var tagExists = await _db.Tags.AnyAsync(t => t.Id == tagId);
            if (!tagExists)
            {
                throw new InvalidOperationException($"Tag {tagId} not found.");
            }

            // Check for duplicate association
            var alreadyAssigned = await _db.DocumentTags
                .AnyAsync(dt => dt.DocumentId == documentId && dt.TagId == tagId);

            if (alreadyAssigned)
            {
                _log.Debug(
                    "Tag {TagId} is already assigned to document {DocumentId}, skipping",
                    tagId, documentId);
                return;
            }

            var documentTag = new DocumentTagEntity
            {
                DocumentId = documentId,
                TagId = tagId,
                Confidence = 1.0, // Manual assignment = full confidence
                AssignedAt = DateTime.UtcNow,
            };

            _db.DocumentTags.Add(documentTag);
            await _db.SaveChangesAsync();

            _log.Information(
                "Manually assigned tag {TagId} to document {DocumentId}",
                tagId, documentId);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex, "Failed to assign tag {TagId} to document {DocumentId}",
                tagId, documentId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RemoveTagAsync(long documentId, long tagId)
    {
        try
        {
            var documentTag = await _db.DocumentTags
                .FirstOrDefaultAsync(dt => dt.DocumentId == documentId && dt.TagId == tagId);

            if (documentTag is null)
            {
                _log.Warning(
                    "No association found between document {DocumentId} and tag {TagId}",
                    documentId, tagId);
                return;
            }

            _db.DocumentTags.Remove(documentTag);
            await _db.SaveChangesAsync();

            _log.Information(
                "Removed tag {TagId} from document {DocumentId}",
                tagId, documentId);
        }
        catch (Exception ex)
        {
            _log.Error(
                ex, "Failed to remove tag {TagId} from document {DocumentId}",
                tagId, documentId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TagEntity>> GetTagsForDocumentAsync(long documentId)
    {
        try
        {
            var tags = await _db.DocumentTags
                .Where(dt => dt.DocumentId == documentId)
                .Include(dt => dt.Tag)
                .Select(dt => dt.Tag)
                .OrderBy(t => t.Name)
                .ToListAsync();

            _log.Debug(
                "Retrieved {Count} tags for document {DocumentId}",
                tags.Count, documentId);

            return tags;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get tags for document {DocumentId}", documentId);
            throw;
        }
    }

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Gathers document text content for tag generation. Prefers already-extracted
    /// chunk content from the database; falls back to reading the source file.
    /// </summary>
    private async Task<string> GetDocumentContentAsync(
        DocumentEntity document,
        CancellationToken ct)
    {
        // Try to build content from existing chunks (already extracted text)
        var chunks = await _db.DocumentChunks
            .Where(c => c.DocumentId == document.Id)
            .OrderBy(c => c.ChunkIndex)
            .Select(c => c.Content)
            .ToListAsync(ct);

        if (chunks.Count > 0)
        {
            var combinedChunkContent = string.Join(" ", chunks);
            _log.Debug(
                "Built content for document {DocumentId} from {ChunkCount} chunks ({Length} chars)",
                document.Id, chunks.Count, combinedChunkContent.Length);
            return combinedChunkContent;
        }

        // Fallback: try to read the raw file if it is a plain text type
        if (!string.IsNullOrEmpty(document.FilePath) && File.Exists(document.FilePath))
        {
            var textTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "txt", "md", "markdown", "csv", "json", "xml", "html", "htm",
                "log", "yaml", "yml", "ini", "cfg", "conf", "toml",
            };

            if (textTypes.Contains(document.FileType))
            {
                try
                {
                    var fileContent = await File.ReadAllTextAsync(document.FilePath, ct);
                    _log.Debug(
                        "Read {Length} chars from file for document {DocumentId}",
                        fileContent.Length, document.Id);
                    return fileContent;
                }
                catch (Exception ex)
                {
                    _log.Warning(
                        ex,
                        "Failed to read file '{FilePath}' for document {DocumentId}, returning empty",
                        document.FilePath, document.Id);
                }
            }
        }

        // Last resort: use summary or extracted title if available
        if (!string.IsNullOrWhiteSpace(document.Summary))
        {
            return document.Summary;
        }

        if (!string.IsNullOrWhiteSpace(document.ExtractedTitle))
        {
            return document.ExtractedTitle;
        }

        return string.Empty;
    }

    /// <summary>
    /// Generates tags by sending a structured prompt to IAiService.ChatAsync,
    /// then parses the JSON response. Falls back to simple text splitting if
    /// JSON parsing fails.
    /// </summary>
    private async Task<IReadOnlyList<(string TagName, double Confidence)>> GenerateTagsViaChatAsync(
        string content,
        int maxTags,
        CancellationToken ct)
    {
        var userPrompt =
            $"Generate {maxTags} descriptive tags for the following document content. " +
            "Return ONLY a JSON array of objects with \"tag\" and \"confidence\" fields. " +
            "Confidence should be 0.0-1.0. Example: [{\"tag\":\"machine-learning\",\"confidence\":0.95}]\n\n" +
            $"CONTENT:\n{content}";

        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = userPrompt },
        };

        var options = new ChatOptions
        {
            Temperature = 0.3, // Low temperature for consistent structured output
            MaxTokens = 512,
        };

        string response;
        try
        {
            response = await _aiService.ChatAsync(messages, TagGenerationSystemPrompt, options, ct);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "ChatAsync failed during tag generation fallback");
            return Array.Empty<(string, double)>();
        }

        if (string.IsNullOrWhiteSpace(response))
        {
            _log.Warning("AI returned empty response for tag generation");
            return Array.Empty<(string, double)>();
        }

        // Attempt to parse structured JSON response
        var tags = TryParseTagJson(response, maxTags);

        if (tags.Count > 0)
        {
            _log.Information(
                "Generated {TagCount} tags via ChatAsync JSON response", tags.Count);
            return tags;
        }

        // Fallback: split response by common delimiters
        tags = ParseTagsFallback(response, maxTags);
        _log.Information(
            "Generated {TagCount} tags via ChatAsync fallback parsing", tags.Count);

        return tags;
    }

    /// <summary>
    /// Attempts to parse the AI response as a JSON array of objects
    /// with "tag" and "confidence" fields.
    /// </summary>
    private List<(string TagName, double Confidence)> TryParseTagJson(
        string response,
        int maxTags)
    {
        var results = new List<(string TagName, double Confidence)>();

        try
        {
            // Extract JSON array from response (the AI might include extra text)
            var jsonMatch = Regex.Match(response, @"\[[\s\S]*?\]");
            if (!jsonMatch.Success)
            {
                return results;
            }

            var jsonArray = jsonMatch.Value;

            using var doc = JsonDocument.Parse(jsonArray);

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (results.Count >= maxTags)
                {
                    break;
                }

                var tagName = string.Empty;
                var confidence = FallbackConfidence;

                // Try multiple common field names for the tag
                if (element.TryGetProperty("tag", out var tagProp))
                {
                    tagName = tagProp.GetString() ?? string.Empty;
                }
                else if (element.TryGetProperty("name", out var nameProp))
                {
                    tagName = nameProp.GetString() ?? string.Empty;
                }

                // Try to read confidence
                if (element.TryGetProperty("confidence", out var confProp))
                {
                    if (confProp.ValueKind == JsonValueKind.Number)
                    {
                        confidence = confProp.GetDouble();
                    }
                }

                var normalized = NormalizeTagName(tagName);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    // Clamp confidence to [0.0, 1.0]
                    confidence = Math.Clamp(confidence, 0.0, 1.0);
                    results.Add((normalized, confidence));
                }
            }
        }
        catch (JsonException ex)
        {
            _log.Debug(ex, "JSON parsing failed for tag response, will use fallback");
        }

        return results;
    }

    /// <summary>
    /// Fallback parser: splits the AI response by commas, newlines, or semicolons
    /// and treats each segment as a tag name with a default confidence.
    /// </summary>
    private static List<(string TagName, double Confidence)> ParseTagsFallback(
        string response,
        int maxTags)
    {
        var results = new List<(string TagName, double Confidence)>();

        // Split by common delimiters
        var candidates = response.Split(
            new[] { ',', '\n', '\r', ';', '|' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var candidate in candidates)
        {
            if (results.Count >= maxTags)
            {
                break;
            }

            // Strip common noise characters (bullets, numbering, quotes, brackets)
            var cleaned = Regex.Replace(candidate, @"^[\d\.\)\-\*\#\[\]""'\s]+", "").Trim();
            cleaned = Regex.Replace(cleaned, @"[""'\[\]\{\}]+$", "").Trim();

            var normalized = NormalizeTagName(cleaned);
            if (!string.IsNullOrWhiteSpace(normalized) && normalized.Length >= 2)
            {
                results.Add((normalized, FallbackConfidence));
            }
        }

        return results;
    }

    /// <summary>
    /// Normalizes a tag name: trims whitespace, converts to lowercase,
    /// and replaces spaces with hyphens for consistency.
    /// </summary>
    private static string NormalizeTagName(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return string.Empty;
        }

        var normalized = tagName.Trim().ToLowerInvariant();

        // Replace spaces and underscores with hyphens for a consistent tag format
        normalized = Regex.Replace(normalized, @"[\s_]+", "-");

        // Remove any non-alphanumeric characters except hyphens
        normalized = Regex.Replace(normalized, @"[^a-z0-9\-]", "");

        // Collapse multiple hyphens into one
        normalized = Regex.Replace(normalized, @"-{2,}", "-");

        // Trim leading/trailing hyphens
        normalized = normalized.Trim('-');

        return normalized;
    }
}
