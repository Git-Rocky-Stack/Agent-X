using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Security;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.OAuth;

/// <summary>
/// Production implementation of <see cref="IOAuthService"/>.
/// Manages the full OAuth2 authorization code flow for desktop applications,
/// including browser-based consent, token exchange, DPAPI-encrypted persistence,
/// automatic token refresh (5-minute buffer), and server-side revocation.
/// </summary>
/// <remarks>
/// <para>Thread safety: <see cref="_refreshLocks"/> provides per-provider
/// <see cref="SemaphoreSlim"/> guards to prevent concurrent token refresh operations
/// from racing against each other.</para>
///
/// <para>DPAPI encryption: All tokens are encrypted via <see cref="IDpapiEncryptionService"/>
/// before being persisted to SQLite. Decryption happens only at runtime, in memory.</para>
///
/// <para>Auto-refresh: <see cref="GetAccessTokenAsync"/> checks whether the stored
/// access token is expired or within 5 minutes of expiry. If so, it calls
/// <see cref="RefreshTokenAsync"/> automatically before returning the token.</para>
/// </remarks>
public sealed class OAuthService : IOAuthService, IDisposable
{
    // ── Constants ──────────────────────────────────────────────────────────────

    private const string ProviderIdGoogle = "google";
    private const string ProviderIdMicrosoft = "microsoft";

    /// <summary>
    /// Buffer duration before token expiry at which a refresh is triggered.
    /// Prevents API calls from failing due to a token that expires mid-request.
    /// </summary>
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── Fields ─────────────────────────────────────────────────────────────────

    private readonly AgentXDbContext _db;
    private readonly IDpapiEncryptionService _encryption;
    private readonly ILogger _log;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Per-provider semaphore locks for token refresh operations.
    /// Prevents concurrent refresh attempts for the same provider.
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshLocks = new(StringComparer.Ordinal);

    /// <summary>
    /// Registered provider configurations, keyed by <see cref="OAuthProviderConfig.ProviderId"/>.
    /// </summary>
    private readonly ConcurrentDictionary<string, OAuthProviderConfig> _providerConfigs = new(StringComparer.Ordinal);

    /// <summary>
    /// Pending CSRF state values for in-progress authorization flows, keyed by provider ID.
    /// Used to validate that callback requests originate from the same authorization request.
    /// One-time use: removed immediately after validation.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _pendingStates = new(StringComparer.Ordinal);

    /// <summary>
    /// Pending PKCE code verifiers for in-progress authorization flows, keyed by provider ID.
    /// Sent to the token endpoint during code exchange to prove the client owns the authorization code.
    /// One-time use: removed after the token exchange is complete.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _pendingCodeVerifiers = new(StringComparer.Ordinal);

    // ── Constructor ────────────────────────────────────────────────────────────

    /// <summary>
    /// Initializes <see cref="OAuthService"/> with the required dependencies.
    /// </summary>
    /// <param name="db">The application database context for credential persistence.</param>
    /// <param name="encryption">The DPAPI encryption service for token protection.</param>
    /// <param name="logger">The application-level Serilog logger.</param>
    public OAuthService(AgentXDbContext db, IDpapiEncryptionService encryption, ILogger logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
        _log = logger?.ForContext<OAuthService>() ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        Log.Information("OAuthService initialized");
    }

    // ── Public: Provider Configuration ─────────────────────────────────────────

