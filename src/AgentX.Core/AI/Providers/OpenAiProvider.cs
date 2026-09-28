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
    private readonly ICostTracker? _costTracker;
    private readonly bool _isOfficialEndpoint;
    private readonly ProviderLifetime _lifetime = new(nameof(OpenAiProvider));
    private bool _isAvailable;

    /// <summary>Model used when neither the request nor the settings name one.</summary>
    public const string DefaultModelId = "gpt-4o-mini";

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
    /// <param name="costTracker">Optional usage recorder; token usage reported by the API is recorded per response.</param>
    public OpenAiProvider(string apiKey, string endpoint, ILogger logger, ICostTracker? costTracker = null)
        : this(apiKey, endpoint, logger, handler: null, costTracker)
    {
    }

    /// <summary>Test seam: uses <paramref name="handler"/> for all HTTP traffic.</summary>
    internal OpenAiProvider(string apiKey, string endpoint, ILogger logger, HttpMessageHandler? handler, ICostTracker? costTracker)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key cannot be null or empty.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));

        _logger = (logger ?? Log.Logger).ForContext<OpenAiProvider>();
        _costTracker = costTracker;

        // Ensure endpoint ends with a trailing slash for proper URI resolution
        if (!endpoint.EndsWith('/'))
            endpoint += "/";

        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(endpoint);
        _http.Timeout = AppConstants.StreamingResponseTimeout; // Long timeout for streaming responses
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        // stream_options is only sent to OpenAI itself: OpenAI-compatible servers behind a custom
        // endpoint may reject unknown fields.
        _isOfficialEndpoint = string.Equals(_http.BaseAddress.Host, "api.openai.com", StringComparison.OrdinalIgnoreCase);

        _logger.Information("OpenAiProvider created targeting {Endpoint}", endpoint);
    }

    /// <summary>
    /// True for reasoning models (o1, o3, o4, gpt-5 families). They take max_completion_tokens
    /// instead of max_tokens and reject non-default temperature, top_p and penalties.
    /// </summary>
    internal static bool IsReasoningModel(string modelId)
    {
        var id = modelId.Trim();
        return id.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
               id.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
               id.StartsWith("o4", StringComparison.OrdinalIgnoreCase) ||
               id.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<bool> CheckConnectionAsync(CancellationToken ct = default)
    {
        _lifetime.Enter();
        try
        {
            _logger.Debug("Checking OpenAI connection...");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(AppConstants.OpenAiCheckTimeout);

            using var response = await _http.GetAsync("models", timeoutCts.Token).ConfigureAwait(false);
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _isAvailable = false;
            _logger.Warning(ex, "OpenAI connection check failed");
            return false;
        }
        finally
        {
            _lifetime.Exit();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
    {
        _lifetime.Enter();
        try
        {
            _logger.Debug("Listing OpenAI models...");

            using var response = await _http.GetAsync("models", ct).ConfigureAwait(false);
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "Failed to list OpenAI models");
            return Array.Empty<AiModel>();
        }
        finally
        {
            _lifetime.Exit();
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
        _logger.Debug("PullModelAsync called for OpenAI - cloud models do not require pulling");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Model deletion is not supported for cloud providers. This is a no-op.
    /// </remarks>
    public Task DeleteModelAsync(string modelName, CancellationToken ct = default)
    {
        _logger.Debug("DeleteModelAsync called for OpenAI - cloud models cannot be deleted locally");
        return Task.CompletedTask;
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
            if (messages is null || messages.Count == 0)
                throw new ArgumentException("Messages list cannot be null or empty.", nameof(messages));

            var modelId = string.IsNullOrWhiteSpace(options?.ModelId) ? DefaultModelId : options.ModelId;
            var body = BuildRequestBody(messages, options, modelId, _isOfficialEndpoint, _logger);

            _logger.Debug("Streaming OpenAI chat with {Count} messages, model={Model}",
                messages.Count, modelId);

            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(body),
                    Encoding.UTF8,
                    "application/json")
            };

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var detail = await ReadErrorDetailAsync(response, ct).ConfigureAwait(false);
                _logger.Error("OpenAI request failed for model {Model}: {Detail}", modelId, detail);
                throw new HttpRequestException(
                    $"OpenAI API request failed ({(int)response.StatusCode} {response.StatusCode}): {detail}",
                    null,
                    response.StatusCode);
            }

            // Tracks the most recent finish_reason emitted by the model so we can warn
            // on truncation (P0-6) after the stream ends. OpenAI sends "stop", "length",
            // "content_filter", "tool_calls"; anything other than "stop"/"tool_calls"
            // typically means the response is degraded.
            string? finishReason = null;
            var usage = new UsageTotals();

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                // ReadLineAsync(ct) observes cancellation while waiting for the next line;
                // EndOfStream would block synchronously on the socket.
                string? line;
                while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
                {
                    if (string.IsNullOrEmpty(line))
                        continue;

                    if (!line.StartsWith("data: ", StringComparison.Ordinal))
                        continue;

                    var data = line["data: ".Length..];

                    if (data == "[DONE]")
                        break;

                    var text = ParseChunk(data, ref finishReason, usage);
                    if (!string.IsNullOrEmpty(text))
                        yield return text;
                }
            }
            finally
            {
                RecordUsage(modelId, usage);
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
        finally
        {
            _lifetime.Exit();
        }
    }

    /// <summary>
    /// Builds the Chat Completions request body. Reasoning models get max_completion_tokens and
    /// no sampling or penalty parameters (they reject non-default values); other models get the
    /// requested temperature, top_p and penalties. Null options send only the defaults.
    /// </summary>
    internal static Dictionary<string, object> BuildRequestBody(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        string modelId,
        bool includeUsage,
        ILogger logger)
    {
        var reasoning = IsReasoningModel(modelId);
        var maxTokens = options?.MaxTokens ?? 2048;

        var body = new Dictionary<string, object>
        {
            ["model"] = modelId,
            ["messages"] = BuildRequestMessages(messages),
            ["stream"] = true
        };

        if (reasoning)
        {
            body["max_completion_tokens"] = maxTokens;
        }
        else
        {
            body["max_tokens"] = maxTokens;
            body["temperature"] = options?.Temperature ?? 0.7;

            if (options is not null)
            {
                if (options.TopP is > 0 and < 1)
                    body["top_p"] = options.TopP;
                if (options.FrequencyPenalty != 0)
                    body["frequency_penalty"] = options.FrequencyPenalty;
                if (options.PresencePenalty != 0)
                    body["presence_penalty"] = options.PresencePenalty;
            }
        }

        if (includeUsage)
            body["stream_options"] = new { include_usage = true };

        if (options?.StopSequences is { Length: > 0 })
            body["stop"] = options.StopSequences;
        if (options?.ResponseFormat == ResponseFormat.JsonObject)
        {
            // FU-5: prefer json_schema with strict: true when a schema is supplied.
            // OpenAI enforces the schema at decode time, rejecting outputs that
            // miss required fields or violate types, much stronger than the
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
                    logger.Warning(ex,
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

        return body;
    }

    private sealed class UsageTotals
    {
        public bool HasUsage { get; set; }
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
    }

    /// <summary>
    /// Parses one streamed chunk. A <c>{"error": ...}</c> payload (sent mid-stream when a
    /// request fails after the HTTP 200) throws instead of being skipped, so a partial answer is
    /// never reported as complete.
    /// </summary>
    private string? ParseChunk(string data, ref string? finishReason, UsageTotals usage)
    {
        JsonElement chunk;
        try
        {
            chunk = JsonSerializer.Deserialize<JsonElement>(data);
        }
        catch (JsonException ex)
        {
            _logger.Debug(ex, "Skipping malformed SSE chunk from OpenAI");
            return null;
        }

        if (chunk.ValueKind != JsonValueKind.Object)
            return null;

        if (chunk.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            throw new HttpRequestException($"OpenAI stream failed: {DescribeError(error)}");

        if (chunk.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object)
        {
            usage.HasUsage = true;
            usage.PromptTokens = ReadInt(usageElement, "prompt_tokens");
            usage.CompletionTokens = ReadInt(usageElement, "completion_tokens");
        }

        string? text = null;
        if (chunk.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
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

        return text;

        static int ReadInt(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
                ? number
                : 0;
    }

    private void RecordUsage(string modelId, UsageTotals usage)
    {
        if (_costTracker is null || !usage.HasUsage)
            return;

        try
        {
            // OpenAI reports cached prompt tokens as part of prompt_tokens; they are billed at a
            // discount, which the cost table does not model, so they are priced as input.
            _costTracker.RecordUsage(modelId, ProviderId, usage.PromptTokens, usage.CompletionTokens);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Could not record OpenAI usage");
        }
    }

    private static string DescribeError(JsonElement error)
    {
        if (error.ValueKind == JsonValueKind.Object)
        {
            var type = error.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            var code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            var kind = code ?? type;
            return string.IsNullOrEmpty(kind) ? message ?? error.GetRawText() : $"{kind}: {message}";
        }

        return error.ValueKind == JsonValueKind.String ? error.GetString() ?? string.Empty : error.GetRawText();
    }

    private static async Task<string> ReadErrorDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string payload;
        try
        {
            payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            payload = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(payload))
            return $"{(int)response.StatusCode} (no details returned)";

        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(payload);
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error", out var error))
                return $"{(int)response.StatusCode} {DescribeError(error)}";
        }
        catch (JsonException)
        {
            // Not JSON; use the raw (truncated) body.
        }

        return $"{(int)response.StatusCode} {(payload.Length > 500 ? payload[..500] : payload)}";
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
        _lifetime.Enter();
        try
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Text cannot be null or empty.", nameof(text));
            if (string.IsNullOrWhiteSpace(modelName))
                throw new ArgumentException("Model name cannot be null or empty.", nameof(modelName));

            _logger.Debug("Generating OpenAI embedding with model {Model}, text length: {Length}",
                modelName, text.Length);

            var body = new { model = modelName, input = text };
            using var response = await _http.PostAsJsonAsync("embeddings", body, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "embedding", ct).ConfigureAwait(false);

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)
                .ConfigureAwait(false);

            var embedding = json.GetProperty("data")[0].GetProperty("embedding");
            var result = embedding.EnumerateArray().Select(e => e.GetSingle()).ToArray();

            _logger.Debug("Generated OpenAI embedding with {Dimensions} dimensions", result.Length);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(ex, "Failed to generate OpenAI embedding with model {Model}", modelName);
            throw;
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
            if (texts is null || texts.Count == 0)
                throw new ArgumentException("Texts list cannot be null or empty.", nameof(texts));
            if (string.IsNullOrWhiteSpace(modelName))
                throw new ArgumentException("Model name cannot be null or empty.", nameof(modelName));

            _logger.Debug("Generating {Count} OpenAI embeddings with model {Model}", texts.Count, modelName);

            // OpenAI supports batch embedding in a single request
            var body = new { model = modelName, input = texts };
            using var response = await _http.PostAsJsonAsync("embeddings", body, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "embedding", ct).ConfigureAwait(false);

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(ex, "Failed to generate batch OpenAI embeddings with model {Model}", modelName);
            throw;
        }
        finally
        {
            _lifetime.Exit();
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var detail = await ReadErrorDetailAsync(response, ct).ConfigureAwait(false);
        throw new HttpRequestException($"OpenAI {operation} request failed: {detail}", null, response.StatusCode);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Deferred: a response that is still streaming keeps the HTTP client until it ends.
        if (!_lifetime.RequestDispose(() => _http.Dispose()))
            return;

        _isAvailable = false;
        _logger.Debug("OpenAiProvider disposed");
    }

    // -- Private Helpers ---------------------------------------------

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
    /// Determines whether a model ID is a chat model usable with the Chat Completions API.
    /// The models endpoint also lists image, speech, transcription, realtime, embedding and
    /// moderation models, and Responses-API-only models, none of which can serve a chat request.
    /// </summary>
    internal static bool IsChatModel(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return false;

        var id = modelId.Trim().ToLowerInvariant();

        var family = id.StartsWith("gpt-", StringComparison.Ordinal) ||
                     id.StartsWith("chatgpt-", StringComparison.Ordinal) ||
                     id.StartsWith("o1", StringComparison.Ordinal) ||
                     id.StartsWith("o3", StringComparison.Ordinal) ||
                     id.StartsWith("o4", StringComparison.Ordinal);
        if (!family)
            return false;

        return !NonChatModelMarkers.Any(marker => id.Contains(marker, StringComparison.Ordinal));
    }

    private static readonly string[] NonChatModelMarkers =
    [
        "image", "dall-e", "tts", "transcribe", "whisper", "audio", "realtime", "embedding",
        "moderation", "instruct", "search", "-pro", "deep-research", "codex", "computer-use"
    ];

    private void ThrowIfDisposed()
    {
        if (_lifetime.IsDisposeRequested)
            throw new ObjectDisposedException(nameof(OpenAiProvider));
    }
}
