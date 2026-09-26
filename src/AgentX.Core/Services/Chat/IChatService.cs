using AgentX.Core.Services.Chat.Models;

namespace AgentX.Core.Services.Chat;

/// <summary>
/// Orchestrates AI chat operations: sends messages, streams responses,
/// manages generation state, and coordinates persistence via IConversationService.
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Sends a user message and streams the assistant response token-by-token.
    /// The user message and final assistant response are persisted automatically.
    /// </summary>
    /// <param name="conversationId">The conversation to send the message in.</param>
    /// <param name="userMessage">The user's message content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async enumerable of response tokens as they arrive.</returns>
    IAsyncEnumerable<string> SendMessageAsync(
        long conversationId,
        string userMessage,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a user message like <see cref="SendMessageAsync(long, string, CancellationToken)"/>,
    /// adding <paramref name="supplementalContext"/> (for example cited web search results) to the
    /// context assembled for this reply only. The supplemental context is not persisted.
    /// </summary>
    IAsyncEnumerable<string> SendMessageAsync(
        long conversationId,
        string userMessage,
        string? supplementalContext,
        CancellationToken ct);

    /// <summary>
    /// Streams a new answer to an existing user message without persisting that message again.
    /// The message must close the conversation, optionally followed by its current answer; that
    /// answer is replaced only once the new one has been saved, so stopping or failing keeps it.
    /// </summary>
    /// <param name="conversationId">The conversation to regenerate in.</param>
    /// <param name="userMessageId">The persisted user message to answer again.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// The message is not a user message of the conversation, or later messages follow its answer.
    /// </exception>
    IAsyncEnumerable<string> RegenerateResponseAsync(
        long conversationId,
        long userMessageId,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a user message and waits for the complete assistant response.
    /// The user message and assistant response are persisted automatically.
    /// </summary>
    /// <param name="conversationId">The conversation to send the message in.</param>
    /// <param name="userMessage">The user's message content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The complete assistant response.</returns>
    Task<string> SendMessageAndWaitAsync(
        long conversationId,
        string userMessage,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the latest in-memory context inspection snapshot captured for the
    /// specified conversation, or null when none exists.
    /// </summary>
    ChatContextInspectionSnapshot? GetLatestContextInspection(long conversationId);

    /// <summary>
    /// Refreshes the durable summary for one conversation and updates the latest
    /// cached inspection snapshot when possible.
    /// </summary>
    Task<ConversationSummaryRefreshResult> RefreshConversationSummaryInspectionAsync(
        long conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels any in-progress generation.
    /// </summary>
    Task StopGenerationAsync();

    /// <summary>
    /// Indicates whether an AI response is currently being generated.
    /// </summary>
    bool IsGenerating { get; }

    /// <summary>
    /// Fires when <see cref="IsGenerating"/> changes. The event argument is the new value.
    /// </summary>
    event EventHandler<bool>? GenerationStateChanged;
}
