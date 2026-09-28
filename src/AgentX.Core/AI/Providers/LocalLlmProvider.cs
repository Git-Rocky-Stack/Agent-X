using System.Runtime.CompilerServices;
using System.Text;
using AgentX.Core.AI.Models;
using AgentX.Core.Constants;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using Serilog;

namespace AgentX.Core.AI.Providers;

/// <summary>
/// AI provider implementation backed by LLamaSharp - .NET bindings for llama.cpp.
/// Loads a GGUF model directly from disk for fully offline, zero-internet inference.
/// Supports chat completion (streaming + non-streaming) and embedding generation.
/// <para>
/// Embeddings always come from the configured model file (the one passed to the constructor),
/// mean-pooled over every token of the input. Chat uses the model named by
/// <see cref="ChatOptions.ModelId"/> when it is another GGUF installed in the models directory,
/// so selecting a different chat model never changes the embedding space.
/// </para>
/// </summary>
public sealed class LocalLlmProvider : IAiProvider
{
    private readonly string _modelsDirectory;
    private readonly string _modelFileName;
    private readonly int _contextSize;
    private readonly int _gpuLayers;
    private readonly ILogger _logger;
    private readonly ProviderLifetime _lifetime = new(nameof(LocalLlmProvider));

    // The configured model backs embeddings and default chat; an alternate GGUF selected for chat
    // is loaded separately. Both are published with volatile writes because the fast paths read
    // them without taking the load lock.
    private volatile LoadedModel? _primary;
    private volatile LoadedModel? _alternate;
    private volatile LLamaEmbedder? _embedder;
    private volatile bool _isAvailable;
    private int? _detectedGpuLayers;

