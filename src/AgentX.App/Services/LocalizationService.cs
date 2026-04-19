using System.Globalization;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Settings;
using Serilog;

namespace AgentX.App.Services;

/// <summary>
/// Default <see cref="ILocalizationService"/> implementation. All platform
/// resource-loading and language-override calls are delegated to
/// <see cref="IResourceLoaderAdapter"/> so this class carries zero WinUI 3
/// dependencies and is fully unit-testable. In production the adapter is
/// <c>WinUIResourceLoaderAdapter</c>; in tests it is a pre-populated fake.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private readonly ISettingsService _settingsService;
    private readonly IPluralRuleProvider _pluralRules;
    private readonly IResourceLoaderAdapter _resourceLoader;
    private string _currentLanguage;

    private static readonly List<LanguageOption> _supportedLanguages = new()
    {
        new LanguageOption { Code = "en-US", DisplayName = "English", NativeName = "English" },
        new LanguageOption { Code = "es", DisplayName = "Spanish", NativeName = "Espa\u00f1ol" },
        new LanguageOption { Code = "de", DisplayName = "German", NativeName = "Deutsch" },
        new LanguageOption { Code = "fr", DisplayName = "French", NativeName = "Fran\u00e7ais" },
        new LanguageOption { Code = "ja", DisplayName = "Japanese", NativeName = "\u65e5\u672c\u8a9e" },
        new LanguageOption { Code = "zh-CN", DisplayName = "Chinese (Simplified)", NativeName = "\u7b80\u4f53\u4e2d\u6587" }
    };

    public string CurrentLanguage => _currentLanguage;
    public IReadOnlyList<LanguageOption> SupportedLanguages => _supportedLanguages;

    public LocalizationService(
        ISettingsService settingsService,
        IPluralRuleProvider pluralRules,
        IResourceLoaderAdapter resourceLoader)
    {
        _settingsService = settingsService;
        _pluralRules = pluralRules;
        _resourceLoader = resourceLoader;
        _currentLanguage = "en-US";
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        // Awaited by InitializeCoreServicesAsync during app startup so no caller can
        // observe a half-initialized service. Replaces the old fire-and-forget ctor
        // race (the ctor used to kick off an `async void` that set _resourceLoader
        // and _currentLanguage on a thread-pool thread — UI thread reads could
        // therefore see a null loader and the wrong language on cold start).
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            var languageOverride = settings is AppSettingsExtended ext ? ext.LanguageOverride : null;

            if (!string.IsNullOrEmpty(languageOverride))
            {
                _currentLanguage = languageOverride;
                _resourceLoader.SetLanguageOverride(languageOverride);
            }
            else
            {
                _currentLanguage = _resourceLoader.GetActiveLanguage();
            }

            _resourceLoader.Initialize();

            Log.Information("Localization initialized: {Language}", _currentLanguage);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to initialize localization, defaulting to en-US");
            _currentLanguage = "en-US";
        }
    }

    public async Task SetLanguageAsync(string? languageCode)
    {
        try
        {
            if (string.IsNullOrEmpty(languageCode))
            {
                _resourceLoader.SetLanguageOverride(null);
                _currentLanguage = _resourceLoader.GetActiveLanguage();
            }
            else
            {
                _resourceLoader.SetLanguageOverride(languageCode);
                _currentLanguage = languageCode;
            }

            // Persist the language override in settings
            var settings = await _settingsService.GetSettingsAsync();
            if (settings is AppSettingsExtended ext)
            {
                ext.LanguageOverride = languageCode;
                await _settingsService.SaveSettingsAsync(ext);
            }

            Log.Information("Language changed to {Language} — restart required for full effect", _currentLanguage);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set language to {Language}", languageCode);
        }
    }

    public string GetString(string resourceKey)
    {
        var value = _resourceLoader.GetString(resourceKey);
        // Fallback: return the resource key as-is (useful during development
        // before all .resw files are populated). The adapter returns null on
        // miss, so !IsNullOrEmpty preserves the previous WinUI-behavior where
        // the empty-string miss also triggered the fallback.
        return string.IsNullOrEmpty(value) ? resourceKey : value;
    }

    public string GetString(string resourceKey, params object[] args)
    {
        var template = GetString(resourceKey);
        try
        {
            return string.Format(template, args);
        }
        catch
        {
            return template;
        }
    }

    /// <summary>
    /// True-miss-aware variant of <see cref="GetString(string)"/>. Returns the resource
    /// value only when the loader produced a non-empty string; returns null when the key
    /// is absent or the loader is unavailable. This avoids confusing "key present whose
    /// value equals the key" with "key absent" (which <see cref="GetString(string)"/> can't
    /// distinguish because it returns the key itself on miss).
    /// </summary>
    private string? TryGetString(string resourceKey)
    {
        var value = _resourceLoader.GetString(resourceKey);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public string FormatPlural(string baseKey, double count, params object[] args)
    {
        // GetString returns the resource key itself on miss, which cannot be distinguished
        // from a legitimate "present resource equal to its key" lookup. We use a private
        // TryGetString helper that returns null on miss so the fallback ladder is unambiguous.
        var culture = CultureInfo.CurrentUICulture;
        var category = _pluralRules.GetCategory(culture, count);
        var specificKey = $"{baseKey}_{category}";

        var template = TryGetString(specificKey)
                       ?? TryGetString($"{baseKey}_other")
                       ?? throw new KeyNotFoundException(
                           $"No plural resource for '{baseKey}' in category '{category}' or '_other' fallback (culture '{culture.Name}').");

        try
        {
            return string.Format(culture, template, args);
        }
        catch (FormatException ex)
        {
            // Malformed format string in the .resw template — surface the issue so
            // mis-authored placeholders don't silently render garbled to the user.
            Log.Warning(ex,
                "FormatPlural template for '{BaseKey}' (category '{Category}', culture '{Culture}') had an invalid format string; returning raw template.",
                baseKey, category, culture.Name);
            return template;
        }
    }
}

/// <summary>
/// Extended AppSettings with localization support.
/// Inherits from the base AppSettings to add language configuration.
/// </summary>
public class AppSettingsExtended : AgentX.Core.Services.Settings.AppSettings
{
    public string? LanguageOverride { get; set; }
    public string? BackupDestination { get; set; }
    public bool ScheduledBackupEnabled { get; set; }
    public int ScheduledBackupIntervalHours { get; set; } = 168;
    public int MaxBackupsToKeep { get; set; } = 5;
    public string? ScheduledBackupPassword { get; set; }
}
