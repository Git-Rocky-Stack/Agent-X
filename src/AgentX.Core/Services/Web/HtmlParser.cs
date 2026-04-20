using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using Serilog;

namespace AgentX.Core.Services.Web;

/// <summary>
/// Parses raw HTML into structured content using HtmlAgilityPack and a text-density-based
/// readability algorithm. Extracts article content while stripping non-article elements
/// (navigation, ads, scripts, etc.) and retrieves metadata from OpenGraph, Twitter Card,
/// standard HTML meta tags, and JSON-LD structured data.
/// <para>
/// The readability algorithm works as follows:
/// <list type="number">
///   <item>Remove non-content elements (script, style, nav, header, footer, aside, form, etc.)</item>
///   <item>Look for an <c>&lt;article&gt;</c> element first.</item>
///   <item>If none found, try elements with role="main" or semantic IDs.</item>
///   <item>Score all <c>&lt;div&gt;</c> and <c>&lt;section&gt;</c> elements by text density
///         (ratio of text length to total descendant count plus link density penalty).</item>
///   <item>Select the highest-scoring container as the main content.</item>
///   <item>Extract and clean the text from the selected container.</item>
/// </list>
/// </para>
/// </summary>
public class HtmlParser : IHtmlParser
{
    private readonly ILogger _log;

    /// <summary>
    /// Average adult reading speed in words per minute, used to estimate reading time.
    /// Research shows 200-250 WPM for non-fiction; 225 is a widely used midpoint.
    /// </summary>
    private const int WordsPerMinute = 225;

    /// <summary>
    /// Minimum word count to consider a content extraction successful.
    /// Below this threshold, the algorithm continues to the next fallback strategy.
    /// </summary>
    private const int MinWordThreshold = 20;

