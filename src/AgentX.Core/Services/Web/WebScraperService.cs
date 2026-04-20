using AgentX.Core.Services.Web.Models;
using Serilog;

namespace AgentX.Core.Services.Web;

/// <summary>
/// Thin pipeline orchestrator that delegates web scraping to three specialized services:
/// <list type="bullet">
///   <item><see cref="IWebContentFetcher"/> -- HTTP fetching + JS rendering fallback</item>
///   <item><see cref="IHtmlParser"/> -- readability content extraction + metadata</item>
///   <item><see cref="IStructuredDataExtractor"/> -- JSON-LD, OpenGraph, semantic meta tags</item>
/// </list>
/// YouTube transcript extraction and batch processing remain inline since they are
/// orchestration-level concerns that coordinate across the pipeline services.
/// </summary>
public class WebScraperService : IWebScraperService
{
    private readonly IWebContentFetcher _fetcher;
    private readonly IHtmlParser _parser;
    private readonly IStructuredDataExtractor _extractor;
    private readonly ILogger _log;

    /// <summary>
    /// Delay between batch requests to be polite to servers.
    /// </summary>
    private static readonly TimeSpan BatchRequestDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Initializes a new instance of <see cref="WebScraperService"/>.
    /// </summary>
    /// <param name="fetcher">The content fetcher for HTTP requests and JS rendering fallback.</param>
    /// <param name="parser">The HTML parser for readability extraction and metadata.</param>
    /// <param name="extractor">The structured data extractor for JSON-LD and OpenGraph.</param>
    /// <param name="logger">The Serilog logger instance for structured logging.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="fetcher"/>, <paramref name="parser"/>,
    /// <paramref name="extractor"/>, or <paramref name="logger"/> is null.
    /// </exception>
    public WebScraperService(
        IWebContentFetcher fetcher,
        IHtmlParser parser,
        IStructuredDataExtractor extractor,
        ILogger logger)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _log = logger?.ForContext<WebScraperService>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    // ─── IWebScraperService Implementation ──────────────────────────────────

    /// <inheritdoc />
    public async Task<WebContent> ExtractContentAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return CreateFailureResult(url, "URL must not be empty.");

        if (!IsValidUrl(url))
            return CreateFailureResult(url, $"Invalid URL: '{url}'. Only HTTP and HTTPS URLs are supported.");

        // Route YouTube URLs to the dedicated transcript extractor
        if (IsYouTubeUrl(url))
            return await ExtractYouTubeTranscriptAsync(url, ct).ConfigureAwait(false);

        _log.Debug("Extracting web content from: {Url}", url);

