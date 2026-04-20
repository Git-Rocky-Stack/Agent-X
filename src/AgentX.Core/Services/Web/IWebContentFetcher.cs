namespace AgentX.Core.Services.Web;

/// <summary>
/// Fetches raw HTML content from web URLs. Handles HTTP client configuration,
/// redirect following, decompression, timeout management, and optional JS rendering
/// fallback for JavaScript-heavy pages.
/// <para>
/// Extracted from <see cref="WebScraperService"/> to separate HTTP fetching concerns
/// from content extraction/parsing concerns, following the Single Responsibility Principle.
/// </para>
/// </summary>
public interface IWebContentFetcher
{
    /// <summary>
    /// Fetches the HTML content from the specified URL.
    /// </summary>
    /// <param name="url">The absolute HTTP or HTTPS URL to fetch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="FetchResult"/> containing the HTML, final URL after redirects,
    /// elapsed time, and whether JS rendering was used.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="url"/> is null, empty, or not a valid HTTP/HTTPS URL.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the cancellation token is triggered.</exception>
    /// <exception cref="HttpRequestException">Thrown when the HTTP request fails with a non-success status code.</exception>
    /// <exception cref="TimeoutException">Thrown when the request exceeds the configured timeout.</exception>
    Task<FetchResult> FetchAsync(string url, CancellationToken ct = default);
}

/// <summary>
/// Result of an HTTP content fetch operation.
/// </summary>
/// <param name="Html">The HTML content of the page, or an empty string if the fetch failed.</param>
/// <param name="FinalUrl">The final URL after following any redirects. Null if the URL could not be determined.</param>
/// <param name="Elapsed">The total elapsed time for the fetch operation, including any JS rendering fallback.</param>
/// <param name="UsedJsRendering">Whether JavaScript rendering (headless browser) was used to obtain the content.</param>
public record FetchResult(string Html, string? FinalUrl, TimeSpan Elapsed, bool UsedJsRendering);
