namespace AgentX.Core.Data.Entities;

/// <summary>
/// Represents a user's subscription to an RSS or Atom feed.
/// Persisted in the SQLite database via Entity Framework Core.
/// </summary>
public class FeedSubscriptionEntity
{
    /// <summary>
    /// Unique identifier for the feed subscription.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// The URL of the RSS or Atom feed.
    /// </summary>
    public string FeedUrl { get; set; } = string.Empty;

    /// <summary>
    /// The title of the feed, as parsed from the feed XML or set by the user.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The collection ID to which new items from this feed are automatically imported.
    /// If null, items are imported to the user's default collection.
    /// </summary>
    public long? DefaultCollectionId { get; set; }

    /// <summary>
    /// Whether new items from this feed are automatically imported into the Knowledge Vault.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool AutoImport { get; set; } = true;

    /// <summary>
    /// How often (in minutes) the feed is polled for new items.
    /// Defaults to 60 minutes.
    /// </summary>
    public int PollIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// The timestamp of the last time this feed was polled for new items.
    /// </summary>
    public DateTime LastPolledAt { get; set; } = DateTime.MinValue;

    /// <summary>
    /// The timestamp when this feed subscription was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this feed subscription is active. Disabled feeds are not polled.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}