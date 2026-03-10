using System.Runtime.CompilerServices;
using System.Text;
using AgentX.Core.AI.Models;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Serilog;

namespace AgentX.Core.AI.Providers;

/// <summary>
/// AI provider implementation backed by LLamaSharp — .NET bindings for llama.cpp.
/// Loads a GGUF model directly from disk for fully offline, zero-internet inference.
/// Supports chat completion (streaming + non-streaming) and embedding generation.
/// </summary>
public sealed class LocalLlmProvider : IAiProvider
{
    private readonly string _modelsDirectory;
    private readonly string _modelFileName;
    private readonly int _contextSize;
    private readonly int _gpuLayers;
    private readonly ILogger _logger;

    private LLamaWeights? _weights;
    private LLamaEmbedder? _embedder;
    private ModelParams? _chatParams;
    private ModelParams? _embeddingParams;
    private bool _isAvailable;
    private bool _disposed;

    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);

    /// <inheritdoc />
    public string ProviderId => "local";

    /// <inheritdoc />
    public string DisplayName => "Built-in LLM";

    /// <inheritdoc />
    public bool IsAvailable => _isAvailable;

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
            "LocalLlmProvider created — model: {Model}, context: {ContextSize}, GPU layers: {GpuLayers}",
            _modelFileName, _contextSize, _gpuLayers);
    }

    /// <summary>
    /// Gets the full file path to the GGUF model.
    /// </summary>
    private string ModelPath => Path.Combine(_modelsDirectory, _modelFileName);

    /// <inheritdoc />
    public async Task<bool> CheckConnectionAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        try
        {
            var modelExists = File.Exists(ModelPath);
            _isAvailable = modelExists;

            if (!modelExists)
            {
                _logger.Warning("Local model not found at {ModelPath}. Download it first.", ModelPath);
                return false;
            }

            // Lazy-load the model on first connection check
            if (_weights is null)
            {
                await LoadModelAsync(ct).ConfigureAwait(false);
            }

            _logger.Information("Local LLM available: {ModelPath}", ModelPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to check local LLM availability");
            _isAvailable = false;
            return false;
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
                Name = "Llama 3.2 3B Instruct (Q4_K_M)",
                ProviderId = ProviderId,
                Family = "llama",
                IsAvailable = true,
                SizeBytes = fileInfo.Length,
                QuantizationLevel = "Q4_K_M",
                ParameterCount = 3000, // Stored in millions (3B = 3000M)
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
    public async Task PullModelAsync(
        string modelName,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        // Download the default model from HuggingFace
        var targetPath = Path.Combine(_modelsDirectory, modelName);
        Directory.CreateDirectory(_modelsDirectory);

        var url = ResolveDownloadUrl(modelName);
        if (string.IsNullOrEmpty(url))
        {
            _logger.Warning("No download URL known for model: {Model}", modelName);
            return;
        }

        _logger.Information("Downloading model {Model} from {Url}", modelName, url);

        progress?.Report(new ModelDownloadProgress
        {
            ModelId = modelName,
            Status = "Downloading..."
        });

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var downloadedBytes = 0L;

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);

        var buffer = new byte[81920];
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
            downloadedBytes += bytesRead;

            progress?.Report(new ModelDownloadProgress
            {
                ModelId = modelName,
                Status = "Downloading...",
                CompletedBytes = downloadedBytes,
                TotalBytes = totalBytes
            });
        }

        _logger.Information("Model downloaded: {Model} ({Size} bytes)", modelName, downloadedBytes);

        progress?.Report(new ModelDownloadProgress
        {
            ModelId = modelName,
            Status = "Complete",
            CompletedBytes = downloadedBytes,
            TotalBytes = downloadedBytes
        });

        // Reload the model after download
        await LoadModelAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DeleteModelAsync(string modelName, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var path = Path.Combine(_modelsDirectory, modelName);
        if (File.Exists(path))
        {
            // Unload if it's the active model
            if (modelName.Equals(_modelFileName, StringComparison.OrdinalIgnoreCase))
            {
                DisposeModel();
            }

            File.Delete(path);
            _logger.Information("Deleted local model: {Model}", modelName);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await EnsureModelLoadedAsync(ct).ConfigureAwait(false);

        var prompt = FormatChatPrompt(messages, options?.ResponseFormat == ResponseFormat.JsonObject);
        var inferenceParams = BuildInferenceParams(options);

        // StatelessExecutor creates its own context per call — thread-safe
        var executor = new StatelessExecutor(_weights!, _chatParams!);

        await _inferenceLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await foreach (var token in executor.InferAsync(prompt, inferenceParams, ct)
                .ConfigureAwait(false))
            {
                if (ct.IsCancellationRequested) yield break;
                yield return token;
            }
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder(1024);

        await foreach (var token in StreamChatAsync(messages, options, ct).ConfigureAwait(false))
        {
            sb.Append(token);
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        string modelName,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await EnsureModelLoadedAsync(ct).ConfigureAwait(false);

        if (_embedder is null)
            throw new InvalidOperationException("Embedding model not loaded.");

        var embeddings = await _embedder.GetEmbeddings(text).ConfigureAwait(false);
        return embeddings[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        string modelName,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await EnsureModelLoadedAsync(ct).ConfigureAwait(false);

        if (_embedder is null)
            throw new InvalidOperationException("Embedding model not loaded.");

        // LLamaEmbedder doesn't support batch natively — process sequentially
        var results = new List<float[]>(texts.Count);
        foreach (var text in texts)
        {
            ct.ThrowIfCancellationRequested();
            var embedding = await _embedder.GetEmbeddings(text).ConfigureAwait(false);
            results.Add(embedding[0]);
        }

        return results.AsReadOnly();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DisposeModel();
        _loadLock.Dispose();
        _inferenceLock.Dispose();

        _logger.Information("LocalLlmProvider disposed");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Model Lifecycle
    // ═══════════════════════════════════════════════════════════════════

    private async Task LoadModelAsync(CancellationToken ct)
    {
        await _loadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_weights is not null) return; // Already loaded

            var modelPath = ModelPath;
            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"GGUF model not found: {modelPath}");

            // Auto-detect GPU layers: if user set 0 (default), try to detect NVIDIA GPU
            var effectiveGpuLayers = _gpuLayers;
            if (effectiveGpuLayers == 0)
            {
                effectiveGpuLayers = DetectRecommendedGpuLayers();
            }

            _logger.Information(
                "Loading local LLM from {ModelPath} (GPU layers: {GpuLayers})...",
                modelPath, effectiveGpuLayers);

            // Chat parameters
            _chatParams = new ModelParams(modelPath)
            {
                ContextSize = (uint)_contextSize,
                GpuLayerCount = effectiveGpuLayers
            };

            // Load weights (shared between chat and embeddings)
            _weights = await LLamaWeights.LoadFromFileAsync(
                _chatParams, ct,
                new Progress<float>(p =>
                    _logger.Debug("Model loading: {Percent:P0}", p)))
                .ConfigureAwait(false);

            // Embedding parameters (same weights, embedding mode)
            _embeddingParams = new ModelParams(modelPath)
            {
                ContextSize = 512, // Smaller context for embeddings
                GpuLayerCount = effectiveGpuLayers,
                Embeddings = true
            };

            _embedder = new LLamaEmbedder(_weights, _embeddingParams);

            _isAvailable = true;
            _logger.Information("Local LLM loaded successfully — context: {ContextSize}, embeddings ready",
                _contextSize);
        }
        catch (Exception ex)
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

    private async Task EnsureModelLoadedAsync(CancellationToken ct)
    {
        if (_weights is not null) return;
        await LoadModelAsync(ct).ConfigureAwait(false);
    }

    private void DisposeModel()
    {
        _embedder?.Dispose();
        _embedder = null;

        _weights?.Dispose();
        _weights = null;

        _isAvailable = false;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Prompt Formatting (Llama 3 Instruct Template)
    // ═══════════════════════════════════════════════════════════════════

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
    /// Resolves the HuggingFace download URL for a known GGUF model.
    /// </summary>
    private static string? ResolveDownloadUrl(string modelFileName)
    {
        // Map known model filenames to their HuggingFace download URLs
        return modelFileName.ToLowerInvariant() switch
        {
            "llama-3.2-3b-instruct-q4_k_m.gguf" =>
                "https://huggingface.co/hugging-quants/Llama-3.2-3B-Instruct-Q4_K_M-GGUF/resolve/main/llama-3.2-3b-instruct-q4_k_m.gguf",
            "llama-3.2-1b-instruct-q4_k_m.gguf" =>
                "https://huggingface.co/hugging-quants/Llama-3.2-1B-Instruct-Q4_K_M-GGUF/resolve/main/llama-3.2-1b-instruct-q4_k_m.gguf",
            _ => null
        };
    }

    /// <summary>
    /// Detects NVIDIA GPU via WMI and returns recommended GPU layer count.
    /// Falls back to 0 (CPU-only) on any failure.
    /// </summary>
    private int DetectRecommendedGpuLayers()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT Name, AdapterRAM FROM Win32_VideoController");
            using var results = searcher.Get();

            foreach (System.Management.ManagementObject gpu in results)
            {
                try
                {
                    var name = gpu["Name"]?.ToString() ?? "";
                    if (!name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var adapterRam = gpu["AdapterRAM"];
                    long vramBytes = adapterRam is not null ? Convert.ToInt64(adapterRam) : 0;
                    if (vramBytes < 0) vramBytes += 4_294_967_296L;

                    var layers = vramBytes switch
                    {
                        < 2_000_000_000L => 0,
                        < 4_000_000_000L => 16,
                        < 6_000_000_000L => 28,
                        < 8_000_000_000L => 33,
                        _ => 33
                    };

                    _logger.Information(
                        "NVIDIA GPU detected: {GpuName} ({Vram:F1} GB) — auto-setting {Layers} GPU layers",
                        name, vramBytes / 1_000_000_000.0, layers);

                    return layers;
                }
                finally
                {
                    gpu.Dispose();
                }
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
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
