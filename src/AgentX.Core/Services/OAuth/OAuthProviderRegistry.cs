namespace AgentX.Core.Services.OAuth;

/// <summary>
/// Static registry that creates pre-configured <see cref="OAuthProviderConfig"/> instances
/// for well-known OAuth2 providers (Google, Microsoft). These factories incorporate the
/// correct authorization/token/revocation endpoints, default scopes, and provider-specific
/// extra parameters so callers don't need to know provider-specific details.
/// </summary>
/// <remarks>
/// <para>Usage: call the factory methods from <c>App.xaml.cs</c> during startup to register
/// providers with <see cref="OAuthService.RegisterProvider"/>. Client credentials are
/// pulled from <see cref="Settings.AppSettings.OAuth"/> so they remain configurable at
/// runtime through the settings UI.</para>
///
/// <para>The scopes are defined as space-separated strings to match the
/// <see cref="OAuthProviderConfig.Scopes"/> property format used throughout the OAuth pipeline.</para>
/// </remarks>
public static class OAuthProviderRegistry
{
    /// <summary>Stable identifier for the Google OAuth2 provider.</summary>
    public const string ProviderIdGoogle = "google";

    /// <summary>Stable identifier for the Microsoft OAuth2 provider.</summary>
    public const string ProviderIdMicrosoft = "microsoft";

    /// <summary>
    /// Creates a pre-configured <see cref="OAuthProviderConfig"/> for Google OAuth2.
    /// Uses Google's v2 authorization endpoint, token endpoint, and revocation endpoint.
    /// Includes <c>access_type=offline</c> and <c>prompt=consent</c> to ensure
    /// a refresh token is always returned.
    /// </summary>
    /// <param name="clientId">Google OAuth2 client ID from Google Cloud Console.</param>
    /// <param name="clientSecret">Google OAuth2 client secret from Google Cloud Console.</param>
    /// <param name="redirectUri">Local redirect URI for the OAuth callback listener.</param>
    /// <returns>A fully configured <see cref="OAuthProviderConfig"/> for Google.</returns>
    public static OAuthProviderConfig Google(string clientId, string clientSecret, string redirectUri) =>
        new()
        {
            ProviderId = ProviderIdGoogle,
            DisplayName = "Google",
            AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
            TokenEndpoint = "https://oauth2.googleapis.com/token",
            RevocationEndpoint = "https://oauth2.googleapis.com/revoke",
            Scopes = "openid profile email https://www.googleapis.com/auth/calendar.readonly https://www.googleapis.com/auth/gmail.readonly",
            ClientId = clientId,
            ClientSecret = clientSecret,
            RedirectUri = redirectUri,
            ExtraAuthParameters = new Dictionary<string, string>
            {
                ["access_type"] = "offline",
                ["prompt"] = "consent"
            }
        };

    /// <summary>
    /// Creates a pre-configured <see cref="OAuthProviderConfig"/> for Microsoft Graph OAuth2.
    /// Uses the v2.0 endpoints with the specified tenant. Microsoft does not have a
    /// standard token revocation endpoint, so <see cref="OAuthProviderConfig.RevocationEndpoint"/>
    /// is left empty (local token deletion only).
    /// </summary>
    /// <param name="clientId">Microsoft application (client) ID from Azure Portal.</param>
    /// <param name="clientSecret">Microsoft client secret from Azure Portal.</param>
    /// <param name="tenantId">
    /// Azure AD tenant ID. Use <c>"common"</c> for multi-tenant consumer apps,
    /// <c>"organizations"</c> for work/school accounts only, or a specific tenant GUID.
    /// </param>
    /// <param name="redirectUri">Local redirect URI for the OAuth callback listener.</param>
    /// <returns>A fully configured <see cref="OAuthProviderConfig"/> for Microsoft.</returns>
    public static OAuthProviderConfig Microsoft(string clientId, string clientSecret, string tenantId, string redirectUri) =>
        new()
        {
            ProviderId = ProviderIdMicrosoft,
            DisplayName = "Microsoft",
            AuthorizationEndpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize",
            TokenEndpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token",
            RevocationEndpoint = string.Empty, // Microsoft does not expose a standard revocation endpoint
            Scopes = "openid profile email Calendars.Read Mail.Read User.Read",
            ClientId = clientId,
            ClientSecret = clientSecret,
            RedirectUri = redirectUri,
            ExtraAuthParameters = new Dictionary<string, string>
            {
                ["prompt"] = "select_account"
            }
        };
}
