using AgentX.Core.Configuration;
using AgentX.Core.Search;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Search;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Search;

public sealed class HybridSearchOrchestratorTests
{
    private readonly Mock<ISemanticSearchService> _semanticSearch = new();
    private readonly Mock<IKeywordSearchService> _keywordSearch = new();
    private readonly Mock<ISearchCacheService> _cacheService = new();
    private readonly HybridSearchOrchestrator _orchestrator;

    public HybridSearchOrchestratorTests()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        _orchestrator = new HybridSearchOrchestrator(
            _semanticSearch.Object,
            _keywordSearch.Object,
            logger,
            _cacheService.Object);
    }

    [Fact]
    public async Task SearchAsync_SemanticMode_DelegatesToSemanticSearch()
    {
        // Arrange
        var query = new SearchQuery
        {
            Mode = SearchMode.Semantic,
            QueryText = "test query",
            TopK = 10
        };

        var expectedResults = new List<SearchResult>
        {
            new() { ChunkId = 1, Score = 0.9f, FileName = "doc1.txt" }
        };

        _semanticSearch
            .Setup(s => s.SearchAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResults);

        // Act
        var result = await _orchestrator.SearchAsync(query);

        // Assert
        result.Should().HaveCount(1);
        result[0].ChunkId.Should().Be(1);
        _semanticSearch.Verify(s => s.SearchAsync(query, It.IsAny<CancellationToken>()), Times.Once);
        _keywordSearch.Verify(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_KeywordMode_DelegatesToKeywordSearch()
    {
        // Arrange
        var query = new SearchQuery
        {
            Mode = SearchMode.Keyword,
            QueryText = "test query",
            TopK = 10
        };

        var expectedResults = new List<SearchResult>
        {
            new() { ChunkId = 2, Score = 0.8f, FileName = "doc2.txt" }
        };

        _keywordSearch
            .Setup(s => s.SearchAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResults);

        // Act
        var result = await _orchestrator.SearchAsync(query);

        // Assert
        result.Should().HaveCount(1);
        result[0].ChunkId.Should().Be(2);
        _keywordSearch.Verify(s => s.SearchAsync(query, It.IsAny<CancellationToken>()), Times.Once);
        _semanticSearch.Verify(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_HybridMode_MergesResultsWithRRF()
    {
        // Arrange
        var query = new SearchQuery
        {
            Mode = SearchMode.Hybrid,
            QueryText = "test query",
            TopK = 5
        };

        var semanticResults = new List<SearchResult>
        {
            new() { ChunkId = 1, Score = 0.9f },
            new() { ChunkId = 3, Score = 0.7f },
            new() { ChunkId = 5, Score = 0.5f }
        };

        var keywordResults = new List<SearchResult>
        {
            new() { ChunkId = 2, Score = 0.8f },
            new() { ChunkId = 3, Score = 0.6f }, // Overlap with semantic
            new() { ChunkId = 4, Score = 0.4f }
        };

        _semanticSearch
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(semanticResults);

        _keywordSearch
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(keywordResults);

        // Act
        var result = await _orchestrator.SearchAsync(query);

        // Assert
        // Chunk 3 appears in both, should get higher RRF score
        result.Should().HaveCount(5); // TopK = 5
        result[0].ChunkId.Should().Be(3); // Highest RRF (appears in both at rank 2)
        result[0].Score.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SearchAsync_CacheHit_ReturnsCachedResults()
    {
        // Arrange
        var query = new SearchQuery
        {
            Mode = SearchMode.Semantic,
            QueryText = "cached query",
            TopK = 10
        };

        var cachedResults = new List<SearchResult>
        {
            new() { ChunkId = 1, Score = 0.9f }
        };

        _cacheService
            .Setup(c => c.TryGetCached(query))
            .Returns(cachedResults);

        // Act
        var result = await _orchestrator.SearchAsync(query);

        // Assert
        result.Should().BeSameAs(cachedResults);
        _semanticSearch.Verify(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()), Times.Never);
        _keywordSearch.Verify(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_CacheMiss_StoresResultsInCache()
    {
        // Arrange
        var query = new SearchQuery
        {
            Mode = SearchMode.Semantic,
            QueryText = "uncached query",
            TopK = 10
        };

        var results = new List<SearchResult>
        {
            new() { ChunkId = 1, Score = 0.9f }
        };

        _cacheService.Setup(c => c.TryGetCached(query)).Returns((IReadOnlyList<SearchResult>?)null);
        _semanticSearch
            .Setup(s => s.SearchAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(results);

        // Act
        var result = await _orchestrator.SearchAsync(query);

        // Assert
        _cacheService.Verify(c => c.Cache(query, It.Is<IReadOnlyList<SearchResult>>(r => r.Count == 1)), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_HybridMode_ParallelExecution()
    {
        // Verifies that the orchestrator launches BOTH backend searches before awaiting either.
        // Each mocked backend signals that it has started, then awaits the OTHER backend's
        // start signal before returning. If launches are parallel, both signals fire and both
        // mocks complete. If launches are sequential (e.g. someone changes the production code
        // to `await semantic; await keyword;`), the second mock never starts and the first
        // mock's await deadlocks — surfaced as a 5-second test timeout.
        //
        // The original test version used `ReturnsAsync(Func<T>)` to defer lambda execution,
        // but Moq invokes that Func eagerly (synchronously when the mocked method is called),
        // so on a single thread the assertions inside the Func ran before the second backend
        // had a chance to set its flag — the test failed deterministically rather than flakily.

        // Arrange
        var query = new SearchQuery
        {
            Mode = SearchMode.Hybrid,
            QueryText = "test",
            TopK = 5
        };

        var semanticResults = new List<SearchResult> { new() { ChunkId = 1, Score = 0.9f } };
        var keywordResults = new List<SearchResult> { new() { ChunkId = 2, Score = 0.8f } };

        // RunContinuationsAsynchronously prevents the SetResult call from inline-running the
        // continuation on the calling thread, which would deadlock the test.
        var semanticStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var keywordStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _semanticSearch
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                semanticStarted.SetResult(true);
                await keywordStarted.Task;
                return (IReadOnlyList<SearchResult>)semanticResults;
            });

        _keywordSearch
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                keywordStarted.SetResult(true);
                await semanticStarted.Task;
                return (IReadOnlyList<SearchResult>)keywordResults;
            });

        // Act — race against a 5-second timeout to catch a regression to sequential launch.
        var searchTask = _orchestrator.SearchAsync(query);
        var winner = await Task.WhenAny(searchTask, Task.Delay(TimeSpan.FromSeconds(5)));

        // Assert
        winner.Should().Be(searchTask, "the orchestrator must launch both backends in parallel");
        await searchTask; // surface any inner exception
        _semanticSearch.Verify(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()), Times.Once);
        _keywordSearch.Verify(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_HybridMode_SemanticFails_FallsBackToKeyword()
    {
        // Arrange
        var query = new SearchQuery
        {
            Mode = SearchMode.Hybrid,
            QueryText = "test",
            TopK = 5
        };

        var keywordResults = new List<SearchResult>
        {
            new() { ChunkId = 2, Score = 0.8f },
            new() { ChunkId = 3, Score = 0.6f }
        };

        _semanticSearch
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Semantic search failed"));

        _keywordSearch
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(keywordResults);

        // Act
        var result = await _orchestrator.SearchAsync(query);

        // Assert
        result.Should().HaveCount(2);
        result[0].ChunkId.Should().Be(2);
        result[1].ChunkId.Should().Be(3);
    }

    [Fact]
    public async Task SearchAsync_HybridMode_KeywordFails_FallsBackToSemantic()
    {
        // Arrange
        var query = new SearchQuery
        {
            Mode = SearchMode.Hybrid,
            QueryText = "test",
            TopK = 5
        };

        var semanticResults = new List<SearchResult>
        {
            new() { ChunkId = 1, Score = 0.9f },
            new() { ChunkId = 3, Score = 0.7f }
        };

        _semanticSearch
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(semanticResults);

        _keywordSearch
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Keyword search failed"));

        // Act
        var result = await _orchestrator.SearchAsync(query);

        // Assert
        result.Should().HaveCount(2);
        result[0].ChunkId.Should().Be(1);
        result[1].ChunkId.Should().Be(3);
    }

    [Fact]
    public async Task SearchAsync_HybridMode_BothFail_ReturnsEmpty()
    {
        // Arrange
        var query = new SearchQuery
        {
            Mode = SearchMode.Hybrid,
            QueryText = "test",
            TopK = 5
        };

        _semanticSearch
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Semantic failed"));

        _keywordSearch
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Keyword failed"));

        // Act
        var result = await _orchestrator.SearchAsync(query);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_HybridMode_RRFScores_NormalizedToZeroToOne()
    {
        // Arrange
        var query = new SearchQuery
        {
            Mode = SearchMode.Hybrid,
            QueryText = "test",
            TopK = 10
        };

        var semanticResults = Enumerable.Range(1, 100)
            .Select(i => new SearchResult { ChunkId = i, Score = 0.9f })
            .ToList();

        var keywordResults = Enumerable.Range(101, 100)
            .Select(i => new SearchResult { ChunkId = i, Score = 0.8f })
            .ToList();

        _semanticSearch
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(semanticResults);

        _keywordSearch
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(keywordResults);

        // Act
        var result = await _orchestrator.SearchAsync(query);

        // Assert
        result.Should().NotBeEmpty();
        foreach (var item in result)
        {
            item.Score.Should().BeGreaterThanOrEqualTo(0.0f);
            item.Score.Should().BeLessThanOrEqualTo(1.0f);
        }
    }

    [Fact]
    public async Task SearchAsync_HybridMode_RespectsTopK()
    {
        // Arrange
        var query = new SearchQuery
        {
            Mode = SearchMode.Hybrid,
            QueryText = "test",
            TopK = 3
        };

        var semanticResults = Enumerable.Range(1, 20)
            .Select(i => new SearchResult { ChunkId = i, Score = 0.9f })
            .ToList();

        var keywordResults = Enumerable.Range(21, 20)
            .Select(i => new SearchResult { ChunkId = i, Score = 0.8f })
            .ToList();

        _semanticSearch
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(semanticResults);

        _keywordSearch
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(keywordResults);

        // Act
        var result = await _orchestrator.SearchAsync(query);

        // Assert
        result.Should().HaveCount(3); // TopK = 3
    }

    [Fact]
    public async Task SearchAsync_NullQuery_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _orchestrator.SearchAsync(null!));
    }

    [Fact]
    public async Task SearchAsync_UnknownMode_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var query = new SearchQuery
        {
            Mode = (SearchMode)999, // Invalid mode
            QueryText = "test",
            TopK = 10
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _orchestrator.SearchAsync(query));
    }

    [Fact]
    public async Task SearchAsync_HybridMode_DeduplicatesByChunkId()
    {
        // Arrange
        var query = new SearchQuery
        {
            Mode = SearchMode.Hybrid,
            QueryText = "test",
            TopK = 10
        };

        var semanticResults = new List<SearchResult>
        {
            new() { ChunkId = 1, Score = 0.9f, FileName = "semantic.txt" },
            new() { ChunkId = 2, Score = 0.8f, FileName = "semantic.txt" }
        };

        var keywordResults = new List<SearchResult>
        {
            new() { ChunkId = 1, Score = 0.7f, FileName = "keyword.txt" }, // Same ChunkId
            new() { ChunkId = 3, Score = 0.6f, FileName = "keyword.txt" }
        };

        _semanticSearch
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(semanticResults);

        _keywordSearch
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(keywordResults);

        // Act
        var result = await _orchestrator.SearchAsync(query);

        // Assert
        result.Should().HaveCount(3); // 3 unique chunk IDs (1, 2, 3)

        // Chunk 1 should have combined RRF score (higher than others)
        var chunk1Result = result.FirstOrDefault(r => r.ChunkId == 1);
        chunk1Result.Should().NotBeNull();
        chunk1Result!.Score.Should().BeGreaterThan(result.First(r => r.ChunkId == 2).Score);
    }

    [Fact]
    public async Task SearchAsync_WithFilters_PassesFiltersToBackends()
    {
        // Arrange
        var query = new SearchQuery
        {
            Mode = SearchMode.Semantic,
            QueryText = "test",
            TopK = 10,
            CollectionId = 123,
            FileTypeFilter = "pdf",
            CreatedAfter = DateTime.UtcNow.AddDays(-7)
        };

        var expectedResults = new List<SearchResult>
        {
            new() { ChunkId = 1, Score = 0.9f }
        };

        SearchQuery? capturedQuery = null;
        _semanticSearch
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .Callback<SearchQuery, CancellationToken>((q, _) => capturedQuery = q)
            .ReturnsAsync(expectedResults);

        // Act
        await _orchestrator.SearchAsync(query);

        // Assert
        capturedQuery.Should().NotBeNull();
        capturedQuery!.CollectionId.Should().Be(123);
        capturedQuery.FileTypeFilter.Should().Be("pdf");
        capturedQuery.CreatedAfter.Should().BeCloseTo(query.CreatedAfter.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SearchAsync_HybridMode_RequestsMoreResultsFromBackends()
    {
        // Arrange
        var query = new SearchQuery
        {
            Mode = SearchMode.Hybrid,
            QueryText = "test",
            TopK = 5
        };

        SearchQuery? semanticQuery = null;
        SearchQuery? keywordQuery = null;

        _semanticSearch
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .Callback<SearchQuery, CancellationToken>((q, _) => semanticQuery = q)
            .ReturnsAsync(Array.Empty<SearchResult>());

        _keywordSearch
            .Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .Callback<SearchQuery, CancellationToken>((q, _) => keywordQuery = q)
            .ReturnsAsync(Array.Empty<SearchResult>());

        // Act
        await _orchestrator.SearchAsync(query);

        // Assert
        // Hybrid mode requests TopK * 3 for better RRF results
        semanticQuery!.TopK.Should().Be(15); // 5 * 3
        keywordQuery!.TopK.Should().Be(15);
        semanticQuery.MinScore.Should().Be(0.0f); // No pre-filtering for RRF
        keywordQuery.MinScore.Should().Be(0.0f);
    }
}
