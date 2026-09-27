using System.Xml.Linq;
using Serilog;

namespace AgentX.Core.Services.Web;

/// <summary>
/// Parses sitemap.xml files and sitemap index files using System.Xml.Linq.
/// <para>
/// Supports the standard sitemap protocol (http://www.sitemaps.org/schemas/sitemap/0.9),
/// including sitemap index files that reference child sitemaps. When a sitemap index is
/// encountered, child sitemaps are fetched recursively up to a maximum depth of 10, each
/// sitemap at most once, and within a budget of fetched documents and collected URLs, so
/// indexes that list each other or fan out endlessly cannot run away.
/// </para>
/// <para>
/// For regular sitemaps, extracts <c>&lt;loc&gt;</c> from each <c>&lt;url&gt;</c> element.
/// For sitemap indexes, extracts <c>&lt;loc&gt;</c> from each <c>&lt;sitemap&gt;</c> element
/// and recursively fetches the referenced sitemaps.
/// </para>
/// </summary>
public sealed class SitemapParser : ISitemapParser
{
    private readonly ILogger _log;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// A long-lived, shared HttpClient instance configured with appropriate defaults
    /// for fetching sitemap XML: a realistic User-Agent header, 30-second timeout, and
    /// automatic decompression. Redirects are followed by <see cref="WebHttp"/>.
    /// </summary>
    private static readonly HttpClient SharedHttpClient;

    /// <summary>
    /// Most sitemap documents fetched for one <see cref="ParseSitemapAsync"/> call, the root
    /// included. Together with the visited set this bounds indexes that list each other or
    /// fan out into ever more distinct child sitemaps.
    /// </summary>
    internal const int MaxSitemapFetches = 500;

    /// <summary>
    /// Most page URLs collected for one <see cref="ParseSitemapAsync"/> call (the sitemap
    /// protocol's per-file maximum); no further child sitemaps are fetched once it is reached.
    /// </summary>
    internal const int MaxUrls = 50_000;

    /// <summary>
    /// Largest sitemap document accepted: the protocol's own 50 MB limit, counted after
    /// decompression, so a gzip bomb or an endless response cannot exhaust memory.
    /// </summary>
    internal const int MaxSitemapBytes = 50 * 1024 * 1024;

    /// <summary>
    /// Default timeout for HTTP requests when fetching sitemap XML.
    /// </summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum recursion depth for nested sitemap indexes to prevent infinite loops.
    /// </summary>
    internal const int MaxDepth = 10;

    /// <summary>
    /// Maximum number of child sitemaps to process from a single sitemap index.
    /// Prevents resource exhaustion from extremely large sitemap indexes.
    /// </summary>
    internal const int MaxChildSitemapsPerIndex = 100;

    /// <summary>
    /// Standard sitemap XML namespace.
    /// </summary>
    private static readonly XNamespace SitemapNs = "http://www.sitemaps.org/schemas/sitemap/0.9";

