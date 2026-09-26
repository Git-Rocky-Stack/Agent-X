using AgentX.Core.AI.Models;
using Serilog;

namespace AgentX.Core.AI;

/// <summary>
/// Manages local AI model lifecycle by delegating to the active IAiProvider.
/// Provides model list caching with configurable expiration and fires
/// change notifications after pull/delete operations.
/// </summary>
public sealed class ModelManager : IModelManager
{
    private readonly IAiService _aiService;
    private readonly ILogger _logger;

    // The list belongs to the provider that produced it: after a provider switch (or an AI
    // re-initialization that replaced the provider) the cached list of the previous provider
    // must not be shown or offered for selection.
    private sealed record ModelListCache(IAiProvider Provider, IReadOnlyList<AiModel> Models, DateTime ExpiresUtc);

    private volatile ModelListCache? _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public event EventHandler<AiModel>? ModelListChanged;

    /// <summary>
    /// Creates a new ModelManager instance.
    /// </summary>
    /// <param name="aiService">The AI service providing access to the active provider.</param>
    public ModelManager(IAiService aiService)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _logger = Log.ForContext<ModelManager>();
        _logger.Information("ModelManager created");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiModel>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        // For Ollama, "available" and "installed" are the same (local models).
        // A future implementation could query a registry for all pullable models.
        return await GetInstalledModelsAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiModel>> GetInstalledModelsAsync(CancellationToken ct = default)
    {
        var provider = _aiService.ActiveProvider;
        var cache = _cache;
        if (cache is not null && ReferenceEquals(cache.Provider, provider) && DateTime.UtcNow < cache.ExpiresUtc)
        {
            _logger.Debug("Returning cached model list ({Count} models)", cache.Models.Count);
            return cache.Models;
        }

        try
        {
            _logger.Debug("Fetching installed models from active provider {Provider}...", provider.ProviderId);
            var models = await provider.ListModelsAsync(ct).ConfigureAwait(false);

            _cache = new ModelListCache(provider, models, DateTime.UtcNow + CacheDuration);

            _logger.Information("Cached {Count} installed models (expires in {Seconds}s)",
                models.Count, CacheDuration.TotalSeconds);

            return models;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch installed models");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task PullModelAsync(
        string modelName,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("Model name cannot be null or empty.", nameof(modelName));

        try
        {
            _logger.Information("Pulling model via ModelManager: {ModelName}", modelName);
            var provider = _aiService.ActiveProvider;
            await provider.PullModelAsync(modelName, progress, ct).ConfigureAwait(false);

            // Invalidate cache after successful pull
            InvalidateCache();

            // Fire change event with a basic model object
            var pulledModel = new AiModel { Id = modelName, Name = modelName };
            OnModelListChanged(pulledModel);

            _logger.Information("Model pull completed: {ModelName}", modelName);
        }
        catch (OperationCanceledException)
        {
            _logger.Information("Model pull cancelled: {ModelName}", modelName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Model pull failed: {ModelName}", modelName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteModelAsync(string modelName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("Model name cannot be null or empty.", nameof(modelName));

        try
        {
            _logger.Information("Deleting model via ModelManager: {ModelName}", modelName);
            var provider = _aiService.ActiveProvider;
            await provider.DeleteModelAsync(modelName, ct).ConfigureAwait(false);

            // Invalidate cache after successful delete
            InvalidateCache();

            // Fire change event
            var deletedModel = new AiModel { Id = modelName, Name = modelName };
            OnModelListChanged(deletedModel);

            _logger.Information("Model deleted: {ModelName}", modelName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Model deletion failed: {ModelName}", modelName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<AiModel?> GetModelInfoAsync(string modelName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            return null;

        try
        {
            var models = await GetInstalledModelsAsync(ct).ConfigureAwait(false);
            var model = models.FirstOrDefault(m =>
                string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.Id, modelName, StringComparison.OrdinalIgnoreCase));

            if (model is null)
            {
                _logger.Debug("Model not found: {ModelName}", modelName);
            }

            return model;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to get model info: {ModelName}", modelName);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsModelAvailableAsync(string modelName, CancellationToken ct = default)
    {
        var model = await GetModelInfoAsync(modelName, ct).ConfigureAwait(false);
        return model is not null;
    }

    // ── Private Helpers ─────────────────────────────────────────────

    private void InvalidateCache()
    {
        _cache = null;
        _logger.Debug("Model cache invalidated");
    }

    private void OnModelListChanged(AiModel model)
    {
        try
        {
            ModelListChanged?.Invoke(this, model);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error in ModelListChanged event handler");
        }
    }
}
