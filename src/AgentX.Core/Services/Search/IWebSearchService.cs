namespace AgentX.Core.Services.Search;

/// <summary>
/// Abstraction over web search providers used by Deep Research Mode.
/// Each provider implementation wraps a specific search API
/// (Brave, Serper, SearXNG) and returns normalised <see cref="WebSearchResponse"/> results.
/// </summary>
public interface IWebSearchService
{
    /// <summary>
    /// Executes a web search and returns matching results.
    /// </summary>
    /// <param name="query">The natural-language search query.</param>
    /// <param name="maxResults">Maximum number of results to return (default 10).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="WebSearchResponse"/> containing the search results and metadata.</returns>
    Task<WebSearchResponse> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default);

    /// <summary>
    /// Whether this service has been configured with valid credentials/URL.
    /// Unconfigured services should return an empty response rather than throwing.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// The <see cref="WebSearchProvider"/> this instance wraps.
    /// </summary>
    WebSearchProvider ActiveProvider { get; }
}