    // Lock order (never acquired in reverse): inference -> embedding -> load.
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);

    // The embedder owns a single native context whose KV cache is cleared and refilled per call,
    // so background indexing and query embedding must never run through it concurrently.
    private readonly SemaphoreSlim _embeddingLock = new(1, 1);

    /// <summary>
    /// Test seam (AX-QA-009): when set, replaces the StatelessExecutor-based inference stream so
    /// the chat pipeline (prompt formatting, inference lock, token accounting, truncation warning,
    /// cancellation) is unit-testable without loading a native GGUF model. Never set in production;
    /// follows the ComparisonService optional-collaborator-seam precedent.
    /// </summary>
    internal Func<string, InferenceParams, CancellationToken, IAsyncEnumerable<string>>? InferenceOverride { get; set; }

    /// <summary>
    /// Test seam: replaces the native embedder call (after the embedding lock is taken) so the
    /// embedding pipeline (lock, pooling, model-name validation) is testable without a GGUF.
    /// Receives the input text and returns the raw vectors the embedder would produce.
    /// Never set in production.
    /// </summary>
    internal Func<string, CancellationToken, Task<IReadOnlyList<float[]>>>? EmbeddingOverride { get; set; }

    /// <summary>
    /// Test seam: counts prompt tokens for the context-fit check when no native tokenizer is
    /// available. Never set in production.
    /// </summary>
    internal Func<string, int>? PromptTokenCounterOverride { get; set; }

    /// <summary>
    /// Test seam (AX-QA-009): overrides the catalog lookup in <see cref="PullModelAsync"/> so the
    /// download pipeline (verified bootstrap download, progress, atomic .part move, failure
    /// cleanup) is testable against a localhost HTTP stub. Never set in production.
    /// </summary>
    internal Func<string, BuiltInModelSource?>? DownloadSourceResolver { get; set; }

    /// <inheritdoc />
    public string ProviderId => "local";

    /// <inheritdoc />
    public string DisplayName => "Built-in LLM";

    /// <inheritdoc />
    public bool IsAvailable => _isAvailable;

    /// <summary>The configured model file (the one used for embeddings).</summary>
    public string ModelFileName => _modelFileName;

    /// <summary>True when the configured model file exists in the models directory.</summary>
    public bool IsModelFileInstalled => File.Exists(ModelPath);

    public LocalLlmProvider(
        string modelsDirectory,
        string modelFileName,
        int contextSize,
        int gpuLayers,
        ILogger logger)
    {
        _modelsDirectory = modelsDirectory ?? throw new ArgumentNullException(nameof(modelsDirectory));
        _modelFileName = modelFileName ?? throw new ArgumentNullException(nameof(modelFileName));
        _contextSize = contextSize;
        _gpuLayers = gpuLayers;
        _logger = logger?.ForContext<LocalLlmProvider>() ?? throw new ArgumentNullException(nameof(logger));

        _logger.Information(
            "LocalLlmProvider created - model: {Model}, context: {ContextSize}, GPU layers: {GpuLayers}",
            _modelFileName, _contextSize, _gpuLayers);
    }

    /// <summary>
    /// Gets the full file path to the GGUF model.
    /// </summary>
    private string ModelPath => Path.Combine(_modelsDirectory, _modelFileName);

    /// <inheritdoc />
    public async Task<bool> CheckConnectionAsync(CancellationToken ct = default)
    {
        _lifetime.Enter();
        try
        {
            var modelExists = File.Exists(ModelPath);
            if (!modelExists)
            {
                _isAvailable = false;
                _logger.Warning("Local model not found at {ModelPath}. Download it first.", ModelPath);
                return false;
            }

            // Lazy-load the model on first connection check
            await EnsurePrimaryLoadedAsync(ct).ConfigureAwait(false);

            _logger.Information("Local LLM available: {ModelPath}", ModelPath);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to check local LLM availability");
            _isAvailable = false;
            return false;
        }
        finally
        {
            _lifetime.Exit();
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var models = new List<AiModel>();

        if (File.Exists(ModelPath))
        {
            var fileInfo = new FileInfo(ModelPath);
            models.Add(new AiModel
            {
                Id = _modelFileName,
                Name = BuiltInModelCatalog.Find(_modelFileName)?.DisplayName
                    ?? Path.GetFileNameWithoutExtension(_modelFileName),
                ProviderId = ProviderId,
                Family = "llama",
                IsAvailable = true,
                SizeBytes = fileInfo.Length,
                QuantizationLevel = "Q4_K_M",
                ParameterCount = AppConstants.DefaultModelParamCountMillions, // Stored in millions (3B = 3000M)
                ContextLength = _contextSize,
                ModifiedAt = fileInfo.LastWriteTimeUtc
            });
        }

        // List any other GGUF files in the models directory
        if (Directory.Exists(_modelsDirectory))
        {
            foreach (var gguf in Directory.GetFiles(_modelsDirectory, "*.gguf"))
            {
                var name = Path.GetFileName(gguf);
                if (name.Equals(_modelFileName, StringComparison.OrdinalIgnoreCase))
                    continue; // Already added above

                var fi = new FileInfo(gguf);
                models.Add(new AiModel
                {
                    Id = name,
                    Name = Path.GetFileNameWithoutExtension(name),
                    ProviderId = ProviderId,
                    Family = "gguf",
                    IsAvailable = true,
                    SizeBytes = fi.Length,
                    ContextLength = _contextSize,
                    ModifiedAt = fi.LastWriteTimeUtc
                });
            }
        }

        return Task.FromResult<IReadOnlyList<AiModel>>(models.AsReadOnly());
    }

    /// <inheritdoc />
    /// <remarks>
    /// Only models listed in <see cref="BuiltInModelCatalog"/> can be pulled. The download goes
    /// through <see cref="BuiltInModelBootstrap"/>, so it gets the same completeness, size-floor
    /// and (when pinned) SHA-256 checks as the first-run download. An unknown name throws instead
    /// of returning as if the pull had succeeded.
    /// </remarks>
    public async Task PullModelAsync(
        string modelName,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        _lifetime.Enter();
        try
        {
            var fileName = RequireGgufFileName(modelName);
            var source = DownloadSourceResolver is not null
                ? DownloadSourceResolver(fileName)
                : BuiltInModelCatalog.Find(fileName);

            if (source is null)
            {
                var known = string.Join(", ", BuiltInModelCatalog.All.Select(m => m.FileName));
                throw new NotSupportedException(
                    $"No download source is known for '{fileName}'. The built-in provider can download: {known}. " +
                    $"Other GGUF files can be copied into {_modelsDirectory}.");
            }

            _logger.Information("Downloading model {Model} from {Url}", source.FileName, source.DownloadUrl);

            using var httpClient = new HttpClient { Timeout = AppConstants.ModelDownloadTimeout };
            var bootstrap = BuiltInModelBootstrap.ForSource(httpClient, _modelsDirectory, _logger, source);
            await bootstrap.EnsureInstalledAsync(progress, ct).ConfigureAwait(false);

            // No eager load here: the pulled file may not be the configured model at all (pulling
            // the 1B model must not load the 3B one), and the configured model loads lazily on
            // its first use or connection check.
            _logger.Information("Model available: {Model}", source.FileName);
        }
        finally
        {
            _lifetime.Exit();
        }
    }

    /// <inheritdoc />
    public async Task DeleteModelAsync(string modelName, CancellationToken ct = default)
    {
        _lifetime.Enter();
        try
        {
            var fileName = RequireGgufFileName(modelName);
            var path = Path.Combine(_modelsDirectory, fileName);
            if (!File.Exists(path))
                return;

            // Unload first if the file is mapped by a loaded model. Wait for running inference
            // and embedding work before freeing the weights: releasing them underneath llama.cpp
            // crashes the process.
            await _inferenceLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _embeddingLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await _loadLock.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        UnloadModelFile(fileName);
                    }
                    finally
                    {
                        _loadLock.Release();
                    }
                }
                finally
                {
                    _embeddingLock.Release();
                }
            }
            finally
            {
                _inferenceLock.Release();
            }

            File.Delete(path);
            _logger.Information("Deleted local model: {Model}", fileName);
        }
        finally
        {
            _lifetime.Exit();
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _lifetime.Enter();
        try
        {
            var jsonMode = options?.ResponseFormat == ResponseFormat.JsonObject;
            var inferenceParams = BuildInferenceParams(options);

            // Track emitted tokens so we can warn on MaxTokens truncation (P0-6).
            // LLamaSharp StatelessExecutor stops naturally on antiprompt or MaxTokens -
            // when token count equals MaxTokens, we likely hit the budget cap.
            int emittedTokens = 0;
            var maxTokens = inferenceParams.MaxTokens;

            await _inferenceLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                IAsyncEnumerable<string> tokenStream;
                if (InferenceOverride is not null)
                {
                    // The override substitutes the token source only; prompt fitting, lock,
                    // accounting, and cancellation are unchanged.
                    var countTokens = PromptTokenCounterOverride ?? EstimatePromptTokens;
                    var prompt = BuildPromptWithinContext(messages, jsonMode, countTokens, _contextSize, maxTokens, _logger);
                    tokenStream = InferenceOverride(prompt, inferenceParams, ct);
                }
                else
                {
                    // Resolved under the inference lock, so an alternate chat model is never
                    // swapped out while another stream is still using it.
                    var model = await ResolveChatModelAsync(options?.ModelId, ct).ConfigureAwait(false);
                    var countTokens = PromptTokenCounterOverride
                        ?? (text => model.Weights.Tokenize(text, true, true, Encoding.UTF8).Length);
                    var prompt = BuildPromptWithinContext(messages, jsonMode, countTokens, _contextSize, maxTokens, _logger);

                    // StatelessExecutor creates its own context per call, sized to the configured
                    // LocalContextSize; the prompt was fitted to that size above, so a caller that
                    // budgeted for a larger ContextWindow cannot overflow it.
                    tokenStream = new StatelessExecutor(model.Weights, model.ChatParams).InferAsync(prompt, inferenceParams, ct);
                }

                // JSON mode primes the prompt with "{", so the model continues after the brace and
                // the brace itself is not part of the generated stream. Emit it first so callers
                // receive a complete JSON object.
                var awaitingJsonBody = jsonMode;
                if (jsonMode)
                    yield return "{";

                await foreach (var token in tokenStream.ConfigureAwait(false))
                {
                    if (ct.IsCancellationRequested) yield break;
                    emittedTokens++;

                    var text = token;
                    if (awaitingJsonBody)
                        (text, awaitingJsonBody) = StripDuplicateOpeningBrace(text);

                    if (text.Length > 0)
                        yield return text;
                }
            }
            finally
            {
                _inferenceLock.Release();
            }

            // Heuristic truncation detection: LLamaSharp doesn't expose a stop_reason,
            // but reaching MaxTokens is the most common failure mode for evaluators
            // and rerankers (which set tight budgets like 128 tokens).
            if (maxTokens > 0 && emittedTokens >= maxTokens)
            {
                _logger.Warning(
                    "Local LLM response likely truncated: emitted {Emitted} tokens, MaxTokens={MaxTokens}, model={Model}. " +
                    "If the response should have been shorter, check antiprompts; otherwise raise MaxTokens.",
                    emittedTokens, maxTokens, options?.ModelId ?? _modelFileName);
            }
        }
        finally
        {
            _lifetime.Exit();
        }
    }

    /// <inheritdoc />
    public async Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder(AppConstants.ResponseBuilderCapacity);

        await foreach (var token in StreamChatAsync(messages, options, ct).ConfigureAwait(false))
        {
            sb.Append(token);
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Embeddings come from the configured model file only; <paramref name="modelName"/> must be
    /// empty or name that file. The input is mean-pooled over all of its tokens and truncated to
    /// the embedding context (<see cref="AppConstants.EmbeddingContextSize"/> tokens).
    /// </remarks>
    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        string modelName,
        CancellationToken ct = default)
    {
        _lifetime.Enter();
        try
        {
            EnsureEmbeddingModel(modelName);
            return await EmbedAsync(text, ct).ConfigureAwait(false);
        }
        finally
        {
            _lifetime.Exit();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        string modelName,
        CancellationToken ct = default)
    {
        _lifetime.Enter();
        try
        {
            EnsureEmbeddingModel(modelName);

            // LLamaEmbedder has no batch API. The lock is taken per text so query embeddings for
            // search can interleave with a long indexing batch instead of waiting for all of it.
            var results = new List<float[]>(texts.Count);
            foreach (var text in texts)
            {
                ct.ThrowIfCancellationRequested();
                results.Add(await EmbedAsync(text, ct).ConfigureAwait(false));
            }

            return results.AsReadOnly();
        }
        finally
        {
            _lifetime.Exit();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Deferred: if a chat stream or an embedding is still running, the weights are released
        // when it finishes instead of being freed underneath llama.cpp.
        if (!_lifetime.RequestDispose(ReleaseResources))
            return;

        _logger.Information("LocalLlmProvider disposed");
    }

    // ===================================================================
    //  Model Lifecycle
    // ===================================================================

    /// <summary>Loaded weights for one GGUF file plus the chat parameters built for it.</summary>
    private sealed class LoadedModel : IDisposable
    {
        public LoadedModel(string fileName, LLamaWeights weights, ModelParams chatParams)
        {
            FileName = fileName;
            Weights = weights;
            ChatParams = chatParams;
        }

        public string FileName { get; }
        public LLamaWeights Weights { get; }
        public ModelParams ChatParams { get; }

        public void Dispose() => Weights.Dispose();
    }

    private async Task<LoadedModel> LoadWeightsAsync(string fileName, CancellationToken ct)
    {
        var modelPath = Path.Combine(_modelsDirectory, fileName);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"GGUF model not found: {modelPath}", modelPath);

        var effectiveGpuLayers = ResolveGpuLayers(_gpuLayers, () => _detectedGpuLayers ??= DetectRecommendedGpuLayers());

        _logger.Information(
            "Loading local LLM from {ModelPath} (GPU layers: {GpuLayers})...",
            modelPath, effectiveGpuLayers);

        var chatParams = new ModelParams(modelPath)
        {
            ContextSize = (uint)_contextSize,
            GpuLayerCount = effectiveGpuLayers
        };

        var weights = await LLamaWeights.LoadFromFileAsync(
            chatParams, ct,
            new Progress<float>(p =>
                _logger.Debug("Model loading: {Percent:P0}", p)))
            .ConfigureAwait(false);

        _logger.Information("Local LLM loaded: {Model} (context: {ContextSize})", fileName, _contextSize);
        return new LoadedModel(fileName, weights, chatParams);
    }

    /// <summary>Loads the configured model (used for embeddings and default chat) once.</summary>
    private async Task<LoadedModel> EnsurePrimaryLoadedAsync(CancellationToken ct)
    {
        var loaded = _primary;
        if (loaded is not null)
            return loaded;

        await _loadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            loaded = _primary;
            if (loaded is not null)
                return loaded;

            loaded = await LoadWeightsAsync(_modelFileName, ct).ConfigureAwait(false);
            _primary = loaded;
            _isAvailable = true;
            return loaded;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(ex, "Failed to load local LLM model");
            _isAvailable = false;
            throw;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Creates the embedding context on the configured model: mean pooling (one vector for the
    /// whole input rather than one per token) and a batch as large as the context, because
    /// llama.cpp only pools the tokens of a single micro-batch and LLamaSharp rejects inputs
    /// longer than the batch.
    /// </summary>
    private async Task<LLamaEmbedder> EnsureEmbedderAsync(CancellationToken ct)
    {
        var embedder = _embedder;
        if (embedder is not null)
            return embedder;

        var primary = await EnsurePrimaryLoadedAsync(ct).ConfigureAwait(false);

        await _loadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            embedder = _embedder;
            if (embedder is not null)
                return embedder;

            const uint embeddingWindow = AppConstants.EmbeddingContextSize;
            var embeddingParams = new ModelParams(primary.ChatParams.ModelPath)
            {
                ContextSize = embeddingWindow,
                BatchSize = embeddingWindow,
                UBatchSize = embeddingWindow,
                GpuLayerCount = primary.ChatParams.GpuLayerCount,
                Embeddings = true,
                PoolingType = LLamaPoolingType.Mean
            };

            embedder = new LLamaEmbedder(primary.Weights, embeddingParams);
            _embedder = embedder;
            _logger.Information(
                "Local embedder ready: {Model}, {Dimensions} dimensions, mean pooling, {Window}-token window",
                primary.FileName, embedder.EmbeddingSize, embeddingWindow);
            return embedder;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Returns the weights to chat with. Caller must hold the inference lock, which guarantees
    /// no other stream is using the alternate model while it is replaced.
    /// </summary>
    private async Task<LoadedModel> ResolveChatModelAsync(string? requestedModelId, CancellationToken ct)
    {
        var fileName = ResolveChatModelFileName(requestedModelId);
        if (string.Equals(fileName, _modelFileName, StringComparison.OrdinalIgnoreCase))
            return await EnsurePrimaryLoadedAsync(ct).ConfigureAwait(false);

        var alternate = _alternate;
        if (alternate is not null && string.Equals(alternate.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            return alternate;

        await _loadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Embeddings never use the alternate, and the inference lock keeps other chats off
            // it, so the previous alternate can be released right away.
            var previous = _alternate;
            _alternate = null;
            previous?.Dispose();

            alternate = await LoadWeightsAsync(fileName, ct).ConfigureAwait(false);
            _alternate = alternate;
            return alternate;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Maps a requested chat model id to a GGUF file: the configured model when the id is empty
    /// or not an installed GGUF file name (ids such as an Ollama tag cannot be served by this
    /// provider), otherwise the requested file.
    /// </summary>
    internal string ResolveChatModelFileName(string? requestedModelId)
    {
        if (string.IsNullOrWhiteSpace(requestedModelId))
            return _modelFileName;

        var requested = requestedModelId.Trim();
        if (string.Equals(requested, _modelFileName, StringComparison.OrdinalIgnoreCase))
            return _modelFileName;

        if (IsPlainGgufFileName(requested) && File.Exists(Path.Combine(_modelsDirectory, requested)))
            return requested;

        _logger.Warning(
            "Model {Requested} is not an installed GGUF file; the built-in provider uses {Configured}",
            requested, _modelFileName);
        return _modelFileName;
    }

    private void EnsureEmbeddingModel(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName) ||
            string.Equals(modelName.Trim(), _modelFileName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new NotSupportedException(
            $"The built-in provider embeds only with its configured model '{_modelFileName}'; " +
            $"'{modelName}' was requested.");
    }

    private async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text to embed cannot be null or empty.", nameof(text));

        await _embeddingLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            IReadOnlyList<float[]> vectors;
            if (EmbeddingOverride is not null)
            {
                vectors = await EmbeddingOverride(text, ct).ConfigureAwait(false);
            }
            else
            {
                var embedder = await EnsureEmbedderAsync(ct).ConfigureAwait(false);
                var input = FitToEmbeddingWindow(embedder, text);
                vectors = await embedder.GetEmbeddings(input, ct).ConfigureAwait(false);
            }

            return PoolEmbeddings(vectors);
        }
        finally
        {
            _embeddingLock.Release();
        }
    }

    /// <summary>
    /// Truncates <paramref name="text"/> so that, with the BOS token the embedder prepends, it
    /// fits the embedding batch. Longer chunks are embedded from their leading tokens instead of
    /// failing with "Input contains more tokens than configured batch size".
    /// </summary>
    private string FitToEmbeddingWindow(LLamaEmbedder embedder, string text)
    {
        var context = embedder.Context;
        var limit = (int)context.BatchSize - 1;

        var fitted = FitToTokenLimit(
            text,
            limit,
            value => context.Tokenize(value, addBos: false, special: false),
            (tokens, count) =>
            {
                var decoder = new StreamingTokenDecoder(context);
                decoder.AddRange(new ReadOnlySpan<LLamaToken>(tokens, 0, count));
                return decoder.Read();
            });

        if (!ReferenceEquals(fitted, text))
        {
            _logger.Debug(
                "Embedding input truncated to the {Limit}-token embedding window ({Chars} of {Total} characters kept)",
                limit, fitted.Length, text.Length);
        }

        return fitted;
    }

    /// <summary>
    /// Returns <paramref name="text"/> unchanged when it has at most <paramref name="maxTokens"/>
    /// tokens, otherwise the longest decoded token prefix that re-tokenizes within the limit
    /// (decoding and re-encoding is not always length-preserving, so the prefix is re-checked).
    /// </summary>
    internal static string FitToTokenLimit<TToken>(
        string text,
        int maxTokens,
        Func<string, TToken[]> tokenize,
        Func<TToken[], int, string> decodePrefix)
    {
        ArgumentNullException.ThrowIfNull(tokenize);
        ArgumentNullException.ThrowIfNull(decodePrefix);

        if (maxTokens <= 0)
            return string.Empty;

        var tokens = tokenize(text);
        if (tokens.Length <= maxTokens)
            return text;

        var keep = maxTokens;
        while (keep > 0)
        {
            var candidate = decodePrefix(tokens, keep);
            if (tokenize(candidate).Length <= maxTokens)
                return candidate;

            keep -= Math.Max(1, keep / 16);
        }

        return string.Empty;
    }

    /// <summary>
    /// Reduces the embedder output to one vector. With mean pooling llama.cpp returns a single
    /// pooled vector; if a runtime ignores the pooling type and returns one vector per token,
    /// the token vectors are averaged here instead of returning the first (BOS) vector, which
    /// is identical for every input in a causal model.
    /// </summary>
    internal static float[] PoolEmbeddings(IReadOnlyList<float[]> vectors)
    {
        ArgumentNullException.ThrowIfNull(vectors);

        if (vectors.Count == 0 || vectors[0].Length == 0)
            throw new InvalidOperationException("The local embedder returned no embedding vector.");

        if (vectors.Count == 1)
            return vectors[0];

        var dimensions = vectors[0].Length;
        var mean = new float[dimensions];
        foreach (var vector in vectors)
        {
            if (vector.Length != dimensions)
                throw new InvalidOperationException("The local embedder returned vectors of different sizes.");

            for (var i = 0; i < dimensions; i++)
                mean[i] += vector[i];
        }

        for (var i = 0; i < dimensions; i++)
            mean[i] /= vectors.Count;

        return mean;
    }

    private void UnloadModelFile(string fileName)
    {
        var primary = _primary;
        if (primary is not null && string.Equals(primary.FileName, fileName, StringComparison.OrdinalIgnoreCase))
        {
            var embedder = _embedder;
            _embedder = null;
            embedder?.Dispose();

            _primary = null;
            primary.Dispose();
            _isAvailable = false;
        }

        var alternate = _alternate;
        if (alternate is not null && string.Equals(alternate.FileName, fileName, StringComparison.OrdinalIgnoreCase))
        {
            _alternate = null;
            alternate.Dispose();
        }
    }

    private void ReleaseResources()
    {
        var embedder = _embedder;
        _embedder = null;
        embedder?.Dispose();

        var alternate = _alternate;
        _alternate = null;
        alternate?.Dispose();

        var primary = _primary;
        _primary = null;
        primary?.Dispose();

        _isAvailable = false;
        _loadLock.Dispose();
        _inferenceLock.Dispose();
        _embeddingLock.Dispose();
    }

    // ===================================================================
    //  Prompt Formatting (Llama 3 Instruct Template)
    // ===================================================================

    /// <summary>
    /// Formats the messages and, when the prompt would not leave room for the answer inside the
    /// model context, drops the oldest non-system messages (the final message is always kept).
    /// Throws a clear error when even that cannot fit, instead of letting llama.cpp fail the
    /// decode with an opaque "no KV slot" error.
    /// </summary>
    internal static string BuildPromptWithinContext(
        IReadOnlyList<ChatMessage> messages,
        bool jsonMode,
        Func<string, int> countTokens,
        int contextSize,
        int maxTokens,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(countTokens);

        var prompt = FormatChatPrompt(messages, jsonMode);
        if (contextSize <= 0)
            return prompt;

        // Room for the answer: MaxTokens, capped at a quarter of the context. Generation beyond
        // the context is handled by the executor's context shift, but the prompt itself must fit.
        const int minimumHeadroom = 16;
        var reserve = Math.Clamp(maxTokens, minimumHeadroom, Math.Max(minimumHeadroom, contextSize / 4));
        var budget = contextSize - reserve;

        var tokens = countTokens(prompt);
        if (tokens <= budget)
            return prompt;

        var kept = messages.ToList();
        var dropped = 0;
        while (tokens > budget)
        {
            var oldest = kept.FindIndex(m => !string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase));
            if (oldest < 0 || oldest == kept.Count - 1)
                break;

            kept.RemoveAt(oldest);
            dropped++;
            prompt = FormatChatPrompt(kept, jsonMode);
            tokens = countTokens(prompt);
        }

        if (dropped > 0)
        {
            logger.Warning(
                "Prompt exceeded the built-in model context ({ContextSize} tokens, {Reserve} reserved for the answer); " +
                "dropped the {Dropped} oldest messages",
                contextSize, reserve, dropped);
        }

        if (tokens > contextSize - minimumHeadroom)
        {
            throw new InvalidOperationException(
                $"The prompt needs about {tokens} tokens but the built-in model context holds {contextSize}. " +
                "Shorten the input or raise the local context size in Settings.");
        }

        return prompt;
    }

    /// <summary>Rough token estimate used when no native tokenizer is loaded (test seam path).</summary>
    private static int EstimatePromptTokens(string text) => (text.Length + 3) / 4;

    /// <summary>
    /// In JSON mode the prompt already ends with "{" and that brace is emitted before the
    /// generated tokens. If the model nevertheless starts its answer with another "{", that
    /// duplicate is dropped: a second "{" can never legally follow the opening brace.
    /// Returns the token to emit and whether the body start is still pending (whitespace only).
    /// </summary>
    internal static (string Text, bool StillAwaiting) StripDuplicateOpeningBrace(string token)
    {
        if (string.IsNullOrEmpty(token))
            return (token ?? string.Empty, true);

        for (var i = 0; i < token.Length; i++)
        {
            if (char.IsWhiteSpace(token[i]))
                continue;

            return token[i] == '{'
                ? (token.Remove(i, 1), false)
                : (token, false);
        }

        return (token, true);
    }

    /// <summary>
    /// Formats messages into the Llama 3.x instruct chat template.
    /// When <paramref name="jsonMode"/> is true, injects a JSON-constraining system instruction.
    /// </summary>
    private static string FormatChatPrompt(IReadOnlyList<ChatMessage> messages, bool jsonMode = false)
    {
        var sb = new StringBuilder(4096);
        sb.Append("<|begin_of_text|>");

        // Inject JSON mode instruction before any user-provided system message
        if (jsonMode)
        {
            sb.Append("<|start_header_id|>system<|end_header_id|>\n\n");
            sb.Append("You MUST respond with valid JSON only. No markdown code fences, no explanation, no text outside the JSON object.");
            sb.Append("<|eot_id|>");
        }

        foreach (var msg in messages)
        {
            var role = msg.Role.ToLowerInvariant() switch
            {
                "system" => "system",
                "assistant" => "assistant",
                _ => "user"
            };

            sb.Append($"<|start_header_id|>{role}<|end_header_id|>\n\n");
            sb.Append(msg.Content);
            sb.Append("<|eot_id|>");
        }

        // Prompt the model to generate the assistant response
        sb.Append("<|start_header_id|>assistant<|end_header_id|>\n\n");

        // For JSON mode, prime the output to start with an opening brace
        if (jsonMode)
            sb.Append('{');

        return sb.ToString();
    }

    /// <summary>
    /// Builds LLamaSharp InferenceParams from our ChatOptions.
    /// </summary>
    private static InferenceParams BuildInferenceParams(ChatOptions? options)
    {
        var maxTokens = options?.MaxTokens ?? 2048;
        var temperature = (float)(options?.Temperature ?? 0.7);
        var topP = (float)(options?.TopP ?? 0.9);

        var samplingPipeline = new DefaultSamplingPipeline
        {
            Temperature = temperature,
            TopP = topP,
            RepeatPenalty = 1.1f
        };

        var inferenceParams = new InferenceParams
        {
            MaxTokens = maxTokens,
            AntiPrompts = new List<string> { "<|eot_id|>", "<|end_of_text|>" },
            SamplingPipeline = samplingPipeline
        };

        return inferenceParams;
    }

    /// <summary>
    /// True for a bare "*.gguf" file name with no directory components. Both separator styles
    /// and drive prefixes are rejected on every platform, not only the host's own separator.
    /// </summary>
    private static bool IsPlainGgufFileName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.IndexOfAny(['/', '\\', ':']) < 0 &&
        string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal) &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Validates a model file name received from the UI before it is combined with the models
    /// directory, so a name such as "..\x" can never reach a file outside it.
    /// </summary>
    private static string RequireGgufFileName(string? modelName)
    {
        var name = modelName?.Trim() ?? string.Empty;
        if (!IsPlainGgufFileName(name))
        {
            throw new ArgumentException(
                $"'{modelName}' is not a GGUF model file name (expected a bare *.gguf name).",
                nameof(modelName));
        }

        return name;
    }

    /// <summary>
    /// The number of layers the model is loaded with on the GPU, from the saved setting
    /// (AppSettings.LocalGpuLayers): 0 is Automatic and asks <paramref name="detect"/> (an NVIDIA
    /// GPU gets 16, 28 or 33 layers by its video memory, anything else none), a positive count is
    /// used as it is, and a negative value keeps every layer on the CPU.
    /// <para>
    /// The layers only reach the GPU when LLamaSharp loads its CUDA 12 backend. It tries that
    /// backend only when the NVIDIA CUDA Toolkit is installed (CUDA_PATH, major version 12), since
    /// the backend needs cudart64_12.dll and cublas64_12.dll, which Agent-X does not ship;
    /// otherwise the CPU backend runs and the count has no effect.
    /// </para>
    /// </summary>
    internal static int ResolveGpuLayers(int configuredLayers, Func<int> detect) => configuredLayers switch
    {
        0 => detect(),
        < 0 => 0,
        _ => configuredLayers
    };

    /// <summary>
    /// Detects NVIDIA GPU via WMI and returns recommended GPU layer count.
    /// Falls back to 0 (CPU-only) on any failure.
    /// </summary>
    private int DetectRecommendedGpuLayers()
    {
        try
        {
            // The reader prefers the driver's 64-bit qwMemorySize over WMI AdapterRAM, which
            // saturates at 4 GB and would cap every modern card at the 28-layer tier.
            foreach (var adapter in GpuMemoryReader.ReadAdapters(_logger))
            {
                var capability = new HardwareCapability { GpuName = adapter.Name, GpuVramBytes = adapter.VramBytes };
                if (!capability.IsNvidiaGpu)
                    continue;

                var layers = capability.RecommendedGpuLayers;
                _logger.Information(
                    "NVIDIA GPU detected: {GpuName} ({Vram:F1} GB) - auto-setting {Layers} GPU layers",
                    adapter.Name, adapter.VramBytes / 1_000_000_000.0, layers);

                return layers;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "GPU auto-detection failed; defaulting to CPU-only");
        }

        _logger.Debug("No NVIDIA GPU detected; using CPU-only inference");
        return 0;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_lifetime.IsDisposeRequested, this);
    }
}
