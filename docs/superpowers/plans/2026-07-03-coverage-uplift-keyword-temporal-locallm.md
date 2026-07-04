# Coverage Uplift: KeywordSearchService / TemporalIdentityService / LocalLlmProvider — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the three remaining tracked AX-QA-009 coverage gaps — `KeywordSearchService` (0%), `TemporalIdentityService` (~partial, 4 existing tests), `LocalLlmProvider` (0%) — then ratchet the global coverage floor.

**Architecture:** Three independent test campaigns using the established AX-QA-009 harnesses: (1) real in-memory-SQLite EF context + real FTS5 virtual table for KeywordSearchService; (2) append behavioural tests to the existing partial `TemporalIdentityServiceTests` over the same EF harness, fixing any latent untranslatable-LINQ always-throws bugs that surface (the `GetAllMemoriesAsync` precedent); (3) two minimal `internal` test seams on `LocalLlmProvider` (the ComparisonService optional-seam precedent) so the streaming-chat and model-download pipelines are testable without a native GGUF model, plus a localhost `HttpListener` stub for the download path.

**Tech Stack:** .NET 8, xunit 2.9.2, FluentAssertions 6.12.2, Moq 4.20.72 (barely needed here), Serilog (real silent logger), Microsoft.Data.Sqlite (SQLCipher bundle), EF Core 8, LLamaSharp 0.19.0 (never actually loaded in tests), coverlet + `scripts/check-coverage.ps1` gate.

## Global Constraints

- **Every `dotnet` command needs `-p:Platform=x64`.** Bare `dotnet build` fails with a win-anycpu error. Canonical commands:
  - Build once: `dotnet build tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64`
  - Scoped test run: `dotnet test tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64 --no-build --filter "FullyQualifiedName~<TestClassName>"`
  - Full coverage run (Task 4 only): `dotnet test tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64 --no-build --collect "XPlat Code Coverage" --settings coverlet.runsettings --results-directory .cov-tmp`
  - Gate: `pwsh scripts/check-coverage.ps1 -ReportOnly -CoverageFile .cov-tmp`
- **Run all commands from the repo root:** `C:\Users\User\Desktop\Development Projects\Strategia-Enhanced-App\Agent-X`
- **Work happens directly on `main`** — this is the campaign convention (all six prior coverage rounds: 16fe0d6, 8866651, dcbe0f3, ddca2e4, 5489275, cdec382 are direct-to-main). Do not push until the final task passes the gate.
- **Coverage floors are a ratchet.** Never lower a floor. Raise a floor only with ≥ 0.5 pt headroom below the measured value. None of the three services is a trust boundary → **no new critical namespaces**; gains flow into the GLOBAL floor only.
- **The suite is currently 2696 tests, all green.** Every task must leave the full suite green. Existing tests in `TemporalIdentityServiceTests.cs` must not be modified or deleted.
- **Service code may only change in two sanctioned ways:** (a) the two `internal` test seams on `LocalLlmProvider` specified in Task 3, and (b) fixing latent always-throws EF-translation bugs in `TemporalIdentityService` per the exact conditional fixes in Task 2 — client-side evaluation that preserves the method's observable contract, following the `SemanticMemoryService.GetAllMemoriesAsync` precedent (commit 5489275). No other behaviour changes.
- **Commit style** (match `git log`): `test(core): cover KeywordSearchService 0% -> NN% line / NN% branch`. End every commit message with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- **House test style:** one behavioural test method per mechanism with rich multi-assert bodies (see `tests/AgentX.Tests/Search/SemanticSearchServiceTests.cs`); `IDisposable` test classes disposing their factories; a real silent Serilog logger (`new LoggerConfiguration().CreateLogger()`) wherever a ctor calls `logger.ForContext<T>()`.
- `AgentX.Core` already has `<InternalsVisibleTo Include="AgentX.Tests" />` — internal seams are directly accessible, no reflection needed for internals. Reflection **is** used for private statics (established pattern).

---

### Task 1: KeywordSearchService tests (FTS5 end-to-end harness)

**Files:**
- Create: `tests/AgentX.Tests/Search/KeywordSearchServiceTests.cs`
- Reference (read-only): `src/AgentX.Core/Search/KeywordSearchService.cs`, `src/AgentX.Core/Search/Models/` (SearchQuery/SearchResult), `tests/AgentX.Tests/Helpers/TestDbContextFactory.cs`

**Interfaces:**
- Consumes: `TestDbContextFactory` (in-memory SQLite, shared open connection — FTS virtual tables created on it persist for the factory lifetime), `KeywordSearchService(AgentXDbContext db, ILogger logger)`.
- Produces: nothing consumed by later tasks (independent).

**Design facts you need:**
- The service issues raw ADO.NET against `_db.Database.GetDbConnection()` — the same shared `SqliteConnection` the factory holds open, so EF-seeded rows and FTS rows coexist.
- FTS5 must be present in the `SQLitePCLRaw.bundle_e_sqlcipher` build. The very first test (`InitializeFtsAsync`) proves this. **If it fails with `no such module: fts5`, STOP the task and report** — that changes the whole approach and needs a human decision.
- BM25 `rank` is negative for matches; the service maps it to `score = 1/(1+|rank|)`, so scores are always in `(0, 1)`. A `MinScore` of `0.99f` filters everything.
- The `syntax error` catch arm in `SearchAsync` is unreachable through the public API (the sanitizer quotes every term) — that is an accepted residual; do not chase it.
- `RebuildFtsIndexAsync` selects documents with `IndexingStatus == "completed" && ChunkCount > 0` — the **entity's `ChunkCount` column**, so seeds must set it explicitly.

- [ ] **Step 1: Write the test file skeleton + harness + the FTS-availability test**

```csharp
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
}
```

Note the `SearchQuery` member names (`QueryText`, `TopK`, `MinScore`, `CollectionId`, `FileTypeFilter`, `CreatedAfter`, `CreatedBefore`) mirror the `Q()` helper in the sibling `SemanticSearchServiceTests.cs` — if compilation fails here, diff against that file, it is the source of truth.

- [ ] **Step 2: Build and run — the FTS5 probe must pass**

Run:
```
dotnet build tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64 --no-build --filter "FullyQualifiedName~KeywordSearchServiceTests"
```
Expected: 2 tests PASS. If `no such module: fts5` → STOP, report to the orchestrator.

- [ ] **Step 3: Add indexing + removal tests**

```csharp
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
```

- [ ] **Step 4: Run the new tests**

Run the same filtered command as Step 2. Expected: all PASS. If `Index_failure_mid_batch...` fails because a different exception type surfaces, assert on the actual concrete type the run shows (it must derive from `Exception` and not be `OperationCanceledException`) — the point is the catch-arm + rollback path executes.

- [ ] **Step 5: Add SearchAsync guard + pipeline tests**

```csharp
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
        r.Score.Should().BeGreaterThan(0f).And.BeLessThan(1f); // 1/(1+|bm25|)
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
```

- [ ] **Step 6: Run the new tests** — same filtered command. Expected: all PASS.

- [ ] **Step 7: Add excerpt-building + rebuild tests**

```csharp
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
```

- [ ] **Step 8: Run the full KeywordSearchServiceTests class** — all PASS expected. The `Rebuild_reindexes...` progress assertion uses `Progress<T>` (async posting) — if it flakes, switch it to `SynchronousProgress` like the other two rebuild tests; determinism beats fidelity to `Progress<T>` here.

- [ ] **Step 9: Run the WHOLE suite once (no coverage) to prove no cross-test damage**

Run: `dotnet test tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64 --no-build`
Expected: 2696 + ~24 new, 0 failures.

- [ ] **Step 10: Commit**

