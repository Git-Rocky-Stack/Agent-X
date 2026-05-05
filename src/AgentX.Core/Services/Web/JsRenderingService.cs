using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AgentX.Core.Services.Web;

/// <summary>
/// Headless Chromium rendering service powered by Microsoft.Playwright.
/// Renders JavaScript-heavy web pages that cannot be parsed by static HTML extraction,
/// returning the fully-executed DOM as HTML for downstream readability processing.
/// <para>
/// Implements both <see cref="IDisposable"/> and <see cref="IAsyncDisposable"/> to properly
/// release the Playwright browser process and its associated resources. Async disposal is
/// strongly preferred — Playwright's <see cref="IBrowser"/> only exposes <c>DisposeAsync</c>;
/// the sync <see cref="Dispose"/> path blocks on it as a fallback for sync-using callers.
/// </para>
/// </summary>
public sealed class JsRenderingService : IJsRenderingService, IDisposable, IAsyncDisposable
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
    /// Asynchronously disposes the Playwright browser and Playwright instance, releasing all
    /// associated resources. Preferred over <see cref="Dispose"/> — Playwright's
    /// <see cref="IBrowser"/> only exposes async teardown.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync().ConfigureAwait(false);
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
    }

    /// <summary>
    /// Synchronous fallback disposal. Blocks the calling thread on Playwright's async
    /// browser teardown — prefer <see cref="DisposeAsync"/> when the caller can await.
    /// </summary>
    public void Dispose()
    {
        // Wave 4b: Playwright's IBrowser exposes only DisposeAsync (no sync Dispose).
        // VSTHRD002 is suppressed here because (1) the WinUI shutdown path may invoke
        // sync Dispose on transitive disposables, (2) DI registration uses singleton +
        // IAsyncDisposable and prefers DisposeAsync, and (3) Playwright disposal does
        // not capture a sync context, so the GetResult call cannot deadlock under
        // typical schedulers. The async path is the canonical one.
#pragma warning disable VSTHRD002
        _browser?.DisposeAsync().AsTask().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        _browser = null;
        _playwright?.Dispose();
        _playwright = null;
    }
}