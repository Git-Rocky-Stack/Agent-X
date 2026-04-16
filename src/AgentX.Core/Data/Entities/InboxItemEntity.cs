using System.ComponentModel.DataAnnotations;

namespace AgentX.Core.Data.Entities;

/// <summary>
/// Represents a file that has landed in the Smart Inbox after being detected by a watch
/// folder. Items remain in the inbox until a user (or automated rule) accepts, rejects,
/// or defers them. Accepted items are then picked up by the normal indexing pipeline.
/// </summary>
public class InboxItemEntity
{
    /// <summary>Primary key — auto-incremented by SQLite.</summary>
    public long Id { get; set; }

    /// <summary>Absolute path to the file on disk.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>File name without directory path (e.g. "report.pdf").</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// High-level file category returned by <c>FileTypeHelper.GetFileCategory</c>
    /// (e.g. "PDF", "Document", "Text", "Markdown", "Image", "Code").
    /// </summary>
    public string FileType { get; set; } = string.Empty;

    /// <summary>Size of the file in bytes at the time it was added to the inbox.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// Triage decision for this item.
    /// Valid values: "pending", "accepted", "rejected", "deferred".
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// AI-generated 2–3 sentence preview of the file's content.
    /// Null until <c>IInboxService.GeneratePreviewAsync</c> has been called for this item.
    /// </summary>
    public string? Preview { get; set; }

    /// <summary>
    /// The collection the AI suggests this file should be indexed into.
    /// Null when no suggestion has been generated or no suitable collection was found.
    /// </summary>
    public long? SuggestedCollectionId { get; set; }

    /// <summary>
    /// Denormalized name of <see cref="SuggestedCollectionId"/> for display without a join.
    /// </summary>
    public string? SuggestedCollectionName { get; set; }

    /// <summary>
    /// Comma-separated list of AI-suggested tags (e.g. "finance,quarterly,report").
    /// Null until preview generation has run.
    /// </summary>
    public string? SuggestedTags { get; set; }

    /// <summary>UTC timestamp when the file was first added to the inbox.</summary>
    public DateTime AddedAt { get; set; }

    /// <summary>
    /// UTC timestamp when the item was last processed (accepted/rejected/deferred).
    /// Null while the item is still pending.
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Foreign key to the <see cref="WatchFolderEntity"/> that triggered discovery of
    /// this file. Null for items added to the inbox programmatically.
    /// </summary>
    public long? WatchFolderId { get; set; }

    /// <summary>
    /// Identifies how the item entered the inbox.
    /// Valid values: "file-watcher", "browser-extension", "manual".
    /// Null for items created before this field was introduced.
    /// </summary>
    public string? SourceType { get; set; }

    /// <summary>
    /// Original URL for items clipped from the web via the browser extension.
    /// Null for file-watcher or manually-added items.
    /// </summary>
    public string? SourceUrl { get; set; }

    /// <summary>
    /// Identifies the plugin that created this inbox item (e.g. <c>"com.agentx.calendar"</c>,
    /// <c>"com.agentx.email"</c>). Null for file-watcher or manually-added items.
    /// </summary>
    [MaxLength(50)]
    public string? SourcePluginId { get; set; }

    /// <summary>
    /// Category or source type within the plugin (e.g. <c>"calendar_event"</c>,
    /// <c>"ActionRequired"</c>). Null for non-plugin items.
    /// </summary>
    [MaxLength(50)]
    public string? SourceCategory { get; set; }

    /// <summary>
    /// External ID from the provider (e.g. Google event ID, Gmail message ID).
    /// Used for deduplication — if an item with the same ExternalId and SourcePluginId
    /// already exists in the inbox, the duplicate is skipped.
    /// Null for file-watcher or manually-added items.
    /// </summary>
    [MaxLength(500)]
    public string? ExternalId { get; set; }

    /// <summary>
    /// Foreign key to the <see cref="DocumentEntity"/> created when this inbox item
    /// was bridged into the document library for search indexing.
    /// Null until the bridge completes (or if IDocumentService is unavailable).
    /// </summary>
    public long? DocumentId { get; set; }
}
