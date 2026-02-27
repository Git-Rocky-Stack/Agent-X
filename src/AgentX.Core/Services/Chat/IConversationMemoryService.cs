using AgentX.Core.Data.Entities;

namespace AgentX.Core.Services.Chat;

/// <summary>
/// Manages persistent conversation memory: extracts facts and preferences
/// from conversations, injects relevant context into system prompts, and
/// generates suggested follow-up questions based on prior interactions.
/// </summary>
public interface IConversationMemoryService
{
    /// <summary>Extract and store memories from recent conversation messages.</summary>
    Task ExtractMemoriesAsync(long conversationId, CancellationToken ct = default);

    /// <summary>Get active memories formatted as a context block for system prompts.</summary>
    Task<string> GetMemoryContextAsync(int maxMemories = 10, CancellationToken ct = default);

    /// <summary>Generate suggested follow-up questions based on conversation + memories.</summary>
    Task<IReadOnlyList<string>> GetSuggestedQuestionsAsync(long conversationId, CancellationToken ct = default);

    /// <summary>Get all active memories for display in UI.</summary>
    Task<IReadOnlyList<MemoryEntity>> GetAllMemoriesAsync(CancellationToken ct = default);

    /// <summary>Dismiss (soft-delete) a memory.</summary>
    Task DismissMemoryAsync(long memoryId, CancellationToken ct = default);

    /// <summary>Get count of active memories.</summary>
    Task<int> GetMemoryCountAsync(CancellationToken ct = default);
}
