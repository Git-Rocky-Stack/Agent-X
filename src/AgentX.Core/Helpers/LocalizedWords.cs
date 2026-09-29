using System.Globalization;

namespace AgentX.Core.Helpers;

/// <summary>
/// Wording that Core builds for the user: the resource a lookup returns for a key, otherwise
/// the English given beside the key. The lookup is <see cref="FormatHelper.LocalizedText"/>,
/// which the app sets at startup; until then, and for a resource that is missing or does not
/// format, the English is used, so the English text never changes. Every key is named
/// literally in its GetString call, which is how the locale audit finds it.
/// </summary>
internal readonly struct LocalizedWords
{
    private readonly Func<string, string?>? _localizedText;

    public LocalizedWords(Func<string, string?>? localizedText) => _localizedText = localizedText;

    /// <summary>The wording for the app's current lookup, <see cref="FormatHelper.LocalizedText"/>.</summary>
    public static LocalizedWords Current => new(FormatHelper.LocalizedText);

    public string GetString(string key, string english, params object[] args)
    {
        var text = _localizedText?.Invoke(key);

        // The app's localization service answers a missing resource with the key itself.
        if (string.IsNullOrEmpty(text) || text == key)
            text = english;

        if (args.Length == 0)
            return text;

        try
        {
            return string.Format(CultureInfo.CurrentCulture, text, args);
        }
        catch (FormatException)
        {
            // A translation whose placeholder is broken reads in English, not as a template.
            return string.Format(CultureInfo.CurrentCulture, english, args);
        }
    }
}
