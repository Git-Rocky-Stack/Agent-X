using System.Diagnostics;
using System.Net;
using Serilog;

namespace AgentX.Core.Services.Web;

/// <summary>
/// Fetches raw HTML content from web URLs using a shared, long-lived <see cref="HttpClient"/>.
/// Handles decompression, redirect following, content size limits, timeout management,
/// and optional JS rendering fallback for JavaScript-heavy pages.
/// <para>
/// This class extracts the HTTP fetching logic previously embedded in
/// <see cref="WebScraperService"/>, enabling reuse across services and
/// independent testability of the fetch layer.
/// </para>
/// </summary>
public class WebContentFetcher : IWebContentFetcher, IDisposable
{
    private readonly ILogger _log;
    private readonly IJsRenderingService? _jsRenderingService;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Default timeout for HTTP requests.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Maximum allowed content length (10 MB) to prevent out-of-memory on extremely large pages.
    /// </summary>
    public const int MaxContentLengthBytes = 10 * 1024 * 1024;

    /// <summary>
    /// A realistic browser User-Agent string to avoid being blocked by sites that
    /// reject requests from non-browser clients.
    /// </summary>
    private const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    /// <summary>
    /// Initializes a new instance of <see cref="WebContentFetcher"/> with an internally
    /// managed <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="logger">The Serilog logger instance for structured logging.</param>
    /// <param name="jsRenderingService">
    /// Optional JavaScript rendering service. When provided and the fetched HTML is empty
    /// or minimal, the fetcher will fall back to headless Chromium rendering.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is null.</exception>
    public WebContentFetcher(ILogger logger, IJsRenderingService? jsRenderingService = null)
        : this(logger, jsRenderingService, CreateDefaultHttpClient())
    {
        // When using the default HttpClient, this instance owns it and should dispose it.
        _ownsHttpClient = true;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="WebContentFetcher"/> with a provided
    /// <see cref="HttpClient"/>. The caller is responsible for disposing the client.
    /// <para>
    /// This constructor is intended for dependency injection scenarios where HttpClient
    /// lifetime is managed externally (e.g., IHttpClientFactory) and for unit testing
    /// with mock HTTP handlers.
    /// </para>
    /// </summary>
    /// <param name="logger">The Serilog logger instance for structured logging.</param>
    /// <param name="jsRenderingService">
    /// Optional JavaScript rendering service for JS rendering fallback.
    /// </param>
    /// <param name="httpClient">
    /// The HttpClient to use for requests. Caller is responsible for disposal.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="logger"/> or <paramref name="httpClient"/> is null.
    /// </exception>
    public WebContentFetcher(ILogger logger, IJsRenderingService? jsRenderingService, HttpClient httpClient)
    {
        _log = logger?.ForContext<WebContentFetcher>()
               ?? throw new ArgumentNullException(nameof(logger));
        _jsRenderingService = jsRenderingService;
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = false;
    }

    // ─── IWebContentFetcher Implementation ───────────────────────────────────

    /// <inheritdoc />
    public async Task<FetchResult> FetchAsync(string url, CancellationToken ct = default)
    {
        ValidateUrl(url);

        _log.Debug("Fetching HTML content from: {Url}", url);

        var stopwatch = Stopwatch.StartNew();
        var usedJsRendering = false;
        string html;
        string finalUrl;

        try
        {
            (html, finalUrl) = await FetchHtmlInternalAsync(url, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // TaskCanceledException with a non-cancelled token indicates an HTTP timeout
            _log.Warning(ex, "Request timed out for {Url} after {Timeout}s", url, DefaultTimeout.TotalSeconds);
            throw new TimeoutException(
                $"The request to '{url}' timed out after {DefaultTimeout.TotalSeconds} seconds.", ex);
        }

        // Attempt JS rendering fallback when the HTML response is empty or only a script shell
        // with minimal visible text, and a JS rendering service is available.
        if (_jsRenderingService is not null && NeedsJsRendering(html))
        {
            _log.Information(
                "HTTP fetch returned empty or minimal content for {Url}, falling back to JS rendering", url);

            try
            {
                var renderedHtml = await _jsRenderingService.RenderPageAsync(
                    url, waitForNetworkIdle: true, ct).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(renderedHtml))
                {
                    html = renderedHtml;
                    usedJsRendering = true;
                    _log.Information(
                        "JS rendering fallback succeeded for {Url} ({Length} chars)",
                        url, html.Length);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "JS rendering fallback failed for {Url}", url);
                // Continue with whatever the HTTP fetch produced
            }
        }

        stopwatch.Stop();

        _log.Debug(
            "Fetch completed for {Url}: {Length} chars, {Elapsed}ms, JS rendering: {UsedJsRendering}",
            url, html.Length, stopwatch.ElapsedMilliseconds, usedJsRendering);

        return new FetchResult(html, finalUrl, stopwatch.Elapsed, usedJsRendering);
    }

    // ─── Internal Fetch Logic ────────────────────────────────────────────────

    /// <summary>
    /// Performs the HTTP GET (following redirects one hop at a time) with content size
    /// validation and charset detection. Returns the decoded HTML and the final URL: the
    /// caller's own URL string when no redirect happened, else the redirect target.
    /// </summary>
    private async Task<(string Html, string FinalUrl)> FetchHtmlInternalAsync(string url, CancellationToken ct)
    {
        var requestUri = new Uri(url);

        var (response, finalUri) = await WebHttp.GetFollowingRedirectsAsync(
            _httpClient,
            requestUri,
            // Some sites return different content based on Accept header
            request => request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml"),
            // A public page may not redirect the fetch to this machine or the local network
            (from, to, token) => PrivateNetworkGuard.EnsureRedirectAllowedAsync(requestUri, from, to, token),
            ct).ConfigureAwait(false);

        using (response)
        {
            response.EnsureSuccessStatusCode();

            // Reject early when the server advertises an oversize body via Content-Length.
            var declaredLength = response.Content.Headers.ContentLength;

            if (declaredLength.HasValue && declaredLength.Value > MaxContentLengthBytes)
            {
                throw new InvalidOperationException(
                    $"Page content too large ({declaredLength.Value / 1024d / 1024d:F1} MB). " +
                    $"Maximum is {MaxContentLengthBytes / 1024 / 1024} MB.");
            }

            // Enforce the cap while streaming so a missing or dishonest Content-Length
            // cannot force unbounded buffering.
            var bytes = await WebHttp.ReadBoundedAsync(response.Content, MaxContentLengthBytes, ct)
                .ConfigureAwait(false);

            var html = WebHttp.DecodeText(bytes, response.Content.Headers.ContentType?.CharSet);
            var finalUrl = finalUri == requestUri ? url : finalUri.AbsoluteUri;
            return (html, finalUrl);
        }
    }

    /// <summary>
    /// Below this many characters of visible body text, a page that loads scripts is treated
    /// as a JavaScript shell whose content only appears once the scripts run.
    /// </summary>
    internal const int MinimalContentChars = 200;

    /// <summary>
    /// True when the HTML is empty, or is a script-driven page whose body shows fewer than
    /// <see cref="MinimalContentChars"/> characters of text without JavaScript. A page without
    /// scripts is never rendered: a headless browser would not add anything to it.
    /// </summary>
    internal static bool NeedsJsRendering(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return true;

        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(html);

        if (doc.DocumentNode.SelectSingleNode("//script") is null)
            return false;

        var body = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
        var hidden = body.SelectNodes(".//script|.//style|.//noscript|.//template");
        if (hidden is not null)
        {
            foreach (var node in hidden.ToList())
            {
                node.Remove();
            }
        }

        var visibleChars = WebUtility.HtmlDecode(body.InnerText).Count(c => !char.IsWhiteSpace(c));
        return visibleChars < MinimalContentChars;
    }

    // ─── URL Validation ──────────────────────────────────────────────────────

    /// <summary>
    /// Validates that a URL is a non-empty, well-formed absolute HTTP or HTTPS URL.
    /// Throws <see cref="ArgumentException"/> if validation fails.
    /// </summary>
    private static void ValidateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL must not be empty.", nameof(url));
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"Invalid URL format: '{url}'.", nameof(url));
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                $"Invalid URL scheme: '{uri.Scheme}'. Only HTTP and HTTPS URLs are supported.",
                nameof(url));
        }
    }

    // ─── HttpClient Factory ──────────────────────────────────────────────────

    /// <summary>
    /// Creates the default <see cref="HttpClient"/> with appropriate configuration
    /// for web scraping: decompression, realistic User-Agent and reasonable timeout.
    /// Redirects are followed by <see cref="WebHttp"/> one hop at a time, not by the handler.
    /// </summary>
    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip
                                     | DecompressionMethods.Deflate
                                     | DecompressionMethods.Brotli,
            AllowAutoRedirect = false,
        };

        var client = new HttpClient(handler)
        {
            Timeout = DefaultTimeout,
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd(
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

        return client;
    }

    // ─── IDisposable ─────────────────────────────────────────────────────────

    /// <summary>
    /// Disposes the internally managed <see cref="HttpClient"/> if this instance owns it.
    /// Does not dispose HttpClient instances provided via the constructor overload.
    /// </summary>
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
