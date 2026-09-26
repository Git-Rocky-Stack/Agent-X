using AgentX.Core.Services.Localization;
using Microsoft.Windows.ApplicationModel.Resources;
using Serilog;
using MrtApplicationLanguages = Microsoft.Windows.Globalization.ApplicationLanguages;

namespace AgentX.App.Services;

/// <summary>
/// Production <see cref="IResourceLoaderAdapter"/> implementation over MRT Core, the
/// Windows App SDK resource system: <see cref="ResourceLoader"/> from
/// Microsoft.Windows.ApplicationModel.Resources and ApplicationLanguages from
/// Microsoft.Windows.Globalization. All Windows-namespace dependencies of
/// <see cref="LocalizationService"/> concentrate here so the service itself stays pure
/// and unit-testable.
/// </summary>
/// <remarks>
/// Agent-X ships unpackaged (WindowsPackageType=None). The UWP loader this adapter used
/// before (Windows.ApplicationModel.Resources.ResourceLoader) finds the app's resources
/// through package identity, so in an unpackaged process it cannot reach them: the
/// loader was never built (or every lookup missed), GetString returned null, and
/// code-built UI such as the command palette showed raw keys ("Palette_Actions") while
/// x:Uid markup, which WinUI resolves through MRT Core, was localized. MRT Core reads
/// resources.pri next to the executable, and its default constructor targets the
/// "Resources" subtree of the main resource map, which is where
/// Strings/&lt;language&gt;/Resources.resw lands. The language override likewise goes
/// through the MRT Core ApplicationLanguages, which (unlike the Windows.Globalization
/// one) works without package identity.
/// </remarks>
public sealed class WinUIResourceLoaderAdapter : IResourceLoaderAdapter
{
    private ResourceLoader? _resourceLoader;

    public void SetLanguageOverride(string? languageCode)
    {
        try
        {
            MrtApplicationLanguages.PrimaryLanguageOverride = languageCode ?? string.Empty;
        }
        catch (Exception ex)
        {
            // An override that cannot be applied must not stop the loader from being
            // built; strings then resolve in the OS language instead of as raw keys.
            Log.Warning(ex, "Could not apply language override {Language}", languageCode);
        }
    }

    public string GetActiveLanguage()
    {
        try
        {
            return MrtApplicationLanguages.Languages.FirstOrDefault() ?? "en-US";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not read the active UI language, assuming en-US");
            return "en-US";
        }
    }

    public void Initialize()
    {
        try
        {
            _resourceLoader = new ResourceLoader();
        }
        catch (Exception ex)
        {
            // resources.pri missing or unreadable (e.g. a partial dev deploy). Stay
            // tolerant rather than crash on boot; GetString reports every key as missing.
            Log.Warning(ex, "ResourceLoader initialization failed; code-side strings fall back to their keys");
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
            // Loader failure, including an exception for an unknown resource name, is
            // treated as a miss; callers see null and fall back through their own
            // resolution ladder (e.g., FormatPlural's _other).
        }
        return null;
    }
}
