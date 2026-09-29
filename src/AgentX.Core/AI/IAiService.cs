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
    /// The switch only happens when the provider is registered and reachable; the active model
    /// becomes that provider's configured default so provider and model always match.
    /// </summary>
    /// <param name="providerId">The provider identifier (e.g. "ollama").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the switch succeeded; false leaves the previous provider active.</returns>
    Task<bool> SwitchProviderAsync(string providerId, CancellationToken ct = default);

    /// <summary>
    /// Sets the active model for subsequent inference operations and persists the choice in
    /// the active provider's own model setting.
    /// </summary>
    /// <param name="modelId">The model identifier to activate.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetActiveModelAsync(string modelId, CancellationToken ct = default);

    /// <summary>The ids of the currently registered providers.</summary>
    IReadOnlyCollection<string> RegisteredProviderIds { get; }

    /// <summary>
    /// Returns the registered provider with the given id, or null when it is not registered
    /// (for example a cloud provider without an API key).
    /// </summary>
    IAiProvider? GetProvider(string providerId);

    /// <summary>
    /// Checks whether a registered provider is reachable. Recent results are reused for a short
    /// time, so routing does not re-probe (or bill) a provider on every message.
    /// </summary>
    Task<bool> IsProviderAvailableAsync(string providerId, CancellationToken ct = default);

    /// <summary>The model a provider uses when it is selected: its configured default.</summary>
    string GetDefaultModelId(string providerId);

    /// <summary>
    /// Resolves which provider and model produce embeddings. This is independent of the chat
    /// provider; see <see cref="EmbeddingTargetResolver"/>.
    /// </summary>
    EmbeddingTarget ResolveEmbeddingTarget();

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
