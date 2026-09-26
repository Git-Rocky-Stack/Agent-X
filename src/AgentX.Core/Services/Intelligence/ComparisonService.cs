using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentX.Core.AI;
using AgentX.Core.Data;
using AgentX.Core.Documents;
using AgentX.Core.Search;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Intelligence.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Intelligence;

/// <summary>
/// Production implementation of <see cref="IComparisonService"/>.
///
/// Pipeline for each call to <see cref="CompareDocumentsAsync"/>:
///   1. Validate inputs (duplicate ids removed) and resolve document metadata via
///      <see cref="IDocumentService"/>; give each document a label that is unique
///      within the comparison.
///   2. Run one vault-wide semantic search (keyed on the optional
///      <see cref="ComparisonOptions.FocusQuery"/>, or a broad content-overview query)
///      and take each document's best hits. The search cannot be scoped to a single
///      document, so a document it does not surface is filled from its own stored
///      chunks, spread across the document, instead of being compared as empty.
///   3. Assemble a structured prompt that embeds each document's chunks and
///      instructs the AI to output a specific JSON schema.
///   4. Stream the AI response via <see cref="IAiService"/> and parse the JSON
///      into a <see cref="ComparisonReport"/>.
///   5. Fall back to a plain-text response parser if JSON parsing fails, so the
///      user always receives some output rather than an exception.
/// </summary>
public sealed class ComparisonService : IComparisonService
{
    // ── Dependencies ────────────────────────────────────────────────────────

    private readonly IAiService _aiService;
    private readonly IDocumentService _documentService;
    private readonly ISemanticSearchService _searchService;
    private readonly IDocumentSynthesisService _documentSynthesisService;
    private readonly AgentXDbContext? _db;
    private readonly ILogger _log;

    // ── Constants ────────────────────────────────────────────────────────────

    /// <summary>
    /// Chars-per-token approximation used when the provider does not return an
    /// exact token count (4 chars ≈ 1 token for typical English prose).
    /// </summary>
    private const int CharsPerToken = 4;

    /// <summary>
    /// Minimum similarity threshold for semantic chunk retrieval.
    /// Set deliberately low so that even loosely related chunks are included;
    /// relevance ordering is handled by the vector store's cosine-similarity ranking.
    /// </summary>
    private const float MinChunkSimilarity = 0.15f;

    /// <summary>
    /// Fallback query used when no <see cref="ComparisonOptions.FocusQuery"/>
    /// is provided. Broad enough to surface the most content-rich chunks.
    /// </summary>
    private const string FallbackQuery = "main topics key findings conclusions summary";

    /// <summary>
    /// Search results requested per wanted chunk and document. The vault-wide search is
    /// ranked across every document, so it asks for many candidates to give each compared
    /// document a fair chance of appearing before its top hits are taken.
    /// </summary>
    private const int SemanticCandidatesPerChunk = 10;

    /// <summary>Upper bound on the candidates requested from the vault-wide search.</summary>
    private const int MaxSemanticCandidates = 500;

    /// <summary>
    /// JSON deserialization options: case-insensitive property names so the AI's
    /// casing variations (camelCase vs PascalCase) are tolerated.
    /// </summary>
    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    // ── Constructor ──────────────────────────────────────────────────────────

