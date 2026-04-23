namespace AgentX.Core.Services.Chat;

/// <summary>
/// Manages durable conversation summary state and refresh operations.
/// Summary generation is best-effort and never intended to block chat writes.
/// </summary>
public interface IConversationSummaryService
{
    /// <summary>
    /// Marks the conversation summary state as stale after a message mutation.
    /// When <paramref name="forceFullRefresh"/> is true, prior coverage is reset so
    /// the next refresh rebuilds from the full current transcript.
    /// </summary>
    Task MarkConversationStaleAsync(
        long conversationId,
        bool forceFullRefresh = false,
        CancellationToken ct = default);

    /// <summary>
    /// Refreshes the durable summary for one conversation if needed.
    /// Returns true when a new snapshot is created.
    /// </summary>
    Task<bool> RefreshConversationSummaryAsync(long conversationId, CancellationToken ct = default);

    /// <summary>
    /// Refreshes a bounded number of stale or unsummarized conversations.
    /// Returns the number of new snapshots created.
    /// </summary>
    Task<int> RefreshStaleSummariesAsync(int maxConversations = 4, CancellationToken ct = default);
}