    static SitemapParser()
    {
        // Decompresses, leaves redirects to WebHttp, and checks every connection where it is opened.
        SharedHttpClient = new HttpClient(GuardedWebHandler.Create())
        {
            Timeout = DefaultTimeout,
        };

        // Use a realistic browser User-Agent to avoid being blocked by servers
        SharedHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        // Accept XML content types
        SharedHttpClient.DefaultRequestHeaders.Accept.ParseAdd(
            "application/xml, text/xml, application/xhtml+xml, */*;q=0.8");

        SharedHttpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SitemapParser"/>.
    /// </summary>
    /// <param name="logger">The Serilog logger instance for structured logging.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is null.</exception>
    public SitemapParser(ILogger logger)
        : this(logger, SharedHttpClient)
    {
    }

    /// <summary>
    /// Initializes a new instance with a caller-provided client (tests use a stub handler).
    /// </summary>
    internal SitemapParser(ILogger logger, HttpClient httpClient)
    {
        _log = logger?.ForContext<SitemapParser>()
               ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    // ─── ISitemapParser Implementation ──────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ParseSitemapAsync(string sitemapUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sitemapUrl))
            throw new ArgumentException("Sitemap URL must not be empty.", nameof(sitemapUrl));

        _log.Debug("Fetching sitemap from: {SitemapUrl}", sitemapUrl);

        var crawl = new CrawlState(ParseHttpUrl(sitemapUrl, nameof(sitemapUrl)));
        crawl.TryVisit(sitemapUrl);
        crawl.Fetches++;

        var xml = await FetchSitemapXmlAsync(sitemapUrl, crawl.Root, ct);

        if (string.IsNullOrWhiteSpace(xml))
        {
            _log.Warning("Sitemap at '{SitemapUrl}' returned empty content.", sitemapUrl);
            return Array.Empty<string>();
        }

        var doc = XDocument.Parse(xml);
        return await ParseFromDocumentAsync(doc, depth: 0, crawl, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ParseSitemapIndexAsync(string sitemapIndexUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sitemapIndexUrl))
            throw new ArgumentException("Sitemap index URL must not be empty.", nameof(sitemapIndexUrl));

        _log.Debug("Fetching sitemap index from: {SitemapIndexUrl}", sitemapIndexUrl);

        var xml = await FetchSitemapXmlAsync(
            sitemapIndexUrl, ParseHttpUrl(sitemapIndexUrl, nameof(sitemapIndexUrl)), ct);

        if (string.IsNullOrWhiteSpace(xml))
        {
            _log.Warning("Sitemap index at '{SitemapIndexUrl}' returned empty content.", sitemapIndexUrl);
            return Array.Empty<string>();
        }

        var doc = XDocument.Parse(xml);
        return ParseSitemapIndex(doc);
    }

    // ─── Internal Parsing Methods (testable without network) ────────────────

    /// <summary>
    /// Parses a sitemap from an <see cref="XDocument"/>, detecting whether it is a
    /// regular sitemap (<c>&lt;urlset&gt;</c>) or a sitemap index (<c>&lt;sitemapindex&gt;</c>).
    /// <para>
    /// For sitemap indexes, this method recursively fetches and parses each child sitemap.
    /// Network calls are required for sitemap indexes — use <see cref="ParseFromXml"/>
    /// for unit testing the local parsing logic without network calls.
    /// </para>
    /// </summary>
    /// <param name="doc">The parsed XML document.</param>
    /// <param name="depth">Current recursion depth (starts at 0).</param>
    /// <param name="crawl">Sitemaps already read and fetch and URL budgets for this parse.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A flat list of all discovered URLs.</returns>
    internal async Task<IReadOnlyList<string>> ParseFromDocumentAsync(XDocument doc, int depth, CrawlState crawl, CancellationToken ct)
    {
        if (depth > MaxDepth)
        {
            _log.Warning("Sitemap recursion depth exceeded {MaxDepth}. Stopping recursion.", MaxDepth);
            return Array.Empty<string>();
        }

        var root = doc.Root;
        if (root is null)
        {
            _log.Warning("Sitemap XML has no root element.");
            return Array.Empty<string>();
        }

        var rootLocalName = root.Name.LocalName;

        // Sitemap index: <sitemapindex xmlns="...">
        if (rootLocalName == "sitemapindex")
        {
            _log.Debug("Detected sitemap index at depth {Depth}. Recursing into child sitemaps.", depth);
            var childUrls = ParseSitemapIndex(doc);

            var allUrls = new List<string>();
            foreach (var childUrl in childUrls.Take(MaxChildSitemapsPerIndex))
            {
                // An index that lists itself or an ancestor would otherwise be re-read at every level
                if (!crawl.TryVisit(childUrl))
                {
                    _log.Debug("Skipping child sitemap {ChildUrl}: already read in this import.", childUrl);
                    continue;
                }

                if (crawl.Fetches >= MaxSitemapFetches || crawl.UrlCount >= MaxUrls)
                {
                    _log.Warning(
                        "Stopped reading child sitemaps: limit of {MaxFetches} sitemaps or {MaxUrls} URLs reached.",
                        MaxSitemapFetches, MaxUrls);
                    break;
                }

                try
                {
                    // A public sitemap may not point Agent-X at this machine or the local network
                    if (Uri.TryCreate(childUrl, UriKind.Absolute, out var childUri)
                        && await PrivateNetworkGuard.IsPrivateOrLocalHostAsync(childUri, ct)
                        && !await crawl.RootIsPrivateAsync(ct))
                    {
                        _log.Warning(
                            "Skipping child sitemap {ChildUrl}: it points to a private or local network address.",
                            childUrl);
                        continue;
                    }

                    _log.Debug("Fetching child sitemap: {ChildUrl} (depth {Depth})", childUrl, depth + 1);
                    crawl.Fetches++;
                    var childXml = await FetchSitemapXmlAsync(childUrl, crawl.Root, ct);
                    if (string.IsNullOrWhiteSpace(childXml))
                        continue;

                    var childDoc = XDocument.Parse(childXml);
                    var childResults = await ParseFromDocumentAsync(childDoc, depth + 1, crawl, ct);
                    allUrls.AddRange(childResults);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    // Skip a failed child sitemap and continue; a cancelled import is not a partial success
                    _log.Warning(ex, "Failed to fetch or parse child sitemap: {ChildUrl}. Skipping.", childUrl);
                }
            }

            _log.Information("Sitemap index at depth {Depth} yielded {UrlCount} total URLs from {ChildCount} child sitemaps.",
                depth, allUrls.Count, Math.Min(childUrls.Count, MaxChildSitemapsPerIndex));
            return allUrls;
        }

        // Regular sitemap: <urlset xmlns="...">
        if (rootLocalName == "urlset")
        {
            var urls = ParseUrlset(doc);
            var room = Math.Max(0, MaxUrls - crawl.UrlCount);
            if (urls.Count > room)
            {
                _log.Warning("Sitemap URL limit of {MaxUrls} reached; ignoring the remaining entries.", MaxUrls);
                urls = urls.Take(room).ToList();
            }

            crawl.UrlCount += urls.Count;
            _log.Debug("Parsed regular sitemap with {UrlCount} URLs at depth {Depth}.", urls.Count, depth);
            return urls;
        }

        _log.Warning("Unrecognized sitemap root element: '{RootLocalName}'. Expected 'urlset' or 'sitemapindex'.", rootLocalName);
        return Array.Empty<string>();
    }

    /// <summary>
    /// Parses a regular sitemap (<c>&lt;urlset&gt;</c>) from an <see cref="XDocument"/>,
    /// extracting all <c>&lt;loc&gt;</c> values from <c>&lt;url&gt;</c> elements.
    /// This method is testable without network calls.
    /// </summary>
    /// <param name="doc">The parsed XML document representing a sitemap.</param>
    /// <returns>A list of URLs found in the sitemap.</returns>
    internal IReadOnlyList<string> ParseFromXml(XDocument doc)
    {
        if (doc.Root is null)
            return Array.Empty<string>();

        var rootLocalName = doc.Root.Name.LocalName;

        // Regular sitemap
        if (rootLocalName == "urlset")
            return ParseUrlset(doc);

        // Sitemap index — return the child sitemap URLs themselves
        if (rootLocalName == "sitemapindex")
            return ParseSitemapIndex(doc);

        return Array.Empty<string>();
    }

    /// <summary>
    /// Extracts all <c>&lt;loc&gt;</c> values from <c>&lt;url&gt;</c> elements in a regular sitemap.
    /// Supports both namespaced and non-namespaced elements.
    /// </summary>
    internal IReadOnlyList<string> ParseUrlset(XDocument doc)
    {
        var root = doc.Root;
        if (root is null)
            return Array.Empty<string>();

        // Try namespaced elements first, then fall back to non-namespaced
        var urls = root.Elements(SitemapNs + "url")
            .Union(root.Elements("url"))
            .Select(u => (u.Element(SitemapNs + "loc") ?? u.Element("loc"))?.Value?.Trim())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Cast<string>()
            .ToList();

        return urls;
    }

    /// <summary>
    /// Extracts all <c>&lt;loc&gt;</c> values from <c>&lt;sitemap&gt;</c> elements in a sitemap index.
    /// Supports both namespaced and non-namespaced elements.
    /// </summary>
    internal IReadOnlyList<string> ParseSitemapIndex(XDocument doc)
    {
        var root = doc.Root;
        if (root is null)
            return Array.Empty<string>();

        // Try namespaced elements first, then fall back to non-namespaced
        var sitemapUrls = root.Elements(SitemapNs + "sitemap")
            .Union(root.Elements("sitemap"))
            .Select(s => (s.Element(SitemapNs + "loc") ?? s.Element("loc"))?.Value?.Trim())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Cast<string>()
            .ToList();

        return sitemapUrls;
    }

    // ─── HTTP Fetching ──────────────────────────────────────────────────────

    /// <summary>
    /// Parses an absolute HTTP or HTTPS URL, or throws <see cref="ArgumentException"/>.
    /// </summary>
    private static Uri ParseHttpUrl(string url, string paramName)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"Invalid sitemap URL: '{url}'. Only HTTP and HTTPS URLs are supported.", paramName);
        }

        return uri;
    }

    /// <summary>
    /// Fetches the raw XML content from the specified sitemap URL, reading at most
    /// <see cref="MaxSitemapBytes"/>. Redirects are held to the network zone of
    /// <paramref name="origin"/>, the sitemap the user asked for. Handles BOM removal and
    /// HTML-wrapped XML extraction for compatibility.
    /// </summary>
    private async Task<string> FetchSitemapXmlAsync(string url, Uri origin, CancellationToken ct)
    {
        var uri = ParseHttpUrl(url, nameof(url));

        try
        {
            var (response, _) = await WebHttp.GetFollowingRedirectsAsync(
                _httpClient,
                uri,
                configureRequest: null,
                (from, to, token) => PrivateNetworkGuard.EnsureRedirectAllowedAsync(origin, from, to, token),
                ct);

            string content;
            using (response)
            {
                response.EnsureSuccessStatusCode();

                var bytes = await WebHttp.ReadBoundedAsync(response.Content, MaxSitemapBytes, ct);
                content = WebHttp.DecodeText(bytes, response.Content.Headers.ContentType?.CharSet);
            }

            // Strip BOM and leading whitespace that might break XML parsing
            content = content.TrimStart('\uFEFF', '\u200B', ' ', '\r', '\n');

            // If the response looks like HTML wrapping XML, try to extract the XML portion
            if (!content.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
                && !content.StartsWith("<urlset", StringComparison.OrdinalIgnoreCase)
                && !content.StartsWith("<sitemapindex", StringComparison.OrdinalIgnoreCase))
            {
                // Try to find XML declaration or root element
                var xmlStart = content.IndexOf("<?xml", StringComparison.OrdinalIgnoreCase);
                if (xmlStart >= 0)
                    return content[xmlStart..];

                var urlsetStart = content.IndexOf("<urlset", StringComparison.OrdinalIgnoreCase);
                if (urlsetStart >= 0)
                    return content[urlsetStart..];

                var sitemapIndexStart = content.IndexOf("<sitemapindex", StringComparison.OrdinalIgnoreCase);
                if (sitemapIndexStart >= 0)
                    return content[sitemapIndexStart..];
            }

            return content;
        }
        catch (HttpRequestException ex)
        {
            _log.Error(ex, "HTTP error fetching sitemap from: {Url}", url);
            throw;
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            _log.Information("Sitemap fetch cancelled for: {Url}", url);
            throw;
        }
    }

    /// <summary>
    /// Bookkeeping for one <see cref="ParseSitemapAsync"/> call: which sitemap documents were
    /// already read, how many were fetched, and how many page URLs were collected.
    /// </summary>
    internal sealed class CrawlState(Uri root)
    {
        private readonly HashSet<string> _visited = new(StringComparer.Ordinal);
        private Task<bool>? _rootIsPrivate;

        /// <summary>The sitemap the user asked for; it sets the network zone of the whole crawl.</summary>
        public Uri Root { get; } = root;

        /// <summary>Sitemap documents fetched so far, the root included.</summary>
        public int Fetches { get; set; }

        /// <summary>
        /// Whether the root sitemap is on this machine or a private network (looked up once).
        /// Only then may the sitemaps it lists point at private or local addresses.
        /// </summary>
        public Task<bool> RootIsPrivateAsync(CancellationToken ct) =>
            _rootIsPrivate ??= PrivateNetworkGuard.IsPrivateOrLocalHostAsync(Root, ct);

        /// <summary>Page URLs collected so far.</summary>
        public int UrlCount { get; set; }

        /// <summary>
        /// Records a sitemap URL as read. Returns false when it was read before; the URL is
        /// compared without its fragment and with the scheme and host normalized.
        /// </summary>
        public bool TryVisit(string url)
        {
            var key = Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
                ? uri.GetLeftPart(UriPartial.Query)
                : url.Trim();
            return _visited.Add(key);
        }
    }
}
