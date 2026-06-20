using System.Net;
using System.Xml.Linq;
using Serilog;

namespace AgentX.Core.Services.Web;

/// <summary>
/// Parses sitemap.xml files and sitemap index files using System.Xml.Linq.
/// <para>
/// Supports the standard sitemap protocol (http://www.sitemaps.org/schemas/sitemap/0.9),
/// including sitemap index files that reference child sitemaps. When a sitemap index is
/// encountered, child sitemaps are fetched recursively up to a maximum depth of 10 to
/// prevent infinite loops.
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

    /// <summary>
    /// A long-lived, shared HttpClient instance configured with appropriate defaults
    /// for fetching sitemap XML: a realistic User-Agent header, 30-second timeout, and
    /// automatic decompression.
    /// </summary>
    private static readonly HttpClient SharedHttpClient;

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
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        };

        SharedHttpClient = new HttpClient(handler)
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
    {
        _log = logger?.ForContext<SitemapParser>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    // ─── ISitemapParser Implementation ──────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ParseSitemapAsync(string sitemapUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sitemapUrl))
            throw new ArgumentException("Sitemap URL must not be empty.", nameof(sitemapUrl));

        _log.Debug("Fetching sitemap from: {SitemapUrl}", sitemapUrl);

        var xml = await FetchSitemapXmlAsync(sitemapUrl, ct);

        if (string.IsNullOrWhiteSpace(xml))
        {
            _log.Warning("Sitemap at '{SitemapUrl}' returned empty content.", sitemapUrl);
            return Array.Empty<string>();
        }

        var doc = XDocument.Parse(xml);
        return await ParseFromDocumentAsync(doc, depth: 0, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ParseSitemapIndexAsync(string sitemapIndexUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sitemapIndexUrl))
            throw new ArgumentException("Sitemap index URL must not be empty.", nameof(sitemapIndexUrl));

        _log.Debug("Fetching sitemap index from: {SitemapIndexUrl}", sitemapIndexUrl);

        var xml = await FetchSitemapXmlAsync(sitemapIndexUrl, ct);

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
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A flat list of all discovered URLs.</returns>
    internal async Task<IReadOnlyList<string>> ParseFromDocumentAsync(XDocument doc, int depth, CancellationToken ct)
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
                try
                {
                    _log.Debug("Fetching child sitemap: {ChildUrl} (depth {Depth})", childUrl, depth + 1);
                    var childXml = await FetchSitemapXmlAsync(childUrl, ct);
                    if (string.IsNullOrWhiteSpace(childXml))
                        continue;

                    var childDoc = XDocument.Parse(childXml);
                    var childResults = await ParseFromDocumentAsync(childDoc, depth + 1, ct);
                    allUrls.AddRange(childResults);
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Failed to fetch or parse child sitemap: {ChildUrl}. Skipping.", childUrl);
                    // Skip failed child sitemaps and continue with remaining ones
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
    /// Fetches the raw XML content from the specified sitemap URL using the shared HttpClient.
    /// Handles BOM removal and HTML-wrapped XML extraction for compatibility.
    /// </summary>
    private async Task<string> FetchSitemapXmlAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(ct);

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
}
