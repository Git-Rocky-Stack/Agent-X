using System.Runtime.CompilerServices;
using System.Security.Cryptography;
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
/// <remarks>
/// Provider state (the registered providers, the active provider and model, the connection
/// flag) is published as one immutable snapshot. Re-initialization builds the new snapshot
/// first and swaps it in atomically, so concurrent callers always see a consistent provider
/// and model pair. Providers whose configuration did not change are reused; replaced
/// providers are disposed, and each provider defers releasing its resources until the calls
/// already running on it have finished.
/// </remarks>
public sealed class AiService : IAiService
{
    /// <summary>How long a successful connection check is reused.</summary>
    private static readonly TimeSpan ConnectedCacheDuration = TimeSpan.FromSeconds(60);

    /// <summary>How long a failed connection check is reused before probing again.</summary>
    private static readonly TimeSpan DisconnectedCacheDuration = TimeSpan.FromSeconds(15);

    private readonly ISettingsService _settingsService;
    private readonly ICostTracker? _costTracker;
    private readonly ILogger _logger;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly Dictionary<IAiProvider, (bool Connected, DateTime CheckedAtUtc)> _connectionCache =
        new(ReferenceEqualityComparer.Instance);

    private volatile ServiceState _state = ServiceState.Empty;
    private bool _disposed;

    /// <summary>Immutable provider state, swapped as a whole.</summary>
    private sealed record ServiceState(
        IReadOnlyDictionary<string, IAiProvider> Providers,
        IReadOnlyDictionary<string, string> Fingerprints,
        IAiProvider? Active,
        string ActiveModelId,
        bool IsConnected,
        AppSettings? Settings)
    {
        public static ServiceState Empty { get; } = new(
            new Dictionary<string, IAiProvider>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            null,
            string.Empty,
            false,
            null);
    }

    /// <inheritdoc />
    public IAiProvider ActiveProvider => _state.Active
        ?? throw new InvalidOperationException("AI service has not been initialized. Call InitializeAsync first.");

    /// <inheritdoc />
    public bool IsConnected => _state.IsConnected;

    /// <inheritdoc />
    public string ActiveModelId => _state.ActiveModelId;

    /// <inheritdoc />
    public IReadOnlyCollection<string> RegisteredProviderIds => _state.Providers.Keys.ToList().AsReadOnly();

    /// <summary>
    /// Creates a new AiService with the specified settings service.
    /// </summary>
    /// <param name="settingsService">Service for reading/writing application settings.</param>
    /// <param name="costTracker">
    /// Optional usage recorder handed to the providers, which record the token usage the APIs
    /// report for every response (resolved from DI when registered).
    /// </param>
    public AiService(ISettingsService settingsService, ICostTracker? costTracker = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _costTracker = costTracker;
        _logger = Log.ForContext<AiService>();
        _logger.Information("AiService created");
    }

