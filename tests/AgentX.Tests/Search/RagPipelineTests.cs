using System.Runtime.CompilerServices;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Configuration;
using AgentX.Core.Data;
using AgentX.Core.Observability;
using AgentX.Core.Search;
using AgentX.Core.Search.Models;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Search;

/// <summary>
/// FU-6: integration tests covering the RAG pipeline behaviors added across
/// the audit waves — HyDE gating, PII redaction, search-mode routing, eval
/// sample-rate gating, multi-block system prompt selection, and fail-open
/// behavior on optional service exceptions.
///
/// These tests mock all dependencies (`IHybridSearchOrchestrator`, `IAiService`,
/// `ICitationService`, `IRagReranker`, optional services) and assert on the
/// observable orchestration: which dependencies were called, with what
/// arguments, and what response was produced.
/// </summary>
public sealed class RagPipelineTests
{
    private readonly Mock<IHybridSearchOrchestrator> _searchOrchestrator = new();
    private readonly Mock<IAiService> _aiService = new();
    private readonly Mock<ICitationService> _citationService = new();
    private readonly Mock<IRagReranker> _reranker = new();
    private readonly Mock<AgentXDbContext> _db = new();
    private readonly Mock<IRagConfiguration> _config = new();
    private readonly ILogger _logger = new LoggerConfiguration().CreateLogger();

    public RagPipelineTests()
    {
        // Sensible defaults — individual tests override as needed.
        _config.Setup(c => c.DefaultTopK).Returns(8);
        _config.Setup(c => c.DefaultMinScore).Returns(0.25f);
        _config.Setup(c => c.DefaultSearchMode).Returns("Hybrid");
        _config.Setup(c => c.EnableHyde).Returns(false);
        _config.Setup(c => c.HydeMinQueryLength).Returns(80);
        _config.Setup(c => c.EnablePiiRedaction).Returns(false);
        _config.Setup(c => c.PiiRedactionMask).Returns("***");
        _config.Setup(c => c.EvalSampleRate).Returns(0.0); // disabled by default in tests

        // Default reranker just returns input unchanged so tests can focus on
        // orchestration without re-implementing rerank semantics.
        _reranker
            .Setup(r => r.Rerank(It.IsAny<List<RagContextChunk>>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns<List<RagContextChunk>, string, int>((chunks, _, _) => chunks);

        _citationService
            .Setup(c => c.ExtractCitations(It.IsAny<string>(), It.IsAny<IReadOnlyList<RagContextChunk>>()))
            .Returns(new List<Citation>());
    }

    private RagPipeline BuildPipeline(
        IMultiQueryGenerator? multiQuery = null,
        IHydeService? hyde = null,
        ILlmReranker? llmReranker = null,
        IParentDocumentRetriever? parentRetriever = null,
        IContextualCompressor? compressor = null,
        IRagEvaluator? evaluator = null,
        IPiiDetector? piiDetector = null)
    {
        return new RagPipeline(
            _searchOrchestrator.Object,
            _aiService.Object,
            _citationService.Object,
            _reranker.Object,
            _db.Object,
            _logger,
            _config.Object,
            multiQueryGenerator: multiQuery,
            hydeService: hyde,
            llmReranker: llmReranker,
            parentRetriever: parentRetriever,
            compressor: compressor,
            evaluator: evaluator,
            piiDetector: piiDetector);
    }

    private void SetupSearchReturns(params SearchResult[] results)
    {
        _searchOrchestrator
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(results.ToList());
    }

    private void SetupAiStreamReturns(string answer)
    {
        _aiService
            .Setup(a => a.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<ChatMessage> _, string? _, ChatOptions? _, CancellationToken ct) =>
                ToAsyncEnumerable(answer, ct));
    }

    private static async IAsyncEnumerable<string> ToAsyncEnumerable(
        string text,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var ch in text)
        {
            ct.ThrowIfCancellationRequested();
            yield return ch.ToString();
            await Task.Yield();
        }
    }

