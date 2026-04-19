using System.Globalization;
using AgentX.App.Services;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.Services.Localization;

/// <summary>
/// A1 Task 9 — Integration coverage for <see cref="LocalizationService.FormatPlural"/>.
/// Uses the <see cref="IResourceLoaderAdapter"/> seam (introduced alongside these
/// tests) to inject an in-memory resource set, so the fallback ladder
/// (<c>&lt;key&gt;_&lt;category&gt;</c> → <c>&lt;key&gt;_other</c> → throw) is exercised
/// without requiring a WinUI 3 runtime.
/// </summary>
public class LocalizationServicePluralTests
{
    [Fact]
    public void FormatPlural_selects_one_form_for_count_1_en()
    {
        using var _ = new CultureScope("en-US");
        var sut = BuildServiceWithResources(
            ("DocumentsImported_one", "Imported {0} document"),
            ("DocumentsImported_other", "Imported {0} documents"));

        var result = sut.FormatPlural("DocumentsImported", 1, 1);

        result.Should().Be("Imported 1 document");
    }

    [Fact]
    public void FormatPlural_selects_other_form_for_count_2_en()
    {
        using var _ = new CultureScope("en-US");
        var sut = BuildServiceWithResources(
            ("DocumentsImported_one", "Imported {0} document"),
            ("DocumentsImported_other", "Imported {0} documents"));

        var result = sut.FormatPlural("DocumentsImported", 2, 2);

        result.Should().Be("Imported 2 documents");
    }

    [Fact]
    public void FormatPlural_falls_back_to_other_when_specific_category_absent_ja()
    {
        using var _ = new CultureScope("ja");
        // Japanese has only the "other" category in CLDR — no "_one" entry needed.
        var sut = BuildServiceWithResources(
            ("DocumentsImported_other", "{0}\u4ef6\u306e\u30c9\u30ad\u30e5\u30e1\u30f3\u30c8\u3092\u30a4\u30f3\u30dd\u30fc\u30c8\u3057\u307e\u3057\u305f"));

        var result = sut.FormatPlural("DocumentsImported", 3, 3);

        result.Should().Be("3\u4ef6\u306e\u30c9\u30ad\u30e5\u30e1\u30f3\u30c8\u3092\u30a4\u30f3\u30dd\u30fc\u30c8\u3057\u307e\u3057\u305f");
    }

    [Fact]
    public void FormatPlural_throws_when_other_fallback_also_missing()
    {
        using var _ = new CultureScope("en-US");
        var sut = BuildServiceWithResources(
            ("Unrelated_one", "unused"));

        Action act = () => sut.FormatPlural("DocumentsImported", 1, 1);

        act.Should().Throw<KeyNotFoundException>()
           .WithMessage("*DocumentsImported*");
    }

    [Fact]
    public void FormatPlural_falls_back_from_zero_to_one_to_other_in_french()
    {
        using var _ = new CultureScope("fr");
        // French plural rule: n in {0, 1} → "one", else → "other".
        // Only _other is defined here, so both 0 and 1 (which normally resolve
        // to "one") must gracefully fall back to "_other" without throwing.
        var sut = BuildServiceWithResources(
            ("DocumentsImported_other", "{0} documents import\u00e9s"));

        sut.FormatPlural("DocumentsImported", 0, 0).Should().Be("0 documents import\u00e9s");
        sut.FormatPlural("DocumentsImported", 1, 1).Should().Be("1 documents import\u00e9s");
        sut.FormatPlural("DocumentsImported", 5, 5).Should().Be("5 documents import\u00e9s");
    }

    [Fact]
    public void FormatPlural_uses_current_culture_for_number_formatting()
    {
        // German uses comma as decimal separator — verify FormatPlural honors
        // CultureInfo.CurrentUICulture for the final string.Format call, not
        // InvariantCulture, so fractional counts render naturally per locale.
        using var _ = new CultureScope("de");
        var sut = BuildServiceWithResources(
            ("Weight_one", "{0} Kilogramm"),
            ("Weight_other", "{0} Kilogramm"));

        var result = sut.FormatPlural("Weight", 2.5, 2.5);

        result.Should().Be("2,5 Kilogramm");
    }

    private static ILocalizationService BuildServiceWithResources(params (string key, string value)[] entries)
    {
        var loader = new FakeResourceLoaderAdapter(entries.ToDictionary(e => e.key, e => e.value));
        // ISettingsService is required by the LocalizationService constructor but
        // FormatPlural never calls it; an unconfigured Mock.Of<> is sufficient.
        var settings = Mock.Of<ISettingsService>();
        return new LocalizationService(settings, new CldrPluralRuleProvider(), loader);
    }

    /// <summary>
    /// In-memory <see cref="IResourceLoaderAdapter"/> that serves from a fixed
    /// dictionary. SetLanguageOverride / GetActiveLanguage / Initialize are
    /// no-ops because FormatPlural does not invoke them. Returns null on miss
    /// so the service's null-coalescing fallback ladder works as in production.
    /// </summary>
    private sealed class FakeResourceLoaderAdapter : IResourceLoaderAdapter
    {
        private readonly Dictionary<string, string> _map;
        public FakeResourceLoaderAdapter(Dictionary<string, string> map) => _map = map;
        public void SetLanguageOverride(string? languageCode) { /* no-op */ }
        public string GetActiveLanguage() => "en-US";
        public void Initialize() { /* no-op */ }
        public string? GetString(string key) => _map.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>
    /// Scoped override of <see cref="CultureInfo.CurrentUICulture"/> for the
    /// duration of a single test. Restores the previous value on Dispose so
    /// tests do not leak culture state across xUnit collection execution.
    /// </summary>
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _prev;
        public CultureScope(string name)
        {
            _prev = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentUICulture = new CultureInfo(name);
        }
        public void Dispose() => CultureInfo.CurrentUICulture = _prev;
    }
}
