namespace AgentX.Core.Services.Web.Models;

/// <summary>
/// Represents the extracted content from a web page, including article text,
/// metadata, and extraction status information.
/// </summary>
public class WebContent
{
    /// <summary>
    /// The original URL that was scraped.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The page title extracted from &lt;title&gt; or og:title meta tags.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The cleaned article text with HTML tags stripped and whitespace normalized.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// The author of the article, extracted from meta tags (e.g., author, article:author).
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// The publication date extracted from article:published_time or date-related meta tags.
    /// </summary>
    public DateTime? PublishDate { get; set; }

    /// <summary>
    /// The name of the website, extracted from og:site_name or similar meta tags.
    /// </summary>
    public string? SiteName { get; set; }

    /// <summary>
    /// The meta description of the page, extracted from the description or og:description meta tag.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The URL of the featured/hero image, extracted from og:image or similar meta tags.
    /// </summary>
    public string? FeaturedImageUrl { get; set; }

    /// <summary>
    /// The total word count of the extracted article content.
    /// </summary>
    public long WordCount { get; set; }

    /// <summary>
    /// The language of the page, extracted from the html lang attribute or content-language meta tag.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Indicates whether the content extraction was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// A human-readable error message when <see cref="Success"/> is <c>false</c>.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
