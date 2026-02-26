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
    /// Deletes the last assistant message and re-sends the last user message
    /// to generate a new response.
    /// </summary>
    /// <param name="conversationId">The conversation to regenerate in.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RegenerateLastResponseAsync(
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
