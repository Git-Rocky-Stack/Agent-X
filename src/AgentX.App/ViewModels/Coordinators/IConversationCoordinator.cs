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
    /// Deletes a conversation by ID.
    /// </summary>
    Task DeleteConversationAsync(long conversationId);

    /// <summary>
    /// Loads all conversations from the service and returns them as summary objects.
    /// </summary>
    Task<IReadOnlyList<ConversationSummary>> LoadConversationsAsync();

    /// <summary>
    /// Toggles the pinned state of a conversation.
    /// </summary>
    Task TogglePinAsync(long conversationId);

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
