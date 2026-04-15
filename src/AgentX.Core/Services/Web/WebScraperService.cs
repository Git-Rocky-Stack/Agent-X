using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml.Linq;
using AgentX.Core.Services.Web.Models;
using HtmlAgilityPack;
using Serilog;

namespace AgentX.Core.Services.Web;

/// <summary>
/// Extracts article content from web pages using HtmlAgilityPack and a text-density-based
/// readability algorithm. Supports general HTML pages and YouTube transcript extraction.
/// <para>
/// When the readability algorithm returns minimal content (fewer than 100 characters),
/// the service can optionally fall back to <see cref="IJsRenderingService"/> to render
/// the page in a headless Chromium browser, which allows JavaScript-heavy pages to be
/// fully processed before extraction is attempted again.
/// </para>
/// <para>
/// The readability algorithm works as follows:
/// <list type="number">
///   <item>Remove non-content elements (script, style, nav, header, footer, aside, form, etc.)</item>
///   <item>Look for an <c>&lt;article&gt;</c> element first.</item>
///   <item>If none found, score all <c>&lt;div&gt;</c> and <c>&lt;section&gt;</c> elements by text density
///         (ratio of text length to total descendant count plus link density penalty).</item>
///   <item>Select the highest-scoring container as the main content.</item>
///   <item>Extract and clean the text from the selected container.</item>
/// </list>
/// </para>
/// </summary>
public class WebScraperService : IWebScraperService
{
    private readonly ILogger _log;
    private readonly IJsRenderingService? _jsRenderingService;

    /// <summary>
    /// A long-lived, shared HttpClient instance configured with appropriate defaults
    /// for web scraping: a realistic User-Agent header, 15-second timeout, and
    /// automatic redirect following.
    /// </summary>
    private static readonly HttpClient SharedHttpClient;

    /// <summary>
    /// Default timeout for HTTP requests.
    /// </summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Delay between batch requests to be polite to servers.
    /// </summary>
    private static readonly TimeSpan BatchRequestDelay = TimeSpan.FromMilliseconds(500);

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
    /// Regex to match YouTube video URLs and extract the video ID.
    /// Supports youtube.com/watch?v=, youtu.be/, youtube.com/embed/, and youtube.com/shorts/.
    /// </summary>
    private static readonly Regex YouTubeUrlRegex = new(
        @"(?:https?://)?(?:www\.)?(?:youtube\.com/(?:watch\?.*?v=|embed/|shorts/)|youtu\.be/)(?<id>[\w-]{11})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

    static WebScraperService()
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

        // Use a realistic browser User-Agent to avoid being blocked by sites that
        // reject requests from non-browser clients.
        SharedHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        SharedHttpClient.DefaultRequestHeaders.Accept.ParseAdd(
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

        SharedHttpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }

    /// <summary>
    /// Initializes a new instance of <see cref="WebScraperService"/>.
    /// </summary>
    /// <param name="logger">The Serilog logger instance for structured logging.</param>
    /// <param name="jsRenderingService">
    /// Optional JavaScript rendering service. When provided, the scraper will fall back to
    /// headless Chromium rendering for pages where readability extraction returns minimal content.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is null.</exception>
    public WebScraperService(ILogger logger, IJsRenderingService? jsRenderingService = null)
    {
        _log = logger?.ForContext<WebScraperService>()
               ?? throw new ArgumentNullException(nameof(logger));
        _jsRenderingService = jsRenderingService;
    }

    // ─── IWebScraperService Implementation ──────────────────────────────────

    /// <inheritdoc />
    public async Task<WebContent> ExtractContentAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return CreateFailureResult(url, "URL must not be empty.");
        }

        if (!IsValidUrl(url))
        {
            return CreateFailureResult(url, $"Invalid URL: '{url}'. Only HTTP and HTTPS URLs are supported.");
        }

        // Route YouTube URLs to the dedicated transcript extractor
        if (IsYouTubeUrl(url))
        {
            return await ExtractYouTubeTranscriptAsync(url, ct);
        }

