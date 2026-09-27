using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// The Plugin Manager's detail switch fired the enable or disable command without waiting for
/// it and set the status lamp from the switch position, so a plugin that failed to enable read
/// Active with a GO lamp. The handler now awaits the command and shows the state the view model
/// ends on (PluginManagerViewModelTests pins that a failure leaves the state as it was), setting
/// the switch with its Toggled handler unhooked so that showing a state starts no new change.
/// <para>
/// The page lives in the WinUI project, which this test project cannot compile, so the rule is
/// checked on its source.
/// </para>
/// </summary>
public sealed class PluginToggleShowsTheResultTests
{
    [Fact]
    public void TheDetailSwitch_WaitsForTheCommand_AndShowsTheResultingState()
    {
        var source = ReadPage();
        var toggled = ExtractMethod(source, "private async void OnPluginToggled(");

        toggled.Should().Contain("await ViewModel.EnablePluginCommand.ExecuteAsync(pluginId);")
            .And.Contain("await ViewModel.DisablePluginCommand.ExecuteAsync(pluginId);")
            .And.Contain("ShowSelectedPluginState();")
            .And.NotContain("UpdateStatusBadge(toggle.IsOn)", "the switch position is a request, not the outcome")
            .And.NotContain(".Execute(pluginId)", "a command that is not awaited reports nothing");

        // One change at a time: a flip while one runs changes nothing until it settles.
        toggled.Should().Contain("_pluginChangeInFlight)").And.Contain("_pluginChangeInFlight = false;");
    }

    [Fact]
    public void ShowingTheState_ReadsThePlugin_AndSetsTheSwitchWithoutRaisingToggled()
    {
        var show = ExtractMethod(ReadPage(), "private void ShowSelectedPluginState()");

        show.Should().Contain("UpdateStatusBadge(plugin.IsEnabled);");
        var unhook = show.IndexOf("DetailToggle.Toggled -= OnPluginToggled;", StringComparison.Ordinal);
        var set = show.IndexOf("DetailToggle.IsOn = plugin.IsEnabled;", StringComparison.Ordinal);
        var rehook = show.IndexOf("DetailToggle.Toggled += OnPluginToggled;", StringComparison.Ordinal);
        unhook.Should().BeGreaterThanOrEqualTo(0);
        set.Should().BeGreaterThan(unhook);
        rehook.Should().BeGreaterThan(set);
    }

    private static string ReadPage() =>
        File.ReadAllText(Path.Combine(ResolveSourceRoot(), "AgentX.App", "Views", "PluginManagerPage.xaml.cs"));

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

        throw new DirectoryNotFoundException(
            $"Could not locate the source root from {AppContext.BaseDirectory}.");
    }
}
