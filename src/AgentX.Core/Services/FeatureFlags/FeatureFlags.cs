namespace AgentX.Core.Services.FeatureFlags;

/// <summary>
/// Registry of all known feature flags with their default states.
/// Only flags that code actually checks belong here: a registered flag that gates nothing tells
/// anyone reading <see cref="IFeatureFlagService.GetAllFlags"/> that a feature can be switched
/// off when it cannot. Add a flag together with the <c>IsEnabled</c> check that honors it.
/// </summary>
public static class FeatureFlags
{
    // -- AI Features -------------------------------------------------
    /// <summary>Checked by <c>AutoTagService</c>.</summary>
    public static readonly FeatureFlag AutoTagging = new("ai.auto_tagging", true, "Automatically tag documents during indexing");

    // -- Search ------------------------------------------------------
    /// <summary>Checked by <c>SearchCacheService</c>.</summary>
    public static readonly FeatureFlag SearchCaching = new("search.caching", true, "Cache search results for faster repeated queries");

    // -- Intelligence ------------------------------------------------
    /// <summary>Checked by <c>DuplicateDetectionService</c>.</summary>
    public static readonly FeatureFlag DuplicateDetection = new("intelligence.duplicate_detection", true, "Detect duplicate/near-duplicate documents");

    /// <summary>Returns all registered feature flags.</summary>
    public static IReadOnlyList<FeatureFlag> All { get; } = new[]
    {
        AutoTagging,
        SearchCaching,
        DuplicateDetection,
    };
}

/// <summary>
/// Represents a single feature flag with its name, default value, and description.
/// </summary>
public sealed record FeatureFlag(string Name, bool DefaultValue, string Description);
