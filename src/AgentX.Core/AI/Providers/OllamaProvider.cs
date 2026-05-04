using System.Runtime.CompilerServices;
using System.Text;
using AgentX.Core.AI.Models;
using AgentX.Core.Constants;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
using Serilog;
using ChatMessage = AgentX.Core.AI.Models.ChatMessage;
using ChatOptions = AgentX.Core.AI.Models.ChatOptions;
using OllamaChatRole = OllamaSharp.Models.Chat.ChatRole;
using OllamaMessage = OllamaSharp.Models.Chat.Message;

namespace AgentX.Core.AI.Providers;

/// <summary>
/// AI provider implementation backed by Ollama via the OllamaSharp 4.0.x client library.
/// Communicates with a running Ollama server over HTTP for model management,
/// chat inference, and embedding generation.
/// </summary>
public sealed class OllamaProvider : IAiProvider
{
    private readonly OllamaApiClient _client;
    private readonly ILogger _logger;
    private bool _isAvailable;
    private bool _disposed;

    /// <inheritdoc />
    public string ProviderId => "ollama";

    /// <inheritdoc />
    public string DisplayName => "Ollama";

    /// <inheritdoc />
    public bool IsAvailable => _isAvailable;

    /// <summary>
    /// Creates a new OllamaProvider instance targeting the specified endpoint.
    /// </summary>
    /// <param name="endpoint">The Ollama server URI (e.g. http://localhost:11434).</param>
    /// <param name="logger">Serilog logger for diagnostics.</param>
    public OllamaProvider(Uri endpoint, ILogger logger)
    {
        _client = new OllamaApiClient(endpoint);
        _logger = logger ?? Log.Logger;
        _logger.Information("OllamaProvider created targeting {Endpoint}", endpoint);
    }

    /// <summary>
    /// Creates a new OllamaProvider with the default endpoint (http://localhost:11434).
    /// </summary>
    /// <param name="logger">Serilog logger for diagnostics.</param>
    public OllamaProvider(ILogger logger)
        : this(new Uri("http://localhost:11434"), logger)
    {
    }