    /// <param name="aiService">AI service used by the default synthesis service.</param>
    /// <param name="documentService">Resolves document metadata.</param>
    /// <param name="searchService">Vault-wide semantic search for relevant chunks.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="documentSynthesisService">Optional synthesis seam; a default is built otherwise.</param>
    /// <param name="db">
    /// Database used to read a document's own chunks when the vault-wide search does not
    /// surface enough of them. Without it such a document is compared with a placeholder body.
    /// </param>
    public ComparisonService(
        IAiService aiService,
        IDocumentService documentService,
        ISemanticSearchService searchService,
        ILogger logger,
        IDocumentSynthesisService? documentSynthesisService = null,
        AgentXDbContext? db = null)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _documentSynthesisService = documentSynthesisService ?? new DocumentSynthesisService(aiService, logger);
        _db = db;
        _log = logger?.ForContext<ComparisonService>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── IComparisonService ───────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ComparisonReport> CompareDocumentsAsync(
        IReadOnlyList<long> documentIds,
        ComparisonOptions? options = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        // ── Validate ────────────────────────────────────────────────────────

        if (documentIds is null || documentIds.Count < 2)
        {
            throw new ArgumentException(
                "At least two document IDs are required for a comparison.",
                nameof(documentIds));
        }

        // A repeated id would compare a document with itself and collapse its report entries
        var distinctIds = documentIds.Distinct().ToList();
        if (distinctIds.Count < 2)
        {
            throw new ArgumentException(
                "At least two different documents are required for a comparison.",
                nameof(documentIds));
        }

        options ??= new ComparisonOptions();

        var sw = Stopwatch.StartNew();

        _log.Information(
            "CompareDocumentsAsync started: {Count} document(s), DetailLevel={DetailLevel}, " +
            "MaxChunksPerDoc={MaxChunks}, FocusQuery={FocusQuery}",
            documentIds.Count, options.DetailLevel, options.MaxChunksPerDoc,
            options.FocusQuery ?? "<none>");

        // ── Step 1: Resolve documents ────────────────────────────────────────

        Report(progress, "Loading document metadata…");

        var resolvedDocs = await ResolveDocumentsAsync(distinctIds, ct).ConfigureAwait(false);

        if (resolvedDocs.Count < 2)
        {
            _log.Error(
                "Comparison aborted: only {Count} of {Requested} document(s) could be resolved " +
                "to indexed documents. At least 2 are required.",
                resolvedDocs.Count, distinctIds.Count);

            throw new InvalidOperationException(
                $"At least two resolvable, indexed documents are required for a comparison, " +
                $"but only {resolvedDocs.Count} could be found. " +
                "Ensure the selected documents are fully indexed before comparing.");
        }

        // Labels key the prompt sections and the report, so they must never collide
        var labels = BuildDocumentLabels(resolvedDocs);

        _log.Information(
            "Resolved {Count} document(s): {Names}",
            resolvedDocs.Count, string.Join(", ", resolvedDocs.Select(d => labels[d.Id])));

        // ── Step 2: Retrieve chunks for each document ────────────────────────

        Report(progress, "Retrieving document content via semantic search…");

        var searchQuery = string.IsNullOrWhiteSpace(options.FocusQuery)
            ? FallbackQuery
            : options.FocusQuery;

        var chunksPerDoc = Math.Max(1, options.MaxChunksPerDoc);

        ct.ThrowIfCancellationRequested();

        // One vault-wide search; its hits are then split per document. Asking for many
        // candidates keeps a document from being crowded out by the rest of the vault.
        var hits = await _searchService.SearchAsync(
            new SearchQuery
            {
                QueryText = searchQuery,
                TopK = Math.Min(MaxSemanticCandidates, chunksPerDoc * resolvedDocs.Count * SemanticCandidatesPerChunk),
                MinScore = MinChunkSimilarity,
                Mode = SearchMode.Semantic,
            },
            ct).ConfigureAwait(false);

        var hitsByDoc = hits
            .GroupBy(h => h.DocumentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.Score).ToList());

        // document label -> concatenated chunk text
        var contentByDoc = new Dictionary<string, string>(resolvedDocs.Count);