```bash
git add tests/AgentX.Tests/Search/KeywordSearchServiceTests.cs
git commit -m "test(core): cover KeywordSearchService — real FTS5 end-to-end harness (AX-QA-009)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```
(Exact percentages are not measured until Task 4; they land in the ratchet commit's narrative.)

---

### Task 2: TemporalIdentityService tests (append to existing partial file, fix latent EF-translation bugs)

**Files:**
- Modify: `tests/AgentX.Tests/Services/TemporalIdentityServiceTests.cs` (append only — the 4 existing tests are regression tests, do not touch them)
- Possibly modify (conditional fixes only): `src/AgentX.Core/Services/TemporalIdentity/TemporalIdentityService.cs`
- Reference (read-only): `src/AgentX.Core/Services/TemporalIdentity/Models/TemporalIdentityModels.cs`, `src/AgentX.Core/Data/Entities/{MessageEntity,ConversationEntity,AnnotationEntity,DocumentEntity}.cs`

**Interfaces:**
- Consumes: `TemporalIdentityService(AgentXDbContext db)` (no logger), `TestDbContextFactory`.
- Produces: possibly 2–4 service fixes (exact code below) that Task 4's narrative must mention.

**⚠ Latent-bug protocol (the `GetAllMemoriesAsync` precedent):** four methods contain LINQ that the SQLite EF provider likely cannot translate. For each, FIRST write and run the behavioural test. If the test passes — great, service untouched. If it throws `InvalidOperationException` ("could not be translated"), apply the exact fix below (client-side evaluation preserving the method's contract), then re-run. Never change expected test outcomes to accommodate a throw.

**Risk R1 — `GetRelatedConversationsAsync` (`(c.CreatedAt - around).TotalDays` in Where/OrderBy). Fix:**
```csharp
    private async Task<string[]> GetRelatedConversationsAsync(string topic, DateTime around, CancellationToken ct)
    {
        // DateTime subtraction is not translatable by the SQLite provider; materialise the
        // title matches, then apply the ±30-day window + proximity ordering in memory.
        var candidates = await _db.Conversations
            .Where(c => c.Title != null && c.Title.Contains(topic))
            .Select(c => new { c.Title, c.CreatedAt })
            .ToListAsync(ct);

        return candidates
            .Where(c => Math.Abs((c.CreatedAt - around).TotalDays) < 30)
            .OrderBy(c => Math.Abs((c.CreatedAt - around).TotalDays))
            .Take(3)
            .Select(c => c.Title ?? "")
            .ToArray();
    }
```

**Risk R2 — `GetRelatedDocumentsAsync` (same construct). Fix:**
```csharp
    private async Task<string[]> GetRelatedDocumentsAsync(string topic, DateTime around, CancellationToken ct)
    {
        var candidates = await _db.Documents
            .Where(d => d.FileName != null && d.FileName.Contains(topic))
            .Select(d => new { d.FileName, d.ImportedAt })
            .ToListAsync(ct);

        return candidates
            .Where(d => Math.Abs((d.ImportedAt - around).TotalDays) < 30)
            .Take(3)
            .Select(d => d.FileName ?? "")
            .ToArray();
    }
```

**Risk R3 — `GetActiveTopicsAsync` (`b.ConfidenceLevel * b.LastObservedAt.Ticks` in OrderByDescending). Fix:**
```csharp
    public async Task<List<string>> GetActiveTopicsAsync(
        int days = 30,
        CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-days);
        // Ticks-based weighting is not translatable by the SQLite provider; materialise the
        // recency window, then weight and rank in memory.
        var recent = await _db.Set<TemporalBeliefEntity>()
            .Where(b => b.LastObservedAt >= since)
            .ToListAsync(ct);

        return recent
            .OrderByDescending(b => b.ConfidenceLevel * b.LastObservedAt.Ticks)
            .Take(15)
            .Select(b => b.Topic)
            .ToList();
    }
```

**Risk R4 — `FindSimilarProblemsAsync` (`keywords.Any(k => c.Title.Contains(k))` over a local array — EF 8 may translate this via primitive collections; test decides). Fix if needed:**
```csharp
        var keywords = ExtractKeywords(currentProblem);

        // Correlated Contains over a local collection is not translatable on this provider;
        // materialise titled conversations and filter in memory.
        var titled = await _db.Conversations
            .Where(c => c.Title != null)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        var similarConversations = titled
            .Where(c => keywords.Any(k => c.Title!.Contains(k)))
            .Take(5)
            .ToList();
```
(keep the existing `return similarConversations.Select(...)` block unchanged.)

**Determinism cheat-sheet for the analyzers (used to compute expected values below):**
- `AnalyzeSentiment`: +0.2 per positive word present (`good great love excellent agree support believe`), −0.2 per negative (`bad hate terrible disagree oppose wrong problem`), clamp ±1. Substring match on lowercased content — one hit per distinct word.
- `ComputeConfidence`: base 0.8 if content contains definitely/certainly/absolutely else 0.5; + |sentiment|·0.3, cap 1.
- `ExtractTopics`: per sentence (split `.!?`), trimmed length must be **21–99**, must contain "I think"/"I believe"/"I feel", topic = substring after `" that "`, trimmed, length **4–49**, then `NormalizeTopic` (first char upper, rest lower).
- Belief update: EMA `0.7·old + 0.3·new`; evolution flagged when `|oldSentiment − newSentiment| > 0.5` (compared BEFORE the EMA update); confidence `min(1, old + 0.05)`.

- [ ] **Step 1: Append belief-tracking tests**

Append inside the existing class, below the last test and above `Dispose()`:

```csharp
    // ─── Seed helpers (append) ───────────────────────────────────────────────────

    private async Task<MessageEntity> SeedMessageAsync(
        AgentXDbContext db, string role, string content, string convTitle = "chat")
    {
        var conv = new ConversationEntity { Title = convTitle, CreatedAt = DateTime.UtcNow };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();
        var msg = new MessageEntity
        {
            ConversationId = conv.Id, Role = role, Content = content, Timestamp = DateTime.UtcNow,
        };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();
        return msg;
    }

    // ─── ProcessMessageAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessMessage_ignores_missing_and_non_user_messages()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);

        await svc.ProcessMessageAsync(424242);
        var assistant = await SeedMessageAsync(db, "assistant", "I believe that assistants have beliefs too.");
        await svc.ProcessMessageAsync(assistant.Id);

        (await db.Set<TemporalBeliefEntity>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessMessage_creates_belief_with_topic_sentiment_confidence_and_evidence()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);
        // Sentence 1 (33 chars, in 21..99): topic after " that " = "microservices rock"
        // Sentiment: "believe" +0.2, "great" +0.2 = 0.4. Confidence: 0.5 + 0.4*0.3 = 0.62.
        var msg = await SeedMessageAsync(db, "user",
            "I believe that microservices rock. Great stuff happens with them for sure.");

        await svc.ProcessMessageAsync(msg.Id);

        var belief = await db.Set<TemporalBeliefEntity>().SingleAsync();
        belief.Topic.Should().Be("Microservices rock");
        belief.SentimentScore.Should().BeApproximately(0.4, 0.001);
        belief.ConfidenceLevel.Should().BeApproximately(0.62, 0.001);
        belief.CurrentStance.Should().Be("I believe that microservices rock");
        belief.HasEvolved.Should().BeFalse();
        belief.FirstDetectedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        belief.EvidenceJson.Should().Contain("\"type\":\"message\"").And.Contain("microservices rock");
    }

    [Fact]
    public async Task ProcessMessage_large_sentiment_shift_flags_evolution_and_applies_ema()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);
        var first = await SeedMessageAsync(db, "user",
            "I believe that microservices rock. Great stuff happens with them for sure.");
        await svc.ProcessMessageAsync(first.Id); // sentiment 0.4

        // Same extracted topic; sentiment: believe +0.2, wrong -0.2, problem -0.2, bad -0.2 = -0.4.
        // Delta |0.4 - (-0.4)| = 0.8 > 0.5 -> evolution. EMA: 0.7*0.4 + 0.3*(-0.4) = 0.16.
        var second = await SeedMessageAsync(db, "user",
            "I believe that microservices rock. That was wrong, a problem, and bad news.");
        await svc.ProcessMessageAsync(second.Id);

        var belief = await db.Set<TemporalBeliefEntity>().SingleAsync();
        belief.HasEvolved.Should().BeTrue();
        belief.PreviousStance.Should().StartWith("0.40:");
        belief.StanceChangedAt.Should().NotBeNull();
        belief.SentimentScore.Should().BeApproximately(0.16, 0.001);
        belief.ConfidenceLevel.Should().BeApproximately(0.67, 0.001); // 0.62 + 0.05
        belief.LastObservedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ProcessMessage_small_sentiment_shift_updates_without_evolution()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);
        var first = await SeedMessageAsync(db, "user",
            "I believe that microservices rock. Great stuff happens with them for sure.");
        await svc.ProcessMessageAsync(first.Id);

        var repeat = await SeedMessageAsync(db, "user",
            "I believe that microservices rock. Great stuff happens with them for sure.");
        await svc.ProcessMessageAsync(repeat.Id); // identical sentiment -> delta 0

        var belief = await db.Set<TemporalBeliefEntity>().SingleAsync();
        belief.HasEvolved.Should().BeFalse();
        belief.PreviousStance.Should().BeNull();
    }
```

**Compile note:** the file's existing usings already include `AgentX.Core.Services.TemporalIdentity.Models` and `Microsoft.EntityFrameworkCore`; add `using AgentX.Core.Data;` and `using AgentX.Core.Data.Entities;` if not present.

- [ ] **Step 2: Run** — `dotnet build ... && dotnet test ... --filter "FullyQualifiedName~TemporalIdentityServiceTests"`. Expected: existing 4 + new 4 PASS. These paths have no translation risk.

- [ ] **Step 3: Append past-self + insight tests (translation risks R1/R2 fire here)**

```csharp
    // ─── GetPastSelfAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPastSelf_unknown_topic_returns_null()
    {
        using var db = _dbFactory.CreateContext();
        (await new TemporalIdentityService(db).GetPastSelfAsync("nothing")).Should().BeNull();
    }

    [Fact]
    public async Task GetPastSelf_returns_stance_evidence_and_time_windowed_related_items()
    {
        using var db = _dbFactory.CreateContext();
        var anchor = DateTime.UtcNow.AddMonths(-3);
        db.Set<TemporalBeliefEntity>().Add(new TemporalBeliefEntity
        {
            Topic = "remote work",
            FirstDetectedAt = anchor,
            CurrentStance = "remote work needs strong writing culture",
            ConfidenceLevel = 0.8,
            EvidenceJson = """[{"type":"message","id":1,"excerpt":"remote work excerpt"}]""",
        });
        db.Conversations.AddRange(
            new ConversationEntity { Title = "remote work rituals", CreatedAt = anchor.AddDays(5) },
            new ConversationEntity { Title = "remote work fatigue", CreatedAt = anchor.AddDays(200) }, // outside ±30d
            new ConversationEntity { Title = "unrelated", CreatedAt = anchor });
        db.Documents.AddRange(
            new DocumentEntity { FileName = "remote work handbook.pdf", ImportedAt = anchor.AddDays(-3) },
            new DocumentEntity { FileName = "remote work retro.pdf", ImportedAt = anchor.AddDays(120) }); // outside
        await db.SaveChangesAsync();

        var past = await new TemporalIdentityService(db).GetPastSelfAsync("remote work");

        past.Should().NotBeNull();
        past!.Topic.Should().Be("remote work");
        past.TimePeriod.Should().Be(anchor); // no `at` -> FirstDetectedAt
        past.Stance.Should().Be("remote work needs strong writing culture");
        past.Confidence.Should().Be(0.8);
        past.EvidenceExcerpts.Should().BeEquivalentTo("remote work excerpt");
        past.RelatedConversations.Should().BeEquivalentTo("remote work rituals");
        past.RelatedDocuments.Should().BeEquivalentTo("remote work handbook.pdf");
        past.HasEvolved.Should().BeFalse();
        past.CurrentStance.Should().BeNull(); // only exposed when evolved
    }

    [Fact]
    public async Task GetPastSelf_evolved_belief_exposes_current_stance_and_honors_explicit_time()
    {
        using var db = _dbFactory.CreateContext();
        db.Set<TemporalBeliefEntity>().Add(new TemporalBeliefEntity
        {
            Topic = "monoliths",
            FirstDetectedAt = DateTime.UtcNow.AddYears(-1),
            CurrentStance = "monoliths are fine at small scale",
            HasEvolved = true,
        });
        await db.SaveChangesAsync();
        var at = DateTime.UtcNow.AddMonths(-2);

        var past = await new TemporalIdentityService(db).GetPastSelfAsync("monoliths", at);

        past!.TimePeriod.Should().Be(at);
        past.HasEvolved.Should().BeTrue();
        past.CurrentStance.Should().Be("monoliths are fine at small scale");
    }

    // ─── Insights ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CaptureInsight_persists_a_high_significance_row()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);

        await svc.CaptureInsightAsync("caching", "Cache keys must encode tenant.", InsightSource.UserExplicitSave, 77);

        var row = await db.Set<InsightMomentEntity>().SingleAsync();
        row.Topic.Should().Be("caching");
        row.InsightText.Should().Be("Cache keys must encode tenant.");
        row.SignificanceScore.Should().Be(0.7);
        row.SourceType.Should().Be(InsightSource.UserExplicitSave);
        row.SourceId.Should().Be(77);
        row.RelatedTopicsJson.Should().Contain("caching");
    }

    [Fact]
    public async Task GetTopInsights_orders_by_significance_then_recency_and_caps()
    {
        using var db = _dbFactory.CreateContext();
        db.Set<InsightMomentEntity>().AddRange(
            new InsightMomentEntity { Topic = "a", InsightText = "older-high", SignificanceScore = 0.9, CapturedAt = DateTime.UtcNow.AddDays(-2) },
            new InsightMomentEntity { Topic = "b", InsightText = "newer-high", SignificanceScore = 0.9, CapturedAt = DateTime.UtcNow.AddDays(-1) },
            new InsightMomentEntity { Topic = "c", InsightText = "low", SignificanceScore = 0.5, CapturedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var top = await new TemporalIdentityService(db).GetTopInsightsAsync(count: 2);

        top.Should().HaveCount(2);
        top[0].InsightText.Should().Be("newer-high");
        top[1].InsightText.Should().Be("older-high");
    }

    [Fact]
    public async Task GetRelevantInsights_matches_by_topic_overlap_or_text_and_returns_top_five()
    {
        using var db = _dbFactory.CreateContext();
        db.Set<InsightMomentEntity>().AddRange(
            new InsightMomentEntity { Topic = "d", InsightText = "container insight", SignificanceScore = 0.9,
                CapturedAt = DateTime.UtcNow, RelatedTopicsJson = """["docker"]""" },                    // topic overlap (case-insensitive)
            new InsightMomentEntity { Topic = "k", InsightText = "we should docker-ise this", SignificanceScore = 0.8,
                CapturedAt = DateTime.UtcNow, RelatedTopicsJson = """["k8s"]""" },                       // text contains
            new InsightMomentEntity { Topic = "p", InsightText = "php memories", SignificanceScore = 0.9,
                CapturedAt = DateTime.UtcNow, RelatedTopicsJson = """["php"]""" },                       // no match
            new InsightMomentEntity { Topic = "weak", InsightText = "docker but insignificant", SignificanceScore = 0.4,
                CapturedAt = DateTime.UtcNow, RelatedTopicsJson = """["docker"]""" });                   // filtered: <= 0.5
        for (int i = 0; i < 6; i++)
        {
            db.Set<InsightMomentEntity>().Add(new InsightMomentEntity
            {
                Topic = $"extra{i}", InsightText = $"extra docker note {i}", SignificanceScore = 0.6 + i * 0.01,
                CapturedAt = DateTime.UtcNow, RelatedTopicsJson = """["docker"]""",
            });
        }
        await db.SaveChangesAsync();

        var relevant = await new TemporalIdentityService(db).GetRelevantInsightsAsync(new[] { "Docker" });

        relevant.Should().HaveCount(5); // 8 candidates match, capped at 5
        relevant.Select(r => r.Significance).Should().BeInDescendingOrder();
        relevant.Should().NotContain(r => r.Insight == "php memories");
        relevant.Should().NotContain(r => r.Insight == "docker but insignificant");
        relevant[0].RelevanceReason.Should().StartWith("Related to");
        relevant[0].Context.Should().Contain("From ");
    }
```

- [ ] **Step 4: Run — R1/R2 verdict**

Run the filtered command. `GetPastSelf_returns_stance_evidence...` is the R1/R2 probe:
- PASS → provider translated it; service stays untouched.
- FAIL with `InvalidOperationException` mentioning "could not be translated" → apply fixes **R1 and R2** exactly as specified in the task header, re-run, expect PASS.

- [ ] **Step 5: Append engagement + voice + pattern tests (risks R3/R4 fire here)**

```csharp
    // ─── Engagement ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordEngagement_creates_then_accumulates_and_upgrades_depth()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);

        await svc.RecordEngagementAsync(EngagementTargetType.Document, 5, 50);
        var created = await db.Set<EngagementMetricsEntity>().SingleAsync();
        created.Depth.Should().Be(EngagementDepth.Read);
        created.RevisitCount.Should().Be(0);
        created.TotalSecondsSpent.Should().Be(50);
        created.FirstEngagedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        await svc.RecordEngagementAsync(EngagementTargetType.Document, 5, 20); // 70s -> Engaged
        (await db.Set<EngagementMetricsEntity>().SingleAsync()).Depth.Should().Be(EngagementDepth.Engaged);

        await svc.RecordEngagementAsync(EngagementTargetType.Document, 5, 200); // 270s, revisit 2
        await svc.RecordEngagementAsync(EngagementTargetType.Document, 5, 100); // 370s, revisit 3 -> Deep
        var final = await db.Set<EngagementMetricsEntity>().SingleAsync();
        final.TotalSecondsSpent.Should().Be(370);
        final.RevisitCount.Should().Be(3);
        final.Depth.Should().Be(EngagementDepth.Deep);
    }

    [Fact]
    public async Task GetMostEngagedContent_filters_window_and_orders_by_time_weighted_depth()
    {
        using var db = _dbFactory.CreateContext();
        var now = DateTime.UtcNow;
        db.Set<EngagementMetricsEntity>().AddRange(
            new EngagementMetricsEntity { TargetType = EngagementTargetType.Document, TargetId = 1,
                LastEngagedAt = now.AddDays(-1), TotalSecondsSpent = 100, Depth = EngagementDepth.Deep },   // 100*3=300
            new EngagementMetricsEntity { TargetType = EngagementTargetType.Document, TargetId = 2,
                LastEngagedAt = now.AddDays(-2), TotalSecondsSpent = 200, Depth = EngagementDepth.Read },   // 200*1=200
            new EngagementMetricsEntity { TargetType = EngagementTargetType.Document, TargetId = 3,
                LastEngagedAt = now.AddDays(-40), TotalSecondsSpent = 9999, Depth = EngagementDepth.Core }); // outside window
        await db.SaveChangesAsync();

        var top = await new TemporalIdentityService(db)
            .GetMostEngagedContentAsync(now.AddDays(-7), now, count: 5);

        top.Select(e => e.TargetId).Should().Equal(1, 2);
    }

    [Fact]
    public async Task GetEngagedContentForTopic_filters_topics_json_client_side()
    {
        using var db = _dbFactory.CreateContext();
        db.Set<EngagementMetricsEntity>().AddRange(
            new EngagementMetricsEntity { TargetId = 1, TotalSecondsSpent = 300, TopicsJson = """["Docker","ci"]""" },
            new EngagementMetricsEntity { TargetId = 2, TotalSecondsSpent = 100, TopicsJson = """["docker"]""" },
            new EngagementMetricsEntity { TargetId = 3, TotalSecondsSpent = 900, TopicsJson = """["php"]""" });
        await db.SaveChangesAsync();

        var rows = await new TemporalIdentityService(db).GetEngagedContentForTopicAsync("docker");

        rows.Select(r => r.TargetId).Should().Equal(1, 2); // ordered by time desc, php excluded
    }

    // ─── Voice learning ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LearnFromMessage_skips_missing_and_non_user_messages()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);
        await svc.LearnFromMessageAsync(313131);
        var assistant = await SeedMessageAsync(db, "assistant", "However, this is formal.");
        await svc.LearnFromMessageAsync(assistant.Id);

        (await db.Set<VoiceProfileEntity>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task LearnFromMessage_creates_profile_and_applies_ema()
    {
        using var db = _dbFactory.CreateContext();
        var svc = new TemporalIdentityService(db);
        // One sentence, 6 words -> analysis.AvgSentenceLength 6; "however" -> formality 0.8.
        var msg = await SeedMessageAsync(db, "user", "However the plan needs revising now.");

        await svc.LearnFromMessageAsync(msg.Id);

        var profile = await db.Set<VoiceProfileEntity>().SingleAsync();
        profile.SampleCount.Should().Be(1);
        profile.AvgSentenceLength.Should().BeApproximately(15 * 0.9 + 6 * 0.1, 0.01);   // 14.1
        profile.FormalityScore.Should().BeApproximately(0.5 * 0.95 + 0.8 * 0.05, 0.001); // 0.515
        profile.LastSampleAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task GenerateAsUser_empty_context_asks_for_context()
    {
        using var db = _dbFactory.CreateContext();
        var draft = await new TemporalIdentityService(db).GenerateAsUserAsync("   ", "any goal");
        draft.Should().Be("Please provide context so I can draft something useful.");
    }

    [Fact]
    public async Task GenerateAsUser_informal_profile_uses_informal_opening()
    {
        using var db = _dbFactory.CreateContext();
        db.Set<VoiceProfileEntity>().Add(new VoiceProfileEntity
        {
            SampleCount = 8, FormalityScore = 0.2, AvgSentenceLength = 18,
            CharacteristicPhrasesJson = "[]", SentencePatternsJson = "[]", BookendsJson = "{}", StylisticTraitsJson = "{}",
        });
        await db.SaveChangesAsync();

        var draft = await new TemporalIdentityService(db).GenerateAsUserAsync("Ship the beta now", "unblock the pilot team");

        draft.Should().StartWith("Here is how I would frame it.");
        draft.Should().Contain("Ship the beta now.");
        draft.Should().Contain("The goal is to unblock the pilot team.");
    }

    [Fact]
    public async Task GenerateAsUser_mid_formality_short_sentences_caps_at_three_sentences_and_no_goal_line_without_goal()
    {
        using var db = _dbFactory.CreateContext();
        db.Set<VoiceProfileEntity>().Add(new VoiceProfileEntity
        {
            SampleCount = 8, FormalityScore = 0.5, AvgSentenceLength = 8, // <=10 -> 3 sentences
            CharacteristicPhrasesJson = "[]", SentencePatternsJson = "[]", BookendsJson = "{}", StylisticTraitsJson = "{}",
        });
        await db.SaveChangesAsync();

        var draft = await new TemporalIdentityService(db).GenerateAsUserAsync("Trim the scope", "  ");

        draft.Should().StartWith("I would keep this clear and grounded.");
        draft.Should().Contain("Trim the scope.");
        draft.Should().NotContain("The goal is to");
    }

    // ─── Pattern recognition ─────────────────────────────────────────────────────

    [Fact]
    public async Task FindSimilarProblems_maps_matching_titles_to_typed_patterns()
    {
        using var db = _dbFactory.CreateContext();
        db.Conversations.AddRange(
            new ConversationEntity { Title = "nightly build error triage", CreatedAt = DateTime.UtcNow.AddDays(-1), TokensUsed = 2000 },
            new ConversationEntity { Title = "how to deploy workers", CreatedAt = DateTime.UtcNow.AddDays(-2), TokensUsed = 500 },
            new ConversationEntity { Title = "best practices for retries", CreatedAt = DateTime.UtcNow.AddDays(-3), TokensUsed = 1500 },
            new ConversationEntity { Title = "random chatter", CreatedAt = DateTime.UtcNow, TokensUsed = 9000 });
        await db.SaveChangesAsync();

        // keywords (>4 chars, lowered): "error", "deploy", "retries" — titles are lowercase on purpose
        // because string.Contains translates case-sensitively.
        var patterns = await new TemporalIdentityService(db)
            .FindSimilarProblemsAsync("error deploy retries");

        patterns.Should().HaveCount(3);
        patterns.Select(p => p.ProblemType)
            .Should().BeEquivalentTo("Error Resolution", "How-To", "Optimization");
        patterns.Single(p => p.ProblemType == "Error Resolution").SuccessRate.Should().Be(0.8); // TokensUsed > 1000
        patterns.Single(p => p.ProblemType == "How-To").SuccessRate.Should().Be(0.5);
    }

    [Fact]
    public async Task GetExpertiseLevel_combines_conversation_count_and_engagement_hours()
    {
        using var db = _dbFactory.CreateContext();
        db.Conversations.AddRange(
            new ConversationEntity { Title = "docker networking", CreatedAt = DateTime.UtcNow },
            new ConversationEntity { Title = "docker compose tips", CreatedAt = DateTime.UtcNow });
        db.Set<EngagementMetricsEntity>().AddRange(
            new EngagementMetricsEntity { TargetId = 1, TotalSecondsSpent = 1000, TopicsJson = """["docker"]""" },
            new EngagementMetricsEntity { TargetId = 2, TotalSecondsSpent = 800, TopicsJson = """["docker"]""" });
        await db.SaveChangesAsync();

        var level = await new TemporalIdentityService(db).GetExpertiseLevelAsync("docker");

        level.Should().BeApproximately(2 * 0.1 + 1800 / 3600.0, 0.001); // 0.7
        (await new TemporalIdentityService(db).GetExpertiseLevelAsync("cobol")).Should().Be(0.0);
    }

    [Fact]
    public async Task GetActiveTopics_returns_recent_topics_weighted_by_confidence_and_recency()
    {
        using var db = _dbFactory.CreateContext();
        var now = DateTime.UtcNow;
        db.Set<TemporalBeliefEntity>().AddRange(
            new TemporalBeliefEntity { Topic = "strong-recent", LastObservedAt = now.AddDays(-1), ConfidenceLevel = 0.9 },
            new TemporalBeliefEntity { Topic = "weak-recent", LastObservedAt = now.AddDays(-1), ConfidenceLevel = 0.1 },
            new TemporalBeliefEntity { Topic = "stale", LastObservedAt = now.AddDays(-90), ConfidenceLevel = 1.0 });
        await db.SaveChangesAsync();

        var topics = await new TemporalIdentityService(db).GetActiveTopicsAsync(days: 30);

        topics.Should().Equal("strong-recent", "weak-recent"); // stale excluded, weighted order
    }
```

- [ ] **Step 6: Run — R3/R4 verdict**

`GetActiveTopics_...` probes R3; `FindSimilarProblems_...` probes R4. Apply the corresponding fix from the task header ONLY for the one(s) that throw the untranslatable `InvalidOperationException`, then re-run. Expected: all PASS.

- [ ] **Step 7: Append annotation + insight-detection tests**

```csharp
    // ─── Annotations & auto-detected insights ───────────────────────────────────

    private async Task<AnnotationEntity> SeedAnnotationAsync(
        AgentXDbContext db, string highlighted, string? note, string docName = "guide.pdf")
    {
        var doc = new DocumentEntity { FileName = docName, ImportedAt = DateTime.UtcNow };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        var ann = new AnnotationEntity
        {
            DocumentId = doc.Id, HighlightedText = highlighted, NoteText = note,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Annotations.Add(ann);
        await db.SaveChangesAsync();
        return ann;
    }

    [Fact]
    public async Task ProcessAnnotation_missing_id_is_a_noop()
    {
        using var db = _dbFactory.CreateContext();
        await new TemporalIdentityService(db).ProcessAnnotationAsync(515151);
        (await db.Set<InsightMomentEntity>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ProcessAnnotation_prefers_note_text_for_topic_and_insight()
    {
        using var db = _dbFactory.CreateContext();
        var ann = await SeedAnnotationAsync(db, "highlighted words", "Container orchestration simplifies deployments");

        await new TemporalIdentityService(db).ProcessAnnotationAsync(ann.Id);

        var insight = await db.Set<InsightMomentEntity>().SingleAsync();
        insight.InsightText.Should().Be("Container orchestration simplifies deployments");
        insight.Topic.Should().Be("Container orchestration simplifies deployments");
        insight.SourceType.Should().Be(InsightSource.DocumentAnnotation);
        insight.SourceId.Should().Be(ann.Id);
    }

    [Fact]
    public async Task ProcessAnnotation_falls_back_to_highlighted_text_then_document_name()
    {
        using var db = _dbFactory.CreateContext();
        var highlightOnly = await SeedAnnotationAsync(db, "Latency budgets matter", note: null);
        var emptyBoth = await SeedAnnotationAsync(db, "", note: null, docName: "empty-ann.pdf");
        var svc = new TemporalIdentityService(db);

        await svc.ProcessAnnotationAsync(highlightOnly.Id);
        await svc.ProcessAnnotationAsync(emptyBoth.Id);

        var insights = await db.Set<InsightMomentEntity>().OrderBy(i => i.Id).ToListAsync();
        insights[0].InsightText.Should().Be("Latency budgets matter");
        insights[1].InsightText.Should().Be("Annotation on document: empty-ann.pdf");
    }

    [Fact]
    public async Task GetBeliefEvolution_returns_row_or_null()
    {
        using var db = _dbFactory.CreateContext();
        db.Set<TemporalBeliefEntity>().Add(new TemporalBeliefEntity { Topic = "graphql" });
        await db.SaveChangesAsync();
        var svc = new TemporalIdentityService(db);

        (await svc.GetBeliefEvolutionAsync("graphql"))!.Topic.Should().Be("graphql");
        (await svc.GetBeliefEvolutionAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task DetectInsights_captures_breakthrough_and_excitement_assistant_messages_only()
    {
        using var db = _dbFactory.CreateContext();
        var conv = new ConversationEntity { Title = "session", CreatedAt = DateTime.UtcNow };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();
        var longTail = new string('x', 520);
        db.Messages.AddRange(
            new MessageEntity { ConversationId = conv.Id, Role = "assistant", Timestamp = DateTime.UtcNow.AddMinutes(-3),
                Content = "This is the key insight about cache stampedes. " + longTail },   // breakthrough, >500 chars
            new MessageEntity { ConversationId = conv.Id, Role = "assistant", Timestamp = DateTime.UtcNow.AddMinutes(-2),
                Content = "The results look amazing overall." },                            // excitement
            new MessageEntity { ConversationId = conv.Id, Role = "assistant", Timestamp = DateTime.UtcNow.AddMinutes(-1),
                Content = "Routine summary of steps." },                                    // neither
            new MessageEntity { ConversationId = conv.Id, Role = "user", Timestamp = DateTime.UtcNow,
                Content = "What a breakthrough." });                                        // wrong role
        await db.SaveChangesAsync();

        await new TemporalIdentityService(db).DetectInsightsAsync(conv.Id);

        var insights = await db.Set<InsightMomentEntity>().OrderBy(i => i.Id).ToListAsync();
        insights.Should().HaveCount(2);
        insights[0].InsightText.Should().EndWith("...");          // truncated at 500
        insights[0].InsightText.Length.Should().Be(503);
        insights[0].SignificanceScore.Should().BeApproximately(0.8, 0.001); // 0.6 + 0.2 breakthrough
        insights[0].Topic.Should().Be("General Insight");         // no "I think that" pattern
        insights[1].SignificanceScore.Should().BeApproximately(0.7, 0.001); // 0.6 + 0.1 excitement
        insights.Should().OnlyContain(i => i.SourceType == InsightSource.ConversationMessage);
    }

    [Fact]
    public async Task GetVoiceProfile_returns_null_when_unlearned()
    {
        using var db = _dbFactory.CreateContext();
        (await new TemporalIdentityService(db).GetVoiceProfileAsync()).Should().BeNull();
    }
```

**Watch-out for `DetectInsights...`:** the first message contains "key insight" (breakthrough) **and** a "!"? It must NOT — re-check the literal: it contains no `!` and none of ` amazing/ incredible/ fascinating/ interesting`, so significance is exactly 0.8. The second contains " amazing" (leading space matters) → 0.7. If an assertion lands at 0.9, the content accidentally matched both marker sets — adjust the content, not the expectation, until the marker sets are disjoint.

- [ ] **Step 8: Run the whole TemporalIdentityServiceTests class** — expected: 4 existing + ~24 new PASS.

- [ ] **Step 9: Run the WHOLE suite (no coverage)** — 0 failures.

- [ ] **Step 10: Commit**

```bash
git add tests/AgentX.Tests/Services/TemporalIdentityServiceTests.cs
git add src/AgentX.Core/Services/TemporalIdentity/TemporalIdentityService.cs   # only if R-fixes applied
git commit -m "test(core): cover TemporalIdentityService — belief/insight/engagement/voice/pattern surface (AX-QA-009)

<if fixes applied, add:> Fixes latent always-throws EF-translation bugs in GetRelatedConversations/
GetRelatedDocuments/GetActiveTopics[/FindSimilarProblems] by materialising before untranslatable
DateTime arithmetic / Ticks weighting (SemanticMemoryService.GetAllMemoriesAsync precedent).

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: LocalLlmProvider — internal test seams + tests

**Files:**
- Modify: `src/AgentX.Core/AI/Providers/LocalLlmProvider.cs` (two internal seams, nothing else)
- Create: `tests/AgentX.Tests/AI/Providers/LocalLlmProviderTests.cs`
- Reference (read-only): `src/AgentX.Core/AI/Models/AiModel.cs` (ChatMessage/ModelDownloadProgress/AiModel), `src/AgentX.Core/AI/Models/ChatOptions.cs`

**Interfaces:**
- Consumes: `LocalLlmProvider(string modelsDirectory, string modelFileName, int contextSize, int gpuLayers, ILogger logger)`; `ChatOptions { MaxTokens(int, default 2048), Temperature(double, 0.7), TopP(double, 0.9), ResponseFormat(enum Text|JsonObject) }`; `ChatMessage { Role, Content }`.
- Produces: two `internal` seams on the provider (below) — the tests in this task are their only consumer.

**Hard rule: no test may trigger a real LLamaSharp native model load with a VALID file.** The only permitted native interaction is `LLamaWeights.LoadFromFileAsync` **failing** on a garbage file after the download test (llama.cpp rejects the GGUF magic managed-side; `LLamaSharp.Backend.Cpu` is present so no DllNotFound crash). Everything else goes through the seams or never reaches model loading.

- [ ] **Step 1: Add the two internal seams to `LocalLlmProvider.cs`**

(1) After the `private readonly SemaphoreSlim _inferenceLock = new(1, 1);` line, add:

```csharp
    /// <summary>
    /// Test seam (AX-QA-009): when set, replaces the StatelessExecutor-based inference stream so
    /// the chat pipeline (prompt formatting, inference lock, token accounting, truncation warning,
    /// cancellation) is unit-testable without loading a native GGUF model. Never set in production;
    /// follows the ComparisonService optional-collaborator-seam precedent.
    /// </summary>
    internal Func<string, InferenceParams, CancellationToken, IAsyncEnumerable<string>>? InferenceOverride { get; set; }

    /// <summary>
    /// Test seam (AX-QA-009): overrides the HuggingFace URL lookup in <see cref="PullModelAsync"/>
    /// so the download pipeline (streaming copy, progress, atomic .part move, failure cleanup) is
    /// testable against a localhost HTTP stub. Never set in production.
    /// </summary>
    internal Func<string, string?>? DownloadUrlResolver { get; set; }
```

(2) In `StreamChatAsync`, replace the block from `ThrowIfDisposed();` through the `await foreach` line with:

```csharp
        ThrowIfDisposed();
        if (InferenceOverride is null)
        {
            await EnsureModelLoadedAsync(ct).ConfigureAwait(false);
        }

        var prompt = FormatChatPrompt(messages, options?.ResponseFormat == ResponseFormat.JsonObject);
        var inferenceParams = BuildInferenceParams(options);

        // StatelessExecutor creates its own context per call — thread-safe. The override
        // substitutes the token source only; lock, accounting, and cancellation are unchanged.
        var tokenStream = InferenceOverride is not null
            ? InferenceOverride(prompt, inferenceParams, ct)
            : new StatelessExecutor(_weights!, _chatParams!).InferAsync(prompt, inferenceParams, ct);

        // Track emitted tokens so we can warn on MaxTokens truncation (P0-6).
        // LLamaSharp StatelessExecutor stops naturally on antiprompt or MaxTokens —
        // when token count equals MaxTokens, we likely hit the budget cap.
        int emittedTokens = 0;
        var maxTokens = inferenceParams.MaxTokens;

        await _inferenceLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await foreach (var token in tokenStream.ConfigureAwait(false))
```
(the loop body, `finally`, and the truncation warning below it stay byte-for-byte identical).

(3) In `PullModelAsync`, replace `var url = ResolveDownloadUrl(modelName);` with:

```csharp
        var url = DownloadUrlResolver is not null
            ? DownloadUrlResolver(modelName)
            : ResolveDownloadUrl(modelName);
```

- [ ] **Step 2: Build to prove the seams compile** — `dotnet build tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64`. Expected: success, 0 warnings introduced.

- [ ] **Step 3: Create the test file with harness + lifecycle/listing tests**

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using AgentX.Core.AI.Models;
using AgentX.Core.AI.Providers;
using FluentAssertions;
using LLama.Common;
using LLama.Sampling;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace AgentX.Tests.AI.Providers;

/// <summary>
/// Behavioural coverage for <see cref="LocalLlmProvider"/> — the LLamaSharp-backed offline
/// provider. Real native model loading is impossible in unit tests (needs a multi-GB GGUF), so
/// coverage splits three ways: (1) file-system paths (listing, delete, availability) run for
/// real against a temp models directory; (2) the streaming-chat pipeline runs through the
/// internal <c>InferenceOverride</c> seam with hand-rolled IAsyncEnumerable token streams
/// (established AX-QA-009 harness); (3) the download pipeline runs through the internal
/// <c>DownloadUrlResolver</c> seam against a localhost HttpListener stub (established
/// HttpListener-stub + bind-retry harness). The deliberate residual is LoadModelAsync's
/// success body and the real StatelessExecutor/embedder calls.
/// </summary>
public sealed class LocalLlmProviderTests : IDisposable
{
    private const string PrimaryModel = "llama-3.2-3b-instruct-q4_k_m.gguf";

    private readonly string _modelsDir =
        Path.Combine(Path.GetTempPath(), "agentx-llm-tests", Guid.NewGuid().ToString("N"));
    private readonly List<LocalLlmProvider> _providers = new();
    private readonly CollectingSink _sink = new();
    private readonly Logger _logger;

    public LocalLlmProviderTests()
    {
        _logger = new LoggerConfiguration().WriteTo.Sink(_sink).CreateLogger();
    }

    public void Dispose()
    {
        foreach (var p in _providers) p.Dispose();
        _logger.Dispose();
        try { if (Directory.Exists(_modelsDir)) Directory.Delete(_modelsDir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }

    private LocalLlmProvider NewProvider(
        string modelFileName = PrimaryModel, int contextSize = 2048, int gpuLayers = 0)
    {
        var p = new LocalLlmProvider(_modelsDir, modelFileName, contextSize, gpuLayers, _logger);
        _providers.Add(p);
        return p;
    }

    private string WriteModelFile(string name, int bytes = 64)
    {
        Directory.CreateDirectory(_modelsDir);
        var path = Path.Combine(_modelsDir, name);
        File.WriteAllBytes(path, Enumerable.Repeat((byte)0x42, bytes).ToArray());
        return path;
    }

    private static async IAsyncEnumerable<string> Tokens(
        [EnumeratorCancellation] CancellationToken ct = default, params string[] tokens)
    {
        foreach (var t in tokens)
        {
            await Task.Yield();
            yield return t;
        }
    }

    /// <summary>List-backed Serilog sink so warning-branch tests can assert on log events.</summary>
    private sealed class CollectingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }

    // ─── Construction & identity ─────────────────────────────────────────────────

    [Fact]
    public void Ctor_guards_null_arguments()
    {
        FluentActions.Invoking(() => new LocalLlmProvider(null!, PrimaryModel, 2048, 0, _logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("modelsDirectory");
        FluentActions.Invoking(() => new LocalLlmProvider(_modelsDir, null!, 2048, 0, _logger))
            .Should().Throw<ArgumentNullException>().WithParameterName("modelFileName");
        FluentActions.Invoking(() => new LocalLlmProvider(_modelsDir, PrimaryModel, 2048, 0, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Identity_and_initial_availability()
    {
        var p = NewProvider();
        p.ProviderId.Should().Be("local");
        p.DisplayName.Should().Be("Built-in LLM");
        p.IsAvailable.Should().BeFalse();
    }

    // ─── CheckConnectionAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task CheckConnection_missing_model_returns_false_without_loading()
    {
        var p = NewProvider();
        (await p.CheckConnectionAsync()).Should().BeFalse();
        p.IsAvailable.Should().BeFalse();
    }

    // ─── ListModelsAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ListModels_missing_directory_returns_empty()
    {
        (await NewProvider().ListModelsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ListModels_primary_model_carries_curated_metadata()
    {
        WriteModelFile(PrimaryModel, bytes: 128);
        var p = NewProvider(contextSize: 4096);

        var models = await p.ListModelsAsync();

        models.Should().HaveCount(1);
        var m = models[0];
        m.Id.Should().Be(PrimaryModel);
        m.Name.Should().Be("Llama 3.2 3B Instruct (Q4_K_M)");
        m.ProviderId.Should().Be("local");
        m.Family.Should().Be("llama");
        m.IsAvailable.Should().BeTrue();
        m.SizeBytes.Should().Be(128);
        m.QuantizationLevel.Should().Be("Q4_K_M");
        m.ContextLength.Should().Be(4096);
    }

    [Fact]
    public async Task ListModels_lists_extra_ggufs_once_without_duplicating_primary()
    {
        WriteModelFile(PrimaryModel);
        WriteModelFile("other-model.gguf", bytes: 32);
        var p = NewProvider();

        var models = await p.ListModelsAsync();

        models.Should().HaveCount(2);
        models.Select(m => m.Id).Should().BeEquivalentTo(PrimaryModel, "other-model.gguf");
        models.Single(m => m.Id == "other-model.gguf").Family.Should().Be("gguf");
        models.Single(m => m.Id == "other-model.gguf").Name.Should().Be("other-model");
    }

    // ─── DeleteModelAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_removes_inactive_model_file()
    {
        var path = WriteModelFile("stale.gguf");
        await NewProvider().DeleteModelAsync("stale.gguf");
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_active_model_unloads_and_removes()
    {
        var path = WriteModelFile(PrimaryModel);
        var p = NewProvider();

        await p.DeleteModelAsync(PrimaryModel);

        File.Exists(path).Should().BeFalse();
        p.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_missing_file_is_a_noop()
    {
        await NewProvider().DeleteModelAsync("never-existed.gguf"); // must not throw
    }
}
```

- [ ] **Step 4: Run** — filtered on `LocalLlmProviderTests`. Expected: all PASS.

- [ ] **Step 5: Add download-pipeline tests (HttpListener stub) + URL resolution**

```csharp
    // ─── PullModelAsync / download pipeline ──────────────────────────────────────

    [Fact]
    public async Task Pull_unknown_model_without_url_is_a_noop()
    {
        var p = NewProvider();
        await p.PullModelAsync("unknown-model.gguf");
        Directory.EnumerateFiles(_modelsDir).Should().BeEmpty();
    }

    [Fact]
    public void ResolveDownloadUrl_maps_known_models_and_rejects_unknown()
    {
        var method = typeof(LocalLlmProvider).GetMethod(
            "ResolveDownloadUrl", BindingFlags.NonPublic | BindingFlags.Static)!;
        string? Invoke(string name) => (string?)method.Invoke(null, new object[] { name });

        Invoke("llama-3.2-3b-instruct-q4_k_m.gguf").Should()
            .Be("https://huggingface.co/hugging-quants/Llama-3.2-3B-Instruct-Q4_K_M-GGUF/resolve/main/llama-3.2-3b-instruct-q4_k_m.gguf");
        Invoke("LLAMA-3.2-1B-INSTRUCT-Q4_K_M.GGUF").Should()
            .Be("https://huggingface.co/hugging-quants/Llama-3.2-1B-Instruct-Q4_K_M-GGUF/resolve/main/llama-3.2-1b-instruct-q4_k_m.gguf");
        Invoke("mystery.gguf").Should().BeNull();
    }

    /// <summary>Starts a localhost HttpListener on a free port (established bind-retry harness
    /// for the free-port TOCTOU flake) and serves exactly one request via the handler.</summary>
    private static (HttpListener Listener, string Url, Task Served) StartStub(
        Action<HttpListenerContext> handler)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            var port = GetFreePort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            try { listener.Start(); }
            catch (HttpListenerException) { continue; }

            var served = Task.Run(async () =>
            {
                var ctx = await listener.GetContextAsync();
                try { handler(ctx); }
                finally { try { ctx.Response.Close(); } catch { /* aborted responses */ } }
            });
            return (listener, $"http://localhost:{port}/model.gguf", served);
        }
        throw new InvalidOperationException("Could not bind an HttpListener stub after 5 attempts.");
    }

    private static int GetFreePort()
    {
        var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    [Fact]
    public async Task Pull_downloads_streams_progress_and_moves_part_file_atomically()
    {
        var payload = Enumerable.Repeat((byte)7, 1024).ToArray();
        var (listener, url, served) = StartStub(ctx =>
        {
            ctx.Response.ContentLength64 = payload.Length;
            ctx.Response.OutputStream.Write(payload);
        });
        using var _ = listener;

        var p = NewProvider();
        p.DownloadUrlResolver = _ => url;
        var reports = new ConcurrentQueue<ModelDownloadProgress>();
        var progress = new SynchronousProgress<ModelDownloadProgress>(reports.Enqueue);

        // The download itself must succeed; the trailing LoadModelAsync then fails on the
        // garbage GGUF (llama.cpp rejects the magic managed-side). That throw is expected
        // and is exactly the LoadModelAsync catch-arm we want covered.
        await FluentActions.Awaiting(() => p.PullModelAsync("target.gguf", progress))
            .Should().ThrowAsync<Exception>();

        var target = Path.Combine(_modelsDir, "target.gguf");
        File.Exists(target).Should().BeTrue();
        new FileInfo(target).Length.Should().Be(1024);
        File.Exists(target + ".part").Should().BeFalse();
        reports.Should().Contain(r => r.Status == "Complete" && r.CompletedBytes == 1024 && r.TotalBytes == 1024);
        await served.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Pull_server_error_throws_and_leaves_no_partial()
    {
        var (listener, url, served) = StartStub(ctx => ctx.Response.StatusCode = 500);
        using var _ = listener;

        var p = NewProvider();
        p.DownloadUrlResolver = _ => url;

        await FluentActions.Awaiting(() => p.PullModelAsync("errored.gguf"))
            .Should().ThrowAsync<HttpRequestException>();

        Directory.EnumerateFiles(_modelsDir).Should().BeEmpty();
        await served.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Pull_aborted_mid_stream_cleans_partial_and_rethrows()
    {
        var (listener, url, served) = StartStub(ctx =>
        {
            ctx.Response.ContentLength64 = 4096;               // promise more than we send
            ctx.Response.OutputStream.Write(new byte[512]);
            ctx.Response.OutputStream.Flush();
            ctx.Response.Abort();                              // hard-kill mid-body
        });
        using var _ = listener;

        var p = NewProvider();
        p.DownloadUrlResolver = _ => url;

        await FluentActions.Awaiting(() => p.PullModelAsync("aborted.gguf"))
            .Should().ThrowAsync<Exception>(); // HttpIOException/IOException depending on stack

        File.Exists(Path.Combine(_modelsDir, "aborted.gguf")).Should().BeFalse();
        File.Exists(Path.Combine(_modelsDir, "aborted.gguf.part")).Should().BeFalse();
        await served.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>Inline IProgress — Progress&lt;T&gt; posts asynchronously and loses reports.</summary>
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SynchronousProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
```

- [ ] **Step 6: Run** — filtered. Expected: all PASS. If `Pull_downloads_...` fails because `LoadModelAsync` did NOT throw (wildly unexpected — garbage magic), change the assertion to tolerate success but keep every file/progress assert; report this in the task summary.

- [ ] **Step 7: Add streaming-chat pipeline tests (InferenceOverride seam) + guards**

```csharp
    // ─── StreamChatAsync / ChatAsync via InferenceOverride ───────────────────────

    private static List<ChatMessage> Msgs(params (string Role, string Content)[] items)
        => items.Select(i => new ChatMessage { Role = i.Role, Content = i.Content }).ToList();

    [Fact]
    public async Task StreamChat_formats_llama3_prompt_and_yields_override_tokens()
    {
        var p = NewProvider();
        string? capturedPrompt = null;
        InferenceParams? capturedParams = null;
        p.InferenceOverride = (prompt, prms, ct) =>
        {
            capturedPrompt = prompt;
            capturedParams = prms;
            return Tokens(ct, "Hello", " world");
        };

        var output = new List<string>();
        await foreach (var t in p.StreamChatAsync(Msgs(
            ("System", "Be brief"), ("user", "Hi"), ("ASSISTANT", "Yo"), ("tool", "data"))))
        {
            output.Add(t);
        }

        string.Concat(output).Should().Be("Hello world");
        capturedPrompt.Should().StartWith("<|begin_of_text|><|start_header_id|>system<|end_header_id|>\n\nBe brief<|eot_id|>");
        capturedPrompt.Should().Contain("<|start_header_id|>user<|end_header_id|>\n\nHi<|eot_id|>");
        capturedPrompt.Should().Contain("<|start_header_id|>assistant<|end_header_id|>\n\nYo<|eot_id|>");
        capturedPrompt.Should().Contain("<|start_header_id|>user<|end_header_id|>\n\ndata<|eot_id|>"); // unknown role -> user
        capturedPrompt.Should().EndWith("<|start_header_id|>assistant<|end_header_id|>\n\n");
        capturedParams!.MaxTokens.Should().Be(2048); // defaults with null options
        capturedParams.AntiPrompts.Should().Contain(new[] { "<|eot_id|>", "<|end_of_text|>" });
    }

    [Fact]
    public async Task StreamChat_json_mode_injects_instruction_and_primes_brace()
    {
        var p = NewProvider();
        string? capturedPrompt = null;
        p.InferenceOverride = (prompt, _, ct) => { capturedPrompt = prompt; return Tokens(ct, "{}"); };

        await foreach (var _ in p.StreamChatAsync(
            Msgs(("user", "give json")), new ChatOptions { ResponseFormat = ResponseFormat.JsonObject })) { }

        capturedPrompt.Should().Contain("You MUST respond with valid JSON only.");
        capturedPrompt.Should().EndWith("<|start_header_id|>assistant<|end_header_id|>\n\n{");
    }

    [Fact]
    public async Task StreamChat_maps_chat_options_to_inference_params()
    {
        var p = NewProvider();
        InferenceParams? captured = null;
        p.InferenceOverride = (_, prms, ct) => { captured = prms; return Tokens(ct, "x"); };

        await foreach (var _ in p.StreamChatAsync(Msgs(("user", "hi")),
            new ChatOptions { MaxTokens = 64, Temperature = 0.2, TopP = 0.5 })) { }

        captured!.MaxTokens.Should().Be(64);
        var pipeline = captured.SamplingPipeline.Should().BeOfType<DefaultSamplingPipeline>().Subject;
        pipeline.Temperature.Should().BeApproximately(0.2f, 0.0001f);
        pipeline.TopP.Should().BeApproximately(0.5f, 0.0001f);
    }

    [Fact]
    public async Task StreamChat_warns_when_token_budget_exhausted()
    {
        var p = NewProvider();
        p.InferenceOverride = (_, _, ct) => Tokens(ct, "a", "b");

        await foreach (var _ in p.StreamChatAsync(Msgs(("user", "hi")),
            new ChatOptions { MaxTokens = 2 })) { }

        _sink.Events.Should().Contain(e =>
            e.Level == LogEventLevel.Warning &&
            e.MessageTemplate.Text.Contains("likely truncated"));
    }

    [Fact]
    public async Task StreamChat_stops_yielding_after_cancellation_and_releases_lock()
    {
        var p = NewProvider();
        using var cts = new CancellationTokenSource();
        p.InferenceOverride = (_, _, _) => CancelAfterFirst(cts);

        var received = new List<string>();
        await foreach (var t in p.StreamChatAsync(Msgs(("user", "hi")), null, cts.Token))
        {
            received.Add(t);
        }

        received.Should().Equal("a"); // "b" arrives after cancel and must not surface

        // Lock must have been released by the finally — a second call proceeds.
        p.InferenceOverride = (_, _, ct) => Tokens(ct, "again");
        (await p.ChatAsync(Msgs(("user", "hi")))).Should().Be("again");

        static async IAsyncEnumerable<string> CancelAfterFirst(CancellationTokenSource cts)
        {
            yield return "a";
            cts.Cancel();
            await Task.Yield();
            yield return "b";
        }
    }

    [Fact]
    public async Task Chat_concatenates_streamed_tokens()
    {
        var p = NewProvider();
        p.InferenceOverride = (_, _, ct) => Tokens(ct, "foo", "bar", "!");
        (await p.ChatAsync(Msgs(("user", "hi")))).Should().Be("foobar!");
    }

    // ─── Embeddings & model-load failure paths ───────────────────────────────────

    [Fact]
    public async Task Embeddings_without_model_file_throw_FileNotFound_and_mark_unavailable()
    {
        var p = NewProvider();

        await FluentActions.Awaiting(() => p.GenerateEmbeddingAsync("text", "model"))
            .Should().ThrowAsync<FileNotFoundException>();
        await FluentActions.Awaiting(() => p.GenerateEmbeddingsAsync(new[] { "a", "b" }, "model"))
            .Should().ThrowAsync<FileNotFoundException>();
        p.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task StreamChat_without_override_and_without_model_throws_FileNotFound()
    {
        var p = NewProvider();
        await FluentActions.Awaiting(async () =>
        {
            await foreach (var _ in p.StreamChatAsync(Msgs(("user", "hi")))) { }
        }).Should().ThrowAsync<FileNotFoundException>();
    }

    // ─── Dispose semantics ───────────────────────────────────────────────────────

    [Fact]
    public async Task Dispose_is_idempotent_and_guards_every_entry_point()
    {
        var p = NewProvider();
        p.Dispose();
        p.Dispose(); // idempotent

        await FluentActions.Awaiting(() => p.CheckConnectionAsync())
            .Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => p.ListModelsAsync())
            .Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => p.PullModelAsync("x.gguf"))
            .Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => p.DeleteModelAsync("x.gguf"))
            .Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => p.GenerateEmbeddingAsync("t", "m"))
            .Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(async () =>
        {
            await foreach (var _ in p.StreamChatAsync(Msgs(("user", "hi")))) { }
        }).Should().ThrowAsync<ObjectDisposedException>();
    }

    // ─── GPU detection (environment-tolerant) ────────────────────────────────────

    [Fact]
    public void DetectRecommendedGpuLayers_returns_a_supported_tier()
    {
        var p = NewProvider();
        var method = typeof(LocalLlmProvider).GetMethod(
            "DetectRecommendedGpuLayers", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var layers = (int)method.Invoke(p, null)!;

        // Real WMI probe: 0 on CPU-only machines/CI, a fixed tier when an NVIDIA GPU exists.
        layers.Should().BeOneOf(0, 16, 28, 33);
    }
```

- [ ] **Step 8: Run the whole LocalLlmProviderTests class** — all PASS expected.

- [ ] **Step 9: Run the WHOLE suite (no coverage)** — 0 failures. (Watch for LLamaSharp native init side effects on other tests: there should be none, since only failure paths were touched.)

- [ ] **Step 10: Commit**

```bash
git add src/AgentX.Core/AI/Providers/LocalLlmProvider.cs tests/AgentX.Tests/AI/Providers/LocalLlmProviderTests.cs
git commit -m "test(core): cover LocalLlmProvider — internal inference/download seams + HttpListener stub (AX-QA-009)

Two internal test seams (ComparisonService optional-seam precedent): InferenceOverride replaces
the StatelessExecutor token stream; DownloadUrlResolver redirects PullModelAsync to a localhost
stub. Deliberate residual: LoadModelAsync success body + real executor/embedder calls (need a
multi-GB native GGUF).

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Full coverage run, floor ratchet, push

**Files:**
- Modify: `scripts/check-coverage.ps1` (floors + narrative comment)
- Reference: `.cov-tmp/**/coverage.cobertura.xml` (generated)

**Interfaces:**
- Consumes: the three committed test campaigns.
- Produces: the ratcheted `$Policy` block; the final green `git push`.

- [ ] **Step 1: Full coverage run**

```
Remove-Item -Recurse -Force .cov-tmp -ErrorAction SilentlyContinue
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64 --no-build --collect "XPlat Code Coverage" --settings coverlet.runsettings --results-directory .cov-tmp
```
Expected: ~2770+ tests, 0 failures.

- [ ] **Step 2: Read the gate report**

Run: `pwsh scripts/check-coverage.ps1 -ReportOnly -CoverageFile .cov-tmp`
Expected: GLOBAL measured strictly above 58.59 line / 48.68 branch (three ~0% services just gained coverage). Record: global line/branch, and per-class figures for the three services (from the cobertura XML `<class name="...KeywordSearchService">` etc. — `line-rate`/`branch-rate` attributes ×100).

**Also verify the watch-item:** `AgentX.Core.Services.Backup` line must still be ≥ 75 (it wobbles 75.9–77.4 as the suite grows). If it dipped below, STOP and report — do not touch the Backup floor.

- [ ] **Step 3: Ratchet the global floor**

In `scripts/check-coverage.ps1`, update:
```powershell
    Global = @{ Line = <L>; Branch = <B> }          # measured <ml> / <mb> (2026-07-03, KeywordSearch/TemporalIdentity/LocalLlm)
```
where `<L>` = highest integer ≤ (measured line − 0.5) and `<B>` = highest integer ≤ (measured branch − 0.5). If a floor cannot rise by ≥ 1 whole point with that headroom, HOLD it and say so in the comment (the ConversationBranchService precedent). None of the three namespaces becomes critical.

Prepend a narrative paragraph to the policy comment block, matching the established house format (date, service, previous %, what the service does, harness used, key seams/tricks, whether a latent bug was fixed, why not critical, old → new floors, measured values). Mention: the FTS5 end-to-end harness, any R1–R4 translation fixes applied, and the two LocalLlmProvider internal seams with their deliberate residual.

- [ ] **Step 4: Re-run the gate in ENFORCING mode**

Run: `pwsh scripts/check-coverage.ps1 -CoverageFile .cov-tmp`
Expected: `PASS: all coverage floors met.` with the NEW floors.

- [ ] **Step 5: Commit + push**

```bash
git add scripts/check-coverage.ps1
git commit -m "test(coverage): ratchet global floor to <L>/<B> after KeywordSearch/TemporalIdentity/LocalLlm round

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
git push origin main
```
Then watch CI (`gh run watch` or `gh run list --limit 1`) until the build-test workflow is green. If the coverage gate fails in CI due to run-to-run branch variance, the floor chosen in Step 3 had insufficient headroom — widen headroom by 1 point (still a raise vs. today) and push the correction; never lower below today's 58/48.

---

## Verification (whole plan)

1. Full suite green locally at `-c Release -p:Platform=x64`.
2. `scripts/check-coverage.ps1` (enforcing) passes with raised global floors.
3. CI build-test workflow green on `main`.
4. `git log` shows 4 commits in campaign style.
5. The three services report (from cobertura): KeywordSearchService ≥ ~90% line, TemporalIdentityService ≥ ~90% line, LocalLlmProvider ≥ ~75% line (residuals documented above).

## Post-execution note (orchestrator, not the repo)

Update the persistent memory file `agentx-coverage-uplift.md`: the three "next gaps" are closed; record the new floors, the FTS5 harness, the TemporalIdentity translation fixes (if applied), the LocalLlmProvider seam pattern, and identify the next-largest uncovered Core services from the fresh cobertura report as the new "next gaps".
