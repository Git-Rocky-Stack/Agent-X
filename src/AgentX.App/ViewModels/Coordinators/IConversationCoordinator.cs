namespace AgentX.App.ViewModels.Coordinators;

/// <summary>
/// Coordinates conversation management operations: CRUD, pinning, folder organization,
/// and search/filter. The coordinator owns the business logic; the ChatViewModel retains
/// UI state collections and subscribes to coordinator events for synchronization.
/// </summary>
public interface IConversationCoordinator
{
    /// <summary>
    /// Raised when the conversation list has changed and the ViewModel should refresh.
    /// </summary>
    event EventHandler? ConversationsChanged;

    /// <summary>
    /// Raised when folder names have changed and the ViewModel should refresh.
    /// </summary>
    event EventHandler? FolderNamesChanged;

    /// <summary>
    /// Creates a new conversation and returns its summary.
    /// The caller (ChatViewModel) is responsible for updating its own UI state.
    /// </summary>
    /// <param name="title">Conversation title (typically derived from the first message).</param>
    /// <param name="systemPrompt">Optional system prompt content.</param>
    /// <param name="modelId">The active AI model identifier.</param>
    /// <returns>The newly created conversation summary, or null on failure.</returns>
    Task<ConversationSummary?> CreateConversationAsync(string title, string? systemPrompt, string? modelId);

    /// <summary>
    /// Deletes a conversation by ID. Its branches are kept and promoted in its place.
    /// </summary>
    /// <returns>True when the conversation was deleted; false when the delete failed.</returns>
    Task<bool> DeleteConversationAsync(long conversationId);

    /// <summary>
    /// Loads one conversation's summary by ID, whether or not it is in the loaded list.
    /// </summary>
    /// <returns>The summary, or null when the conversation does not exist or cannot be read.</returns>
    Task<ConversationSummary?> LoadConversationSummaryAsync(long conversationId);

    /// <summary>
    /// Loads all conversations from the service and returns them as summary objects.
    /// </summary>
    Task<IReadOnlyList<ConversationSummary>> LoadConversationsAsync();

    /// <summary>
    /// Toggles the pinned state of a conversation.
    /// </summary>
    /// <returns>True when the pinned state was changed; false when the update failed.</returns>
    Task<bool> TogglePinAsync(long conversationId);

    /// <summary>
    /// Sets the folder for a conversation.
    /// </summary>
    Task SetConversationFolderAsync(long conversationId, string? folder);

    /// <summary>
    /// Loads conversations filtered by folder.
    /// </summary>
    Task<IReadOnlyList<ConversationSummary>> LoadConversationsByFolderAsync(string folder);

    /// <summary>
    /// Loads all folder names in use.
    /// </summary>
    Task<IReadOnlyList<string>> LoadFolderNamesAsync();

    /// <summary>
    /// Searches conversations by query string.
    /// </summary>
    Task<IReadOnlyList<ConversationSummary>> SearchConversationsAsync(string query);

    /// <summary>
    /// Updates the content of an existing message.
    /// </summary>
    Task UpdateMessageContentAsync(long messageId, string newContent);

    /// <summary>
    /// Deletes all messages in a conversation after the specified sort order.
    /// </summary>
    Task DeleteMessagesAfterAsync(long conversationId, int sortOrder);

    /// <summary>
    /// Deletes a persisted message and every message after it. Used before an edited prompt
    /// is resent, since the resend persists the new text as a fresh message.
    /// </summary>
    /// <returns>True when the messages were deleted; false when the id is not persisted in
    /// the conversation or the delete failed, in which case nothing was removed.</returns>
    Task<bool> DeleteMessageAndFollowingAsync(long conversationId, long messageId);

    /// <summary>
    /// Loads messages for a specific conversation, including feedback ratings
    /// for assistant messages. Returns coordinator-level DTOs (not UI items).
    /// </summary>
    Task<IReadOnlyList<MessageSummary>> LoadMessagesAsync(long conversationId);
}

/// <summary>
/// Lightweight summary of a conversation for sidebar display.
/// Decoupled from entity types so the coordinator doesn't leak DB concerns.
/// </summary>
public sealed class ConversationSummary
{
    public long Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string LastMessage { get; init; } = string.Empty;
    public DateTime UpdatedAt { get; init; }
    public bool IsPinned { get; init; }
    public int MessageCount { get; init; }
    public string? FolderName { get; init; }
}

/// <summary>
/// Lightweight summary of a message for coordinator-to-ViewModel transfer.
/// The ViewModel maps these into ChatMessageItem instances for UI binding.
/// </summary>
public sealed class MessageSummary
{
    public long MessageId { get; init; }
    public long ConversationId { get; init; }
    public int SortOrder { get; init; }
    public string Role { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public int TokenCount { get; init; }
    public double GenerationTimeMs { get; init; }
    public string FeedbackRating { get; init; } = "none";
}
