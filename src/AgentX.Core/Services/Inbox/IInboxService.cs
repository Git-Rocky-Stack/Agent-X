using AgentX.Core.Data.Entities;

namespace AgentX.Core.Services.Inbox;

/// <summary>
/// Smart Inbox service that holds newly-detected files in a triage queue before they
/// enter the full indexing pipeline. Callers can inspect AI-generated previews and
/// collection suggestions, then accept, reject, or defer individual items or batches.
/// </summary>
public interface IInboxService
{
    // ── Ingestion ────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a file to the inbox with <c>Status = "pending"</c>.
    /// Duplicate paths that are already pending are returned as-is without creating
    /// a second row.
    /// </summary>
    /// <param name="filePath">Absolute path of the file to triage.</param>
    /// <param name="watchFolderId">
    /// Optional ID of the watch folder that detected the file.
    /// </param>
    /// <returns>The newly created (or pre-existing pending) inbox item.</returns>
    Task<InboxItemEntity> AddToInboxAsync(
        string filePath,
        long? watchFolderId = null,
        string? sourceType = null,
        string? sourceUrl = null);

    // ── Queries ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all items whose <c>Status</c> is "pending", ordered by
    /// <c>AddedAt</c> ascending (oldest first).
    /// </summary>
    Task<IReadOnlyList<InboxItemEntity>> GetPendingItemsAsync();

    /// <summary>
    /// Returns a paged slice of inbox items, optionally filtered by status.
    /// Results are ordered by <c>AddedAt</c> descending (newest first).
    /// </summary>
    /// <param name="statusFilter">
    /// One of "pending", "accepted", "rejected", "deferred", or <c>null</c> to
    /// return all statuses.
    /// </param>
    /// <param name="skip">Number of rows to skip (for pagination).</param>
    /// <param name="take">Maximum number of rows to return.</param>
    Task<IReadOnlyList<InboxItemEntity>> GetAllItemsAsync(
        string? statusFilter = null,
        int skip = 0,
        int take = 50);

    /// <summary>
    /// Returns the count of items currently in "pending" status.
    /// Used to drive inbox badge counters in the UI without loading entities.
    /// </summary>
    Task<int> GetPendingCountAsync();

    // ── Single-item triage ───────────────────────────────────────────────────

    /// <summary>
    /// Accepts a single pending item, setting its status to "accepted" and
    /// <c>ProcessedAt</c> to the current UTC time. The indexing pipeline will
    /// subsequently pick up the item based on the accepted status.
    /// </summary>
    /// <param name="itemId">Primary key of the inbox item to accept.</param>
    /// <param name="collectionId">
    /// If provided, overrides the AI-suggested collection so the indexing
    /// pipeline places the document in the correct collection.
    /// </param>
    Task AcceptItemAsync(long itemId, long? collectionId = null);

    /// <summary>
    /// Accepts all items currently in "pending" status using the collection
    /// suggested by the AI triage (if any), then stamps each with the current
    /// UTC time as <c>ProcessedAt</c>.
    /// </summary>
    Task AcceptAllPendingAsync();

    /// <summary>
    /// Rejects a single pending item, setting its status to "rejected" and
    /// <c>ProcessedAt</c> to the current UTC time. The file is not deleted
    /// from disk; only the inbox record is updated.
    /// </summary>
    /// <param name="itemId">Primary key of the inbox item to reject.</param>
    Task RejectItemAsync(long itemId);

    /// <summary>
    /// Defers a single pending item, setting its status to "deferred" and
    /// <c>ProcessedAt</c> to the current UTC time. Deferred items remain
    /// visible for later review without blocking the inbox count.
    /// </summary>
    /// <param name="itemId">Primary key of the inbox item to defer.</param>
    Task DeferItemAsync(long itemId);

    // ── Batch triage ─────────────────────────────────────────────────────────

    /// <summary>
    /// Accepts a set of items by their primary keys. Each item is stamped
    /// "accepted" with <c>ProcessedAt = UtcNow</c>. Items not found or already
    /// processed are silently skipped.
    /// </summary>
    /// <param name="itemIds">IDs of the items to accept.</param>
    /// <param name="collectionId">
    /// Optional collection override applied to every item in the batch.
    /// When null each item retains its own <c>SuggestedCollectionId</c>.
    /// </param>
    Task AcceptSelectedAsync(IEnumerable<long> itemIds, long? collectionId = null);

    /// <summary>
    /// Rejects a set of items by their primary keys. Items not found or already
    /// processed are silently skipped.
    /// </summary>
    /// <param name="itemIds">IDs of the items to reject.</param>
    Task RejectSelectedAsync(IEnumerable<long> itemIds);

    // ── AI preview generation ────────────────────────────────────────────────

    /// <summary>
    /// Reads the first 2 000 characters of the file at <c>InboxItemEntity.FilePath</c>,
    /// sends them to the AI for a 2–3 sentence preview, and also requests a collection
    /// suggestion and comma-separated tags. Updates the entity in the database.
    /// </summary>
    /// <param name="itemId">Primary key of the inbox item to preview.</param>
    /// <param name="ct">Cancellation token.</param>
    Task GeneratePreviewAsync(long itemId, CancellationToken ct = default);

    /// <summary>
    /// Runs <see cref="GeneratePreviewAsync"/> for every pending item that does not yet
    /// have a preview. Items are processed sequentially to avoid saturating the AI
    /// provider. The operation is cancellable; already-completed items are unaffected.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task GenerateAllPreviewsAsync(CancellationToken ct = default);

    // ── Maintenance ──────────────────────────────────────────────────────────

    /// <summary>
    /// Permanently deletes all inbox rows whose status is "accepted", "rejected",
    /// or "deferred". Pending items are not touched. Does not affect files on disk.
    /// </summary>
    Task DeleteProcessedItemsAsync();

    // ── External (plugin-sourced) items ────────────────────────────────────────

    /// <summary>
    /// Adds an external item to the inbox from a DataConnector plugin (calendar, email, etc.).
    /// Unlike <see cref="AddToInboxAsync"/>, this does not require a physical file on disk.
    /// The item is auto-accepted and immediately available for indexing since external
    /// items are already processed by the plugin before submission.
    /// </summary>
    /// <param name="fileName">Display name for the item (e.g. "Meeting: Sprint Planning").</param>
    /// <param name="fileType">Category label (e.g. "CalendarEvent", "EmailMessage").</param>
    /// <param name="sourceType">Source type identifier (e.g. "calendar-connector", "email-connector").</param>
    /// <param name="sourceUrl">Link to the original item (e.g. Google Calendar web link).</param>
    /// <param name="sourcePluginId">Plugin ID that created this item (e.g. "com.agentx.calendar").</param>
    /// <param name="sourceCategory">Category within the plugin (e.g. "calendar_event", "ActionRequired").</param>
    /// <param name="externalId">Provider-specific ID for deduplication.</param>
    /// <param name="contentPreview">AI-generated or extracted content preview.</param>
    /// <param name="contentText">Full text content for indexing (will be stored as a temp file).</param>
    /// <returns>The created inbox item, already in "accepted" status.</returns>
    Task<InboxItemEntity> TriageExternalAsync(
        string fileName,
        string fileType,
        string sourceType,
        string? sourceUrl,
        string sourcePluginId,
        string? sourceCategory,
        string externalId,
        string? contentPreview,
        string contentText);
}