        _log.Debug("Extracting web content from: {Url}", url);

        try
        {
            // 1. Fetch the HTML
            var html = await FetchHtmlAsync(url, ct);

            if (string.IsNullOrWhiteSpace(html))
            {
                return CreateFailureResult(url, "The page returned empty content.");
            }

            // 2. Parse with HtmlAgilityPack
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);

            // 3. Extract metadata from <head>
            var metadata = ExtractMetadata(htmlDoc, url);

            // 3b. Enrich author metadata with JSON-LD and additional meta author sources
            // JSON-LD author takes highest priority (most reliable on modern sites)
            var jsonLdAuthor = ExtractJsonLdAuthor(htmlDoc);
            if (!string.IsNullOrEmpty(jsonLdAuthor))
            {
                metadata.Author = jsonLdAuthor;
            }
            else if (string.IsNullOrEmpty(metadata.Author))
            {
                // Fall back to additional meta author sources not covered by ExtractMetadata
                var metaAuthor = ExtractMetaAuthor(htmlDoc);
                if (!string.IsNullOrEmpty(metaAuthor))
                {
                    metadata.Author = metaAuthor;
                }
            }

            // 3c. Extract canonical URL
            metadata.CanonicalUrl = ExtractCanonicalUrl(htmlDoc);

            // 3d. Extract tables as markdown before readability (tables may be removed
            //     during content extraction, and markdown format preserves structure)
            var tableMarkdown = ExtractTablesAsMarkdown(htmlDoc);

            // 4. Extract article content using readability algorithm
            var articleText = ExtractArticleContent(htmlDoc);

