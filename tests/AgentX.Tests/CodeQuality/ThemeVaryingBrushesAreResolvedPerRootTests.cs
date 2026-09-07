using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.CodeQuality;

/// <summary>
/// Guards the "theme-blind code-behind brush" defect class.
/// <para>
/// ThemeService applies the shift by setting <c>RequestedTheme</c> on the window
/// root element. An <c>Application.Current.Resources["Key"]</c> lookup resolves
/// ThemeDictionaries against the APPLICATION's theme, which the root never
/// updates. So any brush pulled that way in code-behind is frozen at the Night
/// Ops value: on Day Shift the silver faceplates get Night text (near-white on
/// silver), and the hardware skin leaks into HighContrast, which DESIGN.md
/// declares untouchable.
/// </para>
/// <para>
/// This is not hypothetical. The CommandPalette built its rows in code and pulled
/// TextPrimary/Secondary/Tertiary that way, so every command in the Ctrl+K palette
/// rendered Night-white on a Day-silver faceplate. DESIGN.md already records the
/// same trap being fixed once for StatusToColorConverter; this guard stops the
/// third occurrence.
/// </para>
/// <para>
/// The fix is <c>ThemeResources.Brush(key)</c> (resolves against the root's
/// ActualTheme), or a XAML Style with <c>{ThemeResource}</c> setters where the
/// element has one.
/// </para>
/// </summary>
public sealed class ThemeVaryingBrushesAreResolvedPerRootTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly Regex AppLevelLookup = new(
        @"Application\s*\.\s*Current\s*\.\s*Resources\s*\[\s*""(?<key>[^""]+)""\s*\]",
        RegexOptions.Compiled);

    [Fact]
    public void NoCodeBehindLookup_ResolvesAThemeVaryingResourceAgainstTheApplication()
    {
        var sourceRoot = ResolveSourceRoot();
        var appRoot = Path.Combine(sourceRoot, "AgentX.App");

        var themeVarying = ThemeVaryingKeys(
            Path.Combine(appRoot, "Styles", "Colors.xaml"));

        themeVarying.Should().NotBeEmpty(
            "the scan must find the ThemeDictionaries in Colors.xaml, otherwise " +
            "this guard silently passes forever");

        var offenders = new List<string>();

        foreach (var path in EnumerateSourceFiles(appRoot))
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match match in AppLevelLookup.Matches(lines[i]))
                {
                    var key = match.Groups["key"].Value;
                    if (themeVarying.Contains(key))
                    {
                        offenders.Add(
                            $"{Path.GetRelativePath(sourceRoot, path)}:{i + 1} -> {key}");
                    }
                }
            }
        }

        offenders.Should().BeEmpty(
            "these keys are redefined in every ThemeDictionary, so an application-level " +
            "lookup freezes them at the Night Ops value and breaks Day Shift and " +
            "HighContrast. Use ThemeResources.Brush(key) or a {ThemeResource} style " +
            "setter instead. Offenders:\n  " + string.Join("\n  ", offenders));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns every x:Key declared inside a ThemeDictionary in Colors.xaml.
    /// A key defined there has a per-shift value, which is exactly what an
    /// application-level lookup cannot resolve correctly.
    /// </summary>
    private static HashSet<string> ThemeVaryingKeys(string colorsXamlPath)
    {
        var document = XDocument.Load(colorsXamlPath);

        var themeDictionaries = document
            .Descendants()
            .Where(element => element.Name.LocalName == "ResourceDictionary.ThemeDictionaries");

        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var themed in themeDictionaries.Elements())
        {
            foreach (var resource in themed.Descendants())
            {
                var key = resource.Attribute(XamlNamespace + "Key")?.Value;
                if (!string.IsNullOrEmpty(key))
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
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

        throw new DirectoryNotFoundException(
            "Could not locate the src directory from " + AppContext.BaseDirectory);
    }
}