        try
        {
            // Pipeline step 1: Fetch HTML
            var fetchResult = await _fetcher.FetchAsync(url, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(fetchResult.Html))
                return CreateFailureResult(url, "The page returned empty content.");

            // Pipeline step 2: Parse + enrich + build result
            var result = BuildWebContent(fetchResult.Html, url);

            _log.Information(
                "Successfully extracted content from {Url}: '{Title}' ({WordCount} words)",
                url, result.Title, result.WordCount);

            return result;
        }
        catch (TimeoutException ex)
        {
            _log.Warning(ex, "Request timed out for {Url}", url);
            return CreateFailureResult(url, ex.Message);
        }
        catch (OperationCanceledException)
        {
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
            return CreateFailureResult(youtubeUrl, "YouTube URL must not be empty.");

        var videoId = YouTubeTranscriptHelper.ExtractVideoId(youtubeUrl);
        if (string.IsNullOrEmpty(videoId))
            return CreateFailureResult(youtubeUrl, "Could not extract a valid video ID from the URL.");

        _log.Debug("Extracting YouTube transcript for video ID: {VideoId}", videoId);

        try
        {
            // Step 1: Fetch the YouTube watch page
            var watchPageUrl = $"https://www.youtube.com/watch?v={videoId}";
            var watchFetchResult = await _fetcher.FetchAsync(watchPageUrl, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(watchFetchResult.Html))
                return CreateFailureResult(youtubeUrl, "Failed to load the YouTube video page.");

            // Step 2: Extract metadata from the watch page
            var metadata = _parser.ExtractMetadata(watchFetchResult.Html, watchPageUrl);

            // Step 3: Extract captions URL from the page source
            var captionsUrl = YouTubeTranscriptHelper.ExtractCaptionsUrl(watchFetchResult.Html, videoId);

            if (string.IsNullOrEmpty(captionsUrl))
            {
                _log.Warning("No transcript/captions available for YouTube video: {VideoId}", videoId);
                return new WebContent
                {
                    Url = youtubeUrl,
                    Title = metadata.Title ?? string.Empty,
                    Description = metadata.Description,
                    SiteName = "YouTube",
                    FeaturedImageUrl = $"https://img.youtube.com/vi/{videoId}/maxresdefault.jpg",
                    Success = false,
                    ErrorMessage = "No transcript or captions are available for this video.",
                };
            }

            // Step 4: Fetch the transcript XML
            var transcriptFetchResult = await _fetcher.FetchAsync(captionsUrl, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(transcriptFetchResult.Html))
                return CreateFailureResult(youtubeUrl, "Failed to fetch the transcript data.");

            // Step 5: Parse the XML transcript into clean text
            var transcriptText = YouTubeTranscriptHelper.ParseTranscriptXml(transcriptFetchResult.Html);

            if (string.IsNullOrWhiteSpace(transcriptText))
                return CreateFailureResult(youtubeUrl, "The transcript was empty or could not be parsed.");

            var wordCount = CountWords(transcriptText);

            var result = new WebContent
            {
                Url = youtubeUrl,
                Title = metadata.Title ?? string.Empty,
                Content = transcriptText,
                Author = metadata.Author,
                PublishDate = metadata.PublishedDate,
                SiteName = "YouTube",
                Description = metadata.Description,
                FeaturedImageUrl = $"https://img.youtube.com/vi/{videoId}/maxresdefault.jpg",
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
            return Array.Empty<WebContent>();

        _log.Information("Starting batch extraction of {Count} URLs", urls.Count);

        var results = new List<WebContent>(urls.Count);
        var completed = 0;

        foreach (var url in urls)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await ExtractContentAsync(url, ct).ConfigureAwait(false);
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
                await Task.Delay(BatchRequestDelay, ct).ConfigureAwait(false);
            }
        }

        var successCount = results.Count(r => r.Success);
        _log.Information(
            "Batch extraction completed: {Success}/{Total} URLs succeeded",
            successCount, urls.Count);

        return results.AsReadOnly();
    }

    /// <inheritdoc />
    public bool IsYouTubeUrl(string url) => YouTubeTranscriptHelper.IsYouTubeUrl(url);

    /// <inheritdoc />
    public bool IsValidUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    // ─── Pipeline Composition ─────────────────────────────────────────────

    /// <summary>
    /// Composes the full pipeline result by delegating to the three extracted services
    /// and enriching with supplementary data (canonical URL, language, tables).
    /// </summary>
    private WebContent BuildWebContent(string html, string url)
    {
        // Step 1: Parse content and metadata via HtmlParser
        var parsed = _parser.Parse(html, url);

        if (string.IsNullOrWhiteSpace(parsed.Text))
        {
            _log.Warning("No article content extracted from: {Url}", url);
            return CreateFailureResult(url, "Could not extract meaningful article content from the page.");
        }

        // Step 2: Enrich with structured data
        var author = _extractor.ExtractAuthor(html);
        var canonicalUrl = HtmlSupplementaryHelper.ExtractCanonicalUrl(html);
        var language = HtmlSupplementaryHelper.ExtractLanguage(html);
        var tableMarkdown = HtmlSupplementaryHelper.ExtractTablesAsMarkdown(html);

        // Step 3: Build final content with table markdown appended
        var content = parsed.Text;
        if (!string.IsNullOrWhiteSpace(tableMarkdown))
        {
            content = content + "\n\n" + tableMarkdown.TrimEnd();
        }

        var metadata = _parser.ExtractMetadata(html, url);
        var wordCount = CountWords(content);

        return new WebContent
        {
            Url = url,
            Title = parsed.Title,
            Content = content,
            Author = author ?? metadata.Author,
            PublishDate = metadata.PublishedDate,
            SiteName = metadata.SiteName,
            Description = metadata.Description,
            FeaturedImageUrl = metadata.ImageUrl,
            Language = language,
            CanonicalUrl = canonicalUrl,
            WordCount = wordCount,
            Success = true,
        };
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
    private static WebContent CreateFailureResult(string? url, string errorMessage)
    {
        return new WebContent
        {
            Url = url ?? string.Empty,
            Success = false,
            ErrorMessage = errorMessage,
        };
    }
}
