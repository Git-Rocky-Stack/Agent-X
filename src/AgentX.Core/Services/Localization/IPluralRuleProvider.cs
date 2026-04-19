using System.Globalization;

namespace AgentX.Core.Services.Localization;

/// <summary>
/// Resolves a CLDR plural-category name (e.g., "one", "other", "few") for a given culture + count.
/// See https://cldr.unicode.org/index/cldr-spec/plural-rules
/// </summary>
public interface IPluralRuleProvider
{
    /// <summary>Returns a lowercase CLDR category: "zero" / "one" / "two" / "few" / "many" / "other".</summary>
    string GetCategory(CultureInfo culture, double count);
}
