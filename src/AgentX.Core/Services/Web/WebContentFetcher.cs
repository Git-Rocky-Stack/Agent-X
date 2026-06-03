using System.Diagnostics;
using System.Net;
using System.Text;
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
        var html = string.Empty;
        string? finalUrl = url;

        try
        {
            html = await FetchHtmlInternalAsync(url, ct).ConfigureAwait(false);
            finalUrl = url;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // TaskCanceledException with a non-cancelled token indicates an HTTP timeout
            _log.Warning(ex, "Request timed out for {Url} after {Timeout}s", url, DefaultTimeout.TotalSeconds);
            throw new TimeoutException(
                $"The request to '{url}' timed out after {DefaultTimeout.TotalSeconds} seconds.", ex);
        }

        // Attempt JS rendering fallback when the HTML response is empty or minimal
        // and a JS rendering service is available.
        if (string.IsNullOrWhiteSpace(html) && _jsRenderingService is not null)
        {
            _log.Information(
                "HTTP fetch returned empty content for {Url}, falling back to JS rendering", url);

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
            catch (Exception ex)
            {
                _log.Warning(ex, "JS rendering fallback failed for {Url}", url);
                // Continue with whatever HTTP fetch produced (empty in this case)
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
    /// Performs the actual HTTP GET request with content size validation.
    /// </summary>
    private async Task<string> FetchHtmlInternalAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        // Some sites return different content based on Accept header
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        // Capture the final URL after redirects
        var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;

        // Reject early when the server advertises an oversize body via Content-Length.
        var declaredLength = response.Content.Headers.ContentLength;

        if (declaredLength.HasValue && declaredLength.Value > MaxContentLengthBytes)
        {
            throw new InvalidOperationException(
                $"Page content too large ({declaredLength.Value / 1024d / 1024d:F1} MB). " +
                $"Maximum is {MaxContentLengthBytes / 1024 / 1024} MB.");
        }

        // Enforce the cap while streaming so a missing or dishonest Content-Length
        // cannot force unbounded buffering. Previously the limit was only checked
        // when the header was present, leaving a memory-exhaustion bypass.
        var bytes = await ReadBoundedAsync(response.Content, MaxContentLengthBytes, ct)
            .ConfigureAwait(false);

        return ResolveEncoding(response.Content.Headers.ContentType?.CharSet).GetString(bytes);
    }

    /// <summary>
    /// Reads an HTTP content stream fully into memory while enforcing a hard byte
    /// cap. Aborts as soon as the cap is exceeded, regardless of whether the server
    /// supplied an accurate <c>Content-Length</c> header.
    /// </summary>
    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        await using var source = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        // Pre-size the buffer only when the declared length is present and within
        // the cap; never trust a declared length beyond the cap.
        var declared = content.Headers.ContentLength;
        var initialCapacity = declared.HasValue && declared.Value > 0 && declared.Value <= maxBytes
            ? (int)declared.Value
            : 0;

        using var buffer = new MemoryStream(initialCapacity);
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk.AsMemory(0, chunk.Length), ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new InvalidOperationException(
                    "Page content exceeded the maximum allowed size of " +
                    $"{maxBytes / 1024 / 1024} MB while streaming the response.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Resolves a .NET <see cref="Encoding"/> from an HTTP charset token, defaulting
    /// to UTF-8 when the charset is missing or unrecognized. Mirrors the behavior of
    /// <c>HttpContent.ReadAsStringAsync</c> for the common cases.
    /// </summary>
    private static Encoding ResolveEncoding(string? charSet)
    {
        if (string.IsNullOrWhiteSpace(charSet))
            return Encoding.UTF8;

        try
        {
            return Encoding.GetEncoding(charSet.Trim().Trim('"'));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
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
    /// for web scraping: decompression, realistic User-Agent, redirect following,
    /// and reasonable timeout.
    /// </summary>
    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip
                                     | DecompressionMethods.Deflate
                                     | DecompressionMethods.Brotli,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
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
