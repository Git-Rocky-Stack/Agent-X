using AgentX.Core.Services.Web.Models;

namespace AgentX.Core.Services.Web;

/// <summary>
/// Extracts article content and metadata from web pages. Supports general HTML pages
/// and YouTube video transcripts. Used by <see cref="WebImportService"/> to ingest
/// web content into the Knowledge Vault.
/// </summary>
public interface IWebScraperService
{
    /// <summary>
    /// Fetches the HTML for the given URL and extracts article content using a readability
    /// algorithm that identifies the main content area while stripping navigation, headers,
    /// footers, ads, and other non-article elements.
    /// </summary>
    /// <param name="url">The absolute HTTP or HTTPS URL to scrape.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="WebContent"/> with extracted text, title, and metadata.
    /// If extraction fails, <see cref="WebContent.Success"/> is <c>false</c> and
    /// <see cref="WebContent.ErrorMessage"/> describes the failure.
    /// </returns>
    Task<WebContent> ExtractContentAsync(string url, CancellationToken ct = default);

    /// <summary>
    /// Extracts the transcript text from a YouTube video. Supports both
    /// <c>youtube.com/watch?v=</c> and <c>youtu.be/</c> URL formats.
    /// </summary>
    /// <param name="youtubeUrl">A YouTube video URL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="WebContent"/> containing the transcript text as the content.
    /// If no transcript is available, <see cref="WebContent.Success"/> is <c>false</c>.
    /// </returns>
    Task<WebContent> ExtractYouTubeTranscriptAsync(string youtubeUrl, CancellationToken ct = default);

    /// <summary>
    /// Extracts content from multiple URLs sequentially, reporting progress after each URL.
    /// Individual failures do not abort the batch; failed URLs produce <see cref="WebContent"/>
    /// entries with <see cref="WebContent.Success"/> set to <c>false</c>.
    /// A 500ms delay is inserted between requests for politeness.
    /// </summary>
    /// <param name="urls">The list of URLs to scrape.</param>
    /// <param name="progress">Optional progress reporter (number of URLs completed).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of <see cref="WebContent"/> results, one per input URL.</returns>
    Task<IReadOnlyList<WebContent>> ExtractBatchAsync(
        IReadOnlyList<string> urls,
        IProgress<int>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Determines whether the given URL points to a YouTube video.
    /// Matches <c>youtube.com/watch?v=</c>, <c>youtu.be/</c>, <c>youtube.com/embed/</c>,
    /// and <c>youtube.com/shorts/</c> patterns.
    /// </summary>
    /// <param name="url">The URL to check.</param>
    /// <returns><c>true</c> if the URL is a recognized YouTube video URL.</returns>
    bool IsYouTubeUrl(string url);

    /// <summary>
    /// Validates that a string is a well-formed absolute HTTP or HTTPS URL.
    /// </summary>
    /// <param name="url">The string to validate.</param>
    /// <returns><c>true</c> if the URL is valid and uses the HTTP or HTTPS scheme.</returns>
    bool IsValidUrl(string url);
}
