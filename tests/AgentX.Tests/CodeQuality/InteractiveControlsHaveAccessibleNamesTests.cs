using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards against interactive controls that a screen reader cannot identify.
/// <para>
/// WinUI derives a control's automation name from its <c>Content</c> only when that content
/// is a string. A button holding a panel, the common icon-plus-label and icon-only shapes,
/// therefore reaches assistive technology as an unlabelled button, and a user navigating by
/// screen reader hears nothing that distinguishes it from any other. Nothing in the build or
/// the visual design surfaces this, which is why it needs a structural check.
/// </para>
/// <para>
/// A control is named when it carries <c>AutomationProperties.Name</c>, points at its own
/// visible label with <c>AutomationProperties.LabeledBy</c>, sets <c>Content</c> or
/// <c>Header</c>, or carries an <c>x:Uid</c> that resolves to a name-bearing resource.
/// Controls that are explicitly disabled are exempt: they are not focusable, so they are
/// never announced as actionable.
/// </para>
/// <para>
/// An <c>x:Uid</c> only counts when the resource file actually supplies a name under it. A uid
/// whose only entry is <c>.ToolTipService.ToolTip</c> supplies help text, which UIA never
/// announces as the name, and a ToggleSwitch's <c>.OnContent</c> / <c>.OffContent</c> supply
/// the state rather than the identity: "Off" says nothing about what is off. Treating the mere
/// presence of a uid as proof of a name is what let twelve controls ship unnamed.
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
        "AppBarToggleButton",
        "RadioButton",
        "CheckBox",
        "ToggleSwitch",
    };

    /// <summary>
    /// Attributes that give the control a name directly, or by reference to a label it renders.
    /// </summary>
    private static readonly string[] NamingAttributes =
    {
        "AutomationProperties.Name",
        "AutomationProperties.LabeledBy",
    };

    /// <summary>
    /// Resource suffixes that set a name. A tooltip is help text and the on/off captions are
    /// state, so neither appears here.
    /// </summary>
    private static readonly string[] NameBearingResourceSuffixes =
    {
        ".[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
        ".AutomationProperties.Name",
        ".Content",
        ".Text",
        ".Header",
    };

    [Fact]
    public void EveryInteractiveControl_HasAnAccessibleName()
    {
        var sourceRoot = ResolveSourceRoot();
        var appRoot = Path.Combine(sourceRoot, "AgentX.App");
        var resourceKeys = LoadResourceKeys(appRoot);

        var offenders = EnumerateXamlFiles(appRoot)
            .SelectMany(path => FindUnnamedControls(path, resourceKeys))
            .OrderBy(o => o, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "a screen reader announces these as unlabelled, so they cannot be told " +
            "apart by a user navigating without sight:\n  " + string.Join("\n  ", offenders));
    }

    // Scanning

    private static IEnumerable<string> FindUnnamedControls(string path, ISet<string> resourceKeys)
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

                // Content and Header are the name, whether literal or bound to a string.
                if (Regex.IsMatch(attributes, @"\b(Content|Header)\s*=\s*"""))
                {
                    continue;
                }

                // A disabled control is not focusable, so it is never announced.
                if (Regex.IsMatch(attributes, @"IsEnabled\s*=\s*""False""", RegexOptions.IgnoreCase))
                {
                    continue;
                }

                var uid = Regex.Match(attributes, @"\bx:Uid\s*=\s*""(?<uid>[^""]+)""");
                if (uid.Success && SuppliesName(uid.Groups["uid"].Value, resourceKeys))
                {
                    continue;
                }

                var reason = uid.Success
                    ? $"x:Uid=\"{uid.Groups["uid"].Value}\" supplies no name resource"
                    : "no naming attribute";

                yield return
                    $"{fileName}:{text.Take(match.Index).Count(c => c == '\n') + 1} <{tag}> {reason}";
            }
        }
    }

    private static bool SuppliesName(string uid, ISet<string> resourceKeys) =>
        NameBearingResourceSuffixes.Any(suffix => resourceKeys.Contains(uid + suffix));

    private static ISet<string> LoadResourceKeys(string appRoot)
    {
        var path = Path.Combine(appRoot, "Strings", "en-US", "Resources.resw");
        File.Exists(path).Should().BeTrue($"the guard reads names from {path}");

        return XDocument.Load(path)
            .Root!
            .Elements("data")
            .Select(d => (string?)d.Attribute("name"))
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);
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
