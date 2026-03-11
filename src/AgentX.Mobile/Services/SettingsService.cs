namespace AgentX.Mobile.Services;

/// <summary>
/// Persists user preferences using <see cref="Preferences"/>.
/// Acts as a thin typed wrapper so ViewModels do not reference the MAUI API directly.
/// </summary>
public sealed class SettingsService
{
    private const string KeyApiUrl = "AgentX.ApiUrl";
    private const string DefaultApiUrl = "http://localhost:9846";

    /// <summary>The base URL of the AgentX desktop REST API.</summary>
    public string ApiUrl
    {
        get => Preferences.Default.Get(KeyApiUrl, DefaultApiUrl);
        set => Preferences.Default.Set(KeyApiUrl, value.Trim().TrimEnd('/'));
    }
}
