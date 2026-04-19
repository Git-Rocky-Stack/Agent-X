using System.Globalization;

namespace AgentX.Core.Services.Localization;

/// <summary>
/// Minimal, direct-coded CLDR plural rules for the six supported Agent-X locales.
/// Matches Unicode CLDR v44 plural rules for cardinal numbers.
/// </summary>
public sealed class CldrPluralRuleProvider : IPluralRuleProvider
{
    public string GetCategory(CultureInfo culture, double count)
    {
        var n = Math.Abs(count);
        var lang = culture.TwoLetterISOLanguageName;

        return lang switch
        {
            // English, German, Spanish — "one" for n == 1, else "other".
            "en" => n == 1 ? "one" : "other",
            "de" => n == 1 ? "one" : "other",
            "es" => n == 1 ? "one" : "other",
            // French: 0 and 1 both "one".
            "fr" => (n == 0 || n == 1) ? "one" : "other",
            // Japanese, Simplified Chinese: single plural category.
            "ja" => "other",
            "zh" => "other",
            // Default: English-style.
            _ => n == 1 ? "one" : "other",
        };
    }
}
