using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgentX.Core.AI.Models;
using AgentX.Core.Constants;
using Serilog;

namespace AgentX.Core.AI.Providers;

/// <summary>
/// AI provider implementation backed by the OpenAI Chat Completions API.
/// Uses raw HttpClient to avoid additional SDK dependencies. Supports
/// streaming responses via Server-Sent Events (SSE).
/// </summary>
public sealed class OpenAiProvider : IAiProvider
{
    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private bool _isAvailable;
    private bool _disposed;

    /// <inheritdoc />
    public string ProviderId => "openai";

    /// <inheritdoc />
    public string DisplayName => "OpenAI";

    /// <inheritdoc />
    public bool IsAvailable => _isAvailable;

    /// <summary>
    /// Creates a new OpenAiProvider targeting the specified endpoint with the given API key.
    /// </summary>
    /// <param name="apiKey">The OpenAI API key for authentication.</param>
    /// <param name="endpoint">The base API endpoint (e.g. https://api.openai.com/v1/).</param>
    /// <param name="logger">Serilog logger for diagnostics.</param>
    public OpenAiProvider(string apiKey, string endpoint, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key cannot be null or empty.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));

        _logger = (logger ?? Log.Logger).ForContext<OpenAiProvider>();

        // Ensure endpoint ends with a trailing slash for proper URI resolution
        if (!endpoint.EndsWith('/'))
            endpoint += "/";

        _http = new HttpClient
        {
            BaseAddress = new Uri(endpoint),
            Timeout = AppConstants.StreamingResponseTimeout // Long timeout for streaming responses
        };
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        _logger.Information("OpenAiProvider created targeting {Endpoint}", endpoint);
    }

    /// <inheritdoc />
    public async Task<bool> CheckConnectionAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        try
        {
            _logger.Debug("Checking OpenAI connection...");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(AppConstants.OpenAiCheckTimeout);

            var response = await _http.GetAsync("models", timeoutCts.Token).ConfigureAwait(false);
            _isAvailable = response.IsSuccessStatusCode;

            _logger.Information("OpenAI connection check: {IsAvailable} (status: {StatusCode})",
                _isAvailable, response.StatusCode);

            return _isAvailable;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _isAvailable = false;
            _logger.Warning("OpenAI connection check timed out (10s)");
            return false;
        }
        catch (Exception ex)
        {
            _isAvailable = false;
            _logger.Warning(ex, "OpenAI connection check failed");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        try
        {
            _logger.Debug("Listing OpenAI models...");

            var response = await _http.GetAsync("models", ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)
                .ConfigureAwait(false);

            var models = new List<AiModel>();

            if (json.TryGetProperty("data", out var data))
            {
                foreach (var m in data.EnumerateArray())
                {
                    var id = m.GetProperty("id").GetString() ?? string.Empty;

                    // Filter to chat-capable models only
                    if (IsChatModel(id))
                    {
                        models.Add(new AiModel
                        {
                            Id = id,
                            Name = id,
                            ProviderId = ProviderId,
                            Family = "OpenAI",
                            IsAvailable = true
                        });
                    }
                }
            }

            _logger.Information("Found {Count} OpenAI chat models", models.Count);
            return models.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to list OpenAI models");
            return Array.Empty<AiModel>();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Model pull is not supported for cloud providers. This is a no-op.
    /// </remarks>
    public Task PullModelAsync(
        string modelName,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        _logger.Debug("PullModelAsync called for OpenAI — cloud models do not require pulling");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Model deletion is not supported for cloud providers. This is a no-op.
    /// </remarks>
    public Task DeleteModelAsync(string modelName, CancellationToken ct = default)
    {
        _logger.Debug("DeleteModelAsync called for OpenAI — cloud models cannot be deleted locally");
        return Task.CompletedTask;
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

        var requestMessages = BuildRequestMessages(messages);
        var modelId = options?.ModelId ?? "gpt-4o-mini";

        var body = new Dictionary<string, object>
        {
            ["model"] = modelId,
            ["messages"] = requestMessages,
            ["temperature"] = options?.Temperature ?? 0.7,
            ["max_tokens"] = options?.MaxTokens ?? 2048,
            ["stream"] = true
        };

        if (options?.TopP is > 0 and < 1)
            body["top_p"] = options.TopP;
        if (options?.FrequencyPenalty is not 0)
            body["frequency_penalty"] = options!.FrequencyPenalty;
        if (options?.PresencePenalty is not 0)
            body["presence_penalty"] = options!.PresencePenalty;
        if (options?.StopSequences is { Length: > 0 })
            body["stop"] = options.StopSequences;
        if (options?.ResponseFormat == ResponseFormat.JsonObject)
        {
            // FU-5: prefer json_schema with strict: true when a schema is supplied.
            // OpenAI enforces the schema at decode time, rejecting outputs that
            // miss required fields or violate types — much stronger than the
            // looser json_object mode which only requires syntactically-valid JSON.
            if (!string.IsNullOrWhiteSpace(options.JsonSchema)
                && !string.IsNullOrWhiteSpace(options.JsonSchemaName))
            {
                JsonElement schemaElement;
                try
                {
                    schemaElement = JsonSerializer.Deserialize<JsonElement>(options.JsonSchema);
                }
                catch (JsonException ex)
                {
                    _logger.Warning(ex,
                        "ChatOptions.JsonSchema is not valid JSON; falling back to json_object mode");
                    body["response_format"] = new Dictionary<string, string> { ["type"] = "json_object" };
                    schemaElement = default;
                }

                if (schemaElement.ValueKind == JsonValueKind.Object)
                {
                    body["response_format"] = new
                    {
                        type = "json_schema",
                        json_schema = new
                        {
                            name = options.JsonSchemaName,
                            schema = schemaElement,
                            strict = true
                        }
                    };
                }
            }
            else
            {
                body["response_format"] = new Dictionary<string, string> { ["type"] = "json_object" };
            }
        }

        _logger.Debug("Streaming OpenAI chat with {Count} messages, model={Model}",
            messages.Count, modelId);

        var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json")
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            _logger.Error(ex, "OpenAI streaming request failed for model {Model}", modelId);
            throw;
        }

        // Tracks the most recent finish_reason emitted by the model so we can warn
        // on truncation (P0-6) after the stream ends. OpenAI sends "stop", "length",
        // "content_filter", "tool_calls" — anything other than "stop"/"tool_calls"
        // typically means the response is degraded.
        string? finishReason = null;

        using (response)
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);

                if (string.IsNullOrEmpty(line))
                    continue;

                if (!line.StartsWith("data: ", StringComparison.Ordinal))
                    continue;

                var data = line["data: ".Length..];

                if (data == "[DONE]")
                    break;

                string? text = null;
                try
                {
                    var chunk = JsonSerializer.Deserialize<JsonElement>(data);
                    if (chunk.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var firstChoice = choices[0];
                        if (firstChoice.TryGetProperty("delta", out var delta) &&
                            delta.TryGetProperty("content", out var content))
                        {
                            text = content.GetString();
                        }

                        // Capture finish_reason from the choice (OpenAI emits it on the final chunk).
                        if (firstChoice.TryGetProperty("finish_reason", out var fr) &&
                            fr.ValueKind == JsonValueKind.String)
                        {
                            var reason = fr.GetString();
                            if (!string.IsNullOrEmpty(reason))
                                finishReason = reason;
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.Debug(ex, "Skipping malformed SSE chunk from OpenAI");
                }

                if (!string.IsNullOrEmpty(text))
                    yield return text;
            }
        }

        // P0-6: warn the operator on truncation so silent eval / rerank failures
        // become visible. "stop" and "tool_calls" are healthy terminations; anything
        // else (especially "length") means the response was cut off.
        if (!string.IsNullOrEmpty(finishReason)
            && !string.Equals(finishReason, "stop", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(finishReason, "tool_calls", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning(
                "OpenAI response truncated or filtered: finish_reason={FinishReason}, model={Model}, max_tokens={MaxTokens}. " +
                "Consider raising MaxTokens or inspecting prompt safety settings.",
                finishReason, modelId, options?.MaxTokens ?? 2048);
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

        _logger.Debug("Chat request to OpenAI with {Count} messages", messages.Count);

        var sb = new StringBuilder();

        await foreach (var token in StreamChatAsync(messages, options, ct).ConfigureAwait(false))
        {
            sb.Append(token);
        }

        var result = sb.ToString();
        _logger.Debug("OpenAI chat completed, response length: {Length} characters", result.Length);
        return result;
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
            _logger.Debug("Generating OpenAI embedding with model {Model}, text length: {Length}",
                modelName, text.Length);

            var body = new { model = modelName, input = text };
            var response = await _http.PostAsJsonAsync("embeddings", body, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)
                .ConfigureAwait(false);

            var embedding = json.GetProperty("data")[0].GetProperty("embedding");
            var result = embedding.EnumerateArray().Select(e => e.GetSingle()).ToArray();

            _logger.Debug("Generated OpenAI embedding with {Dimensions} dimensions", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to generate OpenAI embedding with model {Model}", modelName);
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
            _logger.Debug("Generating {Count} OpenAI embeddings with model {Model}", texts.Count, modelName);

            // OpenAI supports batch embedding in a single request
            var body = new { model = modelName, input = texts };
            var response = await _http.PostAsJsonAsync("embeddings", body, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)
                .ConfigureAwait(false);

            var results = new List<float[]>();
            var data = json.GetProperty("data");

            foreach (var item in data.EnumerateArray())
            {
                var embedding = item.GetProperty("embedding");
                results.Add(embedding.EnumerateArray().Select(e => e.GetSingle()).ToArray());
            }

            _logger.Debug("Generated {Count} OpenAI embeddings, each with {Dimensions} dimensions",
                results.Count, results.Count > 0 ? results[0].Length : 0);

            return results.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to generate batch OpenAI embeddings with model {Model}", modelName);
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isAvailable = false;
        _http.Dispose();
        _logger.Debug("OpenAiProvider disposed");
    }

    // ── Private Helpers ─────────────────────────────────────────────

    /// <summary>
    /// Converts the application's ChatMessage list into the OpenAI API message format.
    /// </summary>
    private static List<object> BuildRequestMessages(IReadOnlyList<ChatMessage> messages)
    {
        var result = new List<object>(messages.Count);

        foreach (var msg in messages)
        {
            result.Add(new
            {
                role = msg.Role.ToLowerInvariant(),
                content = msg.Content
            });
        }

        return result;
    }

    /// <summary>
    /// Determines whether a model ID represents a chat-capable model.
    /// </summary>
    private static bool IsChatModel(string modelId)
    {
        // Include GPT models, O-series reasoning models, and ChatGPT models
        return modelId.Contains("gpt", StringComparison.OrdinalIgnoreCase) ||
               modelId.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
               modelId.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
               modelId.StartsWith("o4", StringComparison.OrdinalIgnoreCase) ||
               modelId.Contains("chatgpt", StringComparison.OrdinalIgnoreCase);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OpenAiProvider));
    }
}
