namespace AgentX.Core.Services.Web;

/// <summary>
/// Parses sitemap.xml files and sitemap index files, extracting all discovered URLs.
/// Used by the Web Content Ingestion feature to discover pages for import into the Knowledge Vault.
/// </summary>
public interface ISitemapParser
{
    /// <summary>
    /// Fetches and parses a sitemap.xml from the given URL, returning all discovered URLs.
    /// If the sitemap is a sitemap index (containing references to child sitemaps),
    /// the child sitemaps are fetched and their URLs are included in the results.
    /// </summary>
    /// <param name="sitemapUrl">The absolute URL of the sitemap.xml file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A flat list of all URLs discovered from the sitemap and any child sitemaps.</returns>
    Task<IReadOnlyList<string>> ParseSitemapAsync(string sitemapUrl, CancellationToken ct = default);

    /// <summary>
    /// Fetches and parses a sitemap index file, returning only the URLs of the child sitemaps
    /// (not the URLs within those child sitemaps).
    /// </summary>
    /// <param name="sitemapIndexUrl">The absolute URL of the sitemap index XML file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of child sitemap URLs found in the sitemap index.</returns>
    Task<IReadOnlyList<string>> ParseSitemapIndexAsync(string sitemapIndexUrl, CancellationToken ct = default);
}
