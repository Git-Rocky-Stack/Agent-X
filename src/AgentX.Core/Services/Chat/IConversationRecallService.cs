using AgentX.Core.Services.Chat.Models;

namespace AgentX.Core.Services.Chat;

/// <summary>
/// Persists message embeddings and performs bounded semantic recall over
/// historical conversation messages.
/// </summary>
public interface IConversationRecallService
{
    /// <summary>
    /// Refreshes the embedding for a single message when the message is eligible
    /// for recall. Returns true when an embedding was created or replaced.
    /// </summary>
    Task<bool> RefreshMessageEmbeddingAsync(
        long messageId,
        bool forceRefresh = false,
        CancellationToken ct = default);

    /// <summary>
    /// Refreshes missing or stale embeddings for one conversation. Returns the
    /// number of messages whose embeddings were created or replaced.
    /// </summary>
    Task<int> RefreshConversationEmbeddingsAsync(
        long conversationId,
        bool forceRefresh = false,
        CancellationToken ct = default);

    /// <summary>
    /// Backfills embeddings for a bounded set of recent conversations that still
    /// contain recall-eligible messages without embeddings.
    /// </summary>
    Task<int> RefreshRecentConversationEmbeddingsAsync(
        int maxConversations = 4,
        CancellationToken ct = default);

    /// <summary>
    /// Searches persisted message embeddings for semantically relevant past
    /// messages. Results are ordered by descending similarity.
    /// </summary>
    Task<IReadOnlyList<ConversationRecallResult>> SearchRelevantMessagesAsync(
        string query,
        int maxResults = 6,
        float minSimilarity = 0.65f,
        long? excludeConversationId = null,
        CancellationToken ct = default);
}
