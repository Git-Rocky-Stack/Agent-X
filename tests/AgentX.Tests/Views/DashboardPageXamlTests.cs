using System.Xml.Linq;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Views;

/// <summary>
/// The Dashboard's connection card had a status dot painted a fixed grey (OfflineBrush) whatever
/// the connection did, which DESIGN.md's data-bound rule bans. It now follows the provider's state
/// with the lamp brushes the MDL lamp on the instrument strip uses: steady GO while the provider
/// answers, steady HOLD otherwise. Never a red: nothing here awaits an acknowledgment.
/// </summary>
public sealed class DashboardPageXamlTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ConnectionDot_FollowsTheConnection_WithTheGoAndHoldLamps()
    {
        var row = ConnectionStatusRow();
        var dots = row.Descendants(Presentation + "Ellipse").ToList();

        dots.Should().HaveCount(2);
        dots.Select(dot => (string?)dot.Attribute("Fill")).Should().BeEquivalentTo(
            "{ThemeResource LedGoLampBrush}",
            "{ThemeResource LedHoldLampBrush}");
        dots.Single(dot => (string?)dot.Attribute("Fill") == "{ThemeResource LedGoLampBrush}")
            .Attribute("Visibility")!.Value
            .Should().Be("{x:Bind ViewModel.IsOllamaConnected, Mode=OneWay, Converter={StaticResource BoolToVis}}");
        dots.Single(dot => (string?)dot.Attribute("Fill") == "{ThemeResource LedHoldLampBrush}")
            .Attribute("Visibility")!.Value
            .Should().Be("{x:Bind ViewModel.IsOllamaConnected, Mode=OneWay, Converter={StaticResource InverseBoolToVis}}");
        row.ToString().Should().NotContain("OfflineBrush");
    }

    [Fact]
    public void ConnectionLamps_StayOnSystemColorsInHighContrast()
    {
        var colors = XDocument.Load(ResolveAppFile("Styles", "Colors.xaml"));
        var highContrast = colors.Descendants()
            .Single(element => (string?)element.Attribute(Xaml + "Key") == "HighContrast");

        foreach (var key in new[] { "LedGoLampBrush", "LedHoldLampBrush" })
        {
            highContrast.Descendants(Presentation + "SolidColorBrush")
                .Single(brush => (string?)brush.Attribute(Xaml + "Key") == key)
                .Attribute("Color")!.Value
                .Should().StartWith("{ThemeResource SystemColor", $"{key} must stay system-bound in HighContrast");
        }
    }

    [Fact]
    public void QuickSearch_PromisesNoShortcut_AndFallsBackToTheEnglishResource()
    {
        // The placeholder read "Search your knowledge vault...  (Ctrl+K)" and a key badge in the
        // search well repeated it, but Ctrl+K opens the Command Palette, not this search box.
        var page = XDocument.Load(ResolveAppFile("Views", "DashboardPage.xaml"));
        var searchBox = page.Descendants(Presentation + "TextBox")
            .Single(box => (string?)box.Attribute(Xaml + "Uid") == "Dash_SearchBox");

        searchBox.Attribute("PlaceholderText")!.Value.Should().Be(
            ReswLocalization.For("en-US").GetString("Dash_SearchBox.PlaceholderText"),
            "the XAML fallback is the en-US text");
        page.Descendants().Attributes().Select(attribute => attribute.Value)
            .Should().NotContain(value => value.Contains("Ctrl+K", StringComparison.Ordinal));

        foreach (var locale in new[] { "en-US", "de", "es", "fr", "ja", "zh-CN" })
        {
            ReswLocalization.For(locale).GetString("Dash_SearchBox.PlaceholderText")
                .Should().NotContain("+K", "the {0} placeholder must not promise a shortcut", locale);
        }
    }

    private static XElement ConnectionStatusRow()
    {
        var page = XDocument.Load(ResolveAppFile("Views", "DashboardPage.xaml"));
        var status = page.Descendants(Presentation + "TextBlock")
            .Single(text => (string?)text.Attribute("Text") == "{x:Bind ViewModel.ConnectionStatus, Mode=OneWay}");
        return status.Parent!;
    }

    private static string ResolveAppFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName, "src", "AgentX.App" }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate src/AgentX.App/{string.Join('/', parts)}.");
    }
}
