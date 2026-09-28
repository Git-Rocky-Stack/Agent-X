using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgentX.Core.AI.Models;
using AgentX.Core.Constants;
using Serilog;

namespace AgentX.Core.AI.Providers;

/// <summary>
/// AI provider implementation backed by the Anthropic Messages API.
/// Uses raw HttpClient with Server-Sent Events (SSE) for streaming responses.
/// Anthropic uses a distinct API format: system prompt is a top-level field,
/// and streaming events use typed event blocks.
/// </summary>
public sealed class AnthropicProvider : IAiProvider
{
    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly ICostTracker? _costTracker;
    private readonly ProviderLifetime _lifetime = new(nameof(AnthropicProvider));
    private bool _isAvailable;

    private const string AnthropicApiVersion = "2023-06-01";

    /// <summary>Model used when neither the request nor the settings name one.</summary>
    public const string DefaultModelId = "claude-sonnet-5";

    /// <inheritdoc />
    public string ProviderId => "anthropic";

    /// <inheritdoc />
    public string DisplayName => "Anthropic Claude";

    /// <inheritdoc />
    public bool IsAvailable => _isAvailable;

    /// <summary>
    /// Fallback catalog used when the Models API (GET /v1/models) cannot be reached. The live
    /// list from the API is preferred because model ids change faster than releases of this app.
    /// </summary>
    private static readonly List<(string Id, string Name)> KnownModels = new()
    {
        ("claude-opus-5-5", "Claude Opus 5.5"),
        ("claude-sonnet-5", "Claude Sonnet 5"),
        ("claude-haiku-4-5-20251001", "Claude Haiku 4.5"),
    };

    /// <summary>
    /// Models that still accept a sampling temperature. Newer models reject sampling parameters
    /// with a 400 (Claude 4.7 and later Opus models, Sonnet 5, Fable), so for any model not
    /// listed here no sampling parameter is sent at all.
    /// </summary>
    private static readonly string[] TemperatureCapableModelPrefixes =
    [
        "claude-3",
        "claude-haiku-4",
        "claude-sonnet-4",
        "claude-opus-4-0",
        "claude-opus-4-1",
        "claude-opus-4-5",
        "claude-opus-4-6",
        "claude-opus-4-2025",
    ];

    /// <summary>
    /// Models that reject forced tool use (tool_choice "tool"/"any") with a 400. For these the
    /// structured-output request falls back to JSON mode with the schema in the instructions.
    /// </summary>
    private static readonly string[] NoForcedToolUseModelPrefixes =
    [
        "claude-opus-5-5",
        "claude-fable-5-1",
        "claude-mythos-5-1",
    ];

    /// <summary>
    /// Creates a new AnthropicProvider targeting the specified endpoint with the given API key.
    /// </summary>
    /// <param name="apiKey">The Anthropic API key for authentication.</param>
    /// <param name="endpoint">The base API endpoint (e.g. https://api.anthropic.com/v1/).</param>
    /// <param name="logger">Serilog logger for diagnostics.</param>
    /// <param name="costTracker">Optional usage recorder; token usage reported by the API is recorded per response.</param>
    public AnthropicProvider(string apiKey, string endpoint, ILogger logger, ICostTracker? costTracker = null)
        : this(apiKey, endpoint, logger, handler: null, costTracker)
    {
    }

