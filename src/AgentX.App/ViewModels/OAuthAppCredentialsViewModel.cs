using AgentX.Core.Services.Localization;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

/// <summary>
/// The OAuth App Credentials form on the Calendar and Email connector pages. Agent-X ships no
/// OAuth client, so the user enters the client of their own Google and Microsoft OAuth apps
/// here, and Connect signs in with it. Saving stores the values in settings (the settings
/// service keeps the client secret DPAPI-encrypted) and applies them to the OAuth service at
/// once, so no restart is needed.
/// </summary>
/// <remarks>
/// <para>Google's Desktop app clients send their client secret with every token request, so
/// the Google part asks for one. The Microsoft part sets up a public client (mobile and desktop
/// applications), which has no secret.</para>
/// <para>A client ID cannot change while an account is connected through it: the account's
/// refresh token belongs to the client that issued it, so the account would keep looking
/// connected until its sign-in expired and then fail. The form asks for the account to be
/// disconnected first. With the client ID unchanged, saving keeps connected accounts working.</para>
/// </remarks>
public sealed partial class OAuthAppCredentialsViewModel : ObservableObject
{
    /// <summary>Every Google OAuth client ID ends with this.</summary>
    internal const string GoogleClientIdSuffix = ".apps.googleusercontent.com";

    private readonly ISettingsService _settingsService;
    private readonly IOAuthService _oauthService;
    private readonly ILocalizationService _localization;
    private readonly ILogger _log;

    public OAuthAppCredentialsViewModel(
        ISettingsService settingsService,
        IOAuthService oauthService,
        ILocalizationService localization,
        ILogger logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _log = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<OAuthAppCredentialsViewModel>();
    }

    // -- Form fields ---------------------------------------------------------------------------

    [ObservableProperty]
    private string _googleClientId = string.Empty;

    [ObservableProperty]
    private string _googleClientSecret = string.Empty;

    /// <summary>Shows the Google client secret as text. It is masked by default.</summary>
    [ObservableProperty]
    private bool _isGoogleClientSecretRevealed;

    [ObservableProperty]
    private string _microsoftClientId = string.Empty;

    /// <summary>The redirect URI the Microsoft app registration has to list. Read-only.</summary>
    [ObservableProperty]
    private string _microsoftRedirectUri = string.Empty;