    /// <summary>
    /// Test seam: builds a provider for an id and a validated configuration instead of the
    /// real constructors. Returning null falls back to the real provider. Never set in production.
    /// </summary>
    internal Func<string, AppSettings, IAiProvider?>? ProviderFactoryOverride { get; set; }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        _logger.Information("Initializing AI service...");

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);
            var previous = _state;

            // Build the complete new provider set before touching the published state, so a bad
            // setting (for example an endpoint that is not a URL) can never leave the service
            // with no active provider.
            var providers = new Dictionary<string, IAiProvider>(StringComparer.OrdinalIgnoreCase);
            var fingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            void Register(string providerId, string fingerprint, Func<IAiProvider> create)
            {
                if (previous.Providers.TryGetValue(providerId, out var existing) &&
                    previous.Fingerprints.TryGetValue(providerId, out var existingFingerprint) &&
                    existingFingerprint == fingerprint)
                {
                    // Unchanged configuration: keep the instance, so a settings save does not
                    // reload the built-in model or cut off a response that is streaming.
                    providers[providerId] = existing;
                    fingerprints[providerId] = fingerprint;
                    return;
                }

                try
                {
                    providers[providerId] = ProviderFactoryOverride?.Invoke(providerId, settings) ?? create();
                    fingerprints[providerId] = fingerprint;
                    _logger.Debug("{Provider} provider registered", providerId);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to create {Provider} provider", providerId);
                }
            }

            // 0. Built-in local LLM (LLamaSharp)
            var modelsDir = Path.Combine(settings.StoragePath, "Models");
            Register(
                "local",
                Fingerprint(modelsDir, settings.LocalModelFileName, settings.LocalContextSize, settings.LocalGpuLayers),
                () => new LocalLlmProvider(
                    modelsDir,
                    settings.LocalModelFileName,
                    settings.LocalContextSize,
                    settings.LocalGpuLayers,
                    _logger));

            // 1. Ollama (only with a valid endpoint)
            if (TryParseHttpEndpoint(settings.OllamaEndpoint, out var ollamaEndpoint))
            {
                Register("ollama", Fingerprint(ollamaEndpoint.AbsoluteUri),
                    () => new OllamaProvider(ollamaEndpoint, _logger, _costTracker));
            }
            else
            {
                _logger.Warning(
                    "Ollama endpoint '{Endpoint}' is not an absolute http(s) URL; Ollama stays unavailable until it is corrected in Settings",
                    settings.OllamaEndpoint);
            }

            // 2. OpenAI when an API key is configured
            if (!string.IsNullOrWhiteSpace(settings.OpenAiApiKey))
            {
                Register("openai", Fingerprint(settings.OpenAiApiKey, settings.OpenAiEndpoint),
                    () => new OpenAiProvider(settings.OpenAiApiKey, settings.OpenAiEndpoint, _logger, _costTracker));
            }

            // 3. Anthropic when an API key is configured
            if (!string.IsNullOrWhiteSpace(settings.AnthropicApiKey))
            {
                Register("anthropic", Fingerprint(settings.AnthropicApiKey, settings.AnthropicEndpoint),
                    () => new AnthropicProvider(settings.AnthropicApiKey, settings.AnthropicEndpoint, _logger, _costTracker));
            }

            // 4. Activate the preferred provider
            var preferredProviderId = string.IsNullOrWhiteSpace(settings.ActiveProviderId)
                ? "local"
                : settings.ActiveProviderId.Trim();

            if (!providers.TryGetValue(preferredProviderId, out var active))
            {
                var fallbackId = ChooseFallbackProviderId(providers);
                _logger.Warning(
                    "Preferred provider {ProviderId} is not registered (missing API key or invalid endpoint?). Falling back to {Fallback}.",
                    preferredProviderId, fallbackId ?? "none");

                active = fallbackId is null ? null : providers[fallbackId];
            }

            var connected = false;
            if (active is not null)
            {
                connected = await CheckConnectionUncachedAsync(active, ct).ConfigureAwait(false);
            }

            var activeModelId = active is null ? string.Empty : ResolveDefaultModel(settings, active.ProviderId);
            List<IAiProvider> retired;
            lock (_stateLock)
            {
                var current = _state;
                retired = current.Providers.Values
                    .Where(p => !providers.Values.Any(n => ReferenceEquals(n, p)))
                    .ToList();

                foreach (var provider in retired)
                    _connectionCache.Remove(provider);

                _state = new ServiceState(providers, fingerprints, active, activeModelId, connected, settings);
            }

            foreach (var provider in retired)
            {
                try { provider.Dispose(); }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Error disposing replaced provider: {Provider}", provider.ProviderId);
                }
            }

            if (active is null)
            {
                _logger.Error("No AI provider could be registered");
            }
            else if (connected)
            {
                _logger.Information(
                    "AI service initialized with {Provider} provider, model: {Model}",
                    active.ProviderId, activeModelId);
            }
            else
            {
                _logger.Warning(
                    "{Provider} is not reachable. AI service initialized in offline mode.",
                    active.ProviderId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(ex, "Failed to initialize AI service");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Resolves the appropriate default model ID for the given provider based on app settings.
    /// </summary>
    private static string ResolveDefaultModel(AppSettings? settings, string providerId)
    {
        static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        return providerId.ToLowerInvariant() switch
        {
            "local" => Or(settings?.LocalModelFileName, BuiltInModelBootstrap.DefaultModelFileName),
            "openai" => Or(settings?.OpenAiDefaultModel, OpenAiProvider.DefaultModelId),
            "anthropic" => Or(settings?.AnthropicDefaultModel, AnthropicProvider.DefaultModelId),
            _ => Or(settings?.DefaultModel, "llama3.2")
        };
    }

    /// <inheritdoc />
    public string GetDefaultModelId(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return ResolveDefaultModel(_state.Settings, providerId);
    }

    /// <inheritdoc />
    public IAiProvider? GetProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return null;

        return _state.Providers.TryGetValue(providerId, out var provider) ? provider : null;
    }

    /// <inheritdoc />
    public async Task<bool> IsProviderAvailableAsync(string providerId, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var provider = GetProvider(providerId);
        if (provider is null)
            return false;

        return await CheckConnectionCachedAsync(provider, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public EmbeddingTarget ResolveEmbeddingTarget()
    {
        var state = _state;
        var local = state.Providers.TryGetValue("local", out var provider) ? provider as LocalLlmProvider : null;

        return EmbeddingTargetResolver.Resolve(
            state.Settings?.EmbeddingModel,
            local?.ModelFileName ?? state.Settings?.LocalModelFileName,
            local?.IsModelFileInstalled == true);
    }

    /// <inheritdoc />
    public async Task<bool> SwitchProviderAsync(string providerId, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(providerId))
            throw new ArgumentException("Provider ID cannot be null or empty.", nameof(providerId));

        _logger.Information("Switching AI provider to: {ProviderId}", providerId);

        var provider = GetProvider(providerId);
        if (provider is null)
        {
            _logger.Warning("Provider not found: {ProviderId}. Available: [{Available}]",
                providerId, string.Join(", ", _state.Providers.Keys));
            return false;
        }

        bool connected;
        try
        {
            connected = await CheckConnectionCachedAsync(provider, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(ex, "Failed to switch to provider: {ProviderId}", providerId);
            return false;
        }

        if (!connected)
        {
            // Keep the current provider: switching to an unreachable one would make every
            // following request fail although a working provider was active.
            _logger.Warning("Provider {ProviderId} is not reachable; keeping {Current}",
                providerId, _state.Active?.ProviderId ?? "none");
            return false;
        }

        lock (_stateLock)
        {
            var current = _state;
            if (!current.Providers.TryGetValue(providerId, out var registered) || !ReferenceEquals(registered, provider))
                return false; // re-initialized meanwhile; the checked instance is gone

            if (!ReferenceEquals(current.Active, provider))
            {
                // Provider and model change together, so a model id of the previous provider is
                // never sent to the new one.
                _state = current with
                {
                    Active = provider,
                    ActiveModelId = ResolveDefaultModel(current.Settings, provider.ProviderId),
                    IsConnected = true
                };
            }
            else
            {
                _state = current with { IsConnected = true };
            }
        }

        _logger.Information("Switched to provider {ProviderId}, model {Model}", providerId, _state.ActiveModelId);
        return true;
    }

    /// <inheritdoc />
    public async Task SetActiveModelAsync(string modelId, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("Model ID cannot be null or empty.", nameof(modelId));

        _logger.Information("Setting active model to: {ModelId}", modelId);

        string? providerId;
        lock (_stateLock)
        {
            _state = _state with { ActiveModelId = modelId };
            providerId = _state.Active?.ProviderId;
        }

        if (providerId is null)
            return;

        // Persist the selection in the active provider's own setting, so a restart restores the
        // model for that provider and another provider is never handed this model id.
        try
        {
            var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);
            if (ApplyModelSetting(settings, providerId, modelId))
            {
                await _settingsService.SaveSettingsAsync(settings).ConfigureAwait(false);
                _logger.Debug("Active {Provider} model persisted to settings: {ModelId}", providerId, modelId);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to persist active model setting, but in-memory selection was updated");
        }
    }

    /// <summary>
    /// Stores <paramref name="modelId"/> in the setting that belongs to <paramref name="providerId"/>.
    /// Returns true when a setting changed. The built-in provider's configured file is also its
    /// embedding model, so a chat pick of another GGUF is kept for this session only instead of
    /// silently changing the embedding space of the index.
    /// </summary>
    internal bool ApplyModelSetting(AppSettings settings, string providerId, string modelId)
    {
        switch (providerId.ToLowerInvariant())
        {
            case "ollama":
                if (settings.DefaultModel == modelId) return false;
                settings.DefaultModel = modelId;
                return true;
            case "openai":
                if (settings.OpenAiDefaultModel == modelId) return false;
                settings.OpenAiDefaultModel = modelId;
                return true;
            case "anthropic":
                if (settings.AnthropicDefaultModel == modelId) return false;
                settings.AnthropicDefaultModel = modelId;
                return true;
            case "local":
                _logger.Information(
                    "Built-in model selection {ModelId} applies to this session; the configured built-in model stays {Configured}",
                    modelId, settings.LocalModelFileName);
                return false;
            default:
                return false;
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

        // One snapshot for the whole call: the provider and the model id always belong together
        // even if the service is re-initialized while the response streams.
        var state = _state;
        var provider = EnsureActiveProvider(state);

        var preparedMessages = PrepareMessages(messages, systemPrompt);
        var effectiveOptions = EnsureModelInOptions(options, state.ActiveModelId);

        _logger.Debug("Streaming chat with {Count} messages (system prompt: {HasSystem})",
            preparedMessages.Count, !string.IsNullOrEmpty(systemPrompt));

        await foreach (var token in provider.StreamChatAsync(preparedMessages, effectiveOptions, ct)
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

        var state = _state;
        var provider = EnsureActiveProvider(state);

        var preparedMessages = PrepareMessages(messages, systemPrompt);
        var effectiveOptions = EnsureModelInOptions(options, state.ActiveModelId);

        _logger.Debug("Chat request with {Count} messages (system prompt: {HasSystem})",
            preparedMessages.Count, !string.IsNullOrEmpty(systemPrompt));

        try
        {
            var result = await provider.ChatAsync(preparedMessages, effectiveOptions, ct)
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
        ServiceState state;
        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;
            state = _state;
            _state = ServiceState.Empty;
            _connectionCache.Clear();
        }

        _logger.Debug("Disposing AiService...");

        foreach (var provider in state.Providers.Values)
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

        _logger.Information("AiService disposed");
    }

    // -- Private Helpers ---------------------------------------------

    /// <summary>
    /// Parses an Ollama endpoint. Only absolute http/https URLs are accepted; values such as
    /// "localhost" or "localhost:11434" are rejected instead of throwing UriFormatException.
    /// Public so the settings and onboarding connection tests validate the same way.
    /// </summary>
    public static bool TryParseHttpEndpoint(string? value, out Uri endpoint)
    {
        endpoint = null!;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed))
            return false;

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            return false;

        endpoint = parsed;
        return true;
    }

    /// <summary>
    /// Picks the provider to use when the preferred one is not registered: the built-in model
    /// when its file is installed, otherwise Ollama, otherwise whatever is registered.
    /// </summary>
    private static string? ChooseFallbackProviderId(IReadOnlyDictionary<string, IAiProvider> providers)
    {
        if (providers.TryGetValue("local", out var local) &&
            local is LocalLlmProvider { IsModelFileInstalled: true })
        {
            return "local";
        }

        if (providers.ContainsKey("ollama"))
            return "ollama";

        if (providers.ContainsKey("local"))
            return "local";

        return providers.Keys.FirstOrDefault();
    }

    /// <summary>
    /// A stable identity for a provider configuration. Secrets are hashed so the fingerprint
    /// never holds an API key in clear text.
    /// </summary>
    private static string Fingerprint(params object?[] parts)
    {
        var joined = string.Join("\u001f", parts.Select(p => p?.ToString() ?? string.Empty));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
    }

    private async Task<bool> CheckConnectionCachedAsync(IAiProvider provider, CancellationToken ct)
    {
        lock (_stateLock)
        {
            if (_connectionCache.TryGetValue(provider, out var cached))
            {
                var ttl = cached.Connected ? ConnectedCacheDuration : DisconnectedCacheDuration;
                if (DateTime.UtcNow - cached.CheckedAtUtc < ttl)
                    return cached.Connected;
            }
        }

        return await CheckConnectionUncachedAsync(provider, ct).ConfigureAwait(false);
    }

    private async Task<bool> CheckConnectionUncachedAsync(IAiProvider provider, CancellationToken ct)
    {
        var connected = await provider.CheckConnectionAsync(ct).ConfigureAwait(false);

        lock (_stateLock)
        {
            _connectionCache[provider] = (connected, DateTime.UtcNow);
            if (ReferenceEquals(_state.Active, provider) && _state.IsConnected != connected)
                _state = _state with { IsConnected = connected };
        }

        return connected;
    }

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
    /// Returns options carrying the active model when none is specified. The caller's object is
    /// copied rather than modified, so a model id filled in for one provider never sticks to a
    /// reused options instance that is later sent to another provider.
    /// </summary>
    private static ChatOptions EnsureModelInOptions(ChatOptions? options, string activeModelId)
    {
        if (options is null)
        {
            return new ChatOptions { ModelId = activeModelId };
        }

        if (string.IsNullOrEmpty(options.ModelId))
        {
            var copy = options.ShallowCopy();
            copy.ModelId = activeModelId;
            return copy;
        }

        return options;
    }

    /// <summary>
    /// Validates that an active provider exists and throws if not.
    /// </summary>
    private static IAiProvider EnsureActiveProvider(ServiceState state)
    {
        return state.Active ?? throw new InvalidOperationException(
            "No active AI provider. Call InitializeAsync before making AI requests.");
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
