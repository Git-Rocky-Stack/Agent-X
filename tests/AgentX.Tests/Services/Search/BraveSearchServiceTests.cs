using AgentX.Core.Services.Search;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Search;

public class BraveSearchServiceTests
{
    [Fact]
    public void BraveSearchService_IsConfigured_ReturnsFalse_WhenApiKeyIsNull()
    {
        var service = new BraveSearchService(apiKey: null);
        service.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void BraveSearchService_IsConfigured_ReturnsFalse_WhenApiKeyIsEmpty()
    {
        var service = new BraveSearchService(apiKey: "");
        service.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void BraveSearchService_IsConfigured_ReturnsFalse_WhenApiKeyIsWhitespace()
    {
        var service = new BraveSearchService(apiKey: "   ");
        service.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void BraveSearchService_IsConfigured_ReturnsTrue_WhenApiKeyIsSet()
    {
        var service = new BraveSearchService(apiKey: "test-api-key");
        service.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void BraveSearchService_ActiveProvider_IsBrave()
    {
        var service = new BraveSearchService(apiKey: "test");
        service.ActiveProvider.Should().Be(WebSearchProvider.Brave);
    }

    [Fact]
    public async Task BraveSearchService_ReturnsEmptyResponse_WhenNotConfigured()
    {
        var service = new BraveSearchService(apiKey: null);
        var response = await service.SearchAsync("test query");

        response.Results.Should().BeEmpty();
        response.SearchProvider.Should().Be(WebSearchProvider.Brave);
        response.FromCache.Should().BeFalse();
        response.SearchDuration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void SerperSearchService_IsConfigured_ReturnsFalse_WhenApiKeyIsNull()
    {
        var service = new SerperSearchService(apiKey: null);
        service.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void SerperSearchService_IsConfigured_ReturnsTrue_WhenApiKeyIsSet()
    {
        var service = new SerperSearchService(apiKey: "test-key");
        service.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void SerperSearchService_ActiveProvider_IsSerper()
    {
        var service = new SerperSearchService(apiKey: "test");
        service.ActiveProvider.Should().Be(WebSearchProvider.Serper);
    }

    [Fact]
    public async Task SerperSearchService_ReturnsEmptyResponse_WhenNotConfigured()
    {
        var service = new SerperSearchService(apiKey: null);
        var response = await service.SearchAsync("test query");

        response.Results.Should().BeEmpty();
        response.SearchProvider.Should().Be(WebSearchProvider.Serper);
    }

    [Fact]
    public void SearXngSearchService_IsConfigured_ReturnsFalse_WhenBaseUrlIsNull()
    {
        var service = new SearXngSearchService(baseUrl: null);
        service.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void SearXngSearchService_IsConfigured_ReturnsFalse_WhenBaseUrlIsEmpty()
    {
        var service = new SearXngSearchService(baseUrl: "");
        service.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void SearXngSearchService_IsConfigured_ReturnsTrue_WhenBaseUrlIsSet()
    {
        var service = new SearXngSearchService(baseUrl: "http://localhost:8080");
        service.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void SearXngSearchService_ActiveProvider_IsSearXng()
    {
        var service = new SearXngSearchService(baseUrl: "http://localhost:8080");
        service.ActiveProvider.Should().Be(WebSearchProvider.SearXng);
    }

    [Fact]
    public async Task SearXngSearchService_ReturnsEmptyResponse_WhenNotConfigured()
    {
        var service = new SearXngSearchService(baseUrl: null);
        var response = await service.SearchAsync("test query");

        response.Results.Should().BeEmpty();
        response.SearchProvider.Should().Be(WebSearchProvider.SearXng);
    }

    [Fact]
    public async Task SearchAsync_ThrowsOnNullQuery()
    {
        var service = new BraveSearchService(apiKey: "test");
        var act = async () => await service.SearchAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
