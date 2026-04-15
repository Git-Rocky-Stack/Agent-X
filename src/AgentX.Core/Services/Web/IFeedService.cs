using AgentX.Core.Services.Web.Models;

namespace AgentX.Core.Services.Web;

/// <summary>
/// Parses RSS 2.0 and Atom 1.0 feeds and extracts feed items.
/// Used by the Web Content Ingestion feature to subscribe to and import content
/// from RSS/Atom feeds into the Knowledge Vault.
/// </summary>
public interface IFeedService
{
    /// <summary>
    /// Fetches and parses a feed from the given URL.
    /// Supports RSS 2.0, RSS 1.0 (RDF), and Atom 1.0 feed formats.
    /// </summary>
    /// <param name="feedUrl">The absolute URL of the RSS or Atom feed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="FeedInfo"/> containing feed metadata and all items found in the feed.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown when the feed format is not recognized.</exception>
    Task<FeedInfo> ParseFeedAsync(string feedUrl, CancellationToken ct = default);

    /// <summary>
    /// Fetches and parses a feed from the given URL, returning only items published
    /// after the specified <paramref name="since"/> date.
    /// </summary>
    /// <param name="feedUrl">The absolute URL of the RSS or Atom feed.</param>
    /// <param name="since">Only items published after this date are returned.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of <see cref="FeedItem"/>s published after <paramref name="since"/>.</returns>
    Task<IReadOnlyList<FeedItem>> GetNewItemsAsync(string feedUrl, DateTime since, CancellationToken ct = default);
}