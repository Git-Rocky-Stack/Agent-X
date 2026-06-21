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
/// Settings, or construct with a custom <paramref name="baseUrl"/>.
/// </summary>
public sealed class AgentXApiClient : IDisposable
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const string DefaultBaseUrl = "http://localhost:9846";
    private const int DefaultTimeoutSeconds = 15;

    // ── JSON options ──────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ── State ─────────────────────────────────────────────────────────────────

    private HttpClient _http;
    private string _baseUrl;
    private string? _token;

    // Optional pairing-established server-certificate pin (SPKI SHA-256, base64). When set,
    // HTTPS connections must present a leaf cert whose public-key SPKI hash matches it; when
    // null, platform default chain validation applies. The client never blanket-accepts
    // certificates (AX-QA-005).
    private string? _pinnedSpkiSha256;

    // ── Constructor ───────────────────────────────────────────────────────────

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
    public AgentXApiClient(string? baseUrl = null, string? token = null, string? pinnedServerCertSpkiSha256 = null)
    {
        _token = NormalizeToken(token);
        _pinnedSpkiSha256 = NormalizeToken(pinnedServerCertSpkiSha256);
        _baseUrl = NormalizeBaseUrl(baseUrl ?? DefaultBaseUrl);
        _http = BuildHttpClient(_baseUrl, _pinnedSpkiSha256);
        ApplyAuth(_http, _token);
    }

    // ── Configuration ──────────────────────────────────────────────────────────

    /// <summary>
    /// Updates the base URL at runtime (e.g., after the user saves Settings).
    /// Replaces the underlying <see cref="HttpClient"/> instance, re-applying the token.
    /// </summary>
    public void SetBaseUrl(string baseUrl)
    {
        var normalized = NormalizeBaseUrl(baseUrl);
        if (normalized == _baseUrl)
            return;

        var old = _http;
        _baseUrl = normalized;
        _http = BuildHttpClient(_baseUrl, _pinnedSpkiSha256);
        ApplyAuth(_http, _token);
        old.Dispose();
    }

    /// <summary>
    /// Sets (or clears) the bearer token sent with every request. The desktop API requires this
    /// token on all data routes — pair by entering the token shown in
    /// AgentX → Settings → Connections. Pass null/empty to unpair.
    /// </summary>
    public void SetToken(string? token)
    {
        _token = NormalizeToken(token);
        ApplyAuth(_http, _token);
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
        ApplyAuth(_http, _token);
        old.Dispose();
    }

    /// <summary>The currently configured base URL.</summary>
    public string BaseUrl => _baseUrl;

    /// <summary>True when a bearer token has been configured (the client is paired).</summary>
    public bool IsPaired => !string.IsNullOrEmpty(_token);

    // ── API Methods ───────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/health — checks whether the desktop app is reachable and
    /// returns basic statistics.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The health payload, or null when the host is unreachable.</returns>
    public async Task<HealthDto?> GetHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/api/health", ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var envelope = await response.Content
                .ReadFromJsonAsync<ApiResponse<HealthDto>>(JsonOptions, ct)
                .ConfigureAwait(false);

            return envelope?.Data;
        }
        catch (Exception ex) when (IsNetworkException(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// GET /api/documents — returns all documents in the knowledge vault.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of documents, or empty list on error.</returns>
    public async Task<IReadOnlyList<DocumentDto>> GetDocumentsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/api/documents", ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var envelope = await response.Content
                .ReadFromJsonAsync<ApiResponse<List<DocumentDto>>>(JsonOptions, ct)
                .ConfigureAwait(false);

            return envelope?.Data ?? [];
        }
        catch (Exception ex) when (IsNetworkException(ex))
        {
            return [];
        }
    }

    /// <summary>
    /// GET /api/documents/{id} — returns a single document by ID.
    /// </summary>
    /// <param name="id">The document primary key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document DTO, or null when not found or unreachable.</returns>
    public async Task<DocumentDto?> GetDocumentAsync(long id, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"/api/documents/{id}", ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            var envelope = await response.Content
                .ReadFromJsonAsync<ApiResponse<DocumentDto>>(JsonOptions, ct)
                .ConfigureAwait(false);

            return envelope?.Data;
        }
        catch (Exception ex) when (IsNetworkException(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// GET /api/conversations — returns all non-archived conversations.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of conversations, or empty list on error.</returns>
    public async Task<IReadOnlyList<ConversationDto>> GetConversationsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/api/conversations", ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var envelope = await response.Content
                .ReadFromJsonAsync<ApiResponse<List<ConversationDto>>>(JsonOptions, ct)
                .ConfigureAwait(false);

            return envelope?.Data ?? [];
        }
        catch (Exception ex) when (IsNetworkException(ex))
        {
            return [];
        }
    }

    /// <summary>
    /// GET /api/conversations/{id} — returns a single conversation by ID.
    /// </summary>
    /// <param name="id">The conversation primary key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The conversation DTO, or null when not found or unreachable.</returns>
    public async Task<ConversationDto?> GetConversationAsync(long id, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"/api/conversations/{id}", ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            var envelope = await response.Content
                .ReadFromJsonAsync<ApiResponse<ConversationDto>>(JsonOptions, ct)
                .ConfigureAwait(false);

            return envelope?.Data;
        }
        catch (Exception ex) when (IsNetworkException(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// GET /api/collections — returns all document collections.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of collections, or empty list on error.</returns>
    public async Task<IReadOnlyList<CollectionDto>> GetCollectionsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/api/collections", ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var envelope = await response.Content
                .ReadFromJsonAsync<ApiResponse<List<CollectionDto>>>(JsonOptions, ct)
                .ConfigureAwait(false);

            return envelope?.Data ?? [];
        }
        catch (Exception ex) when (IsNetworkException(ex))
        {
            return [];
        }
    }

    /// <summary>
    /// POST /api/search — executes a semantic search against indexed documents.
    /// </summary>
    /// <param name="query">The natural language search query.</param>
    /// <param name="topK">Maximum number of results to return (1-50).</param>
    /// <param name="minScore">Minimum relevance score threshold (0.0 – 1.0).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of search results (highest relevance first), or empty on error.</returns>
    public async Task<IReadOnlyList<SearchResultDto>> SearchAsync(
        string query,
        int topK = 10,
        float minScore = 0.3f,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        try
        {
            var body = new
            {
                query,
                topK = Math.Clamp(topK, 1, 50),
                minScore = Math.Clamp(minScore, 0f, 1f)
            };

            var json = JsonSerializer.Serialize(body, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync("/api/search", content, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var envelope = await response.Content
                .ReadFromJsonAsync<ApiResponse<List<SearchResultDto>>>(JsonOptions, ct)
                .ConfigureAwait(false);

            return envelope?.Data ?? [];
        }
        catch (Exception ex) when (IsNetworkException(ex))
        {
            return [];
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static HttpClient BuildHttpClient(string baseUrl, string? pinnedSpkiSha256)
    {
        var handler = new HttpClientHandler();

        // Never blanket-accept certificates — the previous DangerousAcceptAnyServerCertificateValidator
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
        uri.IsLoopback || string.Equals(uri.Host, "10.0.2.2", StringComparison.Ordinal);

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

    /// <summary>Applies (or clears) the bearer Authorization header on the given client.</summary>
    private static void ApplyAuth(HttpClient http, string? token)
    {
        http.DefaultRequestHeaders.Authorization = string.IsNullOrEmpty(token)
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Returns true for transient network errors that should surface as an empty
    /// result rather than an unhandled exception.
    /// </summary>
    private static bool IsNetworkException(Exception ex) =>
        ex is HttpRequestException
            or TaskCanceledException
            or OperationCanceledException
            or JsonException
            or InvalidOperationException;

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose() => _http.Dispose();
}
