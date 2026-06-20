using Serilog;

namespace AgentX.Core.AI.Routing;

/// <summary>
/// Routes prompts to the optimal AI provider/model by combining task type detection,
/// routing profile preferences, and available provider checks.
/// </summary>
public sealed class ModelRouterService : IModelRouterService
{
    private readonly IAiService _aiService;
    private readonly ITaskTypeDetector _taskTypeDetector;
    private readonly ILogger _logger;

    private RoutingProfile _activeProfile = RoutingProfile.Balanced;

    /// <inheritdoc />
    public RoutingProfile ActiveProfile => _activeProfile;

    /// <inheritdoc />
    public event EventHandler<RoutingDecision>? DecisionMade;

    public ModelRouterService(IAiService aiService, ITaskTypeDetector taskTypeDetector, ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _taskTypeDetector = taskTypeDetector ?? throw new ArgumentNullException(nameof(taskTypeDetector));
        _logger = logger.ForContext<ModelRouterService>();
    }

    /// <inheritdoc />
    public void SetActiveProfile(RoutingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _activeProfile = profile;
        _logger.Information("Routing profile set to: {Profile}", profile.Id);
    }

    /// <inheritdoc />
    public void SetActiveProfile(string profileId)
    {
        var profile = RoutingProfile.FromId(profileId);
        SetActiveProfile(profile);
    }

    /// <inheritdoc />
    public async Task<RoutingDecision> RouteAsync(string prompt, CancellationToken ct = default)
    {
        var taskType = _taskTypeDetector.Detect(prompt);
        return await RouteAsync(prompt, taskType, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RoutingDecision> RouteAsync(string prompt, TaskType taskTypeOverride, CancellationToken ct = default)
    {
        var taskType = taskTypeOverride ?? TaskType.Chat;
        var profile = _activeProfile;
        var reason = new System.Text.StringBuilder();

        string targetProviderId;
        string targetModelId;

        // 1. Check profile TaskOverrides first (highest priority)
        if (profile.TaskOverrides.TryGetValue(taskType.Name, out var overrideProviderId))
        {
            targetProviderId = overrideProviderId;
            reason.Append($"Profile '{profile.Id}' overrides '{taskType.Name}' to provider '{overrideProviderId}'. ");
        }
        // 2. Use profile's PreferLocalFirst preference combined with task type preferences
        else if (profile.PreferLocalFirst)
        {
            if (taskType.PreferQuality && !taskType.PreferLocal)
            {
                targetProviderId = ResolveProvider(preferenceLocal: true, taskType);
                reason.Append($"Profile prefers local, but task '{taskType.Name}' is quality-critical. ");
            }
            else
            {
                targetProviderId = ResolveProvider(preferenceLocal: true, taskType);
                reason.Append($"Profile prefers local for task '{taskType.Name}'. ");
            }
        }
        else
        {
            if (taskType.PreferLocal && taskType.PreferSpeed)
            {
                targetProviderId = ResolveProvider(preferenceLocal: true, taskType);
                reason.Append($"Task '{taskType.Name}' prefers local/speed. ");
            }
            else
            {
                targetProviderId = ResolveProvider(preferenceLocal: false, taskType);
                reason.Append($"Task '{taskType.Name}' prefers cloud/quality. ");
            }
        }

        // 3. Validate that the target provider is actually available; fallback if not
        var (resolvedProviderId, wasFallback) = await EnsureProviderAvailableAsync(targetProviderId, ct).ConfigureAwait(false);
        targetProviderId = resolvedProviderId;
        if (wasFallback)
        {
            reason.Append($"Requested provider unavailable, fell back to '{targetProviderId}'. ");
        }

        // 4. Resolve the model ID for the selected provider
        targetModelId = ResolveModelForProvider(targetProviderId);

        var decision = new RoutingDecision
        {
            ProviderId = targetProviderId,
            ModelId = targetModelId,
            TaskType = taskType,
            Profile = profile,
            Reason = reason.ToString().Trim(),
            DecidedAt = DateTimeOffset.UtcNow,
        };

        _logger.Information(
            "Routing decision: Task={TaskType}, Provider={ProviderId}, Model={ModelId}, Reason={Reason}",
            taskType.Name, decision.ProviderId, decision.ModelId, decision.Reason);

        DecisionMade?.Invoke(this, decision);

        return decision;
    }

    // ── Private Helpers ─────────────────────────────────────────────

    /// <summary>
    /// Resolves the preferred provider ID based on the local/cloud preference
    /// and task type hints.
    /// </summary>
    private static string ResolveProvider(bool preferenceLocal, TaskType taskType)
    {
        // For embedding tasks, always prefer local (Ollama has embedding support)
        if (taskType.Name == "embedding")
            return "ollama";

        if (preferenceLocal)
        {
            return "ollama";
        }

        // Cloud preference: try openai first, then anthropic, then fallback to ollama
        return "openai";
    }

    /// <summary>
    /// Ensures the requested provider is actually registered and available in the AI service.
    /// Returns the resolved provider ID and whether a fallback occurred.
    /// </summary>
    private async Task<(string providerId, bool wasFallback)> EnsureProviderAvailableAsync(
        string requestedProviderId, CancellationToken ct)
    {
        // Try the requested provider first
        try
        {
            var currentProvider = _aiService.ActiveProvider;
            if (currentProvider is not null &&
                string.Equals(currentProvider.ProviderId, requestedProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return (requestedProviderId, false);
            }

            var switchResult = await _aiService.SwitchProviderAsync(requestedProviderId, ct).ConfigureAwait(false);
            if (switchResult)
            {
                return (requestedProviderId, false);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Requested provider '{ProviderId}' check failed", requestedProviderId);
        }

        // Fallback chain
        // If cloud was requested but unavailable, try ollama
        if (!string.Equals(requestedProviderId, "ollama", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var fallbackResult = await _aiService.SwitchProviderAsync("ollama", ct).ConfigureAwait(false);
                if (fallbackResult)
                    return ("ollama", true);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Fallback to Ollama also failed");
            }
        }

        // If ollama was requested but unavailable, try openai
        try
        {
            var cloudFallback = await _aiService.SwitchProviderAsync("openai", ct).ConfigureAwait(false);
            if (cloudFallback)
                return ("openai", true);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Fallback to OpenAI also failed");
        }

        // Last resort: use whatever the active provider is
        try
        {
            var active = _aiService.ActiveProvider;
            return (active?.ProviderId ?? requestedProviderId, true);
        }
        catch
        {
            return (requestedProviderId, true);
        }
    }

    /// <summary>
    /// Resolves an appropriate model ID for the given provider.
    /// Uses the AI service's active model for the current provider, or
    /// returns a sensible default.
    /// </summary>
    private string ResolveModelForProvider(string providerId)
    {
        // If the active provider matches, use its active model
        try
        {
            var activeProvider = _aiService.ActiveProvider;
            if (activeProvider is not null &&
                string.Equals(activeProvider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            {
                var activeModel = _aiService.ActiveModelId;
                if (!string.IsNullOrWhiteSpace(activeModel))
                    return activeModel;
            }
        }
        catch
        {
            // AI service may not be initialized yet
        }

        // Sensible defaults per provider
        return providerId.ToLowerInvariant() switch
        {
            "ollama" => "llama3.2",
            "openai" => "gpt-4o-mini",
            "anthropic" => "claude-sonnet-4-20250514",
            "local" => "llama-3.2-3b-instruct-q4_k_m.gguf",
            _ => "llama3.2"
        };
    }
}