    /// <summary>Test seam: uses <paramref name="handler"/> for all HTTP traffic.</summary>
    internal AnthropicProvider(string apiKey, string endpoint, ILogger logger, HttpMessageHandler? handler, ICostTracker? costTracker)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key cannot be null or empty.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));

        _logger = (logger ?? Log.Logger).ForContext<AnthropicProvider>();
        _costTracker = costTracker;

        // Ensure endpoint ends with a trailing slash for proper URI resolution
        if (!endpoint.EndsWith('/'))
            endpoint += "/";

        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(endpoint);
        _http.Timeout = AppConstants.StreamingResponseTimeout; // Long timeout for streaming responses
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", AnthropicApiVersion);

        _logger.Information("AnthropicProvider created targeting {Endpoint}", endpoint);
    }

    /// <summary>
    /// True when <paramref name="modelId"/> accepts a sampling temperature. Newer models reject
    /// sampling parameters, so unknown models are treated as not accepting one.
    /// </summary>
    internal static bool AcceptsTemperature(string modelId) =>
        TemperatureCapableModelPrefixes.Any(p => modelId.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when <paramref name="modelId"/> accepts tool_choice "tool".</summary>
    internal static bool SupportsForcedToolUse(string modelId) =>
        !NoForcedToolUseModelPrefixes.Any(p => modelId.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    /// <remarks>
    /// Uses the Models API (GET /v1/models): it verifies the key without generating (and
    /// billing) tokens. A 429 still proves the key is valid.
    /// </remarks>
    public async Task<bool> CheckConnectionAsync(CancellationToken ct = default)
    {
        _lifetime.Enter();
        try
        {
            _logger.Debug("Checking Anthropic connection...");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(AppConstants.AnthropicCheckTimeout);

            using var response = await _http.GetAsync("models?limit=1", timeoutCts.Token).ConfigureAwait(false);

            // 200 = success, 401 = bad key, 429 = rate limited (but key is valid)
            _isAvailable = response.IsSuccessStatusCode ||
                           response.StatusCode == System.Net.HttpStatusCode.TooManyRequests;

            if (_isAvailable)
            {
                _logger.Information("Anthropic connection check: {IsAvailable} (status: {StatusCode})",
                    _isAvailable, response.StatusCode);
            }
            else
            {
                var detail = await ReadErrorDetailAsync(response, timeoutCts.Token).ConfigureAwait(false);
                _logger.Warning("Anthropic connection check failed: {Detail}", detail);
            }

            return _isAvailable;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _isAvailable = false;
            _logger.Warning("Anthropic connection check timed out (10s)");
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _isAvailable = false;
            _logger.Warning(ex, "Anthropic connection check failed");
            return false;
        }
        finally
        {
            _lifetime.Exit();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Lists the models available to the API key through the Models API, falling back to a
    /// static catalog of current models when the list cannot be fetched.
    /// </remarks>
    public async Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
    {
        _lifetime.Enter();
        try
        {
            _logger.Debug("Listing Anthropic Claude models...");

            try
            {
                using var response = await _http.GetAsync("models?limit=100", ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)
                        .ConfigureAwait(false);

                    var listed = new List<AiModel>();
                    if (json.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in data.EnumerateArray())
                        {
                            var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                            if (string.IsNullOrWhiteSpace(id))
                                continue;

                            var name = item.TryGetProperty("display_name", out var nameElement)
                                ? nameElement.GetString()
                                : null;
                            listed.Add(ToAiModel(id, string.IsNullOrWhiteSpace(name) ? id : name));
                        }
                    }

                    if (listed.Count > 0)
                    {
                        _logger.Information("Listed {Count} Anthropic models from the Models API", listed.Count);
                        return listed.AsReadOnly();
                    }
                }
                else
                {
                    var detail = await ReadErrorDetailAsync(response, ct).ConfigureAwait(false);
                    _logger.Warning("Anthropic model list failed ({Detail}); using the built-in catalog", detail);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Warning(ex, "Anthropic model list failed; using the built-in catalog");
            }

            var models = KnownModels.Select(m => ToAiModel(m.Id, m.Name)).ToList();
            _logger.Information("Returned {Count} known Anthropic models", models.Count);
            return models.AsReadOnly();
        }
        finally
        {
            _lifetime.Exit();
        }
    }

    private AiModel ToAiModel(string id, string name) => new()
    {
        Id = id,
        Name = name,
        ProviderId = ProviderId,
        Family = "Claude",
        IsAvailable = true
    };

    /// <inheritdoc />
    /// <remarks>
    /// Model pull is not supported for cloud providers. This is a no-op.
    /// </remarks>
    public Task PullModelAsync(
        string modelName,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        _logger.Debug("PullModelAsync called for Anthropic - cloud models do not require pulling");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Model deletion is not supported for cloud providers. This is a no-op.
    /// </remarks>
    public Task DeleteModelAsync(string modelName, CancellationToken ct = default)
    {
        _logger.Debug("DeleteModelAsync called for Anthropic - cloud models cannot be deleted locally");
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
            var (body, useToolForcing, hasSystem, messageCount) = BuildRequestBody(messages, options, modelId);

            _logger.Debug(
                "Streaming Anthropic chat with {Count} messages, model={Model}, system={HasSystem}, toolForcing={Tool}",
                messageCount, modelId, hasSystem, useToolForcing);

            using var request = new HttpRequestMessage(HttpMethod.Post, "messages")
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
                // The error body names the problem (invalid parameter, unknown model, bad key);
                // EnsureSuccessStatusCode would discard it.
                var detail = await ReadErrorDetailAsync(response, ct).ConfigureAwait(false);
                _logger.Error("Anthropic request failed for model {Model}: {Detail}", modelId, detail);
                throw new HttpRequestException(
                    $"Anthropic API request failed ({(int)response.StatusCode} {response.StatusCode}): {detail}",
                    null,
                    response.StatusCode);
            }

            var stream = new SseState();
            try
            {
                await using var content = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(content, Encoding.UTF8);

                // ReadLineAsync(ct) observes cancellation while waiting for the next line;
                // EndOfStream would block synchronously on the socket.
                string? line;
                while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
                {
                    var text = ProcessSseLine(line, stream, useToolForcing);
                    if (stream.Completed)
                        break;

                    if (!string.IsNullOrEmpty(text))
                        yield return text;
                }
            }
            finally
            {
                RecordUsage(modelId, stream);
            }

            // P0-6: surface truncation. "end_turn" / "stop_sequence" / "tool_use" are healthy.
            var stopReason = stream.StopReason;
            if (!string.IsNullOrEmpty(stopReason)
                && !string.Equals(stopReason, "end_turn", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(stopReason, "stop_sequence", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(stopReason, "tool_use", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warning(
                    "Anthropic response truncated or filtered: stop_reason={StopReason}, model={Model}, max_tokens={MaxTokens}. " +
                    "Consider raising MaxTokens or inspecting prompt safety settings.",
                    stopReason, modelId, options?.MaxTokens ?? 2048);
            }
        }
        finally
        {
            _lifetime.Exit();
        }
    }

    /// <summary>
    /// Builds the Messages API request body. At most one sampling parameter is sent: temperature,
    /// clamped to Anthropic's [0, 1] range, and only for models that still accept it. top_p is
    /// never sent (Claude 4 models reject temperature and top_p together).
    /// </summary>
    internal static (Dictionary<string, object> Body, bool UseToolForcing, bool HasSystem, int MessageCount) BuildRequestBody(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        string modelId)
    {
        // Anthropic requires the system prompt as a top-level field, not a message.
        // Extract any system messages from the list and combine them.
        var (systemPrompt, apiMessages) = ExtractSystemPromptAndMessages(messages);

        var body = new Dictionary<string, object>
        {
            ["model"] = modelId,
            ["messages"] = apiMessages,
            ["max_tokens"] = options?.MaxTokens ?? 2048,
            ["stream"] = true
        };

        var jsonMode = options?.ResponseFormat == ResponseFormat.JsonObject;

        // FU-5 part 2: tool-use forcing function for strict structured outputs.
        // Anthropic doesn't have a json_schema response_format like OpenAI. The
        // canonical workaround is to define a single tool whose input_schema is
        // the desired schema, then set tool_choice to force the model to call
        // that tool. The tool's input field is then schema-validated by the
        // API. We yield the tool's input as the response body so client-side
        // parsers (RagEvaluator/LlmReranker/ContextualCompressor) work unchanged.
        // Models that reject forced tool use get JSON mode with the schema in the
        // instructions instead of a guaranteed 400.
        var useToolForcing = false;
        JsonElement schemaElement = default;
        string? schemaInstruction = null;
        if (!string.IsNullOrWhiteSpace(options?.JsonSchema))
        {
            try
            {
                schemaElement = JsonSerializer.Deserialize<JsonElement>(options!.JsonSchema!);
            }
            catch (JsonException)
            {
                schemaElement = default;
            }

            if (schemaElement.ValueKind == JsonValueKind.Object)
            {
                if (SupportsForcedToolUse(modelId))
                {
                    useToolForcing = true;
                }
                else
                {
                    jsonMode = true;
                    schemaInstruction = "The JSON object must match this JSON Schema: " + options!.JsonSchema!.Trim();
                }
            }
        }

        const string jsonInstruction =
            "You MUST respond with valid JSON only. No markdown, no explanation, no text outside the JSON.";
        var jsonReinforcement = schemaInstruction is null
            ? "IMPORTANT: " + jsonInstruction
            : "IMPORTANT: " + jsonInstruction + " " + schemaInstruction;

        // FU-1: multi-block system prompt path. When the caller supplies
        // SystemPromptBlocks, each block is emitted as its own typed text
        // segment with optional per-block cache_control. This is the path
        // used by RagPipeline so its stable instruction prefix can be cached
        // separately from the per-question retrieved context.
        var blocks = options?.SystemPromptBlocks;
        var hasSystem = !string.IsNullOrEmpty(systemPrompt);
        if (blocks is { Count: > 0 })
        {
            var systemArray = new List<object>(blocks.Count + 1);
            foreach (var block in blocks)
            {
                if (string.IsNullOrEmpty(block.Text)) continue;
                if (block.Cacheable)
                {
                    systemArray.Add(new
                    {
                        type = "text",
                        text = block.Text,
                        cache_control = new { type = "ephemeral" }
                    });
                }
                else
                {
                    systemArray.Add(new { type = "text", text = block.Text });
                }
            }

            // JSON-mode reinforcement is appended as a NON-CACHED block so the
            // cacheable prefix preceding it remains cache-eligible.
            if (jsonMode)
            {
                systemArray.Add(new { type = "text", text = jsonReinforcement });
            }

            if (systemArray.Count > 0)
            {
                body["system"] = systemArray;
                hasSystem = true;
            }
        }
        else
        {
            // Single-block path (unchanged from P1-1).
            if (jsonMode && !string.IsNullOrEmpty(systemPrompt))
                systemPrompt += "\n\n" + jsonReinforcement;
            else if (jsonMode)
                systemPrompt = schemaInstruction is null ? jsonInstruction : jsonInstruction + " " + schemaInstruction;

            if (!string.IsNullOrEmpty(systemPrompt))
            {
                hasSystem = true;
                if (options?.CacheSystemPrompt == true)
                {
                    body["system"] = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = systemPrompt,
                            cache_control = new { type = "ephemeral" }
                        }
                    };
                }
                else
                {
                    body["system"] = systemPrompt;
                }
            }
        }

        if (options is not null && AcceptsTemperature(modelId))
            body["temperature"] = Math.Clamp(options.Temperature, 0.0, 1.0);
        if (options?.StopSequences is { Length: > 0 })
            body["stop_sequences"] = options.StopSequences;

        if (useToolForcing)
        {
            var forcedToolName = string.IsNullOrWhiteSpace(options?.JsonSchemaName)
                ? "structured_output"
                : options!.JsonSchemaName!;

            body["tools"] = new object[]
            {
                new
                {
                    name = forcedToolName,
                    description = "Return the answer in the structured form specified by input_schema.",
                    input_schema = schemaElement
                }
            };
            body["tool_choice"] = new { type = "tool", name = forcedToolName };
        }

        return (body, useToolForcing, hasSystem, apiMessages.Count);
    }

    /// <summary>Per-response streaming state: event type, usage and stop reason.</summary>
    private sealed class SseState
    {
        public string? CurrentEventType { get; set; }
        public string? StopReason { get; set; }
        public bool Completed { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int CacheCreationInputTokens { get; set; }
        public int CacheReadInputTokens { get; set; }
        public bool HasUsage { get; set; }
    }

    /// <summary>
    /// Handles one SSE line and returns the text to emit, if any. An <c>error</c> event (for
    /// example overloaded_error after the HTTP 200) throws, so a partial answer is never
    /// reported as a complete one.
    /// </summary>
    private string? ProcessSseLine(string line, SseState state, bool useToolForcing)
    {
        if (line.Length == 0)
        {
            state.CurrentEventType = null;
            return null;
        }

        // Parse SSE event type
        if (line.StartsWith("event: ", StringComparison.Ordinal))
        {
            state.CurrentEventType = line["event: ".Length..];
            return null;
        }

        // Parse SSE data
        if (!line.StartsWith("data: ", StringComparison.Ordinal))
            return null;

        var data = line["data: ".Length..];

        switch (state.CurrentEventType)
        {
            case "content_block_delta":
                // In text mode we read text_delta; in tool-use forcing mode (FU-5 part 2) we read
                // input_json_delta and yield the partial JSON tokens as the response. Thinking
                // deltas of models that think by default are not part of the answer.
                try
                {
                    var chunk = JsonSerializer.Deserialize<JsonElement>(data);
                    if (chunk.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("type", out var deltaType))
                    {
                        var deltaTypeStr = deltaType.GetString();
                        if (useToolForcing
                            && deltaTypeStr == "input_json_delta"
                            && delta.TryGetProperty("partial_json", out var partialJson))
                        {
                            return partialJson.GetString();
                        }

                        if (!useToolForcing
                            && deltaTypeStr == "text_delta"
                            && delta.TryGetProperty("text", out var textElement))
                        {
                            return textElement.GetString();
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.Debug(ex, "Skipping malformed SSE chunk from Anthropic");
                }

                return null;

            case "message_start":
                // message.usage carries the prompt tokens, including prompt-cache writes/reads.
                TryReadUsage(data, state, fromMessageStart: true);
                return null;

            case "message_delta":
                // The terminal message_delta carries delta.stop_reason and cumulative usage.
                try
                {
                    var chunk = JsonSerializer.Deserialize<JsonElement>(data);
                    if (chunk.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("stop_reason", out var sr) &&
                        sr.ValueKind == JsonValueKind.String)
                    {
                        var reason = sr.GetString();
                        if (!string.IsNullOrEmpty(reason))
                            state.StopReason = reason;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.Debug(ex, "Skipping malformed message_delta from Anthropic");
                }

                TryReadUsage(data, state, fromMessageStart: false);
                return null;

            case "message_stop":
                state.Completed = true;
                return null;

            case "error":
                throw new HttpRequestException($"Anthropic stream failed: {DescribeError(data)}");

            default:
                return null;
        }
    }

    private void TryReadUsage(string data, SseState state, bool fromMessageStart)
    {
        try
        {
            var chunk = JsonSerializer.Deserialize<JsonElement>(data);
            var usageOwner = fromMessageStart && chunk.TryGetProperty("message", out var message) ? message : chunk;
            if (!usageOwner.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
                return;

            state.HasUsage = true;
            state.InputTokens = Math.Max(state.InputTokens, ReadInt(usage, "input_tokens"));
            state.OutputTokens = Math.Max(state.OutputTokens, ReadInt(usage, "output_tokens"));
            state.CacheCreationInputTokens = Math.Max(state.CacheCreationInputTokens, ReadInt(usage, "cache_creation_input_tokens"));
            state.CacheReadInputTokens = Math.Max(state.CacheReadInputTokens, ReadInt(usage, "cache_read_input_tokens"));
        }
        catch (JsonException ex)
        {
            _logger.Debug(ex, "Skipping malformed usage in Anthropic event");
        }

        static int ReadInt(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
                ? number
                : 0;
    }

    private void RecordUsage(string modelId, SseState state)
    {
        if (_costTracker is null || !state.HasUsage)
            return;

        try
        {
            _costTracker.RecordUsage(
                modelId, ProviderId, state.InputTokens, state.OutputTokens,
                state.CacheCreationInputTokens, state.CacheReadInputTokens);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Could not record Anthropic usage");
        }
    }

    /// <summary>Formats an Anthropic error payload ({"type":"error","error":{...}}) for messages.</summary>
    private static string DescribeError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return "no details returned";

        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(payload);
            if (json.ValueKind == JsonValueKind.Object &&
                json.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.Object)
            {
                var type = error.TryGetProperty("type", out var t) ? t.GetString() : null;
                var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
                return string.IsNullOrEmpty(type) ? message ?? payload : $"{type}: {message}";
            }
        }
        catch (JsonException)
        {
            // Not JSON; fall through to the raw (truncated) body.
        }

        return payload.Length > 500 ? payload[..500] : payload;
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

        return $"{(int)response.StatusCode} {DescribeError(payload)}";
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

        _logger.Debug("Chat request to Anthropic with {Count} messages", messages.Count);

        var sb = new StringBuilder();

        await foreach (var token in StreamChatAsync(messages, options, ct).ConfigureAwait(false))
        {
            sb.Append(token);
        }

        var result = sb.ToString();
        _logger.Debug("Anthropic chat completed, response length: {Length} characters", result.Length);
        return result;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Anthropic does not offer an embedding API. This method throws <see cref="NotSupportedException"/>.
    /// Use an alternative provider (OpenAI or Ollama) for embeddings.
    /// </remarks>
    public Task<float[]> GenerateEmbeddingAsync(
        string text,
        string modelName,
        CancellationToken ct = default)
    {
        throw new NotSupportedException(
            "Anthropic does not provide an embedding API. " +
            "Use Ollama or OpenAI for embedding generation.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Anthropic does not offer an embedding API. This method throws <see cref="NotSupportedException"/>.
    /// Use an alternative provider (OpenAI or Ollama) for embeddings.
    /// </remarks>
    public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        string modelName,
        CancellationToken ct = default)
    {
        throw new NotSupportedException(
            "Anthropic does not provide an embedding API. " +
            "Use Ollama or OpenAI for embedding generation.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Deferred: a response that is still streaming keeps the HTTP client until it ends.
        if (!_lifetime.RequestDispose(() => _http.Dispose()))
            return;

        _isAvailable = false;
        _logger.Debug("AnthropicProvider disposed");
    }

    // -- Private Helpers ---------------------------------------------

    /// <summary>
    /// Extracts system-role messages from the conversation and combines them into a
    /// single system prompt string. Returns the remaining non-system messages in
    /// Anthropic API format (alternating user/assistant turns).
    /// </summary>
    private static (string? SystemPrompt, List<object> Messages) ExtractSystemPromptAndMessages(
        IReadOnlyList<ChatMessage> messages)
    {
        var systemParts = new List<string>();
        var apiMessages = new List<object>();

        foreach (var msg in messages)
        {
            var role = msg.Role.ToLowerInvariant();

            if (role == "system")
            {
                if (!string.IsNullOrWhiteSpace(msg.Content))
                    systemParts.Add(msg.Content);
            }
            else
            {
                // Anthropic only accepts "user" and "assistant" roles
                var apiRole = role == "assistant" ? "assistant" : "user";
                apiMessages.Add(new { role = apiRole, content = msg.Content });
            }
        }

        // Ensure the messages list starts with a "user" message (Anthropic requirement).
        // If the first message is "assistant", prepend a placeholder user message.
        if (apiMessages.Count > 0)
        {
            var firstMsg = (dynamic)apiMessages[0];
            if ((string)firstMsg.role == "assistant")
            {
                apiMessages.Insert(0, new { role = "user", content = "Continue the conversation." });
            }
        }

        var systemPrompt = systemParts.Count > 0 ? string.Join("\n\n", systemParts) : null;
        return (systemPrompt, apiMessages);
    }

    private void ThrowIfDisposed()
    {
        if (_lifetime.IsDisposeRequested)
            throw new ObjectDisposedException(nameof(AnthropicProvider));
    }
}
