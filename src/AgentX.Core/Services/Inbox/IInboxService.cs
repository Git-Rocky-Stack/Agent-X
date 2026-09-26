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
    /// Accepts a single item into the knowledge vault. The file is copied into app storage
    /// (so a temp-folder clip cannot vanish under the vault), imported through
    /// <c>IDocumentService.ImportFileAsync</c> (which queues it for indexing), and the inbox
    /// row is linked to the resulting document and marked "accepted". When identical content
    /// is already in the vault the row is linked to that document instead of importing a
    /// second copy.
    /// </summary>
    /// <remarks>
    /// Failure is never reported as success: if the file is gone, its type cannot be
    /// processed, or document import is unavailable, the method throws and the row keeps
    /// its previous status.
    /// </remarks>
    /// <param name="itemId">Primary key of the inbox item to accept.</param>
    /// <param name="collectionId">
    /// If provided, overrides the AI-suggested collection for the imported document.
    /// </param>
    /// <returns>What the accept did and the linked document.</returns>
    Task<InboxAcceptResult> AcceptItemAsync(long itemId, long? collectionId = null);

    /// <summary>
    /// Accepts every item currently in "pending" status, each exactly as
    /// <see cref="AcceptItemAsync"/> does, using the item's own suggested collection.
    /// Items that fail stay pending and are reported in the result.
    /// </summary>
    Task<InboxBatchAcceptResult> AcceptAllPendingAsync();

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
    /// Accepts a set of items by their primary keys, each exactly as
    /// <see cref="AcceptItemAsync"/> does. Unknown IDs are skipped; items that fail keep
    /// their previous status and are reported in the result.
    /// </summary>
    /// <param name="itemIds">IDs of the items to accept.</param>
    /// <param name="collectionId">
    /// Optional collection override applied to every item in the batch.
    /// When null each item retains its own <c>SuggestedCollectionId</c>.
    /// </param>
    Task<InboxBatchAcceptResult> AcceptSelectedAsync(IEnumerable<long> itemIds, long? collectionId = null);

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
    /// Adds or refreshes an external item from a DataConnector plugin (calendar, email, etc.)
    /// and returns the row. Equivalent to <see cref="UpsertExternalAsync"/> without the
    /// outcome; callers that report added / updated / skipped counts should use that method.
    /// </summary>
    /// <param name="fileName">Display name for the item (e.g. "Meeting: Sprint Planning").</param>
    /// <param name="fileType">Category label (e.g. "CalendarEvent", "EmailMessage").</param>
    /// <param name="sourceType">Source type identifier (e.g. "calendar-connector", "email-connector").</param>
    /// <param name="sourceUrl">Link to the original item (e.g. Google Calendar web link).</param>
    /// <param name="sourcePluginId">Plugin ID that created this item (e.g. "com.agentx.calendar").</param>
    /// <param name="sourceCategory">Category within the plugin (e.g. "calendar_event", "ActionRequired").</param>
    /// <param name="externalId">Provider-specific ID for deduplication.</param>
    /// <param name="contentPreview">AI-generated or extracted content preview.</param>
    /// <param name="contentText">Full text content for indexing (stored under the app data folder).</param>
    /// <returns>The created or refreshed inbox item.</returns>
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

    /// <summary>
    /// Creates or refreshes the inbox row for an external provider item, keyed by
    /// (<paramref name="sourcePluginId"/>, <paramref name="externalId"/>).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>No row yet: the content is written under the app data folder (the file name is
    ///   a hash of the plugin and external IDs, so provider IDs such as
    ///   <c>google:primary:abc</c> never reach the file system), an "accepted" row is created,
    ///   and the content is imported into the vault. Outcome <see cref="ExternalTriageOutcome.Created"/>.</item>
    ///   <item>Row exists and the content or metadata changed (a rescheduled meeting, an
    ///   edited description): the content file and row are rewritten and, for an accepted row,
    ///   the linked vault document is re-indexed. Outcome <see cref="ExternalTriageOutcome.Updated"/>.</item>
    ///   <item>Row exists and nothing changed: nothing is written. Outcome
    ///   <see cref="ExternalTriageOutcome.Unchanged"/>.</item>
    /// </list>
    /// Vault import is best effort; a failure is logged and the inbox row is still returned.
    /// </remarks>
    Task<ExternalTriageResult> UpsertExternalAsync(
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
