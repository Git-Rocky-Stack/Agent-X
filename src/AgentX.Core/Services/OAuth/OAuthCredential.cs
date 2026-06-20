namespace AgentX.Core.Services.OAuth;

/// <summary>
/// Represents a decrypted OAuth2 credential for a provider.
/// This is the clean DTO returned by <see cref="IOAuthService"/> methods,
/// as opposed to the persisted <c>OAuthCredentialEntity</c> which stores
/// encrypted tokens.
/// </summary>
public sealed class OAuthCredential
{
    /// <summary>
    /// Stable identifier of the OAuth provider (e.g. <c>"google"</c>, <c>"microsoft"</c>).
    /// </summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>
    /// Decrypted access token for API calls. This value is only present in memory
    /// and is never persisted to the database in plaintext.
    /// </summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>
    /// Decrypted refresh token used to obtain new access tokens.
    /// This value is only present in memory and is never persisted in plaintext.
    /// </summary>
    public string RefreshToken { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the access token expires. Consumers should compare this
    /// to <c>DateTime.UtcNow</c> to decide whether a refresh is needed.
    /// </summary>
    public DateTime TokenExpiry { get; init; }

    /// <summary>
    /// Comma-separated list of OAuth scopes that were granted during authorization
    /// (e.g. <c>"calendar.read,calendar.write,email.read"</c>).
    /// </summary>
    public string Scopes { get; init; } = string.Empty;

    /// <summary>
    /// Provider-specific user identifier returned during OAuth consent
    /// (e.g. Google's <c>sub</c> claim or Microsoft's <c>oid</c> claim).
    /// </summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when this credential was first stored.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// UTC timestamp when this credential was last refreshed or updated.
    /// </summary>
    public DateTime UpdatedAt { get; init; }
}
