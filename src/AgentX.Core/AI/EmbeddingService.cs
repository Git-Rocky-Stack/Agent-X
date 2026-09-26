using System.Collections.Concurrent;
using AgentX.Core.Configuration;
using Serilog;

namespace AgentX.Core.AI;

/// <summary>
/// Generates vector embeddings from text content. The embedding provider and model are chosen
/// independently of the chat provider (see <see cref="EmbeddingTargetResolver"/>): switching the
/// chat model to a provider without an embedding API, or to another model, neither breaks
/// indexing nor changes the embedding space of the index. The user's saved Embedding Model
/// setting is honored for providers that support model selection (Ollama, OpenAI).
/// Supports single-text and batch embedding with configurable batch sizes to avoid
/// overwhelming the inference backend.
/// </summary>
public sealed class EmbeddingService : IEmbeddingService
{
    private readonly IAiService _aiService;
    private readonly IRagConfiguration _configuration;
    private readonly ILogger _logger;

    // Vector size actually returned per provider:model, so ModelVersion carries the real size.
    private readonly ConcurrentDictionary<string, int> _observedDimensions = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public int Dimensions => ResolveDimensions(CurrentTarget);

    /// <inheritdoc />
    public string ModelName => CurrentTarget.ModelName;

    /// <summary>
    /// Identifies the embedding space: <c>{provider}:{model}:{dimensions}</c>, for example
    /// <c>ollama:all-minilm:384</c> or <c>local:llama-3.2-3b-instruct-q4_k_m.gguf:3072</c>.
    /// Chunks stamped with a different value were embedded by a different provider, model or
    /// vector size and are not comparable with current query vectors.
    /// </summary>
    public string ModelVersion
    {
        get
        {
            var target = CurrentTarget;
            return FormatModelVersion(target, ResolveDimensions(target));
        }
    }

    /// <summary>
    /// Creates a new EmbeddingService.
    /// </summary>
    /// <param name="aiService">The AI service that owns the providers and resolves the embedding target.</param>
    /// <param name="configuration">The RAG configuration service for embedding parameters.</param>
    public EmbeddingService(IAiService aiService, IRagConfiguration configuration)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = Log.ForContext<EmbeddingService>();
        _logger.Information("EmbeddingService created (batch size {BatchSize})", _configuration.EmbeddingBatchSize);
    }

    /// <summary>Formats the model version string for a target and vector size.</summary>
    public static string FormatModelVersion(EmbeddingTarget target, int dimensions)
    {
        ArgumentNullException.ThrowIfNull(target);
        return $"{target.ProviderId}:{target.ModelName}:{dimensions}";
    }

    private EmbeddingTarget CurrentTarget =>
        _aiService.ResolveEmbeddingTarget()
        ?? new EmbeddingTarget("ollama", EmbeddingTargetResolver.DefaultEmbeddingModelSetting);

    /// <inheritdoc />
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text to embed cannot be null or empty.", nameof(text));

        var target = CurrentTarget;
        var provider = ResolveProvider(target);

        _logger.Debug("Generating embedding for text of length {Length} with {Provider}:{Model}",
            text.Length, target.ProviderId, target.ModelName);

        try
        {
            var embedding = await provider
                .GenerateEmbeddingAsync(text, target.ModelName, ct)
                .ConfigureAwait(false);

            RecordDimensions(target, embedding);
            _logger.Debug("Embedding generated: {Dimensions} dimensions", embedding.Length);
            return embedding;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(ex, "Failed to generate embedding for text of length {Length} with {Provider}:{Model}",
                text.Length, target.ProviderId, target.ModelName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IEnumerable<string> texts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var textList = texts as IList<string> ?? texts.ToList();

        if (textList.Count == 0)
            return Array.Empty<float[]>();

        var target = CurrentTarget;
        var provider = ResolveProvider(target);
        var batchSize = Math.Max(1, _configuration.EmbeddingBatchSize);

        _logger.Information(
            "Generating batch embeddings for {Count} texts with {Provider}:{Model} (batch size: {BatchSize})",
            textList.Count, target.ProviderId, target.ModelName, batchSize);

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
                var batchEmbeddings = await provider
                    .GenerateEmbeddingsAsync(batchTexts, target.ModelName, ct)
                    .ConfigureAwait(false);

                // A short or empty result would silently shift every following vector onto the
                // wrong chunk, so a count mismatch fails the batch instead.
                if (batchEmbeddings.Count != batchTexts.Count)
                {
                    throw new InvalidOperationException(
                        $"{target.ProviderId} returned {batchEmbeddings.Count} embeddings for {batchTexts.Count} texts.");
                }

                foreach (var embedding in batchEmbeddings)
                    RecordDimensions(target, embedding);

                allEmbeddings.AddRange(batchEmbeddings);

                _logger.Debug("Batch {BatchIndex}/{TotalBatches} completed, {Remaining} texts remaining",
                    batchIndex, totalBatches, textList.Count - offset - batchTexts.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
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
    /// Returns the provider for the embedding target, or throws a clear, actionable exception
    /// when it cannot serve embeddings. Never falls back to another provider: that would mix
    /// embedding spaces in one index or send documents somewhere the user did not choose.
    /// </summary>
    private IAiProvider ResolveProvider(EmbeddingTarget target)
    {
        var provider = _aiService.GetProvider(target.ProviderId);
        if (provider is not null)
            return provider;

        var message = target.ProviderId switch
        {
            "openai" =>
                $"Embedding model '{target.ModelName}' is an OpenAI model, but no OpenAI API key is configured. " +
                "Add the key in Settings or choose a local embedding model.",
            "ollama" =>
                $"Cannot generate embeddings with Ollama model '{target.ModelName}': the Ollama endpoint is not configured correctly. " +
                "Check the endpoint in Settings.",
            _ =>
                $"Cannot generate embeddings: the {target.ProviderId} provider is not available. " +
                "Make sure the AI service is initialized."
        };

        throw new InvalidOperationException(message);
    }

    private void RecordDimensions(EmbeddingTarget target, float[] embedding)
    {
        if (embedding.Length == 0)
        {
            throw new InvalidOperationException(
                $"{target.ProviderId} returned an empty embedding for model '{target.ModelName}'.");
        }

        var key = $"{target.ProviderId}:{target.ModelName}";
        var previous = _observedDimensions.GetOrAdd(key, embedding.Length);
        if (previous != embedding.Length)
        {
            _logger.Warning(
                "Embedding size for {Target} changed from {Previous} to {Current}; the model version changes with it",
                key, previous, embedding.Length);
            _observedDimensions[key] = embedding.Length;
        }
    }

    private int ResolveDimensions(EmbeddingTarget target)
    {
        if (_observedDimensions.TryGetValue($"{target.ProviderId}:{target.ModelName}", out var observed))
            return observed;

        return EmbeddingTargetResolver.KnownDimensions(target) ?? _configuration.DefaultEmbeddingDimensions;
    }
}
