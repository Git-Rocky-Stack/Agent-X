using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentX.Mobile.Models;

namespace AgentX.Mobile.Services;

/// <summary>
/// HTTP client for communicating with the AgentX desktop REST API
/// (Enhancement #16). This is the sole integration layer between the
/// mobile companion and the desktop process.
///
/// Usage: register as a singleton via DI and inject wherever needed.
/// Call <see cref="SetBaseUrl"/> when the user changes the API URL in
/// Settings, or construct with a custom <c>baseUrl</c>.
///
/// Every call returns an <see cref="ApiResult{T}"/>: an unpaired app, a revoked token, an
/// unreachable desktop and a server error are reported as such instead of as empty data.
/// </summary>
public sealed class AgentXApiClient : IDisposable
{
    // -- Constants -------------------------------------------------------------

    private const string DefaultBaseUrl = "http://localhost:9846";
    private const int DefaultTimeoutSeconds = 15;

    /// <summary>The Android emulator's alias for the host machine's loopback interface.</summary>
    private const string EmulatorHostAlias = "10.0.2.2";

    // -- JSON options ----------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // -- State -----------------------------------------------------------------

    private HttpClient _http;
    private string _baseUrl;

    // Token state is read on request threads and written from the UI, so it is guarded by a lock.
    // The Authorization header is set per request; the shared client's default headers are never
    // mutated, so a token change cannot race an in-flight request.
    private readonly object _tokenLock = new();
    private string? _token;
    private bool _tokenSetExplicitly;
    private Task? _persistedTokenLoad;
    private readonly Func<Task<string?>>? _persistedTokenLoader;

    // Optional pairing-established server-certificate pin (SPKI SHA-256, base64). When set,
    // HTTPS connections must present a leaf cert whose public-key SPKI hash matches it; when
    // null, platform default chain validation applies. The client never blanket-accepts
    // certificates (AX-QA-005).
    private string? _pinnedSpkiSha256;

    // -- Constructor -----------------------------------------------------------

    /// <param name="baseUrl">
    /// Optional base URL override. Plaintext HTTP is accepted only for loopback
    /// (localhost / 127.0.0.1 / ::1, or the Android emulator alias 10.0.2.2); any other
    /// host must use HTTPS (e.g., "https://192.168.1.10:9846" across a LAN). See
    /// <see cref="NormalizeBaseUrl"/> (AX-QA-005).
    /// </param>
    /// <param name="token">
    /// Optional bearer token. The desktop API requires this on all data routes; it can also
    /// be supplied later via <see cref="SetToken"/> once the user pairs in Settings.
    /// </param>
    /// <param name="pinnedServerCertSpkiSha256">
    /// Optional pairing-established server-certificate pin (SPKI SHA-256, base64). When set,
    /// HTTPS connections must present a matching leaf certificate; see
    /// <see cref="SetPinnedServerCertificate"/>.
    /// </param>
    /// <param name="persistedTokenLoader">
    /// Optional loader for the token saved at pairing (secure storage). The first request awaits
    /// it, so a request made while the app is still starting is never sent without the token.
    /// A token passed to the constructor or to <see cref="SetToken"/> takes precedence.
    /// </param>
    public AgentXApiClient(
        string? baseUrl = null,
        string? token = null,
        string? pinnedServerCertSpkiSha256 = null,
        Func<Task<string?>>? persistedTokenLoader = null)
    {
        _token = NormalizeToken(token);
        _tokenSetExplicitly = _token is not null;
        _persistedTokenLoader = persistedTokenLoader;
        _pinnedSpkiSha256 = NormalizeToken(pinnedServerCertSpkiSha256);
        _baseUrl = NormalizeBaseUrl(baseUrl ?? DefaultBaseUrl);
        _http = BuildHttpClient(_baseUrl, _pinnedSpkiSha256);
    }

    // -- Configuration ----------------------------------------------------------

