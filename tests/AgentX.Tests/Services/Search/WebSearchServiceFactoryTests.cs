using AgentX.Core.Services.Search;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Search;

public class WebSearchServiceFactoryTests
{
    [Fact]
    public void GetService_ReturnsCorrectProviderType()
    {
        var factory = new WebSearchServiceFactory(
            braveApiKey: "brave-key",
            serperApiKey: "serper-key",
            searxngUrl: "http://localhost:8080");

        var brave = factory.GetService(WebSearchProvider.Brave);
        brave.Should().BeOfType<BraveSearchService>();
        brave.IsConfigured.Should().BeTrue();

        var serper = factory.GetService(WebSearchProvider.Serper);
        serper.Should().BeOfType<SerperSearchService>();
        serper.IsConfigured.Should().BeTrue();

        var searxng = factory.GetService(WebSearchProvider.SearXng);
        searxng.Should().BeOfType<SearXngSearchService>();
        searxng.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void GetService_ThrowsForInvalidProvider()
    {
        var factory = new WebSearchServiceFactory(null, null, null);
        var act = () => factory.GetService((WebSearchProvider)999);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetConfiguredService_ReturnsPreferred_WhenConfigured()
    {
        var factory = new WebSearchServiceFactory(
            braveApiKey: "brave-key",
            serperApiKey: null,
            searxngUrl: null);

        var settings = new AppSettings { WebSearchProvider = WebSearchProvider.Brave };
        var service = factory.GetConfiguredService(settings);

        service.Should().BeOfType<BraveSearchService>();
        service.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void GetConfiguredService_FallsBackToFirstConfigured_WhenPreferredNotConfigured()
    {
        var factory = new WebSearchServiceFactory(
            braveApiKey: null,
            serperApiKey: "serper-key",
            searxngUrl: null);

        var settings = new AppSettings { WebSearchProvider = WebSearchProvider.Brave };
        var service = factory.GetConfiguredService(settings);

        // Brave is not configured, should fall back to Serper
        service.Should().BeOfType<SerperSearchService>();
        service.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void GetConfiguredService_ReturnsPreferred_WhenNoneConfigured()
    {
        var factory = new WebSearchServiceFactory(
            braveApiKey: null,
            serperApiKey: null,
            searxngUrl: null);

        var settings = new AppSettings { WebSearchProvider = WebSearchProvider.Brave };
        var service = factory.GetConfiguredService(settings);

        // Returns preferred provider even though not configured (will return empty results)
        service.Should().BeOfType<BraveSearchService>();
        service.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void ClearCache_DoesNotThrow()
    {
        var factory = new WebSearchServiceFactory(
            braveApiKey: "key",
            serperApiKey: null,
            searxngUrl: null);

        // Should not throw
        factory.ClearCache();
    }
}