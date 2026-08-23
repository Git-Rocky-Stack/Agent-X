using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards against interactive controls that a screen reader cannot identify.
/// <para>
/// WinUI derives a control's automation name from its <c>Content</c> only when that content
/// is a string. A button holding a panel — the common icon-plus-label and icon-only shapes —
/// therefore reaches assistive technology as an unlabelled button, and a user navigating by
/// screen reader hears nothing that distinguishes it from any other. Nothing in the build or
/// the visual design surfaces this, which is why it needs a structural check.
/// </para>
/// <para>
/// A control is named when it carries <c>AutomationProperties.Name</c>, points at its own
/// visible label with <c>AutomationProperties.LabeledBy</c>, sets textual <c>Content</c>, or
/// carries an <c>x:Uid</c> that can supply the name from resources. Controls that are
/// explicitly disabled are exempt: they are not focusable, so they are never announced as
/// actionable.
/// </para>
/// </summary>
public sealed class InteractiveControlsHaveAccessibleNamesTests
{
    private static readonly string[] InteractiveControls =
    {
        "Button",
        "HyperlinkButton",
        "AppBarButton",
        "ToggleButton",
    };

    /// <summary>
    /// Attributes that give the control a name, directly or by reference.
    /// </summary>
    private static readonly string[] NamingAttributes =
    {
        "AutomationProperties.Name",
        "AutomationProperties.LabeledBy",
        "x:Uid=",
    };

    [Fact]
    public void EveryInteractiveControl_HasAnAccessibleName()
    {
        var appRoot = Path.Combine(ResolveSourceRoot(), "AgentX.App");

        var offenders = EnumerateXamlFiles(appRoot)
            .SelectMany(FindUnnamedControls)
            .OrderBy(o => o, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "a screen reader announces these as unlabelled buttons, so they cannot be told " +
            "apart by a user navigating without sight:\n  " + string.Join("\n  ", offenders));
    }

    // ── Scanning ─────────────────────────────────────────────────────────────

    private static IEnumerable<string> FindUnnamedControls(string path)
    {
        var text = File.ReadAllText(path);
        var fileName = Path.GetFileName(path);

        foreach (var tag in InteractiveControls)
        {
            foreach (Match match in Regex.Matches(
                text, $@"<{tag}(?<attrs>\s[^<]*?)?>", RegexOptions.Singleline))
            {
                var attributes = match.Groups["attrs"].Value;

                if (NamingAttributes.Any(a => attributes.Contains(a, StringComparison.Ordinal)))
                {
                    continue;
                }

                // Textual content is itself the name.
                if (Regex.IsMatch(attributes, @"\bContent\s*=\s*"""))
                {
                    continue;
                }

                // A disabled control is not focusable, so it is never announced.
                if (Regex.IsMatch(attributes, @"IsEnabled\s*=\s*""False""", RegexOptions.IgnoreCase))
                {
                    continue;
                }

                yield return $"{fileName}:{text.Take(match.Index).Count(c => c == '\n') + 1} <{tag}>";
            }
        }
    }

    private static IEnumerable<string> EnumerateXamlFiles(string root) =>
        Directory
            .EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

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
