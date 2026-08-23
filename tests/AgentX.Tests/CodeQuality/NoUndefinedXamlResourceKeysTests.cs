using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards against XAML that references a resource key nothing defines.
/// <para>
/// <c>{StaticResource Foo}</c> and <c>{ThemeResource Foo}</c> are resolved when the page is
/// realised, not when it is compiled. A misspelled or removed key therefore builds cleanly
/// and then throws at runtime the first time a user opens that page, which the XAML
/// compiler, the C# compiler and view-model tests all miss.
/// </para>
/// </summary>
public sealed class NoUndefinedXamlResourceKeysTests
{
    /// <summary>
    /// Keys that come from the Windows App SDK's own theme dictionaries rather than this
    /// repository, so no local definition exists for them.
    /// </summary>
    private static readonly HashSet<string> FrameworkProvidedKeys = new(StringComparer.Ordinal)
    {
        "SymbolThemeFontFamily",
        "ContentControlThemeFontFamily",
        "LayerFillColorDefaultBrush",
        "SolidBackgroundFillColorBaseBrush",
        "CardBackgroundFillColorDefaultBrush",
        "CardStrokeColorDefaultBrush",
        "ControlFillColorDefaultBrush",
        "ControlStrokeColorDefaultBrush",
        "SubtleFillColorSecondaryBrush",
        "TextFillColorPrimaryBrush",
        "TextFillColorSecondaryBrush",
        "TextFillColorTertiaryBrush",
        "AccentFillColorDefaultBrush",
        "SystemControlHighlightAccentBrush",
        "DefaultTextBoxStyle",
        "DefaultButtonStyle",
    };

    [Fact]
    public void EveryXamlResourceReference_ResolvesToADefinedKey()
    {
        var appRoot = Path.Combine(ResolveSourceRoot(), "AgentX.App");
        var xamlFiles = EnumerateXamlFiles(appRoot).ToList();

        var definedKeys = CollectDefinedKeys(xamlFiles);
        var reference = new Regex(
            @"\{(?:StaticResource|ThemeResource)\s+(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*\}",
            RegexOptions.Compiled);

        var offenders = new List<string>();

        foreach (var path in xamlFiles)
        {
            var text = File.ReadAllText(path);

            foreach (Match match in reference.Matches(text))
            {
                var key = match.Groups["key"].Value;

                if (definedKeys.Contains(key) ||
                    FrameworkProvidedKeys.Contains(key) ||
                    key.StartsWith("SystemColor", StringComparison.Ordinal))
                {
                    continue;
                }

                var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetFileName(path)}:{line} -> {key}");
            }
        }

        offenders = offenders.Distinct(StringComparer.Ordinal).OrderBy(o => o, StringComparer.Ordinal).ToList();

        offenders.Should().BeEmpty(
            "an unresolved resource key throws when the page is opened, not when it is built; " +
            "the references below have no definition:\n  " + string.Join("\n  ", offenders));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Collects every <c>x:Key</c> and <c>x:Name</c> declared across the app's XAML.
    /// Resource dictionaries are merged app-wide, so a key defined anywhere resolves
    /// anywhere.
    /// </summary>
    private static HashSet<string> CollectDefinedKeys(IEnumerable<string> xamlFiles)
    {
        var declaration = new Regex(@"x:Key\s*=\s*""(?<key>[^""]+)""", RegexOptions.Compiled);
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in xamlFiles)
        {
            foreach (Match match in declaration.Matches(File.ReadAllText(path)))
            {
                keys.Add(match.Groups["key"].Value);
            }
        }

        return keys;
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
