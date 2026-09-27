using AgentX.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Localization;

/// <summary>
/// Texts that say what stays on this computer, or where an answer comes from, have to hold
/// whatever is set up. The Dashboard subtitle said "Everything runs on your machine", which is
/// false once a cloud AI provider or web search is in use, and the Research Mode OFF tooltip
/// and the notices for a web search that added nothing said answers use "your local
/// knowledge", although AI Chat never searches the Knowledge Vault (Ask Your Files does).
/// </summary>
public sealed class LocalOnlyClaimTextTests
{
    [Theory]
    [InlineData("Chat_ResearchModeOffTooltip")]
    [InlineData("Chat_ResearchNoWebSearch")]
    [InlineData("Chat_ResearchNoResults")]
    [InlineData("Chat_ResearchTimedOut")]
    [InlineData("Chat_ResearchFailed")]
    public void Without_web_results_answers_come_from_the_model_and_the_conversation(string key)
    {
        var text = ReswLocalization.For("en-US").GetString(key);

        text.Should().NotContainEquivalentOf("local knowledge")
            .And.Contain("AI model")
            .And.Contain("conversation");
    }

    [Theory]
    [InlineData("Chat_ResearchNoWebSearch")]
    [InlineData("Chat_ResearchNoResults")]
    [InlineData("Chat_ResearchTimedOut")]
    [InlineData("Chat_ResearchFailed")]
    public void The_chat_notices_english_fallback_is_the_english_resource(string key)
    {
        // Without a localization service the coordinator shows its English fallback instead.
        var coordinator = File.ReadAllText(Path.Combine(
            ResolveSourceRoot(), "AgentX.App", "ViewModels", "Coordinators", "MessagingCoordinator.cs"));

        coordinator.Should().Contain($"\"{ReswLocalization.For("en-US").GetString(key)}\"");
    }

    [Fact]
    public void The_dashboard_subtitle_names_what_can_take_data_off_the_computer()
    {
        var subtitle = ReswLocalization.For("en-US").GetString("Dash_Subtitle.Text");

        subtitle.Should().NotContainEquivalentOf("everything runs")
            .And.Contain("cloud AI provider")
            .And.Contain("web search");
        ReadView("DashboardPage.xaml").Should().Contain(
            $"x:Uid=\"Dash_Subtitle\" Text=\"{subtitle}\"", "the XAML fallback is the en-US text");
    }

    private static string ReadView(string fileName) =>
        File.ReadAllText(Path.Combine(ResolveSourceRoot(), "AgentX.App", "Views", fileName));

    private static string ResolveSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src");
            if (Directory.Exists(Path.Combine(candidate, "AgentX.App")) &&
                Directory.Exists(Path.Combine(candidate, "AgentX.Core")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Agent-X source root from the test output directory.");
    }
}
