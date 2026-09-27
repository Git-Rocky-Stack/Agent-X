using AgentX.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Localization;

/// <summary>
/// Texts that say what stays on this computer, or where an answer comes from, have to hold
/// whatever is set up. The Dashboard subtitle said "Everything runs on your machine", which is
/// false once a cloud AI provider or web search is in use, and the Research Mode OFF tooltip
/// said answers use "your local knowledge", although AI Chat never searches the Knowledge Vault
/// (Ask Your Files does).
/// </summary>
public sealed class LocalOnlyClaimTextTests
{
    [Fact]
    public void Research_mode_off_says_answers_come_from_the_model_and_the_conversation()
    {
        var tooltip = ReswLocalization.For("en-US").GetString("Chat_ResearchModeOffTooltip");

        tooltip.Should().NotContainEquivalentOf("local knowledge")
            .And.Contain("AI model")
            .And.Contain("conversation");
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
