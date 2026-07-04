using System.Data;
using System.Data.Common;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Search.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Search;

/// <summary>
/// Full-text keyword search implementation backed by SQLite FTS5.
/// Uses the Porter stemmer and Unicode61 tokenizer for broad language support.
/// BM25 ranking scores are normalized to a 0-1 range for compatibility with
/// the semantic search scoring model.
/// </summary>
public sealed class KeywordSearchService : IKeywordSearchService
{
    private readonly AgentXDbContext _db;
    private readonly ILogger _logger;

    /// <summary>
    /// Maximum character length for the generated excerpt text.
    /// </summary>
    private const int MaxExcerptLength = 200;

    public KeywordSearchService(AgentXDbContext db, ILogger logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger?.ForContext<KeywordSearchService>() ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task InitializeFtsAsync(CancellationToken ct = default)
    {
        _logger.Information("Initializing FTS5 full-text search table");

        var connection = _db.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection, ct);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE VIRTUAL TABLE IF NOT EXISTS fts_chunks USING fts5(
                content,
                document_id UNINDEXED,
                chunk_id UNINDEXED,
                file_name UNINDEXED,
                file_path UNINDEXED,
                file_type UNINDEXED,
                page_number UNINDEXED,
                chunk_index UNINDEXED,
                tokenize='porter unicode61'
            );";

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.Information("FTS5 table fts_chunks initialized successfully");
    }

    /// <inheritdoc />
    public async Task IndexDocumentChunksAsync(long documentId, CancellationToken ct = default)
    {
        _logger.Debug("Indexing document {DocumentId} chunks into FTS5", documentId);

        // Load the document with its chunks
        var document = await _db.Documents
            .AsNoTracking()
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (document is null)
        {
            _logger.Warning("Document {DocumentId} not found; skipping FTS indexing", documentId);
            return;
        }

        if (document.Chunks.Count == 0)
        {
            _logger.Debug("Document {DocumentId} has no chunks; nothing to index in FTS", documentId);
            return;
        }

        var connection = _db.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection, ct);

        // Use a transaction for batch insert consistency
        using var transaction = await connection.BeginTransactionAsync(ct) as SqliteTransaction;

        try
        {
            foreach (var chunk in document.Chunks.OrderBy(c => c.ChunkIndex))
            {
                ct.ThrowIfCancellationRequested();

                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO fts_chunks (content, document_id, chunk_id, file_name, file_path, file_type, page_number, chunk_index)
                    VALUES (@content, @documentId, @chunkId, @fileName, @filePath, @fileType, @pageNumber, @chunkIndex);";

                cmd.Parameters.Add(CreateParameter(cmd, "@content", chunk.Content));
                cmd.Parameters.Add(CreateParameter(cmd, "@documentId", documentId.ToString()));
                cmd.Parameters.Add(CreateParameter(cmd, "@chunkId", chunk.Id.ToString()));
                cmd.Parameters.Add(CreateParameter(cmd, "@fileName", document.FileName));
                cmd.Parameters.Add(CreateParameter(cmd, "@filePath", document.FilePath));
                cmd.Parameters.Add(CreateParameter(cmd, "@fileType", document.FileType));
                cmd.Parameters.Add(CreateParameter(cmd, "@pageNumber", chunk.PageNumber?.ToString() ?? string.Empty));
                cmd.Parameters.Add(CreateParameter(cmd, "@chunkIndex", chunk.ChunkIndex.ToString()));

                await cmd.ExecuteNonQueryAsync(ct);
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(ct);
            }

            _logger.Debug("Indexed {ChunkCount} chunks into FTS5 for document {DocumentId} ({FileName})",
                document.Chunks.Count, documentId, document.FileName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(ex, "Failed to index document {DocumentId} chunks into FTS5", documentId);

            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task RemoveDocumentFromFtsAsync(long documentId, CancellationToken ct = default)
    {
        _logger.Debug("Removing document {DocumentId} from FTS5 index", documentId);

        var connection = _db.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection, ct);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM fts_chunks WHERE document_id = @documentId;";
        cmd.Parameters.Add(CreateParameter(cmd, "@documentId", documentId.ToString()));

        var deleted = await cmd.ExecuteNonQueryAsync(ct);
        _logger.Debug("Removed {Count} FTS5 entries for document {DocumentId}", deleted, documentId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.QueryText))
        {
            _logger.Warning("Keyword SearchAsync called with empty query text; returning empty results");
            return Array.Empty<SearchResult>();
        }

        _logger.Information(
            "Keyword search started: Query={QueryText}, TopK={TopK}, CollectionId={CollectionId}, FileType={FileType}",
            TruncateForLog(query.QueryText), query.TopK, query.CollectionId, query.FileTypeFilter);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var connection = _db.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection, ct);

