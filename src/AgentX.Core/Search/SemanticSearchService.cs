using AgentX.Core.AI;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Data.VectorDb;
using AgentX.Core.Search.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Search;

/// <summary>
/// Production implementation of <see cref="ISemanticSearchService"/>.
/// Orchestrates the full semantic search pipeline: embed query, search vector store,
/// enrich results with EF Core metadata, apply filters, and build excerpts.
/// </summary>
public sealed class SemanticSearchService : ISemanticSearchService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly AgentXDbContext _db;
    private readonly ILogger _logger;

    /// <summary>
    /// Maximum character length for the generated excerpt text.
    /// </summary>
    private const int MaxExcerptLength = 200;

    public SemanticSearchService(
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        AgentXDbContext db,
        ILogger logger)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger?.ForContext<SemanticSearchService>() ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.QueryText))
        {
            _logger.Warning("SearchAsync called with empty query text; returning empty results");
            return Array.Empty<SearchResult>();
        }

        _logger.Information(
            "Semantic search started: Query={QueryText}, TopK={TopK}, MinScore={MinScore}, CollectionId={CollectionId}, FileType={FileType}",
            TruncateForLog(query.QueryText), query.TopK, query.MinScore, query.CollectionId, query.FileTypeFilter);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // ── Step 1: Generate embedding for the query text ───────────────
        float[] queryEmbedding;
        try
        {
            queryEmbedding = await _embeddingService.EmbedAsync(query.QueryText, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to generate embedding for search query");
            return Array.Empty<SearchResult>();
        }

        _logger.Debug("Embedding generated in {ElapsedMs}ms (dimensions={Dimensions})",
            stopwatch.ElapsedMilliseconds, queryEmbedding.Length);

        // ── Step 2: Vector similarity search ────────────────────────────
        // Request extra results to compensate for metadata-based filtering downstream.
        // We fetch up to 3x the requested TopK so that after collection/type/date filters
        // we still have a reasonable number of results to return.
        int vectorTopK = Math.Min(query.TopK * 3, 500);

        IReadOnlyList<VectorSearchResult> vectorResults;
        try
        {
            vectorResults = await _vectorStore.SearchAsync(
                queryEmbedding,
                topK: vectorTopK,
                minSimilarity: query.MinScore,
                ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Vector store search failed");
            return Array.Empty<SearchResult>();
        }

        if (vectorResults.Count == 0)
        {
            _logger.Information("Vector search returned 0 results for query");
            return Array.Empty<SearchResult>();
        }

        _logger.Debug("Vector search returned {Count} candidates in {ElapsedMs}ms",
            vectorResults.Count, stopwatch.ElapsedMilliseconds);

        // ── Step 3: Load chunk and document metadata from EF Core ───────
        var chunkIds = vectorResults.Select(v => v.ChunkId).ToList();

        // Build a lookup from ChunkId -> similarity score for fast access.
        var scoreByChunkId = vectorResults.ToDictionary(v => v.ChunkId, v => v.Similarity);

        // Load chunks with their parent documents in a single query.
        List<DocumentChunkEntity> chunks;
        try
        {
            chunks = await _db.DocumentChunks
                .AsNoTracking()
                .Include(c => c.Document)
                    .ThenInclude(d => d.DocumentCollections)
                        .ThenInclude(dc => dc.Collection)
                .Where(c => chunkIds.Contains(c.Id))
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load document chunks from database");
            return Array.Empty<SearchResult>();
        }

        if (chunks.Count == 0)
        {
            _logger.Warning("No matching chunks found in database for {Count} chunk IDs from vector search", chunkIds.Count);
            return Array.Empty<SearchResult>();
        }

        // ── Step 4: Apply metadata filters ──────────────────────────────
        IEnumerable<DocumentChunkEntity> filtered = chunks;

        // Filter by collection membership
        if (query.CollectionId.HasValue)
        {
            long collectionId = query.CollectionId.Value;
            filtered = filtered.Where(c =>
                c.Document.DocumentCollections.Any(dc => dc.CollectionId == collectionId));
        }

        // Filter by file type
        if (!string.IsNullOrWhiteSpace(query.FileTypeFilter))
        {
            string fileType = query.FileTypeFilter.Trim().ToLowerInvariant();
            filtered = filtered.Where(c =>
                string.Equals(c.Document.FileType, fileType, StringComparison.OrdinalIgnoreCase));
        }

        // Filter by creation date range (using ImportedAt as the creation date)
        if (query.CreatedAfter.HasValue)
        {
            DateTime after = query.CreatedAfter.Value;
            filtered = filtered.Where(c => c.Document.ImportedAt >= after);
        }

        if (query.CreatedBefore.HasValue)
        {
            DateTime before = query.CreatedBefore.Value;
            filtered = filtered.Where(c => c.Document.ImportedAt <= before);
        }

        // Materialize after all in-memory filters are applied.
        var filteredChunks = filtered.ToList();

        _logger.Debug("After metadata filtering: {FilteredCount} of {TotalCount} chunks remain",
            filteredChunks.Count, chunks.Count);

        // ── Step 5: Build search results with excerpts ──────────────────
        var queryWords = ExtractQueryWords(query.QueryText);

        var results = new List<SearchResult>(filteredChunks.Count);

        foreach (var chunk in filteredChunks)
        {
            if (!scoreByChunkId.TryGetValue(chunk.Id, out double similarity))
            {
                continue;
            }

            float score = (float)Math.Clamp(similarity, 0.0, 1.0);

            // Apply MinScore filter (vector store may return results at the boundary).
            if (score < query.MinScore)
            {
                continue;
            }

            var doc = chunk.Document;
            string excerpt = BuildExcerpt(chunk.Content, queryWords);
            var collectionNames = doc.DocumentCollections
                .Select(dc => dc.Collection.Name)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();

            results.Add(new SearchResult
            {
                ChunkId = chunk.Id,
                DocumentId = doc.Id,
                FileName = doc.FileName,
                FilePath = doc.FilePath,
                FileType = doc.FileType,
                PageNumber = chunk.PageNumber,
                ChunkIndex = chunk.ChunkIndex,
                MatchedText = chunk.Content,
                Excerpt = excerpt,
                Score = score,
                CollectionNames = collectionNames
            });
        }

        // ── Step 6: Sort by score descending and take TopK ──────────────
        var finalResults = results
            .OrderByDescending(r => r.Score)
            .Take(query.TopK)
            .ToList();

        stopwatch.Stop();

        _logger.Information(
            "Semantic search completed: {ResultCount} results returned in {ElapsedMs}ms for query \"{Query}\"",
            finalResults.Count, stopwatch.ElapsedMilliseconds, TruncateForLog(query.QueryText));

        return finalResults;
    }

    /// <inheritdoc />
    public async Task SaveSearchHistoryAsync(string queryText, int resultCount,
        double? minScore = null, int? maxResults = null,
        DateTime? dateAfter = null, DateTime? dateBefore = null,
        string? sortOrder = null)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return;
        }

        try
        {
            var entity = new SearchHistoryEntity
            {
                Query = queryText.Trim(),
                SearchType = "semantic",
                ResultCount = resultCount,
                SearchedAt = DateTime.UtcNow,
                IsSaved = false,
                MinScore = minScore,
                MaxResults = maxResults,
                DateAfter = dateAfter,
                DateBefore = dateBefore,
                SortOrder = sortOrder
            };

            _db.SearchHistory.Add(entity);
            await _db.SaveChangesAsync().ConfigureAwait(false);

            _logger.Debug("Search history saved: Query={Query}, ResultCount={ResultCount}",
                TruncateForLog(queryText), resultCount);
        }
        catch (Exception ex)
        {
            // Search history is non-critical; log but do not throw.
            _logger.Warning(ex, "Failed to save search history entry");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchHistoryEntry>> GetSearchHistoryAsync(int limit = 20)
    {
        if (limit <= 0)
        {
            return Array.Empty<SearchHistoryEntry>();
        }

        try
        {
            var entries = await _db.SearchHistory
                .AsNoTracking()
                .OrderByDescending(h => h.SearchedAt)
                .Take(limit)
                .Select(h => new SearchHistoryEntry
                {
                    Id = h.Id,
                    QueryText = h.Query,
                    ResultCount = h.ResultCount,
                    SearchedAt = h.SearchedAt,
                    IsSaved = h.IsSaved,
                    SearchType = h.SearchType,
                    CollectionFilter = h.CollectionFilter,
                    MinScore = h.MinScore,
                    MaxResults = h.MaxResults,
                    DateAfter = h.DateAfter,
                    DateBefore = h.DateBefore,
                    SortOrder = h.SortOrder
                })
                .ToListAsync()
                .ConfigureAwait(false);

            return entries;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to retrieve search history");
            return Array.Empty<SearchHistoryEntry>();
        }
    }

    /// <inheritdoc />
    public async Task ClearSearchHistoryAsync()
    {
        try
        {
            await _db.SearchHistory.ExecuteDeleteAsync().ConfigureAwait(false);
            _logger.Information("Search history cleared");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to clear search history");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SaveSearchFilterAsync(long historyId)
    {
        var entry = await _db.SearchHistory.FindAsync(historyId).ConfigureAwait(false);
        if (entry is not null)
        {
            entry.IsSaved = true;
            await _db.SaveChangesAsync().ConfigureAwait(false);
            _logger.Debug("Search filter saved: ID={Id}", historyId);
        }
    }

    /// <inheritdoc />
    public async Task UnsaveSearchFilterAsync(long historyId)
    {
        var entry = await _db.SearchHistory.FindAsync(historyId).ConfigureAwait(false);
        if (entry is not null)
        {
            entry.IsSaved = false;
            await _db.SaveChangesAsync().ConfigureAwait(false);
            _logger.Debug("Search filter unsaved: ID={Id}", historyId);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchHistoryEntry>> GetSavedFiltersAsync()
    {
        try
        {
            return await _db.SearchHistory
                .AsNoTracking()
                .Where(h => h.IsSaved)
                .OrderByDescending(h => h.SearchedAt)
                .Select(h => new SearchHistoryEntry
                {
                    Id = h.Id,
                    QueryText = h.Query,
                    ResultCount = h.ResultCount,
                    SearchedAt = h.SearchedAt,
                    IsSaved = h.IsSaved,
                    SearchType = h.SearchType,
                    CollectionFilter = h.CollectionFilter,
                    MinScore = h.MinScore,
                    MaxResults = h.MaxResults,
                    DateAfter = h.DateAfter,
                    DateBefore = h.DateBefore,
                    SortOrder = h.SortOrder
                })
                .ToListAsync()
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to retrieve saved filters");
            return Array.Empty<SearchHistoryEntry>();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Private helpers
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a concise excerpt from the chunk content, attempting to center
    /// on the portion most relevant to the query keywords. If no keyword match
    /// is found, falls back to the beginning of the text.
    /// </summary>
    private static string BuildExcerpt(string content, IReadOnlyList<string> queryWords)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        // Normalize whitespace for cleaner excerpts.
        string normalized = NormalizeWhitespace(content);

        if (normalized.Length <= MaxExcerptLength)
        {
            return normalized;
        }

        // Try to find the best position to center the excerpt around a query keyword match.
        int bestMatchIndex = FindBestMatchPosition(normalized, queryWords);

        if (bestMatchIndex >= 0)
        {
            return ExtractCenteredExcerpt(normalized, bestMatchIndex);
        }

        // Fallback: take the first MaxExcerptLength characters.
        return normalized[..MaxExcerptLength].TrimEnd() + "...";
    }

    /// <summary>
    /// Finds the character position of the best (first) keyword match in the text.
    /// Returns -1 if no query words are found.
    /// </summary>
    private static int FindBestMatchPosition(string text, IReadOnlyList<string> queryWords)
    {
        if (queryWords.Count == 0)
        {
            return -1;
        }

        // Prefer longer query words as they tend to be more semantically meaningful.
        // E.g., for "What is machine learning?", prefer matching "learning" over "is".
        var sortedWords = queryWords
            .OrderByDescending(w => w.Length)
            .ToList();

        foreach (string word in sortedWords)
        {
            int index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Extracts an excerpt of <see cref="MaxExcerptLength"/> characters centered
    /// on the given position, with word-boundary awareness and ellipsis indicators.
    /// </summary>
    private static string ExtractCenteredExcerpt(string text, int centerPosition)
    {
        // Calculate the window around the match.
        int halfWindow = MaxExcerptLength / 2;
        int start = Math.Max(0, centerPosition - halfWindow);
        int end = Math.Min(text.Length, start + MaxExcerptLength);

        // Adjust start if end hit the boundary to use available space.
        if (end - start < MaxExcerptLength)
        {
            start = Math.Max(0, end - MaxExcerptLength);
        }

        // Snap to word boundaries to avoid cutting words mid-stream.
        if (start > 0)
        {
            int wordBoundary = text.IndexOf(' ', start);
            if (wordBoundary >= 0 && wordBoundary < start + 30)
            {
                start = wordBoundary + 1;
            }
        }

        if (end < text.Length)
        {
            int wordBoundary = text.LastIndexOf(' ', end - 1, Math.Min(end, 30));
            if (wordBoundary > start)
            {
                end = wordBoundary;
            }
        }

        string excerpt = text[start..end].Trim();

        // Add ellipsis indicators.
        if (start > 0)
        {
            excerpt = "..." + excerpt;
        }

        if (end < text.Length)
        {
            excerpt += "...";
        }

        return excerpt;
    }

    /// <summary>
    /// Extracts meaningful words from the query text for keyword matching.
    /// Filters out very short words (likely stop words) and punctuation.
    /// </summary>
    private static IReadOnlyList<string> ExtractQueryWords(string queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return Array.Empty<string>();
        }

        // Split on whitespace and punctuation, keep words with 3+ characters
        // to skip common stop words like "a", "an", "is", "it", "to", etc.
        return queryText
            .Split(new[] { ' ', '\t', '\n', '\r', ',', '.', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '{', '}' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Collapses consecutive whitespace characters into single spaces
    /// and trims the result.
    /// </summary>
    private static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = new System.Text.StringBuilder(text.Length);
        bool previousWasWhitespace = false;

        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!previousWasWhitespace)
                {
                    result.Append(' ');
                    previousWasWhitespace = true;
                }
            }
            else
            {
                result.Append(c);
                previousWasWhitespace = false;
            }
        }

        return result.ToString().Trim();
    }

    /// <summary>
    /// Truncates a string for safe inclusion in log messages.
    /// </summary>
    private static string TruncateForLog(string text, int maxLength = 80)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= maxLength
            ? text
            : text[..maxLength] + "...";
    }
}
