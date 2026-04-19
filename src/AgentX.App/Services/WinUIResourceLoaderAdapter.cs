using AgentX.Core.Services.Localization;
using Serilog;
using Windows.ApplicationModel.Resources;
using Windows.Globalization;

namespace AgentX.App.Services;

/// <summary>
/// Production <see cref="IResourceLoaderAdapter"/> implementation that delegates
/// to WinUI 3's <see cref="ResourceLoader"/> and <see cref="ApplicationLanguages"/>.
/// All Windows-namespace dependencies of <see cref="LocalizationService"/>
/// concentrate here so the service itself stays pure and unit-testable.
/// </summary>
public sealed class WinUIResourceLoaderAdapter : IResourceLoaderAdapter
{
    private ResourceLoader? _resourceLoader;

    public void SetLanguageOverride(string? languageCode)
    {
        ApplicationLanguages.PrimaryLanguageOverride = languageCode ?? string.Empty;
    }

    public string GetActiveLanguage()
        => ApplicationLanguages.Languages.FirstOrDefault() ?? "en-US";

    public void Initialize()
    {
        try
        {
            _resourceLoader = new ResourceLoader();
        }
        catch
        {
            // Resource files may not exist yet during initial setup — match the
            // existing service's tolerant behavior rather than crash on boot.
            Log.Warning("ResourceLoader initialization failed — using fallback strings");
        }
    }

    public string? GetString(string key)
    {
        try
        {
            if (_resourceLoader is not null)
            {
                var value = _resourceLoader.GetString(key);
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
        }
        catch
        {
            // Loader failure treated as miss; callers see null and fall back
            // through their own resolution ladder (e.g., FormatPlural's _other).
        }
        return null;
    }
}
