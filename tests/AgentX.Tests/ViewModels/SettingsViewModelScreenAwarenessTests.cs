using System.Xml.Linq;
using AgentX.Core.Services.Settings;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.ViewModels;

/// <summary>
/// AppSettings.EnableScreenAwareness defaulted to off and no page changed it, so Quick Chat never
/// read the window in front. Settings now has a switch for it in the Research Mode faceplate,
/// loaded, saved and reset with the rest of the page.
/// <para>
/// SettingsViewModel and SettingsPage live in the WinUI project, which this test project cannot
/// compile, so the wiring is checked on their source.
/// </para>
/// </summary>
public sealed class SettingsViewModelScreenAwarenessTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void The_view_model_loads_saves_and_resets_the_setting()
    {
        var source = ReadApp("ViewModels", "SettingsViewModel.cs");

        source.Should().Contain("[ObservableProperty] private bool _enableScreenAwareness;");
        ExtractMethod(source, "public async Task InitializeAsync()")
            .Should().Contain("EnableScreenAwareness = settings.EnableScreenAwareness;");
        ExtractMethod(source, "private async Task SaveSettingsAsync()")
            .Should().Contain("settings.EnableScreenAwareness = EnableScreenAwareness;");

        // Reset restores the default, which keeps the screen unread.
        new AppSettings().EnableScreenAwareness.Should().BeFalse();
        ExtractMethod(source, "private async Task ResetToDefaultsAsync()")
            .Should().Contain("EnableScreenAwareness = false;");
    }

    [Fact]
    public void The_research_mode_faceplate_has_the_switch_and_its_description()
    {
        var page = XDocument.Load(AppPath("Views", "SettingsPage.xaml"));
        var english = ReswLocalization.For("en-US");

        var toggle = page.Descendants(Presentation + "ToggleSwitch")
            .Single(element => (string?)element.Attribute(Xaml + "Uid") == "Settings_EnableScreenAwareness");
        toggle.Attribute("IsOn")!.Value.Should().Be("{x:Bind ViewModel.EnableScreenAwareness, Mode=TwoWay}");
        toggle.Attribute("Header")!.Value.Should().Be(
            english.GetString("Settings_EnableScreenAwareness.Header"), "the XAML fallback is the en-US text");

        var faceplate = toggle.Ancestors().First(element => element.Name.LocalName == "Faceplate");
        faceplate.Attribute("Kicker")!.Value.Should().Be("MOD - RSRCH - 07");

        var description = page.Descendants(Presentation + "TextBlock")
            .Single(element => (string?)element.Attribute(Xaml + "Uid") == "Settings_ScreenAwarenessDescription");
        description.Parent.Should().BeSameAs(toggle.Parent, "the description sits under the switch");
        description.Attribute("Text")!.Value.Should().Be(
            english.GetString("Settings_ScreenAwarenessDescription.Text"), "the XAML fallback is the en-US text");
        description.Attribute("Text")!.Value.Should().Contain("Quick Chat")
            .And.Contain("window that was in front when you summoned it")
            .And.Contain("with Windows OCR on this computer");
    }

    private static string ReadApp(params string[] parts) => File.ReadAllText(AppPath(parts));

    private static string AppPath(params string[] parts)
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

    /// <summary>Returns the method starting at <paramref name="signature"/>, up to its closing brace.</summary>
    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the source declares {signature}");

        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}' && --depth == 0)
            {
                return source[start..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced braces after {signature}.");
    }
}
