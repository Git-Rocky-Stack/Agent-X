using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Serilog;

namespace AgentX.Core.Services.Search;

/// <summary>
/// Google Search via Serper.dev implementation of <see cref="IWebSearchService"/>.
/// Uses the Serper API (https://google.serper.dev/search).
/// Requires a valid API key set via constructor or configuration.
/// </summary>
public sealed class SerperSearchService : IWebSearchService
{
    private const string SerperApiBaseUrl = "https://google.serper.dev/search";
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly WebSearchCache _cache;
    private readonly ILogger _logger;

    public WebSearchProvider ActiveProvider => WebSearchProvider.Serper;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    /// <summary>
    /// Creates a new <see cref="SerperSearchService"/>.
    /// </summary>
    /// <param name="apiKey">Serper API key. If null or empty, <see cref="IsConfigured"/> returns false.</param>
    /// <param name="cache">Optional cache instance; a new one is created if not provided.</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/> (primarily for testing).</param>
    /// <param name="logger">Optional Serilog logger.</param>
    public SerperSearchService(
        string? apiKey,
        WebSearchCache? cache = null,
        HttpClient? httpClient = null,
        ILogger? logger = null)
    {
        _apiKey = apiKey?.Trim();
        _cache = cache ?? new WebSearchCache(logger);
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger?.ForContext<SerperSearchService>() ?? Serilog.Log.Logger.ForContext<SerperSearchService>();
    }

    /// <inheritdoc />
    public async Task<WebSearchResponse> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!IsConfigured)
        {
            _logger.Warning("SerperSearchService is not configured (missing API key); returning empty response");
            return EmptyResponse(query);
        }

        // Check cache first
        var cached = _cache.Get(query, WebSearchProvider.Serper);
        if (cached is not null)
        {
            return cached;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var requestBody = new { q = query, num = maxResults };
            var jsonBody = JsonSerializer.Serialize(requestBody);

            using var request = new HttpRequestMessage(HttpMethod.Post, SerperApiBaseUrl);
            request.Headers.Add("X-API-KEY", _apiKey!);
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            stopwatch.Stop();

            var results = ParseSerperResults(json);
            var webResponse = new WebSearchResponse
            {
                Query = query,
                Results = results,
                SearchProvider = WebSearchProvider.Serper,
                SearchDuration = stopwatch.Elapsed,
                FromCache = false
            };

            _cache.Set(query, WebSearchProvider.Serper, webResponse);

            _logger.Information(
                "Serper search completed: {ResultCount} results for '{Query}' in {ElapsedMs:F0}ms",
                results.Count, query, stopwatch.Elapsed.TotalMilliseconds);

            return webResponse;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.Error(ex, "Serper search failed for query '{Query}' after {ElapsedMs:F0}ms", query, stopwatch.Elapsed.TotalMilliseconds);

            return new WebSearchResponse
            {
                Query = query,
                Results = Array.Empty<WebSearchResult>(),
                SearchProvider = WebSearchProvider.Serper,
                SearchDuration = stopwatch.Elapsed,
                FromCache = false
            };
        }
    }

    private static WebSearchResponse EmptyResponse(string query) => new()
    {
        Query = query,
        Results = Array.Empty<WebSearchResult>(),
        SearchProvider = WebSearchProvider.Serper,
        SearchDuration = TimeSpan.Zero,
        FromCache = false
    };

    private static List<WebSearchResult> ParseSerperResults(string json)
    {
        var results = new List<WebSearchResult>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Parse organic results
            if (root.TryGetProperty("organic", out var organic))
            {
                foreach (var item in organic.EnumerateArray())
                {
                    var title = item.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? string.Empty : string.Empty;
                    var link = item.TryGetProperty("link", out var linkEl) ? linkEl.GetString() ?? string.Empty : string.Empty;
                    var snippet = item.TryGetProperty("snippet", out var snippetEl) ? snippetEl.GetString() ?? string.Empty : string.Empty;

                    string? domain = null;
                    if (Uri.TryCreate(link, UriKind.Absolute, out var uri))
                    {
                        domain = uri.Host;
                    }

                    DateTime? publishedDate = null;
                    if (item.TryGetProperty("date", out var dateEl) && dateEl.ValueKind == JsonValueKind.String)
                    {
                        _ = DateTime.TryParse(dateEl.GetString(), out var parsed);
                        publishedDate = parsed != default ? parsed : null;
                    }

                    results.Add(new WebSearchResult
                    {
                        Title = title,
                        Url = link,
                        Snippet = snippet,
                        SourceDomain = domain ?? string.Empty,
                        PublishedDate = publishedDate
                    });
                }
            }

            // Also parse knowledge graph if present (often has high-quality results)
            if (root.TryGetProperty("knowledgeGraph", out var kg))
            {
                // Knowledge graph is a single result, not an array — skip for now
                // as it's usually a summary rather than a linkable source
            }
        }
        catch (JsonException)
        {
            // Return whatever we have; malformed JSON shouldn't crash the pipeline
        }

        return results;
    }
}