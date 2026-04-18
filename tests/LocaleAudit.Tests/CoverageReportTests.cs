using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using LocaleAudit;
using Xunit;

namespace LocaleAudit.Tests;

public class CoverageReportTests
{
    [Fact]
    public void Build_counts_coverage_per_locale_and_emits_missing_keys()
    {
        var uids = new List<UidReference>
        {
            new("BtnOk", "x.xaml", 1),
            new("BtnCancel", "x.xaml", 2),
            new("Greeting", "y.xaml", 1),
        };
        var codeKeys = new List<CodeKeyReference>(); // none in this test
        // en-US has all 3, fr has 2 (missing Greeting), ja has 1 (BtnOk only).
        var locales = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["en-US"] = new Dictionary<string, string>
            {
                ["BtnOk.Content"] = "OK",
                ["BtnCancel.Content"] = "Cancel",
                ["Greeting.Text"] = "Hello",
            },
            ["fr"] = new Dictionary<string, string>
            {
                ["BtnOk.Content"] = "OK",
                ["BtnCancel.Content"] = "Annuler",
            },
            ["ja"] = new Dictionary<string, string>
            {
                ["BtnOk.Content"] = "OK",
            },
        };

        var report = CoverageReport.Build(uids, codeKeys, locales);

        report.PerLocale["en-US"].CoveragePercent.Should().Be(100.0);
        report.PerLocale["fr"].CoveragePercent.Should().BeApproximately(66.67, 0.1);
        report.PerLocale["ja"].CoveragePercent.Should().BeApproximately(33.33, 0.1);
        report.PerLocale["fr"].MissingKeys.Should().BeEquivalentTo("Greeting");
        report.PerLocale["ja"].MissingKeys.Should().BeEquivalentTo("BtnCancel", "Greeting");
    }

    [Fact]
    public void Build_unions_xaml_uids_with_csharp_code_keys()
    {
        var uids = new List<UidReference> { new("BtnOk", "x.xaml", 1) };
        var codeKeys = new List<CodeKeyReference>
        {
            new("Nav_Dashboard", "N.cs", 10),
            new("Nav_Chat", "N.cs", 11),
        };
        // en-US has ALL three — total unique keys should be 3 (union).
        var locales = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["en-US"] = new Dictionary<string, string>
            {
                ["BtnOk.Content"] = "OK",
                ["Nav_Dashboard"] = "Dashboard",
                ["Nav_Chat"] = "Chat",
            },
        };

        var report = CoverageReport.Build(uids, codeKeys, locales);

        report.TotalKeys.Should().Be(3);
        report.PerLocale["en-US"].CoveragePercent.Should().Be(100.0);
    }

    [Fact]
    public void Build_dedupes_when_same_key_appears_in_both_xaml_and_code()
    {
        var uids = new List<UidReference> { new("BtnOk", "x.xaml", 1) };
        var codeKeys = new List<CodeKeyReference> { new("BtnOk", "y.cs", 1) };
        var locales = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["en-US"] = new Dictionary<string, string> { ["BtnOk.Content"] = "OK" },
        };

        var report = CoverageReport.Build(uids, codeKeys, locales);

        report.TotalKeys.Should().Be(1); // deduped
    }

    [Fact]
    public void ShouldFail_returns_true_when_any_locale_below_threshold()
    {
        var report = new CoverageReport
        {
            TotalKeys = 100,
            PerLocale = new Dictionary<string, LocaleCoverage>
            {
                ["en-US"] = new() { Locale = "en-US", Covered = 100, CoveragePercent = 100.0 },
                ["fr"] = new() { Locale = "fr", Covered = 90, CoveragePercent = 90.0 },
            },
        };

        report.ShouldFail(threshold: 98.0).Should().BeTrue();
    }

    [Fact]
    public void ShouldFail_returns_false_when_all_locales_above_threshold()
    {
        var report = new CoverageReport
        {
            TotalKeys = 100,
            PerLocale = new Dictionary<string, LocaleCoverage>
            {
                ["en-US"] = new() { Locale = "en-US", Covered = 100, CoveragePercent = 100.0 },
                ["fr"] = new() { Locale = "fr", Covered = 99, CoveragePercent = 99.0 },
            },
        };

        report.ShouldFail(threshold: 98.0).Should().BeFalse();
    }

    [Fact]
    public void WriteJson_emits_valid_json_with_required_fields()
    {
        var report = new CoverageReport
        {
            TotalKeys = 10,
            PerLocale = new Dictionary<string, LocaleCoverage>
            {
                ["en-US"] = new() { Locale = "en-US", Covered = 10, CoveragePercent = 100.0 },
            },
        };
        var path = Path.Combine(Path.GetTempPath(), $"report-{Guid.NewGuid():N}.json");

        try
        {
            CoverageReport.WriteJson(report, path);

            var json = File.ReadAllText(path);
            json.Should().Contain("\"totalKeys\": 10");
            json.Should().Contain("\"en-US\"");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PrintSummary_threshold_controls_OK_vs_LOW_status()
    {
        var report = new CoverageReport
        {
            TotalKeys = 100,
            PerLocale = new Dictionary<string, LocaleCoverage>
            {
                ["en-US"] = new() { Locale = "en-US", Covered = 97, CoveragePercent = 97.0 },
            },
        };

        // At threshold=98, 97% is LOW.
        var lowWriter = new StringWriter();
        CoverageReport.PrintSummary(report, lowWriter, threshold: 98.0);
        lowWriter.ToString().Should().Contain("[LOW]");

        // At threshold=95, same 97% is OK.
        var okWriter = new StringWriter();
        CoverageReport.PrintSummary(report, okWriter, threshold: 95.0);
        okWriter.ToString().Should().Contain("[OK]");
    }
}
