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
/// AX-QA-008: the dashboard's "your data never leaves this machine - no cloud, no exceptions" claim
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

    // The dashboard shows these sentences as they are; chat builds its own from the same evaluation.
    [Fact]
    public void Disclosure_details_name_where_prompts_go()
    {
        var service = CreateService();

        service.Evaluate(new AppSettings { ActiveProviderId = "openai" }).Disclosures.Single().Detail
            .Should().Be("Your prompts and conversation content are sent to OpenAI for processing.");
        service.Evaluate(new AppSettings { ActiveProviderId = "ollama", OllamaEndpoint = "http://192.168.1.40:11434" })
            .Disclosures.Single().Detail
            .Should().Be("Your prompts and conversation content are sent to the Ollama server at 192.168.1.40.");
        service.Evaluate(new AppSettings { EnableModelRouting = true, AnthropicApiKey = "key" }).Disclosures.Single().Detail
            .Should().Be("Smart model routing may send prompts to your configured cloud AI provider.");
        service.Evaluate(new AppSettings { WebSearchProvider = WebSearchProvider.Serper, WebSearchApiKey = "key" })
            .Disclosures.Single().Detail
            .Should().Be("When Research Mode is on in chat, your questions are sent to Serper (Google Search).");
        service.Evaluate(new AppSettings { WebSearchProvider = WebSearchProvider.SearXng, WebSearchApiKey = "https://searx.example.org" })
            .Disclosures.Single().Detail
            .Should().Be("When Research Mode is on in chat, your questions are sent to the SearXNG instance at searx.example.org, which forwards them to public search engines.");
    }

    // --- One chat message ---
    // The empty chat claimed "No data leaves your machine" whatever the provider, so the claim now
    // follows where a message actually goes.

    [Theory]
    [InlineData("local")]
    [InlineData("ollama")]
    public void Chat_message_answered_on_this_computer_goes_nowhere(string providerId)
    {
        CreateService().EvaluateChatMessage(new AppSettings(), providerId, researchModeOn: false)
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("openai", "OpenAI")]
    [InlineData("anthropic", "Anthropic")]
    public void Chat_message_answered_by_a_cloud_provider_names_it(string providerId, string expectedName)
    {
        CreateService().EvaluateChatMessage(new AppSettings(), providerId, researchModeOn: false)
            .Should().Equal(new PromptRecipient(PromptRecipientKind.CloudAiProvider, expectedName));
    }

    [Fact]
    public void Chat_message_follows_the_provider_that_is_active_over_the_saved_one()
    {
        // The saved provider could not be started, so the AI service fell back to the built-in model.
        var settings = new AppSettings { ActiveProviderId = "openai" };

        CreateService().EvaluateChatMessage(settings, "local", researchModeOn: false).Should().BeEmpty();
        CreateService().EvaluateChatMessage(settings, null, researchModeOn: false)
            .Should().Equal(new PromptRecipient(PromptRecipientKind.CloudAiProvider, "OpenAI"));
    }

    [Theory]
    [InlineData("http://192.168.1.40:11434", "192.168.1.40")]
    [InlineData("https://ollama.example.net", "ollama.example.net")]
    public void Chat_message_answered_by_ollama_on_another_machine_names_its_host(string endpoint, string expectedHost)
    {
        CreateService().EvaluateChatMessage(new AppSettings { OllamaEndpoint = endpoint }, "ollama", researchModeOn: false)
            .Should().Equal(new PromptRecipient(PromptRecipientKind.RemoteOllama, expectedHost));
    }

    [Fact]
    public void Chat_message_with_routing_to_a_configured_cloud_provider_can_reach_it()
    {
        CreateService().EvaluateChatMessage(
                new AppSettings { EnableModelRouting = true, OpenAiApiKey = "sk-test" }, "local", researchModeOn: false)
            .Should().Equal(new PromptRecipient(PromptRecipientKind.ModelRouting, null));
    }

    [Theory]
    [InlineData(WebSearchProvider.Brave, "Brave Search")]
    [InlineData(WebSearchProvider.Serper, "Serper (Google Search)")]
    public void Chat_message_with_research_mode_on_goes_to_the_search_provider(WebSearchProvider provider, string expectedName)
    {
        var settings = new AppSettings { EnableResearchMode = true, WebSearchProvider = provider, WebSearchApiKey = "key" };

        CreateService().EvaluateChatMessage(settings, "local", researchModeOn: true)
            .Should().Equal(new PromptRecipient(PromptRecipientKind.WebSearch, expectedName));
    }

    [Fact]
    public void Chat_message_with_research_mode_on_and_searxng_names_the_instance()
    {
        var settings = new AppSettings
        {
            EnableResearchMode = true,
            WebSearchProvider = WebSearchProvider.SearXng,
            WebSearchApiKey = "https://searx.example.org"
        };

        CreateService().EvaluateChatMessage(settings, "local", researchModeOn: true)
            .Should().Equal(new PromptRecipient(PromptRecipientKind.SearXng, "searx.example.org"));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Chat_message_is_not_searched_unless_research_mode_is_on_in_chat_and_in_settings(
        bool researchModeOn, bool enabledInSettings)
    {
        var settings = new AppSettings
        {
            EnableResearchMode = enabledInSettings,
            WebSearchProvider = WebSearchProvider.Brave,
            WebSearchApiKey = "key"
        };

        CreateService().EvaluateChatMessage(settings, "local", researchModeOn).Should().BeEmpty();
    }

    [Fact]
    public void Chat_message_with_research_mode_on_but_no_search_provider_goes_nowhere()
    {
        CreateService().EvaluateChatMessage(new AppSettings { EnableResearchMode = true }, "local", researchModeOn: true)
            .Should().BeEmpty();
    }

    [Fact]
    public void Chat_message_does_not_count_calendar_or_email_sync()
    {
        var settings = new AppSettings();
        settings.CalendarConnector.EnableCalendarSync = true;
        settings.EmailConnector.EnableEmailSync = true;

        CreateService().EvaluateChatMessage(settings, "local", researchModeOn: false).Should().BeEmpty();
    }

    [Fact]
    public async Task GetChatMessageRecipientsAsync_loads_settings_then_evaluates()
    {
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.GetSettingsAsync())
            .ReturnsAsync(new AppSettings { ActiveProviderId = "anthropic" });

        var recipients = await new PrivacyStatusService(settingsService.Object)
            .GetChatMessageRecipientsAsync(activeProviderId: null, researchModeOn: false);

        recipients.Should().Equal(new PromptRecipient(PromptRecipientKind.CloudAiProvider, "Anthropic"));
        settingsService.Verify(s => s.GetSettingsAsync(), Times.Once);
    }
}
