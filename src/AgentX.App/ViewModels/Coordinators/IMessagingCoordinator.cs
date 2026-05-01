using AgentX.Core.Services.Chat.Models;

namespace AgentX.App.ViewModels.Coordinators;

/// <summary>
/// Coordinates message sending, streaming, generation control, feedback, and editing.
/// The coordinator owns the business logic; the ChatViewModel subscribes to events for
/// UI state synchronization.
/// </summary>
public interface IMessagingCoordinator
{
    /// <summary>
    /// Raised when a new token is received during streaming. The argument is the token string.
    /// </summary>
    event EventHandler<string>? TokenReceived;

    /// <summary>
    /// Raised when streaming is complete. The argument is the full response content.
    /// </summary>
    event EventHandler<StreamingCompletedEventArgs>? StreamingCompleted;

    /// <summary>
    /// Raised when an error occurs during generation.
    /// </summary>
    event EventHandler<string>? GenerationError;

    /// <summary>
    /// Raised when a notification should be shown to the user.
    /// The event argument contains the notification message.
    /// </summary>
    event EventHandler<NotificationRequestEventArgs>? NotificationRequested;

    /// <summary>
    /// Whether a generation is currently in progress.
    /// </summary>
    bool IsGenerating { get; }

    /// <summary>
    /// Sends a user message and streams the AI response. Handles conversation creation
    /// if needed, persists messages, and streams tokens back via events.
    /// </summary>
    /// <param name="userContent">The user's message content.</param>
    /// <param name="conversationId">The active conversation ID (null creates a new one).</param>
    /// <param name="systemPrompt">The active system prompt (used for new conversations).</param>
    /// <param name="modelId">The active model ID (used for new conversations).</param>
    /// <param name="isResearchMode">Whether research mode is enabled.</param>
    /// <returns>The conversation ID after sending (may be newly created).</returns>
    Task<SendMessageResult> SendMessageAsync(
        string userContent,
        long? conversationId,
        string? systemPrompt,
        string? modelId,
        bool isResearchMode);

    /// <summary>
    /// Stops the current generation.
    /// </summary>
    Task StopGenerationAsync();

    /// <summary>
    /// Submits feedback (thumbs up/down) for a message.
    /// </summary>
    Task SubmitFeedbackAsync(long messageId, long conversationId, string rating);

    /// <summary>
    /// Deletes a message from the conversation and database.
    /// </summary>
    Task DeleteMessageAsync(long messageId);
}

/// <summary>
/// Result of a SendMessage operation.
/// </summary>
public sealed class SendMessageResult
{
    /// <summary>The conversation ID (may be newly created).</summary>
    public long? ConversationId { get; init; }

    /// <summary>The full assistant response content.</summary>
    public string ResponseContent { get; init; } = string.Empty;

    /// <summary>Token count of the response.</summary>
    public int TokenCount { get; init; }

    /// <summary>Generation time in milliseconds.</summary>
    public double GenerationTimeMs { get; init; }

    /// <summary>Whether generation was cancelled.</summary>
    public bool WasCancelled { get; init; }

    /// <summary>Whether an error occurred.</summary>
    public bool HadError { get; init; }

    /// <summary>Error message if <see cref="HadError"/> is true.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>The conversation title (may be new/updated).</summary>
    public string? ConversationTitle { get; init; }

    /// <summary>The latest context inspection snapshot captured during this send path.</summary>
    public ChatContextInspectionSnapshot? ContextInspection { get; init; }

    /// <summary>The persisted assistant message ID when one was created.</summary>
    public long? AssistantMessageId { get; init; }

    /// <summary>The persisted user message ID when one was created.</summary>
    public long? UserMessageId { get; init; }
}

/// <summary>
/// Event args for streaming completion.
/// </summary>
public sealed class StreamingCompletedEventArgs : EventArgs
{
    public long? ConversationId { get; init; }
    public string ResponseContent { get; init; } = string.Empty;
    public int TokenCount { get; init; }
    public double GenerationTimeMs { get; init; }
    public string? ConversationTitle { get; init; }
    public ChatContextInspectionSnapshot? ContextInspection { get; init; }
    public long? AssistantMessageId { get; init; }
    public long? UserMessageId { get; init; }
}

/// <summary>
/// Event args for notification requests (decoupled from INotificationService).
/// </summary>
public sealed class NotificationRequestEventArgs : EventArgs
{
    public string Level { get; init; } = "info";
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
