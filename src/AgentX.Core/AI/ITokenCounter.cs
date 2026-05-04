namespace AgentX.Core.AI;

/// <summary>
/// Counts tokens in text using model-specific tokenization.
/// Provides accurate token counts for context window budgeting and chunk sizing.
/// </summary>
public interface ITokenCounter
{
    /// <summary>
    /// Counts the number of tokens in the given text for the specified model.
    /// </summary>
    /// <param name="text">The text to count tokens in.</param>
    /// <param name="modelId">Optional model identifier for specific tokenization.
    /// Defaults to the active model's tokenization if not specified.</param>
    /// <returns>The number of tokens in the text.</returns>
    int CountTokens(string text, string? modelId = null);

    /// <summary>
    /// Counts tokens in multiple texts efficiently.
    /// </summary>
    /// <param name="texts">The texts to count tokens in.</param>
    /// <param name="modelId">Optional model identifier.</param>
    /// <returns>Token counts for each text in the same order.</returns>
    IReadOnlyList<int> CountTokensBatch(IReadOnlyList<string> texts, string? modelId = null);

    /// <summary>
    /// Estimates the maximum number of tokens that can fit in the remaining context window.
    /// </summary>
    /// <param name="usedTokens">Current token usage.</param>
    /// <param name="modelId">Optional model identifier for context window size.</param>
    /// <returns>Remaining token capacity.</returns>
    int GetRemainingCapacity(int usedTokens, string? modelId = null);
}
