using AgentX.Core.Services.Settings;

namespace AgentX.Core.Services.Search;

/// <summary>
/// Web search as the current settings configure it. Settings have one credential field for web
/// search, <see cref="AppSettings.WebSearchApiKey"/>, and it belongs to the selected provider only:
/// the API key for Brave or Serper, or the instance URL for SearXNG. Used both to run searches and
/// to disclose where queries go, so the two can never disagree.
/// </summary>
/// <param name="Provider">The provider selected in settings; there is no fallback to another one.</param>
/// <param name="ApiKey">The Brave or Serper API key; null for SearXNG or when none is set.</param>
/// <param name="SearXngUrl">The SearXNG instance URL; null unless SearXNG is selected with a valid http(s) URL.</param>
/// <param name="MaxResults">The configured result limit, 1 to <see cref="MaxAllowedResults"/>.</param>
/// <param name="CacheTtlMinutes">How long results are cached, 1 to 1440 minutes.</param>
public sealed record WebSearchConfiguration(
    WebSearchProvider Provider,
    string? ApiKey,
    Uri? SearXngUrl,
    int MaxResults,
    int CacheTtlMinutes)
{
    /// <summary>Result limit used when the setting is missing or not positive.</summary>
    public const int DefaultMaxResults = 10;

    /// <summary>Highest result limit the settings page offers.</summary>
    public const int MaxAllowedResults = 20;

    /// <summary>Longest cache duration the settings page offers (one day).</summary>
    public const int MaxCacheTtlMinutes = 1440;

    /// <summary>True when the selected provider has what it needs to search.</summary>
    public bool IsConfigured => Provider == WebSearchProvider.SearXng
        ? SearXngUrl is not null
        : !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Reads the web search configuration from a settings snapshot.</summary>
    public static WebSearchConfiguration FromSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var value = settings.WebSearchApiKey?.Trim();
        var isSearXng = settings.WebSearchProvider == WebSearchProvider.SearXng;

        Uri? searXngUrl = null;
        if (isSearXng
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            searXngUrl = uri;
        }

        return new WebSearchConfiguration(
            settings.WebSearchProvider,
            isSearXng || string.IsNullOrWhiteSpace(value) ? null : value,
            searXngUrl,
            settings.MaxSearchResults > 0 ? Math.Min(settings.MaxSearchResults, MaxAllowedResults) : DefaultMaxResults,
            settings.SearchCacheTtlMinutes > 0
                ? Math.Min(settings.SearchCacheTtlMinutes, MaxCacheTtlMinutes)
                : WebSearchCache.DefaultTtlMinutes);
    }
}
