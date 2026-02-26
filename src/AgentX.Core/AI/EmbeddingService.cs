using AgentX.Core.Services.Settings;
using Serilog;

namespace AgentX.Core.AI;

/// <summary>
/// Generates vector embeddings from text content by delegating to the active AI provider.
/// Supports single-text and batch embedding with configurable batch sizes to avoid
/// overwhelming the inference backend.
///
/// Default model: all-MiniLM-L6-v2 (384-dimensional output) via Ollama.
/// </summary>
public sealed class EmbeddingService : IEmbeddingService
{
    private readonly IAiService _aiService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger _logger;

    /// <summary>
    /// Maximum number of texts to embed in a single provider call.
    /// Keeps memory usage and request latency manageable.
    /// </summary>
    private const int BatchSize = 32;

    /// <summary>
    /// Default embedding dimensionality for all-MiniLM-L6-v2.
    /// </summary>
    private const int DefaultDimensions = 384;

    /// <summary>
    /// Default Ollama model name for embedding generation.
    /// </summary>
    private const string DefaultModelName = "all-minilm";

    private string? _cachedModelName;

    /// <inheritdoc />
    public int Dimensions => DefaultDimensions;

    /// <inheritdoc />
    public string ModelName => _cachedModelName ?? DefaultModelName;

    /// <summary>
    /// Creates a new EmbeddingService.
    /// </summary>
    /// <param name="aiService">The AI service providing access to the active inference provider.</param>
    /// <param name="settingsService">The settings service for reading the configured embedding model.</param>
    public EmbeddingService(IAiService aiService, ISettingsService settingsService)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = Log.ForContext<EmbeddingService>();
        _logger.Information("EmbeddingService created (dimensions={Dimensions})", DefaultDimensions);
    }

    /// <inheritdoc />
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text to embed cannot be null or empty.", nameof(text));

        EnsureProviderAvailable();

        var modelName = await GetModelNameAsync().ConfigureAwait(false);

        _logger.Debug("Generating embedding for text of length {Length} using model '{Model}'",
            text.Length, modelName);

        try
        {
            var embedding = await _aiService.ActiveProvider
                .GenerateEmbeddingAsync(text, modelName, ct)
                .ConfigureAwait(false);

            _logger.Debug("Embedding generated: {Dimensions} dimensions", embedding.Length);
            return embedding;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to generate embedding for text of length {Length}", text.Length);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IEnumerable<string> texts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        EnsureProviderAvailable();

        var textList = texts as IList<string> ?? texts.ToList();

        if (textList.Count == 0)
            return Array.Empty<float[]>();

        var modelName = await GetModelNameAsync().ConfigureAwait(false);

        _logger.Information(
            "Generating batch embeddings for {Count} texts using model '{Model}' (batch size: {BatchSize})",
            textList.Count, modelName, BatchSize);

        var allEmbeddings = new List<float[]>(textList.Count);
        var totalBatches = (int)Math.Ceiling((double)textList.Count / BatchSize);
        var batchIndex = 0;

        for (var offset = 0; offset < textList.Count; offset += BatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batchTexts = textList
                .Skip(offset)
                .Take(BatchSize)
                .ToList();

            batchIndex++;
            _logger.Debug("Processing batch {BatchIndex}/{TotalBatches} ({Count} texts)",
                batchIndex, totalBatches, batchTexts.Count);

            try
            {
                var batchEmbeddings = await _aiService.ActiveProvider
                    .GenerateEmbeddingsAsync(batchTexts, modelName, ct)
                    .ConfigureAwait(false);

                allEmbeddings.AddRange(batchEmbeddings);

                _logger.Debug("Batch {BatchIndex}/{TotalBatches} completed, {Remaining} texts remaining",
                    batchIndex, totalBatches, textList.Count - offset - batchTexts.Count);
            }
            catch (Exception ex)
            {
                _logger.Error(ex,
                    "Failed on batch {BatchIndex}/{TotalBatches} (offset={Offset}, count={Count})",
                    batchIndex, totalBatches, offset, batchTexts.Count);
                throw;
            }
        }

        _logger.Information("Batch embedding complete: {Count} embeddings generated", allEmbeddings.Count);
        return allEmbeddings.AsReadOnly();
    }

    // ── Private Helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Reads the embedding model name from settings, caching it for subsequent calls.
    /// Falls back to the default model name if settings cannot be read.
    /// </summary>
    private async Task<string> GetModelNameAsync()
    {
        if (_cachedModelName is not null)
            return _cachedModelName;

        try
        {
            var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);
            _cachedModelName = string.IsNullOrWhiteSpace(settings.EmbeddingModel)
                ? DefaultModelName
                : settings.EmbeddingModel;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to read embedding model from settings, using default '{Model}'",
                DefaultModelName);
            _cachedModelName = DefaultModelName;
        }

        _logger.Debug("Embedding model resolved to '{Model}'", _cachedModelName);
        return _cachedModelName;
    }

    /// <summary>
    /// Validates that the AI provider is initialized and available.
    /// Throws a clear, actionable exception if the provider cannot serve embedding requests.
    /// </summary>
    private void EnsureProviderAvailable()
    {
        if (!_aiService.IsConnected)
        {
            throw new InvalidOperationException(
                "Cannot generate embeddings: the AI provider is not connected. " +
                "Ensure Ollama is running and accessible, then call IAiService.InitializeAsync. " +
                "The embedding model (all-MiniLM-L6-v2) must also be pulled via 'ollama pull all-minilm'.");
        }
    }
}
