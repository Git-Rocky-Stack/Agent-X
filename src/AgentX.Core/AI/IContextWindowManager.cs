using AgentX.Core.AI.Models;

namespace AgentX.Core.AI;

/// <summary>
/// Manages conversation context to fit within model token limits.
/// Implements sliding window compression to prevent silent failures
/// when conversations exceed the model's context window.
/// </summary>
public interface IContextWindowManager
{
    /// <summary>
    /// Trims conversation history to fit within the model's context window.
    /// Preserves the system prompt and most recent messages, removing
    /// older messages in FIFO order when the token budget is exceeded.
    /// </summary>
    /// <param name="messages">Full message history (system, user, assistant).</param>
    /// <param name="maxTokens">Maximum tokens allowed for the context window.</param>
    /// <param name="reserveForResponse">Tokens to reserve for the model's response (default 1024).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Trimmed message list that fits within the token budget.</returns>
    Task<List<ChatMessage>> FitToContextWindowAsync(
        List<ChatMessage> messages,
        int maxTokens,
        int reserveForResponse = 1024,
        CancellationToken ct = default);

    /// <summary>
    /// Estimates the token count for a single text string.
    /// Uses a conservative heuristic: ~4 characters per token for English text.
    /// </summary>
    /// <param name="text">The text to estimate tokens for.</param>
    /// <returns>Estimated token count.</returns>
    int EstimateTokenCount(string text);

    /// <summary>
    /// Estimates the total token count for a list of messages,
    /// including per-message overhead for role labels and formatting.
    /// </summary>
    /// <param name="messages">The messages to estimate tokens for.</param>
    /// <returns>Estimated total token count across all messages.</returns>
    int EstimateTokenCount(IEnumerable<ChatMessage> messages);

    /// <summary>
    /// Gets the effective context window size for a model.
    /// Returns a sensible default if the model doesn't report its context length,
    /// and caps unreasonable values to prevent resource exhaustion.
    /// </summary>
    /// <param name="reportedContextLength">The context length reported by the model (0 if unknown).</param>
    /// <returns>A validated context window size between 4096 and 131072 tokens.</returns>
    int GetEffectiveContextWindow(int reportedContextLength);
}
