using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AgentX.Core.Services.Web;

/// <summary>
/// Headless Chromium rendering service powered by Microsoft.Playwright.
/// Renders JavaScript-heavy web pages that cannot be parsed by static HTML extraction,
/// returning the fully-executed DOM as HTML for downstream readability processing.
/// <para>
/// Implements <see cref="IDisposable"/> to properly release the Playwright browser process
/// and its associated resources when no longer needed.
/// </para>
/// </summary>
public sealed class JsRenderingService : IJsRenderingService, IDisposable
{
    private readonly ILogger<JsRenderingService> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    /// <summary>
    /// Initializes a new instance of <see cref="JsRenderingService"/>.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic output. Falls back to a null logger if not provided.</param>
    public JsRenderingService(ILogger<JsRenderingService>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<JsRenderingService>.Instance;
    }

    /// <inheritdoc />
    public async Task<string> RenderPageAsync(string url, bool waitForNetworkIdle = false, CancellationToken ct = default)
    {
        await EnsureBrowserAsync();

        var page = await _browser!.NewPageAsync(new BrowserNewPageOptions
        {
            UserAgent = "Agent-X/1.5.0 (Knowledge Vault Web Clipper)"
        });

        try
        {
            var response = await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = waitForNetworkIdle ? WaitUntilState.NetworkIdle : WaitUntilState.DOMContentLoaded
            });

            if (response == null || !response.Ok)
            {
                _logger.LogWarning("Failed to render {Url}: HTTP {Status}", url, response?.Status ?? 0);
                return string.Empty;
            }

            var content = await page.ContentAsync();
            return content;
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>
    /// Lazily initializes the Playwright browser instance on first use.
    /// Subsequent calls are no-ops if the browser is already running.
    /// </summary>
    private async Task EnsureBrowserAsync()
    {
        if (_browser != null) return;

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    /// <summary>
    /// Disposes the Playwright browser and Playwright instance, releasing all associated resources.
    /// </summary>
    public void Dispose()
    {
        _browser?.DisposeAsync().GetAwaiter().GetResult();
        _playwright?.Dispose();
    }
}