    // -- Inline errors and the save result -----------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGoogleClientIdError))]
    private string _googleClientIdError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGoogleClientSecretError))]
    private string _googleClientSecretError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMicrosoftClientIdError))]
    private string _microsoftClientIdError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedMessage))]
    private string _savedMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveError))]
    private string _saveError = string.Empty;

    public bool HasGoogleClientIdError => GoogleClientIdError.Length > 0;

    public bool HasGoogleClientSecretError => GoogleClientSecretError.Length > 0;

    public bool HasMicrosoftClientIdError => MicrosoftClientIdError.Length > 0;

    public bool HasSavedMessage => SavedMessage.Length > 0;

    public bool HasSaveError => SaveError.Length > 0;

    private bool HasFieldErrors => HasGoogleClientIdError || HasGoogleClientSecretError || HasMicrosoftClientIdError;

    // -- Loading and saving --------------------------------------------------------------------

    /// <summary>
    /// Shows the saved credentials with the secret masked. Called each time the form is shown,
    /// so credentials saved on the other connector page appear here too.
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            var oauth = (await _settingsService.GetSettingsAsync()).OAuth;

            GoogleClientId = oauth.Google.ClientId ?? string.Empty;
            GoogleClientSecret = oauth.Google.ClientSecret ?? string.Empty;
            MicrosoftClientId = oauth.Microsoft.ClientId ?? string.Empty;
            MicrosoftRedirectUri = oauth.Microsoft.RedirectUri ?? string.Empty;
            IsGoogleClientSecretRevealed = false;
            ClearFeedback();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to load the OAuth app credentials");
            ClearFeedback();
            SaveError = _localization.GetString("OAuthApp_Failed", ex.Message);
        }
    }

    /// <summary>
    /// Checks the form, saves it and registers the providers with the new credentials. An empty
    /// client ID removes that provider (and, for Google, its secret).
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        ClearFeedback();

        var googleClientId = GoogleClientId.Trim();
        var googleClientSecret = GoogleClientSecret.Trim();
        var microsoftClientId = MicrosoftClientId.Trim();

        GoogleClientIdError = ValidateGoogleClientId(googleClientId);
        GoogleClientSecretError = googleClientId.Length == 0 ? string.Empty : ValidateGoogleClientSecret(googleClientSecret);
        MicrosoftClientIdError = ValidateMicrosoftClientId(microsoftClientId);
        if (HasFieldErrors)
            return;

        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            var oauth = settings.OAuth;

            var googleChanged = !IsSameClientId(oauth.Google.ClientId, googleClientId);
            var microsoftChanged = !IsSameClientId(oauth.Microsoft.ClientId, microsoftClientId);

            if (googleChanged && await IsConnectedAsync(OAuthProviderRegistry.ProviderIdGoogle))
                GoogleClientIdError = _localization.GetString("OAuthApp_DisconnectGoogleFirst");
            if (microsoftChanged && await IsConnectedAsync(OAuthProviderRegistry.ProviderIdMicrosoft))
                MicrosoftClientIdError = _localization.GetString("OAuthApp_DisconnectMicrosoftFirst");
            if (HasFieldErrors)
                return;

            oauth.Google.ClientId = googleClientId;
            // A secret is of no use without its client ID, so removing the ID removes both.
            oauth.Google.ClientSecret = googleClientId.Length == 0 ? string.Empty : googleClientSecret;
            oauth.Microsoft.ClientId = microsoftClientId;
            if (microsoftChanged)
            {
                // The form sets up a public client. A secret entered in settings.json by hand
                // belongs to the previous registration, and Microsoft would reject it.
                oauth.Microsoft.ClientSecret = string.Empty;
            }

            await _settingsService.SaveSettingsAsync(settings);
            _oauthService.ApplyProviderSettings(oauth);

            GoogleClientId = oauth.Google.ClientId;
            GoogleClientSecret = oauth.Google.ClientSecret;
            MicrosoftClientId = oauth.Microsoft.ClientId;
            SavedMessage = _localization.GetString("OAuthApp_Saved");

            // Never the values: the secret must not reach the log.
            _log.Information(
                "OAuth app credentials saved and applied. Google={GoogleState} Microsoft={MicrosoftState}",
                googleClientId.Length > 0 ? "set" : "not set",
                microsoftClientId.Length > 0 ? "set" : "not set");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to save the OAuth app credentials");
            SaveError = _localization.GetString("OAuthApp_Failed", ex.Message);
        }
    }

    // -- Validation ----------------------------------------------------------------------------

    private string ValidateGoogleClientId(string clientId)
    {
        if (clientId.Length == 0)
            return string.Empty;
        if (clientId.Any(char.IsWhiteSpace))
            return _localization.GetString("OAuthApp_ErrNoSpaces");

        return clientId.Length > GoogleClientIdSuffix.Length &&
               clientId.EndsWith(GoogleClientIdSuffix, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : _localization.GetString("OAuthApp_ErrGoogleClientId");
    }

    private string ValidateGoogleClientSecret(string clientSecret)
    {
        if (clientSecret.Length == 0)
            return _localization.GetString("OAuthApp_ErrGoogleSecretRequired");

        return clientSecret.Any(char.IsWhiteSpace)
            ? _localization.GetString("OAuthApp_ErrNoSpaces")
            : string.Empty;
    }

    private string ValidateMicrosoftClientId(string clientId)
    {
        if (clientId.Length == 0)
            return string.Empty;
        if (clientId.Any(char.IsWhiteSpace))
            return _localization.GetString("OAuthApp_ErrNoSpaces");

        // Microsoft Entra shows the Application (client) ID as a GUID in the 8-4-4-4-12 form.
        return Guid.TryParseExact(clientId, "D", out _)
            ? string.Empty
            : _localization.GetString("OAuthApp_ErrMicrosoftClientId");
    }

    // -- Helpers -------------------------------------------------------------------------------

    private static bool IsSameClientId(string? saved, string entered) =>
        string.Equals((saved ?? string.Empty).Trim(), entered, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when an account is connected through the provider's current client, the state in
    /// which the page offers Disconnect. A credential without a refresh token (shown as
    /// "Reconnect required") or one that cannot be read does not count: it stops working on
    /// its own, and the page offers no Disconnect for it, so it must not lock the client ID.
    /// </summary>
    private async Task<bool> IsConnectedAsync(string provider)
    {
        try
        {
            var credential = await _oauthService.GetCredentialAsync(provider);
            return credential is { RequiresReauthorization: false };
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Could not read the stored {Provider} credential", provider);
            return false;
        }
    }

    private void ClearFeedback()
    {
        GoogleClientIdError = string.Empty;
        GoogleClientSecretError = string.Empty;
        MicrosoftClientIdError = string.Empty;
        SavedMessage = string.Empty;
        SaveError = string.Empty;
    }

    // Editing a field clears its error, and the last save result, which no longer describes the form.

    partial void OnGoogleClientIdChanged(string value)
    {
        GoogleClientIdError = string.Empty;
        SavedMessage = string.Empty;
    }

    partial void OnGoogleClientSecretChanged(string value)
    {
        GoogleClientSecretError = string.Empty;
        SavedMessage = string.Empty;
    }

    partial void OnMicrosoftClientIdChanged(string value)
    {
        MicrosoftClientIdError = string.Empty;
        SavedMessage = string.Empty;
    }
}
