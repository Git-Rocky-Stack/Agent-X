using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards the anti-slop hue rules in DESIGN.md: no purple-violet, no indigo
/// <c>#6366F1</c>, no lavender "AI uniform".
/// <para>
/// The rule exists because the local-AI category converges on exactly that palette,
/// and Agent-X differentiates by refusing it. Enforcement matters because the
/// violations do not arrive in the token file, where anyone would notice them. They
/// arrive one literal at a time in code-behind and view models: a One Dark syntax
/// theme whose keyword color was <c>#C678DD</c>, a Tailwind swatch set, a Material
/// score ramp. Each looked local and reasonable, and together they meant the app
/// shipped four palettes.
/// </para>
/// <para>
/// The check is hue-based rather than a list of known-bad hex values, because the
/// next violation will be a hex nobody has written down yet.
/// </para>
/// </summary>
public sealed class NoBannedPaletteHuesTests
{
    /// <summary>Purple, violet, indigo and lavender occupy roughly 250-330 degrees.</summary>
    private const double BannedHueStart = 250.0;
    private const double BannedHueEnd = 330.0;

    /// <summary>
    /// Below this saturation a color reads as a neutral grey and carries no hue
    /// identity, so the carbon and silver ramps do not trip the check.
    /// </summary>
    private const double MinimumSaturation = 0.15;

