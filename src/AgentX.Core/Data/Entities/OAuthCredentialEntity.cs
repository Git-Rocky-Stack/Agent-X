namespace AgentX.Core.Data.Entities;

/// <summary>
/// Persists OAuth2 tokens for external providers (Google, Microsoft) used by
/// Calendar and Email integration plugins. Tokens are stored in DPAPI-encrypted
/// form; the host decrypts them at runtime before passing them to provider clients.
/// One row per provider; <see cref="ProviderId"/> is enforced unique so there is
/// at most one credential set per provider.
/// </summary>
public class OAuthCredentialEntity
{
    /// <summary>Surrogate primary key managed by EF Core / SQLite AUTOINCREMENT.</summary>
    public long Id { get; set; }

    /// <summary>
    /// Stable identifier of the OAuth provider (e.g. <c>"google"</c>, <c>"microsoft"</c>).
    /// Indexed with a unique constraint — only one credential row per provider.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// DPAPI-encrypted access token. Stored as a Base64-encoded encrypted blob;
    /// must be decrypted at runtime before use.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// DPAPI-encrypted refresh token. Stored as a Base64-encoded encrypted blob;
    /// used to obtain new access tokens when the current one expires.
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the access token expires. The host compares this to
    /// <c>DateTime.UtcNow</c> to decide whether a refresh is needed before
    /// making an API call.
    /// </summary>
    public DateTime TokenExpiry { get; set; }

    /// <summary>
    /// Comma-separated list of OAuth scopes granted during authorization
    /// (e.g. <c>"calendar.read,calendar.write,email.read"</c>).
    /// </summary>
    public string Scopes { get; set; } = string.Empty;

    /// <summary>
    /// Provider-specific user identifier returned during OAuth consent
    /// (e.g. Google's <c>sub</c> claim or Microsoft's <c>oid</c> claim).
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>UTC timestamp when this credential was first stored.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC timestamp when this credential was last refreshed or updated.</summary>
    public DateTime UpdatedAt { get; set; }
}
