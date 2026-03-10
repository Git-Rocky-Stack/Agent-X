using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgentX.Core.AI.Models;
using AgentX.Core.AI.Providers;
using AgentX.Core.Services.Settings;
using Serilog;

namespace AgentX.Core.AI;

/// <summary>
/// High-level AI service implementation that orchestrates provider lifecycle,
/// model selection, and application-specific AI operations (summarization, tagging).
/// </summary>
public sealed class AiService : IAiService
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger _logger;
    private readonly Dictionary<string, IAiProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    private IAiProvider? _activeProvider;
    private string _activeModelId = string.Empty;
    private bool _isConnected;
    private bool _disposed;

    /// <inheritdoc />
    public IAiProvider ActiveProvider => _activeProvider
        ?? throw new InvalidOperationException("AI service has not been initialized. Call InitializeAsync first.");

    /// <inheritdoc />
    public bool IsConnected => _isConnected;

    /// <inheritdoc />
    public string ActiveModelId => _activeModelId;

    /// <summary>
    /// Creates a new AiService with the specified settings service.
    /// </summary>
    /// <param name="settingsService">Service for reading/writing application settings.</param>
    public AiService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = Log.ForContext<AiService>();
        _logger.Information("AiService created");
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        _logger.Information("Initializing AI service...");

        try
        {
            // Dispose any existing providers before re-initialization
            // (supports being called again after settings change)
            foreach (var existingProvider in _providers.Values)
            {
                try { existingProvider.Dispose(); }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Error disposing existing provider: {Provider}",
                        existingProvider.ProviderId);
                }
            }
            _providers.Clear();
            _activeProvider = null;
            _isConnected = false;

            var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);

            // ── 0. Register built-in local LLM (LLamaSharp) ────────
            try
            {
                var modelsDir = Path.Combine(settings.StoragePath, "Models");
                var localProvider = new LocalLlmProvider(
                    modelsDir,
                    settings.LocalModelFileName,
                    settings.LocalContextSize,
                    settings.LocalGpuLayers,
                    _logger);
                _providers["local"] = localProvider;
                _logger.Debug("Local LLM provider registered — model: {Model}", settings.LocalModelFileName);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to create local LLM provider");
            }

            // ── 1. Always register Ollama ──────────────────────────
            var ollamaEndpoint = new Uri(settings.OllamaEndpoint);
            var ollamaProvider = new OllamaProvider(ollamaEndpoint, _logger);
            _providers["ollama"] = ollamaProvider;
            _logger.Debug("Ollama provider registered with endpoint {Endpoint}", settings.OllamaEndpoint);

            // ── 2. Register OpenAI if API key is configured ────────
            if (!string.IsNullOrWhiteSpace(settings.OpenAiApiKey))
            {
                try
                {
                    var openAiProvider = new OpenAiProvider(
                        settings.OpenAiApiKey,
                        settings.OpenAiEndpoint,
                        _logger);
                    _providers["openai"] = openAiProvider;
                    _logger.Debug("OpenAI provider registered with endpoint {Endpoint}", settings.OpenAiEndpoint);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to create OpenAI provider");
                }
            }

            // ── 3. Register Anthropic if API key is configured ─────
            if (!string.IsNullOrWhiteSpace(settings.AnthropicApiKey))
            {
                try
                {
                    var anthropicProvider = new AnthropicProvider(
                        settings.AnthropicApiKey,
                        settings.AnthropicEndpoint,
                        _logger);
                    _providers["anthropic"] = anthropicProvider;
                    _logger.Debug("Anthropic provider registered with endpoint {Endpoint}", settings.AnthropicEndpoint);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to create Anthropic provider");
                }
            }

            // ── 4. Activate the preferred provider ─────────────────
            var preferredProviderId = settings.ActiveProviderId ?? "ollama";

            if (_providers.TryGetValue(preferredProviderId, out var preferredProvider))
            {
                var connected = await preferredProvider.CheckConnectionAsync(ct).ConfigureAwait(false);
                _activeProvider = preferredProvider;
                _isConnected = connected;
                _activeModelId = ResolveDefaultModel(settings, preferredProviderId);

                if (connected)
                {
                    _logger.Information(
                        "AI service initialized with {Provider} provider, model: {Model}",
                        preferredProviderId, _activeModelId);
                }
                else
                {
                    _logger.Warning(
                        "{Provider} is not reachable. AI service initialized in offline mode.",
                        preferredProviderId);
                }
            }
            else
            {
                // Fallback to Ollama if preferred provider is not registered
                _logger.Warning(
                    "Preferred provider {ProviderId} not registered (missing API key?). Falling back to Ollama.",
                    preferredProviderId);

                var connected = await ollamaProvider.CheckConnectionAsync(ct).ConfigureAwait(false);
                _activeProvider = ollamaProvider;
                _isConnected = connected;
                _activeModelId = settings.DefaultModel;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize AI service");
            _isConnected = false;
            throw;
        }
    }

    /// <summary>
    /// Resolves the appropriate default model ID for the given provider based on app settings.
    /// </summary>
    private static string ResolveDefaultModel(AppSettings settings, string providerId)
    {
        return providerId.ToLowerInvariant() switch
        {
            "local" => settings.LocalModelFileName,
            "openai" => settings.OpenAiDefaultModel ?? "gpt-4o-mini",
            "anthropic" => settings.AnthropicDefaultModel ?? "claude-sonnet-4-20250514",
            _ => settings.DefaultModel
        };
    }

    /// <inheritdoc />
    public async Task<bool> SwitchProviderAsync(string providerId, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(providerId))
            throw new ArgumentException("Provider ID cannot be null or empty.", nameof(providerId));

        _logger.Information("Switching AI provider to: {ProviderId}", providerId);

        if (!_providers.TryGetValue(providerId, out var provider))
        {
            _logger.Warning("Provider not found: {ProviderId}. Available: [{Available}]",
                providerId, string.Join(", ", _providers.Keys));
            return false;
        }

        try
        {
            var connected = await provider.CheckConnectionAsync(ct).ConfigureAwait(false);
            _activeProvider = provider;
            _isConnected = connected;

            _logger.Information("Switched to provider {ProviderId}, connected: {Connected}", providerId, connected);
            return connected;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to switch to provider: {ProviderId}", providerId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task SetActiveModelAsync(string modelId, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("Model ID cannot be null or empty.", nameof(modelId));

        _logger.Information("Setting active model to: {ModelId}", modelId);
        _activeModelId = modelId;

        // Persist the model selection to settings
        try
        {
            var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);
            settings.DefaultModel = modelId;
            await _settingsService.SaveSettingsAsync(settings).ConfigureAwait(false);
            _logger.Debug("Active model persisted to settings: {ModelId}", modelId);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to persist active model setting, but in-memory selection was updated");
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt = null,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnsureActiveProvider();

        var preparedMessages = PrepareMessages(messages, systemPrompt);
        var effectiveOptions = EnsureModelInOptions(options);

        _logger.Debug("Streaming chat with {Count} messages (system prompt: {HasSystem})",
            preparedMessages.Count, !string.IsNullOrEmpty(systemPrompt));

        await foreach (var token in _activeProvider!.StreamChatAsync(preparedMessages, effectiveOptions, ct)
            .ConfigureAwait(false))
        {
            yield return token;
        }
    }

    /// <inheritdoc />
    public async Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt = null,
        ChatOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        EnsureActiveProvider();

        var preparedMessages = PrepareMessages(messages, systemPrompt);
        var effectiveOptions = EnsureModelInOptions(options);

        _logger.Debug("Chat request with {Count} messages (system prompt: {HasSystem})",
            preparedMessages.Count, !string.IsNullOrEmpty(systemPrompt));

        try
        {
            var result = await _activeProvider!.ChatAsync(preparedMessages, effectiveOptions, ct)
                .ConfigureAwait(false);

            _logger.Debug("Chat completed, response length: {Length}", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Chat request failed");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> SummarizeAsync(string content, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be null or empty.", nameof(content));

        _logger.Debug("Summarizing content of length {Length}", content.Length);

        const string systemPrompt =
            "You are a precise summarization assistant. Provide a clear, concise summary of the given content. " +
            "Focus on the key points and main ideas. Keep the summary to 2-3 paragraphs maximum.";

        var messages = new List<ChatMessage>
        {
            new()
            {
                Role = "user",
                Content = $"Please summarize the following content:\n\n{content}"
            }
        };

        return await ChatAsync(messages, systemPrompt, ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GenerateTagsAsync(
        string content,
        int maxTags = 5,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be null or empty.", nameof(content));

        _logger.Debug("Generating up to {MaxTags} tags for content of length {Length}", maxTags, content.Length);

        var systemPrompt =
            "You are a tagging assistant. Generate descriptive tags for the given content. " +
            $"Return ONLY a JSON array of strings with at most {maxTags} tags. " +
            "Each tag should be 1-3 words, lowercase, and descriptive of the content's key topics. " +
            "Example output: [\"machine learning\", \"neural networks\", \"data science\"]";

        var messages = new List<ChatMessage>
        {
            new()
            {
                Role = "user",
                Content = $"Generate tags for this content:\n\n{content}"
            }
        };

        try
        {
            var jsonOptions = new ChatOptions
            {
                ResponseFormat = ResponseFormat.JsonObject
            };
            var response = await ChatAsync(messages, systemPrompt, jsonOptions, ct).ConfigureAwait(false);
            return ParseTagsFromResponse(response, maxTags);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to generate tags, returning empty list");
            return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.Debug("Disposing AiService...");

        foreach (var provider in _providers.Values)
        {
            try
            {
                provider.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error disposing provider: {Provider}", provider.ProviderId);
            }
        }

        _providers.Clear();
        _activeProvider = null;
        _isConnected = false;

        _logger.Information("AiService disposed");
    }

    // ── Private Helpers ─────────────────────────────────────────────

    /// <summary>
    /// Prepends a system prompt message if provided, creating a new message list.
    /// </summary>
    private static IReadOnlyList<ChatMessage> PrepareMessages(
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt)
    {
        if (string.IsNullOrEmpty(systemPrompt))
            return messages;

        var prepared = new List<ChatMessage>(messages.Count + 1)
        {
            new()
            {
                Role = "system",
                Content = systemPrompt,
                Timestamp = DateTime.UtcNow
            }
        };

        prepared.AddRange(messages);
        return prepared;
    }

    /// <summary>
    /// Ensures the ChatOptions has the active model set if not explicitly specified.
    /// </summary>
    private ChatOptions EnsureModelInOptions(ChatOptions? options)
    {
        if (options is null)
        {
            return new ChatOptions { ModelId = _activeModelId };
        }

        if (string.IsNullOrEmpty(options.ModelId))
        {
            options.ModelId = _activeModelId;
        }

        return options;
    }

    /// <summary>
    /// Validates that an active provider exists and throws if not.
    /// </summary>
    private void EnsureActiveProvider()
    {
        if (_activeProvider is null)
        {
            throw new InvalidOperationException(
                "No active AI provider. Call InitializeAsync before making AI requests.");
        }
    }

    /// <summary>
    /// Parses a JSON array of strings from the model's response, with fallback
    /// to line-by-line parsing if JSON parsing fails.
    /// </summary>
    private IReadOnlyList<string> ParseTagsFromResponse(string response, int maxTags)
    {
        // Try JSON array parsing first
        try
        {
            // Extract JSON array from response (model may include surrounding text)
            var startIndex = response.IndexOf('[');
            var endIndex = response.LastIndexOf(']');

            if (startIndex >= 0 && endIndex > startIndex)
            {
                var jsonPart = response[startIndex..(endIndex + 1)];
                var tags = JsonSerializer.Deserialize<List<string>>(jsonPart);

                if (tags is not null && tags.Count > 0)
                {
                    return tags
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Select(t => t.Trim().ToLowerInvariant())
                        .Distinct()
                        .Take(maxTags)
                        .ToList()
                        .AsReadOnly();
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.Debug(ex, "JSON tag parsing failed, falling back to line parsing");
        }

        // Fallback: parse comma-separated or line-separated tags
        var fallbackTags = response
            .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().Trim('"', '[', ']', '-', '*', ' ').ToLowerInvariant())
            .Where(t => !string.IsNullOrWhiteSpace(t) && t.Length <= 50)
            .Distinct()
            .Take(maxTags)
            .ToList();

        _logger.Debug("Parsed {Count} tags via fallback method", fallbackTags.Count);
        return fallbackTags.AsReadOnly();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AiService));
    }
}