    /// <summary>
    /// HTML element names that are removed during content extraction because they
    /// contain non-article content (navigation, scripts, ads, etc.).
    /// </summary>
    private static readonly HashSet<string> ElementsToRemove = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "iframe", "nav", "header", "footer",
        "aside", "form", "button", "select", "textarea", "input",
        "svg", "canvas", "video", "audio", "figure", "figcaption",
        "menu", "menuitem", "dialog"
    };

    /// <summary>
    /// CSS class and ID name fragments that typically indicate non-article content.
    /// Used as a negative signal when scoring content containers.
    /// </summary>
    private static readonly string[] NegativeClassPatterns =
    {
        "comment", "comments", "sidebar", "side-bar", "widget", "footer",
        "header", "nav", "navigation", "menu", "ad", "advertisement",
        "social", "share", "sharing", "related", "recommended", "promo",
        "popup", "modal", "banner", "cookie", "consent", "newsletter",
        "subscribe", "signup", "sign-up", "pagination", "breadcrumb"
    };

    /// <summary>
    /// CSS class and ID name fragments that typically indicate article content.
    /// Used as a positive signal when scoring content containers.
    /// </summary>
    private static readonly string[] PositiveClassPatterns =
    {
        "article", "content", "entry", "post", "text", "body",
        "story", "main", "page", "blog", "single", "hentry",
        "prose", "markdown-body", "rich-text"
    };

    /// <summary>
    /// Regex to normalize runs of whitespace within a single line to a single space.
    /// </summary>
    private static readonly Regex WhitespaceNormalizer = new(
        @"[^\S\n]+",
        RegexOptions.Compiled);

    /// <summary>
    /// Regex to collapse three or more consecutive newlines into exactly two.
    /// </summary>
    private static readonly Regex ExcessiveNewlines = new(
        @"\n{3,}",
        RegexOptions.Compiled);

    /// <summary>
    /// Initializes a new instance of <see cref="HtmlParser"/>.
    /// </summary>
    /// <param name="logger">The Serilog logger instance for structured logging.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is null.</exception>
    public HtmlParser(ILogger logger)
    {
        _log = logger?.ForContext<HtmlParser>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    // ─── IHtmlParser Implementation ──────────────────────────────────────────

    /// <inheritdoc />
    public ParsedContent Parse(string html, string url)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return new ParsedContent(
                Title: string.Empty,
                Text: string.Empty,
                Description: null,
                Author: null,
                PublishedDate: null,
                ReadingTime: null);
        }

        var htmlDoc = LoadDocument(html);

        // Extract metadata from <head>
        var metadata = ExtractMetadataFromDocument(htmlDoc, url);

        // Enrich author metadata with JSON-LD (highest priority source)
        var jsonLdAuthor = ExtractJsonLdAuthor(htmlDoc);
        if (!string.IsNullOrEmpty(jsonLdAuthor))
        {
            metadata = metadata with { Author = jsonLdAuthor };
        }
        else if (string.IsNullOrEmpty(metadata.Author))
        {
            // Fall back to additional meta author sources not covered by primary extraction
            var metaAuthor = ExtractMetaAuthor(htmlDoc);
            if (!string.IsNullOrEmpty(metaAuthor))
            {
                metadata = metadata with { Author = metaAuthor };
            }
        }

        // Extract article content using readability algorithm
        var articleText = ExtractArticleContent(htmlDoc);

        // Clean the extracted text
        var cleanedText = CleanText(articleText);

        // Calculate reading time
        var wordCount = CountWords(cleanedText);
        TimeSpan? readingTime = wordCount > 0
            ? TimeSpan.FromMinutes(Math.Max(1, (double)wordCount / WordsPerMinute))
            : null;

        _log.Debug(
            "Parsed HTML from {Url}: '{Title}' ({WordCount} words, ~{ReadingTime} min reading time)",
            url, metadata.Title, wordCount, readingTime?.TotalMinutes ?? 0);

        return new ParsedContent(
            Title: metadata.Title ?? string.Empty,
            Text: cleanedText,
            Description: metadata.Description,
            Author: metadata.Author,
            PublishedDate: metadata.PublishedDate,
            ReadingTime: readingTime);
    }

    /// <inheritdoc />
    public string ExtractReadabilityText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var htmlDoc = LoadDocument(html);
        var articleText = ExtractArticleContent(htmlDoc);
        return CleanText(articleText);
    }

    /// <inheritdoc />
    public Metadata ExtractMetadata(string html, string url)
    {
        if (string.IsNullOrWhiteSpace(html))
            return new Metadata(null, null, null, null, null, null);

        var htmlDoc = LoadDocument(html);
        var metadata = ExtractMetadataFromDocument(htmlDoc, url);

        // Enrich author with JSON-LD (most reliable source on modern sites)
        var jsonLdAuthor = ExtractJsonLdAuthor(htmlDoc);
        if (!string.IsNullOrEmpty(jsonLdAuthor))
        {
            metadata = metadata with { Author = jsonLdAuthor };
        }
        else if (string.IsNullOrEmpty(metadata.Author))
        {
            var metaAuthor = ExtractMetaAuthor(htmlDoc);
            if (!string.IsNullOrEmpty(metaAuthor))
            {
                metadata = metadata with { Author = metaAuthor };
            }
        }

        return metadata;
    }

    // ─── Document Loading ────────────────────────────────────────────────────

    /// <summary>
    /// Loads raw HTML into an <see cref="HtmlDocument"/>, handling malformed HTML gracefully.
    /// </summary>
    private static HtmlDocument LoadDocument(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc;
    }

    // ─── Metadata Extraction ────────────────────────────────────────────────

    /// <summary>
    /// Extracts page metadata (title, author, publish date, site name, description,
    /// featured image, language) from the HTML document's &lt;head&gt; section.
    /// Checks multiple meta tag conventions: Open Graph, Twitter Card, standard HTML meta tags.
    /// </summary>
    private static Metadata ExtractMetadataFromDocument(HtmlDocument htmlDoc, string url)
    {
        try
        {
            var head = htmlDoc.DocumentNode;

            // Title: og:title > twitter:title > <title>
            var title = GetMetaContent(head, "og:title")
                        ?? GetMetaContent(head, "twitter:title")
                        ?? head.SelectSingleNode("//title")?.InnerText?.Trim()
                        ?? string.Empty;

            title = WebUtility.HtmlDecode(title);

            // Author: author meta > article:author > dc.creator > byl
            var author = GetMetaContent(head, "author")
                         ?? GetMetaContent(head, "article:author")
                         ?? GetMetaContent(head, "dc.creator")
                         ?? GetMetaContent(head, "byl");

            if (!string.IsNullOrEmpty(author))
            {
                author = WebUtility.HtmlDecode(author);
            }

            // Publish date: article:published_time > date > datePublished > dc.date > article:modified_time
            var dateString = GetMetaContent(head, "article:published_time")
                             ?? GetMetaContent(head, "date")
                             ?? GetMetaContent(head, "datePublished")
                             ?? GetMetaContent(head, "dc.date")
                             ?? GetMetaContent(head, "article:modified_time");

            DateTime? publishedDate = null;
            if (!string.IsNullOrEmpty(dateString) && DateTime.TryParse(dateString, out var parsedDate))
            {
                publishedDate = parsedDate.ToUniversalTime();
            }

            // Site name: og:site_name > application-name > URL host fallback
            var siteName = GetMetaContent(head, "og:site_name")
                           ?? GetMetaContent(head, "application-name");

            if (!string.IsNullOrEmpty(siteName))
            {
                siteName = WebUtility.HtmlDecode(siteName);
            }

            if (string.IsNullOrEmpty(siteName) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                siteName = uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
            }

            // Description: og:description > twitter:description > description
            var description = GetMetaContent(head, "og:description")
                              ?? GetMetaContent(head, "twitter:description")
                              ?? GetMetaContent(head, "description");

            if (!string.IsNullOrEmpty(description))
            {
                description = WebUtility.HtmlDecode(description);
            }

            // Featured image: og:image > twitter:image
            var imageUrl = GetMetaContent(head, "og:image")
                           ?? GetMetaContent(head, "twitter:image");

            return new Metadata(
                Title: title,
                Description: description,
                Author: author,
                ImageUrl: imageUrl,
                PublishedDate: publishedDate,
                SiteName: siteName);
        }
        catch (Exception ex)
        {
            // Metadata extraction should never throw; return empty metadata on failure
            System.Diagnostics.Debug.WriteLine($"Metadata extraction failed: {ex.Message}");
            return new Metadata(null, null, null, null, null, null);
        }
    }

    /// <summary>
    /// Retrieves the "content" attribute value from a meta tag matched by name or property.
    /// Searches both <c>name</c> and <c>property</c> attributes to cover standard HTML meta
    /// tags, Open Graph tags, and Twitter Card tags.
    /// </summary>
    private static string? GetMetaContent(HtmlNode root, string nameOrProperty)
    {
        // Search by property attribute (Open Graph, etc.)
        var node = root.SelectSingleNode(
            $"//meta[@property='{nameOrProperty}']");

        if (node is not null)
        {
            var content = node.GetAttributeValue("content", null);
            if (!string.IsNullOrWhiteSpace(content))
                return content.Trim();
        }

        // Search by name attribute (standard HTML meta tags)
        node = root.SelectSingleNode(
            $"//meta[@name='{nameOrProperty}']");

        if (node is not null)
        {
            var content = node.GetAttributeValue("content", null);
            if (!string.IsNullOrWhiteSpace(content))
                return content.Trim();
        }

        return null;
    }

    /// <summary>
    /// Extracts the author name from JSON-LD structured data embedded in
    /// &lt;script type="application/ld+json"&gt; blocks. JSON-LD is the most
    /// reliable source for author information on modern websites.
    /// </summary>
    private static string? ExtractJsonLdAuthor(HtmlDocument doc)
    {
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts == null) return null;

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
                        if (author != null) return author;
                    }
                }
                else
                {
                    var author = ExtractAuthorFromJsonElement(root);
                    if (author != null) return author;
                }
            }
            catch
            {
                // Skip malformed JSON-LD blocks
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
                    if (name != null) return name;
                }
            }
        }

        if (element.TryGetProperty("author", out var author))
        {
            return ResolveAuthorName(author);
        }

        return null;
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
    /// Extracts the author name from HTML meta tags and link elements that are not
    /// covered by the primary metadata extraction: <c>article:author</c> meta property,
    /// <c>author</c> meta name, and <c>&lt;a rel="author"&gt;</c> links.
    /// </summary>
    private static string? ExtractMetaAuthor(HtmlDocument doc)
    {
        // Try article:author meta property
        var articleAuthor = doc.DocumentNode.SelectSingleNode("//meta[@property='article:author']");
        if (articleAuthor != null)
        {
            var content = articleAuthor.GetAttributeValue("content", null);
            if (!string.IsNullOrWhiteSpace(content)) return content.Trim();
        }

        // Try author meta tag (name-based)
        var authorMeta = doc.DocumentNode.SelectSingleNode("//meta[@name='author']");
        if (authorMeta != null)
        {
            var content = authorMeta.GetAttributeValue("content", null);
            if (!string.IsNullOrWhiteSpace(content)) return content.Trim();
        }

        // Try rel=author link (common in WordPress and Blogger themes)
        var authorLink = doc.DocumentNode.SelectSingleNode("//a[@rel='author']");
        if (authorLink != null)
        {
            var text = authorLink.InnerText?.Trim();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        return null;
    }

    // ─── Readability / Content Extraction ───────────────────────────────────

    /// <summary>
    /// Extracts the main article content from an HTML document using a multi-step
    /// readability algorithm:
    /// <list type="number">
    ///   <item>Remove all non-content elements (scripts, styles, nav, etc.).</item>
    ///   <item>Try to find an <c>&lt;article&gt;</c> element.</item>
    ///   <item>If no article, try semantic elements (role="main", id="content").</item>
    ///   <item>Score all block-level containers by text density.</item>
    ///   <item>Fallback: extract all text from body.</item>
    /// </list>
    /// </summary>
    private string ExtractArticleContent(HtmlDocument htmlDoc)
    {
        var body = htmlDoc.DocumentNode.SelectSingleNode("//body");
        if (body is null)
        {
            // Fallback: use the entire document
            body = htmlDoc.DocumentNode;
        }

        // Step 1: Remove non-content elements
        RemoveNonContentElements(body);

        // Step 2: Try <article> element
        var articleNode = body.SelectSingleNode(".//article");
        if (articleNode is not null)
        {
            var articleText = ExtractTextFromNode(articleNode);
            if (!string.IsNullOrWhiteSpace(articleText) && CountWords(articleText) >= MinWordThreshold)
            {
                _log.Debug("Extracted content from <article> element");
                return articleText;
            }
        }

        // Step 3: Try elements with role="main" or semantic IDs
        var mainNode = body.SelectSingleNode(".//*[@role='main']")
                       ?? body.SelectSingleNode(".//*[@id='content']")
                       ?? body.SelectSingleNode(".//*[@id='main-content']")
                       ?? body.SelectSingleNode(".//*[@id='article-body']");

        if (mainNode is not null)
        {
            var mainText = ExtractTextFromNode(mainNode);
            if (!string.IsNullOrWhiteSpace(mainText) && CountWords(mainText) >= MinWordThreshold)
            {
                _log.Debug("Extracted content from semantic main element");
                return mainText;
            }
        }

        // Step 4: Score all block-level containers by text density
        var bestNode = FindBestContentNode(body);
        if (bestNode is not null)
        {
            var bestText = ExtractTextFromNode(bestNode);
            if (!string.IsNullOrWhiteSpace(bestText) && CountWords(bestText) >= MinWordThreshold)
            {
                _log.Debug("Extracted content from highest-scoring container");
                return bestText;
            }
        }

        // Step 5: Fallback - extract all text from body
        _log.Debug("Falling back to full body text extraction");
        return ExtractTextFromNode(body);
    }

    /// <summary>
    /// Removes all elements from the DOM that are known to contain non-article content:
    /// scripts, styles, navigation, headers, footers, forms, ads, etc.
    /// Also removes elements whose class or ID strongly suggest non-content (e.g., "sidebar", "comment").
    /// </summary>
    private static void RemoveNonContentElements(HtmlNode root)
    {
        // Collect nodes to remove first to avoid modifying the tree during iteration
        var nodesToRemove = new List<HtmlNode>();

        foreach (var node in root.DescendantsAndSelf())
        {
            if (node.NodeType != HtmlNodeType.Element)
                continue;

            // Remove known non-content element types
            if (ElementsToRemove.Contains(node.Name))
            {
                nodesToRemove.Add(node);
                continue;
            }

            // Remove elements with obviously non-content class/ID patterns
            var classAttr = node.GetAttributeValue("class", "").ToLowerInvariant();
            var idAttr = node.GetAttributeValue("id", "").ToLowerInvariant();
            var roleAttr = node.GetAttributeValue("role", "").ToLowerInvariant();

            // Skip if the element is the main content area (don't remove role="main")
            if (roleAttr == "main" || roleAttr == "article")
                continue;

            // Remove elements with navigation, banner, or complementary roles
            if (roleAttr is "navigation" or "banner" or "complementary" or "contentinfo")
            {
                nodesToRemove.Add(node);
                continue;
            }

            // Remove elements with strongly non-content class/ID names,
            // but only if they are block-level containers (div, section, etc.)
            if (node.Name is "div" or "section" or "aside" or "ul" or "ol")
            {
                var combined = classAttr + " " + idAttr;

                var negativeScore = NegativeClassPatterns.Count(p => combined.Contains(p, StringComparison.OrdinalIgnoreCase));
                var positiveScore = PositiveClassPatterns.Count(p => combined.Contains(p, StringComparison.OrdinalIgnoreCase));

                // Only remove if strongly negative (negative signals without positive countersignals)
                if (negativeScore >= 2 && positiveScore == 0)
                {
                    nodesToRemove.Add(node);
                }
            }
        }

        // Remove collected nodes (in reverse order to avoid ancestor removal issues)
        foreach (var node in nodesToRemove.AsEnumerable().Reverse())
        {
            try
            {
                node.Remove();
            }
            catch
            {
                // Node may have already been removed as a descendant of a previously removed node
            }
        }
    }

    /// <summary>
    /// Scores all block-level container elements (div, section, td, main) in the document body
    /// by text density and returns the highest-scoring node as the likely main content area.
    /// </summary>
    private static HtmlNode? FindBestContentNode(HtmlNode root)
    {
        var candidates = root.Descendants()
            .Where(n => n.NodeType == HtmlNodeType.Element
                        && n.Name is "div" or "section" or "td" or "main")
            .ToList();

        if (candidates.Count == 0)
            return null;

        HtmlNode? bestNode = null;
        var bestScore = 0.0;

        foreach (var candidate in candidates)
        {
            var score = ScoreContentNode(candidate);

            if (score > bestScore)
            {
                bestScore = score;
                bestNode = candidate;
            }
        }

        return bestNode;
    }

    /// <summary>
    /// Computes a content score for a single HTML node based on text density,
    /// paragraph count, link density, and class/ID signals.
    /// </summary>
    private static double ScoreContentNode(HtmlNode node)
    {
        var textContent = node.InnerText ?? string.Empty;
        var textLength = textContent.Length;

        // Minimum text threshold: skip nodes with very little text
        if (textLength < 100)
            return 0;

        var score = 0.0;

        // Base score: text length (logarithmic to avoid overwhelming other signals)
        score += Math.Log10(Math.Max(textLength, 1)) * 10;

        // Paragraph bonus: more <p> elements strongly indicate article content
        var paragraphCount = node.Descendants("p").Count();
        score += paragraphCount * 5;

        // Text density: text length divided by total descendant element count
        var descendantCount = node.Descendants().Count(d => d.NodeType == HtmlNodeType.Element);
        if (descendantCount > 0)
        {
            var density = (double)textLength / descendantCount;
            score += Math.Min(density, 100); // Cap the density bonus
        }

        // Link density penalty: high ratio of link text to total text indicates navigation
        var linkTextLength = node.Descendants("a")
            .Sum(a => (a.InnerText?.Length ?? 0));

        if (textLength > 0)
        {
            var linkDensity = (double)linkTextLength / textLength;
            if (linkDensity > 0.5)
            {
                score *= 0.1;
            }
            else if (linkDensity > 0.3)
            {
                score *= 0.5;
            }
        }

        // Class/ID signal bonus/penalty
        var classAttr = node.GetAttributeValue("class", "").ToLowerInvariant();
        var idAttr = node.GetAttributeValue("id", "").ToLowerInvariant();
        var combined = classAttr + " " + idAttr;

        foreach (var pattern in PositiveClassPatterns)
        {
            if (combined.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                score += 15;
            }
        }

        foreach (var pattern in NegativeClassPatterns)
        {
            if (combined.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                score -= 20;
            }
        }

        // Heading bonus: presence of headings within the container suggests article structure
        var headingCount = node.Descendants()
            .Count(d => d.Name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6");

        score += headingCount * 3;

        return Math.Max(score, 0);
    }

    // ─── Text Extraction ────────────────────────────────────────────────────

    /// <summary>
    /// Recursively extracts and joins plain text from an HTML node tree.
    /// Inserts newlines after block-level elements and headings to preserve
    /// paragraph structure in the extracted text.
    /// </summary>
    private static string ExtractTextFromNode(HtmlNode node)
    {
        var sb = new StringBuilder();
        ExtractTextRecursive(node, sb);
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Recursively walks the HTML node tree, appending text content to the StringBuilder.
    /// Block-level elements get newline separators to preserve paragraph boundaries.
    /// </summary>
    private static void ExtractTextRecursive(HtmlNode node, StringBuilder sb)
    {
        switch (node.NodeType)
        {
            case HtmlNodeType.Text:
                var text = WebUtility.HtmlDecode(node.InnerText);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.Append(text);
                }
                break;

            case HtmlNodeType.Element:
                // Skip hidden elements
                var style = node.GetAttributeValue("style", "");
                if (style.Contains("display:none", StringComparison.OrdinalIgnoreCase)
                    || style.Contains("display: none", StringComparison.OrdinalIgnoreCase)
                    || style.Contains("visibility:hidden", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var isBlockElement = IsBlockElement(node.Name);

                // Add line break before block elements to preserve paragraph structure
                if (isBlockElement && sb.Length > 0 && sb[sb.Length - 1] != '\n')
                {
                    sb.Append('\n');
                }

                // Special handling for list items
                if (node.Name is "li")
                {
                    sb.Append("- ");
                }

                // Recurse into children
                foreach (var child in node.ChildNodes)
                {
                    ExtractTextRecursive(child, sb);
                }

                // Add line break after block elements
                if (isBlockElement && sb.Length > 0 && sb[sb.Length - 1] != '\n')
                {
                    sb.Append('\n');
                }

                // Add an extra newline after headings and paragraphs for readability
                if (node.Name is "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
                    && sb.Length > 0)
                {
                    sb.Append('\n');
                }

                // Add space after inline elements that typically need word separation
                if (node.Name is "a" or "span" or "em" or "strong" or "b" or "i" or "code"
                    && sb.Length > 0 && sb[sb.Length - 1] != ' ' && sb[sb.Length - 1] != '\n')
                {
                    sb.Append(' ');
                }

                break;
        }
    }

    /// <summary>
    /// Returns whether the given HTML element name is a block-level element
    /// that should be separated by newlines in extracted text.
    /// </summary>
    private static bool IsBlockElement(string elementName)
    {
        return elementName switch
        {
            "p" or "div" or "section" or "article" or "main" => true,
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => true,
            "ul" or "ol" or "li" or "dl" or "dt" or "dd" => true,
            "blockquote" or "pre" or "table" or "tr" or "td" or "th" => true,
            "br" or "hr" => true,
            "header" or "footer" or "nav" or "aside" => true,
            "details" or "summary" or "address" => true,
            _ => false,
        };
    }

    // ─── Text Cleaning ──────────────────────────────────────────────────────

    /// <summary>
    /// Normalizes whitespace and removes excessive blank lines from extracted text.
    /// Collapses runs of horizontal whitespace within a line to a single space,
    /// and reduces three or more consecutive newlines to exactly two (one blank line).
    /// </summary>
    private static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Normalize horizontal whitespace (tabs, multiple spaces) within lines to single space
        text = WhitespaceNormalizer.Replace(text, " ");

        // Collapse excessive newlines (3+ consecutive) to double newline (one blank line)
        text = ExcessiveNewlines.Replace(text, "\n\n");

        // Trim each line individually to remove leading/trailing whitespace
        var lines = text.Split('\n');
        var trimmedLines = lines.Select(line => line.Trim());
        text = string.Join("\n", trimmedLines);

        return text.Trim();
    }

    // ─── Utility Methods ────────────────────────────────────────────────────

    /// <summary>
    /// Counts words by splitting on whitespace, filtering out empty entries.
    /// </summary>
    private static long CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries).LongLength;
    }
}