    private static SearchResult MakeResult(long id, float score = 0.9f, string text = "context text") => new()
    {
        ChunkId = id,
        DocumentId = id * 10,
        FileName = $"doc{id}.txt",
        FilePath = $"/path/doc{id}.txt",
        FileType = "txt",
        ChunkIndex = 0,
        MatchedText = text,
        Excerpt = text,
        Score = score,
        CollectionNames = new List<string>()
    };

    // ════════════════════════════════════════════════════════════════════
    //  HyDE gating
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AskAsync_HydeDisabled_DoesNotInvokeHyde()
    {
        _config.Setup(c => c.EnableHyde).Returns(false);
        var hyde = new Mock<IHydeService>();
        SetupSearchReturns(MakeResult(1));
        SetupAiStreamReturns("answer");

        var pipeline = BuildPipeline(hyde: hyde.Object);

        // Long question — would clear the length gate if HyDE were enabled.
        var longQuestion = new string('x', 200);
        await pipeline.AskAsync(longQuestion);

        hyde.Verify(h => h.GenerateHypotheticalDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AskAsync_HydeEnabledShortQuestion_DoesNotInvokeHyde()
    {
        _config.Setup(c => c.EnableHyde).Returns(true);
        _config.Setup(c => c.HydeMinQueryLength).Returns(100);
        var hyde = new Mock<IHydeService>();
        SetupSearchReturns(MakeResult(1));
        SetupAiStreamReturns("answer");

        var pipeline = BuildPipeline(hyde: hyde.Object);

        await pipeline.AskAsync("short question");

        hyde.Verify(h => h.GenerateHypotheticalDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AskAsync_HydeEnabledLongQuestion_InvokesHydeAndAddsAsQuery()
    {
        _config.Setup(c => c.EnableHyde).Returns(true);
        _config.Setup(c => c.HydeMinQueryLength).Returns(20);
        var hyde = new Mock<IHydeService>();
        const string hypotheticalDoc = "Paris is the capital city of France, located on the Seine.";
        hyde.Setup(h => h.GenerateHypotheticalDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hypotheticalDoc);

        // Wave 3b: capture the actual SearchQuery.QueryText values seen by the
        // orchestrator so we can assert the HyDE doc was added as a query, not
        // just count calls. The previous Times.Exactly(2) check verified the
        // call-count but didn't catch a hypothetical regression where the HyDE
        // doc was generated but never wired into the search loop.
        var seenQueries = new List<string>();
        _searchOrchestrator
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .Callback<SearchQuery, CancellationToken>((q, _) => seenQueries.Add(q.QueryText))
            .ReturnsAsync(new List<SearchResult> { MakeResult(1) });

        SetupAiStreamReturns("answer");

        var pipeline = BuildPipeline(hyde: hyde.Object);

        var longQuestion = "This is a long question that exceeds the HyDE minimum length";
        await pipeline.AskAsync(longQuestion);

        hyde.Verify(h => h.GenerateHypotheticalDocumentAsync(longQuestion, It.IsAny<CancellationToken>()),
            Times.Once);

        // Search fires twice: once with the original question, once with the HyDE doc.
        seenQueries.Should().HaveCount(2);
        seenQueries.Should().Contain(longQuestion, "the original question is always searched");
        seenQueries.Should().Contain(hypotheticalDoc,
            "the hypothetical document text must be added as a second search query — that's the whole point of HyDE");
    }

    [Fact]
    public async Task AskAsync_HydeThrows_FailsOpenAndContinues()
    {
        _config.Setup(c => c.EnableHyde).Returns(true);
        _config.Setup(c => c.HydeMinQueryLength).Returns(0);
        var hyde = new Mock<IHydeService>();
        hyde.Setup(h => h.GenerateHypotheticalDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hyde down"));

        SetupSearchReturns(MakeResult(1));
        SetupAiStreamReturns("answer");

        var pipeline = BuildPipeline(hyde: hyde.Object);

        var response = await pipeline.AskAsync("any question");

        response.AnswerText.Should().Be("answer");
        // Search still fires once with the original query despite HyDE failure.
        _searchOrchestrator.Verify(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ════════════════════════════════════════════════════════════════════
    //  PII redaction
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AskAsync_PiiEnabled_RedactsContextBeforeLlmCall()
    {
        _config.Setup(c => c.EnablePiiRedaction).Returns(true);
        _config.Setup(c => c.PiiRedactionMask).Returns("###");

        var pii = new Mock<IPiiDetector>();
        pii.Setup(p => p.ContainsPii(It.IsAny<string>())).Returns(true);
        pii.Setup(p => p.RedactPii(It.IsAny<string>(), "###")).Returns("[REDACTED]");

        SetupSearchReturns(MakeResult(1, text: "user@example.com"));
        SetupAiStreamReturns("answer");

        var pipeline = BuildPipeline(piiDetector: pii.Object);

        await pipeline.AskAsync("question");

        pii.Verify(p => p.ContainsPii(It.IsAny<string>()), Times.AtLeastOnce);
        pii.Verify(p => p.RedactPii(It.IsAny<string>(), "###"), Times.AtLeastOnce);
    }

    [Fact]
    public async Task AskAsync_PiiDisabled_DoesNotInvokeDetector()
    {
        _config.Setup(c => c.EnablePiiRedaction).Returns(false);

        var pii = new Mock<IPiiDetector>();
        SetupSearchReturns(MakeResult(1));
        SetupAiStreamReturns("answer");

        var pipeline = BuildPipeline(piiDetector: pii.Object);

        await pipeline.AskAsync("question");

        pii.Verify(p => p.ContainsPii(It.IsAny<string>()), Times.Never);
        pii.Verify(p => p.RedactPii(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Search-mode routing
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AskAsync_SemanticSearchMode_RoutesToSemantic()
    {
        _config.Setup(c => c.DefaultSearchMode).Returns("Semantic");
        SetupSearchReturns(MakeResult(1));
        SetupAiStreamReturns("answer");

        var pipeline = BuildPipeline();
        await pipeline.AskAsync("question");

        _searchOrchestrator.Verify(
            s => s.SearchAsync(It.Is<SearchQuery>(q => q.Mode == SearchMode.Semantic), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task AskAsync_KeywordSearchMode_RoutesToKeyword()
    {
        _config.Setup(c => c.DefaultSearchMode).Returns("Keyword");
        SetupSearchReturns(MakeResult(1));
        SetupAiStreamReturns("answer");

        var pipeline = BuildPipeline();
        await pipeline.AskAsync("question");

        _searchOrchestrator.Verify(
            s => s.SearchAsync(It.Is<SearchQuery>(q => q.Mode == SearchMode.Keyword), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task AskAsync_InvalidSearchMode_FallsBackToHybrid()
    {
        _config.Setup(c => c.DefaultSearchMode).Returns("Quantum");
        SetupSearchReturns(MakeResult(1));
        SetupAiStreamReturns("answer");

        var pipeline = BuildPipeline();
        await pipeline.AskAsync("question");

        _searchOrchestrator.Verify(
            s => s.SearchAsync(It.Is<SearchQuery>(q => q.Mode == SearchMode.Hybrid), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Eval sample-rate gating
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AskAsync_EvalSampleRateZero_DoesNotInvokeEvaluator()
    {
        _config.Setup(c => c.EvalSampleRate).Returns(0.0);
        var evaluator = new Mock<IRagEvaluator>();

        SetupSearchReturns(MakeResult(1));
        SetupAiStreamReturns("answer");

        var pipeline = BuildPipeline(evaluator: evaluator.Object);
        await pipeline.AskAsync("question");

        // Give the fire-and-forget Task.Run a chance — should be a no-op anyway.
        await Task.Delay(50);

        evaluator.Verify(e => e.EvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<RagContextChunk>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AskAsync_EvalSampleRateOne_InvokesEvaluator()
    {
        _config.Setup(c => c.EvalSampleRate).Returns(1.0);
        var evaluator = new Mock<IRagEvaluator>();
        evaluator
            .Setup(e => e.EvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<RagContextChunk>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagEvalMetrics { ContextRelevance = 0.9 });

        SetupSearchReturns(MakeResult(1));
        SetupAiStreamReturns("answer");

        var pipeline = BuildPipeline(evaluator: evaluator.Object);
        await pipeline.AskAsync("question");

        // Eval is fire-and-forget — wait briefly for the background task.
        await Task.Delay(200);

        evaluator.Verify(e => e.EvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<RagContextChunk>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Multi-block system prompt
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AskAsync_AlwaysSetsSystemPromptBlocks()
    {
        SetupSearchReturns(MakeResult(1));
        SetupAiStreamReturns("answer");

        ChatOptions? capturedOptions = null;
        _aiService
            .Setup(a => a.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<ChatMessage> _, string? _, ChatOptions? opts, CancellationToken ct) =>
            {
                capturedOptions = opts;
                return ToAsyncEnumerable("answer", ct);
            });

        var pipeline = BuildPipeline();
        await pipeline.AskAsync("question");

        capturedOptions.Should().NotBeNull();
        capturedOptions!.SystemPromptBlocks.Should().NotBeNull();
        capturedOptions.SystemPromptBlocks!.Should().HaveCount(2,
            "the RAG pipeline emits one cacheable static prefix block and one non-cacheable context block");

        capturedOptions.SystemPromptBlocks![0].Cacheable.Should().BeTrue("the static instruction prefix is cacheable");
        capturedOptions.SystemPromptBlocks![1].Cacheable.Should().BeFalse("the per-question context is not cacheable");
    }

    // ════════════════════════════════════════════════════════════════════
    //  No-results path
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AskAsync_NoSearchResults_ReturnsNoResultsMessageAndDoesNotCallLlm()
    {
        SetupSearchReturns(); // empty
        var pipeline = BuildPipeline();

        var response = await pipeline.AskAsync("question");

        response.AnswerText.Should().Contain("couldn't find any relevant information");
        response.ContextChunksUsed.Should().Be(0);
        response.Citations.Should().BeEmpty();
        _aiService.Verify(a => a.StreamChatAsync(
            It.IsAny<IReadOnlyList<ChatMessage>>(),
            It.IsAny<string>(),
            It.IsAny<ChatOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Fail-open on optional services
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AskAsync_MultiQueryThrows_FailsOpenWithOriginalQueryOnly()
    {
        var multiQuery = new Mock<IMultiQueryGenerator>();
        multiQuery
            .Setup(m => m.GenerateQueryVariationsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("model down"));

        SetupSearchReturns(MakeResult(1));
        SetupAiStreamReturns("answer");

        var pipeline = BuildPipeline(multiQuery: multiQuery.Object);

        var response = await pipeline.AskAsync("question");

        response.AnswerText.Should().Be("answer");
        // Pipeline still completes successfully despite the multi-query failure.
        _searchOrchestrator.Verify(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AskAsync_CompressorThrows_FailsOpenAndUsesUncompressedChunks()
    {
        var compressor = new Mock<IContextualCompressor>();
        compressor
            .Setup(c => c.CompressAsync(It.IsAny<List<RagContextChunk>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("compressor down"));

        SetupSearchReturns(MakeResult(1));
        SetupAiStreamReturns("answer");

        var pipeline = BuildPipeline(compressor: compressor.Object);

        var response = await pipeline.AskAsync("question");

        response.AnswerText.Should().Be("answer");
        response.ContextChunksUsed.Should().BeGreaterThan(0,
            "the pipeline should fall back to uncompressed chunks rather than dropping them");
    }
}
