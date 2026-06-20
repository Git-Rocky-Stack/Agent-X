using Serilog;

namespace AgentX.Core.Services.Search;

/// <summary>
/// Simple in-memory TTL-based cache for web search results.
/// Thread-safe via lock-based synchronization. Each cache entry
/// is keyed on (query, provider) and expires after a configurable TTL.
/// </summary>
public sealed class WebSearchCache
{
    private readonly Dictionary<(string Query, WebSearchProvider Provider), (WebSearchResponse Response, DateTime ExpiresAt)> _cache = new();
    private readonly object _lock = new();
    private readonly ILogger _logger;

    /// <summary>Default TTL in minutes for cached entries.</summary>
    public const int DefaultTtlMinutes = 60;

    public WebSearchCache(ILogger? logger = null)
    {
        _logger = logger?.ForContext<WebSearchCache>() ?? Serilog.Log.Logger.ForContext<WebSearchCache>();
    }

    /// <summary>
    /// Attempts to retrieve a cached response for the given query and provider.
    /// Returns <c>null</c> if no non-expired entry exists.
    /// </summary>
    public WebSearchResponse? Get(string query, WebSearchProvider provider)
    {
        ArgumentNullException.ThrowIfNull(query);

        lock (_lock)
        {
            if (_cache.TryGetValue((query, provider), out var entry))
            {
                if (DateTime.UtcNow < entry.ExpiresAt)
                {
                    _logger.Debug("WebSearchCache HIT for query '{Query}' (provider={Provider})", query, provider);
                    // Mark as from cache so callers know this wasn't a live request
                    return entry.Response with { FromCache = true };
                }

                // Entry has expired — remove it
                _cache.Remove((query, provider));
                _logger.Debug("WebSearchCache EXPIRED for query '{Query}' (provider={Provider})", query, provider);
            }

            return null;
        }
    }

    /// <summary>
    /// Stores a search response in the cache with an optional custom TTL.
    /// If <paramref name="ttlMinutes"/> is null, <see cref="DefaultTtlMinutes"/> is used.
    /// </summary>
    public void Set(string query, WebSearchProvider provider, WebSearchResponse response, int? ttlMinutes = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(response);

        var effectiveTtl = ttlMinutes ?? DefaultTtlMinutes;
        var expiresAt = DateTime.UtcNow.AddMinutes(effectiveTtl);

        lock (_lock)
        {
            _cache[(query, provider)] = (response, expiresAt);
        }

        _logger.Debug("WebSearchCache SET for query '{Query}' (provider={Provider}, ttl={Ttl}min)", query, provider, effectiveTtl);
    }

    /// <summary>
    /// Removes all cached entries.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
        }

        _logger.Debug("WebSearchCache CLEARED");
    }
}