            // 5. If readability returned minimal content, fall back to JS rendering
            if (string.IsNullOrWhiteSpace(articleText) || articleText.Length < 100)
            {
                if (_jsRenderingService is not null)
                {
                    _log.Information(
                        "Readability extraction returned minimal content ({Length} chars) for {Url}, falling back to JS rendering",
                        articleText?.Length ?? 0, url);

                    try
                    {
                        var renderedHtml = await _jsRenderingService.RenderPageAsync(
                            url, waitForNetworkIdle: true, ct);

                        if (!string.IsNullOrWhiteSpace(renderedHtml))
                        {
                            var renderedDoc = new HtmlDocument();
                            renderedDoc.LoadHtml(renderedHtml);

                            // Re-extract metadata from the rendered page (JS may have populated meta tags)
                            metadata = ExtractMetadata(renderedDoc, url);

                            // Re-enrich author with JSON-LD and meta author from rendered page
                            var renderedJsonLdAuthor = ExtractJsonLdAuthor(renderedDoc);
                            if (!string.IsNullOrEmpty(renderedJsonLdAuthor))
                            {
                                metadata.Author = renderedJsonLdAuthor;
                            }
                            else if (string.IsNullOrEmpty(metadata.Author))
                            {
                                var renderedMetaAuthor = ExtractMetaAuthor(renderedDoc);
                                if (!string.IsNullOrEmpty(renderedMetaAuthor))
                                {
                                    metadata.Author = renderedMetaAuthor;
                                }
                            }

                            // Re-extract canonical URL from rendered page
                            metadata.CanonicalUrl = ExtractCanonicalUrl(renderedDoc);

                            // Re-extract tables as markdown from rendered page
                            var renderedTableMarkdown = ExtractTablesAsMarkdown(renderedDoc);

                            // Re-run readability extraction on the rendered DOM
                            var renderedArticleText = ExtractArticleContent(renderedDoc);

                            if (!string.IsNullOrWhiteSpace(renderedArticleText)
                                && renderedArticleText.Length > (articleText?.Length ?? 0))
                            {
                                _log.Information(
                                    "JS rendering fallback improved content from {OldLength} to {NewLength} chars for {Url}",
                                    articleText?.Length ?? 0, renderedArticleText.Length, url);

                                articleText = renderedArticleText;
                                tableMarkdown = renderedTableMarkdown;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "JS rendering fallback failed for {Url}", url);
                        // Continue with whatever readability produced
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(articleText))
            {
                _log.Warning("No article content extracted from: {Url}", url);
                return CreateFailureResult(url, "Could not extract meaningful article content from the page.");
            }

            // 6. Clean the extracted text
            var cleanedText = CleanText(articleText);

            // 6b. Append table markdown if available (adds structured table data that
            //     plain text extraction would lose)
            if (!string.IsNullOrWhiteSpace(tableMarkdown))
            {
                cleanedText = cleanedText + "\n\n" + tableMarkdown.TrimEnd();
            }

            var wordCount = CountWords(cleanedText);

            var result = new WebContent
            {
                Url = url,
                Title = metadata.Title,
                Content = cleanedText,
                Author = metadata.Author,
                PublishDate = metadata.PublishDate,
                SiteName = metadata.SiteName,
                Description = metadata.Description,
                FeaturedImageUrl = metadata.FeaturedImageUrl,
                Language = metadata.Language,
                CanonicalUrl = metadata.CanonicalUrl,
                WordCount = wordCount,
                Success = true,
            };

            _log.Information(
                "Successfully extracted content from {Url}: '{Title}' ({WordCount} words)",
                url, result.Title, result.WordCount);

            return result;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // TaskCanceledException with a non-cancelled token indicates an HTTP timeout
            _log.Warning(ex, "Request timed out for {Url}", url);
            return CreateFailureResult(url, "The request timed out after 15 seconds.");
        }
        catch (OperationCanceledException)
        {
            // Genuine cancellation requested by the caller
            throw;
        }
        catch (HttpRequestException ex)
        {
            _log.Error(ex, "HTTP error while fetching {Url}", url);
            return CreateFailureResult(url, $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unexpected error extracting content from {Url}", url);
            return CreateFailureResult(url, $"Extraction failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<WebContent> ExtractYouTubeTranscriptAsync(string youtubeUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(youtubeUrl))
        {
            return CreateFailureResult(youtubeUrl, "YouTube URL must not be empty.");
        }

        var videoId = ExtractYouTubeVideoId(youtubeUrl);
        if (string.IsNullOrEmpty(videoId))
        {
            return CreateFailureResult(youtubeUrl, "Could not extract a valid video ID from the URL.");
        }

        _log.Debug("Extracting YouTube transcript for video ID: {VideoId}", videoId);

        try
        {
            // Step 1: Fetch the YouTube watch page to find available captions
            var watchPageUrl = $"https://www.youtube.com/watch?v={videoId}";
            var watchPageHtml = await FetchHtmlAsync(watchPageUrl, ct);

            if (string.IsNullOrWhiteSpace(watchPageHtml))
            {
                return CreateFailureResult(youtubeUrl, "Failed to load the YouTube video page.");
            }

            // Extract video title from the watch page
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(watchPageHtml);
            var metadata = ExtractMetadata(htmlDoc, watchPageUrl);

            // Step 2: Extract the captions URL from the page source
            // YouTube embeds caption track info in a JSON structure within the page
            var captionsUrl = ExtractCaptionsUrlFromPage(watchPageHtml, videoId);

            if (string.IsNullOrEmpty(captionsUrl))
            {
                _log.Warning("No transcript/captions available for YouTube video: {VideoId}", videoId);
                return new WebContent
                {
                    Url = youtubeUrl,
                    Title = metadata.Title,
                    Description = metadata.Description,
                    SiteName = "YouTube",
                    FeaturedImageUrl = $"https://img.youtube.com/vi/{videoId}/maxresdefault.jpg",
                    Language = metadata.Language,
                    Success = false,
                    ErrorMessage = "No transcript or captions are available for this video.",
                };
            }

            // Step 3: Fetch the transcript XML
            var transcriptXml = await FetchHtmlAsync(captionsUrl, ct);

            if (string.IsNullOrWhiteSpace(transcriptXml))
            {
                return CreateFailureResult(youtubeUrl, "Failed to fetch the transcript data.");
            }

            // Step 4: Parse the XML transcript into clean text
            var transcriptText = ParseYouTubeTranscriptXml(transcriptXml);

            if (string.IsNullOrWhiteSpace(transcriptText))
            {
                return CreateFailureResult(youtubeUrl, "The transcript was empty or could not be parsed.");
            }

            var wordCount = CountWords(transcriptText);

            var result = new WebContent
            {
                Url = youtubeUrl,
                Title = metadata.Title,
                Content = transcriptText,
                Author = metadata.Author,
                PublishDate = metadata.PublishDate,
                SiteName = "YouTube",
                Description = metadata.Description,
                FeaturedImageUrl = $"https://img.youtube.com/vi/{videoId}/maxresdefault.jpg",
                Language = metadata.Language,
                WordCount = wordCount,
                Success = true,
            };

            _log.Information(
                "Successfully extracted YouTube transcript for {VideoId}: '{Title}' ({WordCount} words)",
                videoId, result.Title, result.WordCount);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to extract YouTube transcript for: {Url}", youtubeUrl);
            return CreateFailureResult(youtubeUrl, $"YouTube transcript extraction failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WebContent>> ExtractBatchAsync(
        IReadOnlyList<string> urls,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (urls is null || urls.Count == 0)
        {
            return Array.Empty<WebContent>();
        }

        _log.Information("Starting batch extraction of {Count} URLs", urls.Count);

        var results = new List<WebContent>(urls.Count);
        var completed = 0;

        foreach (var url in urls)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await ExtractContentAsync(url, ct);
                results.Add(result);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Batch extraction failed for URL: {Url}", url);
                results.Add(CreateFailureResult(url, $"Extraction failed: {ex.Message}"));
            }

            completed++;
            progress?.Report(completed);

            // Politeness delay between requests (skip after the last one)
            if (completed < urls.Count)
            {
                await Task.Delay(BatchRequestDelay, ct);
            }
        }

        var successCount = results.Count(r => r.Success);
        _log.Information(
            "Batch extraction completed: {Success}/{Total} URLs succeeded",
            successCount, urls.Count);

        return results.AsReadOnly();
    }

    /// <inheritdoc />
    public bool IsYouTubeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        return YouTubeUrlRegex.IsMatch(url);
    }

    /// <inheritdoc />
    public bool IsValidUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // Only allow HTTP and HTTPS schemes
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    // ─── HTTP Fetching ──────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the raw HTML content from the specified URL using the shared HttpClient.
    /// </summary>
    /// <param name="url">The URL to fetch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The HTML string, or null if the request fails.</returns>
    private async Task<string> FetchHtmlAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        // Some sites return different content based on Accept header
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");

        using var response = await SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // Read content with a size limit to prevent out-of-memory on extremely large pages
        const int maxContentLength = 10 * 1024 * 1024; // 10 MB
        var contentLength = response.Content.Headers.ContentLength;

        if (contentLength.HasValue && contentLength.Value > maxContentLength)
        {
            throw new InvalidOperationException(
                $"Page content too large ({contentLength.Value / 1024 / 1024:F1} MB). Maximum is 10 MB.");
        }

        return await response.Content.ReadAsStringAsync(ct);
    }

    // ─── Metadata Extraction ────────────────────────────────────────────────

    /// <summary>
    /// Extracts page metadata (title, author, publish date, site name, description,
    /// featured image, language) from the HTML document's &lt;head&gt; section.
    /// Checks multiple meta tag conventions: Open Graph, Twitter Card, standard HTML meta tags.
    /// </summary>
    private PageMetadata ExtractMetadata(HtmlDocument htmlDoc, string url)
    {
        var metadata = new PageMetadata();

        try
        {
            var head = htmlDoc.DocumentNode;

            // Title: og:title > twitter:title > <title>
            metadata.Title = GetMetaContent(head, "og:title")
                             ?? GetMetaContent(head, "twitter:title")
                             ?? head.SelectSingleNode("//title")?.InnerText?.Trim()
                             ?? string.Empty;

            // Decode HTML entities in the title
            metadata.Title = WebUtility.HtmlDecode(metadata.Title);

            // Author: author meta > article:author > dc.creator
            metadata.Author = GetMetaContent(head, "author")
                               ?? GetMetaContent(head, "article:author")
                               ?? GetMetaContent(head, "dc.creator")
                               ?? GetMetaContent(head, "byl");

            if (!string.IsNullOrEmpty(metadata.Author))
            {
                metadata.Author = WebUtility.HtmlDecode(metadata.Author);
            }

            // Publish date: article:published_time > date > datePublished > dc.date
            var dateString = GetMetaContent(head, "article:published_time")
                             ?? GetMetaContent(head, "date")
                             ?? GetMetaContent(head, "datePublished")
                             ?? GetMetaContent(head, "dc.date")
                             ?? GetMetaContent(head, "article:modified_time");

            if (!string.IsNullOrEmpty(dateString) && DateTime.TryParse(dateString, out var parsedDate))
            {
                metadata.PublishDate = parsedDate.ToUniversalTime();
            }

            // Site name: og:site_name > application-name
            metadata.SiteName = GetMetaContent(head, "og:site_name")
                                ?? GetMetaContent(head, "application-name");

            if (!string.IsNullOrEmpty(metadata.SiteName))
            {
                metadata.SiteName = WebUtility.HtmlDecode(metadata.SiteName);
            }

            // If site name is still empty, derive from the URL host
            if (string.IsNullOrEmpty(metadata.SiteName) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                metadata.SiteName = uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
            }

            // Description: og:description > twitter:description > description
            metadata.Description = GetMetaContent(head, "og:description")
                                   ?? GetMetaContent(head, "twitter:description")
                                   ?? GetMetaContent(head, "description");

            if (!string.IsNullOrEmpty(metadata.Description))
            {
                metadata.Description = WebUtility.HtmlDecode(metadata.Description);
            }

            // Featured image: og:image > twitter:image
            metadata.FeaturedImageUrl = GetMetaContent(head, "og:image")
                                        ?? GetMetaContent(head, "twitter:image");

            // Language: html lang attribute > content-language meta
            var htmlNode = htmlDoc.DocumentNode.SelectSingleNode("//html");
            metadata.Language = htmlNode?.GetAttributeValue("lang", null)
                                ?? GetMetaHttpEquiv(head, "content-language");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Error extracting metadata from {Url}", url);
        }

        return metadata;
    }

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
    /// Retrieves the "content" attribute value from a meta tag matched by http-equiv.
    /// </summary>
    private static string? GetMetaHttpEquiv(HtmlNode root, string httpEquiv)
    {
        var node = root.SelectSingleNode(
            $"//meta[@http-equiv='{httpEquiv}']");

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
    /// <c>&lt;script type="application/ld+json"&gt;</c> blocks. JSON-LD is the most
    /// reliable source for author information on modern websites and takes priority
    /// over meta tag extraction.
    /// </summary>
    private string? ExtractJsonLdAuthor(HtmlDocument doc)
    {
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts == null) return null;

        foreach (var script in scripts)
        {
            try
            {
                using var json = JsonDocument.Parse(script.InnerText);
                var root = json.RootElement;

                // Handle arrays of JSON-LD objects (some pages have multiple scripts)
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
    /// covered by <see cref="ExtractMetadata"/>: <c>article:author</c> meta property,
    /// <c>author</c> meta name, and <c>&lt;a rel="author"&gt;</c> links.
    /// This serves as a fallback when JSON-LD author data is unavailable.
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

        // Try author meta tag (different from the property-based version)
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

    /// <summary>
    /// Extracts the canonical URL from <c>&lt;link rel="canonical"&gt;</c> in the
    /// document head. The canonical URL represents the preferred URL for the page,
    /// which may differ from the request URL when pages have alternate URLs
    /// (query parameters, www vs non-www, etc.).
    /// </summary>
    private static string? ExtractCanonicalUrl(HtmlDocument doc)
    {
        var link = doc.DocumentNode.SelectSingleNode("//link[@rel='canonical']");
        return link?.GetAttributeValue("href", null);
    }

    /// <summary>
    /// Converts HTML <c>&lt;table&gt;</c> elements to markdown table format.
    /// Each table is rendered as a pipe-delimited markdown table with a separator
    /// row after the header. Tables are separated by blank lines in the output.
    /// </summary>
    private static string ExtractTablesAsMarkdown(HtmlDocument doc)
    {
        var sb = new StringBuilder();
        var tables = doc.DocumentNode.SelectNodes("//table");
        if (tables == null) return string.Empty;

        foreach (var table in tables)
        {
            var rows = table.SelectNodes(".//tr");
            if (rows == null) continue;

            var isFirstRow = true;
            foreach (var row in rows)
            {
                var cells = row.SelectNodes(".//th | .//td");
                if (cells == null) continue;

                var cellTexts = cells.Select(c =>
                {
                    var text = WebUtility.HtmlDecode(c.InnerText ?? string.Empty).Trim();
                    // Escape pipe characters in cell content for valid markdown
                    return text.Replace("|", "\\|");
                }).ToList();

                if (cellTexts.Count == 0) continue;

                sb.AppendLine("| " + string.Join(" | ", cellTexts) + " |");

                if (isFirstRow)
                {
                    sb.AppendLine("| " + string.Join(" | ", cellTexts.Select(_ => "---")) + " |");
                    isFirstRow = false;
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ─── Readability / Content Extraction ───────────────────────────────────

    /// <summary>
    /// Extracts the main article content from an HTML document using a multi-step
    /// readability algorithm:
    /// <list type="number">
    ///   <item>Remove all non-content elements (scripts, styles, nav, etc.).</item>
    ///   <item>Try to find an <c>&lt;article&gt;</c> element.</item>
    ///   <item>If no article, score all block-level containers by text density.</item>
    ///   <item>Extract text from the highest-scoring container.</item>
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

        // Step 2: Try to find <article> element
        var articleNode = body.SelectSingleNode(".//article");
        if (articleNode is not null)
        {
            var articleText = ExtractTextFromNode(articleNode);
            if (!string.IsNullOrWhiteSpace(articleText) && CountWords(articleText) >= 20)
            {
                _log.Debug("Extracted content from <article> element");
                return articleText;
            }
        }

        // Step 3: Try elements with role="main" or id/class containing "content"/"article"
        var mainNode = body.SelectSingleNode(".//*[@role='main']")
                       ?? body.SelectSingleNode(".//*[@id='content']")
                       ?? body.SelectSingleNode(".//*[@id='main-content']")
                       ?? body.SelectSingleNode(".//*[@id='article-body']");

        if (mainNode is not null)
        {
            var mainText = ExtractTextFromNode(mainNode);
            if (!string.IsNullOrWhiteSpace(mainText) && CountWords(mainText) >= 20)
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
            if (!string.IsNullOrWhiteSpace(bestText) && CountWords(bestText) >= 20)
            {
                _log.Debug("Extracted content from highest-scoring container");
                return bestText;
            }
        }

        // Step 5: Fallback — extract all text from body
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

                // Count negative vs positive signals
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
    /// Scores all block-level container elements (div, section, td) in the document body
    /// by text density and returns the highest-scoring node as the likely main content area.
    /// <para>
    /// The scoring algorithm considers:
    /// <list type="bullet">
    ///   <item><b>Text length</b>: longer text content scores higher.</item>
    ///   <item><b>Paragraph count</b>: more &lt;p&gt; children indicate article-like structure.</item>
    ///   <item><b>Link density</b>: high ratio of link text to total text is penalized (navigation areas).</item>
    ///   <item><b>Class/ID signals</b>: positive class names like "article", "content" boost score;
    ///     negative names like "sidebar", "comment" reduce it.</item>
    ///   <item><b>Descendant count normalization</b>: text length is divided by descendant count
    ///     to favor containers with dense, direct text over deeply nested wrappers.</item>
    /// </list>
    /// </para>
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
        // Get direct and descendant text content
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
        // This penalizes deeply nested containers with little direct text
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
                // Heavy link density: likely navigation or link list
                score *= 0.1;
            }
            else if (linkDensity > 0.3)
            {
                // Moderate link density: somewhat penalize
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

    // ─── YouTube Transcript Helpers ─────────────────────────────────────────

    /// <summary>
    /// Extracts the YouTube video ID from various URL formats.
    /// </summary>
    private static string? ExtractYouTubeVideoId(string url)
    {
        var match = YouTubeUrlRegex.Match(url);
        return match.Success ? match.Groups["id"].Value : null;
    }

    /// <summary>
    /// Extracts the auto-generated or manually-uploaded captions URL from the YouTube
    /// watch page HTML. YouTube embeds this information in a JSON blob within a script tag.
    /// </summary>
    private static string? ExtractCaptionsUrlFromPage(string pageHtml, string videoId)
    {
        // YouTube embeds captions info in ytInitialPlayerResponse or similar JSON structures.
        // Look for "captionTracks" in the page source.
        const string captionTracksMarker = "\"captionTracks\":";
        var markerIndex = pageHtml.IndexOf(captionTracksMarker, StringComparison.Ordinal);

        if (markerIndex < 0)
        {
            // Try alternative marker
            const string altMarker = "\"captions\":";
            markerIndex = pageHtml.IndexOf(altMarker, StringComparison.Ordinal);
            if (markerIndex < 0)
                return null;

            // Navigate to captionTracks within the captions object
            var nestedIndex = pageHtml.IndexOf(captionTracksMarker, markerIndex, StringComparison.Ordinal);
            if (nestedIndex < 0)
                return null;

            markerIndex = nestedIndex;
        }

        // Find the array start
        var arrayStart = pageHtml.IndexOf('[', markerIndex);
        if (arrayStart < 0)
            return null;

        // Find the matching array end (handle nested objects)
        var depth = 0;
        var arrayEnd = -1;
        for (var i = arrayStart; i < pageHtml.Length; i++)
        {
            switch (pageHtml[i])
            {
                case '[':
                    depth++;
                    break;
                case ']':
                    depth--;
                    if (depth == 0)
                    {
                        arrayEnd = i;
                        goto FoundEnd;
                    }
                    break;
            }
        }

    FoundEnd:
        if (arrayEnd < 0)
            return null;

        var captionTracksJson = pageHtml.Substring(arrayStart, arrayEnd - arrayStart + 1);

        // Extract the first baseUrl from the caption tracks
        // Look for English captions first, then fall back to any available
        var bestUrl = ExtractCaptionUrl(captionTracksJson, preferredLanguage: "en")
                      ?? ExtractCaptionUrl(captionTracksJson, preferredLanguage: null);

        return bestUrl;
    }

    /// <summary>
    /// Extracts a caption track URL from the JSON array string.
    /// Optionally filters by language code.
    /// </summary>
    private static string? ExtractCaptionUrl(string jsonArray, string? preferredLanguage)
    {
        // Simple extraction using string searching since we don't want to add
        // a JSON dependency for this single use case (System.Text.Json is available
        // but the JSON is embedded in JavaScript and may not be cleanly extractable)
        var searchStart = 0;

        while (searchStart < jsonArray.Length)
        {
            // Find next baseUrl
            const string baseUrlKey = "\"baseUrl\":\"";
            var urlKeyIndex = jsonArray.IndexOf(baseUrlKey, searchStart, StringComparison.Ordinal);
            if (urlKeyIndex < 0)
                break;

            var urlStart = urlKeyIndex + baseUrlKey.Length;
            var urlEnd = jsonArray.IndexOf('"', urlStart);
            if (urlEnd < 0)
                break;

            var url = jsonArray.Substring(urlStart, urlEnd - urlStart);
            // Unescape JSON string escapes (primarily \u0026 for &)
            url = url.Replace("\\u0026", "&").Replace("\\/", "/");

            if (preferredLanguage is null)
            {
                // Return the first URL found
                return url;
            }

            // Check if this track is for the preferred language
            // Look for languageCode near this baseUrl
            var contextStart = Math.Max(0, urlKeyIndex - 200);
            var contextEnd = Math.Min(jsonArray.Length, urlKeyIndex + url.Length + 200);
            var context = jsonArray.Substring(contextStart, contextEnd - contextStart);

            var langPattern = $"\"languageCode\":\"{preferredLanguage}\"";
            if (context.Contains(langPattern, StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            // Also check for vssId which uses language codes like ".en" or "a.en" (auto-generated)
            var vssPattern1 = $"\"vssId\":\".{preferredLanguage}\"";
            var vssPattern2 = $"\"vssId\":\"a.{preferredLanguage}\"";
            if (context.Contains(vssPattern1, StringComparison.OrdinalIgnoreCase)
                || context.Contains(vssPattern2, StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            searchStart = urlEnd + 1;
        }

        return null;
    }

    /// <summary>
    /// Parses YouTube's XML transcript format into clean plain text with timestamps.
    /// The XML format uses &lt;text&gt; elements with start and dur attributes.
    /// </summary>
    private static string ParseYouTubeTranscriptXml(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var sb = new StringBuilder();

            var textElements = doc.Descendants("text").ToList();

            foreach (var element in textElements)
            {
                var startAttr = element.Attribute("start")?.Value;
                var text = element.Value;

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                // Decode HTML entities that YouTube may include in transcript text
                text = WebUtility.HtmlDecode(text).Trim();

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                // Format timestamp for readability
                if (!string.IsNullOrEmpty(startAttr) && double.TryParse(startAttr,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var startSeconds))
                {
                    var timeSpan = TimeSpan.FromSeconds(startSeconds);
                    var timestamp = timeSpan.TotalHours >= 1
                        ? timeSpan.ToString(@"h\:mm\:ss")
                        : timeSpan.ToString(@"m\:ss");

                    sb.AppendLine($"[{timestamp}] {text}");
                }
                else
                {
                    sb.AppendLine(text);
                }
            }

            return sb.ToString().Trim();
        }
        catch (Exception)
        {
            // If XML parsing fails, try a regex-based fallback
            return ParseTranscriptFallback(xml);
        }
    }

    /// <summary>
    /// Fallback transcript parser that uses regex to extract text from malformed
    /// XML transcript data.
    /// </summary>
    private static string ParseTranscriptFallback(string xml)
    {
        var textPattern = new Regex(
            @"<text[^>]*>(?<content>[^<]*)</text>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        var sb = new StringBuilder();

        foreach (Match match in textPattern.Matches(xml))
        {
            var content = WebUtility.HtmlDecode(match.Groups["content"].Value).Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                sb.AppendLine(content);
            }
        }

        return sb.ToString().Trim();
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

    /// <summary>
    /// Creates a <see cref="WebContent"/> instance representing a failed extraction.
    /// </summary>
    private static WebContent CreateFailureResult(string url, string errorMessage)
    {
        return new WebContent
        {
            Url = url ?? string.Empty,
            Success = false,
            ErrorMessage = errorMessage,
        };
    }

    // ─── Internal Types ─────────────────────────────────────────────────────

    /// <summary>
    /// Internal DTO holding extracted page metadata before it is mapped
    /// to the public <see cref="WebContent"/> model.
    /// </summary>
    private sealed class PageMetadata
    {
        public string Title { get; set; } = string.Empty;
        public string? Author { get; set; }
        public DateTime? PublishDate { get; set; }
        public string? SiteName { get; set; }
        public string? Description { get; set; }
        public string? FeaturedImageUrl { get; set; }
        public string? Language { get; set; }
        public string? CanonicalUrl { get; set; }
    }
}
