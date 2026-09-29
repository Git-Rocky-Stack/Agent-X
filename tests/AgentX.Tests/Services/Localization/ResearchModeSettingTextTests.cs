using AgentX.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Localization;

/// <summary>
/// The Research Mode setting said "Local vault only" / "Local vault + web search" and that results
/// are blended with the Knowledge Vault. Chat never retrieves vault documents: its context is the
/// conversation, stored memories and, in Research Mode, web results
/// (MessagingCoordinator.BuildResearchContextAsync), so the setting describes web search only.
/// </summary>
public sealed class ResearchModeSettingTextTests
{
    [Theory]
    [InlineData("Settings_EnableResearchMode.OnContent")]
    [InlineData("Settings_EnableResearchMode.OffContent")]
    [InlineData("Settings_AugmentAiAnswersWith.Text")]
    public void The_research_mode_setting_does_not_claim_chat_searches_the_vault(string key)
    {
        var text = ReswLocalization.For("en-US").GetString(key);

        text.Should().NotBe(key, "the resource must exist");
        text.Should().NotContainEquivalentOf("vault");
    }

    [Fact]
    public void Switched_on_it_says_chat_can_add_web_results()
    {
        ReswLocalization.For("en-US").GetString("Settings_EnableResearchMode.OnContent")
            .Should().Be("Chat can add web results");
    }
}
