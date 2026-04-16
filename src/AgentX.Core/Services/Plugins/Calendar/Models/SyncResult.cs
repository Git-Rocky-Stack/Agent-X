namespace AgentX.Core.Services.Plugins.Calendar.Models;

/// <summary>
/// Result of a calendar or email sync operation, tracking how many
/// items were added, updated, skipped, or failed.
/// </summary>
public sealed class SyncResult
{
    /// <summary>
    /// Number of new items fetched and added to the inbox.
    /// </summary>
    public int ItemsAdded { get; init; }

    /// <summary>
    /// Number of existing items that were updated (modified since last sync).
    /// </summary>
    public int ItemsUpdated { get; init; }

    /// <summary>
    /// Number of items skipped (unchanged since last sync, detected via delta token).
    /// </summary>
    public int ItemsSkipped { get; init; }

    /// <summary>
    /// Number of items that failed to process. Errors are logged individually.
    /// </summary>
    public int ItemsFailed { get; init; }

    /// <summary>
    /// Total number of items examined during the sync operation.
    /// </summary>
    public int TotalItemsProcessed => ItemsAdded + ItemsUpdated + ItemsSkipped + ItemsFailed;

    /// <summary>
    /// Whether the sync completed without any failures.
    /// </summary>
    public bool IsSuccess => ItemsFailed == 0;

    /// <summary>
    /// UTC timestamp when the sync operation started.
    /// </summary>
    public DateTime StartedAt { get; init; }

    /// <summary>
    /// UTC timestamp when the sync operation completed.
    /// </summary>
    public DateTime CompletedAt { get; init; }

    /// <summary>
    /// Duration of the sync operation.
    /// </summary>
    public TimeSpan Duration => CompletedAt - StartedAt;

    /// <summary>
    /// Provider-specific delta token for incremental sync on the next cycle.
    /// Null for full syncs or when the provider does not support delta tokens.
    /// </summary>
    public string? DeltaToken { get; init; }
}