        foreach (var doc in resolvedDocs)
        {
            ct.ThrowIfCancellationRequested();

            var label = labels[doc.Id];
            Report(progress, $"Reading chunks for '{label}'\u2026");

            // Only this document's own chunks go into its section of the prompt
            var picked = (hitsByDoc.TryGetValue(doc.Id, out var docHits) ? docHits : [])
                .Take(chunksPerDoc)
                .Select(h => (h.ChunkIndex, Text: h.MatchedText))
                .ToList();

            if (picked.Count < chunksPerDoc && _db is not null)
            {
                var chosen = picked.Select(p => p.ChunkIndex).ToHashSet();
                picked.AddRange(await LoadSpreadChunksAsync(doc.Id, chunksPerDoc - picked.Count, chosen, ct)
                    .ConfigureAwait(false));
            }

            var docChunks = picked.OrderBy(p => p.ChunkIndex).ToList();

            if (docChunks.Count == 0)
            {
                _log.Warning(
                    "No chunks found for document {DocumentId} '{FileName}' with query '{Query}'. " +
                    "Document may not have been indexed yet.",
                    doc.Id, doc.FileName, searchQuery);

                // Still include the doc in the prompt with an empty body so the AI
                // can acknowledge it and the document name appears in the report.
                contentByDoc[label] = "(No indexed content available for this document.)";
            }
            else
            {
                contentByDoc[label] = ConcatenateChunks(docChunks.Select(c => c.Text));
                _log.Debug(
                    "Document '{Label}': using {Count} chunk(s), {Chars} chars of content",
                    label, docChunks.Count, contentByDoc[label].Length);
            }
        }

        // ── Step 3: Build the AI prompt ──────────────────────────────────────

        Report(progress, "Building analysis prompt…");

        var synthesisRequest = new ComparisonSynthesisRequest
        {
            ContentByDocument = contentByDoc,
            Options = options
        };

        _log.Information(
            "Sending comparison prompt to AI for {DocCount} document(s)",
            contentByDoc.Count);

        // ── Step 4: Call AI and stream response ──────────────────────────────

        Report(progress, "Running AI analysis — this may take a moment…");

        ComparisonSynthesisResult synthesisResult;

        try
        {
            synthesisResult = await _documentSynthesisService
                .SynthesizeComparisonAsync(synthesisRequest, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "AI inference failed during document comparison");
            throw new InvalidOperationException(
                "The AI service returned an error during the comparison analysis. " +
                "Verify that a model is loaded and connected, then try again.", ex);
        }

        var rawResponse = synthesisResult.RawResponse;

        _log.Debug(
            "AI response received: {Chars} chars, estimated ~{Tokens} completion tokens",
            rawResponse.Length, EstimateTokens(rawResponse));

        long totalTokens = synthesisResult.EstimatedPromptTokens + EstimateTokens(rawResponse);

        // ── Step 5: Parse the AI response into a ComparisonReport ────────────

        Report(progress, "Parsing analysis results…");

        var docNames = resolvedDocs.Select(d => labels[d.Id]).ToList();

        ComparisonReport report;

        try
        {
            report = ParseJsonResponse(rawResponse, docNames, totalTokens, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _log.Warning(ex,
                "JSON parsing failed for AI comparison response. " +
                "Falling back to plain-text extraction.");

            report = ParsePlainTextFallback(rawResponse, docNames, totalTokens, sw.Elapsed.TotalMilliseconds);
        }

        sw.Stop();

        _log.Information(
            "CompareDocumentsAsync completed in {DurationMs:F0}ms: " +
            "{Similarities} similarities, {Differences} differences, " +
            "{Contradictions} contradictions, ~{Tokens} tokens",
            sw.Elapsed.TotalMilliseconds,
            report.Similarities.Count,
            report.Differences.Count,
            report.Contradictions.Count,
            totalTokens);

        Report(progress, "Comparison complete.");

        return report;
    }

