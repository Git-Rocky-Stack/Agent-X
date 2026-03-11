namespace AgentX.Core.Services.Chat;

using AgentX.Core.Data.Entities;

/// <summary>
/// Manages conversation and message persistence. Provides CRUD operations
/// for conversations and their associated messages via EF Core.
/// </summary>
public interface IConversationService
{
    /// <summary>
    /// Creates a new conversation with optional title, system prompt, and model.
    /// </summary>
    Task<ConversationEntity> CreateConversationAsync(
        string? title = null,
        string? systemPrompt = null,
        string? modelId = null);

    /// <summary>
    /// Retrieves a conversation by ID, including its messages.
    /// </summary>
    Task<ConversationEntity?> GetConversationAsync(long conversationId);

    /// <summary>
    /// Returns all conversations ordered by UpdatedAt descending.
    /// </summary>
    /// <param name="includeArchived">When false (default), archived conversations are excluded.</param>
    Task<IReadOnlyList<ConversationEntity>> GetAllConversationsAsync(bool includeArchived = false);

    /// <summary>
    /// Searches conversations by title or message content matching the query.
    /// </summary>
    Task<IReadOnlyList<ConversationEntity>> SearchConversationsAsync(string query);

    /// <summary>
    /// Updates the title of an existing conversation.
    /// </summary>
    Task UpdateConversationTitleAsync(long conversationId, string title);

    /// <summary>
    /// Toggles the pinned state of a conversation.
    /// </summary>
    Task TogglePinAsync(long conversationId);

    /// <summary>
    /// Archives a conversation, hiding it from the default list.
    /// </summary>
    Task ArchiveConversationAsync(long conversationId);

    /// <summary>
    /// Permanently deletes a conversation and all its messages.
    /// </summary>
    Task DeleteConversationAsync(long conversationId);

    /// <summary>
    /// Returns all messages for a conversation, ordered by SortOrder ascending.
    /// </summary>
    Task<IReadOnlyList<MessageEntity>> GetMessagesAsync(long conversationId);

    /// <summary>
    /// Adds a new message to a conversation and updates conversation metadata.
    /// </summary>
    /// <param name="conversationId">The target conversation.</param>
    /// <param name="role">Message role: "user", "assistant", or "system".</param>
    /// <param name="content">The message content.</param>
    /// <param name="tokenCount">Optional estimated token count for the message.</param>
    /// <param name="generationTimeMs">Optional generation time in milliseconds (for assistant messages).</param>
    Task AddMessageAsync(
        long conversationId,
        string role,
        string content,
        int? tokenCount = null,
        double? generationTimeMs = null);

    /// <summary>
    /// Removes the most recent assistant message from a conversation.
    /// Used by regeneration to replace the last response.
    /// </summary>
    Task DeleteLastAssistantMessageAsync(long conversationId);

    /// <summary>
    /// Deletes a specific message by ID and updates conversation metadata.
    /// </summary>
    Task DeleteMessageAsync(long messageId);

    /// <summary>
    /// Updates the content of an existing message. Used for message editing.
    /// </summary>
    Task UpdateMessageContentAsync(long messageId, string newContent);

    /// <summary>
    /// Deletes all messages in a conversation after a given SortOrder.
    /// Used to truncate the conversation when editing and re-generating.
    /// </summary>
    Task DeleteMessagesAfterAsync(long conversationId, int sortOrder);

    /// <summary>
    /// Returns the count of non-archived conversations.
    /// </summary>
    Task<int> GetConversationCountAsync();

    /// <summary>
    /// Returns the sum of TokensUsed across all conversations.
    /// </summary>
    Task<long> GetTotalTokensUsedAsync();
}
