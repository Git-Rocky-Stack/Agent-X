using AgentX.Core.Services.Search;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Search;

public class WebSearchCacheTests
{
    [Fact]
    public void Get_ReturnsNull_WhenCacheIsEmpty()
    {
        var cache = new WebSearchCache();
        var result = cache.Get("test query", WebSearchProvider.Brave);

        result.Should().BeNull();
    }

    [Fact]
    public void Set_ThenGet_ReturnsCachedResponse()
    {
        var cache = new WebSearchCache();
        var response = new WebSearchResponse
        {
            Query = "test",
            Results = new List<WebSearchResult>
            {
                new() { Title = "Result 1", Url = "https://example.com" }
            },
            SearchProvider = WebSearchProvider.Brave,
            SearchDuration = TimeSpan.FromMilliseconds(100),
            FromCache = false
        };

        cache.Set("test", WebSearchProvider.Brave, response);
        var cached = cache.Get("test", WebSearchProvider.Brave);

        cached.Should().NotBeNull();
        cached!.Query.Should().Be("test");
        cached.Results.Should().HaveCount(1);
        cached.FromCache.Should().BeTrue(); // Cache marks responses as from-cache
    }

    [Fact]
    public void Get_ReturnsNull_WhenProviderDiffers()
    {
        var cache = new WebSearchCache();
        var response = new WebSearchResponse
        {
            Query = "test",
            SearchProvider = WebSearchProvider.Brave
        };

        cache.Set("test", WebSearchProvider.Brave, response);
        var cached = cache.Get("test", WebSearchProvider.Serper);

        cached.Should().BeNull();
    }

    [Fact]
    public void Get_ReturnsNull_WhenQueryDiffers()
    {
        var cache = new WebSearchCache();
        var response = new WebSearchResponse
        {
            Query = "test",
            SearchProvider = WebSearchProvider.Brave
        };

        cache.Set("test", WebSearchProvider.Brave, response);
        var cached = cache.Get("different query", WebSearchProvider.Brave);

        cached.Should().BeNull();
    }

    [Fact]
    public void Set_OverwritesExistingEntry()
    {
        var cache = new WebSearchCache();
        var response1 = new WebSearchResponse { Query = "test", SearchProvider = WebSearchProvider.Brave };
        var response2 = new WebSearchResponse
        {
            Query = "test",
            Results = new List<WebSearchResult> { new() { Title = "Updated" } },
            SearchProvider = WebSearchProvider.Brave
        };

        cache.Set("test", WebSearchProvider.Brave, response1);
        cache.Set("test", WebSearchProvider.Brave, response2);
        var cached = cache.Get("test", WebSearchProvider.Brave);

        cached.Should().NotBeNull();
        cached!.Results.Should().HaveCount(1);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var cache = new WebSearchCache();
        var response = new WebSearchResponse { Query = "test", SearchProvider = WebSearchProvider.Brave };

        cache.Set("test", WebSearchProvider.Brave, response);
        cache.Clear();
        var cached = cache.Get("test", WebSearchProvider.Brave);

        cached.Should().BeNull();
    }

    [Fact]
    public void Set_ThrowsOnNullQuery()
    {
        var cache = new WebSearchCache();
        var response = new WebSearchResponse { Query = "test", SearchProvider = WebSearchProvider.Brave };

        var act = () => cache.Set(null!, WebSearchProvider.Brave, response);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Get_ThrowsOnNullQuery()
    {
        var cache = new WebSearchCache();
        var act = () => cache.Get(null!, WebSearchProvider.Brave);
        act.Should().Throw<ArgumentNullException>();
    }
}