using System.Net;
using System.Text.Json;
using HtmlAgilityPack;
using Serilog;

namespace AgentX.Core.Services.Web;

/// <summary>
/// Extracts structured data from HTML documents using HtmlAgilityPack for parsing.
/// Handles JSON-LD schema.org markup, Open Graph protocol data, semantic meta tags,
/// and author resolution through a priority-based fallback chain.
/// <para>
/// This service was extracted from <see cref="WebScraperService"/> to isolate structured
/// data concerns from the main content extraction pipeline, enabling independent testing
/// and reuse across the application.
/// </para>
/// </summary>
public class StructuredDataExtractor : IStructuredDataExtractor
{
    private readonly ILogger _log;

    /// <summary>
    /// Initializes a new instance of <see cref="StructuredDataExtractor"/>.
    /// </summary>
    /// <param name="logger">The Serilog logger instance for structured logging.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is null.</exception>
    public StructuredDataExtractor(ILogger logger)
    {
        _log = logger?.ForContext<StructuredDataExtractor>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public JsonLdData? ExtractJsonLd(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
            if (scripts is null || scripts.Count == 0)
                return null;

            foreach (var script in scripts)
            {
                try
                {
                    using var json = JsonDocument.Parse(script.InnerText);
                    var root = json.RootElement;

                    // Handle arrays of JSON-LD objects (some pages have multiple objects in one script)
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in root.EnumerateArray())
                        {
                            var data = BuildJsonLdData(item);
                            if (data is not null)
                                return data;
                        }
                    }
                    else
                    {
                        var data = BuildJsonLdData(root);
                        if (data is not null)
                            return data;
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed JSON-LD blocks silently; they are common in the wild
                    _log.Debug("Skipping malformed JSON-LD block during extraction");
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Unexpected error extracting JSON-LD data");
            return null;
        }
    }

    /// <inheritdoc />
    public OpenGraphData? ExtractOpenGraph(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var ogTitle = GetMetaContent(doc.DocumentNode, "og:title");
            var ogDescription = GetMetaContent(doc.DocumentNode, "og:description");
            var ogImage = GetMetaContent(doc.DocumentNode, "og:image");
            var ogUrl = GetMetaContent(doc.DocumentNode, "og:url");
            var ogType = GetMetaContent(doc.DocumentNode, "og:type");

            // Return null only if no OG tags were found at all
            if (ogTitle is null && ogDescription is null && ogImage is null
                && ogUrl is null && ogType is null)
            {
                return null;
            }

            return new OpenGraphData(
                Title: ogTitle,
                Description: ogDescription,
                Image: ogImage,
                Url: ogUrl,
                Type: ogType);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Unexpected error extracting OpenGraph data");
            return null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<StructuredTag> ExtractMetaTags(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return Array.Empty<StructuredTag>();

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var metaNodes = doc.DocumentNode.SelectNodes("//meta");
            if (metaNodes is null || metaNodes.Count == 0)
                return Array.Empty<StructuredTag>();

            var tags = new List<StructuredTag>(metaNodes.Count);

            foreach (var node in metaNodes)
            {
                // Determine the tag's identifier: property attribute first, then name attribute
                var property = node.GetAttributeValue("property", null);
                var name = node.GetAttributeValue("name", null);
                var content = node.GetAttributeValue("content", null);

                var identifier = property ?? name;
                if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(content))
                    continue;

                tags.Add(new StructuredTag(
                    Name: identifier!.Trim(),
                    Content: content!.Trim(),
                    Property: property is not null ? "property" : (name is not null ? "name" : null)));
            }

            return tags.AsReadOnly();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Unexpected error extracting meta tags");
            return Array.Empty<StructuredTag>();
        }
    }