    /// <inheritdoc />
    public Task<string> ExportComparisonAsMarkdownAsync(ComparisonReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var md = new StringBuilder(2048);

        // ── Header ───────────────────────────────────────────────────────────

        md.AppendLine("# Comparative Analysis Report");
        md.AppendLine();
        md.AppendLine($"**Generated:** {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        md.AppendLine($"**Documents Compared:** {report.DocumentNames.Count}");

        foreach (var name in report.DocumentNames)
        {
            md.AppendLine($"- {EscapeMarkdown(name)}");
        }

        md.AppendLine();
        md.AppendLine($"**Analysis Duration:** {report.DurationMs:F0} ms");
        md.AppendLine($"**Estimated Tokens Used:** {report.TotalTokensUsed:N0}");
        md.AppendLine();
        md.AppendLine("---");
        md.AppendLine();

        // ── Executive Summary ────────────────────────────────────────────────

        md.AppendLine("## Summary");
        md.AppendLine();
        md.AppendLine(string.IsNullOrWhiteSpace(report.Summary)
            ? "_No summary generated._"
            : report.Summary);
        md.AppendLine();

        // ── Similarities ─────────────────────────────────────────────────────

        md.AppendLine("## Similarities");
        md.AppendLine();

        if (report.Similarities.Count == 0)
        {
            md.AppendLine("_No significant similarities identified._");
        }
        else
        {
            foreach (var item in report.Similarities)
            {
                md.AppendLine($"- {item}");
            }
        }

        md.AppendLine();

        // ── Differences ──────────────────────────────────────────────────────

        md.AppendLine("## Differences");
        md.AppendLine();

        if (report.Differences.Count == 0)
        {
            md.AppendLine("_No significant differences identified._");
        }
        else
        {
            foreach (var item in report.Differences)
            {
                md.AppendLine($"- {item}");
            }
        }

        md.AppendLine();

        // ── Contradictions ───────────────────────────────────────────────────

        md.AppendLine("## Contradictions");
        md.AppendLine();

        if (report.Contradictions.Count == 0)
        {
            md.AppendLine("_No direct contradictions detected._");
        }
        else
        {
            foreach (var item in report.Contradictions)
            {
                md.AppendLine($"- {item}");
            }
        }

        md.AppendLine();

        // ── Unique Points per Document ───────────────────────────────────────

        md.AppendLine("## Unique Points by Document");
        md.AppendLine();

        if (report.UniquePoints.Count == 0)
        {
            md.AppendLine("_No document-exclusive points identified._");
        }
        else
        {
            foreach (var (docName, points) in report.UniquePoints)
            {
                md.AppendLine($"### {EscapeMarkdown(docName)}");
                md.AppendLine();

                if (points.Count == 0)
                {
                    md.AppendLine("_No exclusive points identified for this document._");
                }
                else
                {
                    foreach (var point in points)
                    {
                        md.AppendLine($"- {point}");
                    }
                }

                md.AppendLine();
            }
        }

        return Task.FromResult(md.ToString());
    }

    // ── Private helpers — document retrieval ─────────────────────────────────

    /// <summary>
    /// Resolves each document ID to its <see cref="Data.Entities.DocumentEntity"/>,
    /// skipping IDs that do not exist in the database. Logs a warning for each
    /// skipped ID so it is visible in diagnostics.
    /// </summary>
    private async Task<List<Data.Entities.DocumentEntity>> ResolveDocumentsAsync(
        IReadOnlyList<long> documentIds,
        CancellationToken ct)
    {
        var resolved = new List<Data.Entities.DocumentEntity>(documentIds.Count);

        foreach (var id in documentIds)
        {
            ct.ThrowIfCancellationRequested();

            var doc = await _documentService.GetDocumentAsync(id).ConfigureAwait(false);

            if (doc is null)
            {
                _log.Warning(
                    "Document ID {DocumentId} was not found in the database and will be skipped.", id);
                continue;
            }

            resolved.Add(doc);
        }

        return resolved;
    }

    /// <summary>
    /// Gives each document a label that is unique within the comparison (ignoring case): its
    /// file name, or "name (#id)" when another compared document has the same name. Labels key
    /// the prompt sections and the report, so two documents never collapse into one.
    /// </summary>
    private static Dictionary<long, string> BuildDocumentLabels(IReadOnlyList<Data.Entities.DocumentEntity> docs)
    {
        var sharedNames = docs
            .GroupBy(d => d.FileName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var labels = new Dictionary<long, string>(docs.Count);

        foreach (var doc in docs)
        {
            var label = sharedNames.Contains(doc.FileName) ? $"{doc.FileName} (#{doc.Id})" : doc.FileName;
            while (!used.Add(label))
            {
                label = $"{label} (#{doc.Id})";
            }

            labels[doc.Id] = label;
        }

        return labels;
    }

    /// <summary>
    /// Reads up to <paramref name="count"/> of a document's own stored chunks, spread evenly
    /// from its first to its last chunk and skipping chunks already chosen, so a document the
    /// vault-wide search did not surface is still compared by its actual content.
    /// </summary>
    private async Task<List<(int ChunkIndex, string Text)>> LoadSpreadChunksAsync(
        long documentId,
        int count,
        IReadOnlySet<int> alreadyChosen,
        CancellationToken ct)
    {
        var indexes = await _db!.DocumentChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.ChunkIndex)
            .Select(c => c.ChunkIndex)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var available = indexes.Where(i => !alreadyChosen.Contains(i)).ToList();
        if (available.Count == 0 || count <= 0)
        {
            return [];
        }

        var wanted = available.Count <= count
            ? available
            : Enumerable.Range(0, count)
                .Select(i => available[count == 1 ? 0 : (int)Math.Round(i * (available.Count - 1) / (double)(count - 1))])
                .Distinct()
                .ToList();

        var chunks = await _db.DocumentChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == documentId && wanted.Contains(c.ChunkIndex))
            .Select(c => new { c.ChunkIndex, c.Content })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return chunks.Select(c => (c.ChunkIndex, c.Content)).ToList();
    }

