namespace AgentX.Core.Services.Web;

/// <summary>
/// Parses raw HTML into structured content using a readability algorithm that extracts
/// the main article content while stripping non-article elements (navigation, ads, etc.).
/// Also extracts metadata from OpenGraph, Twitter Card, and standard HTML meta tags.
/// </summary>
public interface IHtmlParser
{
    /// <summary>
    /// Parses raw HTML into a structured result containing the extracted article text,
    /// title, description, author, publish date, and estimated reading time.
    /// </summary>
    /// <param name="html">The raw HTML string to parse.</param>
    /// <param name="url">The source URL, used to derive site name when not present in metadata.</param>
    /// <returns>A <see cref="ParsedContent"/> record with extracted content and metadata.</returns>
    ParsedContent Parse(string html, string url);

    /// <summary>
    /// Extracts clean, readable article text from raw HTML using a text-density-based
    /// readability algorithm. Strips non-content elements (nav, footer, script, style, etc.)
    /// and normalizes whitespace.
    /// </summary>
    /// <param name="html">The raw HTML string to extract text from.</param>
    /// <returns>Clean article text, or <see cref="string.Empty"/> if no meaningful content is found.</returns>
    string ExtractReadabilityText(string html);

    /// <summary>
    /// Extracts page metadata (title, description, author, image, publish date, site name)
    /// from the HTML document's head section. Checks OpenGraph, Twitter Card, standard meta tags,
    /// and JSON-LD structured data.
    /// </summary>
    /// <param name="html">The raw HTML string to extract metadata from.</param>
    /// <param name="url">The source URL, used to derive site name when not present in metadata.</param>
    /// <returns>A <see cref="Metadata"/> record with extracted metadata fields.</returns>
    Metadata ExtractMetadata(string html, string url);
}

/// <summary>
/// Represents the fully parsed content of an HTML document, combining extracted article
/// text with key metadata fields and an estimated reading time.
/// </summary>
/// <param name="Title">The page title extracted from og:title, twitter:title, or &lt;title&gt;.</param>
/// <param name="Text">The cleaned article text with HTML tags stripped and whitespace normalized.</param>
/// <param name="Description">The page description from og:description or meta description.</param>
/// <param name="Author">The article author from JSON-LD, meta tags, or rel=author links.</param>
/// <param name="PublishedDate">The publication date from article:published_time or similar meta tags.</param>
/// <param name="ReadingTime">Estimated reading time based on ~225 words per minute.</param>
public record ParsedContent(
    string Title,
    string Text,
    string? Description,
    string? Author,
    DateTime? PublishedDate,
    TimeSpan? ReadingTime);

/// <summary>
/// Represents page-level metadata extracted from an HTML document's head section and
/// structured data (JSON-LD).
/// </summary>
/// <param name="Title">The page title.</param>
/// <param name="Description">The page description.</param>
/// <param name="Author">The article author.</param>
/// <param name="ImageUrl">The featured/hero image URL from og:image or twitter:image.</param>
/// <param name="PublishedDate">The publication date.</param>
/// <param name="SiteName">The site name from og:site_name, or derived from the URL host.</param>
public record Metadata(
    string? Title,
    string? Description,
    string? Author,
    string? ImageUrl,
    DateTime? PublishedDate,
    string? SiteName);
