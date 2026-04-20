namespace AgentX.Core.Services.Web;

/// <summary>
/// Extracts structured data from HTML documents, including JSON-LD schema.org markup,
/// Open Graph protocol data, and semantic meta tags. Provides a unified interface for
/// structured data extraction independent of the main web scraping pipeline.
/// <para>
/// JSON-LD extraction handles single objects, arrays of objects, and nested
/// <c>@graph</c> structures commonly used in schema.org markup. Open Graph extraction
/// covers the standard <c>og:</c> prefixed properties. Author resolution follows a
/// priority chain: JSON-LD (most reliable) &gt; meta tags &gt; rel=author links.
/// </para>
/// </summary>
public interface IStructuredDataExtractor
{
    /// <summary>
    /// Extracts the primary JSON-LD structured data block from the HTML document.
    /// Parses <c>&lt;script type="application/ld+json"&gt;</c> elements and returns
    /// the first valid schema.org object found. Handles arrays of JSON-LD objects
    /// and nested <c>@graph</c> structures.
    /// </summary>
    /// <param name="html">The raw HTML string to parse.</param>
    /// <returns>
    /// A <see cref="JsonLdData"/> record with extracted fields, or <c>null</c>
    /// if no valid JSON-LD block is found.
    /// </returns>
    JsonLdData? ExtractJsonLd(string html);

    /// <summary>
    /// Extracts Open Graph protocol data from the HTML document's &lt;head&gt; section.
    /// Covers the core OG properties: <c>og:title</c>, <c>og:description</c>,
    /// <c>og:image</c>, <c>og:url</c>, and <c>og:type</c>.
    /// </summary>
    /// <param name="html">The raw HTML string to parse.</param>
    /// <returns>
    /// An <see cref="OpenGraphData"/> record with extracted fields, or <c>null</c>
    /// if no Open Graph tags are found.
    /// </returns>
    OpenGraphData? ExtractOpenGraph(string html);

    /// <summary>
    /// Extracts all semantic meta tags from the HTML document. Returns both
    /// <c>name</c>-based and <c>property</c>-based meta tags (e.g., <c>og:*</c>,
    /// <c>twitter:*</c>, <c>article:*</c>, <c>dc.*</c>, and standard HTML meta tags).
    /// </summary>
    /// <param name="html">The raw HTML string to parse.</param>
    /// <returns>
    /// A read-only list of <see cref="StructuredTag"/> records representing
    /// each meta tag found. Returns an empty list if no meta tags are present.
    /// </returns>
    IReadOnlyList<StructuredTag> ExtractMetaTags(string html);

    /// <summary>
    /// Extracts the author name from the HTML document using a priority-based
    /// resolution chain:
    /// <list type="number">
    ///   <item>JSON-LD <c>author</c> property (most reliable on modern sites)</item>
    ///   <item>Meta tags: <c>author</c>, <c>article:author</c>, <c>dc.creator</c>, <c>byl</c></item>
    ///   <item>Additional meta sources: <c>&lt;meta property="article:author"&gt;</c>,
    ///         <c>&lt;meta name="author"&gt;</c>, <c>&lt;a rel="author"&gt;</c></item>
    /// </list>
    /// Returns the first non-empty author found in the chain.
    /// </summary>
    /// <param name="html">The raw HTML string to parse.</param>
    /// <returns>The resolved author name, or <c>null</c> if no author information is found.</returns>
    string? ExtractAuthor(string html);
}

/// <summary>
/// Represents structured data extracted from a JSON-LD <c>&lt;script type="application/ld+json"&gt;</c> block.
/// Captures the most commonly used schema.org properties for articles and web pages.
/// </summary>
/// <param name="Type">The schema.org type (e.g., "Article", "BlogPosting", "NewsArticle").</param>
/// <param name="Name">The name/headline of the content.</param>
/// <param name="Author">The author name, resolved from string, object, or array forms.</param>
/// <param name="Description">The description or abstract of the content.</param>
/// <param name="DatePublished">The publication date, if parseable.</param>
public record JsonLdData(string? Type, string? Name, string? Author, string? Description, DateTime? DatePublished);

/// <summary>
/// Represents Open Graph protocol data extracted from <c>og:*</c> meta tags.
/// Covers the five core OG properties as defined by the Open Graph protocol specification.
/// </summary>
/// <param name="Title">The og:title of the page.</param>
/// <param name="Description">The og:description of the page.</param>
/// <param name="Image">The og:image URL.</param>
/// <param name="Url">The og:url (canonical URL for the page).</param>
/// <param name="Type">The og:type (e.g., "article", "website").</param>
public record OpenGraphData(string? Title, string? Description, string? Image, string? Url, string? Type);

/// <summary>
/// Represents a single semantic meta tag extracted from an HTML document.
/// Captures both <c>name</c>-based and <c>property</c>-based meta tags.
/// </summary>
/// <param name="Name">The meta tag's name or property attribute value (e.g., "author", "og:title").</param>
/// <param name="Content">The meta tag's content attribute value.</param>
/// <param name="Property">
/// The attribute source: "property" for <c>&lt;meta property="..."&gt;</c>,
/// "name" for <c>&lt;meta name="..."&gt;</c>,
/// or <c>null</c> if neither attribute is present.
/// </param>
public record StructuredTag(string Name, string Content, string? Property);
