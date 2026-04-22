using AgentX.Core.Data.Entities;

namespace AgentX.Core.Services.Chat;

/// <summary>
/// Enhanced semantic memory service with embedding-based retrieval,
/// associative links, and temporal decay for human-like memory.
/// </summary>
public interface ISemanticMemoryService
{
    /// <summary>
    /// Retrieves memories most relevant to the query using semantic similarity.
    /// Results are ranked by effective importance (base importance × temporal decay).
    /// </summary>
    Task<IReadOnlyList<MemoryEntity>> RetrieveRelevantMemoriesAsync(
        string query,
        int maxMemories = 10,
        float minSimilarity = 0.7f,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves associative memories transitively (memory → linked memories).
    /// Expands retrieval scope by following associative links.
    /// </summary>
    Task<IReadOnlyList<MemoryEntity>> RetrieveAssociativeMemoriesAsync(
        long seedMemoryId,
        int maxDepth = 2,
        CancellationToken ct = default);

    /// <summary>
    /// Extracts and stores memories from a conversation with embedding generation.
    /// Automatically creates associative links between semantically similar memories.
    /// </summary>
    Task ExtractMemoriesAsync(long conversationId, CancellationToken ct = default);

    /// <summary>
    /// Creates an associative link between two memories with high semantic similarity.
    /// </summary>
    Task LinkMemoriesAsync(long memoryId1, long memoryId2, CancellationToken ct = default);

    /// <summary>
    /// Reinforces or corrects a memory based on user feedback.
    /// Positive feedback increases importance; negative feedback decreases it.
    /// </summary>
    Task ApplyFeedbackAsync(long memoryId, bool isPositive, CancellationToken ct = default);

    /// <summary>
    /// Gets the effective importance of a memory accounting for temporal decay.
    /// </summary>
    double GetEffectiveImportance(MemoryEntity memory);

    /// <summary>
    /// Gets active memories for display in UI.
    /// </summary>
    Task<IReadOnlyList<MemoryEntity>> GetAllMemoriesAsync(CancellationToken ct = default);

    /// <summary>
    /// Dismisses (soft-deletes) a memory.
    /// </summary>
    Task DismissMemoryAsync(long memoryId, CancellationToken ct = default);

    /// <summary>
    /// Gets count of active memories.
    /// </summary>
    Task<int> GetMemoryCountAsync(CancellationToken ct = default);
}
