namespace AgentX.Core.Services.Localization;

/// <summary>
/// Provides localized string resources for the application UI.
/// Wraps the WinUI 3 resource loading system with a service-friendly interface
/// and supports language override (vs. system locale auto-detect).
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Asynchronously initializes the service: reads the persisted language override,
    /// applies it to <see cref="Windows.Globalization.ApplicationLanguages"/>, and
    /// constructs the underlying WinUI ResourceLoader. Must be awaited during app
    /// startup before any <see cref="GetString(string)"/> or <see cref="FormatPlural"/>
    /// call to avoid racing against a null loader. Safe to call multiple times.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Gets the current language code (e.g., "en-US", "es", "de", "fr", "ja", "zh-CN").
    /// </summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// Gets all supported language codes with their display names.
    /// </summary>
    IReadOnlyList<LanguageOption> SupportedLanguages { get; }

    /// <summary>
    /// Sets the active language. Pass null to use system default.
    /// Changes take effect on next app restart.
    /// </summary>
    Task SetLanguageAsync(string? languageCode);

    /// <summary>
    /// Gets a localized string by resource key.
    /// Falls back to English if the key is not found in the current language.
    /// </summary>
    string GetString(string resourceKey);

    /// <summary>
    /// Gets a localized string with format arguments.
    /// </summary>
    string GetString(string resourceKey, params object[] args);

    /// <summary>
    /// Selects a CLDR plural-category resource for the current UI culture.
    /// Looks up "&lt;baseKey&gt;_&lt;category&gt;" (e.g., "DocumentsImported_one"), falling back
    /// to "&lt;baseKey&gt;_other" if the specific category is absent. Throws if neither exists.
    /// </summary>
    /// <param name="baseKey">Resource base key. Must have at least "&lt;baseKey&gt;_other" defined.</param>
    /// <param name="count">The count driving plural selection.</param>
    /// <param name="args">Optional format args substituted into the chosen resource value.</param>
    string FormatPlural(string baseKey, double count, params object[] args);
}

/// <summary>
/// Represents a supported language option for the UI.
/// </summary>
public sealed class LanguageOption
{
    public required string Code { get; init; }
    public required string DisplayName { get; init; }
    public required string NativeName { get; init; }
    public bool IsRtl { get; init; }
}
