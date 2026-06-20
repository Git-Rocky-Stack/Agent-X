namespace AgentX.Core.Services.Web.Models;

/// <summary>
/// Represents a single item (entry) from an RSS or Atom feed.
/// </summary>
public class FeedItem
{
    /// <summary>
    /// The title of the feed item.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The full content of the feed item (HTML or plain text).
    /// For RSS, this comes from &lt;content:encoded&gt; falling back to &lt;description&gt;.
    /// For Atom, this comes from &lt;content&gt; falling back to &lt;summary&gt;.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// The canonical URL (link) of the feed item.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The author of the feed item.
    /// For RSS, this comes from &lt;dc:creator&gt; or &lt;author&gt;.
    /// For Atom, this comes from &lt;author&gt;&lt;name&gt;.
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// The publication date of the feed item.
    /// For RSS, this comes from &lt;pubDate&gt;.
    /// For Atom, this comes from &lt;published&gt; falling back to &lt;updated&gt;.
    /// </summary>
    public DateTime? PublishedDate { get; set; }

    /// <summary>
    /// A short description or summary of the feed item.
    /// For RSS, this comes from &lt;description&gt;.
    /// For Atom, this comes from &lt;summary&gt;.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The category or topic of the feed item.
    /// For RSS, this comes from &lt;category&gt;.
    /// For Atom, this comes from &lt;category term=""&gt;.
    /// </summary>
    public string? Category { get; set; }
}

/// <summary>
/// Represents a parsed RSS or Atom feed, including feed-level metadata and all items.
/// </summary>
public class FeedInfo
{
    /// <summary>
    /// The title of the feed.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The URL of the feed's website.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The description of the feed.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The last time the feed was updated, according to the feed itself.
    /// </summary>
    public DateTime? LastUpdated { get; set; }

    /// <summary>
    /// The items (entries) contained in the feed.
    /// </summary>
    public List<FeedItem> Items { get; set; } = [];
}
