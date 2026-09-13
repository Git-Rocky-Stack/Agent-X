using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Every keyed resource in the style dictionaries has a consumer, or is on a short
/// vocabulary list with its reason stated here.
/// <para>
/// The 2026-09 audit found 34 styles and roughly 200 tokens in the Styles dictionaries with no
/// reference anywhere in the app: Tier 1 faceplate primitives superseded by the
/// Faceplate templated control, status badges superseded by lamp tiles, legacy
/// colour ramps whose brushes carried literal hex instead of the token, and a whole
/// Spacing* scale nobody had consumed. Each looked like part of the system; together
/// they were a second design system that did not render. A primitive nobody can
/// reach is the same defect class as a feature nobody can reach.
/// </para>
/// <para>
/// A reference is any whole-word occurrence of the key outside its own definition,
/// with comments stripped (a key named in a comment is prose). A reference that lives
/// inside the definition block of another orphan does not count, so a style used only
/// by another dead style is still dead. Fluent lightweight-styling override keys
/// (TextControl*, ComboBox*, ToggleSwitch*, NavigationView*) are consumed by WinUI's
/// own templates by name and are allowed by prefix.
/// </para>
/// </summary>
public sealed class NoOrphanKeyedResourcesTests
{
    /// <summary>Keys WinUI's generic templates consume by name (lightweight styling).</summary>
    private static readonly string[] FrameworkConsumedPrefixes =
    {
        "TextControl", "ComboBox", "ToggleSwitch", "NavigationView",
    };

    /// <summary>
    /// DESIGN.md vocabulary that is declared as tokens for designers and future
    /// consumers even though the shipped brushes carry the values directly. Each entry
    /// is a named thing in DESIGN.md; anything not named there must be consumed or go.
    /// </summary>
    private static readonly HashSet<string> DesignVocabulary = new(StringComparer.Ordinal)
    {
        // Carbon ramp (DESIGN.md Color / Carbon ramp)
        "Void", "Carbon950", "Carbon900", "Carbon850", "Carbon800", "Carbon750", "Carbon700",
        // Silver text ramp (DESIGN.md Color / Text - silver ramp)
        "Silver300", "Silver400", "Silver500", "Silver600", "Silver700",
        // Armed red (DESIGN.md Color / Agent-X brand)
        "ArmedLit", "ArmedDeep", "ArmedCapTop",
        // LED semantics and the chrome accent (DESIGN.md Color / LED semantics)
        "LedGo", "LedHold", "LedWarn", "LedNoGo", "LedScope", "ChromeAccent", "ChromeBrush",
        "LedGoTextBrush", "LedHoldTextBrush", "LedWarnTextBrush", "LedNoGoTextBrush", "LedScopeTextBrush",
        "LcdHotBrush", "HairlineStrongBrush",
        // Spacing scale and breakpoints (DESIGN.md Spacing / Layout)
        "Sp1", "Sp2", "Sp3", "Sp4", "Sp5", "Sp6", "Sp7",
        "BreakpointMedium", "BreakpointWide", "BreakpointXWide",
        // Bundled static font instances (DESIGN.md Typography: static instances, never runtime downloads)
        "FontPrimaryBold", "FontPrimaryMedium", "FontStreamBold",
    };

    private static readonly Regex KeyedElement = new(
        @"<(?<el>[A-Za-z:.]+)\s+[^>]*?x:Key=""(?<key>[^""]+)""[^>]*?(?<selfClosing>/)?>",
        RegexOptions.Compiled);

    [Fact]
    public void EveryKeyedResource_HasAConsumer_OrAStatedReason()
    {
        var sourceRoot = ResolveSourceRoot();
        var appRoot = Path.Combine(sourceRoot, "AgentX.App");

        var texts = EnumerateSourceFiles(appRoot)
            .ToDictionary(path => path, path => StripComments(File.ReadAllText(path), path));

        var definitions = new List<Definition>();
        foreach (var (path, text) in texts)
        {
            var folder = Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty;
            if (folder is not ("Styles" or "Themes")) continue;

            foreach (Match match in KeyedElement.Matches(text))
            {
                var end = match.Groups["selfClosing"].Success
                    ? match.Index + match.Length
                    : FindClose(text, match.Groups["el"].Value, match.Index + match.Length);
                definitions.Add(new Definition(match.Groups["key"].Value, path, match.Index, end));
            }
        }

        definitions.Should().HaveCountGreaterThan(200,
            "the scan must find the token layer, otherwise this guard passes forever");

        // Fixpoint: drop keys with no live reference; a reference from inside a dead
        // definition is not live.
        var alive = definitions.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var key in alive.ToList())
            {
                if (!HasLiveReference(key, texts, definitions, alive))
                {
                    alive.Remove(key);
                    changed = true;
                }
            }
        } while (changed);

        var orphans = definitions
            .Where(d => !alive.Contains(d.Key))
            .Select(d => d.Key)
            .Distinct(StringComparer.Ordinal)
            .Where(key => !DesignVocabulary.Contains(key))
            .Where(key => !FrameworkConsumedPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        orphans.Should().BeEmpty(
            "a keyed style or token nobody references is a primitive nobody can reach. " +
            "Wire it (grep for the hand-rolled twin first), delete it, or add it to " +
            "DesignVocabulary with the DESIGN.md name it implements. Orphans:\n  " +
            string.Join("\n  ", orphans));
    }

    [Fact]
    public void TheVocabularyList_OnlyNamesKeysThatStillExist()
    {
        var sourceRoot = ResolveSourceRoot();
        var appRoot = Path.Combine(sourceRoot, "AgentX.App");
        var defined = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in EnumerateSourceFiles(appRoot))
        {
            var folder = Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty;
            if (folder is not ("Styles" or "Themes")) continue;
            foreach (Match match in KeyedElement.Matches(File.ReadAllText(path)))
            {
                defined.Add(match.Groups["key"].Value);
            }
        }

        var stale = DesignVocabulary.Where(key => !defined.Contains(key)).OrderBy(k => k).ToList();
        stale.Should().BeEmpty(
            "an allowlist entry for a key that no longer exists is a stale exemption. Stale:\n  " +
            string.Join("\n  ", stale));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed record Definition(string Key, string Path, int Start, int End);

    private static bool HasLiveReference(
        string key,
        Dictionary<string, string> texts,
        List<Definition> definitions,
        HashSet<string> alive)
    {
        var pattern = new Regex(@"(?<![A-Za-z0-9_])" + Regex.Escape(key) + @"(?![A-Za-z0-9_])");
        var deadBlocks = definitions.Where(d => !alive.Contains(d.Key)).ToList();

        foreach (var (path, text) in texts)
        {
            foreach (Match match in pattern.Matches(text))
            {
                // Its own x:Key attribute is the definition, not a use.
                var prefix = text.Substring(Math.Max(0, match.Index - 7), Math.Min(7, match.Index));
                if (prefix.EndsWith("x:Key=\"", StringComparison.Ordinal)) continue;

                // A use inside a dead definition block is not live.
                if (deadBlocks.Any(d => d.Path == path && match.Index >= d.Start && match.Index < d.End)) continue;

                return true;
            }
        }

        return false;
    }

    private static int FindClose(string text, string element, int from)
    {
        var close = text.IndexOf($"</{element}>", from, StringComparison.Ordinal);
        return close < 0 ? text.Length : close + element.Length + 3;
    }

    private static string StripComments(string text, string path) =>
        path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
            ? Regex.Replace(text, @"<!--.*?-->", string.Empty, RegexOptions.Singleline)
            : Regex.Replace(Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline), @"//[^\n]*", string.Empty);

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
