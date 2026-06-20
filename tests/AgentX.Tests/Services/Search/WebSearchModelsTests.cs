using AgentX.Core.Services.Search;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Search;

public class WebSearchModelsTests
{
    [Fact]
    public void WebSearchProvider_Enum_HasExpectedValues()
    {
        // Assert all three providers exist in the enum
        ((int)WebSearchProvider.Brave).Should().Be(0);
        ((int)WebSearchProvider.Serper).Should().Be(1);
        ((int)WebSearchProvider.SearXng).Should().Be(2);

        Enum.GetNames<WebSearchProvider>().Should().Contain(["Brave", "Serper", "SearXng"]);
    }

    [Fact]
    public void WebSearchResult_DefaultValues_AreEmptyStrings()
    {
        var result = new WebSearchResult();

        result.Title.Should().BeEmpty();
        result.Url.Should().BeEmpty();
        result.Snippet.Should().BeEmpty();
        result.SourceDomain.Should().BeEmpty();
        result.PublishedDate.Should().BeNull();
        result.RawContent.Should().BeNull();
    }

    [Fact]
    public void WebSearchResult_InitValues_AreSetCorrectly()
    {
        var publishedDate = new DateTime(2026, 4, 14, 0, 0, 0, DateTimeKind.Utc);
        var result = new WebSearchResult
        {
            Title = "Test Title",
            Url = "https://example.com",
            Snippet = "Test snippet",
            SourceDomain = "example.com",
            PublishedDate = publishedDate,
            RawContent = "Full content"
        };

        result.Title.Should().Be("Test Title");
        result.Url.Should().Be("https://example.com");
        result.Snippet.Should().Be("Test snippet");
        result.SourceDomain.Should().Be("example.com");
        result.PublishedDate.Should().Be(publishedDate);
        result.RawContent.Should().Be("Full content");
    }

    [Fact]
    public void WebSearchResponse_DefaultValues_AreCorrect()
    {
        var response = new WebSearchResponse();

        response.Query.Should().BeEmpty();
        response.Results.Should().BeEmpty();
        response.SearchProvider.Should().Be(WebSearchProvider.Brave); // default enum value
        response.SearchDuration.Should().Be(TimeSpan.Zero);
        response.FromCache.Should().BeFalse();
    }

    [Fact]
    public void WebSearchResponse_WithResults_IsCorrectlyConstructed()
    {
        var results = new List<WebSearchResult>
        {
            new() { Title = "Result 1", Url = "https://example.com/1" },
            new() { Title = "Result 2", Url = "https://example.com/2" }
        };

        var duration = TimeSpan.FromMilliseconds(250);
        var response = new WebSearchResponse
        {
            Query = "test query",
            Results = results,
            SearchProvider = WebSearchProvider.Serper,
            SearchDuration = duration,
            FromCache = true
        };

        response.Query.Should().Be("test query");
        response.Results.Should().HaveCount(2);
        response.SearchProvider.Should().Be(WebSearchProvider.Serper);
        response.SearchDuration.Should().Be(duration);
        response.FromCache.Should().BeTrue();
    }
}
