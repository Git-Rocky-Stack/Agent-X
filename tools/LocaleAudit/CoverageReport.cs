using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocaleAudit;

public sealed class LocaleCoverage
{
    public string Locale { get; set; } = string.Empty;
    public int Covered { get; set; }
    public double CoveragePercent { get; set; }
    public List<string> MissingKeys { get; set; } = new();
    /// <summary>
    /// Keys present in this locale's resw but NOT referenced by any XAML x:Uid or
    /// C# GetString literal. These are dead entries — likely historical cruft from
    /// removed UI. Use to drive cleanup (see plan Task 7).
    /// </summary>
    public List<string> OrphanKeys { get; set; } = new();
}

public sealed class CoverageReport
{
    public int TotalKeys { get; set; }
    public Dictionary<string, LocaleCoverage> PerLocale { get; set; } = new();

    /// <summary>
    /// Coverage is computed over the UNION of XAML x:Uid references and C# GetString("key")
    /// call sites. A key counts as "covered" in a locale if EITHER:
    ///   (a) the locale has an entry whose name starts with "&lt;key&gt;." (XAML-style, e.g. "BtnOk.Content"), OR
    ///   (b) the locale has an entry whose name equals "&lt;key&gt;" exactly (code-style, e.g. "Nav_Dashboard").
    /// This matches Agent-X's mixed naming convention (Spike 3 finding).
    /// </summary>
    public static CoverageReport Build(
        IReadOnlyList<UidReference> xamlUids,
        IReadOnlyList<CodeKeyReference> codeKeys,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> locales)
    {
        var unionKeys = xamlUids.Select(u => u.Uid)
            .Concat(codeKeys.Select(c => c.Key))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var report = new CoverageReport { TotalKeys = unionKeys.Count };

        var unionKeySet = new HashSet<string>(unionKeys, StringComparer.Ordinal);
        foreach (var (locale, entries) in locales)
        {
            var coverage = new LocaleCoverage { Locale = locale };
            foreach (var key in unionKeys)
            {
                var hasXamlStyle = entries.Keys.Any(k => k.StartsWith(key + ".", StringComparison.Ordinal));
                var hasCodeStyle = entries.ContainsKey(key);
                if (hasXamlStyle || hasCodeStyle) coverage.Covered++;
                else coverage.MissingKeys.Add(key);
            }
            // Orphan = resw entry whose base-name (before any first dot) is NOT in the union.
            // Handles both XAML-style ("Foo.Content" → base "Foo") and code-style ("Nav_Bar" → base "Nav_Bar").
            // A leading '.' (dotIndex == 0) would yield an empty baseName; we treat such malformed keys
            // as orphans by keeping the full key for lookup (guaranteed miss against the non-empty union).
            foreach (var reswKey in entries.Keys)
            {
                var dotIndex = reswKey.IndexOf('.', StringComparison.Ordinal);
                var baseName = dotIndex > 0 ? reswKey.Substring(0, dotIndex) : reswKey;
                if (!unionKeySet.Contains(baseName))
                    coverage.OrphanKeys.Add(reswKey);
            }
            coverage.OrphanKeys.Sort(StringComparer.Ordinal);
            coverage.CoveragePercent = unionKeys.Count == 0
                ? 100.0
                : Math.Round(coverage.Covered * 100.0 / unionKeys.Count, 2);
            report.PerLocale[locale] = coverage;
        }
        return report;
    }

    public bool ShouldFail(double threshold)
        => PerLocale.Values.Any(c => c.CoveragePercent < threshold);

    public static void WriteJson(CoverageReport report, string path)
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        var json = JsonSerializer.Serialize(report, opts);
        File.WriteAllText(path, json);
    }

    public static void PrintSummary(CoverageReport report, TextWriter writer, double threshold = 98.0)
    {
        writer.WriteLine($"LocaleAudit — {report.TotalKeys} unique localization keys (XAML + C# union)");
        foreach (var (locale, c) in report.PerLocale.OrderBy(kv => kv.Key))
        {
            var status = c.CoveragePercent >= threshold ? "OK" : "LOW";
            writer.WriteLine($"  [{status}] {locale,-6} {c.CoveragePercent,6:F2}% ({c.Covered}/{report.TotalKeys})  missing: {c.MissingKeys.Count}  orphan: {c.OrphanKeys.Count}");
        }
    }
}