    /// <summary>
    /// Registers an OAuth provider configuration. Must be called before
    /// <see cref="AuthorizeAsync"/> or <see cref="RefreshTokenAsync"/> can
    /// work with the provider.
    /// </summary>
    /// <param name="config">The provider configuration to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
    public void RegisterProvider(OAuthProviderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.ProviderId))
            throw new ArgumentException("ProviderId is required.", nameof(config));

        _providerConfigs[config.ProviderId] = config;
        _log.Information("OAuth provider registered: {ProviderId} ({DisplayName})",
            config.ProviderId, config.DisplayName);
    }

    /// <summary>
    /// Returns all registered provider configurations.
    /// </summary>
    public IReadOnlyDictionary<string, OAuthProviderConfig> GetRegisteredProviders() =>
        _providerConfigs;

    // ── IOAuthService Implementation ────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<OAuthCredential> AuthorizeAsync(string provider, string? scopes = null, string? redirectUri = null, CancellationToken cancellationToken = default)
    {
        ValidateProviderId(provider);

        var config = GetProviderConfig(provider);
        var effectiveRedirectUri = redirectUri ?? config.RedirectUri;
        var effectiveScopes = BuildScopes(config.Scopes, scopes);

        _log.Information("Starting OAuth authorization for {Provider} with scopes: {Scopes}",
            provider, effectiveScopes);

        // Generate CSRF state parameter (RFC 6749 Section 10.12)
        var state = GenerateState();
        _pendingStates[provider] = state;

        // Generate PKCE code verifier and challenge (RFC 8252)
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = ComputeCodeChallenge(codeVerifier);
        _pendingCodeVerifiers[provider] = codeVerifier;

        // Build the authorization URL with state and PKCE
        var authUrl = BuildAuthorizationUrl(config, effectiveScopes, effectiveRedirectUri, state, codeChallenge);

        // Create a linked cancellation token with a 5-minute timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

        // Start the local HTTP listener to receive the callback
        var callbackUri = new Uri(effectiveRedirectUri);
        var listenerPrefix = $"{callbackUri.Scheme}://{callbackUri.Host}:{callbackUri.Port}/";

        HttpListener? listener = null;
        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add(listenerPrefix);
            listener.Start();
            _log.Debug("Listening for OAuth callback at {Prefix}", listenerPrefix);

            // Open the system browser for user consent
            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

            // Wait for the callback with a timeout
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _log.Warning("OAuth authorization timed out for {Provider} after 5 minutes", provider);
                throw new OperationCanceledException(
                    $"OAuth authorization timed out for provider '{provider}'. " +
                    "The operation was cancelled after 5 minutes of waiting for the browser callback.");
            }

            // Extract state, code, and error from the callback
            var receivedState = context.Request.QueryString["state"];
            var code = context.Request.QueryString["code"];
            var error = context.Request.QueryString["error"];

            // Validate CSRF state parameter (one-time use)
            if (string.IsNullOrEmpty(receivedState) || !string.Equals(receivedState, state, StringComparison.Ordinal))
            {
                _log.Warning("OAuth state validation failed for {Provider}: received state does not match expected value", provider);

                var errorHtml = "<html><body><h2>Authorization failed</h2><p>Security validation failed. Please try again.</p></body></html>";
                var errorBytes = Encoding.UTF8.GetBytes(errorHtml);
                context.Response.StatusCode = 400;
                context.Response.ContentType = "text/html";
                await context.Response.OutputStream.WriteAsync(errorBytes, cancellationToken);
                context.Response.Close();

                throw new SecurityException(
                    $"OAuth state validation failed for provider '{provider}'. " +
                    "The callback may have been tampered with or is from a different authorization request.");
            }

            // Remove state after successful validation (one-time use)
            _pendingStates.TryRemove(provider, out _);

            // Respond to the browser
            var responseHtml = string.IsNullOrEmpty(error)
                ? "<html><body><h2>Authorization successful!</h2><p>You can close this tab.</p></body></html>"
                : $"<html><body><h2>Authorization denied</h2><p>Error: {WebUtility.HtmlEncode(error)}</p></body></html>";

            var responseBytes = Encoding.UTF8.GetBytes(responseHtml);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html";
            await context.Response.OutputStream.WriteAsync(responseBytes, cancellationToken);
            context.Response.Close();

            if (!string.IsNullOrEmpty(error))
            {
                _log.Warning("OAuth authorization denied for {Provider}: {Error}", provider, error);
                throw new InvalidOperationException(
                    $"OAuth authorization denied by user for provider '{provider}': {error}");
            }

            if (string.IsNullOrEmpty(code))
            {
                throw new InvalidOperationException(
                    $"OAuth authorization callback for provider '{provider}' did not include an authorization code.");
            }

            _log.Debug("Received OAuth authorization code for {Provider}", provider);

            // Remove PKCE code verifier before token exchange (one-time use)
            _pendingCodeVerifiers.TryRemove(provider, out _);

            // Exchange the authorization code for tokens (with PKCE code_verifier)
            var tokenResponse = await ExchangeCodeForTokensAsync(config, code, effectiveRedirectUri, codeVerifier);

            // Encrypt and persist the credential
            var credential = await PersistCredentialAsync(provider, tokenResponse, effectiveScopes);

            _log.Information("OAuth authorization completed successfully for {Provider}", provider);
            return credential;
        }
        catch (HttpListenerException ex)
        {
            _log.Error(ex, "Failed to start HTTP listener for OAuth callback for {Provider}", provider);
            throw new InvalidOperationException(
                $"Could not start the local HTTP listener for OAuth callback. " +
                $"Ensure port {callbackUri.Port} is available. Details: {ex.Message}", ex);
        }
        finally
        {
            listener?.Stop();
            listener?.Close();

            // Clean up pending state and PKCE values regardless of outcome
            _pendingStates.TryRemove(provider, out _);
            _pendingCodeVerifiers.TryRemove(provider, out _);
        }
    }

    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(string provider)
    {
        ValidateProviderId(provider);

        var credential = await GetCredentialAsync(provider);

        if (credential is null)
        {
            throw new InvalidOperationException(
                $"No OAuth credential stored for provider '{provider}'. " +
                "Call AuthorizeAsync first to establish a credential.");
        }

        // Check if the token is expired or within the refresh buffer
        if (credential.TokenExpiry <= DateTime.UtcNow.Add(RefreshBuffer))
        {
            _log.Information("Access token for {Provider} expires at {Expiry} (within {Buffer} min buffer), refreshing",
                provider, credential.TokenExpiry, RefreshBuffer.TotalMinutes);

            var refreshed = await RefreshTokenAsync(provider);
            if (!refreshed)
            {
                throw new InvalidOperationException(
                    $"Failed to refresh the expired access token for provider '{provider}'. " +
                    "The refresh token may have been revoked. Re-authorize with AuthorizeAsync.");
            }

            // Re-fetch the refreshed credential
            credential = await GetCredentialAsync(provider);
            if (credential is null)
            {
                throw new InvalidOperationException(
                    $"Credential for provider '{provider}' was lost after refresh. This should not happen.");
            }
        }

        return credential.AccessToken;
    }

    /// <inheritdoc />
    public async Task<bool> RefreshTokenAsync(string provider)
    {
        ValidateProviderId(provider);

        // Acquire a per-provider lock to prevent concurrent refresh operations
        var lockSlim = _refreshLocks.GetOrAdd(provider, _ => new SemaphoreSlim(1, 1));
        await lockSlim.WaitAsync();
        try
        {
            return await RefreshTokenInternalAsync(provider);
        }
        finally
        {
            lockSlim.Release();
        }
    }

    /// <inheritdoc />
    public async Task RevokeAsync(string provider)
    {
        ValidateProviderId(provider);

        _log.Information("Revoking OAuth credential for {Provider}", provider);

        var entity = await _db.OAuthCredentials
            .FirstOrDefaultAsync(c => c.ProviderId == provider);

        if (entity is null)
        {
            _log.Debug("No credential found for {Provider} — nothing to revoke", provider);
            return;
        }

        // Attempt server-side revocation if a revocation endpoint is configured
        var config = GetProviderConfigOrNull(provider);
        if (config is not null && !string.IsNullOrEmpty(config.RevocationEndpoint))
        {
            try
            {
                var accessToken = _encryption.Decrypt(entity.AccessToken);
                await RevokeTokenWithProviderAsync(config.RevocationEndpoint, accessToken);
                _log.Debug("Server-side token revocation succeeded for {Provider}", provider);
            }
            catch (Exception ex)
            {
                // Server-side revocation is best-effort; log but don't block local deletion
                _log.Warning(ex, "Server-side token revocation failed for {Provider} — proceeding with local deletion",
                    provider);
            }
        }

        // Remove the local credential
        _db.OAuthCredentials.Remove(entity);
        await _db.SaveChangesAsync();

        _log.Information("OAuth credential revoked and deleted for {Provider}", provider);
    }

    /// <inheritdoc />
    public async Task<OAuthCredential?> GetCredentialAsync(string provider)
    {
        ValidateProviderId(provider);

        var entity = await _db.OAuthCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ProviderId == provider);

        if (entity is null)
        {
            _log.Debug("No OAuth credential found for {Provider}", provider);
            return null;
        }

        return DecryptEntity(entity);
    }

    // ── Private: Authorization Flow ─────────────────────────────────────────────

    /// <summary>
    /// Builds the full authorization URL with query parameters for the OAuth2 consent screen,
    /// including the CSRF <c>state</c> parameter and PKCE <c>code_challenge</c>.
    /// </summary>
    private static string BuildAuthorizationUrl(
        OAuthProviderConfig config, string scopes, string redirectUri, string state, string codeChallenge)
    {
        var queryParams = new List<KeyValuePair<string, string>>
        {
            new("client_id", config.ClientId),
            new("redirect_uri", redirectUri),
            new("response_type", "code"),
            new("scope", scopes),
            new("state", state),
            new("code_challenge", codeChallenge),
            new("code_challenge_method", "S256")
        };

        // Merge provider-specific extra parameters (e.g. access_type=offline, prompt=consent for Google)
        if (config.ExtraAuthParameters is not null)
        {
            foreach (var kvp in config.ExtraAuthParameters)
            {
                queryParams.Add(new KeyValuePair<string, string>(kvp.Key, kvp.Value));
            }
        }

        var query = string.Join("&", queryParams.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        var builder = new UriBuilder(config.AuthorizationEndpoint)
        {
            Query = query
        };
        return builder.Uri.ToString();
    }

    /// <summary>
    /// Combines default provider scopes with additional scopes requested for this authorization.
    /// </summary>
    private static string BuildScopes(string defaultScopes, string? additionalScopes)
    {
        if (string.IsNullOrWhiteSpace(additionalScopes))
            return defaultScopes;

        if (string.IsNullOrWhiteSpace(defaultScopes))
            return additionalScopes;

        // Merge and deduplicate scopes
        var scopeSet = new HashSet<string>(
            defaultScopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);

        foreach (var scope in additionalScopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            scopeSet.Add(scope);
        }

        return string.Join(' ', scopeSet);
    }

    /// <summary>
    /// Exchanges an authorization code for access and refresh tokens via the token endpoint,
    /// including the PKCE <c>code_verifier</c> to complete the PKCE flow.
    /// </summary>
    private async Task<TokenResponse> ExchangeCodeForTokensAsync(
        OAuthProviderConfig config, string code, string redirectUri, string codeVerifier)
    {
        var tokenRequest = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = config.ClientId,
            ["client_secret"] = config.ClientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = codeVerifier
        };

        var response = await _httpClient.PostAsync(config.TokenEndpoint, new FormUrlEncodedContent(tokenRequest));
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _log.Error("Token exchange failed with status {StatusCode}", (int)response.StatusCode);
            _log.Debug("Token exchange failure response body: {Body}", responseBody);
            throw new InvalidOperationException(
                $"Token exchange failed with HTTP {(int)response.StatusCode}.");
        }

        var tokenData = JsonSerializer.Deserialize<TokenResponse>(responseBody, JsonOptions);
        if (tokenData is null || string.IsNullOrEmpty(tokenData.AccessToken))
        {
            _log.Debug("Token exchange response did not contain an access token. Response body: {Body}", responseBody);
            throw new InvalidOperationException(
                "Token exchange response did not contain an access token.");
        }

        return tokenData;
    }

    /// <summary>
    /// Refreshes an expired access token using the stored refresh token.
    /// </summary>
    private async Task<bool> RefreshTokenInternalAsync(string provider)
    {
        _log.Debug("Attempting to refresh token for {Provider}", provider);

        var entity = await _db.OAuthCredentials
            .FirstOrDefaultAsync(c => c.ProviderId == provider);

        if (entity is null)
        {
            _log.Warning("No credential found for {Provider} during refresh", provider);
            return false;
        }

        var config = GetProviderConfigOrNull(provider);
        if (config is null)
        {
            _log.Error("No provider config registered for {Provider} — cannot refresh token", provider);
            return false;
        }

        string refreshToken;
        try
        {
            refreshToken = _encryption.Decrypt(entity.RefreshToken);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to decrypt refresh token for {Provider}", provider);
            return false;
        }

        if (string.IsNullOrEmpty(refreshToken))
        {
            _log.Warning("Refresh token is empty for {Provider} — cannot refresh", provider);
            return false;
        }

        // Make the token refresh request
        var tokenRequest = new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = config.ClientId,
            ["client_secret"] = config.ClientSecret,
            ["grant_type"] = "refresh_token"
        };

        try
        {
            var response = await _httpClient.PostAsync(
                config.TokenEndpoint, new FormUrlEncodedContent(tokenRequest));
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _log.Error("Token refresh failed for {Provider} with status {StatusCode}",
                    provider, (int)response.StatusCode);
                _log.Debug("Token refresh failure response body for {Provider}: {Body}",
                    provider, responseBody);
                return false;
            }

            var tokenData = JsonSerializer.Deserialize<TokenResponse>(responseBody, JsonOptions);
            if (tokenData is null || string.IsNullOrEmpty(tokenData.AccessToken))
            {
                _log.Error("Token refresh response for {Provider} did not contain an access token", provider);
                return false;
            }

            // Update the stored credential
            var newAccessToken = _encryption.Encrypt(tokenData.AccessToken);
            // Some providers return a new refresh token; keep the old one if not provided
            var newRefreshToken = !string.IsNullOrEmpty(tokenData.RefreshToken)
                ? _encryption.Encrypt(tokenData.RefreshToken)
                : entity.RefreshToken;

            var newExpiry = DateTime.UtcNow.AddSeconds(tokenData.ExpiresInSeconds > 0
                ? tokenData.ExpiresInSeconds
                : 3600); // Default to 1 hour if not provided

            entity.AccessToken = newAccessToken;
            entity.RefreshToken = newRefreshToken;
            entity.TokenExpiry = newExpiry;
            entity.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _log.Information("Token refreshed successfully for {Provider}, new expiry: {Expiry}",
                provider, newExpiry);
            return true;
        }
        catch (HttpRequestException ex)
        {
            _log.Error(ex, "Network error during token refresh for {Provider}", provider);
            return false;
        }
    }

    /// <summary>
    /// Encrypts tokens and persists the credential to the database.
    /// </summary>
    private async Task<OAuthCredential> PersistCredentialAsync(
        string provider, TokenResponse tokenResponse, string scopes)
    {
        var encryptedAccessToken = _encryption.Encrypt(tokenResponse.AccessToken);
        var encryptedRefreshToken = _encryption.Encrypt(
            tokenResponse.RefreshToken ?? string.Empty);

        var expiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresInSeconds > 0
            ? tokenResponse.ExpiresInSeconds
            : 3600);

        var now = DateTime.UtcNow;

        // Upsert: replace any existing credential for this provider
        var existing = await _db.OAuthCredentials
            .FirstOrDefaultAsync(c => c.ProviderId == provider);

        if (existing is not null)
        {
            existing.AccessToken = encryptedAccessToken;
            existing.RefreshToken = encryptedRefreshToken;
            existing.TokenExpiry = expiry;
            existing.Scopes = scopes;
            existing.UserId = tokenResponse.UserId ?? string.Empty;
            existing.UpdatedAt = now;
        }
        else
        {
            var entity = new OAuthCredentialEntity
            {
                ProviderId = provider,
                AccessToken = encryptedAccessToken,
                RefreshToken = encryptedRefreshToken,
                TokenExpiry = expiry,
                Scopes = scopes,
                UserId = tokenResponse.UserId ?? string.Empty,
                CreatedAt = now,
                UpdatedAt = now
            };

            _db.OAuthCredentials.Add(entity);
        }

        await _db.SaveChangesAsync();

        _log.Information("OAuth credential persisted for {Provider}", provider);

        return new OAuthCredential
        {
            ProviderId = provider,
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken ?? string.Empty,
            TokenExpiry = expiry,
            Scopes = scopes,
            UserId = tokenResponse.UserId ?? string.Empty,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Sends a server-side revocation request to the provider.
    /// </summary>
    private async Task RevokeTokenWithProviderAsync(string revocationEndpoint, string accessToken)
    {
        var revokeParams = new Dictionary<string, string>
        {
            ["token"] = accessToken
        };

        var response = await _httpClient.PostAsync(
            revocationEndpoint, new FormUrlEncodedContent(revokeParams));

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _log.Debug("Server-side revocation failure response: {Body}", body);
            throw new InvalidOperationException(
                $"Server-side revocation failed with HTTP {(int)response.StatusCode}.");
        }
    }

    // ── Private: Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Decrypts an <see cref="OAuthCredentialEntity"/> into a plain <see cref="OAuthCredential"/>.
    /// </summary>
    private OAuthCredential DecryptEntity(OAuthCredentialEntity entity)
    {
        string accessToken;
        string refreshToken;

        try
        {
            accessToken = _encryption.Decrypt(entity.AccessToken);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to decrypt access token for {Provider}", entity.ProviderId);
            throw new InvalidOperationException(
                $"Failed to decrypt the access token for provider '{entity.ProviderId}'. " +
                "The encryption key may have changed or the data is corrupted.", ex);
        }

        try
        {
            refreshToken = _encryption.Decrypt(entity.RefreshToken);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to decrypt refresh token for {Provider}", entity.ProviderId);
            throw new InvalidOperationException(
                $"Failed to decrypt the refresh token for provider '{entity.ProviderId}'. " +
                "The encryption key may have changed or the data is corrupted.", ex);
        }

        return new OAuthCredential
        {
            ProviderId = entity.ProviderId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            TokenExpiry = entity.TokenExpiry,
            Scopes = entity.Scopes,
            UserId = entity.UserId,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    /// <summary>
    /// Gets the provider configuration, throwing if not found.
    /// </summary>
    private OAuthProviderConfig GetProviderConfig(string provider)
    {
        if (_providerConfigs.TryGetValue(provider, out var config))
            return config;

        throw new InvalidOperationException(
            $"No OAuth provider configuration registered for '{provider}'. " +
            $"Call RegisterProvider() before attempting OAuth operations. " +
            $"Registered providers: {string.Join(", ", _providerConfigs.Keys)}");
    }

    /// <summary>
    /// Gets the provider configuration, returning null if not found.
    /// </summary>
    private OAuthProviderConfig? GetProviderConfigOrNull(string provider)
    {
        return _providerConfigs.TryGetValue(provider, out var config) ? config : null;
    }

    /// <summary>
    /// Validates that the provider identifier is not null or whitespace.
    /// </summary>
    private static void ValidateProviderId(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider identifier cannot be null or whitespace.", nameof(provider));
    }

    // ── Private: CSRF & PKCE Helpers ────────────────────────────────────────────

    /// <summary>
    /// Generates a cryptographically random <c>state</c> parameter for CSRF protection
    /// per RFC 6749 Section 10.12. The value is Base64-encoded (32 bytes of entropy).
    /// </summary>
    private static string GenerateState()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    /// <summary>
    /// Generates a PKCE <c>code_verifier</c> per RFC 7636. Uses 32 bytes of
    /// cryptographic randomness, Base64Url-encoded to produce a 43-character verifier.
    /// </summary>
    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    /// <summary>
    /// Computes the PKCE <c>code_challenge</c> from the <c>code_verifier</c>
    /// using SHA-256, per RFC 7636 Section 4.2.
    /// </summary>
    private static string ComputeCodeChallenge(string codeVerifier)
    {
        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(challengeBytes);
    }

    /// <summary>
    /// Encodes bytes using Base64Url encoding (RFC 4648 Section 5) with no padding,
    /// as required by RFC 7636 for PKCE.
    /// </summary>
    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    // ── IDisposable ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Disposes owned resources: the <see cref="HttpClient"/> and all
    /// <see cref="SemaphoreSlim"/> instances in <see cref="_refreshLocks"/>.
    /// The DI container disposes this automatically since it is registered as a singleton.
    /// </summary>
    public void Dispose()
    {
        foreach (var slim in _refreshLocks.Values)
            slim.Dispose();
        _refreshLocks.Clear();

        _httpClient.Dispose();

        _log.Debug("OAuthService disposed");
    }

    // ── Inner: Token Response DTO ───────────────────────────────────────────────

    /// <summary>
    /// DTO for deserializing the OAuth2 token endpoint response.
    /// OAuth2 providers return snake_case JSON fields, so explicit
    /// <see cref="JsonPropertyNameAttribute"/> mappings are required
    /// since <see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/>
    /// only handles case differences, not snake_case-to-PascalCase conversion.
    /// </summary>
    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresInSeconds { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("id_token")]
        public string? IdToken { get; set; }

        // Non-standard field; some providers include user_id in token response
        [JsonPropertyName("user_id")]
        public string? UserId { get; set; }
    }
}

