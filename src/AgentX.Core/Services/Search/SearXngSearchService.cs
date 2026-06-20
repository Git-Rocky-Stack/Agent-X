using System.Diagnostics;
using System.Text.Json;
using Serilog;

namespace AgentX.Core.Services.Search;

/// <summary>
/// SearXNG (self-hosted meta-search engine) implementation of <see cref="IWebSearchService"/>.
/// Uses the SearXNG JSON API endpoint (/search?format=json).
/// Requires a valid base URL pointing to a running SearXNG instance.
/// </summary>
public sealed class SearXngSearchService : IWebSearchService
{
    private readonly HttpClient _httpClient;
    private readonly string? _baseUrl;
    private readonly WebSearchCache _cache;
    private readonly ILogger _logger;

    public WebSearchProvider ActiveProvider => WebSearchProvider.SearXng;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_baseUrl);

    /// <summary>
    /// Creates a new <see cref="SearXngSearchService"/>.
    /// </summary>
    /// <param name="baseUrl">
    /// Base URL of the SearXNG instance (e.g. "http://localhost:8080").
    /// If null or empty, <see cref="IsConfigured"/> returns false.
    /// </param>
    /// <param name="cache">Optional cache instance; a new one is created if not provided.</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/> (primarily for testing).</param>
    /// <param name="logger">Optional Serilog logger.</param>
    public SearXngSearchService(
        string? baseUrl,
        WebSearchCache? cache = null,
        HttpClient? httpClient = null,
        ILogger? logger = null)
    {
        // Normalize: trim trailing slash
        _baseUrl = baseUrl?.Trim().TrimEnd('/');
        _cache = cache ?? new WebSearchCache(logger);
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger?.ForContext<SearXngSearchService>() ?? Serilog.Log.Logger.ForContext<SearXngSearchService>();
    }

    /// <inheritdoc />
    public async Task<WebSearchResponse> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!IsConfigured)
        {
            _logger.Warning("SearXngSearchService is not configured (missing base URL); returning empty response");
            return EmptyResponse(query);
        }

        // Check cache first
        var cached = _cache.Get(query, WebSearchProvider.SearXng);
        if (cached is not null)
        {
            return cached;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var url = $"{_baseUrl}/search?q={Uri.EscapeDataString(query)}&format=json&pageno=1";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            stopwatch.Stop();

            var results = ParseSearXngResults(json, maxResults);
            var webResponse = new WebSearchResponse
            {
                Query = query,
                Results = results,
                SearchProvider = WebSearchProvider.SearXng,
                SearchDuration = stopwatch.Elapsed,
                FromCache = false
            };

            _cache.Set(query, WebSearchProvider.SearXng, webResponse);

            _logger.Information(
                "SearXNG search completed: {ResultCount} results for '{Query}' in {ElapsedMs:F0}ms",
                results.Count, query, stopwatch.Elapsed.TotalMilliseconds);

            return webResponse;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.Error(ex, "SearXNG search failed for query '{Query}' after {ElapsedMs:F0}ms", query, stopwatch.Elapsed.TotalMilliseconds);

            return new WebSearchResponse
            {
                Query = query,
                Results = Array.Empty<WebSearchResult>(),
                SearchProvider = WebSearchProvider.SearXng,
                SearchDuration = stopwatch.Elapsed,
                FromCache = false
            };
        }
    }

    private static WebSearchResponse EmptyResponse(string query) => new()
    {
        Query = query,
        Results = Array.Empty<WebSearchResult>(),
        SearchProvider = WebSearchProvider.SearXng,
        SearchDuration = TimeSpan.Zero,
        FromCache = false
    };

    private static List<WebSearchResult> ParseSearXngResults(string json, int maxResults)
    {
        var results = new List<WebSearchResult>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("results", out var resultsArray))
            {
                return results;
            }

            foreach (var item in resultsArray.EnumerateArray())
            {
                if (results.Count >= maxResults)
                {
                    break;
                }

                var title = item.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? string.Empty : string.Empty;
                var url = item.TryGetProperty("url", out var urlEl) ? urlEl.GetString() ?? string.Empty : string.Empty;
                var snippet = item.TryGetProperty("content", out var contentEl) ? contentEl.GetString() ?? string.Empty : string.Empty;

                string? domain = null;
                if (item.TryGetProperty("parsed_url", out var parsedUrl) &&
                    parsedUrl.ValueKind == JsonValueKind.Object &&
                    parsedUrl.TryGetProperty("hostname", out var hostnameEl))
                {
                    domain = hostnameEl.GetString();
                }
                else if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    domain = uri.Host;
                }

                DateTime? publishedDate = null;
                // SearXNG doesn't typically provide publication dates in the JSON API

                results.Add(new WebSearchResult
                {
                    Title = title,
                    Url = url,
                    Snippet = snippet,
                    SourceDomain = domain ?? string.Empty,
                    PublishedDate = publishedDate
                });
            }
        }
        catch (JsonException)
        {
            // Return whatever we have; malformed JSON shouldn't crash the pipeline
        }

        return results;
    }
}
