namespace AgentX.Core.AI.Routing;

/// <summary>
/// Defines a routing profile that determines how the <see cref="IModelRouterService"/>
/// selects providers and models based on task type. Profiles control the overall
/// preference for local vs. cloud providers and allow per-task-type overrides.
/// </summary>
public sealed class RoutingProfile
{
    /// <summary>
    /// Unique identifier for this profile (e.g. "cost-optimized").
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable display name (e.g. "Cost Optimized").
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Description of the profile's routing strategy.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// When true, the router prefers local (on-device) providers unless a
    /// task-specific override or quality requirement demands a cloud provider.
    /// </summary>
    public bool PreferLocalFirst { get; init; }

    /// <summary>
    /// Per-task-type provider overrides. Key = task type name (e.g. "code"),
    /// Value = provider ID (e.g. "openai"). Takes precedence over PreferLocalFirst.
    /// </summary>
    public Dictionary<string, string> TaskOverrides { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    // ── Default Profiles ───────────────────────────────────────────

    /// <summary>
    /// Cost-optimized profile: prefers local providers for everything to minimize API spend.
    /// Falls back to cloud only for tasks that cannot run locally (if local unavailable).
    /// </summary>
    public static RoutingProfile CostOptimized { get; } = new()
    {
        Id = "cost-optimized",
        DisplayName = "Cost Optimized",
        Description = "Prefer local models for all tasks to minimize API costs. Cloud providers used only as fallback.",
        PreferLocalFirst = true,
        TaskOverrides = new(StringComparer.OrdinalIgnoreCase)
        {
            // All tasks prefer local by default in this profile; no overrides needed.
        }
    };

    /// <summary>
    /// Quality-optimized profile: prefers cloud providers for quality-critical tasks
    /// (analysis, generation, code, creative), local for simple tasks.
    /// </summary>
    public static RoutingProfile QualityOptimized { get; } = new()
    {
        Id = "quality-optimized",
        DisplayName = "Quality Optimized",
        Description = "Use cloud models for quality-critical tasks (analysis, code, creative). Local for simple tasks.",
        PreferLocalFirst = false,
        TaskOverrides = new(StringComparer.OrdinalIgnoreCase)
        {
            ["analysis"] = "openai",
            ["generation"] = "openai",
            ["code"] = "openai",
            ["creative"] = "openai",
        }
    };

    /// <summary>
    /// Balanced profile: local for speed-friendly tasks, cloud for quality-critical tasks.
    /// This is the default profile.
    /// </summary>
    public static RoutingProfile Balanced { get; } = new()
    {
        Id = "balanced",
        DisplayName = "Balanced",
        Description = "Local models for fast/simple tasks, cloud models for quality-critical tasks.",
        PreferLocalFirst = true,
        TaskOverrides = new(StringComparer.OrdinalIgnoreCase)
        {
            ["analysis"] = "openai",
            ["code"] = "anthropic",
            ["creative"] = "anthropic",
        }
    };

    // ── Lookup ─────────────────────────────────────────────────────

    private static readonly Dictionary<string, RoutingProfile> _defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        [CostOptimized.Id] = CostOptimized,
        [QualityOptimized.Id] = QualityOptimized,
        [Balanced.Id] = Balanced,
    };

    /// <summary>
    /// All default profile identifiers, useful for UI population.
    /// </summary>
    public static IReadOnlyList<string> AllDefaultIds => _defaults.Keys.ToList().AsReadOnly();

    /// <summary>
    /// All default profiles.
    /// </summary>
    public static IReadOnlyList<RoutingProfile> AllDefaults => _defaults.Values.ToList().AsReadOnly();

    /// <summary>
    /// Resolves a default profile by its identifier. Returns <see cref="Balanced"/> for unknown IDs.
    /// </summary>
    /// <param name="profileId">The profile identifier (case-insensitive).</param>
    /// <returns>The matching <see cref="RoutingProfile"/>, or <see cref="Balanced"/> as fallback.</returns>
    public static RoutingProfile FromId(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return Balanced;

        return _defaults.TryGetValue(profileId, out var profile) ? profile : Balanced;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{DisplayName} ({Id})";

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is RoutingProfile other && Id.Equals(other.Id, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override int GetHashCode() => Id.ToLowerInvariant().GetHashCode();
}
