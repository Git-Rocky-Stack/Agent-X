namespace AgentX.Core.Services.Localization;

/// <summary>
/// Provides localized string resources for the application UI.
/// Wraps the WinUI 3 resource loading system with a service-friendly interface
/// and supports language override (vs. system locale auto-detect).
/// </summary>
public interface ILocalizationService
{
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
