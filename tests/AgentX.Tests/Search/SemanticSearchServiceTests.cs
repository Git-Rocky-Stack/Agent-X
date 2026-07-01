using AgentX.Core.AI;
using AgentX.Core.Configuration;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Data.VectorDb;
using AgentX.Core.Search;
using AgentX.Core.Search.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Search;

/// <summary>
/// Behavioural coverage for <see cref="SemanticSearchService"/> — the semantic-search pipeline that
/// embeds a query, runs a vector-similarity search, enriches the hits with EF-Core document metadata,
/// filters by embedding-model version + collection / file-type / date, builds display excerpts, and
/// sorts / truncates to TopK; plus the search-history and saved-filter CRUD surface.
///
/// <para><b>Harness design.</b> The service composes three mockable collaborators over a real
/// <see cref="AgentXDbContext"/> (in-memory SQLite via <see cref="TestDbContextFactory"/>):
/// <see cref="IEmbeddingService"/> (query embedding + <c>ModelVersion</c> for the compatibility gate),
/// <see cref="IVectorStore"/> (the ANN search), and an optional <see cref="IRagConfiguration"/>
/// (retrieval multiplier / cap; absent → built-in fallbacks 3 / 500). Vector hits are seeded by
/// <c>Distance</c> because <see cref="VectorSearchResult.Similarity"/> is the computed inverse
/// <c>1 - Distance</c>. The logger flows through <c>ILogger.ForContext&lt;T&gt;()</c>, so a real silent
/// Serilog logger is supplied. The version-compatibility gate compares a chunk's
/// <c>EmbeddingModelVersion</c> to the embedding service's <c>ModelVersion</c> (default
/// <c>"test-model:1.0"</c> here); null/empty chunk versions are treated as legacy-compatible.</para>
/// </summary>
public sealed class SemanticSearchServiceTests : IDisposable
{
    private const string ModelVersion = "test-model:1.0";

    private readonly List<Harness> _harnesses = new();

    private Harness NewHarness(bool withRag = false)
    {
        var h = new Harness(withRag);
        _harnesses.Add(h);
        return h;
    }

    public void Dispose()
    {
        foreach (var h in _harnesses)
        {
            h.Dispose();
        }
    }

    // ─── Harness ────────────────────────────────────────────────────────────────

    private sealed class Harness : IDisposable
    {
        public TestDbContextFactory Factory { get; } = new();
        public AgentXDbContext Db { get; }
        public Serilog.Core.Logger Logger { get; } = new LoggerConfiguration().CreateLogger();
        public Mock<IEmbeddingService> Embed { get; } = new();
        public Mock<IVectorStore> Vector { get; } = new();
        public Mock<IRagConfiguration> Rag { get; } = new();
        public SemanticSearchService Service { get; }

        public Harness(bool withRag)
        {
            Db = Factory.CreateContext();

            Embed.SetupGet(e => e.ModelVersion).Returns(ModelVersion);
            Embed.Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f, 0.4f });

            Vector.Setup(v => v.SearchAsync(
                    It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<VectorSearchResult>)Array.Empty<VectorSearchResult>());

