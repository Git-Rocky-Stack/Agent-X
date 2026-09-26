using System.Threading;
using System.Threading.Tasks;
using AgentX.Core.Services.Privacy;
using AgentX.Core.Services.Search;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.Services.Privacy;

/// <summary>
/// AX-QA-008: the dashboard's "your data never leaves this machine — no cloud, no exceptions" claim
/// must become state-aware. These tests pin the evaluation that drives it: every cloud/third-party
/// surface the product actually exposes (cloud AI provider, cloud-routing, web search, calendar and
/// email connectors) must flip the status off "fully local" and add an accurate disclosure, while a
/// genuinely local configuration must remain fully local.
/// </summary>
public class PrivacyStatusServiceTests
{
    private static PrivacyStatusService CreateService()
        => new(Mock.Of<ISettingsService>());

    [Fact]
    public void Default_settings_are_fully_local()
    {
        var status = CreateService().Evaluate(new AppSettings());

        status.IsFullyLocal.Should().BeTrue();
        status.Disclosures.Should().BeEmpty();
    }

    [Fact]
    public void Ollama_provider_is_local()
    {
        var status = CreateService().Evaluate(new AppSettings { ActiveProviderId = "ollama" });

        status.IsFullyLocal.Should().BeTrue();
    }

    [Theory]
    [InlineData("openai", "OpenAI")]
    [InlineData("anthropic", "Anthropic")]
    public void Cloud_ai_provider_is_disclosed(string providerId, string expectedName)
    {
        var status = CreateService().Evaluate(new AppSettings { ActiveProviderId = providerId });

        status.IsFullyLocal.Should().BeFalse();
        status.Disclosures.Should().ContainSingle(d => d.Surface == "AI model")
            .Which.Detail.Should().Contain(expectedName);
    }

    [Fact]
    public void Model_routing_with_a_cloud_key_is_disclosed()
    {
        var status = CreateService().Evaluate(new AppSettings
        {
            ActiveProviderId = "local",
            EnableModelRouting = true,
            OpenAiApiKey = "sk-test"
        });

        status.IsFullyLocal.Should().BeFalse();
        status.Disclosures.Should().Contain(d => d.Surface == "Model routing");
    }

    [Fact]
    public void Model_routing_without_any_cloud_key_stays_local()
    {
        var status = CreateService().Evaluate(new AppSettings
        {
            ActiveProviderId = "local",
            EnableModelRouting = true
        });

        status.IsFullyLocal.Should().BeTrue();
    }

    [Theory]
    [InlineData(WebSearchProvider.Brave, "Brave")]
    [InlineData(WebSearchProvider.Serper, "Serper")]
    public void Cloud_web_search_in_research_mode_is_disclosed(WebSearchProvider provider, string expectedName)
    {
        var status = CreateService().Evaluate(new AppSettings
        {
            EnableResearchMode = true,
            WebSearchProvider = provider,
            WebSearchApiKey = "search-key"
        });

        status.IsFullyLocal.Should().BeFalse();
        status.Disclosures.Should().ContainSingle(d => d.Surface == "Web search")
            .Which.Detail.Should().Contain(expectedName);
    }

    [Fact]
    public void Searxng_without_an_instance_url_cannot_search_and_stays_local()
    {
        var status = CreateService().Evaluate(new AppSettings
        {
            EnableResearchMode = true,
            WebSearchProvider = WebSearchProvider.SearXng
        });

        status.IsFullyLocal.Should().BeTrue();
    }

    [Theory]
    [InlineData("http://localhost:8888", "localhost")]
    [InlineData("https://searx.example.org", "searx.example.org")]
    public void Searxng_instance_is_disclosed_because_it_forwards_queries(string instanceUrl, string expectedHost)
    {
        var status = CreateService().Evaluate(new AppSettings
        {
            WebSearchProvider = WebSearchProvider.SearXng,
            WebSearchApiKey = instanceUrl
        });

        status.IsFullyLocal.Should().BeFalse();
        status.Disclosures.Should().ContainSingle(d => d.Surface == "Web search")
            .Which.Detail.Should().Contain(expectedHost).And.Contain("public search engines");
    }

