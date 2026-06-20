using System.Net;
using System.Xml.Linq;
using AgentX.Core.Services.Web.Models;
using Serilog;

namespace AgentX.Core.Services.Web;

/// <summary>
/// Parses RSS 2.0, RSS 1.0 (RDF), and Atom 1.0 feeds using System.Xml.Linq.
/// <para>
/// RSS items extract: title from &lt;title&gt;, content from &lt;content:encoded&gt; (falling back
/// to &lt;description&gt;), URL from &lt;link&gt;, author from &lt;dc:creator&gt; or &lt;author&gt;,
/// date from &lt;pubDate&gt;, and category from &lt;category&gt;.
/// </para>
/// <para>
/// Atom entries extract: title from &lt;title&gt;, content from &lt;content&gt; (falling back
/// to &lt;summary&gt;), URL from &lt;link href=""&gt;, author from &lt;author&gt;&lt;name&gt;,
/// date from &lt;published&gt; (falling back to &lt;updated&gt;), and category from &lt;category term=""&gt;.
/// </para>
/// </summary>
public class FeedService : IFeedService
{
    private readonly ILogger _log;

    /// <summary>
    /// A long-lived, shared HttpClient instance configured with appropriate defaults
    /// for fetching feed XML: a realistic User-Agent header, 30-second timeout, and
    /// automatic decompression.
    /// </summary>
    private static readonly HttpClient SharedHttpClient;

    /// <summary>
    /// Default timeout for HTTP requests when fetching feed XML.
    /// </summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    // ─── XML Namespace Constants ─────────────────────────────────────────────

    private static readonly XNamespace ContentNamespace = "http://purl.org/rss/1.0/modules/content/";
    private static readonly XNamespace DublinCoreNamespace = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace AtomNamespace = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace Rss10Namespace = "http://purl.org/rss/1.0/";

    static FeedService()
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

        // Use a realistic browser User-Agent to avoid being blocked by feed servers
        SharedHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        // Accept XML content types
        SharedHttpClient.DefaultRequestHeaders.Accept.ParseAdd(
            "application/rss+xml, application/atom+xml, application/xml, text/xml, application/xhtml+xml, */*;q=0.8");

        SharedHttpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }

    /// <summary>
    /// Initializes a new instance of <see cref="FeedService"/>.
    /// </summary>
    /// <param name="logger">The Serilog logger instance for structured logging.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is null.</exception>
    public FeedService(ILogger logger)
    {
        _log = logger?.ForContext<FeedService>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    // ─── IFeedService Implementation ────────────────────────────────────────

    /// <inheritdoc />
    public async Task<FeedInfo> ParseFeedAsync(string feedUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(feedUrl))
            throw new ArgumentException("Feed URL must not be empty.", nameof(feedUrl));

        _log.Debug("Fetching feed from: {FeedUrl}", feedUrl);

        var xml = await FetchFeedXmlAsync(feedUrl, ct);

        if (string.IsNullOrWhiteSpace(xml))
        {
            throw new InvalidOperationException($"Feed at '{feedUrl}' returned empty content.");
        }

        var doc = XDocument.Parse(xml);
        return ParseFeed(doc, feedUrl);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FeedItem>> GetNewItemsAsync(string feedUrl, DateTime since, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(feedUrl))
            throw new ArgumentException("Feed URL must not be empty.", nameof(feedUrl));

        _log.Debug("Fetching new items from {FeedUrl} since {Since}", feedUrl, since);

        var feedInfo = await ParseFeedAsync(feedUrl, ct);

        var newItems = feedInfo.Items
            .Where(item => item.PublishedDate.HasValue && item.PublishedDate.Value > since)
            .ToList()
            .AsReadOnly();

        _log.Information(
            "Found {NewCount} new items (out of {TotalCount}) from {FeedUrl} since {Since}",
            newItems.Count, feedInfo.Items.Count, feedUrl, since);

        return newItems;
    }

    // ─── Internal Parsing Methods (testable without network) ───────────────

    /// <summary>
    /// Parses a feed from an <see cref="XDocument"/>, detecting the format from the root element.
    /// Supports RSS 2.0 (&lt;rss&gt;), RSS 1.0/RDF (&lt;RDF&gt;), and Atom 1.0 (&lt;feed&gt;).
    /// </summary>
    /// <param name="doc">The parsed XML document.</param>
    /// <param name="sourceUrl">The URL the feed was fetched from (used as fallback for feed URL).</param>
    /// <returns>A parsed <see cref="FeedInfo"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the feed format is not recognized.</exception>
    internal FeedInfo ParseFeed(XDocument doc, string sourceUrl)
    {
        var root = doc.Root;
        if (root is null)
            throw new InvalidOperationException("Feed XML has no root element.");

        var rootLocalName = root.Name.LocalName;

        // RSS 2.0: <rss version="2.0"><channel>...</channel></rss>
        if (rootLocalName == "rss")
        {
            _log.Debug("Detected RSS 2.0 feed format");
            var channel = root.Element("channel");
            if (channel is null)
                throw new InvalidOperationException("RSS feed is missing the <channel> element.");

            return ParseRssChannel(channel, sourceUrl);
        }

        // RSS 1.0 (RDF): <RDF xmlns="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
        if (rootLocalName == "RDF" || root.Name.Equals(RdfNamespace + "RDF"))
        {
            _log.Debug("Detected RSS 1.0 (RDF) feed format");
            return ParseRdfFeed(root, sourceUrl);
        }

        // Atom 1.0: <feed xmlns="http://www.w3.org/2005/Atom">
        if (rootLocalName == "feed" || root.Name.Equals(AtomNamespace + "feed"))
        {
            _log.Debug("Detected Atom 1.0 feed format");
            return ParseAtomFeed(root, sourceUrl);
        }

        throw new InvalidOperationException(
            $"Unrecognized feed format. Root element: '{root.Name}'. " +
            "Expected <rss>, <RDF>, or <feed> (Atom).");
    }

    /// <summary>
    /// Parses an RSS 2.0 &lt;channel&gt; element into a <see cref="FeedInfo"/>.
    /// </summary>
    internal FeedInfo ParseRssChannel(XElement channel, string sourceUrl)
    {
        var feedInfo = new FeedInfo
        {
            Title = GetText(channel, "title") ?? string.Empty,
            Url = GetText(channel, "link") ?? sourceUrl,
            Description = GetText(channel, "description"),
        };

        // Parse lastBuildDate or pubDate for LastUpdated
        var lastUpdatedStr = GetText(channel, "lastBuildDate") ?? GetText(channel, "pubDate");
        if (lastUpdatedStr is not null)
        {
            feedInfo.LastUpdated = ParseRfc822Date(lastUpdatedStr);
        }

        // Parse all <item> elements
        foreach (var item in channel.Elements("item"))
        {
            var feedItem = ParseRssItem(item);
            feedInfo.Items.Add(feedItem);
        }

        _log.Debug("Parsed RSS feed '{Title}' with {ItemCount} items", feedInfo.Title, feedInfo.Items.Count);
        return feedInfo;
    }

    /// <summary>
    /// Parses a single RSS 2.0 &lt;item&gt; element.
    /// </summary>
    internal FeedItem ParseRssItem(XElement item)
    {
        // Content: prefer <content:encoded>, fall back to <description>
        var content = GetNamespaceElementValue(item, "encoded", ContentNamespace)
                      ?? GetText(item, "description")
                      ?? string.Empty;

        // Description: use <description> if it differs from content
        var description = GetText(item, "description");
        if (description == content)
            description = null;

        // Author: prefer <dc:creator>, fall back to <author>
        var author = GetNamespaceElementValue(item, "creator", DublinCoreNamespace)
                     ?? GetText(item, "author");

        // Published date: <pubDate>
        var pubDateStr = GetText(item, "pubDate");
        DateTime? publishedDate = pubDateStr is not null ? ParseRfc822Date(pubDateStr) : null;

        // Category: first <category> element
        var category = GetText(item, "category");

        return new FeedItem
        {
            Title = GetText(item, "title") ?? string.Empty,
            Content = content,
            Url = GetText(item, "link") ?? string.Empty,
            Author = author,
            PublishedDate = publishedDate,
            Description = description,
            Category = category,
        };
    }

    /// <summary>
    /// Parses an RSS 1.0 (RDF) feed into a <see cref="FeedInfo"/>.
    /// </summary>
    internal FeedInfo ParseRdfFeed(XElement rdfRoot, string sourceUrl)
    {
        // RSS 1.0 channel is under <channel> with RSS 1.0 namespace, or plain
        var channel = rdfRoot.Element(Rss10Namespace + "channel")
                     ?? rdfRoot.Element("channel");

        var feedInfo = new FeedInfo
        {
            Title = channel is not null ? (GetText(channel, "title") ?? GetNamespaceElementValue(channel, "title", Rss10Namespace) ?? string.Empty) : string.Empty,
            Url = channel is not null ? (GetText(channel, "link") ?? GetNamespaceElementValue(channel, "link", Rss10Namespace) ?? sourceUrl) : sourceUrl,
            Description = channel is not null ? (GetText(channel, "description") ?? GetNamespaceElementValue(channel, "description", Rss10Namespace)) : null,
        };

        // Parse dc:date for LastUpdated
        var dateStr = channel is not null ? (GetNamespaceElementValue(channel, "date", DublinCoreNamespace) ?? GetText(channel, "date")) : null;
        if (dateStr is not null)
        {
            feedInfo.LastUpdated = ParseIso8601Date(dateStr);
        }

        // Parse all <item> elements (may be namespaced)
        var items = rdfRoot.Elements(Rss10Namespace + "item")
                    .Union(rdfRoot.Elements("item"));

        foreach (var item in items)
        {
            var feedItem = ParseRssItem(item);
            feedInfo.Items.Add(feedItem);
        }

        _log.Debug("Parsed RDF feed '{Title}' with {ItemCount} items", feedInfo.Title, feedInfo.Items.Count);
        return feedInfo;
    }

    /// <summary>
    /// Parses an Atom 1.0 &lt;feed&gt; element into a <see cref="FeedInfo"/>.
    /// </summary>
    internal FeedInfo ParseAtomFeed(XElement feed, string sourceUrl)
    {
        // Handle both namespaced and non-namespaced elements
        var title = GetAtomText(feed, "title") ?? string.Empty;

        // Atom links: prefer rel="alternate" or no rel, then rel="self"
        var link = GetAtomLink(feed);
        var url = link ?? sourceUrl;

        var feedInfo = new FeedInfo
        {
            Title = title,
            Url = url,
            Description = GetAtomText(feed, "subtitle"),
        };

        // Parse <updated> for LastUpdated
        var updatedStr = GetAtomText(feed, "updated");
        if (updatedStr is not null)
        {
            feedInfo.LastUpdated = ParseIso8601Date(updatedStr);
        }

        // Parse all <entry> elements
        var entries = feed.Elements(AtomNamespace + "entry")
                      .Union(feed.Elements("entry"));

        foreach (var entry in entries)
        {
            var feedItem = ParseAtomEntry(entry);
            feedInfo.Items.Add(feedItem);
        }

        _log.Debug("Parsed Atom feed '{Title}' with {ItemCount} items", feedInfo.Title, feedInfo.Items.Count);
        return feedInfo;
    }

    /// <summary>
    /// Parses a single Atom &lt;entry&gt; element.
    /// </summary>
    internal FeedItem ParseAtomEntry(XElement entry)
    {
        // Content: prefer <content type="html"> or <content>, fall back to <summary>
        var content = GetAtomText(entry, "content")
                      ?? GetAtomText(entry, "summary")
                      ?? string.Empty;

        // Description: use <summary> if it differs from content
        var summary = GetAtomText(entry, "summary");
        var description = (summary is not null && summary != content) ? summary : null;

        // Author: <author><name>
        var authorElement = entry.Element(AtomNamespace + "author")
                            ?? entry.Element("author");
        var author = authorElement?
            .Elements(AtomNamespace + "name")
            .Union(authorElement.Elements("name"))
            .Select(e => e.Value.Trim())
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

        // Published date: prefer <published>, fall back to <updated>
        var publishedStr = GetAtomText(entry, "published")
                           ?? GetAtomText(entry, "updated");
        DateTime? publishedDate = publishedStr is not null ? ParseIso8601Date(publishedStr) : null;

        // Category: first <category term="">
        var category = entry.Elements(AtomNamespace + "category")
                       .Union(entry.Elements("category"))
                       .Select(c => c.Attribute("term")?.Value ?? c.Attribute("label")?.Value)
                       .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

        // Link: prefer rel="alternate" or no rel, then rel="self"
        var url = GetAtomLink(entry) ?? string.Empty;

        return new FeedItem
        {
            Title = GetAtomText(entry, "title") ?? string.Empty,
            Content = content,
            Url = url,
            Author = author,
            PublishedDate = publishedDate,
            Description = description,
            Category = category,
        };
    }

    // ─── HTTP Fetching ──────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the raw XML content from the specified feed URL using the shared HttpClient.
    /// </summary>
    private async Task<string> FetchFeedXmlAsync(string feedUrl, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, feedUrl);
        using var response = await SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // Some feeds return text/html; accept it as well for compatibility
        var content = await response.Content.ReadAsStringAsync(ct);

        // Strip any BOM or leading whitespace that might break XML parsing
        content = content.TrimStart('\uFEFF', '\u200B', ' ', '\r', '\n');

        // Some feeds are wrapped in HTML — try to extract the XML portion
        if (content.StartsWith("<!", StringComparison.OrdinalIgnoreCase) || content.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) || content.StartsWith("<rss", StringComparison.OrdinalIgnoreCase) || content.StartsWith("<feed", StringComparison.OrdinalIgnoreCase) || content.StartsWith("<RDF", StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        // If the response looks like HTML, try to find the XML declaration or root element
        var xmlStart = content.IndexOf("<?xml", StringComparison.OrdinalIgnoreCase);
        if (xmlStart >= 0)
        {
            return content[xmlStart..];
        }

        var rssStart = content.IndexOf("<rss", StringComparison.OrdinalIgnoreCase);
        if (rssStart >= 0)
        {
            return content[rssStart..];
        }

        var feedStart = content.IndexOf("<feed", StringComparison.OrdinalIgnoreCase);
        if (feedStart >= 0)
        {
            return content[feedStart..];
        }

        var rdfStart = content.IndexOf("<RDF", StringComparison.OrdinalIgnoreCase);
        if (rdfStart >= 0)
        {
            return content[rdfStart..];
        }

        // Return as-is and let the parser handle it
        return content;
    }

    // ─── XML Helper Methods ─────────────────────────────────────────────────

    /// <summary>
    /// Gets the text value of a direct child element by local name, ignoring namespace.
    /// Returns null if the element is not found.
    /// </summary>
    private static string? GetText(XElement parent, string localName)
    {
        return parent.Elements()
            .FirstOrDefault(e => e.Name.LocalName == localName)?
            .Value?.Trim();
    }

    /// <summary>
    /// Gets the text value of a child element matching a specific namespace.
    /// Falls back to matching by local name if the namespace-prefixed element is not found.
    /// </summary>
    private static string? GetNamespaceElementValue(XElement parent, string localName, XNamespace ns)
    {
        // Try the namespace-prefixed element first
        var nsElement = parent.Element(ns + localName);
        if (nsElement is not null)
            return nsElement.Value?.Trim();

        // Fallback: match by local name only (handles feeds that declare namespaces differently)
        foreach (var element in parent.Elements())
        {
            if (element.Name.LocalName == localName)
            {
                // Accept any element whose local name matches, regardless of namespace
                return element.Value?.Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the text value of an Atom element, trying both the Atom namespace and
    /// the default namespace.
    /// </summary>
    private static string? GetAtomText(XElement parent, string localName)
    {
        // Try Atom namespace first
        var atomElement = parent.Element(AtomNamespace + localName);
        if (atomElement is not null)
            return atomElement.Value?.Trim();

        // Fallback: match by local name
        return GetText(parent, localName);
    }

    /// <summary>
    /// Extracts the best link URL from an Atom entry or feed, preferring
    /// rel="alternate" or no rel attribute. Does not fall back to rel="self"
    /// because self links point to the feed URL, not the content page.
    /// Returns null if no suitable link is found, allowing the caller to
    /// use a fallback URL.
    /// </summary>
    private static string? GetAtomLink(XElement parent)
    {
        var links = parent.Elements(AtomNamespace + "link")
                     .Union(parent.Elements("link"))
                     .ToList();

        if (links.Count == 0)
            return null;

        // Prefer rel="alternate" or links without rel
        var alternate = links.FirstOrDefault(l =>
        {
            var rel = l.Attribute("rel")?.Value;
            return string.IsNullOrEmpty(rel) || rel == "alternate";
        });

        if (alternate is not null)
            return alternate.Attribute("href")?.Value?.Trim();

        // No suitable alternate link found — return null so caller can use fallback
        return null;
    }

    // ─── Date Parsing ───────────────────────────────────────────────────────

    /// <summary>
    /// Parses an RFC 822 date string commonly used in RSS feeds.
    /// Handles variations like "Mon, 01 Jan 2024 12:00:00 GMT" and
    /// "Mon, 01 Jan 2024 12:00:00 +0000".
    /// </summary>
    private static DateTime? ParseRfc822Date(string dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        // Common RFC 822 formats to try
        var formats = new[]
        {
            "ddd, dd MMM yyyy HH:mm:ss zzz",
            "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
            "ddd, dd MMM yyyy HH:mm:ss",
            "dd MMM yyyy HH:mm:ss zzz",
            "dd MMM yyyy HH:mm:ss 'GMT'",
            "dd MMM yyyy HH:mm:ss",
            "yyyy-MM-ddTHH:mm:sszzz",
            "yyyy-MM-ddTHH:mm:ssZ",
            "yyyy-MM-ddTHH:mm:ss",
        };

        // Remove potential timezone abbreviations that aren't parseable
        dateStr = dateStr.Trim();

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(dateStr, format,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AllowWhiteSpaces | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var result))
            {
                return result;
            }
        }

        // Fall back to general parsing
        if (DateTime.TryParse(dateStr,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>
    /// Parses an ISO 8601 date string commonly used in Atom feeds.
    /// Handles variations like "2024-01-01T12:00:00Z" and
    /// "2024-01-01T12:00:00+00:00".
    /// </summary>
    private static DateTime? ParseIso8601Date(string dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        dateStr = dateStr.Trim();

        // Atom feeds often use ISO 8601 with various fractional second precisions
        var formats = new[]
        {
            "yyyy-MM-ddTHH:mm:sszzz",
            "yyyy-MM-ddTHH:mm:ssZ",
            "yyyy-MM-ddTHH:mm:ss.fffzzz",
            "yyyy-MM-ddTHH:mm:ss.fffZ",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-dd",
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(dateStr, format,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AllowWhiteSpaces | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var result))
            {
                return result;
            }
        }

        // Fall back to general parsing
        if (DateTime.TryParse(dateStr,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
