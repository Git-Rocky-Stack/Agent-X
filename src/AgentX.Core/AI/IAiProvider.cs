using AgentX.Core.AI.Models;

namespace AgentX.Core.AI;

/// <summary>
/// Low-level abstraction over AI inference providers (Ollama, LLamaSharp, etc.).
/// Each provider implementation wraps a specific backend and exposes a unified
/// interface for model management, chat inference, and embedding generation.
/// </summary>
public interface IAiProvider : IDisposable
{
    /// <summary>
    /// Unique identifier for this provider (e.g. "ollama", "llamasharp").
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Human-readable display name for the provider (e.g. "Ollama", "LLamaSharp").
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Indicates whether the provider is currently reachable and operational.
    /// Updated by <see cref="CheckConnectionAsync"/>.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Tests the connection to the AI provider backend.
    /// </summary>
    /// <returns>True if the provider is reachable and operational.</returns>
    Task<bool> CheckConnectionAsync(CancellationToken ct = default);

    /// <summary>
    /// Lists all models currently available (installed) on this provider.
    /// </summary>
    Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default);

    /// <summary>
    /// Downloads/pulls a model from the provider's model registry.
    /// </summary>
    /// <param name="modelName">The name/tag of the model to pull (e.g. "llama3.2:latest").</param>
    /// <param name="progress">Optional progress reporter for download status updates.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PullModelAsync(string modelName, IProgress<ModelDownloadProgress>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Deletes a locally installed model from the provider.
    /// </summary>
    /// <param name="modelName">The name/tag of the model to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteModelAsync(string modelName, CancellationToken ct = default);

    /// <summary>
    /// Streams a chat completion token-by-token for the given conversation history.
    /// </summary>
    /// <param name="messages">The conversation message history.</param>
    /// <param name="options">Optional inference parameters (temperature, max tokens, etc.).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async enumerable of generated text tokens.</returns>
    IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a complete chat response for the given conversation history.
    /// </summary>
    /// <param name="messages">The conversation message history.</param>
    /// <param name="options">Optional inference parameters (temperature, max tokens, etc.).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The full generated response text.</returns>
    Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a vector embedding for a single text input.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="modelName">The embedding model to use.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The embedding vector as a float array.</returns>
    Task<float[]> GenerateEmbeddingAsync(
        string text,
        string modelName,
        CancellationToken ct = default);

    /// <summary>
    /// Generates vector embeddings for multiple text inputs in a batch.
    /// </summary>
    /// <param name="texts">The texts to embed.</param>
    /// <param name="modelName">The embedding model to use.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of embedding vectors, one per input text.</returns>
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        string modelName,
        CancellationToken ct = default);
}
