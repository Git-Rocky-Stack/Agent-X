namespace AgentX.Core.Services.Web;

/// <summary>
/// Provides headless browser rendering of JavaScript-heavy web pages using Playwright.
/// Used as a fallback when readability-based extraction returns minimal content,
/// ensuring that dynamically-rendered pages can still be ingested into the Knowledge Vault.
/// </summary>
public interface IJsRenderingService
{
    /// <summary>
    /// Renders the given URL in a headless Chromium browser and returns the fully-rendered HTML.
    /// </summary>
    /// <param name="url">The absolute HTTP or HTTPS URL to render.</param>
    /// <param name="waitForNetworkIdle">
    /// If <c>true</c>, waits until the network is idle (no new requests for 500ms) before
    /// extracting content. This is useful for pages that load content via AJAX/fetch calls
    /// after the initial page load. If <c>false</c>, waits only for the DOMContentLoaded event.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The fully-rendered HTML of the page, or <see cref="string.Empty"/> if rendering fails.</returns>
    Task<string> RenderPageAsync(string url, bool waitForNetworkIdle = false, CancellationToken ct = default);
}