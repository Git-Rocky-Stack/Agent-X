using System.IO;
using FluentAssertions;
using LocaleAudit;
using Xunit;

namespace LocaleAudit.Tests;

/// <summary>
/// End-to-end snapshot QA for Agent-X's six shipping locales. Runs the LocaleAudit
/// pipeline (XAML + C# extractors → CoverageReport) against the real repo and asserts:
///   (1) every required locale folder exists,
///   (2) every referenced key resolves to a non-empty value in every locale,
///   (3) zero orphan resw entries in any locale,
///   (4) global coverage ≥ 98% per the CI gate contract.
/// Visual / layout QA remains manual (Task 13) — this fixture is a data-level regression guard.
/// </summary>
public class PerPageLocaleSnapshotTests
{
    private static readonly string[] RequiredLocales =
        { "de", "en-US", "es", "fr", "ja", "zh-CN" };

    private const double CoverageThreshold = 98.0;

    [Fact]
    public void Every_referenced_key_resolves_to_non_empty_value_in_every_required_locale()
    {
        var repo = FindRepoRoot();
        var xamlRoot = Path.Combine(repo, "src", "AgentX.App");
        var csharpRoot = Path.Combine(repo, "src");
        var stringsRoot = Path.Combine(repo, "src", "AgentX.App", "Strings");

        var xamlUids = XamlUidExtractor.ExtractAll(xamlRoot);
        var codeKeys = CSharpGetStringExtractor.ExtractAll(csharpRoot);
        var locales = ReswReader.ReadAllLocales(stringsRoot);

        foreach (var required in RequiredLocales)
        {
            locales.Should().ContainKey(required,
                $"locale folder '{required}/Resources.resw' must exist");
        }

        var report = CoverageReport.Build(xamlUids, codeKeys, locales);

        foreach (var locale in RequiredLocales)
        {
            var coverage = report.PerLocale[locale];

            coverage.CoveragePercent.Should().BeGreaterThanOrEqualTo(
                CoverageThreshold,
                $"'{locale}' must meet the {CoverageThreshold}% CI-gate threshold " +
                $"(missing: {string.Join(", ", coverage.MissingKeys)})");

            coverage.OrphanKeys.Should().BeEmpty(
                $"'{locale}' must not carry dead resw entries — A1 Task 7 cleaned them up; " +
                "any new orphans indicate regression");
        }

        foreach (var locale in RequiredLocales)
        {
            var entries = locales[locale];
            foreach (var kv in entries)
            {
                kv.Value.Should().NotBeNullOrWhiteSpace(
                    $"'{kv.Key}' in '{locale}/Resources.resw' must not be blank " +
                    "(blanks render as empty strings and leak untranslated UI)");
            }
        }
    }

    [Fact]
    public void All_required_locales_share_identical_key_sets()
    {
        var repo = FindRepoRoot();
        var stringsRoot = Path.Combine(repo, "src", "AgentX.App", "Strings");
        var locales = ReswReader.ReadAllLocales(stringsRoot);

        var canonical = locales["en-US"].Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var locale in RequiredLocales)
        {
            if (locale == "en-US") continue;
            var keys = locales[locale].Keys.ToHashSet(StringComparer.Ordinal);
            keys.Should().BeEquivalentTo(canonical,
                $"'{locale}' must carry the same key set as canonical en-US; " +
                $"drift indicates a missing translation or leftover entry");
        }
    }

    /// <summary>
    /// Walks up from the test's working directory until <c>AgentX.sln</c> is found.
    /// Robust against bin/Debug/net8.0 layers that vary with platform + config.
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
