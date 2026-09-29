using System.Data;
using System.Data.Common;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Search.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Serilog;

namespace AgentX.Core.Search;

/// <summary>
/// Full-text keyword search implementation backed by SQLite FTS5.
/// Uses the Porter stemmer and Unicode61 tokenizer for broad language support.
/// BM25 ranks are reported on a 0-1 scale relative to the best hit of each search
/// (the best keyword match scores 1.0); <see cref="SearchQuery.MinScore"/> is applied
/// on that relative scale.
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

        using var gate = EnterRawSqlSection();
        var connection = _db.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection, ct).ConfigureAwait(false);

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

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

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

        using var gate = EnterRawSqlSection();
        var connection = _db.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection, ct).ConfigureAwait(false);

        // Use a transaction for batch insert consistency
        using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false) as SqliteTransaction;

        try
        {
            // Replace, never append: clear the document's existing rows inside the same
            // transaction so re-indexing (or retrying) a document cannot leave a second copy
            // of its text behind. Keyword hits carry the indexed text straight into search
            // results and RAG prompts, so a stale row is stale content. A document that now
            // has no chunks ends up with no rows at all.
            using (var deleteCmd = connection.CreateCommand())
            {
                deleteCmd.Transaction = transaction;
                deleteCmd.CommandText = "DELETE FROM fts_chunks WHERE document_id = @documentId;";
                deleteCmd.Parameters.Add(CreateParameter(deleteCmd, "@documentId", documentId.ToString()));
                await deleteCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

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

                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }

            _logger.Debug("Indexed {ChunkCount} chunks into FTS5 for document {DocumentId} ({FileName})",
                document.Chunks.Count, documentId, document.FileName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(ex, "Failed to index document {DocumentId} chunks into FTS5", documentId);

            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task RemoveDocumentFromFtsAsync(long documentId, CancellationToken ct = default)
    {
        _logger.Debug("Removing document {DocumentId} from FTS5 index", documentId);

        using var gate = EnterRawSqlSection();
        var connection = _db.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection, ct).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM fts_chunks WHERE document_id = @documentId;";
        cmd.Parameters.Add(CreateParameter(cmd, "@documentId", documentId.ToString()));

        var deleted = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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

        using var gate = EnterRawSqlSection();
        var connection = _db.Database.GetDbConnection();
        await EnsureConnectionOpenAsync(connection, ct).ConfigureAwait(false);

        // Sanitize the query text for FTS5 MATCH syntax.
        // Convert natural language to a valid FTS5 query by quoting individual terms.
        var ftsQuery = SanitizeFtsQuery(query.QueryText);

        if (string.IsNullOrWhiteSpace(ftsQuery))
        {
            _logger.Warning("FTS query sanitization produced empty query; returning empty results");
            return Array.Empty<SearchResult>();
        }

        using var cmd = connection.CreateCommand();

        // Collection, file-type and date filters are part of the query, not applied to its
        // top rows afterwards: filtering a LIMITed result set returned nothing whenever the
        // in-scope matches ranked below the cut. Table and column names are the ones mapped
        // in AgentXDbContext (documents, document_collections).
        var sql = new System.Text.StringBuilder(@"
            SELECT content, document_id, chunk_id, file_name, file_path, file_type,
                   page_number, chunk_index, rank
            FROM fts_chunks
            WHERE fts_chunks MATCH @query");

        if (!string.IsNullOrWhiteSpace(query.FileTypeFilter))
        {
            sql.Append(" AND lower(file_type) = @fileType");
            cmd.Parameters.Add(CreateParameter(cmd, "@fileType", query.FileTypeFilter.Trim().ToLowerInvariant()));
        }

        if (query.CollectionId.HasValue)
        {
            sql.Append(" AND CAST(document_id AS INTEGER) IN " +
                       "(SELECT DocumentId FROM document_collections WHERE CollectionId = @collectionId)");
            cmd.Parameters.Add(CreateParameter(cmd, "@collectionId", query.CollectionId.Value));
        }

        if (query.CreatedAfter.HasValue || query.CreatedBefore.HasValue)
        {
            sql.Append(" AND CAST(document_id AS INTEGER) IN (SELECT Id FROM documents WHERE 1 = 1");

            if (query.CreatedAfter.HasValue)
            {
                sql.Append(" AND ImportedAt >= @createdAfter");
                cmd.Parameters.Add(CreateParameter(cmd, "@createdAfter", query.CreatedAfter.Value));
            }

            if (query.CreatedBefore.HasValue)
            {
                sql.Append(" AND ImportedAt <= @createdBefore");
                cmd.Parameters.Add(CreateParameter(cmd, "@createdBefore", query.CreatedBefore.Value));
            }

            sql.Append(')');
        }

        sql.Append(@"
            ORDER BY rank
            LIMIT @topK;");

        cmd.CommandText = sql.ToString();
        cmd.Parameters.Add(CreateParameter(cmd, "@query", ftsQuery));
        cmd.Parameters.Add(CreateParameter(cmd, "@topK", Math.Max(1, query.TopK)));

        var rawResults = new List<FtsRawResult>();

        try
        {
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
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

        var filteredResults = rawResults;

        // BM25 ranks are negative (more negative = better match) and their magnitude depends
        // on the corpus and the query, so a fixed cut-off such as |rank| / (1 + |rank|) >= 0.3
        // discarded good matches on common terms. Scores are relative to the best hit in this
        // result set (1.0 = the best keyword match), and MinScore keeps hits at least that
        // fraction as strong as the best one.
        var bestRankMagnitude = rawResults.Max(r => Math.Abs(r.Rank));

        // Build query words for excerpt generation
        var queryWords = ExtractQueryWords(query.QueryText);

        // Load collection names for enrichment
        var allDocIds = filteredResults.Select(r => r.DocumentId).Distinct().ToList();
        var docCollections = await _db.DocumentCollections
            .AsNoTracking()
            .Include(dc => dc.Collection)
            .Where(dc => allDocIds.Contains(dc.DocumentId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

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
            float score = bestRankMagnitude > 0
                ? (float)Math.Clamp(Math.Abs(raw.Rank) / bestRankMagnitude, 0.0, 1.0)
                : 1f;

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

    // ===================================================================
    //  Private helpers
    // ===================================================================

    /// <summary>
    /// Holds the shared context's database gate for a raw ADO.NET section. Commands created
    /// from <c>Database.GetDbConnection()</c> are invisible to EF, so without the gate an EF
    /// operation from another flow could use the same connection at the same time, or issue a
    /// command while this service's transaction is open, which SQLite rejects because that
    /// command is not enlisted in the transaction. With it, EF work from other flows waits.
    /// Awaits inside the section use ConfigureAwait(false), so releasing the gate never needs
    /// the UI thread, which may itself be waiting for the gate.
    /// </summary>
    private ConcurrencyDetectorCriticalSectionDisposer EnterRawSqlSection() => _db.EnterDatabaseGate();

    /// <summary>
    /// Ensures the database connection is open. Required for raw ADO.NET operations.
    /// </summary>
    private static async Task EnsureConnectionOpenAsync(DbConnection connection, CancellationToken ct)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Creates a DbParameter with the given name and value.
    /// </summary>
    private static DbParameter CreateParameter(DbCommand cmd, string name, object value)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value;
        return param;
    }

    /// <summary>
    /// Common English function words dropped from keyword queries. Natural-language questions
    /// are largely made of them; requiring them made "What did the contract say about
    /// termination fees?" match nothing, because no passage contains every one.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "about", "above", "after", "again", "against", "all", "am", "an", "and", "any", "are", "as", "at",
        "be", "because", "been", "before", "being", "below", "between", "both", "but", "by",
        "can", "could", "did", "do", "does", "doing", "down", "during",
        "each", "few", "for", "from", "further",
        "had", "has", "have", "having", "he", "her", "here", "hers", "herself", "him", "himself", "his", "how",
        "i", "if", "in", "into", "is", "it", "its", "itself",
        "just", "me", "more", "most", "my", "myself",
        "no", "nor", "not", "now", "of", "off", "on", "once", "only", "or", "other", "our", "ours",
        "ourselves", "out", "over", "own",
        "same", "she", "should", "so", "some", "such",
        "than", "that", "the", "their", "theirs", "them", "themselves", "then", "there", "these", "they",
        "this", "those", "through", "to", "too",
        "under", "until", "up", "very",
        "was", "we", "were", "what", "when", "where", "which", "while", "who", "whom", "why", "will",
        "with", "would",
        "you", "your", "yours", "yourself", "yourselves"
    };

    private static readonly char[] QueryTermSeparators =
    {
        ' ', '\t', '\n', '\r', ',', '.', '!', '?', ';', ':', '(', ')', '[', ']', '{', '}', '"', '\''
    };

    /// <summary>
    /// Turns user input into an FTS5 MATCH expression. Every term is double-quoted (embedded
    /// quotes doubled), so operators (AND, OR, NOT, NEAR), prefix stars, column filters and
    /// punctuation in the input are matched as text, never interpreted. Stop words are dropped
    /// and the remaining terms are joined with OR: a passage matching any of them qualifies
    /// and BM25 ranks those containing more, and rarer, terms first. A query made only of stop
    /// words still requires all of its terms, so it matches something specific.
    /// </summary>
    internal static string SanitizeFtsQuery(string queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return string.Empty;
        }

        // Split on whitespace and punctuation; a term must contain a letter or digit, or the
        // tokenizer would reduce it to an empty phrase.
        var terms = queryText
            .Split(QueryTermSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Any(char.IsLetterOrDigit))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (terms.Count == 0)
        {
            return string.Empty;
        }

        var meaningful = terms.Where(t => !StopWords.Contains(t)).ToList();

        return meaningful.Count > 0
            ? string.Join(" OR ", meaningful.Select(QuoteFtsTerm))
            : string.Join(" ", terms.Select(QuoteFtsTerm)); // implicit AND
    }

    private static string QuoteFtsTerm(string term) => $"\"{term.Replace("\"", "\"\"")}\"";

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
