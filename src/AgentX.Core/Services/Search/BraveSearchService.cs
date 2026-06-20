using System.Diagnostics;
using System.Text.Json;
using Serilog;

namespace AgentX.Core.Services.Search;

/// <summary>
/// Brave Search API implementation of <see cref="IWebSearchService"/>.
/// Uses the Brave Web Search API (https://api.search.brave.com/res/v1/web/search).
/// Requires a valid API key set via constructor or configuration.
/// </summary>
public sealed class BraveSearchService : IWebSearchService
{
    private const string BraveApiBaseUrl = "https://api.search.brave.com/res/v1/web/search";
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly WebSearchCache _cache;
    private readonly ILogger _logger;

    public WebSearchProvider ActiveProvider => WebSearchProvider.Brave;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    /// <summary>
    /// Creates a new <see cref="BraveSearchService"/>.
    /// </summary>
    /// <param name="apiKey">Brave Search API key. If null or empty, <see cref="IsConfigured"/> returns false.</param>
    /// <param name="cache">Optional cache instance; a new one is created if not provided.</param>
    /// <param name="httpClient">Optional <see cref="HttpClient"/> (primarily for testing).</param>
    /// <param name="logger">Optional Serilog logger.</param>
    public BraveSearchService(
        string? apiKey,
        WebSearchCache? cache = null,
        HttpClient? httpClient = null,
        ILogger? logger = null)
    {
        _apiKey = apiKey?.Trim();
        _cache = cache ?? new WebSearchCache(logger);
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger?.ForContext<BraveSearchService>() ?? Serilog.Log.Logger.ForContext<BraveSearchService>();
    }

    /// <inheritdoc />
    public async Task<WebSearchResponse> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!IsConfigured)
        {
            _logger.Warning("BraveSearchService is not configured (missing API key); returning empty response");
            return EmptyResponse(query);
        }

        // Check cache first
        var cached = _cache.Get(query, WebSearchProvider.Brave);
        if (cached is not null)
        {
            return cached;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var url = $"{BraveApiBaseUrl}?q={Uri.EscapeDataString(query)}&count={maxResults}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("Accept-Encoding", "gzip");
            request.Headers.Add("X-Subscription-Token", _apiKey!);

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            stopwatch.Stop();

            var results = ParseBraveResults(json);
            var webResponse = new WebSearchResponse
            {
                Query = query,
                Results = results,
                SearchProvider = WebSearchProvider.Brave,
                SearchDuration = stopwatch.Elapsed,
                FromCache = false
            };

            _cache.Set(query, WebSearchProvider.Brave, webResponse);

            _logger.Information(
                "Brave search completed: {ResultCount} results for '{Query}' in {ElapsedMs:F0}ms",
                results.Count, query, stopwatch.Elapsed.TotalMilliseconds);

            return webResponse;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.Error(ex, "Brave search failed for query '{Query}' after {ElapsedMs:F0}ms", query, stopwatch.Elapsed.TotalMilliseconds);

            return new WebSearchResponse
            {
                Query = query,
                Results = Array.Empty<WebSearchResult>(),
                SearchProvider = WebSearchProvider.Brave,
                SearchDuration = stopwatch.Elapsed,
                FromCache = false
            };
        }
    }

    private static WebSearchResponse EmptyResponse(string query) => new()
    {
        Query = query,
        Results = Array.Empty<WebSearchResult>(),
        SearchProvider = WebSearchProvider.Brave,
        SearchDuration = TimeSpan.Zero,
        FromCache = false
    };

    private static List<WebSearchResult> ParseBraveResults(string json)
    {
        var results = new List<WebSearchResult>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var web = doc.RootElement.GetProperty("web");

            if (web.TryGetProperty("results", out var resultsArray))
            {
                foreach (var item in resultsArray.EnumerateArray())
                {
                    var title = item.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? string.Empty : string.Empty;
                    var url = item.TryGetProperty("url", out var urlEl) ? urlEl.GetString() ?? string.Empty : string.Empty;
                    var snippet = item.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? string.Empty : string.Empty;

                    string? domain = null;
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    {
                        domain = uri.Host;
                    }

                    DateTime? publishedDate = null;
                    if (item.TryGetProperty("age", out var ageEl) && ageEl.ValueKind == JsonValueKind.String)
                    {
                        // Brave returns age as a relative string like "2 days ago" — not parseable as DateTime
                        // Leave publishedDate null; could be enhanced later
                    }

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
        }
        catch (JsonException)
        {
            // Return whatever we have; malformed JSON shouldn't crash the pipeline
        }

        return results;
    }
}
