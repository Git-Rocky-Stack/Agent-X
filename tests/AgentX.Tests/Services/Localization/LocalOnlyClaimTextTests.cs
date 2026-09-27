using AgentX.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Localization;

/// <summary>
/// Texts that say what stays on this computer have to hold whatever is set up. The Dashboard
/// subtitle said "Everything runs on your machine", which is false once a cloud AI provider or
/// web search is in use.
/// </summary>
public sealed class LocalOnlyClaimTextTests
{
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