    /// <summary>
    /// Updates the base URL at runtime (e.g., after the user saves Settings).
    /// Replaces the underlying <see cref="HttpClient"/> instance.
    /// </summary>
    public void SetBaseUrl(string baseUrl)
    {
        var normalized = NormalizeBaseUrl(baseUrl);
        if (normalized == _baseUrl)
            return;

        var old = _http;
        _baseUrl = normalized;
        _http = BuildHttpClient(_baseUrl, _pinnedSpkiSha256);
        old.Dispose();
    }

    /// <summary>
    /// Sets (or clears) the bearer token sent with every request. The desktop API requires this
    /// token on all data routes: pair by entering the token shown in
    /// AgentX > Settings > Connections. Pass null/empty to unpair.
    /// </summary>
    public void SetToken(string? token)
    {
        lock (_tokenLock)
        {
            _token = NormalizeToken(token);
            _tokenSetExplicitly = true;
        }
    }

    /// <summary>
    /// Pins the desktop server's certificate (SPKI SHA-256, base64) established during pairing.
    /// Once set, HTTPS connections must present a leaf certificate whose public-key SPKI hash
    /// matches; pass null/empty to clear the pin and fall back to platform chain validation.
    /// The client never blanket-accepts certificates (AX-QA-005).
    /// </summary>
    public void SetPinnedServerCertificate(string? spkiSha256Base64)
    {
        _pinnedSpkiSha256 = NormalizeToken(spkiSha256Base64);

        var old = _http;
        _http = BuildHttpClient(_baseUrl, _pinnedSpkiSha256);
        old.Dispose();
    }

    /// <summary>The currently configured base URL.</summary>
    public string BaseUrl => _baseUrl;

    /// <summary>True when a bearer token has been configured (the client is paired).</summary>
    public bool IsPaired
    {
        get
        {
            lock (_tokenLock)
                return !string.IsNullOrEmpty(_token);
        }
    }

    // -- API Methods -----------------------------------------------------------

    /// <summary>
    /// GET /api/health: checks whether the desktop app is reachable, accepts the token, and
    /// returns basic statistics.
    /// </summary>
    public Task<ApiResult<HealthDto>> GetHealthAsync(CancellationToken ct = default) =>
        SendAsync<HealthDto>(HttpMethod.Get, "/api/health", null, ct);

    /// <summary>GET /api/documents: all documents in the knowledge vault.</summary>
    public Task<ApiResult<IReadOnlyList<DocumentDto>>> GetDocumentsAsync(CancellationToken ct = default) =>
        SendListAsync<DocumentDto>(HttpMethod.Get, "/api/documents", null, ct);

    /// <summary>GET /api/documents/{id}: a single document; <see cref="ApiStatus.NotFound"/> when absent.</summary>
    public Task<ApiResult<DocumentDto>> GetDocumentAsync(long id, CancellationToken ct = default) =>
        SendAsync<DocumentDto>(HttpMethod.Get, $"/api/documents/{id}", null, ct);

    /// <summary>GET /api/conversations: all non-archived conversations.</summary>
    public Task<ApiResult<IReadOnlyList<ConversationDto>>> GetConversationsAsync(CancellationToken ct = default) =>
        SendListAsync<ConversationDto>(HttpMethod.Get, "/api/conversations", null, ct);

    /// <summary>GET /api/conversations/{id}: a single conversation; <see cref="ApiStatus.NotFound"/> when absent.</summary>
    public Task<ApiResult<ConversationDto>> GetConversationAsync(long id, CancellationToken ct = default) =>
        SendAsync<ConversationDto>(HttpMethod.Get, $"/api/conversations/{id}", null, ct);

    /// <summary>GET /api/collections: all document collections.</summary>
    public Task<ApiResult<IReadOnlyList<CollectionDto>>> GetCollectionsAsync(CancellationToken ct = default) =>
        SendListAsync<CollectionDto>(HttpMethod.Get, "/api/collections", null, ct);

