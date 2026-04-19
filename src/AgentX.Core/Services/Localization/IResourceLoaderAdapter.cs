namespace AgentX.Core.Services.Localization;

/// <summary>
/// Thin seam around the platform resource-loading / language-override APIs
/// (WinUI 3 <c>Windows.ApplicationModel.Resources.ResourceLoader</c> and
/// <c>Windows.Globalization.ApplicationLanguages</c> in production). Exists so
/// <see cref="ILocalizationService"/> can be unit-tested without a WinUI 3
/// runtime: tests inject an in-memory fake; production DI injects the real
/// WinUI-backed adapter in <c>AgentX.App</c>.
/// </summary>
/// <remarks>
/// Lifecycle: callers MUST invoke <see cref="SetLanguageOverride"/> (if any)
/// BEFORE <see cref="Initialize"/> so the resource loader resolves against the
/// correct language. <see cref="GetString"/> returns null until
/// <see cref="Initialize"/> has run at least once.
/// </remarks>
public interface IResourceLoaderAdapter
{
    /// <summary>
    /// Sets the app-wide primary language override. Pass <c>null</c> or empty
    /// to clear the override (falling back to the OS locale).
    /// </summary>
    void SetLanguageOverride(string? languageCode);

    /// <summary>
    /// Returns the currently-active UI language code (from the override if set,
    /// else the OS preferred language, else <c>"en-US"</c>).
    /// </summary>
    string GetActiveLanguage();

    /// <summary>
    /// Constructs / refreshes the underlying resource loader. Safe to call
    /// multiple times; internal failures are logged and swallowed so startup
    /// does not crash on misconfigured locales.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Returns the localized string for <paramref name="key"/>, or <c>null</c>
    /// if the key is absent or the loader is not yet initialized. Must NOT
    /// return the literal key on miss — callers distinguish "missing" from
    /// "present-but-equal-to-key" on the strength of this null signal.
    /// </summary>
    string? GetString(string key);
}
