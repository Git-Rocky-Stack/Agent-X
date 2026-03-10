namespace AgentX.Core.Services.FeatureFlags;

/// <summary>
/// Provides a simple feature flag mechanism for staged rollout of features.
/// Flags are persisted in the user settings store.
/// </summary>
public interface IFeatureFlagService
{
    /// <summary>Returns true if the named feature is enabled.</summary>
    bool IsEnabled(string featureName);

    /// <summary>Returns true if the feature is enabled, using <paramref name="defaultValue"/> for unregistered flags.</summary>
    bool IsEnabled(string featureName, bool defaultValue);

    /// <summary>Enables or disables a feature flag and persists the override.</summary>
    Task SetFlagAsync(string featureName, bool enabled);

    /// <summary>Returns all known feature flags with their current effective state.</summary>
    IReadOnlyDictionary<string, bool> GetAllFlags();

    /// <summary>Removes the user override for a flag, reverting it to its default value.</summary>
    Task ResetFlagAsync(string featureName);

    /// <summary>Loads persisted flag overrides from storage. Call once at startup.</summary>
    Task InitializeAsync();
}
