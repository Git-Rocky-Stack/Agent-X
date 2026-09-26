using AgentX.Core.Data.Entities;

namespace AgentX.Core.Services.Inbox;

/// <summary>
/// What <see cref="IInboxService.UpsertExternalAsync"/> did with an external item.
/// Sync services map these to their added / updated / skipped counters, so the counts
/// come from the inbox itself rather than from comparing timestamps.
/// </summary>
public enum ExternalTriageOutcome
{
    /// <summary>No row existed for the provider item; a new accepted row was created.</summary>
    Created,

    /// <summary>A row existed and its content or metadata changed; the row (and vault copy) was refreshed.</summary>
    Updated,

    /// <summary>A row existed and nothing changed; nothing was written.</summary>
    Unchanged,
}

/// <summary>
/// Result of <see cref="IInboxService.UpsertExternalAsync"/>: the inbox row plus an
/// explicit statement of whether it was created, updated, or left unchanged.
/// </summary>
/// <param name="Item">The created or pre-existing inbox row.</param>
/// <param name="Outcome">What the call did.</param>
public sealed record ExternalTriageResult(InboxItemEntity Item, ExternalTriageOutcome Outcome);

/// <summary>
/// What accepting a single inbox item did.
/// </summary>
public enum InboxAcceptOutcome
{
    /// <summary>The file was copied into app storage and imported into the knowledge vault.</summary>
    Imported,

    /// <summary>
    /// Identical content was already in the vault; the item was linked to that document
    /// instead of importing a second copy.
    /// </summary>
    AlreadyInVault,

    /// <summary>The item had already been accepted and linked to a document; nothing changed.</summary>
    AlreadyAccepted,
}

/// <summary>
/// Result of accepting one inbox item.
/// </summary>
/// <param name="ItemId">Primary key of the inbox item.</param>
/// <param name="Outcome">What the accept did.</param>
/// <param name="DocumentId">The vault document the item is now linked to, when known.</param>
public sealed record InboxAcceptResult(long ItemId, InboxAcceptOutcome Outcome, long? DocumentId);

/// <summary>
/// Result of a batch accept. Items that fail are left in their previous status so they can
/// be retried or rejected; <see cref="Errors"/> carries one line per failure.
/// </summary>
/// <param name="Imported">Items imported into the vault.</param>
/// <param name="AlreadyInVault">Items linked to an existing identical document.</param>
/// <param name="AlreadyAccepted">Items that were already accepted and linked.</param>
/// <param name="Failed">Items that could not be accepted.</param>
/// <param name="Errors">Human-readable reason per failed item.</param>
public sealed record InboxBatchAcceptResult(
    int Imported,
    int AlreadyInVault,
    int AlreadyAccepted,
    int Failed,
    IReadOnlyList<string> Errors)
{
    /// <summary>An empty result for a batch with nothing to accept.</summary>
    public static InboxBatchAcceptResult Empty { get; } = new(0, 0, 0, 0, Array.Empty<string>());

    /// <summary>Items that ended the batch accepted, whether imported or linked.</summary>
    public int Accepted => Imported + AlreadyInVault + AlreadyAccepted;
}
