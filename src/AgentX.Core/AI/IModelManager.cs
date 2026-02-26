using AgentX.Core.AI.Models;

namespace AgentX.Core.AI;

/// <summary>
/// Manages locally available AI models -- listing, downloading, deleting,
/// and querying model information. Delegates to the active IAiProvider
/// and provides caching and change notification.
/// </summary>
public interface IModelManager
{
    /// <summary>
    /// Gets all models available from the remote registry for the active provider.
    /// For Ollama, this returns the same as installed models since
    /// there is no separate "available" vs "installed" distinction locally.
    /// </summary>
    Task<IReadOnlyList<AiModel>> GetAvailableModelsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all models currently installed on the local system.
    /// </summary>
    Task<IReadOnlyList<AiModel>> GetInstalledModelsAsync(CancellationToken ct = default);

    /// <summary>
    /// Downloads/pulls a model from the provider's registry.
    /// </summary>
    /// <param name="modelName">The model name/tag to pull (e.g. "llama3.2:latest").</param>
    /// <param name="progress">Optional progress reporter for download status.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PullModelAsync(string modelName, IProgress<ModelDownloadProgress>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Deletes a locally installed model.
    /// </summary>
    /// <param name="modelName">The model name/tag to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteModelAsync(string modelName, CancellationToken ct = default);

    /// <summary>
    /// Retrieves detailed information for a specific model by name.
    /// </summary>
    /// <param name="modelName">The model name/tag to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The model info, or null if not found.</returns>
    Task<AiModel?> GetModelInfoAsync(string modelName, CancellationToken ct = default);

    /// <summary>
    /// Checks whether a specific model is currently installed and available locally.
    /// </summary>
    /// <param name="modelName">The model name/tag to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the model is installed locally.</returns>
    Task<bool> IsModelAvailableAsync(string modelName, CancellationToken ct = default);

    /// <summary>
    /// Raised when the local model list changes (after a pull or delete operation).
    /// </summary>
    event EventHandler<AiModel>? ModelListChanged;
}