    /// <summary>
    /// POST /api/search: executes a semantic search against indexed documents.
    /// </summary>
    /// <param name="query">The natural language search query.</param>
    /// <param name="topK">Maximum number of results to return (1-50).</param>
    /// <param name="minScore">Minimum relevance score threshold (0.0 to 1.0).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered results (highest relevance first).</returns>
    public Task<ApiResult<IReadOnlyList<SearchResultDto>>> SearchAsync(
        string query,
        int topK = 10,
        float minScore = 0.3f,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult(ApiResult<IReadOnlyList<SearchResultDto>>.Ok(Array.Empty<SearchResultDto>()));

        var body = new
        {
            query,
            topK = Math.Clamp(topK, 1, 50),
            minScore = Math.Clamp(minScore, 0f, 1f)
        };

        var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        return SendListAsync<SearchResultDto>(HttpMethod.Post, "/api/search", content, ct);
    }

    // --- Request pipeline ---

    private async Task<ApiResult<IReadOnlyList<TItem>>> SendListAsync<TItem>(
        HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        var result = await SendAsync<List<TItem>>(method, path, content, ct).ConfigureAwait(false);
        return result.IsSuccess
            ? ApiResult<IReadOnlyList<TItem>>.Ok(result.Data!)
            : ApiResult<IReadOnlyList<TItem>>.Failure(result.Status, result.ErrorMessage);
    }

    /// <summary>
    /// Sends one request and classifies the outcome. Cancellation by the caller propagates as
    /// <see cref="OperationCanceledException"/>; everything else becomes an <see cref="ApiResult{T}"/>.
    /// </summary>
    private async Task<ApiResult<T>> SendAsync<T>(
        HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        await EnsurePersistedTokenLoadedAsync().ConfigureAwait(false);

        // Snapshot the mutable state once, so a concurrent Settings change cannot mix URLs.
        var http = _http;
        var baseUrl = _baseUrl;
        string? token;
        lock (_tokenLock)
            token = _token;

        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        ApplyEmulatorHostHeader(request, baseUrl);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ApiResult<T>.Failure(ApiStatus.Unreachable,
                $"Agent-X at {baseUrl} did not answer within {DefaultTimeoutSeconds} seconds.");
        }
        catch (Exception ex)
        {
            // No HTTP answer at all: refused, unresolvable, blocked by the platform, or TLS failure.
            return ApiResult<T>.Failure(ApiStatus.Unreachable,
                $"Cannot reach Agent-X at {baseUrl}. Make sure the desktop app is running with the Local API enabled, and see docs/MOBILE-TRANSPORT.md. ({ex.Message})");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return ApiResult<T>.Failure(ApiStatus.Unauthorized,
                    "Not paired: Agent-X rejected the API token (missing, mistyped, or regenerated). Paste the current token from AgentX > Settings > Connections in Settings.");
            }

            ApiResponse<T>? envelope = null;
            try
            {
                envelope = await response.Content
                    .ReadFromJsonAsync<ApiResponse<T>>(JsonOptions, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (response.IsSuccessStatusCode)
                {
                    return ApiResult<T>.Failure(ApiStatus.Error,
                        $"Agent-X sent a response this app cannot read ({ex.Message}).");
                }

                // An error response without a readable body: the status code is reported below.
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
                return ApiResult<T>.Failure(ApiStatus.NotFound, envelope?.Error ?? "Not found.");

            if (!response.IsSuccessStatusCode)
            {
                var detail = string.IsNullOrWhiteSpace(envelope?.Error) ? string.Empty : $": {envelope!.Error}";
                return ApiResult<T>.Failure(ApiStatus.Error,
                    $"Agent-X returned HTTP {(int)response.StatusCode}{detail}");
            }

            if (envelope is not null && envelope.Data is { } data)
                return ApiResult<T>.Ok(data);

            return ApiResult<T>.Failure(ApiStatus.Error, "Agent-X returned an empty response.");
        }
    }

    /// <summary>
    /// Applies the token saved at pairing before the first request. Runs the loader once; a token
    /// set explicitly in the meantime (pairing in Settings) wins over the stored one.
    /// </summary>
    private Task EnsurePersistedTokenLoadedAsync()
    {
        if (_persistedTokenLoader is null)
            return Task.CompletedTask;

        lock (_tokenLock)
            return _persistedTokenLoad ??= LoadPersistedTokenAsync(_persistedTokenLoader);
    }

