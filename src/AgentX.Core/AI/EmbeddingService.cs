using AgentX.Core.Configuration;
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
    private readonly IRagConfiguration _configuration;
    private readonly ILogger _logger;

    /// <inheritdoc />
    public int Dimensions => _configuration.DefaultEmbeddingDimensions;

    /// <inheritdoc />
    public string ModelName => _configuration.DefaultEmbeddingModel;

    /// <summary>
    /// Gets the full model version string for the current embedding model.
    /// Format: "{ModelName}:1.0" (e.g., "all-minilm:1.0").
    /// </summary>
    public string ModelVersion => $"{_configuration.DefaultEmbeddingModel}:1.0";

    /// <summary>
    /// Creates a new EmbeddingService.
    /// </summary>
    /// <param name="aiService">The AI service providing access to the active inference provider.</param>
    /// <param name="configuration">The RAG configuration service for embedding parameters.</param>
    public EmbeddingService(IAiService aiService, IRagConfiguration configuration)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = Log.ForContext<EmbeddingService>();
        _logger.Information("EmbeddingService created (model={Model}, dimensions={Dimensions})", ModelName, Dimensions);
    }

    /// <inheritdoc />
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text to embed cannot be null or empty.", nameof(text));

        EnsureProviderAvailable();

        var modelName = ModelName;

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

        var modelName = ModelName;
        var batchSize = _configuration.EmbeddingBatchSize;

        _logger.Information(
            "Generating batch embeddings for {Count} texts using model '{Model}' (batch size: {BatchSize})",
            textList.Count, modelName, batchSize);

        var allEmbeddings = new List<float[]>(textList.Count);
        var totalBatches = (int)Math.Ceiling((double)textList.Count / batchSize);
        var batchIndex = 0;

        for (var offset = 0; offset < textList.Count; offset += batchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batchTexts = textList
                .Skip(offset)
                .Take(batchSize)
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
                $"The embedding model ({ModelName}) must also be pulled via 'ollama pull {ModelName}'.");
        }
    }
}
