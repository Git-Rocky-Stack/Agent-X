using AgentX.Core.AI.Models;

namespace AgentX.Core.AI;

/// <summary>
/// High-level AI service that orchestrates provider selection and provides
/// the primary interface for all AI operations. Wraps the active IAiProvider
/// and adds application-specific capabilities such as summarization and tagging.
/// </summary>
public interface IAiService : IDisposable
{
    /// <summary>
    /// The currently active AI provider instance.
    /// </summary>
    IAiProvider ActiveProvider { get; }

    /// <summary>
    /// Indicates whether the active provider is connected and operational.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// The model identifier currently selected for inference.
    /// </summary>
    string ActiveModelId { get; }

    /// <summary>
    /// Initializes the AI service, creating providers and establishing
    /// the initial connection based on persisted settings.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Switches the active provider to the one identified by <paramref name="providerId"/>.
    /// </summary>
    /// <param name="providerId">The provider identifier (e.g. "ollama").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the switch succeeded and the new provider is connected.</returns>
    Task<bool> SwitchProviderAsync(string providerId, CancellationToken ct = default);

    /// <summary>
    /// Sets the active model for subsequent inference operations and persists the choice.
    /// </summary>
    /// <param name="modelId">The model identifier to activate.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetActiveModelAsync(string modelId, CancellationToken ct = default);

    /// <summary>
    /// Streams a chat completion token-by-token. Optionally prepends a system prompt
    /// to the conversation history.
    /// </summary>
    /// <param name="messages">The conversation message history.</param>
    /// <param name="systemPrompt">Optional system prompt to prepend.</param>
    /// <param name="options">Optional inference parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async enumerable of generated text tokens.</returns>
    IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt = null,
        ChatOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a complete chat response. Optionally prepends a system prompt
    /// to the conversation history.
    /// </summary>
    /// <param name="messages">The conversation message history.</param>
    /// <param name="systemPrompt">Optional system prompt to prepend.</param>
    /// <param name="options">Optional inference parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The full generated response text.</returns>
    Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt = null,
        ChatOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a concise summary of the provided content using the active model.
    /// </summary>
    /// <param name="content">The text content to summarize.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A summary of the content.</returns>
    Task<string> SummarizeAsync(string content, CancellationToken ct = default);

    /// <summary>
    /// Generates descriptive tags for the provided content using the active model.
    /// </summary>
    /// <param name="content">The text content to generate tags for.</param>
    /// <param name="maxTags">Maximum number of tags to generate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of generated tags.</returns>
    Task<IReadOnlyList<string>> GenerateTagsAsync(string content, int maxTags = 5, CancellationToken ct = default);
}
