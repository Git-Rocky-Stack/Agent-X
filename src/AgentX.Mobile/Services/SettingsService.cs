namespace AgentX.Mobile.Services;

/// <summary>
/// Persists user preferences using <see cref="Preferences"/>.
/// Acts as a thin typed wrapper so ViewModels do not reference the MAUI API directly.
/// </summary>
public sealed class SettingsService
{
    private const string KeyApiUrl = "AgentX.ApiUrl";
    private const string KeyApiToken = "AgentX.ApiToken";
    private const string DefaultApiUrl = "http://localhost:9846";

    /// <summary>The base URL of the AgentX desktop REST API.</summary>
    public string ApiUrl
    {
        get => Preferences.Default.Get(KeyApiUrl, DefaultApiUrl);
        set => Preferences.Default.Set(KeyApiUrl, value.Trim().TrimEnd('/'));
    }

    /// <summary>
    /// Retrieves the bearer token used to authenticate with the desktop API. Stored in
    /// <see cref="SecureStorage"/> (platform keystore/keychain) rather than plain preferences
    /// because it is a secret. Returns null when unpaired or when secure storage is unavailable.
    /// </summary>
    public async Task<string?> GetApiTokenAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync(KeyApiToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // SecureStorage can throw on some platforms/emulators without a keystore — treat as unpaired.
            return null;
        }
    }

    /// <summary>Persists (or clears) the bearer token in secure storage.</summary>
    public async Task SetApiTokenAsync(string? token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
                SecureStorage.Default.Remove(KeyApiToken);
            else
                await SecureStorage.Default.SetAsync(KeyApiToken, token.Trim()).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort: if secure storage is unavailable the token simply isn't persisted.
        }
    }
}
