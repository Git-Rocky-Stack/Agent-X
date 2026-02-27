using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgentX.Core.AI.Models;
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
    private bool _isAvailable;
    private bool _disposed;

    private const string AnthropicApiVersion = "2023-06-01";

    /// <inheritdoc />
    public string ProviderId => "anthropic";

    /// <inheritdoc />
    public string DisplayName => "Anthropic Claude";

    /// <inheritdoc />
    public bool IsAvailable => _isAvailable;

    /// <summary>
    /// Known Claude models with their display names.
    /// Anthropic does not provide a list-models endpoint, so we maintain a static catalog.
    /// </summary>
    private static readonly List<(string Id, string Name)> KnownModels = new()
    {
        ("claude-sonnet-4-20250514", "Claude Sonnet 4"),
        ("claude-haiku-4-5-20251001", "Claude Haiku 4.5"),
        ("claude-opus-4-20250514", "Claude Opus 4"),
        ("claude-3-5-sonnet-20241022", "Claude 3.5 Sonnet"),
        ("claude-3-5-haiku-20241022", "Claude 3.5 Haiku"),
    };

    /// <summary>
    /// Creates a new AnthropicProvider targeting the specified endpoint with the given API key.
    /// </summary>
    /// <param name="apiKey">The Anthropic API key for authentication.</param>
    /// <param name="endpoint">The base API endpoint (e.g. https://api.anthropic.com/v1/).</param>
    /// <param name="logger">Serilog logger for diagnostics.</param>
    public AnthropicProvider(string apiKey, string endpoint, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key cannot be null or empty.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));

        _logger = (logger ?? Log.Logger).ForContext<AnthropicProvider>();

        // Ensure endpoint ends with a trailing slash for proper URI resolution
        if (!endpoint.EndsWith('/'))
            endpoint += "/";

        _http = new HttpClient
        {
            BaseAddress = new Uri(endpoint),
            Timeout = TimeSpan.FromMinutes(5) // Long timeout for streaming responses
        };
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", AnthropicApiVersion);

        _logger.Information("AnthropicProvider created targeting {Endpoint}", endpoint);
    }

    /// <inheritdoc />
    public async Task<bool> CheckConnectionAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        try
        {
            _logger.Debug("Checking Anthropic connection...");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            // Anthropic doesn't have a simple health-check endpoint.
            // We send a minimal messages request to verify the API key works.
            var body = new
            {
                model = "claude-haiku-4-5-20251001",
                max_tokens = 1,
                messages = new[] { new { role = "user", content = "hi" } }
            };

            var response = await _http.PostAsJsonAsync("messages", body, timeoutCts.Token)
                .ConfigureAwait(false);

            // 200 = success, 401 = bad key, 429 = rate limited (but key is valid)
            _isAvailable = response.IsSuccessStatusCode ||
                           response.StatusCode == System.Net.HttpStatusCode.TooManyRequests;

            _logger.Information("Anthropic connection check: {IsAvailable} (status: {StatusCode})",
                _isAvailable, response.StatusCode);

            return _isAvailable;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _isAvailable = false;
            _logger.Warning("Anthropic connection check timed out (10s)");
            return false;
        }
        catch (Exception ex)
        {
            _isAvailable = false;
            _logger.Warning(ex, "Anthropic connection check failed");
            return false;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        _logger.Debug("Listing known Anthropic Claude models...");

        var models = KnownModels.Select(m => new AiModel
        {
            Id = m.Id,
            Name = m.Name,
            ProviderId = ProviderId,
            Family = "Claude",
            IsAvailable = true
        }).ToList();

        _logger.Information("Returned {Count} known Anthropic models", models.Count);
        return Task.FromResult<IReadOnlyList<AiModel>>(models.AsReadOnly());
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
        _logger.Debug("PullModelAsync called for Anthropic — cloud models do not require pulling");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Model deletion is not supported for cloud providers. This is a no-op.
    /// </remarks>
    public Task DeleteModelAsync(string modelName, CancellationToken ct = default)
    {
        _logger.Debug("DeleteModelAsync called for Anthropic — cloud models cannot be deleted locally");
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

        var modelId = options?.ModelId ?? "claude-sonnet-4-20250514";

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

        if (!string.IsNullOrEmpty(systemPrompt))
            body["system"] = systemPrompt;

        if (options?.Temperature is >= 0)
            body["temperature"] = options.Temperature;
        if (options?.TopP is > 0 and < 1)
            body["top_p"] = options.TopP;
        if (options?.StopSequences is { Length: > 0 })
            body["stop_sequences"] = options.StopSequences;

        _logger.Debug("Streaming Anthropic chat with {Count} messages, model={Model}, system={HasSystem}",
            apiMessages.Count, modelId, !string.IsNullOrEmpty(systemPrompt));

        var request = new HttpRequestMessage(HttpMethod.Post, "messages")
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
            _logger.Error(ex, "Anthropic streaming request failed for model {Model}", modelId);
            throw;
        }

        using (response)
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? currentEventType = null;

            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);

                if (string.IsNullOrEmpty(line))
                {
                    currentEventType = null;
                    continue;
                }

                // Parse SSE event type
                if (line.StartsWith("event: ", StringComparison.Ordinal))
                {
                    currentEventType = line["event: ".Length..];
                    continue;
                }

                // Parse SSE data
                if (!line.StartsWith("data: ", StringComparison.Ordinal))
                    continue;

                var data = line["data: ".Length..];

                // Handle content_block_delta events which carry the actual text
                if (currentEventType == "content_block_delta")
                {
                    string? text = null;
                    try
                    {
                        var chunk = JsonSerializer.Deserialize<JsonElement>(data);
                        if (chunk.TryGetProperty("delta", out var delta) &&
                            delta.TryGetProperty("type", out var deltaType) &&
                            deltaType.GetString() == "text_delta" &&
                            delta.TryGetProperty("text", out var textElement))
                        {
                            text = textElement.GetString();
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.Debug(ex, "Skipping malformed SSE chunk from Anthropic");
                    }

                    if (!string.IsNullOrEmpty(text))
                        yield return text;
                }
                else if (currentEventType == "message_stop")
                {
                    // End of message
                    break;
                }
                else if (currentEventType == "error")
                {
                    _logger.Warning("Anthropic streaming error event: {Data}", data);
                    break;
                }
            }
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
        if (_disposed) return;
        _disposed = true;
        _isAvailable = false;
        _http.Dispose();
        _logger.Debug("AnthropicProvider disposed");
    }

    // ── Private Helpers ─────────────────────────────────────────────

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
        if (_disposed)
            throw new ObjectDisposedException(nameof(AnthropicProvider));
    }
}