    /// <summary>
    /// Annotation ink is user content, not chassis chrome. The five color names are a
    /// persisted contract (<c>AnnotationEntity.Color</c>, defaulted in the migration
    /// baseline), so an annotation the user saved as "purple" has to render purple.
    /// DESIGN.md governs the instrument palette, which is a different thing from a
    /// highlighter the operator chose. The exemption covers the one file that holds
    /// the ink table and nothing else; <see cref="TheInkFile_HoldsOnlyTheSixPersistedInks"/>
    /// keeps it that narrow. (It used to exempt the whole AnnotationsPage code-behind,
    /// which would have let any chassis colour added there go unguarded.)
    /// </summary>
    private static readonly HashSet<string> ContentColorFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "AnnotationInk.cs",
    };

    [Fact]
    public void TheInkFile_HoldsOnlyTheSixPersistedInks()
    {
        var sourceRoot = ResolveSourceRoot();
        var inkFile = Path.Combine(sourceRoot, "AgentX.App", "Helpers", "AnnotationInk.cs");
        File.Exists(inkFile).Should().BeTrue("the exempt ink file must exist at the path the exemption names");

        var literals = ColorLiterals(inkFile).ToList();

        literals.Should().HaveCount(6,
            "five persisted inks plus the grey fallback are the whole contract; any other " +
            "colour literal in the exempt file is chassis colour hiding from the guard. Found:\n  " +
            string.Join("\n  ", literals.Select(l => $"line {l.LineNumber}: {l.Literal}")));
    }

    private static readonly Regex HexColor = new(
        @"#(?<value>[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})\b",
        RegexOptions.Compiled);

    private static readonly Regex ArgbColor = new(
        @"FromArgb\(\s*(?<a>\d+)\s*,\s*(?<r>\d+)\s*,\s*(?<g>\d+)\s*,\s*(?<b>\d+)\s*\)",
        RegexOptions.Compiled);

    [Fact]
    public void NoSourceFile_DeclaresAPurpleVioletOrIndigoColor()
    {
        var sourceRoot = ResolveSourceRoot();
        var appRoot = Path.Combine(sourceRoot, "AgentX.App");

        var offenders = new List<string>();
        var scanned = 0;

        foreach (var path in EnumerateSourceFiles(appRoot))
        {
            if (ContentColorFiles.Contains(Path.GetFileName(path)))
            {
                continue;
            }

            scanned++;

            foreach (var (line, number, color) in ColorLiterals(path))
            {
                var (hue, saturation) = HueAndSaturation(color);
                if (saturation >= MinimumSaturation && hue >= BannedHueStart && hue <= BannedHueEnd)
                {
                    offenders.Add(
                        $"{Path.GetRelativePath(sourceRoot, path)}:{number} -> {line} " +
                        $"(hue {hue:F0}, saturation {saturation:P0})");
                }
            }
        }

        scanned.Should().BeGreaterThan(50,
            "the scan must actually reach the app sources, otherwise this guard " +
            "silently passes forever");

        offenders.Should().BeEmpty(
            "DESIGN.md bans purple-violet, indigo and lavender outright: they are the " +
            "palette the local-AI category converges on, and refusing them is a " +
            "deliberate differentiator. Use the documented LED and silver ramps " +
            "instead. Offenders:\n  " + string.Join("\n  ", offenders));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Yields every color literal in the file, skipping commented lines. A hex inside
    /// a comment is usually a note about a color that was REMOVED, and flagging that
    /// would punish the very cleanup this guard exists to encourage.
    /// </summary>
    private static IEnumerable<(string Literal, int LineNumber, (int R, int G, int B) Color)>
        ColorLiterals(string path)
    {
        var lines = File.ReadAllLines(path);
        var inBlockComment = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (inBlockComment)
            {
                if (line.Contains("-->", StringComparison.Ordinal) ||
                    line.Contains("*/", StringComparison.Ordinal))
                {
                    inBlockComment = false;
                }

                continue;
            }

            var opensComment =
                (line.Contains("<!--", StringComparison.Ordinal) &&
                 !line.Contains("-->", StringComparison.Ordinal)) ||
                (line.Contains("/*", StringComparison.Ordinal) &&
                 !line.Contains("*/", StringComparison.Ordinal));

            if (opensComment)
            {
                inBlockComment = true;
                continue;
            }

            var isCommentLine =
                trimmed.StartsWith("//", StringComparison.Ordinal) ||
                trimmed.StartsWith("*", StringComparison.Ordinal) ||
                (line.Contains("<!--", StringComparison.Ordinal) &&
                 line.Contains("-->", StringComparison.Ordinal));

            if (isCommentLine)
            {
                continue;
            }

            foreach (Match match in HexColor.Matches(line))
            {
                var value = match.Groups["value"].Value;
                if (value.Length == 8)
                {
                    // Skip fully transparent values: an invisible color has no hue.
                    if (ParseByte(value[..2]) == 0)
                    {
                        continue;
                    }

                    value = value[2..];
                }

                yield return (match.Value, i + 1,
                    (ParseByte(value[..2]), ParseByte(value.Substring(2, 2)), ParseByte(value.Substring(4, 2))));
            }

            foreach (Match match in ArgbColor.Matches(line))
            {
                if (int.Parse(match.Groups["a"].Value, CultureInfo.InvariantCulture) == 0)
                {
                    continue;
                }

                yield return (match.Value, i + 1, (
                    int.Parse(match.Groups["r"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["g"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["b"].Value, CultureInfo.InvariantCulture)));
            }
        }
    }

    private static int ParseByte(string hex) =>
        int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    /// <summary>Returns hue in degrees and HSL saturation for an RGB triple.</summary>
    private static (double Hue, double Saturation) HueAndSaturation((int R, int G, int B) color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        if (delta <= double.Epsilon)
        {
            return (0.0, 0.0);
        }

        double hue;
        if (max == r)
        {
            hue = 60.0 * (((g - b) / delta) % 6.0);
        }
        else if (max == g)
        {
            hue = 60.0 * (((b - r) / delta) + 2.0);
        }
        else
        {
            hue = 60.0 * (((r - g) / delta) + 4.0);
        }

        if (hue < 0.0)
        {
            hue += 360.0;
        }

        var lightness = (max + min) / 2.0;
        var saturation = delta / (1.0 - Math.Abs((2.0 * lightness) - 1.0));

        return (hue, saturation);
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