    /// <inheritdoc />
    public string? ExtractAuthor(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Priority 1: JSON-LD author (most reliable on modern sites)
            var jsonLdAuthor = ExtractJsonLdAuthor(doc);
            if (!string.IsNullOrEmpty(jsonLdAuthor))
                return jsonLdAuthor;

            // Priority 2: Standard meta author sources (author, article:author, dc.creator, byl)
            var metaAuthor = GetMetaContent(doc.DocumentNode, "author")
                             ?? GetMetaContent(doc.DocumentNode, "article:author")
                             ?? GetMetaContent(doc.DocumentNode, "dc.creator")
                             ?? GetMetaContent(doc.DocumentNode, "byl");
            if (!string.IsNullOrEmpty(metaAuthor))
                return WebUtility.HtmlDecode(metaAuthor);

            // Priority 3: Additional meta author sources and rel=author links
            var additionalAuthor = ExtractMetaAuthor(doc);
            if (!string.IsNullOrEmpty(additionalAuthor))
                return additionalAuthor;

            return null;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Unexpected error extracting author from HTML");
            return null;
        }
    }

    // ─── JSON-LD Helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="JsonLdData"/> record from a parsed JSON element,
    /// extracting type, name, author, description, and datePublished fields.
    /// Handles nested <c>@graph</c> structures by recursing into graph items.
    /// </summary>
    private static JsonLdData? BuildJsonLdData(JsonElement element)
    {
        // Check for @graph arrays (schema.org commonly uses this pattern)
        if (element.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
        {
            foreach (var graphItem in graph.EnumerateArray())
            {
                var data = BuildJsonLdData(graphItem);
                if (data is not null)
                    return data;
            }
        }

        // Only consider elements with a @type property (valid schema.org objects)
        if (!element.TryGetProperty("@type", out _))
            return null;

        var type = element.TryGetProperty("@type", out var typeEl) ? typeEl.GetString() : null;

        string? name = null;
        if (element.TryGetProperty("name", out var nameEl))
            name = nameEl.ValueKind == JsonValueKind.String ? nameEl.GetString() : null;

        // Try "headline" as fallback for name (common in Article schema)
        name ??= element.TryGetProperty("headline", out var headlineEl)
            ? headlineEl.ValueKind == JsonValueKind.String ? headlineEl.GetString() : null
            : null;

        var author = element.TryGetProperty("author", out var authorEl)
            ? ResolveAuthorName(authorEl)
            : null;

        string? description = null;
        if (element.TryGetProperty("description", out var descEl))
            description = descEl.ValueKind == JsonValueKind.String ? descEl.GetString() : null;

        DateTime? datePublished = null;
        if (element.TryGetProperty("datePublished", out var dateEl) && dateEl.ValueKind == JsonValueKind.String)
        {
            var dateString = dateEl.GetString();
            if (!string.IsNullOrEmpty(dateString) && DateTime.TryParse(dateString, out var parsed))
                datePublished = parsed.ToUniversalTime();
        }

        return new JsonLdData(
            Type: type,
            Name: name,
            Author: author,
            Description: description,
            DatePublished: datePublished);
    }

    /// <summary>
    /// Resolves an author value from JSON-LD, which may be a plain string,
    /// a single object with a "name" property, or an array of authors.
    /// Returns the first author name found.
    /// </summary>
    private static string? ResolveAuthorName(JsonElement author)
    {
        if (author.ValueKind == JsonValueKind.String)
            return author.GetString();

        if (author.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in author.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    return item.GetString();

                if (item.TryGetProperty("name", out var name))
                    return name.GetString();
            }
        }

        if (author.TryGetProperty("name", out var objectName))
            return objectName.GetString();

        return null;
    }

    /// <summary>
    /// Extracts the author name from JSON-LD structured data embedded in
    /// <c>&lt;script type="application/ld+json"&gt;</c> blocks.
    /// Handles arrays of JSON-LD objects and nested <c>@graph</c> structures.
    /// </summary>
    private string? ExtractJsonLdAuthor(HtmlDocument doc)
    {
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts is null)
            return null;

        foreach (var script in scripts)
        {
            try
            {
                using var json = JsonDocument.Parse(script.InnerText);
                var root = json.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        var author = ExtractAuthorFromJsonElement(item);
                        if (author is not null)
                            return author;
                    }
                }
                else
                {
                    var author = ExtractAuthorFromJsonElement(root);
                    if (author is not null)
                        return author;
                }
            }
            catch (JsonException)
            {
                // Skip malformed JSON-LD blocks
                _log.Debug("Skipping malformed JSON-LD block during author extraction");
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the author name from a single JSON-LD element, handling both
    /// string-form authors and object-form authors with a "name" property.
    /// Also checks nested <c>@graph</c> structures common in schema.org markup.
    /// </summary>
    private static string? ExtractAuthorFromJsonElement(JsonElement element)
    {
        // Check for @graph arrays (schema.org commonly uses this pattern)
        if (element.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
        {
            foreach (var graphItem in graph.EnumerateArray())
            {
                if (graphItem.TryGetProperty("author", out var graphAuthor))
                {
                    var name = ResolveAuthorName(graphAuthor);
                    if (name is not null)
                        return name;
                }
            }
        }

        if (element.TryGetProperty("author", out var author))
            return ResolveAuthorName(author);

        return null;
    }

    /// <summary>
    /// Extracts the author name from HTML meta tags and link elements that are not
    /// covered by the standard meta resolution: <c>&lt;a rel="author"&gt;</c> links.
    /// This serves as the final fallback in the author resolution chain.
    /// </summary>
    private static string? ExtractMetaAuthor(HtmlDocument doc)
    {
        // Try article:author meta property (property-based, not name-based)
        var articleAuthor = doc.DocumentNode.SelectSingleNode("//meta[@property='article:author']");
        if (articleAuthor is not null)
        {
            var content = articleAuthor.GetAttributeValue("content", null);
            if (!string.IsNullOrWhiteSpace(content))
                return content.Trim();
        }

        // Try author meta tag (name-based)
        var authorMeta = doc.DocumentNode.SelectSingleNode("//meta[@name='author']");
        if (authorMeta is not null)
        {
            var content = authorMeta.GetAttributeValue("content", null);
            if (!string.IsNullOrWhiteSpace(content))
                return content.Trim();
        }

        // Try rel=author link (common in WordPress and Blogger themes)
        var authorLink = doc.DocumentNode.SelectSingleNode("//a[@rel='author']");
        if (authorLink is not null)
        {
            var text = authorLink.InnerText?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    // ─── Meta Tag Helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Retrieves the "content" attribute value from a meta tag matched by name or property.
    /// Searches both <c>name</c> and <c>property</c> attributes to cover standard HTML meta
    /// tags, Open Graph tags, and Twitter Card tags.
    /// </summary>
    /// <param name="root">The root node to search within.</param>
    /// <param name="nameOrProperty">The meta tag name or property value to look for.</param>
    /// <returns>The content value, or null if not found.</returns>
    private static string? GetMetaContent(HtmlNode root, string nameOrProperty)
    {
        // Search by property attribute (Open Graph, etc.)
        var node = root.SelectSingleNode($"//meta[@property='{nameOrProperty}']");

        if (node is not null)
        {
            var content = node.GetAttributeValue("content", null);
            if (!string.IsNullOrWhiteSpace(content))
                return content.Trim();
        }

        // Search by name attribute (standard HTML meta tags)
        node = root.SelectSingleNode($"//meta[@name='{nameOrProperty}']");

        if (node is not null)
        {
            var content = node.GetAttributeValue("content", null);
            if (!string.IsNullOrWhiteSpace(content))
                return content.Trim();
        }

        return null;
    }
}
