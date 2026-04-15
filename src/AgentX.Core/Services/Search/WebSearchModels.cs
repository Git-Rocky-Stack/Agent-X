namespace AgentX.Core.Services.Search;

/// <summary>
/// Supported web search providers for Deep Research Mode.
/// Each provider requires different configuration (API key vs. self-hosted URL).
/// </summary>
public enum WebSearchProvider
{
    /// <summary>Brave Search API (requires API key).</summary>
    Brave,

    /// <summary>Google via Serper.dev (requires API key).</summary>
    Serper,

    /// <summary>Self-hosted SearXNG instance (requires base URL).</summary>
    SearXng
}

/// <summary>
/// A single web search result returned by a search provider.
/// </summary>
public sealed class WebSearchResult
{
    /// <summary>Title of the web page.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Absolute URL of the web page.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Short snippet/extract from the search engine.</summary>
    public string Snippet { get; init; } = string.Empty;

    /// <summary>Extracted domain name (e.g. "arxiv.org").</summary>
    public string SourceDomain { get; init; } = string.Empty;

    /// <summary>Publication date if available from the search provider.</summary>
    public DateTime? PublishedDate { get; init; }

    /// <summary>Raw HTML or full-text content fetched from the result page (populated on demand).</summary>
    public string? RawContent { get; init; }
}

/// <summary>
/// The aggregated response from a web search, including metadata about the search itself.
/// </summary>
public sealed record WebSearchResponse
{
    /// <summary>The original query string that produced these results.</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>Ordered list of search results.</summary>
    public IReadOnlyList<WebSearchResult> Results { get; init; } = Array.Empty<WebSearchResult>();

    /// <summary>The search provider that produced these results.</summary>
    public WebSearchProvider SearchProvider { get; init; }

    /// <summary>Time taken for the search request.</summary>
    public TimeSpan SearchDuration { get; init; }

    /// <summary>Whether this response was served from the local cache.</summary>
    public bool FromCache { get; init; }
}