            Service = withRag
                ? new SemanticSearchService(Embed.Object, Vector.Object, Db, Rag.Object, Logger)
                : new SemanticSearchService(Embed.Object, Vector.Object, Db, Logger);
        }

        public AgentXDbContext Fresh() => Factory.CreateContext();

        public void Seed(Action<AgentXDbContext> seed)
        {
            using var ctx = Factory.CreateContext();
            seed(ctx);
            ctx.SaveChanges();
        }

        public void SetVector(params VectorSearchResult[] results)
            => Vector.Setup(v => v.SearchAsync(
                    It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<VectorSearchResult>)results);

        public void Dispose()
        {
            Db.Dispose();
            Factory.Dispose();
            Logger.Dispose();
        }
    }

    // ─── Builders / seed helpers ──────────────────────────────────────────────────

    private static VectorSearchResult Vec(long chunkId, double similarity)
        => new() { ChunkId = chunkId, Distance = 1.0 - similarity };

    private static SearchQuery Q(
        string text,
        int topK = 10,
        float minScore = 0.3f,
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

    private static int _hashSeq;

    private static long SeedDoc(
        Harness h,
        string fileName = "doc.pdf",
        string fileType = "pdf",
        string filePath = "/docs/doc.pdf",
        DateTime? importedAt = null)
    {
        var now = importedAt ?? DateTime.UtcNow;
        var doc = new DocumentEntity
        {
            FileName = fileName,
            FilePath = filePath,
            FileType = fileType,
            ContentHash = "hash-" + Interlocked.Increment(ref _hashSeq),
            ImportedAt = now,
            FileModifiedAt = now,
            IndexingStatus = "completed",
        };
        h.Seed(ctx => ctx.Documents.Add(doc));
        return doc.Id;
    }

    private static long SeedChunk(
        Harness h,
        long docId,
        string content = "content",
        int chunkIndex = 0,
        int? pageNumber = null,
        string? modelVersion = ModelVersion)
    {
        var chunk = new DocumentChunkEntity
        {
            DocumentId = docId,
            Content = content,
            ChunkIndex = chunkIndex,
            PageNumber = pageNumber,
            EmbeddingModelVersion = modelVersion,
            IsEmbedded = true,
        };
        h.Seed(ctx => ctx.DocumentChunks.Add(chunk));
        return chunk.Id;
    }

    private static long SeedCollection(Harness h, string name)
    {
        var col = new CollectionEntity
        {
            Name = name,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        h.Seed(ctx => ctx.Collections.Add(col));
        return col.Id;
    }

    private static void Link(Harness h, long docId, long colId)
        => h.Seed(ctx => ctx.DocumentCollections.Add(new DocumentCollectionEntity
        {
            DocumentId = docId,
            CollectionId = colId,
            AddedAt = DateTime.UtcNow,
        }));

    private static long SeedHistory(
        Harness h,
        string query = "q",
        DateTime? searchedAt = null,
        bool isSaved = false,
        string searchType = "semantic",
        int resultCount = 0)
    {
        var e = new SearchHistoryEntity
        {
            Query = query,
            SearchType = searchType,
            SearchedAt = searchedAt ?? DateTime.UtcNow,
            IsSaved = isSaved,
            ResultCount = resultCount,
        };
        h.Seed(ctx => ctx.SearchHistory.Add(e));
        return e.Id;
    }

    private static CancellationToken Canceled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        return cts.Token;
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  Constructor guards
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Ctor_NullEmbeddingService_Throws()
    {
        using var f = new TestDbContextFactory();
        using var db = f.CreateContext();
        using var log = new LoggerConfiguration().CreateLogger();
        var act = () => new SemanticSearchService(null!, Mock.Of<IVectorStore>(), db, log);
        act.Should().Throw<ArgumentNullException>().WithParameterName("embeddingService");
    }

    [Fact]
    public void Ctor_NullVectorStore_Throws()
    {
        using var f = new TestDbContextFactory();
        using var db = f.CreateContext();
        using var log = new LoggerConfiguration().CreateLogger();
        var act = () => new SemanticSearchService(Mock.Of<IEmbeddingService>(), null!, db, log);
        act.Should().Throw<ArgumentNullException>().WithParameterName("vectorStore");
    }

    [Fact]
    public void Ctor_NullDb_Throws()
    {
        using var log = new LoggerConfiguration().CreateLogger();
        var act = () => new SemanticSearchService(
            Mock.Of<IEmbeddingService>(), Mock.Of<IVectorStore>(), null!, log);
        act.Should().Throw<ArgumentNullException>().WithParameterName("db");
    }

    [Fact]
    public void Ctor_NullLogger_Throws()
    {
        using var f = new TestDbContextFactory();
        using var db = f.CreateContext();
        var act = () => new SemanticSearchService(
            Mock.Of<IEmbeddingService>(), Mock.Of<IVectorStore>(), db, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Ctor_FiveArg_NullRagConfiguration_IsAllowed()
    {
        using var f = new TestDbContextFactory();
        using var db = f.CreateContext();
        using var log = new LoggerConfiguration().CreateLogger();
        var act = () => new SemanticSearchService(
            Mock.Of<IEmbeddingService>(), Mock.Of<IVectorStore>(), db, null, log);
        act.Should().NotThrow();
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  SearchAsync — guards & short-circuits
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SearchAsync_NullQuery_Throws()
    {
        var h = NewHarness();
        var act = () => h.Service.SearchAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_BlankQueryText_ReturnsEmpty(string text)
    {
        var h = NewHarness();
        var results = await h.Service.SearchAsync(Q(text));
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_EmbeddingThrowsOperationCanceled_Rethrows()
    {
        var h = NewHarness();
        h.Embed.Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => h.Service.SearchAsync(Q("hello world"));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SearchAsync_EmbeddingThrowsGeneric_ReturnsEmpty()
    {
        var h = NewHarness();
        h.Embed.Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("embed boom"));

        var results = await h.Service.SearchAsync(Q("hello world"));

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_VectorStoreThrowsOperationCanceled_Rethrows()
    {
        var h = NewHarness();
        h.Vector.Setup(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => h.Service.SearchAsync(Q("hello world"));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SearchAsync_VectorStoreThrowsGeneric_ReturnsEmpty()
    {
        var h = NewHarness();
        h.Vector.Setup(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("vector boom"));

        var results = await h.Service.SearchAsync(Q("hello world"));

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_VectorStoreReturnsNoHits_ReturnsEmpty()
    {
        var h = NewHarness();
        // Default vector setup returns an empty list.
        var results = await h.Service.SearchAsync(Q("hello world"));
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_NoMatchingChunksInDatabase_ReturnsEmpty()
    {
        var h = NewHarness();
        // Vector returns a chunk id that does not exist in the DB.
        h.SetVector(Vec(999, 0.9));

        var results = await h.Service.SearchAsync(Q("hello world"));

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_ChunkLoadThrows_ReturnsEmpty()
    {
        var h = NewHarness();
        h.SetVector(Vec(1, 0.9));
        h.Db.Dispose(); // the chunk-load query will throw ObjectDisposedException (generic catch → empty)

        var results = await h.Service.SearchAsync(Q("hello world"));

        results.Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  SearchAsync — embedding-model version compatibility gate
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SearchAsync_MismatchedVersionChunk_IsExcluded()
    {
        var h = NewHarness();
        var docId = SeedDoc(h);
        var good = SeedChunk(h, docId, content: "compatible", chunkIndex: 0, modelVersion: ModelVersion);
        var stale = SeedChunk(h, docId, content: "stale", chunkIndex: 1, modelVersion: "old-model:0.9");
        h.SetVector(Vec(good, 0.9), Vec(stale, 0.95));

        var results = await h.Service.SearchAsync(Q("query text"));

        results.Should().ContainSingle().Which.ChunkId.Should().Be(good);
    }

    [Fact]
    public async Task SearchAsync_AllChunksVersionMismatched_ReturnsEmpty()
    {
        var h = NewHarness();
        var docId = SeedDoc(h);
        var stale = SeedChunk(h, docId, modelVersion: "old-model:0.9");
        h.SetVector(Vec(stale, 0.95));

        var results = await h.Service.SearchAsync(Q("query text"));

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_LegacyNullVersionChunk_TreatedAsCompatible()
    {
        var h = NewHarness();
        var docId = SeedDoc(h);
        var legacy = SeedChunk(h, docId, content: "legacy chunk", modelVersion: null);
        h.SetVector(Vec(legacy, 0.9));

        var results = await h.Service.SearchAsync(Q("query text"));

        results.Should().ContainSingle().Which.ChunkId.Should().Be(legacy);
    }

    [Fact]
    public async Task SearchAsync_SecondMismatchOnSameInstance_DoesNotReWarn()
    {
        var h = NewHarness();
        var docId = SeedDoc(h);
        var good = SeedChunk(h, docId, content: "good", chunkIndex: 0, modelVersion: ModelVersion);
        var stale = SeedChunk(h, docId, content: "stale", chunkIndex: 1, modelVersion: "old:0.1");
        h.SetVector(Vec(good, 0.9), Vec(stale, 0.95));

        // First call trips the "warn once" latch; second call exercises the already-warned branch.
        var first = await h.Service.SearchAsync(Q("query text"));
        var second = await h.Service.SearchAsync(Q("query text"));

        first.Should().ContainSingle();
        second.Should().ContainSingle();
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  SearchAsync — happy path & result mapping
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SearchAsync_HappyPath_MapsAllResultFields()
    {
        var h = NewHarness();
        var docId = SeedDoc(h, fileName: "report.pdf", fileType: "pdf", filePath: "/vault/report.pdf");
        var chunkId = SeedChunk(h, docId, content: "the answer is 42", chunkIndex: 3, pageNumber: 7);
        var colId = SeedCollection(h, "Research");
        Link(h, docId, colId);
        h.SetVector(Vec(chunkId, 0.87));

        var results = await h.Service.SearchAsync(Q("answer"));

        var r = results.Should().ContainSingle().Subject;
        r.ChunkId.Should().Be(chunkId);
        r.DocumentId.Should().Be(docId);
        r.FileName.Should().Be("report.pdf");
        r.FilePath.Should().Be("/vault/report.pdf");
        r.FileType.Should().Be("pdf");
        r.PageNumber.Should().Be(7);
        r.ChunkIndex.Should().Be(3);
        r.MatchedText.Should().Be("the answer is 42");
        r.Excerpt.Should().Be("the answer is 42");
        r.Score.Should().BeApproximately(0.87f, 0.0001f);
        r.CollectionNames.Should().ContainSingle().Which.Should().Be("Research");
    }

    [Fact]
    public async Task SearchAsync_ScoreAboveOne_IsClampedToOne()
    {
        var h = NewHarness();
        var docId = SeedDoc(h);
        var chunkId = SeedChunk(h, docId, content: "clamp me");
        h.SetVector(Vec(chunkId, 1.6)); // Distance = -0.6, similarity 1.6 → clamp to 1.0

        var results = await h.Service.SearchAsync(Q("clamp"));

        results.Should().ContainSingle().Which.Score.Should().Be(1.0f);
    }

    [Fact]
    public async Task SearchAsync_ScoreBelowMinScore_IsFilteredOut()
    {
        var h = NewHarness();
        var docId = SeedDoc(h);
        var chunkId = SeedChunk(h, docId, content: "weak match");
        // Vector returns a hit below the query's MinScore; the service re-checks and drops it.
        h.SetVector(Vec(chunkId, 0.2));

        var results = await h.Service.SearchAsync(Q("weak", minScore: 0.5f));

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_SortsByScoreDescending_AndTakesTopK()
    {
        var h = NewHarness();
        var docId = SeedDoc(h);
        var low = SeedChunk(h, docId, content: "low", chunkIndex: 0);
        var high = SeedChunk(h, docId, content: "high", chunkIndex: 1);
        var mid = SeedChunk(h, docId, content: "mid", chunkIndex: 2);
        h.SetVector(Vec(low, 0.40), Vec(high, 0.95), Vec(mid, 0.70));

        var results = await h.Service.SearchAsync(Q("term", topK: 2));

        results.Should().HaveCount(2);
        results[0].ChunkId.Should().Be(high);
        results[1].ChunkId.Should().Be(mid);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  SearchAsync — metadata filters
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SearchAsync_CollectionFilter_ExcludesNonMembers()
    {
        var h = NewHarness();
        var colId = SeedCollection(h, "InScope");

        var inDoc = SeedDoc(h, fileName: "in.pdf");
        var inChunk = SeedChunk(h, inDoc, content: "in collection", chunkIndex: 0);
        Link(h, inDoc, colId);

        var outDoc = SeedDoc(h, fileName: "out.pdf");
        var outChunk = SeedChunk(h, outDoc, content: "not in collection", chunkIndex: 0);

        h.SetVector(Vec(inChunk, 0.9), Vec(outChunk, 0.95));

        var results = await h.Service.SearchAsync(Q("term", collectionId: colId));

        results.Should().ContainSingle().Which.ChunkId.Should().Be(inChunk);
    }

    [Fact]
    public async Task SearchAsync_FileTypeFilter_IsCaseInsensitiveAndTrimmed()
    {
        var h = NewHarness();
        var pdfDoc = SeedDoc(h, fileName: "a.pdf", fileType: "pdf");
        var pdfChunk = SeedChunk(h, pdfDoc, content: "pdf body", chunkIndex: 0);
        var docxDoc = SeedDoc(h, fileName: "b.docx", fileType: "docx");
        var docxChunk = SeedChunk(h, docxDoc, content: "docx body", chunkIndex: 0);
        h.SetVector(Vec(pdfChunk, 0.9), Vec(docxChunk, 0.95));

        var results = await h.Service.SearchAsync(Q("term", fileType: "  PDF  "));

        results.Should().ContainSingle().Which.ChunkId.Should().Be(pdfChunk);
    }

    [Fact]
    public async Task SearchAsync_CreatedAfterFilter_ExcludesOlderDocuments()
    {
        var h = NewHarness();
        var cutoff = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var oldDoc = SeedDoc(h, fileName: "old.pdf", importedAt: cutoff.AddDays(-10));
        var oldChunk = SeedChunk(h, oldDoc, content: "old", chunkIndex: 0);
        var newDoc = SeedDoc(h, fileName: "new.pdf", importedAt: cutoff.AddDays(10));
        var newChunk = SeedChunk(h, newDoc, content: "new", chunkIndex: 0);
        h.SetVector(Vec(oldChunk, 0.95), Vec(newChunk, 0.9));

        var results = await h.Service.SearchAsync(Q("term", createdAfter: cutoff));

        results.Should().ContainSingle().Which.ChunkId.Should().Be(newChunk);
    }

    [Fact]
    public async Task SearchAsync_CreatedBeforeFilter_ExcludesNewerDocuments()
    {
        var h = NewHarness();
        var cutoff = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var oldDoc = SeedDoc(h, fileName: "old.pdf", importedAt: cutoff.AddDays(-10));
        var oldChunk = SeedChunk(h, oldDoc, content: "old", chunkIndex: 0);
        var newDoc = SeedDoc(h, fileName: "new.pdf", importedAt: cutoff.AddDays(10));
        var newChunk = SeedChunk(h, newDoc, content: "new", chunkIndex: 0);
        h.SetVector(Vec(oldChunk, 0.9), Vec(newChunk, 0.95));

        var results = await h.Service.SearchAsync(Q("term", createdBefore: cutoff));

        results.Should().ContainSingle().Which.ChunkId.Should().Be(oldChunk);
    }

    [Fact]
    public async Task SearchAsync_CombinedFilters_AllApplied()
    {
        var h = NewHarness();
        var cutoff = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var colId = SeedCollection(h, "C");

        var match = SeedDoc(h, fileName: "match.pdf", fileType: "pdf", importedAt: cutoff.AddDays(5));
        var matchChunk = SeedChunk(h, match, content: "match", chunkIndex: 0);
        Link(h, match, colId);

        // Fails the file-type filter only.
        var wrongType = SeedDoc(h, fileName: "wrong.txt", fileType: "txt", importedAt: cutoff.AddDays(5));
        var wrongTypeChunk = SeedChunk(h, wrongType, content: "wrong-type", chunkIndex: 0);
        Link(h, wrongType, colId);

        h.SetVector(Vec(matchChunk, 0.9), Vec(wrongTypeChunk, 0.95));

        var results = await h.Service.SearchAsync(
            Q("term", collectionId: colId, fileType: "pdf", createdAfter: cutoff));

        results.Should().ContainSingle().Which.ChunkId.Should().Be(matchChunk);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  SearchAsync — excerpt building
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SearchAsync_ShortContent_ExcerptIsWhitespaceNormalizedContent()
    {
        var h = NewHarness();
        var docId = SeedDoc(h);
        var chunkId = SeedChunk(h, docId, content: "  hello   world\tfrom\nthe   chunk  ");
        h.SetVector(Vec(chunkId, 0.9));

        var results = await h.Service.SearchAsync(Q("hello"));

        results.Should().ContainSingle().Which.Excerpt.Should().Be("hello world from the chunk");
    }

    [Fact]
    public async Task SearchAsync_EmptyContent_ExcerptIsEmpty()
    {
        var h = NewHarness();
        var docId = SeedDoc(h);
        var chunkId = SeedChunk(h, docId, content: "");
        h.SetVector(Vec(chunkId, 0.9));

        var results = await h.Service.SearchAsync(Q("term"));

        var r = results.Should().ContainSingle().Subject;
        r.Excerpt.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_LongContentWithKeywordMatch_BuildsCenteredExcerptWithEllipses()
    {
        var h = NewHarness();
        var docId = SeedDoc(h);
        // Word-filled content so the centered-excerpt word-boundary snapping is exercised;
        // "learning" sits in the middle so both leading and trailing ellipses appear.
        var prefix = string.Join(" ", Enumerable.Repeat("word", 60));
        var suffix = string.Join(" ", Enumerable.Repeat("term", 60));
        var content = $"{prefix} learning {suffix}";
        var chunkId = SeedChunk(h, docId, content: content);
        h.SetVector(Vec(chunkId, 0.9));

        var results = await h.Service.SearchAsync(Q("machine learning"));

        var excerpt = results.Should().ContainSingle().Subject.Excerpt;
        excerpt.Should().StartWith("...");
        excerpt.Should().EndWith("...");
        excerpt.Should().Contain("learning");
        excerpt.Length.Should().BeLessThan(content.Length);
    }

    [Fact]
    public async Task SearchAsync_LongContentWithoutKeywordMatch_FallsBackToLeadingSlice()
    {
        var h = NewHarness();
        var docId = SeedDoc(h);
        var content = string.Join(" ", Enumerable.Repeat("alpha", 80)); // > 200 chars, no query word
        var chunkId = SeedChunk(h, docId, content: content);
        h.SetVector(Vec(chunkId, 0.9));

        // Query words that never appear in the content.
        var results = await h.Service.SearchAsync(Q("zzz nonexistent keyword"));

        var excerpt = results.Should().ContainSingle().Subject.Excerpt;
        excerpt.Should().EndWith("...");
        excerpt.Should().StartWith("alpha");
        excerpt.Length.Should().BeLessThanOrEqualTo(203); // 200 + "..."
    }

    [Fact]
    public async Task SearchAsync_LongContentWithOnlyShortQueryWords_FallsBackToLeadingSlice()
    {
        var h = NewHarness();
        var docId = SeedDoc(h);
        var content = string.Join(" ", Enumerable.Repeat("beta", 80));
        var chunkId = SeedChunk(h, docId, content: content);
        h.SetVector(Vec(chunkId, 0.9));

        // Every query word is < 3 chars, so no keyword positions are extracted → fallback slice.
        var results = await h.Service.SearchAsync(Q("is ai to"));

        results.Should().ContainSingle().Which.Excerpt.Should().EndWith("...");
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  SearchAsync — retrieval sizing (multiplier / cap)
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SearchAsync_WithoutRagConfig_UsesFallbackMultiplierOfThree()
    {
        var h = NewHarness();
        int capturedTopK = -1;
        h.Vector.Setup(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .Callback((float[] _, int topK, double _, CancellationToken _) => capturedTopK = topK)
            .ReturnsAsync((IReadOnlyList<VectorSearchResult>)Array.Empty<VectorSearchResult>());

        await h.Service.SearchAsync(Q("term", topK: 10));

        capturedTopK.Should().Be(30); // 10 * fallback multiplier 3, under the 500 cap
    }

    [Fact]
    public async Task SearchAsync_WithRagConfig_AppliesMultiplierAndCap()
    {
        var h = NewHarness(withRag: true);
        h.Rag.SetupGet(r => r.RetrievalMultiplier).Returns(4);
        h.Rag.SetupGet(r => r.RetrievalCap).Returns(25);
        int capturedTopK = -1;
        h.Vector.Setup(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .Callback((float[] _, int topK, double _, CancellationToken _) => capturedTopK = topK)
            .ReturnsAsync((IReadOnlyList<VectorSearchResult>)Array.Empty<VectorSearchResult>());

        await h.Service.SearchAsync(Q("term", topK: 10));

        capturedTopK.Should().Be(25); // min(10 * 4 = 40, cap 25)
    }

    [Fact]
    public async Task SearchAsync_WithRagConfig_ZeroMultiplierAndCap_ClampedToOne()
    {
        var h = NewHarness(withRag: true);
        h.Rag.SetupGet(r => r.RetrievalMultiplier).Returns(0);
        h.Rag.SetupGet(r => r.RetrievalCap).Returns(0);
        int capturedTopK = -1;
        h.Vector.Setup(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .Callback((float[] _, int topK, double _, CancellationToken _) => capturedTopK = topK)
            .ReturnsAsync((IReadOnlyList<VectorSearchResult>)Array.Empty<VectorSearchResult>());

        await h.Service.SearchAsync(Q("term", topK: 10));

        // multiplier max(1,0)=1 → 10; cap max(1,0)=1 → min(10,1)=1.
        capturedTopK.Should().Be(1);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  SaveSearchHistoryAsync
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveSearchHistory_BlankQuery_DoesNotPersist(string text)
    {
        var h = NewHarness();

        await h.Service.SaveSearchHistoryAsync(text, 3);

        await using var read = h.Fresh();
        (await read.SearchHistory.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SaveSearchHistory_PersistsTrimmedEntryWithAllFilterFields()
    {
        var h = NewHarness();
        var after = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var before = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        await h.Service.SaveSearchHistoryAsync(
            "  neural nets  ", resultCount: 5,
            minScore: 0.4, maxResults: 20, dateAfter: after, dateBefore: before, sortOrder: "relevance");

        await using var read = h.Fresh();
        var entry = await read.SearchHistory.SingleAsync();
        entry.Query.Should().Be("neural nets");
        entry.SearchType.Should().Be("semantic");
        entry.ResultCount.Should().Be(5);
        entry.IsSaved.Should().BeFalse();
        entry.MinScore.Should().Be(0.4);
        entry.MaxResults.Should().Be(20);
        entry.DateAfter.Should().Be(after);
        entry.DateBefore.Should().Be(before);
        entry.SortOrder.Should().Be("relevance");
    }

    [Fact]
    public async Task SaveSearchHistory_OnFailure_SwallowsAndDoesNotThrow()
    {
        var h = NewHarness();
        h.Db.Dispose(); // Add / SaveChanges will throw; the method logs and returns.

        var act = () => h.Service.SaveSearchHistoryAsync("query", 1);

        await act.Should().NotThrowAsync();
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  GetSearchHistoryAsync
    // ══════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task GetSearchHistory_NonPositiveLimit_ReturnsEmpty(int limit)
    {
        var h = NewHarness();
        SeedHistory(h);

        var entries = await h.Service.GetSearchHistoryAsync(limit);

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSearchHistory_ReturnsNewestFirst_MappedFields()
    {
        var h = NewHarness();
        SeedHistory(h, query: "older", searchedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), resultCount: 1);
        SeedHistory(h, query: "newer", searchedAt: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), resultCount: 2);

        var entries = await h.Service.GetSearchHistoryAsync();

        entries.Should().HaveCount(2);
        entries[0].QueryText.Should().Be("newer");
        entries[0].ResultCount.Should().Be(2);
        entries[1].QueryText.Should().Be("older");
    }

    [Fact]
    public async Task GetSearchHistory_HonoursLimit()
    {
        var h = NewHarness();
        for (var i = 0; i < 5; i++)
        {
            SeedHistory(h, query: $"q{i}", searchedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i));
        }

        var entries = await h.Service.GetSearchHistoryAsync(limit: 2);

        entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetSearchHistory_OnFailure_ReturnsEmpty()
    {
        var h = NewHarness();
        h.Db.Dispose();

        var entries = await h.Service.GetSearchHistoryAsync();

        entries.Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  ClearSearchHistoryAsync
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ClearSearchHistory_DeletesAllEntries()
    {
        var h = NewHarness();
        SeedHistory(h, query: "a");
        SeedHistory(h, query: "b");

        await h.Service.ClearSearchHistoryAsync();

        await using var read = h.Fresh();
        (await read.SearchHistory.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ClearSearchHistory_OnFailure_Throws()
    {
        var h = NewHarness();
        h.Db.Dispose();

        var act = () => h.Service.ClearSearchHistoryAsync();

        await act.Should().ThrowAsync<Exception>();
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  SaveSearchFilterAsync / UnsaveSearchFilterAsync
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SaveSearchFilter_ExistingEntry_SetsIsSavedTrue()
    {
        var h = NewHarness();
        var id = SeedHistory(h, isSaved: false);

        await h.Service.SaveSearchFilterAsync(id);

        await using var read = h.Fresh();
        (await read.SearchHistory.FindAsync(id))!.IsSaved.Should().BeTrue();
    }

    [Fact]
    public async Task SaveSearchFilter_MissingEntry_NoOp()
    {
        var h = NewHarness();

        var act = () => h.Service.SaveSearchFilterAsync(123456);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UnsaveSearchFilter_ExistingEntry_SetsIsSavedFalse()
    {
        var h = NewHarness();
        var id = SeedHistory(h, isSaved: true);

        await h.Service.UnsaveSearchFilterAsync(id);

        await using var read = h.Fresh();
        (await read.SearchHistory.FindAsync(id))!.IsSaved.Should().BeFalse();
    }

    [Fact]
    public async Task UnsaveSearchFilter_MissingEntry_NoOp()
    {
        var h = NewHarness();

        var act = () => h.Service.UnsaveSearchFilterAsync(123456);

        await act.Should().NotThrowAsync();
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  GetSavedFiltersAsync
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSavedFilters_ReturnsOnlySaved_NewestFirst()
    {
        var h = NewHarness();
        SeedHistory(h, query: "unsaved", isSaved: false);
        SeedHistory(h, query: "saved-old", isSaved: true, searchedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        SeedHistory(h, query: "saved-new", isSaved: true, searchedAt: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var entries = await h.Service.GetSavedFiltersAsync();

        entries.Should().HaveCount(2);
        entries.Should().OnlyContain(e => e.IsSaved);
        entries[0].QueryText.Should().Be("saved-new");
        entries[1].QueryText.Should().Be("saved-old");
    }

    [Fact]
    public async Task GetSavedFilters_NoneSaved_ReturnsEmpty()
    {
        var h = NewHarness();
        SeedHistory(h, isSaved: false);

        var entries = await h.Service.GetSavedFiltersAsync();

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSavedFilters_OnFailure_ReturnsEmpty()
    {
        var h = NewHarness();
        h.Db.Dispose();

        var entries = await h.Service.GetSavedFiltersAsync();

        entries.Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  SearchAsync — cancellation propagation
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SearchAsync_CanceledTokenDuringChunkLoad_Rethrows()
    {
        var h = NewHarness();
        var docId = SeedDoc(h);
        var chunkId = SeedChunk(h, docId, content: "body");
        h.SetVector(Vec(chunkId, 0.9));

        // Embedding + vector mocks ignore the token; the EF chunk-load honours it and throws OCE.
        var act = () => h.Service.SearchAsync(Q("body"), Canceled());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