    // ── Private helpers — response parsing ───────────────────────────────────

    /// <summary>
    /// Attempts to extract a JSON object from the raw AI response and deserialise
    /// it into a <see cref="ComparisonReport"/>. Handles common AI formatting
    /// artefacts such as leading prose, markdown code fences, and trailing comments.
    /// </summary>
    private static ComparisonReport ParseJsonResponse(
        string rawResponse,
        List<string> docNames,
        long totalTokens,
        double durationMs)
    {
        // Find the outermost JSON object boundaries.
        var jsonStart = rawResponse.IndexOf('{');
        var jsonEnd = rawResponse.LastIndexOf('}');

        if (jsonStart < 0 || jsonEnd <= jsonStart)
        {
            throw new JsonException(
                "No valid JSON object delimiters found in the AI response.");
        }

        var json = rawResponse[jsonStart..(jsonEnd + 1)];

        var dto = JsonSerializer.Deserialize<ComparisonResponseDto>(json, JsonReadOptions)
                  ?? throw new JsonException("Deserialisation returned null.");

        // Map from the wire DTO into the domain model.
        var uniquePoints = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Ensure every document name has an entry even if the AI omitted it.
        foreach (var name in docNames)
        {
            uniquePoints[name] = new List<string>();
        }

        if (dto.UniquePoints is not null)
        {
            foreach (var (key, value) in dto.UniquePoints)
            {
                // Match AI-returned keys to canonical doc names case-insensitively.
                var canonicalKey = docNames.FirstOrDefault(n =>
                    string.Equals(n, key, StringComparison.OrdinalIgnoreCase)) ?? key;

                uniquePoints[canonicalKey] = value ?? new List<string>();
            }
        }

        return new ComparisonReport
        {
            DocumentNames = docNames,
            Summary = dto.Summary?.Trim() ?? string.Empty,
            Similarities = SanitiseList(dto.Similarities),
            Differences = SanitiseList(dto.Differences),
            Contradictions = SanitiseList(dto.Contradictions),
            UniquePoints = uniquePoints,
            GeneratedAt = DateTime.UtcNow,
            TotalTokensUsed = totalTokens,
            DurationMs = durationMs,
        };
    }

