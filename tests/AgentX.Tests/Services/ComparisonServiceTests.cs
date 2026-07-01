using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Search;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Intelligence.Models;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

/// <summary>
/// Behavioural coverage for <see cref="ComparisonService"/> — the AI-powered cross-document
/// comparison pipeline: resolve document metadata → retrieve each document's most-relevant chunks
/// via semantic search → assemble a structured prompt → synthesize a JSON analysis via the AI →
/// parse it into a <see cref="ComparisonReport"/> (with a plain-text fallback) — plus the Markdown
/// export renderer.
///
/// <para><b>Harness design.</b> The service composes four collaborators, all mocked:
/// <see cref="IAiService"/> (only consumed when the default <see cref="IDocumentSynthesisService"/>
/// is constructed), <see cref="IDocumentService"/> (<c>GetDocumentAsync</c> per id),
/// <see cref="ISemanticSearchService"/> (<c>SearchAsync</c> per document), and — the key seam —
/// an injected <see cref="IDocumentSynthesisService"/> whose <c>SynthesizeComparisonAsync</c> returns
/// a caller-controlled <c>RawResponse</c>, letting each test drive the parser deterministically
/// (valid JSON, malformed JSON → plain-text fallback, cancellation, or AI failure). A real silent
/// Serilog logger is supplied because the ctor consumes <c>logger.ForContext&lt;T&gt;()</c>.
/// One integration-style test omits the synthesis seam so the real
/// <see cref="DocumentSynthesisService"/> is built from <see cref="IAiService"/>, exercising the
/// default-construction branch end-to-end.</para>
/// </summary>
public sealed class ComparisonServiceTests : IDisposable
{
    private readonly List<Harness> _harnesses = new();

