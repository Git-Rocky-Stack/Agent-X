using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards against interactive controls that render as actionable but do nothing.
/// <para>
/// A button that ships with an icon, a tooltip, and an AutomationProperties.Name but no
/// Click handler and no Command binding is indistinguishable from a working button until
/// a user presses it. WinUI reports no error for this: the control simply absorbs the
/// press. That failure mode is invisible to the compiler, invisible to XAML validation,
/// and invisible to view-model unit tests, so it needs a structural guard.
/// </para>
/// <para>
/// A control counts as wired when it carries any of: a Click handler, a Command binding,
/// a NavigateUri, a checked/toggled state binding, an explicit IsEnabled="False"
/// (deliberately inert, e.g. a progress affordance), or a nested attached Flyout that the
/// press opens.
/// </para>
/// </summary>
public sealed class NoUnwiredInteractiveControlsTests
{
    /// <summary>
    /// Control types whose entire purpose is to respond to a press.
    /// </summary>
    private static readonly string[] InteractiveControls =
    {
        "Button",
        "HyperlinkButton",
        "AppBarButton",
        "SplitButton",
        "DropDownButton",
        "MenuFlyoutItem",
        "ToggleMenuFlyoutItem",
    };

    /// <summary>
    /// Attributes that prove the control does something when pressed.
    /// </summary>
    private static readonly string[] WiringAttributes =
    {
        "Click=",
        "Command=",
        "NavigateUri=",
        "IsChecked=",
        "IsOn=",
        "Tapped=",
        "Checked=",
    };

    [Fact]
    public void InteractiveControls_AreAllWiredToAnAction()
    {
        var appRoot = Path.Combine(ResolveSourceRoot(), "AgentX.App");

        var offenders = EnumerateXamlFiles(appRoot)
            .SelectMany(FindUnwiredControls)
            .OrderBy(o => o.File, StringComparer.Ordinal)
            .ThenBy(o => o.Line)
            .Select(o => $"{o.File}:{o.Line} <{o.Tag}> {o.Hint}")
            .ToList();

        offenders.Should().BeEmpty(
            "every interactive control must invoke an action; the controls below render as " +
            "pressable but silently do nothing:\n  " + string.Join("\n  ", offenders));
    }

    // ── Scanning ─────────────────────────────────────────────────────────────

    private static IEnumerable<string> EnumerateXamlFiles(string root) =>
        Directory
            .EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    private static IEnumerable<UnwiredControl> FindUnwiredControls(string path)
    {
        var text = File.ReadAllText(path);
        var fileName = Path.GetFileName(path);

        foreach (var tag in InteractiveControls)
        {
            // Match an element instance only: the tag name must be followed by whitespace,
            // '/' or '>'. This excludes attached-property syntax such as <Button.Flyout>.
            var pattern = $@"<{tag}(?<attrs>\s[^<]*?)?(?<close>/>|>)";

            foreach (Match match in Regex.Matches(text, pattern, RegexOptions.Singleline))
            {
                var attributes = match.Groups["attrs"].Value;

                if (WiringAttributes.Any(a => attributes.Contains(a, StringComparison.Ordinal)))
                {
                    continue;
                }

                // Deliberately inert controls (spinners, disabled affordances) are exempt.
                if (Regex.IsMatch(attributes, @"IsEnabled\s*=\s*""False""", RegexOptions.IgnoreCase))
                {
                    continue;
                }

                // A control that hosts a flyout acts on press by opening it.
                if (match.Groups["close"].Value == ">" && OpensAFlyout(text, match.Index, tag))
                {
                    continue;
                }

                yield return new UnwiredControl(
                    fileName,
                    LineNumberAt(text, match.Index),
                    tag,
                    DescribeControl(attributes));
            }
        }
    }

    /// <summary>
    /// Returns true when the element starting at <paramref name="start"/> contains an
    /// attached Flyout (e.g. &lt;Button.Flyout&gt;) before its closing tag.
    /// </summary>
    private static bool OpensAFlyout(string text, int start, string tag)
    {
        var closeIndex = text.IndexOf($"</{tag}>", start, StringComparison.Ordinal);
        var scanEnd = closeIndex < 0 ? text.Length : closeIndex;
        var body = text[start..scanEnd];

        return body.Contains($"<{tag}.Flyout", StringComparison.Ordinal) ||
               body.Contains("FlyoutBase.AttachedFlyout", StringComparison.Ordinal);
    }

    private static int LineNumberAt(string text, int index) =>
        text.Take(index).Count(c => c == '\n') + 1;

    /// <summary>
    /// Extracts a human-recognisable label so a failure names the control the user sees.
    /// </summary>
    private static string DescribeControl(string attributes)
    {
        var match = Regex.Match(
            attributes,
            @"(?:AutomationProperties\.Name|x:Uid|Content|Text)\s*=\s*""(?<value>[^""]{0,60})""");

        return match.Success ? $"({match.Groups["value"].Value})" : string.Empty;
    }

    private readonly record struct UnwiredControl(string File, int Line, string Tag, string Hint);

    // ── Shared helper ────────────────────────────────────────────────────────

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

        throw new DirectoryNotFoundException("Could not locate Agent-X source root from test output directory.");
    }
}
