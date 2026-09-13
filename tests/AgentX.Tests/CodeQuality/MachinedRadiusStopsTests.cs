using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards the machined radius scale in DESIGN.md: plates 2, caps 4, overlays 8,
/// true circles 9999, and nothing else.
/// <para>
/// The rule that matters is the one underneath the numbers: "NEVER uniform radius
/// across surface types". The hierarchy is the anti-slop signal, so the failure mode
/// is not an exotic value, it is tiers quietly collapsing into each other. That is
/// what had happened here. <c>RadiusMD</c> resolved to 8, the overlay stop, and 111
/// content Borders referenced it, so cards and dialogs were cut identically while
/// <c>RadiusLG</c> and <c>RadiusXL</c> still resolved to 12 and 16, which are not
/// stops at all.
/// </para>
/// </summary>
public sealed class MachinedRadiusStopsTests
{
    /// <summary>The machined stops, plus 0 for a deliberately square edge.</summary>
    private static readonly HashSet<double> MachinedStops = [0, 2, 4, 8, 9999];

    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly Regex LiteralCornerRadius = new(
        @"CornerRadius=""(?<value>[0-9][0-9,\s]*)""",
        RegexOptions.Compiled);

    [Fact]
    public void EveryRadiusToken_ResolvesToAMachinedStop()
    {
        var colors = Path.Combine(ResolveSourceRoot(), "AgentX.App", "Styles", "Colors.xaml");
        var document = XDocument.Load(colors);

        var tokens = document
            .Descendants()
            .Where(element => element.Name.LocalName == "CornerRadius")
            .Select(element => (
                Key: element.Attribute(XamlNamespace + "Key")?.Value ?? "(unnamed)",
                Raw: element.Value.Trim()))
            .ToList();

        tokens.Should().NotBeEmpty(
            "the scan must find the radius tokens, otherwise this guard passes forever");

        var offGrid = tokens
            .Where(token => !double.TryParse(token.Raw, NumberStyles.Float,
                       CultureInfo.InvariantCulture, out var value)
                   || !MachinedStops.Contains(value))
            .Select(token => $"{token.Key} = {token.Raw}")
            .ToList();

        offGrid.Should().BeEmpty(
            "DESIGN.md defines exactly four machined stops (2 plate, 4 cap, 8 overlay, " +
            "9999 circle). A token resolving to anything else puts a surface on a radius " +
            "the system does not have. Off-grid:\n  " + string.Join("\n  ", offGrid));
    }

    [Fact]
    public void NoView_HardcodesARadiusOffTheMachinedScale()
    {
        var sourceRoot = ResolveSourceRoot();
        var appRoot = Path.Combine(sourceRoot, "AgentX.App");

        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            // Colors.xaml declares the tokens themselves; Hardware.xaml and
            // Generic.xaml hold the hardware recipes DESIGN.md specifies literally.
            var name = Path.GetFileName(path);
            if (name is "Colors.xaml" or "Hardware.xaml" or "Generic.xaml")
            {
                continue;
            }

            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in LiteralCornerRadius.Matches(lines[i]))
                {
                    var parts = match.Groups["value"].Value
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    foreach (var part in parts)
                    {
                        if (!double.TryParse(part, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out var value) ||
                            !MachinedStops.Contains(value))
                        {
                            offenders.Add(
                                $"{Path.GetRelativePath(sourceRoot, path)}:{i + 1} -> {match.Value}");
                            break;
                        }
                    }
                }
            }
        }

        offenders.Should().BeEmpty(
            "a literal radius off the machined scale bypasses the token layer, so it " +
            "cannot be re-cut when the scale moves. Use RCard, RControl, ROverlay or " +
            "RPill. Offenders:\n  " + string.Join("\n  ", offenders));
    }

    private static readonly Regex CodeCornerRadius = new(
        @"new CornerRadius\((?<args>[^)]*)\)",
        RegexOptions.Compiled);

    /// <summary>
    /// Code-behind builds elements too, and a literal there bypasses the token layer
    /// exactly as a XAML literal does. The first sweep only read XAML, and a 6 lived on
    /// in BranchCompareWindow for that reason.
    /// </summary>
    [Fact]
    public void NoCodeBehind_ConstructsARadiusOffTheMachinedScale()
    {
        var sourceRoot = ResolveSourceRoot();
        var appRoot = Path.Combine(sourceRoot, "AgentX.App");
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var path in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            scanned++;
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in CodeCornerRadius.Matches(lines[i]))
                {
                    var parts = match.Groups["args"].Value
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    foreach (var part in parts)
                    {
                        if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                            !MachinedStops.Contains(value))
                        {
                            offenders.Add($"{Path.GetRelativePath(sourceRoot, path)}:{i + 1} -> {match.Value}");
                            break;
                        }
                    }
                }
            }
        }

        scanned.Should().BeGreaterThan(50, "the scan must reach the app sources");
        offenders.Should().BeEmpty(
            "a code-behind radius off the machined scale is the same defect as a XAML one. " +
            "Offenders:\n  " + string.Join("\n  ", offenders));
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
            "Could not locate the src directory from " + AppContext.BaseDirectory);
    }
}
