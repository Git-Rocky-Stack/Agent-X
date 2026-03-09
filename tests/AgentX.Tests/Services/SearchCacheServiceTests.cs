using AgentX.Core.Search.Models;
using AgentX.Core.Services.Search;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services;

/// <summary>
/// Unit tests for <see cref="SearchCacheService"/>.
/// Tests cover cache hits/misses, TTL expiration, LRU eviction,
/// invalidation, statistics tracking, thread safety, and disposal.
/// </summary>
public sealed class SearchCacheServiceTests : IDisposable
{
    private SearchCacheService? _sut;

    public void Dispose()
    {
        _sut?.Dispose();
    }

    /// <summary>Creates a default query used across tests.</summary>
    private static SearchQuery CreateQuery(string text = "test query", int topK = 10)
    {
        return new SearchQuery
        {
            QueryText = text,
            TopK = topK,
            MinScore = 0.3f,
            Mode = SearchMode.Semantic
        };
    }

    /// <summary>Creates a list of search results with the specified document IDs.</summary>
    private static IReadOnlyList<SearchResult> CreateResults(params long[] documentIds)
    {
        return documentIds.Select(id => new SearchResult
        {
            DocumentId = id,
            ChunkId = id * 10,
            FileName = $"doc_{id}.pdf",
            FilePath = $"/docs/doc_{id}.pdf",
            FileType = "pdf",
            MatchedText = $"Content for document {id}",
            Score = 0.85f
        }).ToList();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Cache miss
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TryGetCached_WhenCacheMiss_ReturnsNull()
    {
        // Arrange
        _sut = new SearchCacheService();
        var query = CreateQuery("uncached query");

        // Act
        var result = _sut.TryGetCached(query);

        // Assert
        result.Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Cache hit
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TryGetCached_WhenCacheHit_ReturnsCachedResults()
    {
        // Arrange
        _sut = new SearchCacheService();
        var query = CreateQuery("cached query");
        var expected = CreateResults(1, 2, 3);
        _sut.Cache(query, expected);

        // Act
        var result = _sut.TryGetCached(query);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void TryGetCached_SameQueryDifferentCase_ReturnsCachedResults()
    {
        // Arrange: the cache key normalizes to lowercase
        _sut = new SearchCacheService();
        var query1 = CreateQuery("Hello World");
        var query2 = CreateQuery("hello world");
        var expected = CreateResults(1);
        _sut.Cache(query1, expected);

        // Act
        var result = _sut.TryGetCached(query2);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void TryGetCached_DifferentQuery_ReturnsNull()
    {
        // Arrange
        _sut = new SearchCacheService();
        var query1 = CreateQuery("query one");
        var query2 = CreateQuery("query two");
        _sut.Cache(query1, CreateResults(1));

        // Act
        var result = _sut.TryGetCached(query2);

        // Assert
        result.Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  TTL expiration
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TryGetCached_WhenEntryExpired_ReturnsNull()
    {
        // Arrange: use an extremely short TTL so the entry expires immediately
        var shortTtl = TimeSpan.FromMilliseconds(1);
        _sut = new SearchCacheService(ttl: shortTtl);
        var query = CreateQuery("expiring query");
        _sut.Cache(query, CreateResults(1));

        // Wait long enough for the TTL to expire
        Thread.Sleep(50);

        // Act
        var result = _sut.TryGetCached(query);

        // Assert
        result.Should().BeNull("the entry should have expired based on its TTL");
    }

    [Fact]
    public void TryGetCached_WhenEntryNotExpired_ReturnsCachedResults()
    {
        // Arrange: use a long TTL to ensure the entry is still valid
        var longTtl = TimeSpan.FromMinutes(60);
        _sut = new SearchCacheService(ttl: longTtl);
        var query = CreateQuery("persistent query");
        var expected = CreateResults(42);
        _sut.Cache(query, expected);

        // Act
        var result = _sut.TryGetCached(query);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expected);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  LRU eviction
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Cache_WhenCapacityExceeded_EvictsLeastRecentlyUsed()
    {
        // Arrange: capacity of 2
        _sut = new SearchCacheService(maxEntries: 2);

        var query1 = CreateQuery("first");
        var query2 = CreateQuery("second");
        var query3 = CreateQuery("third");

        _sut.Cache(query1, CreateResults(1));
        _sut.Cache(query2, CreateResults(2));

        // Act: adding a third entry should evict the first (LRU)
        _sut.Cache(query3, CreateResults(3));

        // Assert
        _sut.TryGetCached(query1).Should().BeNull("query1 should have been evicted as the LRU entry");
        _sut.TryGetCached(query2).Should().NotBeNull("query2 should still be cached");
        _sut.TryGetCached(query3).Should().NotBeNull("query3 should be cached");
    }

    [Fact]
    public void Cache_WhenAccessedEntryIsRetained_LRUEvictsCorrectEntry()
    {
        // Arrange: capacity of 2
        _sut = new SearchCacheService(maxEntries: 2);

        var query1 = CreateQuery("first");
        var query2 = CreateQuery("second");
        var query3 = CreateQuery("third");

        _sut.Cache(query1, CreateResults(1));
        _sut.Cache(query2, CreateResults(2));

        // Access query1 to promote it to MRU (Most Recently Used)
        _sut.TryGetCached(query1);

        // Act: adding a third entry should evict query2 (now the LRU)
        _sut.Cache(query3, CreateResults(3));

        // Assert
        _sut.TryGetCached(query1).Should().NotBeNull("query1 was accessed and promoted to MRU");
        _sut.TryGetCached(query2).Should().BeNull("query2 should have been evicted as the LRU entry");
        _sut.TryGetCached(query3).Should().NotBeNull("query3 should be cached");
    }

    [Fact]
    public void Cache_OverwritesExistingEntryWithSameKey()
    {
        // Arrange
        _sut = new SearchCacheService();
        var query = CreateQuery("same key");
        var original = CreateResults(1);
        var updated = CreateResults(99);

        _sut.Cache(query, original);

        // Act
        _sut.Cache(query, updated);

        // Assert
        var result = _sut.TryGetCached(query);
        result.Should().NotBeNull();
        result!.First().DocumentId.Should().Be(99, "the entry should have been overwritten");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  InvalidateAll
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void InvalidateAll_ClearsEverything()
    {
        // Arrange
        _sut = new SearchCacheService();
        _sut.Cache(CreateQuery("a"), CreateResults(1));
        _sut.Cache(CreateQuery("b"), CreateResults(2));
        _sut.Cache(CreateQuery("c"), CreateResults(3));

        // Act
        _sut.InvalidateAll();

        // Assert
        _sut.TryGetCached(CreateQuery("a")).Should().BeNull();
        _sut.TryGetCached(CreateQuery("b")).Should().BeNull();
        _sut.TryGetCached(CreateQuery("c")).Should().BeNull();

        var stats = _sut.GetStatistics();
        stats.EntryCount.Should().Be(0);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  InvalidateForDocument
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void InvalidateForDocument_RemovesMatchingEntries()
    {
        // Arrange
        _sut = new SearchCacheService();
        var queryA = CreateQuery("query with doc 1");
        var queryB = CreateQuery("query with doc 2");
        var queryC = CreateQuery("query with doc 1 and 2");

        _sut.Cache(queryA, CreateResults(1));          // references document 1
        _sut.Cache(queryB, CreateResults(2));          // references document 2
        _sut.Cache(queryC, CreateResults(1, 2));       // references both

        // Act: invalidate for document 1
        _sut.InvalidateForDocument(1);

        // Assert
        _sut.TryGetCached(queryA).Should().BeNull("entry references document 1");
        _sut.TryGetCached(queryB).Should().NotBeNull("entry does not reference document 1");
        _sut.TryGetCached(queryC).Should().BeNull("entry references document 1 (among others)");
    }

    [Fact]
    public void InvalidateForDocument_WithNoMatchingEntries_DoesNothing()
    {
        // Arrange
        _sut = new SearchCacheService();
        var query = CreateQuery("test");
        _sut.Cache(query, CreateResults(5));

        // Act
        _sut.InvalidateForDocument(999); // document 999 is not in any cached result

        // Assert
        _sut.TryGetCached(query).Should().NotBeNull("no entries reference document 999");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Statistics
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetStatistics_TracksMissesCorrectly()
    {
        // Arrange
        _sut = new SearchCacheService();

        // Act: three misses
        _sut.TryGetCached(CreateQuery("miss 1"));
        _sut.TryGetCached(CreateQuery("miss 2"));
        _sut.TryGetCached(CreateQuery("miss 3"));

        // Assert
        var stats = _sut.GetStatistics();
        stats.MissCount.Should().Be(3);
        stats.HitCount.Should().Be(0);
        stats.HitRate.Should().Be(0.0);
        stats.EntryCount.Should().Be(0);
    }

    [Fact]
    public void GetStatistics_TracksHitsCorrectly()
    {
        // Arrange
        _sut = new SearchCacheService();
        var query = CreateQuery("cached");
        _sut.Cache(query, CreateResults(1));

        // Act: two hits
        _sut.TryGetCached(query);
        _sut.TryGetCached(query);

        // Assert
        var stats = _sut.GetStatistics();
        stats.HitCount.Should().Be(2);
        stats.MissCount.Should().Be(0);
        stats.HitRate.Should().Be(1.0);
    }

    [Fact]
    public void GetStatistics_TracksHitsAndMissesCorrectly()
    {
        // Arrange
        _sut = new SearchCacheService();
        var query = CreateQuery("exists");
        _sut.Cache(query, CreateResults(1));

        // Act: 1 hit + 1 miss
        _sut.TryGetCached(query);                         // hit
        _sut.TryGetCached(CreateQuery("does not exist")); // miss

        // Assert
        var stats = _sut.GetStatistics();
        stats.HitCount.Should().Be(1);
        stats.MissCount.Should().Be(1);
        stats.HitRate.Should().Be(0.5);
        stats.EntryCount.Should().Be(1);
    }

    [Fact]
    public void GetStatistics_WhenNoLookups_ReturnsZeroHitRate()
    {
        // Arrange
        _sut = new SearchCacheService();

        // Act
        var stats = _sut.GetStatistics();

        // Assert
        stats.HitCount.Should().Be(0);
        stats.MissCount.Should().Be(0);
        stats.HitRate.Should().Be(0.0);
        stats.EntryCount.Should().Be(0);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Thread safety
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ConcurrentAccess_DoesNotThrow()
    {
        // Arrange
        _sut = new SearchCacheService(maxEntries: 50);

        // Act: hammer the cache from multiple threads simultaneously
        var act = () =>
        {
            var tasks = new List<Task>();

            for (int i = 0; i < 100; i++)
            {
                var index = i;
                tasks.Add(Task.Run(() =>
                {
                    var query = CreateQuery($"thread query {index}");
                    var results = CreateResults(index);

                    _sut.Cache(query, results);
                    _sut.TryGetCached(query);
                    _sut.GetStatistics();

                    if (index % 10 == 0)
                    {
                        _sut.InvalidateForDocument(index);
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray());
        };

        // Assert: no exceptions should be thrown during concurrent access
        act.Should().NotThrow("the cache service must be thread-safe");
    }

    [Fact]
    public void ConcurrentAccess_WithInvalidateAll_DoesNotThrow()
    {
        // Arrange
        _sut = new SearchCacheService(maxEntries: 20);

        // Act
        var act = () =>
        {
            var tasks = new List<Task>();

            // Writers
            for (int i = 0; i < 50; i++)
            {
                var index = i;
                tasks.Add(Task.Run(() =>
                {
                    _sut.Cache(CreateQuery($"q{index}"), CreateResults(index));
                }));
            }

            // Readers
            for (int i = 0; i < 50; i++)
            {
                var index = i;
                tasks.Add(Task.Run(() =>
                {
                    _sut.TryGetCached(CreateQuery($"q{index}"));
                }));
            }

            // Invalidators
            tasks.Add(Task.Run(() => _sut.InvalidateAll()));
            tasks.Add(Task.Run(() => _sut.InvalidateForDocument(5)));

            Task.WaitAll(tasks.ToArray());
        };

        // Assert
        act.Should().NotThrow();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Dispose
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Dispose_PreventsFurtherOperations_TryGetCached()
    {
        // Arrange
        var service = new SearchCacheService();
        service.Dispose();

        // Act
        var act = () => service.TryGetCached(CreateQuery("after dispose"));

        // Assert
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_PreventsFurtherOperations_Cache()
    {
        // Arrange
        var service = new SearchCacheService();
        service.Dispose();

        // Act
        var act = () => service.Cache(CreateQuery("after dispose"), CreateResults(1));

        // Assert
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_PreventsFurtherOperations_InvalidateAll()
    {
        // Arrange
        var service = new SearchCacheService();
        service.Dispose();

        // Act
        var act = () => service.InvalidateAll();

        // Assert
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_PreventsFurtherOperations_InvalidateForDocument()
    {
        // Arrange
        var service = new SearchCacheService();
        service.Dispose();

        // Act
        var act = () => service.InvalidateForDocument(1);

        // Assert
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_PreventsFurtherOperations_GetStatistics()
    {
        // Arrange
        var service = new SearchCacheService();
        service.Dispose();

        // Act
        var act = () => service.GetStatistics();

        // Assert
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var service = new SearchCacheService();

        // Act
        var act = () =>
        {
            service.Dispose();
            service.Dispose();
            service.Dispose();
        };

        // Assert
        act.Should().NotThrow("Dispose should be idempotent");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Constructor validation
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_WithZeroMaxEntries_ThrowsArgumentOutOfRange()
    {
        // Act
        var act = () => new SearchCacheService(maxEntries: 0);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("maxEntries");
    }

    [Fact]
    public void Constructor_WithNegativeTtl_ThrowsArgumentOutOfRange()
    {
        // Act
        var act = () => new SearchCacheService(ttl: TimeSpan.FromSeconds(-1));

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("ttl");
    }

    [Fact]
    public void Constructor_WithZeroTtl_ThrowsArgumentOutOfRange()
    {
        // Act
        var act = () => new SearchCacheService(ttl: TimeSpan.Zero);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("ttl");
    }
}
