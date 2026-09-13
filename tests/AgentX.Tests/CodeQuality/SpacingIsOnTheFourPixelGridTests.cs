using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards the base-4 spacing grid in DESIGN.md.
/// <para>
/// DESIGN.md's spacing scale is Fibonacci-flavoured on purpose (4 / 8 / 12 / 20 / 32 /
/// 52 / 84) and warns that mechanical even spacing reads as machine output. Neither
/// point licenses values between the stops. The 2026-09 audit found 767 XAML
/// attributes and 15 code-behind literals carrying a 6, 10 or 14, not by design but
/// by drift, one literal at a time. scripts/normalize-spacing.py snapped them; this
/// keeps them there.
/// </para>
/// <para>
/// The rule: a Margin, Padding, Spacing, RowSpacing or ColumnSpacing component of 5 or
/// more, or a <c>new Thickness(...)</c> component of 5 or more, must be a multiple of 4.
/// Components 0 to 4 are optical fine adjustments (hairline offsets, 1px bevels, 2px
/// stacks between a label and its value) and negatives are overlaps; both are left to
/// the designer.
/// </para>
/// <para>
/// Components are split on commas and on whitespace, because XAML accepts both forms:
/// <c>Margin="4,6,4,6"</c> and <c>Margin="4 6 4 6"</c> mean the same thing. Splitting on
/// commas alone made the second form parse as a single unreadable component, which was
/// then skipped, so a space-separated off-grid thickness passed this guard.
/// </para>
/// </summary>
public sealed class SpacingIsOnTheFourPixelGridTests
{
    private const double MinimumGoverned = 5;

    private static readonly Regex XamlSpacingAttribute = new(
        @"\b(?<attr>Margin|Padding|Spacing|RowSpacing|ColumnSpacing)=""(?<value>-?[0-9][0-9,\-\s\.]*)""",
        RegexOptions.Compiled);

    private static readonly Regex CodeThickness = new(
        @"new Thickness\((?<args>[^)]*)\)",
        RegexOptions.Compiled);

    [Fact]
    public void EverySpacingComponentOfFiveOrMore_IsAMultipleOfFour()
    {
        var sourceRoot = ResolveSourceRoot();
        var appRoot = Path.Combine(sourceRoot, "AgentX.App");
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var path in EnumerateSourceFiles(appRoot))
        {
            var lines = File.ReadAllLines(path);
            var isXaml = path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
            scanned++;

            for (var i = 0; i < lines.Length; i++)
            {
                var matches = isXaml
                    ? XamlSpacingAttribute.Matches(lines[i]).Select(m => m.Groups["value"].Value)
                    : CodeThickness.Matches(lines[i]).Select(m => m.Groups["args"].Value);

                foreach (var raw in matches)
                {
                    if (OffGrid(raw))
                    {
                        offenders.Add($"{Path.GetRelativePath(sourceRoot, path)}:{i + 1} -> {lines[i].Trim()}");
                    }
                }
            }
        }

        scanned.Should().BeGreaterThan(50,
            "the scan must actually reach the app sources, otherwise this guard silently passes forever");

        offenders.Should().BeEmpty(
            "DESIGN.md puts every spacing stop on the 4px grid; a 6, 10 or 14 sits between " +
            "stops and is drift, not cadence. Run scripts/normalize-spacing.py --apply, or " +
            "pick a stop. Offenders:\n  " + string.Join("\n  ", offenders));
    }

    private static readonly char[] ComponentSeparators = [',', ' ', '	'];

    private static bool OffGrid(string raw)
    {
        foreach (var part in raw.Split(ComponentSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            if (value >= MinimumGoverned && Math.Abs(value % 4) > double.Epsilon)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                 path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)) &&
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

        throw new DirectoryNotFoundException(
            "Could not locate the src directory from " + AppContext.BaseDirectory);
    }
}
