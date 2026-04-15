using AgentX.Core.Services.Settings;
using Serilog;

namespace AgentX.Core.Services.Search;

/// <summary>
/// Factory that creates and manages <see cref="IWebSearchService"/> instances for
/// all supported <see cref="WebSearchProvider"/> values. Also resolves the best
/// configured service based on <see cref="AppSettings"/>.
/// </summary>
public sealed class WebSearchServiceFactory
{
    private readonly Dictionary<WebSearchProvider, IWebSearchService> _services;
    private readonly WebSearchCache _sharedCache;

    /// <summary>
    /// Creates a new factory with provider instances configured from the given credentials.
    /// A shared <see cref="WebSearchCache"/> is created and used by all provider instances.
    /// </summary>
    /// <param name="braveApiKey">Brave Search API key (or null if not configured).</param>
    /// <param name="serperApiKey">Serper.dev API key (or null if not configured).</param>
    /// <param name="searxngUrl">SearXNG instance base URL (or null if not configured).</param>
    /// <param name="logger">Optional Serilog logger.</param>
    public WebSearchServiceFactory(
        string? braveApiKey,
        string? serperApiKey,
        string? searxngUrl,
        ILogger? logger = null)
    {
        _sharedCache = new WebSearchCache(logger);

        _services = new Dictionary<WebSearchProvider, IWebSearchService>
        {
            [WebSearchProvider.Brave] = new BraveSearchService(braveApiKey, _sharedCache, logger: logger),
            [WebSearchProvider.Serper] = new SerperSearchService(serperApiKey, _sharedCache, logger: logger),
            [WebSearchProvider.SearXng] = new SearXngSearchService(searxngUrl, _sharedCache, logger: logger)
        };
    }

    /// <summary>
    /// Returns the <see cref="IWebSearchService"/> for the specified <paramref name="provider"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="provider"/> is not a recognised <see cref="WebSearchProvider"/>.
    /// </exception>
    public IWebSearchService GetService(WebSearchProvider provider) =>
        _services.TryGetValue(provider, out var service)
            ? service
            : throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported web search provider.");

    /// <summary>
    /// Returns the best available <see cref="IWebSearchService"/> based on <paramref name="settings"/>.
    /// If the preferred provider is not configured, falls back to the first configured provider.
    /// If no provider is configured, returns the preferred provider (which will return empty results).
    /// </summary>
    public IWebSearchService GetConfiguredService(AppSettings settings)
    {
        var preferredService = GetService(settings.WebSearchProvider);

        if (preferredService.IsConfigured)
        {
            return preferredService;
        }

        // Fall back to whichever provider is actually configured
        foreach (var service in _services.Values)
        {
            if (service.IsConfigured)
            {
                return service;
            }
        }

        // Nothing configured — return the preferred provider (it will return empty results)
        return preferredService;
    }

    /// <summary>
    /// Clears the shared cache used by all provider instances.
    /// </summary>
    public void ClearCache() => _sharedCache.Clear();
}