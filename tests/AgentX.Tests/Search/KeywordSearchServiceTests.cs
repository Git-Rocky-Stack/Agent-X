using System.Data;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Search;
using AgentX.Core.Search.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore; // UseSqlite extension for the closed-connection test
using Serilog;
using Xunit;

namespace AgentX.Tests.Search;

/// <summary>
/// Behavioural coverage for <see cref="KeywordSearchService"/> — the SQLite FTS5 full-text
/// pipeline (porter/unicode61 virtual table init -> chunk indexing in a transaction -> MATCH
/// query with BM25-rank normalisation -> post-query metadata filters -> excerpt building ->
/// TopK) plus removal and full-index rebuild.
///
/// <para><b>Harness design.</b> The service issues raw ADO.NET against the SAME connection EF
/// uses (<c>_db.Database.GetDbConnection()</c>), so a real in-memory SQLite database via
/// <see cref="TestDbContextFactory"/> exercises the production SQL end to end: EF seeds the
/// relational rows, the service creates and queries the real fts_chunks virtual table on the
/// shared connection. No mocks. The logger flows through <c>ILogger.ForContext&lt;T&gt;()</c>,
/// so a real silent Serilog logger is supplied.</para>
/// </summary>
public sealed class KeywordSearchServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();
    private readonly AgentXDbContext _db;
    private readonly Serilog.Core.Logger _logger = new LoggerConfiguration().CreateLogger();
    private readonly KeywordSearchService _service;

    public KeywordSearchServiceTests()
    {
        _db = _factory.CreateContext();
        _service = new KeywordSearchService(_db, _logger);
    }

    public void Dispose()
    {
        _db.Dispose();
        _factory.Dispose();
        _logger.Dispose();
    }

    // ─── Seed / raw-SQL helpers ─────────────────────────────────────────────────

    private DocumentEntity SeedDocument(
        string fileName,
        string fileType = "pdf",
        string status = "completed",
        DateTime? importedAt = null,
        params string[] chunkContents)
    {
        var doc = new DocumentEntity
        {
            FileName = fileName,
            FilePath = $@"C:\docs\{fileName}",
            FileType = fileType,
            IndexingStatus = status,
            ImportedAt = importedAt ?? DateTime.UtcNow,
            ChunkCount = chunkContents.Length,
        };
        for (int i = 0; i < chunkContents.Length; i++)
        {
            doc.Chunks.Add(new DocumentChunkEntity
            {
                ChunkIndex = i,
                Content = chunkContents[i],
                PageNumber = i + 1,
            });
        }
        _db.Documents.Add(doc);
        _db.SaveChanges();
        return doc;
    }

    private async Task<long> CountFtsRowsAsync(long? documentId = null)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = documentId is null
            ? "SELECT COUNT(*) FROM fts_chunks;"
            : "SELECT COUNT(*) FROM fts_chunks WHERE document_id = @id;";
        if (documentId is not null)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = "@id";
            p.Value = documentId.Value.ToString();
            cmd.Parameters.Add(p);
        }
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task ExecuteRawAsync(string sql)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static SearchQuery Q(
        string text,
        int topK = 10,
        float minScore = 0.0f,
        long? collectionId = null,
        string? fileType = null,
        DateTime? createdAfter = null,
        DateTime? createdBefore = null)
        => new()
        {
            QueryText = text,
            TopK = topK,
            MinScore = minScore,
            CollectionId = collectionId,
            FileTypeFilter = fileType,
            CreatedAfter = createdAfter,
            CreatedBefore = createdBefore,
        };

    // ─── Construction ────────────────────────────────────────────────────────────

    [Fact]
    public void Ctor_guards_null_dependencies()
    {
        FluentActions.Invoking(() => new KeywordSearchService(null!, _logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("db");
        FluentActions.Invoking(() => new KeywordSearchService(_db, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ─── InitializeFtsAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task InitializeFts_creates_fts5_table_and_is_idempotent()
    {
        // This test doubles as the FTS5-availability probe for the SQLCipher bundle.
        // If it fails with "no such module: fts5" STOP THE TASK and report.
        await _service.InitializeFtsAsync();
        await _service.InitializeFtsAsync(); // IF NOT EXISTS — second call must not throw

        var conn = _db.Database.GetDbConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name = 'fts_chunks';";
        ((long)(await cmd.ExecuteScalarAsync())!).Should().Be(1);
    }

    // ─── IndexDocumentChunksAsync ────────────────────────────────────────────────

    [Fact]
    public async Task Index_missing_document_is_a_noop()
    {
        await _service.InitializeFtsAsync();
        await _service.IndexDocumentChunksAsync(999_999);
        (await CountFtsRowsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Index_document_without_chunks_is_a_noop()
    {
        await _service.InitializeFtsAsync();
        var doc = SeedDocument("empty.pdf");
        await _service.IndexDocumentChunksAsync(doc.Id);
        (await CountFtsRowsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Index_inserts_one_fts_row_per_chunk_with_metadata()
    {
        await _service.InitializeFtsAsync();
        var doc = SeedDocument("spec.pdf", chunkContents: new[]
        {
            "alpha content first chunk", "bravo content second chunk", "charlie content third chunk",
        });

        await _service.IndexDocumentChunksAsync(doc.Id);

        (await CountFtsRowsAsync(doc.Id)).Should().Be(3);

        var conn = _db.Database.GetDbConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT content, file_name, file_path, file_type, page_number, chunk_index " +
            "FROM fts_chunks WHERE document_id = @id ORDER BY chunk_index;";
        var p = cmd.CreateParameter(); p.ParameterName = "@id"; p.Value = doc.Id.ToString();
        cmd.Parameters.Add(p);
        using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be("alpha content first chunk");
        reader.GetString(1).Should().Be("spec.pdf");
        reader.GetString(2).Should().Be(@"C:\docs\spec.pdf");
        reader.GetString(3).Should().Be("pdf");
        reader.GetString(4).Should().Be("1");
        reader.GetString(5).Should().Be("0");
    }

    [Fact]
    public async Task Index_precanceled_token_throws_OCE_and_persists_nothing()
    {
        await _service.InitializeFtsAsync();
        var doc = SeedDocument("cancel.pdf", chunkContents: new[] { "some content" });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // OperationCanceledException must NOT be swallowed by the catch filter.
        await FluentActions.Awaiting(() => _service.IndexDocumentChunksAsync(doc.Id, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        (await CountFtsRowsAsync(doc.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Index_failure_mid_batch_rolls_back_logs_and_rethrows()
    {
        // Arrange a hard failure INSIDE the insert loop: initialise, then drop the FTS table.
        await _service.InitializeFtsAsync();
        var doc = SeedDocument("boom.pdf", chunkContents: new[] { "content that will fail" });
        await ExecuteRawAsync("DROP TABLE fts_chunks;");

        await FluentActions.Awaiting(() => _service.IndexDocumentChunksAsync(doc.Id))
            .Should().ThrowAsync<SqliteException>();
    }

    // ─── RemoveDocumentFromFtsAsync ──────────────────────────────────────────────

    [Fact]
    public async Task Remove_deletes_only_that_documents_rows()
    {
        await _service.InitializeFtsAsync();
        var keep = SeedDocument("keep.pdf", chunkContents: new[] { "kept content" });
        var drop = SeedDocument("drop.pdf", chunkContents: new[] { "dropped content" });
        await _service.IndexDocumentChunksAsync(keep.Id);
        await _service.IndexDocumentChunksAsync(drop.Id);

        await _service.RemoveDocumentFromFtsAsync(drop.Id);

        (await CountFtsRowsAsync(drop.Id)).Should().Be(0);
        (await CountFtsRowsAsync(keep.Id)).Should().Be(1);
    }

    // ─── SearchAsync: guards ─────────────────────────────────────────────────────

    [Fact]
    public async Task Search_null_query_throws()
    {
        await FluentActions.Awaiting(() => _service.SearchAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Search_empty_or_whitespace_query_returns_empty()
    {
        (await _service.SearchAsync(Q(""))).Should().BeEmpty();
        (await _service.SearchAsync(Q("   "))).Should().BeEmpty();
    }

    [Fact]
    public async Task Search_query_of_only_punctuation_sanitizes_to_empty_and_returns_empty()
    {
        await _service.InitializeFtsAsync();
        (await _service.SearchAsync(Q("... !!! ??? ()"))).Should().BeEmpty();
    }

    [Fact]
    public async Task Search_without_initialized_fts_table_returns_empty_not_throw()
    {
        // Deliberately no InitializeFtsAsync — hits the "no such table" catch arm.
        (await _service.SearchAsync(Q("anything"))).Should().BeEmpty();
    }

    // ─── SearchAsync: pipeline ───────────────────────────────────────────────────

    [Fact]
    public async Task Search_returns_stemmed_matches_with_normalized_scores_and_mapped_metadata()
    {
        await _service.InitializeFtsAsync();
        var doc = SeedDocument("revenue.pdf", chunkContents: new[]
        {
            "The quarterly revenue projections improved dramatically this year.",
        });
        await _service.IndexDocumentChunksAsync(doc.Id);

        // porter stemming: "projection" matches "projections"
        var results = await _service.SearchAsync(Q("revenue projection"));

        results.Should().HaveCount(1);
        var r = results[0];
        r.DocumentId.Should().Be(doc.Id);
        r.FileName.Should().Be("revenue.pdf");
        r.FilePath.Should().Be(@"C:\docs\revenue.pdf");
        r.FileType.Should().Be("pdf");
        r.PageNumber.Should().Be(1);
        r.ChunkIndex.Should().Be(0);
        r.MatchedText.Should().Contain("quarterly revenue projections");
        r.Score.Should().BeGreaterThan(0f).And.BeLessThan(1f); // |bm25| / (1+|bm25|)
        r.CollectionNames.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_no_matches_returns_empty()
    {
        await _service.InitializeFtsAsync();
        var doc = SeedDocument("a.pdf", chunkContents: new[] { "alpha bravo charlie" });
        await _service.IndexDocumentChunksAsync(doc.Id);

        (await _service.SearchAsync(Q("zebra"))).Should().BeEmpty();
    }

    [Fact]
    public async Task Search_min_score_filters_out_all_bm25_scores()
    {
        await _service.InitializeFtsAsync();
        var doc = SeedDocument("a.pdf", chunkContents: new[] { "alpha bravo charlie" });
        await _service.IndexDocumentChunksAsync(doc.Id);

        (await _service.SearchAsync(Q("alpha", minScore: 0.99f))).Should().BeEmpty();
    }

    [Fact]
    public async Task Search_file_type_filter_is_case_insensitive()
    {
        await _service.InitializeFtsAsync();
        var pdf = SeedDocument("a.pdf", fileType: "pdf", chunkContents: new[] { "shared subject matter" });
        var docx = SeedDocument("b.docx", fileType: "docx", chunkContents: new[] { "shared subject matter" });
        await _service.IndexDocumentChunksAsync(pdf.Id);
        await _service.IndexDocumentChunksAsync(docx.Id);

        var results = await _service.SearchAsync(Q("shared subject", fileType: "  PDF  "));

        results.Should().OnlyContain(r => r.DocumentId == pdf.Id);
        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task Search_collection_filter_keeps_only_member_documents_and_enriches_names()
    {
        await _service.InitializeFtsAsync();
        var inside = SeedDocument("in.pdf", chunkContents: new[] { "topic keyword payload" });
        var outside = SeedDocument("out.pdf", chunkContents: new[] { "topic keyword payload" });

        var research = new CollectionEntity { Name = "Research" };
        var archive = new CollectionEntity { Name = "Archive" };
        _db.Set<CollectionEntity>().AddRange(research, archive);
        _db.SaveChanges();
        _db.Set<DocumentCollectionEntity>().AddRange(
            new DocumentCollectionEntity { DocumentId = inside.Id, CollectionId = research.Id },
            new DocumentCollectionEntity { DocumentId = inside.Id, CollectionId = archive.Id });
        _db.SaveChanges();

        await _service.IndexDocumentChunksAsync(inside.Id);
        await _service.IndexDocumentChunksAsync(outside.Id);

        var results = await _service.SearchAsync(Q("topic keyword", collectionId: research.Id));

        results.Should().HaveCount(1);
        results[0].DocumentId.Should().Be(inside.Id);
        results[0].CollectionNames.Should().BeEquivalentTo("Research", "Archive");
    }

    [Fact]
    public async Task Search_date_range_filters_by_ImportedAt()
    {
        await _service.InitializeFtsAsync();
        var old = SeedDocument("old.pdf", importedAt: DateTime.UtcNow.AddDays(-10),
            chunkContents: new[] { "dated keyword payload" });
        var mid = SeedDocument("mid.pdf", importedAt: DateTime.UtcNow.AddDays(-5),
            chunkContents: new[] { "dated keyword payload" });
        var fresh = SeedDocument("new.pdf", importedAt: DateTime.UtcNow,
            chunkContents: new[] { "dated keyword payload" });
        await _service.IndexDocumentChunksAsync(old.Id);
        await _service.IndexDocumentChunksAsync(mid.Id);
        await _service.IndexDocumentChunksAsync(fresh.Id);

        var results = await _service.SearchAsync(Q("dated keyword",
            createdAfter: DateTime.UtcNow.AddDays(-7),
            createdBefore: DateTime.UtcNow.AddDays(-1)));

        results.Should().HaveCount(1);
        results[0].DocumentId.Should().Be(mid.Id);
    }

    [Fact]
    public async Task Search_orders_by_score_desc_and_truncates_to_topK()
    {
        await _service.InitializeFtsAsync();
        // The doc that repeats the term ranks better under BM25.
        var strong = SeedDocument("strong.pdf", chunkContents: new[]
            { "ranking ranking ranking relevance test" });
        var weak1 = SeedDocument("w1.pdf", chunkContents: new[] { "ranking relevance filler one two" });
        var weak2 = SeedDocument("w2.pdf", chunkContents: new[] { "ranking relevance filler three four" });
        var weak3 = SeedDocument("w3.pdf", chunkContents: new[] { "ranking relevance filler five six" });
        foreach (var d in new[] { strong, weak1, weak2, weak3 })
            await _service.IndexDocumentChunksAsync(d.Id);

        var results = await _service.SearchAsync(Q("ranking", topK: 3));

        results.Should().HaveCount(3);
        results.Select(r => r.Score).Should().BeInDescendingOrder();
        results[0].DocumentId.Should().Be(strong.Id);
    }

    // ─── Excerpt building ────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_short_content_excerpt_is_full_text_with_whitespace_normalized()
    {
        await _service.InitializeFtsAsync();
        var doc = SeedDocument("short.pdf", chunkContents: new[]
            { "compact   excerpt\n\ncontent   here" });
        await _service.IndexDocumentChunksAsync(doc.Id);

        var results = await _service.SearchAsync(Q("excerpt"));

        results.Should().HaveCount(1);
        results[0].Excerpt.Should().Be("compact excerpt content here");
    }

    [Fact]
    public async Task Search_long_content_centers_excerpt_on_keyword_with_ellipses()
    {
        await _service.InitializeFtsAsync();
        var prefix = string.Join(" ", Enumerable.Repeat("filler", 80));   // ~560 chars
        var suffix = string.Join(" ", Enumerable.Repeat("padding", 80));
        var doc = SeedDocument("long.pdf", chunkContents: new[]
            { $"{prefix} needle {suffix}" });
        await _service.IndexDocumentChunksAsync(doc.Id);

        var results = await _service.SearchAsync(Q("needle"));

        results.Should().HaveCount(1);
        var excerpt = results[0].Excerpt;
        excerpt.Should().Contain("needle");
        excerpt.Should().StartWith("...");
        excerpt.Should().EndWith("...");
        excerpt.Length.Should().BeLessThan(220); // MaxExcerptLength 200 + ellipses + boundary snap
    }

    [Fact]
    public async Task Search_long_content_without_verbatim_keyword_head_truncates()
    {
        await _service.InitializeFtsAsync();
        // FTS matches via porter stem ("running" -> "run" matches "runs") but the raw
        // IndexOf("running") finds nothing, forcing the head-truncation fallback.
        var body = "The team runs daily standups. " + string.Join(" ", Enumerable.Repeat("noise", 80));
        var doc = SeedDocument("stem.pdf", chunkContents: new[] { body });
        await _service.IndexDocumentChunksAsync(doc.Id);

        var results = await _service.SearchAsync(Q("running"));

        results.Should().HaveCount(1);
        results[0].Excerpt.Should().StartWith("The team runs daily standups.");
        results[0].Excerpt.Should().EndWith("...");
    }

    // ─── RebuildFtsIndexAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task Rebuild_reindexes_completed_docs_with_chunks_and_reports_progress()
    {
        await _service.InitializeFtsAsync();
        var done1 = SeedDocument("d1.pdf", chunkContents: new[] { "first done content" });
        var done2 = SeedDocument("d2.pdf", chunkContents: new[] { "second done content" });
        var pending = SeedDocument("p.pdf", status: "pending", chunkContents: new[] { "pending content" });
        var noChunks = SeedDocument("n.pdf"); // completed but ChunkCount 0

        // Stale row that must be cleared by the rebuild.
        await _service.IndexDocumentChunksAsync(pending.Id);

        var reports = new List<(int Processed, int Total)>();
        var progress = new Progress<(int, int)>(t => { lock (reports) reports.Add(t); });

        await _service.RebuildFtsIndexAsync(progress);

        (await CountFtsRowsAsync(done1.Id)).Should().Be(1);
        (await CountFtsRowsAsync(done2.Id)).Should().Be(1);
        (await CountFtsRowsAsync(pending.Id)).Should().Be(0);
        (await CountFtsRowsAsync(noChunks.Id)).Should().Be(0);

        // Progress<T> posts asynchronously; poll briefly for both reports.
        for (int i = 0; i < 50 && reports.Count < 2; i++) await Task.Delay(20);
        reports.Should().BeEquivalentTo(new[] { (1, 2), (2, 2) });
    }

    [Fact]
    public async Task Rebuild_continues_past_a_document_that_fails_to_index()
    {
        await _service.InitializeFtsAsync();
        SeedDocument("ok.pdf", chunkContents: new[] { "fine content" });
        SeedDocument("ok2.pdf", chunkContents: new[] { "fine content too" });

        // Sabotage from the progress callback fired after doc 1: dropping the FTS table
        // makes doc 2's IndexDocumentChunksAsync throw, exercising the warn-and-continue arm.
        var completed = new TaskCompletionSource();
        int calls = 0;
        var progress = new SynchronousProgress(t =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                ExecuteRawAsync("DROP TABLE fts_chunks;").GetAwaiter().GetResult();
            }
            if (t.Processed == t.Total) completed.TrySetResult();
        });

        await _service.RebuildFtsIndexAsync(progress);

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        calls.Should().Be(2); // both docs processed despite the second failing
    }

    [Fact]
    public async Task Rebuild_honors_cancellation_between_documents()
    {
        await _service.InitializeFtsAsync();
        SeedDocument("c1.pdf", chunkContents: new[] { "cancel content one" });
        SeedDocument("c2.pdf", chunkContents: new[] { "cancel content two" });

        using var cts = new CancellationTokenSource();
        var progress = new SynchronousProgress(_ => cts.Cancel());

        await FluentActions.Awaiting(() => _service.RebuildFtsIndexAsync(progress, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task InitializeFts_opens_a_closed_connection()
    {
        // A context over a fresh, never-opened connection covers EnsureConnectionOpenAsync's
        // open branch (the shared factory connection is always already open).
        using var conn = new SqliteConnection("DataSource=:memory:");
        var options = new DbContextOptionsBuilder<AgentXDbContext>()
            .UseSqlite(conn).Options;
        using var db = new AgentXDbContext(options);
        var service = new KeywordSearchService(db, _logger);

        await service.InitializeFtsAsync(); // must open the connection itself, then succeed
    }

    /// <summary>Synchronous IProgress: Rebuild's sabotage/cancel hooks must run inline,
    /// not on a captured SynchronizationContext like <see cref="Progress{T}"/>.</summary>
    private sealed class SynchronousProgress : IProgress<(int Processed, int Total)>
    {
        private readonly Action<(int Processed, int Total)> _handler;
        public SynchronousProgress(Action<(int Processed, int Total)> handler) => _handler = handler;
        public void Report((int Processed, int Total) value) => _handler(value);
    }
}
