using System.Net.Http.Json;
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

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="baseUrl">
    /// Optional base URL override (e.g., "http://192.168.1.10:9846" when connecting
    /// across a LAN rather than localhost).
    /// </param>
    public AgentXApiClient(string? baseUrl = null)
    {
        _baseUrl = NormalizeBaseUrl(baseUrl ?? DefaultBaseUrl);
        _http = BuildHttpClient(_baseUrl);
    }

    // ── Configuration ──────────────────────────────────────────────────────────

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
        _http = BuildHttpClient(_baseUrl);
        old.Dispose();
    }

    /// <summary>The currently configured base URL.</summary>
    public string BaseUrl => _baseUrl;

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

    private static HttpClient BuildHttpClient(string baseUrl)
    {
        var handler = new HttpClientHandler
        {
            // Allow self-signed certs on LAN (the API is HTTP-only by design)
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds)
        };
    }

    private static string NormalizeBaseUrl(string url)
    {
        // Trim trailing slashes so path concatenation is predictable
        return url.TrimEnd('/');
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