    /// <inheritdoc />
    public async Task<bool> CheckConnectionAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        try
        {
            _logger.Debug("Checking Ollama connection...");

            // Use a short timeout so the app doesn't hang when Ollama isn't running.
            // The default HttpClient timeout is ~100s which is far too long for a health check.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(AppConstants.OllamaCheckTimeout);

            var running = await _client.IsRunningAsync(timeoutCts.Token).ConfigureAwait(false);
            _isAvailable = running;
            _logger.Information("Ollama connection check: {IsAvailable}", _isAvailable);
            return _isAvailable;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout expired (not caller cancellation) — Ollama is not responding
            _isAvailable = false;
            _logger.Warning("Ollama connection check timed out (3s)");
            return false;
        }
        catch (Exception ex)
        {
            _isAvailable = false;
            _logger.Warning(ex, "Ollama connection check failed");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        try
        {
            _logger.Debug("Listing local Ollama models...");
            var ollamaModels = await _client.ListLocalModelsAsync(ct).ConfigureAwait(false);
            var models = new List<AiModel>();

            foreach (var m in ollamaModels)
            {
                var aiModel = MapToAiModel(m);
                models.Add(aiModel);
            }

            _logger.Information("Found {Count} local Ollama models", models.Count);
            return models.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to list Ollama models");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task PullModelAsync(
        string modelName,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("Model name cannot be null or empty.", nameof(modelName));

        try
        {
            _logger.Information("Pulling Ollama model: {ModelName}", modelName);

            await foreach (var status in _client.PullModelAsync(modelName, ct).ConfigureAwait(false))
            {
                if (status is null)
                    continue;

                progress?.Report(new ModelDownloadProgress
                {
                    ModelId = modelName,
                    Status = status.Status ?? "Downloading",
                    CompletedBytes = status.Completed,
                    TotalBytes = status.Total
                });
            }

            _logger.Information("Successfully pulled model: {ModelName}", modelName);
        }
        catch (OperationCanceledException)
        {
            _logger.Information("Model pull cancelled: {ModelName}", modelName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to pull model: {ModelName}", modelName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteModelAsync(string modelName, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("Model name cannot be null or empty.", nameof(modelName));

        try
        {
            _logger.Information("Deleting Ollama model: {ModelName}", modelName);
            await _client.DeleteModelAsync(modelName, ct).ConfigureAwait(false);
            _logger.Information("Successfully deleted model: {ModelName}", modelName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete model: {ModelName}", modelName);
            throw;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (messages is null || messages.Count == 0)
            throw new ArgumentException("Messages list cannot be null or empty.", nameof(messages));

        var chatRequest = BuildChatRequest(messages, options, stream: true);

        _logger.Debug("Streaming chat with {MessageCount} messages, model={Model}",
            messages.Count, chatRequest.Model);

        IAsyncEnumerable<ChatResponseStream?> responseStream;

        try
        {
            responseStream = _client.ChatAsync(chatRequest, ct);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initiate streaming chat");
            throw;
        }

        // Tracks Ollama's done_reason (P0-6). The terminal chunk is typed as
        // ChatDoneResponseStream and exposes DoneReason: "stop" (natural), "length"
        // (max tokens), "load", "unload". Anything other than "stop" indicates a
        // degraded response.
        string? doneReason = null;

        await foreach (var chunk in responseStream.WithCancellation(ct).ConfigureAwait(false))
        {
            if (chunk is ChatDoneResponseStream done)
            {
                doneReason = done.DoneReason;
            }

            var token = chunk?.Message?.Content;
            if (!string.IsNullOrEmpty(token))
            {
                yield return token;
            }
        }

        if (!string.IsNullOrEmpty(doneReason)
            && !string.Equals(doneReason, "stop", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning(
                "Ollama response truncated: done_reason={DoneReason}, model={Model}, max_tokens={MaxTokens}. " +
                "Consider raising MaxTokens.",
                doneReason, chatRequest.Model, options?.MaxTokens ?? 2048);
        }
    }

    /// <inheritdoc />
    public async Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (messages is null || messages.Count == 0)
            throw new ArgumentException("Messages list cannot be null or empty.", nameof(messages));

        var chatRequest = BuildChatRequest(messages, options, stream: true);

        _logger.Debug("Chat request with {MessageCount} messages, model={Model}",
            messages.Count, chatRequest.Model);

        try
        {
            var sb = new StringBuilder();
            string? doneReason = null;

            await foreach (var chunk in _client.ChatAsync(chatRequest, ct).ConfigureAwait(false))
            {
                if (chunk is ChatDoneResponseStream done)
                {
                    doneReason = done.DoneReason;
                }

                var token = chunk?.Message?.Content;
                if (!string.IsNullOrEmpty(token))
                {
                    sb.Append(token);
                }
            }

            var result = sb.ToString();

            if (!string.IsNullOrEmpty(doneReason)
                && !string.Equals(doneReason, "stop", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warning(
                    "Ollama response truncated: done_reason={DoneReason}, model={Model}, max_tokens={MaxTokens}. " +
                    "Consider raising MaxTokens.",
                    doneReason, chatRequest.Model, options?.MaxTokens ?? 2048);
            }

            _logger.Debug("Chat completed, response length: {Length} characters", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Chat request failed");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        string modelName,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be null or empty.", nameof(text));
        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("Model name cannot be null or empty.", nameof(modelName));

        try
        {
            _logger.Debug("Generating embedding with model {Model}, text length: {Length}",
                modelName, text.Length);

            // Set the selected model for the embed request
            _client.SelectedModel = modelName;

            var response = await _client.EmbedAsync(text, ct).ConfigureAwait(false);

            if (response.Embeddings is null || response.Embeddings.Count == 0)
            {
                _logger.Warning("Embedding response returned no embeddings for model {Model}", modelName);
                return Array.Empty<float>();
            }

            // OllamaSharp 4.0.x returns List<float[]> from EmbedResponse.Embeddings
            var embedding = response.Embeddings[0];
            _logger.Debug("Generated embedding with {Dimensions} dimensions", embedding.Length);
            return embedding;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to generate embedding with model {Model}", modelName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        string modelName,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (texts is null || texts.Count == 0)
            throw new ArgumentException("Texts list cannot be null or empty.", nameof(texts));
        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("Model name cannot be null or empty.", nameof(modelName));

        try
        {
            _logger.Debug("Generating {Count} embeddings with model {Model}", texts.Count, modelName);

            var request = new EmbedRequest
            {
                Model = modelName,
                Input = texts.ToList()
            };

            var response = await _client.EmbedAsync(request, ct).ConfigureAwait(false);

            if (response.Embeddings is null || response.Embeddings.Count == 0)
            {
                _logger.Warning("Batch embedding response returned no embeddings for model {Model}", modelName);
                return Array.Empty<float[]>();
            }

            // EmbedResponse.Embeddings is List<float[]> in OllamaSharp 4.0.x
            var result = new List<float[]>(response.Embeddings.Count);
            foreach (var embedding in response.Embeddings)
            {
                result.Add(embedding);
            }

            _logger.Debug("Generated {Count} embeddings, each with {Dimensions} dimensions",
                result.Count, result.Count > 0 ? result[0].Length : 0);

            return result.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to generate batch embeddings with model {Model}", modelName);
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isAvailable = false;
        _logger.Debug("OllamaProvider disposed");
    }

    // ── Private Helpers ─────────────────────────────────────────────

    /// <summary>
    /// Builds an OllamaSharp ChatRequest from the application's ChatMessage list and options.
    /// </summary>
    private ChatRequest BuildChatRequest(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        bool stream)
    {
        var ollamaMessages = new List<OllamaMessage>(messages.Count);

        foreach (var msg in messages)
        {
            var role = MapRole(msg.Role);
            ollamaMessages.Add(new OllamaMessage(role, msg.Content));
        }

        var modelId = options?.ModelId ?? _client.SelectedModel;

        var request = new ChatRequest
        {
            Model = modelId ?? string.Empty,
            Messages = ollamaMessages,
            Stream = stream,
            Options = BuildRequestOptions(options),
            Format = options?.ResponseFormat == ResponseFormat.JsonObject ? "json" : null
        };

        return request;
    }

    /// <summary>
    /// Maps ChatOptions to OllamaSharp's RequestOptions for model inference parameters.
    /// </summary>
    private static RequestOptions? BuildRequestOptions(ChatOptions? options)
    {
        if (options is null)
            return null;

        var requestOptions = new RequestOptions
        {
            Temperature = (float)options.Temperature,
            NumPredict = options.MaxTokens,
            NumCtx = options.ContextWindow,
            TopP = (float)options.TopP,
            FrequencyPenalty = (float)options.FrequencyPenalty,
            PresencePenalty = (float)options.PresencePenalty
        };

        if (options.StopSequences is { Length: > 0 })
        {
            requestOptions.Stop = options.StopSequences;
        }

        return requestOptions;
    }

    /// <summary>
    /// Maps a role string (system/user/assistant/tool) to OllamaSharp's ChatRole.
    /// </summary>
    private static OllamaChatRole MapRole(string role)
    {
        return role.ToLowerInvariant() switch
        {
            "system" => OllamaChatRole.System,
            "user" => OllamaChatRole.User,
            "assistant" => OllamaChatRole.Assistant,
            "tool" => OllamaChatRole.Tool,
            _ => OllamaChatRole.User
        };
    }

    /// <summary>
    /// Maps an OllamaSharp Model to the application's AiModel.
    /// </summary>
    private static AiModel MapToAiModel(OllamaSharp.Models.Model model)
    {
        var name = model.Name ?? string.Empty;

        return new AiModel
        {
            Id = name,
            Name = name,
            Family = model.Details?.Family ?? string.Empty,
            SizeBytes = model.Size,
            QuantizationLevel = model.Details?.QuantizationLevel ?? string.Empty,
            Digest = model.Digest ?? string.Empty,
            ModifiedAt = model.ModifiedAt,
            ParameterCount = ParseParameterCount(model.Details?.ParameterSize),
            ContextLength = 0 // Context length is not exposed by the list endpoint
        };
    }

    /// <summary>
    /// Parses a parameter size string (e.g. "7B", "3.8B", "70B") into an integer
    /// representing millions of parameters (e.g. 7000 for 7B).
    /// </summary>
    private static int ParseParameterCount(string? parameterSize)
    {
        if (string.IsNullOrWhiteSpace(parameterSize))
            return 0;

        var cleaned = parameterSize.Trim().ToUpperInvariant();

        if (cleaned.EndsWith("B"))
        {
            if (double.TryParse(cleaned[..^1], out var billions))
                return (int)(billions * 1000); // Convert to millions for display
        }
        else if (cleaned.EndsWith("M"))
        {
            if (double.TryParse(cleaned[..^1], out var millions))
                return (int)millions;
        }

        return 0;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OllamaProvider));
    }
}