    [Fact]
    public void Web_search_without_a_key_cannot_search_and_stays_local()
    {
        var status = CreateService().Evaluate(new AppSettings
        {
            EnableResearchMode = true,
            WebSearchProvider = WebSearchProvider.Brave
        });

        status.IsFullyLocal.Should().BeTrue();
    }

    [Fact]
    public void Configured_web_search_is_disclosed_even_with_the_settings_toggle_off()
    {
        // Research Mode is switched on per conversation in chat, not by this settings toggle.
        var status = CreateService().Evaluate(new AppSettings
        {
            EnableResearchMode = false,
            WebSearchProvider = WebSearchProvider.Brave,
            WebSearchApiKey = "brave-key"
        });

        status.IsFullyLocal.Should().BeFalse();
        status.Disclosures.Should().ContainSingle(d => d.Surface == "Web search")
            .Which.Detail.Should().Contain("Brave");
    }

    [Theory]
    [InlineData("http://192.168.1.40:11434", "192.168.1.40")]
    [InlineData("https://ollama.example.net", "ollama.example.net")]
    public void Ollama_on_another_machine_is_disclosed(string endpoint, string expectedHost)
    {
        var status = CreateService().Evaluate(new AppSettings
        {
            ActiveProviderId = "ollama",
            OllamaEndpoint = endpoint
        });

        status.IsFullyLocal.Should().BeFalse();
        status.Disclosures.Should().ContainSingle(d => d.Surface == "AI model")
            .Which.Detail.Should().Contain(expectedHost);
    }

    [Theory]
    [InlineData("http://localhost:11434")]
    [InlineData("http://127.0.0.1:11434")]
    [InlineData("http://[::1]:11434")]
    public void Ollama_on_this_machine_stays_local(string endpoint)
    {
        var status = CreateService().Evaluate(new AppSettings
        {
            ActiveProviderId = "ollama",
            OllamaEndpoint = endpoint
        });

        status.IsFullyLocal.Should().BeTrue();
    }

    [Fact]
    public void Calendar_sync_is_disclosed()
    {
        var settings = new AppSettings();
        settings.CalendarConnector.EnableCalendarSync = true;

        var status = CreateService().Evaluate(settings);

        status.IsFullyLocal.Should().BeFalse();
        status.Disclosures.Should().Contain(d => d.Surface == "Calendar sync");
    }

    [Fact]
    public void Email_sync_is_disclosed()
    {
        var settings = new AppSettings();
        settings.EmailConnector.EnableEmailSync = true;

        var status = CreateService().Evaluate(settings);

        status.IsFullyLocal.Should().BeFalse();
        status.Disclosures.Should().Contain(d => d.Surface == "Email sync");
    }

    [Fact]
    public void Multiple_active_surfaces_are_all_disclosed()
    {
        var settings = new AppSettings
        {
            ActiveProviderId = "openai",
            OpenAiApiKey = "sk-test",
            EnableModelRouting = true,
            EnableResearchMode = true,
            WebSearchProvider = WebSearchProvider.Serper,
            WebSearchApiKey = "serper-key"
        };
        settings.CalendarConnector.EnableCalendarSync = true;
        settings.EmailConnector.EnableEmailSync = true;

        var status = CreateService().Evaluate(settings);

        status.IsFullyLocal.Should().BeFalse();
        status.Disclosures.Select(d => d.Surface).Should().BeEquivalentTo(
            new[] { "AI model", "Model routing", "Web search", "Calendar sync", "Email sync" });
    }

    [Fact]
    public async Task GetCurrentAsync_loads_settings_then_evaluates()
    {
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.GetSettingsAsync())
            .ReturnsAsync(new AppSettings { ActiveProviderId = "anthropic" });

        var status = await new PrivacyStatusService(settingsService.Object).GetCurrentAsync();

        status.IsFullyLocal.Should().BeFalse();
        status.Disclosures.Should().Contain(d => d.Surface == "AI model");
        settingsService.Verify(s => s.GetSettingsAsync(), Times.Once);
    }
}
