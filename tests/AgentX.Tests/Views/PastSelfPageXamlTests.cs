using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Views;

public sealed class PastSelfPageXamlTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void PastSelfPage_DeclaresConvertersUsedByStaticResources()
    {
        var xaml = ReadPastSelfPageXaml();

        xaml.Should().Contain("<converters:NullToVisibilityConverter x:Key=\"NullToVisibilityConverter\"");
        xaml.Should().Contain("<converters:InverseBoolConverter x:Key=\"InverseBoolConverter\"");
        xaml.Should().Contain("<converters:BoolToVisibilityConverter x:Key=\"BoolToVisibilityConverter\"");
        xaml.Should().Contain("<converters:TimeAgoConverter x:Key=\"TimeAgoConverter\"");
        xaml.Should().NotContain("StaticResource BooleanToVisibilityConverter");
        xaml.Should().NotContain("StaticResource DateTimeToStringConverter");
    }

    [Fact]
    public void PastSelfSearchButton_IsEnabledWhileIdleAndDisabledWhileLoading()
    {
        var xaml = ReadPastSelfPageXaml();

        xaml.Should().NotContain("IsEnabled=\"{x:Bind ViewModel.IsLoading, Mode=OneWay}\"");
        xaml.Should().Contain("IsEnabled=\"{x:Bind ViewModel.IsLoading, Mode=OneWay, Converter={StaticResource InverseBoolConverter}}\"");
    }

    [Fact]
    public void PastSelfDraftActions_DoNotAdvertiseUnwiredChatPrefill()
    {
        // "Copy for Chat" did what Copy does, on the armed-red cap DESIGN.md keeps for
        // consequential commands. The chat page takes no composer text on arrival, so there is
        // no hand-off to offer until it does.
        var buttons = LoadPastSelfPage().Descendants(Presentation + "Button").ToList();
        var codeBehind = ReadPastSelfPageCodeBehind();

        buttons.Should().NotContain(button => ((string?)button.Attribute("Content") ?? string.Empty).Contains("Chat"));
        codeBehind.Should().NotContain("UseInChatButton_Click");
        buttons.Where(button => (string?)button.Attribute("Click") == "CopyDraftButton_Click")
            .Should().ContainSingle()
            .Which.Attribute("Style")!.Value.Should().Be("{StaticResource SecondaryButtonStyle}");
    }

    [Fact]
    public void EvolutionBadge_FollowsTheResult_AndTodaysViewIsShownWithIt()
    {
        // The badge was hard-coded Collapsed, and today's stance and when it changed never showed.
        var page = LoadPastSelfPage();
        var badge = Named(page, "EvolutionBadge");

        badge.Attribute("Visibility")!.Value.Should().Be("{x:Bind ViewModel.CurrentResult.ShowsEvolution, Mode=OneWay}");
        badge.Attribute("Style")!.Value.Should().Be("{StaticResource BadgeDefaultStyle}", "it informs; armed red is for commands and live states");
        ReadPastSelfPageXaml().Should()
            .Contain("Text=\"{x:Bind ViewModel.CurrentResult.CurrentStance, Mode=OneWay}\"")
            .And.Contain("Text=\"{x:Bind ViewModel.CurrentResult.CurrentStanceLabel, Mode=OneWay}\"");
    }

    [Fact]
    public void StanceAndConfidence_ShowOnlyForAResultWithAStance()
    {
        var section = Named(LoadPastSelfPage(), "StanceText").Ancestors(Presentation + "Border").First();

        section.Attribute("Visibility")!.Value.Should().Be("{x:Bind ViewModel.CurrentResult.HasStance, Mode=OneWay}");
        section.Descendants(Presentation + "ProgressBar").Should().ContainSingle("the Confidence bar belongs to the stance");
    }

    private static XElement Named(XDocument page, string name) =>
        page.Descendants().Single(element => (string?)element.Attribute(Xaml + "Name") == name);

    private static XDocument LoadPastSelfPage() => XDocument.Load(ResolvePastSelfFile("PastSelfPage.xaml"));

    private static string ReadPastSelfPageXaml()
    {
        return File.ReadAllText(ResolvePastSelfFile("PastSelfPage.xaml"));
    }

    private static string ReadPastSelfPageCodeBehind()
    {
        return File.ReadAllText(ResolvePastSelfFile("PastSelfPage.xaml.cs"));
    }

    private static string ResolvePastSelfFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "AgentX.App", "Views", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {fileName} from test output directory.");
    }
}
