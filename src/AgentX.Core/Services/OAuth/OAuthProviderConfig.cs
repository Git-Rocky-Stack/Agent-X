namespace AgentX.Core.Services.OAuth;

/// <summary>
/// Immutable configuration for an OAuth2 provider. Contains the endpoints,
/// client credentials, and default scopes needed to initiate and complete
/// the authorization code flow.
/// </summary>
/// <remarks>
/// Instances are typically loaded from <c>AppSettings</c> or registered via DI.
/// The class is intentionally simple and immutable — all properties are <c>init</c>-only
/// to prevent accidental mutation after construction.
/// </remarks>
public sealed class OAuthProviderConfig
{
    /// <summary>
    /// Stable identifier of the provider (e.g. <c>"google"</c>, <c>"microsoft"</c>).
    /// Must match the <see cref="OAuthCredential.ProviderId"/> stored in the database.
    /// </summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>
    /// Display name shown in UI (e.g. <c>"Google Calendar"</c>, <c>"Microsoft Outlook"</c>).
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// OAuth2 authorization endpoint URL. The user's browser is directed here
    /// to grant consent. For Google this is
    /// <c>https://accounts.google.com/o/oauth2/v2/auth</c>.
    /// </summary>
    public string AuthorizationEndpoint { get; init; } = string.Empty;

    /// <summary>
    /// OAuth2 token endpoint URL. Used to exchange an authorization code for tokens
    /// and to refresh expired access tokens. For Google this is
    /// <c>https://oauth2.googleapis.com/token</c>.
    /// </summary>
    public string TokenEndpoint { get; init; } = string.Empty;

    /// <summary>
    /// OAuth2 revocation endpoint URL (optional). Used by <see cref="IOAuthService.RevokeAsync"/>
    /// to invalidate tokens. For Google this is <c>https://oauth2.googleapis.com/revoke</c>.
    /// Not all providers support token revocation; leave empty to skip server-side revocation.
    /// </summary>
    public string? RevocationEndpoint { get; init; }

    /// <summary>
    /// Comma-separated default scopes to request during authorization
    /// (e.g. <c>"https://www.googleapis.com/auth/calendar.readonly"</c>).
    /// Additional scopes may be passed to <see cref="IOAuthService.AuthorizeAsync"/>.
    /// </summary>
    public string Scopes { get; init; } = string.Empty;

    /// <summary>
    /// OAuth2 client ID issued by the provider's developer console.
    /// </summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    /// OAuth2 client secret issued by the provider's developer console.
    /// Stored here for the authorization code exchange; in production this
    /// should be protected via DPAPI or a secrets manager.
    /// </summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>
    /// Redirect URI registered with the provider. For desktop apps this is
    /// typically <c>http://localhost:{port}/callback</c> where the local
    /// HTTP listener captures the authorization code.
    /// </summary>
    public string RedirectUri { get; init; } = string.Empty;
}