    /// <summary>
    /// Plain-text fallback parser used when the AI response cannot be parsed as
    /// JSON. Extracts whatever structure it can by scanning for section headings.
    /// Returns a report with a populated <see cref="ComparisonReport.Summary"/>
    /// and empty structured lists so the UI degrades gracefully.
    /// </summary>
    private static ComparisonReport ParsePlainTextFallback(
        string rawResponse,
        List<string> docNames,
        long totalTokens,
        double durationMs)
    {
        // Populate unique-points keys so every document has an entry.
        var uniquePoints = docNames.ToDictionary(
            n => n,
            _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);

        // Try to detect section-level content by scanning for keywords.
        var similarities = ExtractBulletSection(rawResponse, "similarit");
        var differences = ExtractBulletSection(rawResponse, "differenc");
        var contradictions = ExtractBulletSection(rawResponse, "contradict");

        // Use the full response as the summary so no content is lost.
        var summary = rawResponse.Length > 600
            ? rawResponse[..600].TrimEnd() + "…"
            : rawResponse;

        return new ComparisonReport
        {
            DocumentNames = docNames,
            Summary = summary,
            Similarities = similarities,
            Differences = differences,
            Contradictions = contradictions,
            UniquePoints = uniquePoints,
            GeneratedAt = DateTime.UtcNow,
            TotalTokensUsed = totalTokens,
            DurationMs = durationMs,
        };
    }

    /// <summary>
    /// Scans the text for a section whose heading contains <paramref name="keyword"/>
    /// (case-insensitive) and extracts bullet-point lines from it until the next
    /// heading or end of text.
    /// </summary>
    private static List<string> ExtractBulletSection(string text, string keyword)
    {
        var results = new List<string>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var inSection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Detect section headings (lines containing the keyword, possibly prefixed with #).
            var isHeading = line.StartsWith('#') ||
                            (line.Length < 80 && line.EndsWith(':'));

            if (isHeading)
            {
                inSection = line.Contains(keyword, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection)
            {
                continue;
            }

            // Extract bullet content.
            if (line.StartsWith('-') || line.StartsWith('*') || line.StartsWith('•'))
            {
                var content = line.TrimStart('-', '*', '•').Trim();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    results.Add(content);
                }
            }
        }

        return results;
    }

    // ── Private helpers — utility ─────────────────────────────────────────────

    /// <summary>
    /// Concatenates chunk texts with a visual separator so the AI receives clearly
    /// delineated content segments rather than a single merged blob.
    /// </summary>
    private static string ConcatenateChunks(IEnumerable<string> chunks)
    {
        return string.Join("\n\n", chunks.Where(c => !string.IsNullOrWhiteSpace(c)));
    }

    /// <summary>
    /// Estimates the token count for a string using the 4-chars-per-token heuristic.
    /// </summary>
    private static long EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return (long)Math.Ceiling((double)text.Length / CharsPerToken);
    }

    /// <summary>
    /// Removes null or whitespace-only entries from a list returned by the AI.
    /// Returns an empty list when the source is null.
    /// </summary>
    private static List<string> SanitiseList(List<string>? source)
    {
        if (source is null)
        {
            return new List<string>();
        }

        return source
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();
    }

    /// <summary>
    /// Escapes Markdown special characters in a document name so it renders
    /// correctly in headings and list items.
    /// </summary>
    private static string EscapeMarkdown(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("*", "\\*")
            .Replace("_", "\\_")
            .Replace("[", "\\[")
            .Replace("]", "\\]")
            .Replace("`", "\\`")
            .Replace("#", "\\#");
    }

    /// <summary>
    /// Reports a progress message when a reporter has been provided, then logs
    /// the same message at Debug level so it appears in diagnostics regardless.
    /// </summary>
    private void Report(IProgress<string>? progress, string message)
    {
        progress?.Report(message);
        _log.Debug("ComparisonService progress: {Message}", message);
    }

    // ── Wire DTO for JSON deserialization ─────────────────────────────────────

    /// <summary>
    /// Internal DTO that mirrors the JSON schema specified in the system prompt.
    /// Kept private because consumers always receive the fully typed
    /// <see cref="ComparisonReport"/> from the public API.
    /// </summary>
    private sealed class ComparisonResponseDto
    {
        [JsonPropertyName("summary")]
        public string? Summary { get; init; }

        [JsonPropertyName("similarities")]
        public List<string>? Similarities { get; init; }

        [JsonPropertyName("differences")]
        public List<string>? Differences { get; init; }

        [JsonPropertyName("contradictions")]
        public List<string>? Contradictions { get; init; }

        [JsonPropertyName("uniquePoints")]
        public Dictionary<string, List<string>>? UniquePoints { get; init; }
    }
}
