using System.IO;
using System.Text.Json;
using FluentAssertions;
using LocaleAudit;
using Xunit;

namespace LocaleAudit.Tests;

/// <summary>
/// The per-locale JSON files in scripts/translations are the legacy translation record that
/// inject-translations.py reads and the changeset scripts extend. A key the resw files no
/// longer define reads there like a live translation: the AI email categorization toggle,
/// two export dialog options and the license tier guide section all lingered in the mirrors
/// after their resw entries were removed.
/// </summary>
public class LegacyTranslationMirrorTests
{
    private static readonly string[] MirrorLocales = { "de", "es", "fr", "ja", "zh-CN" };

    [Fact]
    public void Every_mirror_key_still_exists_in_the_resw_files()
    {
        var repo = FindRepoRoot();
        var locales = ReswReader.ReadAllLocales(Path.Combine(repo, "src", "AgentX.App", "Strings"));
        var live = locales["en-US"].Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var locale in MirrorLocales)
        {
            var path = Path.Combine(repo, "scripts", "translations", $"{locale}.json");
            File.Exists(path).Should().BeTrue($"the legacy mirror for '{locale}' is expected at {path}");

            using var mirror = JsonDocument.Parse(File.ReadAllText(path));
            var stale = mirror.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .Where(key => !live.Contains(key))
                .ToList();

            stale.Should().BeEmpty(
                $"scripts/translations/{locale}.json must not keep keys the resw files no longer " +
                "define; remove them together with the resw entries");
        }
    }

    /// <summary>
    /// Walks up from the test's working directory until <c>AgentX.sln</c> is found.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AgentX.sln")))
        {
            dir = dir.Parent;
        }
        if (dir == null)
            throw new DirectoryNotFoundException(
                $"Could not locate AgentX.sln above '{AppContext.BaseDirectory}'");
        return dir.FullName;
    }
}