    private async Task LoadPersistedTokenAsync(Func<Task<string?>> loader)
    {
        string? persisted;
        try
        {
            persisted = await loader().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Secure storage can fail on some devices and emulators; the app is then unpaired
            // and every call reports Unauthorized, which tells the user to pair again.
            persisted = null;
        }

        lock (_tokenLock)
        {
            if (!_tokenSetExplicitly)
                _token = NormalizeToken(persisted);
        }
    }

    /// <summary>
    /// The desktop listener is HTTP.sys bound to the http://localhost:9846/ prefix, and HTTP.sys
    /// matches the Host header against that prefix: a request that says Host: 10.0.2.2:9846 is
    /// answered with 400 "Invalid Hostname". The emulator reaches the host's loopback through the
    /// 10.0.2.2 alias, so requests sent there must still name localhost.
    /// </summary>
    private static void ApplyEmulatorHostHeader(HttpRequestMessage request, string baseUrl)
    {
        var uri = new Uri(baseUrl);
        if (string.Equals(uri.Host, EmulatorHostAlias, StringComparison.Ordinal))
            request.Headers.Host = $"localhost:{uri.Port}";
    }

    // -- Helpers ---------------------------------------------------------------

    private static HttpClient BuildHttpClient(string baseUrl, string? pinnedSpkiSha256)
    {
        var handler = new HttpClientHandler();

        // Never blanket-accept certificates. The previous DangerousAcceptAnyServerCertificateValidator
        // permitted trivial interception. When a pairing-established SPKI pin is configured, the leaf
        // certificate must match it; otherwise defer to the platform's default chain validation.
        // Loopback connections are HTTP and never reach this callback (AX-QA-005).
        if (!string.IsNullOrEmpty(pinnedSpkiSha256))
        {
            handler.ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                cert is not null && CertMatchesPin(cert, pinnedSpkiSha256);
        }

        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds)
        };
    }

    /// <summary>
    /// Normalizes and validates the base URL. Plaintext HTTP is permitted only to a loopback host
    /// (localhost / 127.0.0.1 / ::1) or the Android emulator's host-loopback alias (10.0.2.2),
    /// neither of which leaves the device. Every other host MUST use HTTPS so the bearer token and
    /// private document/search/conversation payloads are encrypted in transit (AX-QA-005).
    /// </summary>
    /// <exception cref="ArgumentException">The URL is malformed, uses an unsupported scheme, or is
    /// plaintext HTTP to a non-loopback host.</exception>
    private static string NormalizeBaseUrl(string url)
    {
        var trimmed = (url ?? string.Empty).Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Invalid API URL: '{url}'.", nameof(url));

        var isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        if (!isHttps && !isHttp)
            throw new ArgumentException($"API URL must use http or https: '{url}'.", nameof(url));

        if (isHttp && !IsLocalLoopback(uri))
            throw new ArgumentException(
                $"Refusing plaintext HTTP to non-loopback host '{uri.Host}'. Use https:// for LAN or remote connections.",
                nameof(url));

        return trimmed;
    }

    /// <summary>True for loopback hosts and the Android emulator host-loopback alias (10.0.2.2).</summary>
    private static bool IsLocalLoopback(Uri uri) =>
        uri.IsLoopback || string.Equals(uri.Host, EmulatorHostAlias, StringComparison.Ordinal);

    /// <summary>
    /// Constant-time comparison of the certificate's SubjectPublicKeyInfo SHA-256 (base64) against
    /// the configured pin. Pinning the SPKI (not the whole cert) survives renewal with the same key.
    /// </summary>
    private static bool CertMatchesPin(X509Certificate2 cert, string expectedSpkiSha256Base64)
    {
        var spkiHash = SHA256.HashData(cert.PublicKey.ExportSubjectPublicKeyInfo());
        var actual = Convert.ToBase64String(spkiHash);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(actual),
            Encoding.ASCII.GetBytes(expectedSpkiSha256Base64));
    }

    private static string? NormalizeToken(string? token) =>
        string.IsNullOrWhiteSpace(token) ? null : token.Trim();

    // -- IDisposable -----------------------------------------------------------

    public void Dispose() => _http.Dispose();
}
