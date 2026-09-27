using System.Xml.Linq;
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
