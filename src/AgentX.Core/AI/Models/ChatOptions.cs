namespace AgentX.Core.AI.Models;

/// <summary>
/// Configuration options for AI chat inference, controlling model behavior
/// such as temperature, token limits, and sampling parameters.
/// </summary>
public class ChatOptions
{
    /// <summary>
    /// The model identifier to use for this request. When null, the active model is used.
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// Controls randomness in the output. Higher values (e.g. 1.0) produce more creative
    /// responses, while lower values (e.g. 0.2) produce more deterministic responses.
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// Maximum number of tokens to generate in the response.
    /// </summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>
    /// Size of the context window used to generate the next token.
    /// </summary>
    public int ContextWindow { get; set; } = 4096;

    /// <summary>
    /// Nucleus sampling parameter. Controls the cumulative probability threshold
    /// for token selection. A value of 0.9 means the model considers tokens
    /// comprising the top 90% of probability mass.
    /// </summary>
    public double TopP { get; set; } = 0.9;

    /// <summary>
    /// Penalizes tokens based on their frequency in the generated text so far,
    /// reducing repetition of common phrases.
    /// </summary>
    public double FrequencyPenalty { get; set; }

    /// <summary>
    /// Penalizes tokens based on whether they have appeared in the generated text so far,
    /// encouraging the model to explore new topics.
    /// </summary>
    public double PresencePenalty { get; set; }

    /// <summary>
    /// Sequences that will cause the model to stop generating further tokens
    /// when encountered.
    /// </summary>
    public string[]? StopSequences { get; set; }
}