        // Sanitize the query text for FTS5 MATCH syntax.
        // Convert natural language to a valid FTS5 query by quoting individual terms.
        var ftsQuery = SanitizeFtsQuery(query.QueryText);

        if (string.IsNullOrWhiteSpace(ftsQuery))
        {
            _logger.Warning("FTS query sanitization produced empty query; returning empty results");
            return Array.Empty<SearchResult>();
        }

        // Request extra results to compensate for post-query metadata filtering.
        int ftsTopK = Math.Min(query.TopK * 3, 500);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT content, document_id, chunk_id, file_name, file_path, file_type,
                   page_number, chunk_index, rank
            FROM fts_chunks
            WHERE fts_chunks MATCH @query
            ORDER BY rank
            LIMIT @topK;";

        cmd.Parameters.Add(CreateParameter(cmd, "@query", ftsQuery));
        cmd.Parameters.Add(CreateParameter(cmd, "@topK", ftsTopK.ToString()));

        var rawResults = new List<FtsRawResult>();

        try
        {
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rawResults.Add(new FtsRawResult
                {
                    Content = reader.GetString(0),
                    DocumentId = long.Parse(reader.GetString(1)),
                    ChunkId = long.Parse(reader.GetString(2)),
                    FileName = reader.GetString(3),
                    FilePath = reader.GetString(4),
                    FileType = reader.GetString(5),
                    PageNumber = ParseNullableInt(reader.GetString(6)),
                    ChunkIndex = int.Parse(reader.GetString(7)),
                    Rank = reader.GetDouble(8)
                });
            }
        }
        catch (SqliteException ex) when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning("FTS5 table does not exist; returning empty results. Initialize FTS first.");
            return Array.Empty<SearchResult>();
        }
        catch (SqliteException ex) when (ex.Message.Contains("syntax error", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning(ex, "FTS5 MATCH syntax error for query: {Query}. Returning empty results.", ftsQuery);
            return Array.Empty<SearchResult>();
        }

        if (rawResults.Count == 0)
        {
            _logger.Information("Keyword search returned 0 results for query");
            return Array.Empty<SearchResult>();
        }

        _logger.Debug("FTS5 returned {Count} raw results", rawResults.Count);

        // Apply post-query metadata filters
        IEnumerable<FtsRawResult> filtered = rawResults;

        // Filter by file type
        if (!string.IsNullOrWhiteSpace(query.FileTypeFilter))
        {
            string fileType = query.FileTypeFilter.Trim().ToLowerInvariant();
            filtered = filtered.Where(r =>
                string.Equals(r.FileType, fileType, StringComparison.OrdinalIgnoreCase));
        }

        // Filter by collection membership (requires a DB lookup)
        if (query.CollectionId.HasValue)
        {
            var collectionId = query.CollectionId.Value;
            var documentIdsInCollection = await _db.DocumentCollections
                .AsNoTracking()
                .Where(dc => dc.CollectionId == collectionId)
                .Select(dc => dc.DocumentId)
                .ToListAsync(ct);

            var docIdSet = new HashSet<long>(documentIdsInCollection);
            filtered = filtered.Where(r => docIdSet.Contains(r.DocumentId));
        }

        // Filter by date range
        if (query.CreatedAfter.HasValue || query.CreatedBefore.HasValue)
        {
            var docIds = filtered.Select(r => r.DocumentId).Distinct().ToList();
            var documentsQuery = _db.Documents.AsNoTracking().Where(d => docIds.Contains(d.Id));

            if (query.CreatedAfter.HasValue)
            {
                documentsQuery = documentsQuery.Where(d => d.ImportedAt >= query.CreatedAfter.Value);
            }

            if (query.CreatedBefore.HasValue)
            {
                documentsQuery = documentsQuery.Where(d => d.ImportedAt <= query.CreatedBefore.Value);
            }

            var validDocIds = new HashSet<long>(await documentsQuery.Select(d => d.Id).ToListAsync(ct));
            filtered = filtered.Where(r => validDocIds.Contains(r.DocumentId));
        }

        var filteredResults = filtered.ToList();

        // Build query words for excerpt generation
        var queryWords = ExtractQueryWords(query.QueryText);

        // Load collection names for enrichment
        var allDocIds = filteredResults.Select(r => r.DocumentId).Distinct().ToList();
        var docCollections = await _db.DocumentCollections
            .AsNoTracking()
            .Include(dc => dc.Collection)
            .Where(dc => allDocIds.Contains(dc.DocumentId))
            .ToListAsync(ct);

        var collectionsByDocId = docCollections
            .GroupBy(dc => dc.DocumentId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(dc => dc.Collection.Name)
                      .Where(name => !string.IsNullOrEmpty(name))
                      .ToList());

        // Convert to SearchResult objects
        var results = new List<SearchResult>(filteredResults.Count);

        foreach (var raw in filteredResults)
        {
            // Normalize BM25 rank to a 0-1 score.
            // BM25 returns negative values (more negative = better match), so |rank|
            // grows with match quality; |rank| / (1 + |rank|) maps it to 0-1 with
            // higher = better. (1 / (1 + |rank|) would invert relevance ordering.)
            float score = (float)(Math.Abs(raw.Rank) / (1.0 + Math.Abs(raw.Rank)));

            // Apply MinScore filter
            if (score < query.MinScore)
            {
                continue;
            }

            string excerpt = BuildExcerpt(raw.Content, queryWords);
            collectionsByDocId.TryGetValue(raw.DocumentId, out var collectionNames);

            results.Add(new SearchResult
            {
                ChunkId = raw.ChunkId,
                DocumentId = raw.DocumentId,
                FileName = raw.FileName,
                FilePath = raw.FilePath,
                FileType = raw.FileType,
                PageNumber = raw.PageNumber,
                ChunkIndex = raw.ChunkIndex,
                MatchedText = raw.Content,
                Excerpt = excerpt,
                Score = score,
                CollectionNames = collectionNames ?? new List<string>()
            });
        }

        // Sort by score descending and take TopK
        var finalResults = results
            .OrderByDescending(r => r.Score)
            .Take(query.TopK)
            .ToList();

        stopwatch.Stop();

        _logger.Information(
            "Keyword search completed: {ResultCount} results returned in {ElapsedMs}ms for query \"{Query}\"",
            finalResults.Count, stopwatch.ElapsedMilliseconds, TruncateForLog(query.QueryText));

        return finalResults;
    }

    /// <inheritdoc />
    public async Task RebuildFtsIndexAsync(IProgress<(int Processed, int Total)>? progress = null, CancellationToken ct = default)
    {
        _logger.Information("Starting FTS5 index rebuild");

        var connection = _db.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection, ct);

        // Clear existing FTS data
        using (var clearCmd = connection.CreateCommand())
        {
            clearCmd.CommandText = "DELETE FROM fts_chunks;";
            await clearCmd.ExecuteNonQueryAsync(ct);
        }

        _logger.Debug("Cleared existing FTS5 index data");

        // Get all documents that have chunks
        var documentIds = await _db.Documents
            .AsNoTracking()
            .Where(d => d.IndexingStatus == "completed" && d.ChunkCount > 0)
            .Select(d => d.Id)
            .ToListAsync(ct);

        var total = documentIds.Count;
        var processed = 0;

        _logger.Information("Rebuilding FTS5 index for {Total} documents", total);

        foreach (var docId in documentIds)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await IndexDocumentChunksAsync(docId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Warning(ex, "Failed to FTS-index document {DocumentId} during rebuild; continuing", docId);
            }

            processed++;
            progress?.Report((processed, total));
        }

        _logger.Information("FTS5 index rebuild completed: {Processed}/{Total} documents indexed", processed, total);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Private helpers
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ensures the database connection is open. Required for raw ADO.NET operations.
    /// </summary>
    private static async Task EnsureConnectionOpenAsync(DbConnection connection, CancellationToken ct)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }
    }

    /// <summary>
    /// Creates a DbParameter with the given name and value.
    /// </summary>
    private static DbParameter CreateParameter(DbCommand cmd, string name, string value)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value;
        return param;
    }

    /// <summary>
    /// Sanitizes user input for FTS5 MATCH syntax.
    /// Splits the input into individual terms and quotes each one to prevent
    /// FTS5 syntax errors from special characters (AND, OR, NOT, parentheses, etc.).
    /// </summary>
    private static string SanitizeFtsQuery(string queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return string.Empty;
        }

        // Split on whitespace and punctuation, keep meaningful terms
        var terms = queryText
            .Split(new[] { ' ', '\t', '\n', '\r', ',', '.', '!', '?', ';', ':', '(', ')', '[', ']', '{', '}', '"', '\'' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 1)
            .Select(t => t.Replace("\"", "\"\"")) // Escape double quotes within terms
            .Select(t => $"\"{t}\"")               // Quote each term
            .ToList();

        // Join with implicit AND (FTS5 default)
        return string.Join(" ", terms);
    }

    /// <summary>
    /// Parses a nullable integer from a string. Returns null for empty strings.
    /// </summary>
    private static int? ParseNullableInt(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, out var result) ? result : null;
    }

    /// <summary>
    /// Builds a concise excerpt from the chunk content, attempting to center
    /// on the portion most relevant to the query keywords.
    /// </summary>
    private static string BuildExcerpt(string content, IReadOnlyList<string> queryWords)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

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

        return normalized[..MaxExcerptLength].TrimEnd() + "...";
    }

    /// <summary>
    /// Finds the character position of the best (first) keyword match in the text.
    /// </summary>
    private static int FindBestMatchPosition(string text, IReadOnlyList<string> queryWords)
    {
        if (queryWords.Count == 0)
        {
            return -1;
        }

        var sortedWords = queryWords.OrderByDescending(w => w.Length).ToList();

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
    /// Extracts an excerpt centered on the given position with ellipsis indicators.
    /// </summary>
    private static string ExtractCenteredExcerpt(string text, int centerPosition)
    {
        int halfWindow = MaxExcerptLength / 2;
        int start = Math.Max(0, centerPosition - halfWindow);
        int end = Math.Min(text.Length, start + MaxExcerptLength);

        if (end - start < MaxExcerptLength)
        {
            start = Math.Max(0, end - MaxExcerptLength);
        }

        // Snap to word boundaries
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

        if (start > 0) excerpt = "..." + excerpt;
        if (end < text.Length) excerpt += "...";

        return excerpt;
    }

    /// <summary>
    /// Extracts meaningful words from the query text for keyword matching.
    /// </summary>
    private static IReadOnlyList<string> ExtractQueryWords(string queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return Array.Empty<string>();
        }

        return queryText
            .Split(new[] { ' ', '\t', '\n', '\r', ',', '.', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '{', '}' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Collapses consecutive whitespace characters into single spaces and trims.
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

        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    /// <summary>
    /// Internal DTO for raw FTS5 query results before mapping to SearchResult.
    /// </summary>
    private sealed class FtsRawResult
    {
        public string Content { get; init; } = string.Empty;
        public long DocumentId { get; init; }
        public long ChunkId { get; init; }
        public string FileName { get; init; } = string.Empty;
        public string FilePath { get; init; } = string.Empty;
        public string FileType { get; init; } = string.Empty;
        public int? PageNumber { get; init; }
        public int ChunkIndex { get; init; }
        public double Rank { get; init; }
    }
}
