using System.Globalization;
using AgentX.Core.Services.Localization;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Localization;

/// <summary>
/// A1 Task 9 — Verifies the direct-coded CLDR plural rules for each of the six
/// shipped Agent-X locales plus the catch-all default. Categories exercised:
/// "one" (n == 1 in most Latin locales; n in {0,1} in French), and "other"
/// (everything else, including all counts for ja / zh which are single-category).
/// </summary>
public class CldrPluralRuleProviderTests
{
    private readonly CldrPluralRuleProvider _sut = new();

    [Theory]
    // English — "one" only for n == 1.
    [InlineData("en-US", 0, "other")]
    [InlineData("en-US", 1, "one")]
    [InlineData("en-US", 2, "other")]
    [InlineData("en-US", 5, "other")]
    // German — same rule as English.
    [InlineData("de", 1, "one")]
    [InlineData("de", 2, "other")]
    // Spanish — same rule as English.
    [InlineData("es", 1, "one")]
    [InlineData("es", 3, "other")]
    // French — "one" covers BOTH n == 0 and n == 1 (CLDR v44).
    [InlineData("fr", 0, "one")]
    [InlineData("fr", 1, "one")]
    [InlineData("fr", 2, "other")]
    // Japanese — single-category; every count resolves to "other".
    [InlineData("ja", 0, "other")]
    [InlineData("ja", 1, "other")]
    [InlineData("ja", 99, "other")]
    // Simplified Chinese — single-category; every count resolves to "other".
    [InlineData("zh-CN", 1, "other")]
    [InlineData("zh-CN", 999, "other")]
    public void GetCategory_returns_expected_cldr_category(string cultureName, double count, string expected)
    {
        var result = _sut.GetCategory(new CultureInfo(cultureName), count);
        result.Should().Be(expected);
    }

    [Theory]
    // Default branch: any unknown ISO language falls back to English-style one/other.
    [InlineData("it", 1, "one")]
    [InlineData("it", 2, "other")]
    // Absolute-value handling: negative counts collapse via Math.Abs.
    [InlineData("en-US", -1, "one")]
    [InlineData("fr", -1, "one")]
    public void GetCategory_handles_edge_cases(string cultureName, double count, string expected)
    {
        var result = _sut.GetCategory(new CultureInfo(cultureName), count);
        result.Should().Be(expected);
    }
}
