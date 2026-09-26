using System.Text;
using AgentX.Core.AI.Providers;
using AgentX.Core.Services.Settings;
using Serilog;

namespace AgentX.Core.AI.Routing;

/// <summary>
/// Routes prompts to the optimal AI provider/model by combining task type detection,
/// routing profile preferences, and available provider checks.
/// </summary>
/// <remarks>
/// Routing only decides: it never switches the active provider or changes settings. Availability
/// comes from <see cref="IAiService.IsProviderAvailableAsync"/>, which reuses recent connection
/// checks, so routing does not re-probe every provider on every message. A local preference
/// never falls back to a cloud provider the user has not selected.
/// </remarks>
public sealed class ModelRouterService : IModelRouterService
{
    private static readonly string[] LocalProviderIds = ["ollama", "local"];
    private static readonly string[] CloudProviderIds = ["openai", "anthropic"];

    private readonly IAiService _aiService;
    private readonly ITaskTypeDetector _taskTypeDetector;
    private readonly ISettingsService? _settingsService;
    private readonly ILogger _logger;

    // Set by SetActiveProfile; wins over the saved setting for the rest of the session.
    private volatile RoutingProfile? _selectedProfile;

    // Last profile read from the saved settings.
    private volatile RoutingProfile? _savedProfile;

    /// <inheritdoc />
    public RoutingProfile ActiveProfile => _selectedProfile ?? _savedProfile ?? RoutingProfile.Balanced;

    /// <inheritdoc />
    public event EventHandler<RoutingDecision>? DecisionMade;

    /// <summary>Creates the router.</summary>
    /// <param name="aiService">Provider registry and availability checks.</param>
    /// <param name="taskTypeDetector">Classifies prompts into task types.</param>
    /// <param name="logger">Serilog logger.</param>
    /// <param name="settingsService">
    /// Optional settings source. When supplied, the saved routing profile
    /// (<see cref="AppSettings.ActiveRoutingProfileId"/>) is used until
    /// <see cref="SetActiveProfile(RoutingProfile)"/> selects another one.
    /// </param>
    public ModelRouterService(
        IAiService aiService,
        ITaskTypeDetector taskTypeDetector,
        ILogger logger,
        ISettingsService? settingsService = null)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _taskTypeDetector = taskTypeDetector ?? throw new ArgumentNullException(nameof(taskTypeDetector));
        _settingsService = settingsService;
        _logger = logger.ForContext<ModelRouterService>();
    }

    /// <inheritdoc />
    public void SetActiveProfile(RoutingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _selectedProfile = profile;
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
        var profile = await ResolveProfileAsync().ConfigureAwait(false);
        var activeProviderId = TryGetActiveProviderId();
        var reason = new StringBuilder();

        // 1. Candidate providers, most preferred first.
        var candidates = new List<string>();
        var prefersLocal = profile.PreferLocalFirst ||
                           (taskType.PreferLocal && taskType.PreferSpeed) ||
                           string.Equals(taskType.Name, TaskType.Embedding.Name, StringComparison.OrdinalIgnoreCase);

        if (profile.TaskOverrides.TryGetValue(taskType.Name, out var overrideProviderId) &&
            !string.IsNullOrWhiteSpace(overrideProviderId))
        {
            candidates.Add(overrideProviderId.Trim());
            reason.Append($"Profile '{profile.Id}' overrides '{taskType.Name}' to provider '{overrideProviderId}'. ");
        }
        else if (prefersLocal)
        {
            reason.Append(profile.PreferLocalFirst
                ? $"Profile '{profile.Id}' prefers local for task '{taskType.Name}'. "
                : $"Task '{taskType.Name}' prefers local/speed. ");
        }
        else
        {
            reason.Append($"Task '{taskType.Name}' prefers cloud/quality. ");
        }

        candidates.AddRange(prefersLocal
            ? PreferredOrder(activeProviderId, LocalProviderIds)
            : PreferredOrder(activeProviderId, CloudProviderIds));

        // 2. First registered and reachable candidate. Nothing is switched here.
        string? targetProviderId = null;
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (await IsAvailableAsync(candidate, ct).ConfigureAwait(false))
            {
                targetProviderId = candidate;
                break;
            }
        }

        if (targetProviderId is null)
        {
            // Stay with the provider the user selected rather than sending the prompt to a
            // provider they never chose (a local preference must not end up in the cloud).
            targetProviderId = activeProviderId ?? candidates[0];
            reason.Append($"No preferred provider is available; using '{targetProviderId}'. ");
        }
        else if (!string.Equals(targetProviderId, candidates[0], StringComparison.OrdinalIgnoreCase))
        {
            reason.Append($"'{candidates[0]}' is unavailable; using '{targetProviderId}'. ");
        }

        // 3. The model for that provider: the active model when it is the active provider,
        // otherwise the provider's own configured default.
        var targetModelId = ResolveModelForProvider(targetProviderId, activeProviderId);

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

    // Private helpers

    /// <summary>
    /// The profile chosen with <see cref="SetActiveProfile(RoutingProfile)"/>, else the saved one,
    /// else <see cref="RoutingProfile.Balanced"/>.
    /// </summary>
    private async Task<RoutingProfile> ResolveProfileAsync()
    {
        var selected = _selectedProfile;
        if (selected is not null)
            return selected;

        if (_settingsService is not null)
        {
            try
            {
                var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);
                _savedProfile = RoutingProfile.FromId(settings.ActiveRoutingProfileId);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not read the saved routing profile; using {Profile}", ActiveProfile.Id);
            }
        }

        return ActiveProfile;
    }

    /// <summary>The active provider first when it belongs to <paramref name="group"/>, then the group.</summary>
    private static IEnumerable<string> PreferredOrder(string? activeProviderId, string[] group)
    {
        if (activeProviderId is not null && group.Contains(activeProviderId, StringComparer.OrdinalIgnoreCase))
            yield return activeProviderId;

        foreach (var id in group)
            yield return id;
    }

    private string? TryGetActiveProviderId()
    {
        try
        {
            return _aiService.ActiveProvider?.ProviderId;
        }
        catch (InvalidOperationException)
        {
            return null; // not initialized yet
        }
    }

    private async Task<bool> IsAvailableAsync(string providerId, CancellationToken ct)
    {
        if (_aiService.GetProvider(providerId) is null)
            return false; // not registered, for example a cloud provider without an API key

        try
        {
            return await _aiService.IsProviderAvailableAsync(providerId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Availability check for provider '{ProviderId}' failed", providerId);
            return false;
        }
    }

    /// <summary>
    /// Resolves the model for <paramref name="providerId"/>: the active model when the provider is
    /// already active, otherwise the provider's configured default, so a model id is never sent
    /// to a provider it does not belong to.
    /// </summary>
    private string ResolveModelForProvider(string providerId, string? activeProviderId)
    {
        try
        {
            if (string.Equals(activeProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            {
                var activeModel = _aiService.ActiveModelId;
                if (!string.IsNullOrWhiteSpace(activeModel))
                    return activeModel;
            }

            var configured = _aiService.GetDefaultModelId(providerId);
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // AI service may not be initialized yet
        }

        return providerId.ToLowerInvariant() switch
        {
            "openai" => OpenAiProvider.DefaultModelId,
            "anthropic" => AnthropicProvider.DefaultModelId,
            "local" => BuiltInModelBootstrap.DefaultModelFileName,
            _ => "llama3.2"
        };
    }
}
