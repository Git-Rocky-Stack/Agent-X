using AgentX.Core.Services.Settings;
using Serilog;

namespace AgentX.Core.Services.Search;

/// <summary>
/// The <see cref="IWebSearchService"/> to register for the app. A provider built once at startup
/// keeps the settings of that moment; this service reads the current settings on every call, so
/// a new provider, key or SearXNG URL, result limit or cache duration applies to the next search
/// without a restart. See <see cref="WebSearchConfiguration"/> for how settings map to a search:
/// the selected provider only (no fallback to another provider with the same key), the configured
/// result limit as an upper bound, and the configured cache duration.
/// </summary>
public sealed class SettingsAwareWebSearchService : IWebSearchService
{
    private readonly ISettingsService _settingsService;
    private readonly WebSearchCache _cache;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    /// <summary>The last settings seen, for the synchronous properties before settings finish loading.</summary>
    private AppSettings? _lastSettings;

    /// <summary>
    /// Creates the service.
    /// </summary>
    /// <param name="settingsService">Source of the current settings.</param>
    /// <param name="cache">Result cache shared by all providers.</param>
    /// <param name="logger">Serilog logger.</param>
    /// <param name="httpClient">Optional client (tests); a decompressing client is created otherwise.</param>
    public SettingsAwareWebSearchService(
        ISettingsService settingsService,
        WebSearchCache cache,
        ILogger logger,
        HttpClient? httpClient = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger?.ForContext<SettingsAwareWebSearchService>()
                  ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? WebSearchHttp.CreateClient();
    }

    /// <inheritdoc />
    public bool IsConfigured => CurrentConfiguration()?.IsConfigured ?? false;

    /// <inheritdoc />
    public WebSearchProvider ActiveProvider => CurrentConfiguration()?.Provider ?? WebSearchProvider.Brave;

    /// <inheritdoc />
    /// <remarks>
    /// <paramref name="maxResults"/> and the configured result limit both apply; the smaller wins.
    /// </remarks>
    public async Task<WebSearchResponse> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);
        _lastSettings = settings;
        var configuration = WebSearchConfiguration.FromSettings(settings);

        _cache.TtlMinutes = configuration.CacheTtlMinutes;
        var limit = Math.Max(1, Math.Min(maxResults, configuration.MaxResults));

        var response = await CreateProvider(configuration)
            .SearchAsync(query, limit, ct)
            .ConfigureAwait(false);

        // A cached response may have been stored under a higher limit
        return response.Results.Count > limit
            ? response with { Results = response.Results.Take(limit).ToList() }
            : response;
    }

    private IWebSearchService CreateProvider(WebSearchConfiguration configuration) => configuration.Provider switch
    {
        WebSearchProvider.Serper => new SerperSearchService(configuration.ApiKey, _cache, _httpClient, _logger),
        WebSearchProvider.SearXng => new SearXngSearchService(configuration.SearXngUrl?.AbsoluteUri, _cache, _httpClient, _logger),
        _ => new BraveSearchService(configuration.ApiKey, _cache, _httpClient, _logger),
    };

    /// <summary>
    /// The configuration for the synchronous properties. Settings are cached after the first
    /// load, so the lookup normally completes at once; while it is still pending the last
    /// settings seen are used instead of blocking the calling (often UI) thread.
    /// </summary>
    private WebSearchConfiguration? CurrentConfiguration()
    {
        var pending = _settingsService.GetSettingsAsync();
        if (pending.IsCompletedSuccessfully)
        {
            // The task has already completed, so reading Result cannot block or deadlock.
#pragma warning disable VSTHRD002
            _lastSettings = pending.Result;
#pragma warning restore VSTHRD002
        }

        return _lastSettings is null ? null : WebSearchConfiguration.FromSettings(_lastSettings);
    }
}
