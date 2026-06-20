namespace AgentX.Core.Services.OAuth;

/// <summary>
/// Manages the OAuth2 authorization lifecycle for external providers
/// (Google, Microsoft, etc.). Tokens are stored in the database with DPAPI
/// encryption and are decrypted only at runtime before API calls.
/// </summary>
/// <remarks>
/// <para>Thread safety: implementations must be safe for concurrent access from
/// the UI thread and background services. Token refresh operations are guarded
/// by a <see cref="System.Threading.SemaphoreSlim"/> to prevent race conditions.</para>
///
/// <para>Auto-refresh: <see cref="GetAccessTokenAsync"/> automatically refreshes
/// expired tokens (with a 5-minute buffer) before returning. Callers do not need
/// to check expiry manually.</para>
/// </remarks>
public interface IOAuthService
{
    /// <summary>
    /// Initiates the OAuth2 authorization code flow for the specified provider.
    /// Opens the system browser to the consent screen, listens for the redirect
    /// callback on localhost, exchanges the authorization code for tokens, and
    /// persists the encrypted credentials.
    /// </summary>
    /// <param name="provider">
    /// The provider identifier (e.g. <c>"google"</c>, <c>"microsoft"</c>).
    /// Must correspond to a registered <see cref="OAuthProviderConfig"/>.
    /// </param>
    /// <param name="scopes">
    /// Comma-separated OAuth scopes to request, in addition to the provider's
    /// default scopes. Pass <c>null</c> or empty string to use defaults only.
    /// </param>
    /// <param name="redirectUri">
    /// Override redirect URI for this authorization request. Pass <c>null</c>
    /// to use the provider's configured <see cref="OAuthProviderConfig.RedirectUri"/>.
    /// </param>
    /// <returns>The decrypted credential after successful token exchange.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="provider"/> is null or whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no <see cref="OAuthProviderConfig"/> is registered for the provider,
    /// or when the user denies consent, or when the token exchange fails.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when the operation is cancelled via the <paramref name="cancellationToken"/>
    /// or when the 5-minute timeout elapses without a callback.
    /// </exception>
    Task<OAuthCredential> AuthorizeAsync(string provider, string? scopes = null, string? redirectUri = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a valid access token for the specified provider. If the stored token
    /// has expired (or will expire within 5 minutes), this method automatically
    /// refreshes it before returning.
    /// </summary>
    /// <param name="provider">
    /// The provider identifier (e.g. <c>"google"</c>, <c>"microsoft"</c>).
    /// </param>
    /// <returns>The decrypted, valid access token.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="provider"/> is null or whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no credential exists for the provider, or when token refresh fails.
    /// </exception>
    Task<string> GetAccessTokenAsync(string provider);

    /// <summary>
    /// Refreshes the access token for the specified provider using the stored
    /// refresh token. Updates the encrypted credential in the database on success.
    /// </summary>
    /// <param name="provider">
    /// The provider identifier (e.g. <c>"google"</c>, <c>"microsoft"</c>).
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the token was refreshed successfully;
    /// <see langword="false"/> if no credential exists or the refresh failed.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="provider"/> is null or whitespace.
    /// </exception>
    Task<bool> RefreshTokenAsync(string provider);

    /// <summary>
    /// Revokes the OAuth credential for the specified provider. If the provider
    /// supports server-side token revocation, the tokens are revoked with the
    /// provider before being deleted from the local database.
    /// </summary>
    /// <param name="provider">
    /// The provider identifier (e.g. <c>"google"</c>, <c>"microsoft"</c>).
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="provider"/> is null or whitespace.
    /// </exception>
    Task RevokeAsync(string provider);

    /// <summary>
    /// Retrieves the stored OAuth credential for the specified provider, decrypting
    /// tokens for in-memory use. Returns <see langword="null"/> if no credential
    /// has been stored for the provider.
    /// </summary>
    /// <param name="provider">
    /// The provider identifier (e.g. <c>"google"</c>, <c>"microsoft"</c>).
    /// </param>
    /// <returns>
    /// The decrypted credential, or <see langword="null"/> if none exists.
    /// </returns>
    Task<OAuthCredential?> GetCredentialAsync(string provider);
}