    private Harness NewHarness()
    {
        var h = new Harness();
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
        public Mock<IAiService> Ai { get; } = new();
        public Mock<IDocumentService> Docs { get; } = new();
        public Mock<ISemanticSearchService> Search { get; } = new();
        public Mock<IDocumentSynthesisService> Synth { get; } = new();
        public Serilog.Core.Logger Logger { get; } = new LoggerConfiguration().CreateLogger();

        /// <summary>The SearchQuery captured on the most recent SearchAsync invocation.</summary>
        public SearchQuery? LastQuery { get; private set; }

        /// <summary>The synthesis request captured on the most recent SynthesizeComparisonAsync call.</summary>
        public ComparisonSynthesisRequest? LastSynthesisRequest { get; private set; }

        public Harness()
        {
            // Sensible default: an empty JSON object parses into an empty (but valid) report.
            SetSynthesisResponse("{}");
        }

        /// <summary>Builds the SUT with the injected synthesis seam (the default for most tests).</summary>
        public ComparisonService Build() =>
            new(Ai.Object, Docs.Object, Search.Object, Logger, Synth.Object);

        /// <summary>
        /// Builds the SUT WITHOUT the synthesis seam, so the ctor constructs a real
        /// <see cref="DocumentSynthesisService"/> from <see cref="IAiService"/>.
        /// </summary>
        public ComparisonService BuildWithDefaultSynthesis() =>
            new(Ai.Object, Docs.Object, Search.Object, Logger);

        /// <summary>Registers a document so GetDocumentAsync(id) resolves to it. Unregistered ids stay null.</summary>
        public Harness WithDocument(long id, string fileName)
        {
            Docs.Setup(d => d.GetDocumentAsync(id))
                .ReturnsAsync(new DocumentEntity { Id = id, FileName = fileName });
            return this;
        }

        /// <summary>Registers a document whose fetch runs a side effect (e.g. cancels a token).</summary>
        public Harness WithDocument(long id, string fileName, Action onFetched)
        {
            Docs.Setup(d => d.GetDocumentAsync(id))
                .ReturnsAsync(new DocumentEntity { Id = id, FileName = fileName })
                .Callback(onFetched);
            return this;
        }

        /// <summary>SearchAsync returns the given chunks for every call, capturing the query each time.</summary>
        public Harness WithChunks(params SearchResult[] chunks)
        {
            Search.Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
                  .Callback((SearchQuery q, CancellationToken _) => LastQuery = q)
                  .ReturnsAsync((IReadOnlyList<SearchResult>)chunks.ToList());
            return this;
        }

        /// <summary>The injected synthesis service returns the given raw AI response.</summary>
        public Harness SetSynthesisResponse(string rawResponse, long estimatedPromptTokens = 0)
        {
            Synth.Setup(s => s.SynthesizeComparisonAsync(
                        It.IsAny<ComparisonSynthesisRequest>(), It.IsAny<CancellationToken>()))
                 .Callback((ComparisonSynthesisRequest r, CancellationToken _) => LastSynthesisRequest = r)
                 .ReturnsAsync(new ComparisonSynthesisResult
                 {
                     RawResponse = rawResponse,
                     EstimatedPromptTokens = estimatedPromptTokens,
                 });
            return this;
        }

        /// <summary>The injected synthesis service throws the given exception.</summary>
        public Harness SetSynthesisThrows(Exception ex)
        {
            Synth.Setup(s => s.SynthesizeComparisonAsync(
                        It.IsAny<ComparisonSynthesisRequest>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(ex);
            return this;
        }

        /// <summary>Configures the real-synthesis path's underlying ChatAsync to return the given text.</summary>
        public Harness WithAiChatResponse(string response)
        {
            Ai.Setup(a => a.ChatAsync(
                    It.IsAny<IReadOnlyList<ChatMessage>>(),
                    It.IsAny<string>(),
                    It.IsAny<ChatOptions>(),
                    It.IsAny<CancellationToken>()))
              .ReturnsAsync(response);
            return this;
        }

        public void Dispose() => Logger.Dispose();
    }

    private static SearchResult Chunk(long documentId, int chunkIndex, string text) => new()
    {
        ChunkId = documentId * 1000 + chunkIndex,
        DocumentId = documentId,
        ChunkIndex = chunkIndex,
        MatchedText = text,
    };

    // ══════════════════════════════════════════════════════════════════════════
    //  Constructor validation
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_NullAiService_Throws()
    {
        var h = NewHarness();
        var act = () => new ComparisonService(null!, h.Docs.Object, h.Search.Object, h.Logger, h.Synth.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("aiService");
    }

    [Fact]
    public void Constructor_NullDocumentService_Throws()
    {
        var h = NewHarness();
        var act = () => new ComparisonService(h.Ai.Object, null!, h.Search.Object, h.Logger, h.Synth.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("documentService");
    }

    [Fact]
    public void Constructor_NullSearchService_Throws()
    {
        var h = NewHarness();
        var act = () => new ComparisonService(h.Ai.Object, h.Docs.Object, null!, h.Logger, h.Synth.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("searchService");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        // An explicit (non-null) synthesis service is supplied so the ctor's DocumentSynthesisService
        // fallback is short-circuited and the logger?.ForContext ?? throw guard is what fires.
        var h = NewHarness();
        var act = () => new ComparisonService(h.Ai.Object, h.Docs.Object, h.Search.Object, null!, h.Synth.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithDefaultSynthesisService_DoesNotThrow()
    {
        // Omitting the synthesis service exercises the `?? new DocumentSynthesisService(aiService, logger)` branch.
        var h = NewHarness();
        var act = () => h.BuildWithDefaultSynthesis();
        act.Should().NotThrow();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  CompareDocumentsAsync — input validation
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompareDocumentsAsync_NullIds_ThrowsArgumentException()
    {
        var sut = NewHarness().Build();
        var act = () => sut.CompareDocumentsAsync(null!);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("documentIds");
    }

    [Fact]
    public async Task CompareDocumentsAsync_EmptyIds_ThrowsArgumentException()
    {
        var sut = NewHarness().Build();
        var act = () => sut.CompareDocumentsAsync(Array.Empty<long>());
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("documentIds");
    }

    [Fact]
    public async Task CompareDocumentsAsync_SingleId_ThrowsArgumentException()
    {
        var sut = NewHarness().Build();
        var act = () => sut.CompareDocumentsAsync(new long[] { 1 });
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("documentIds");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  CompareDocumentsAsync — document resolution
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompareDocumentsAsync_FewerThanTwoResolvable_ThrowsInvalidOperation()
    {
        // Two ids requested; only one resolves (the other GetDocumentAsync returns null and is skipped).
        var h = NewHarness().WithDocument(1, "a.txt");
        var sut = h.Build();

        var act = () => sut.CompareDocumentsAsync(new long[] { 1, 2 });

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("At least two resolvable");
    }

    [Fact]
    public async Task CompareDocumentsAsync_SkipsUnresolvableButProceedsWithTwo()
    {
        // Three ids; the middle one is unresolvable → skipped with a warning, two remain → success.
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(3, "c.txt")
            .WithChunks(Chunk(1, 0, "alpha"), Chunk(3, 0, "gamma"));
        var sut = h.Build();

        var report = await sut.CompareDocumentsAsync(new long[] { 1, 2, 3 });

        report.DocumentNames.Should().Equal("a.txt", "c.txt");
        h.Docs.Verify(d => d.GetDocumentAsync(2L), Times.Once);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  CompareDocumentsAsync — chunk retrieval, filtering, query selection
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompareDocumentsAsync_ScopesChunksToOwningDocument_AndOrdersByChunkIndex()
    {
        // The search returns chunks from BOTH documents (and out of index order); each document's
        // section must contain only its own chunks, ordered by ChunkIndex.
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(
                Chunk(1, 2, "a-two"),
                Chunk(2, 0, "b-zero"),
                Chunk(1, 0, "a-zero"),
                Chunk(1, 1, "a-one"));
        var sut = h.Build();

        await sut.CompareDocumentsAsync(new long[] { 1, 2 });

        var content = h.LastSynthesisRequest!.ContentByDocument;
        content["a.txt"].Should().Be("a-zero\n\na-one\n\na-two");
        content["b.txt"].Should().Be("b-zero");
    }

    [Fact]
    public async Task CompareDocumentsAsync_DocumentWithNoChunks_UsesPlaceholderBody()
    {
        // doc 2 has no chunks returned for it → the placeholder body is used so it still appears.
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "alpha"));
        var sut = h.Build();

        await sut.CompareDocumentsAsync(new long[] { 1, 2 });

        var content = h.LastSynthesisRequest!.ContentByDocument;
        content["a.txt"].Should().Be("alpha");
        content["b.txt"].Should().Be("(No indexed content available for this document.)");
    }

    [Fact]
    public async Task CompareDocumentsAsync_NoFocusQuery_UsesFallbackQueryWithSemanticDefaults()
    {
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"));
        var sut = h.Build();

        await sut.CompareDocumentsAsync(new long[] { 1, 2 }, new ComparisonOptions { MaxChunksPerDoc = 7 });

        h.LastQuery.Should().NotBeNull();
        h.LastQuery!.QueryText.Should().Be("main topics key findings conclusions summary");
        h.LastQuery.TopK.Should().Be(7);
        h.LastQuery.MinScore.Should().BeApproximately(0.15f, 0.0001f);
        h.LastQuery.Mode.Should().Be(SearchMode.Semantic);
    }

    [Fact]
    public async Task CompareDocumentsAsync_WithFocusQuery_UsesItAsTheSearchQuery()
    {
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"));
        var sut = h.Build();

        await sut.CompareDocumentsAsync(
            new long[] { 1, 2 },
            new ComparisonOptions { FocusQuery = "pricing strategy" });

        h.LastQuery!.QueryText.Should().Be("pricing strategy");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CompareDocumentsAsync_BlankFocusQuery_FallsBackToDefaultQuery(string? focus)
    {
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"));
        var sut = h.Build();

        await sut.CompareDocumentsAsync(
            new long[] { 1, 2 },
            new ComparisonOptions { FocusQuery = focus });

        h.LastQuery!.QueryText.Should().Be("main topics key findings conclusions summary");
    }

    [Fact]
    public async Task CompareDocumentsAsync_NullOptions_DefaultsAreApplied()
    {
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"));
        var sut = h.Build();

        await sut.CompareDocumentsAsync(new long[] { 1, 2 }); // options == null → defaults

        h.LastQuery!.TopK.Should().Be(5);            // ComparisonOptions.MaxChunksPerDoc default
        h.LastQuery.QueryText.Should().Be("main topics key findings conclusions summary");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  CompareDocumentsAsync — synthesis failure handling
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompareDocumentsAsync_SynthesisCancelled_RethrowsOperationCanceled()
    {
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"))
            .SetSynthesisThrows(new OperationCanceledException());
        var sut = h.Build();

        var act = () => sut.CompareDocumentsAsync(new long[] { 1, 2 });

        // Must propagate as OperationCanceledException, NOT be wrapped in InvalidOperationException.
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CompareDocumentsAsync_SynthesisFails_WrapsInInvalidOperation()
    {
        var inner = new TimeoutException("model timed out");
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"))
            .SetSynthesisThrows(inner);
        var sut = h.Build();

        var act = () => sut.CompareDocumentsAsync(new long[] { 1, 2 });

        var assertion = await act.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Message.Should().Contain("AI service returned an error");
        assertion.Which.InnerException.Should().BeSameAs(inner);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  CompareDocumentsAsync — JSON parsing (happy path)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompareDocumentsAsync_ValidJson_MapsEveryField()
    {
        const string json = """
            {
              "summary": "  Two views on scaling.  ",
              "similarities": ["Both cite cost", "  ", "Both mention latency"],
              "differences": ["A prefers vertical, B prefers horizontal"],
              "contradictions": ["A says cache-first; B says compute-first"],
              "uniquePoints": {
                "a.txt": ["A benchmarks on ARM"],
                "b.txt": ["B includes a rollback plan"]
              }
            }
            """;
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"))
            .SetSynthesisResponse(json, estimatedPromptTokens: 100);
        var sut = h.Build();

        var report = await sut.CompareDocumentsAsync(new long[] { 1, 2 });

        report.Summary.Should().Be("Two views on scaling.");                 // trimmed
        report.Similarities.Should().Equal("Both cite cost", "Both mention latency"); // blank dropped
        report.Differences.Should().ContainSingle().Which.Should().Contain("vertical");
        report.Contradictions.Should().ContainSingle();
        report.UniquePoints["a.txt"].Should().Equal("A benchmarks on ARM");
        report.UniquePoints["b.txt"].Should().Equal("B includes a rollback plan");
        report.DocumentNames.Should().Equal("a.txt", "b.txt");
        report.DurationMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task CompareDocumentsAsync_TotalTokens_IsPromptPlusResponseEstimate()
    {
        // rawResponse length 8 → ceil(8/4) = 2 completion tokens; prompt estimate 100 → total 102.
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"))
            .SetSynthesisResponse("{\"x\":1}", estimatedPromptTokens: 100); // 7 chars → ceil(7/4)=2
        var sut = h.Build();

        var report = await sut.CompareDocumentsAsync(new long[] { 1, 2 });

        report.TotalTokensUsed.Should().Be(102);
    }

    [Fact]
    public async Task CompareDocumentsAsync_JsonWrappedInProseAndFences_IsStillExtracted()
    {
        const string raw = "Here is the analysis you asked for:\n```json\n{\"summary\":\"ok\"}\n```\nThanks!";
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"))
            .SetSynthesisResponse(raw);
        var sut = h.Build();

        var report = await sut.CompareDocumentsAsync(new long[] { 1, 2 });

        report.Summary.Should().Be("ok");
    }

    [Fact]
    public async Task CompareDocumentsAsync_UniquePointsKeyCaseInsensitive_MapsToCanonicalName()
    {
        const string json = """{"uniquePoints":{"A.TXT":["only in A"]}}""";
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"))
            .SetSynthesisResponse(json);
        var sut = h.Build();

        var report = await sut.CompareDocumentsAsync(new long[] { 1, 2 });

        report.UniquePoints["a.txt"].Should().Equal("only in A");   // canonicalised to the real name
        report.UniquePoints["b.txt"].Should().BeEmpty();            // pre-seeded even though omitted
    }

    [Fact]
    public async Task CompareDocumentsAsync_UniquePointsUnknownKey_IsKeptVerbatim()
    {
        const string json = """{"uniquePoints":{"ghost.txt":["orphan point"]}}""";
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"))
            .SetSynthesisResponse(json);
        var sut = h.Build();

        var report = await sut.CompareDocumentsAsync(new long[] { 1, 2 });

        report.UniquePoints.Should().ContainKey("ghost.txt");
        report.UniquePoints["ghost.txt"].Should().Equal("orphan point");
    }

    [Fact]
    public async Task CompareDocumentsAsync_UniquePointsNullValue_BecomesEmptyList()
    {
        const string json = """{"uniquePoints":{"a.txt":null}}""";
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"))
            .SetSynthesisResponse(json);
        var sut = h.Build();

        var report = await sut.CompareDocumentsAsync(new long[] { 1, 2 });

        report.UniquePoints["a.txt"].Should().BeEmpty();
    }

    [Fact]
    public async Task CompareDocumentsAsync_NullLists_SanitiseToEmpty()
    {
        // Every list omitted → SanitiseList's null branch → empty lists, empty summary.
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"))
            .SetSynthesisResponse("{}");
        var sut = h.Build();

        var report = await sut.CompareDocumentsAsync(new long[] { 1, 2 });

        report.Summary.Should().BeEmpty();
        report.Similarities.Should().BeEmpty();
        report.Differences.Should().BeEmpty();
        report.Contradictions.Should().BeEmpty();
        report.UniquePoints.Should().ContainKeys("a.txt", "b.txt");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  CompareDocumentsAsync — plain-text fallback
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompareDocumentsAsync_NoJsonDelimiters_UsesPlainTextFallbackAndExtractsSections()
    {
        // No braces → ParseJsonResponse throws (no delimiters) → plain-text fallback.
        // Exercises heading detection via '#' and via trailing ':', bullets via -, *, •,
        // a blank bullet (skipped), a non-heading non-bullet line (skipped), and a heading
        // that does not match the section keyword (turns the section off).
        const string raw =
            "Comparative Analysis\n" +
            "# Similarities\n" +
            "- alpha\n" +
            "* beta\n" +
            "• gamma\n" +
            "- \n" +
            "Differences:\n" +
            "- delta\n" +
            "just some prose here\n" +
            "Contradictions:\n" +
            "- epsilon\n" +
            "Unique Notes:\n" +
            "- ignored\n";
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"))
            .SetSynthesisResponse(raw);
        var sut = h.Build();

        var report = await sut.CompareDocumentsAsync(new long[] { 1, 2 });

        report.Similarities.Should().Equal("alpha", "beta", "gamma");
        report.Differences.Should().Equal("delta");
        report.Contradictions.Should().Equal("epsilon");
        report.Summary.Should().Be(raw);                     // short → full response retained
        report.UniquePoints.Should().ContainKeys("a.txt", "b.txt");
        report.UniquePoints["a.txt"].Should().BeEmpty();
    }

    [Fact]
    public async Task CompareDocumentsAsync_MalformedJsonBody_FallsBackToPlainText()
    {
        // Has both delimiters (so extraction runs) but the body is invalid JSON → the deserializer
        // throws → the outer catch routes to the plain-text fallback.
        const string raw = "{ this is not valid json }";
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"))
            .SetSynthesisResponse(raw);
        var sut = h.Build();

        var report = await sut.CompareDocumentsAsync(new long[] { 1, 2 });

        // Fallback path: no section headings → empty structured lists, summary holds the raw text.
        report.Similarities.Should().BeEmpty();
        report.Summary.Should().Be(raw);
    }

    [Fact]
    public async Task CompareDocumentsAsync_EmptyResponse_YieldsEmptyReportAndZeroCompletionTokens()
    {
        // An empty AI response exercises EstimateTokens' empty-string guard (0 completion tokens) and
        // routes through the plain-text fallback (no JSON delimiters) with an empty summary.
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"))
            .SetSynthesisResponse(string.Empty, estimatedPromptTokens: 40);
        var sut = h.Build();

        var report = await sut.CompareDocumentsAsync(new long[] { 1, 2 });

        report.Summary.Should().BeEmpty();
        report.TotalTokensUsed.Should().Be(40); // 40 prompt + EstimateTokens("") == 0
    }

    [Fact]
    public async Task CompareDocumentsAsync_LongUnparseableResponse_TruncatesSummaryTo600Chars()
    {
        var raw = new string('x', 700); // no braces → fallback; > 600 chars → truncated + ellipsis
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"))
            .SetSynthesisResponse(raw);
        var sut = h.Build();

        var report = await sut.CompareDocumentsAsync(new long[] { 1, 2 });

        report.Summary.Should().HaveLength(601);         // 600 chars + the ellipsis
        report.Summary.Should().EndWith("…");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  CompareDocumentsAsync — cancellation
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompareDocumentsAsync_TokenCancelledBeforeResolution_ThrowsOperationCanceled()
    {
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt");
        var sut = h.Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => sut.CompareDocumentsAsync(new long[] { 1, 2 }, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        h.Docs.Verify(d => d.GetDocumentAsync(It.IsAny<long>()), Times.Never); // thrown before first fetch
    }

    [Fact]
    public async Task CompareDocumentsAsync_TokenCancelledDuringResolution_ThrowsBeforeChunkRetrieval()
    {
        // Both documents resolve; the second fetch cancels the token, so the chunk-retrieval loop's
        // cancellation check (after resolution succeeds) is the one that trips.
        using var cts = new CancellationTokenSource();
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt", onFetched: cts.Cancel)
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"));
        var sut = h.Build();

        var act = () => sut.CompareDocumentsAsync(new long[] { 1, 2 }, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        h.Search.Verify(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  CompareDocumentsAsync — progress reporting & default synthesis integration
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CompareDocumentsAsync_ReportsProgress_ThroughEveryStage()
    {
        // A synchronous IProgress avoids Progress<T>'s SynchronizationContext dispatch, making the
        // captured message list deterministic the moment the call returns.
        var progress = new CollectingProgress();
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"));
        var sut = h.Build();

        await sut.CompareDocumentsAsync(new long[] { 1, 2 }, progress: progress);

        progress.Messages.Should().Contain("Loading document metadata…");
        progress.Messages.Should().Contain("Comparison complete.");
        progress.Messages.Should().Contain(m => m.Contains("a.txt")); // "Reading chunks for 'a.txt'…"
    }

    private sealed class CollectingProgress : IProgress<string>
    {
        public List<string> Messages { get; } = new();
        public void Report(string value) => Messages.Add(value);
    }

    [Fact]
    public async Task CompareDocumentsAsync_DefaultSynthesisService_RunsRealPipelineEndToEnd()
    {
        // No injected synthesis seam → the ctor built a real DocumentSynthesisService, which calls
        // IAiService.ChatAsync. This exercises the default-construction branch end to end.
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "alpha"), Chunk(2, 0, "beta"))
            .WithAiChatResponse("""{"summary":"real path","similarities":["shared"]}""");
        var sut = h.BuildWithDefaultSynthesis();

        var report = await sut.CompareDocumentsAsync(new long[] { 1, 2 });

        report.Summary.Should().Be("real path");
        report.Similarities.Should().Equal("shared");
        h.Ai.Verify(a => a.ChatAsync(
            It.IsAny<IReadOnlyList<ChatMessage>>(),
            It.IsAny<string>(),
            It.IsAny<ChatOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompareDocumentsAsync_DetailLevelSummary_FlowsThroughToSynthesisRequest()
    {
        var h = NewHarness()
            .WithDocument(1, "a.txt")
            .WithDocument(2, "b.txt")
            .WithChunks(Chunk(1, 0, "a"), Chunk(2, 0, "b"));
        var sut = h.Build();

        await sut.CompareDocumentsAsync(
            new long[] { 1, 2 },
            new ComparisonOptions { DetailLevel = "summary" });

        h.LastSynthesisRequest!.Options.DetailLevel.Should().Be("summary");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  ExportComparisonAsMarkdownAsync
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportComparisonAsMarkdownAsync_NullReport_Throws()
    {
        var sut = NewHarness().Build();
        var act = () => sut.ExportComparisonAsMarkdownAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExportComparisonAsMarkdownAsync_FullReport_RendersEverySection()
    {
        var report = new ComparisonReport
        {
            DocumentNames = new List<string> { "a.txt", "b.txt" },
            Summary = "An executive summary.",
            Similarities = new List<string> { "sim-1" },
            Differences = new List<string> { "diff-1" },
            Contradictions = new List<string> { "contra-1" },
            UniquePoints = new Dictionary<string, List<string>>
            {
                ["a.txt"] = new() { "unique-a" },
                ["b.txt"] = new(), // empty list → per-document "no exclusive points" line
            },
            TotalTokensUsed = 999, // < 1000 → no culture-dependent thousands separator in the assertion
            DurationMs = 42,
        };
        var sut = NewHarness().Build();

        var md = await sut.ExportComparisonAsMarkdownAsync(report);

        md.Should().Contain("# Comparative Analysis Report");
        md.Should().Contain("**Documents Compared:** 2");
        md.Should().Contain("An executive summary.");
        md.Should().Contain("- sim-1");
        md.Should().Contain("- diff-1");
        md.Should().Contain("- contra-1");
        md.Should().Contain("### a.txt");
        md.Should().Contain("- unique-a");
        md.Should().Contain("_No exclusive points identified for this document._");
        md.Should().Contain("**Estimated Tokens Used:** 999");
    }

    [Fact]
    public async Task ExportComparisonAsMarkdownAsync_EmptyReport_RendersPlaceholders()
    {
        var report = new ComparisonReport
        {
            DocumentNames = new List<string> { "a.txt" },
            Summary = "   ", // whitespace → "no summary generated"
        };
        var sut = NewHarness().Build();

        var md = await sut.ExportComparisonAsMarkdownAsync(report);

        md.Should().Contain("_No summary generated._");
        md.Should().Contain("_No significant similarities identified._");
        md.Should().Contain("_No significant differences identified._");
        md.Should().Contain("_No direct contradictions detected._");
        md.Should().Contain("_No document-exclusive points identified._");
    }

    [Fact]
    public async Task ExportComparisonAsMarkdownAsync_EscapesMarkdownSpecialCharsInNames()
    {
        var name = @"re*port_[v2]`draft`#\end";
        var report = new ComparisonReport
        {
            DocumentNames = new List<string> { name },
            UniquePoints = new Dictionary<string, List<string>> { [name] = new() { "point" } },
        };
        var sut = NewHarness().Build();

        var md = await sut.ExportComparisonAsMarkdownAsync(report);

        md.Should().Contain(@"\*");
        md.Should().Contain(@"\_");
        md.Should().Contain(@"\[");
        md.Should().Contain(@"\]");
        md.Should().Contain(@"\`");
        md.Should().Contain(@"\#");
        md.Should().Contain(@"\\");
    }
}
