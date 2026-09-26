namespace AgentX.Core.Services.Plugins;

/// <summary>
/// One plugin that could not be activated at application start.
/// </summary>
/// <param name="PluginId">Reverse-DNS plugin identifier.</param>
/// <param name="Reason">Why activation failed, suitable for a log line or a notification.</param>
public sealed record PluginActivationFailure(string PluginId, string Reason);

/// <summary>
/// Result of <see cref="IPluginService.ActivateEnabledPluginsAsync"/>.
/// </summary>
/// <param name="Activated">Plugin IDs that were loaded and activated, in activation order.</param>
/// <param name="Failed">
/// Plugins that could not be activated. Each is marked disabled so the Plugin Manager does
/// not show a plugin as running when it is not; the user can enable it again to retry.
/// </param>
public sealed record PluginActivationSummary(
    IReadOnlyList<string> Activated,
    IReadOnlyList<PluginActivationFailure> Failed);

/// <summary>
/// Result of <see cref="IPluginService.UninstallPluginAsync"/>.
/// </summary>
/// <param name="Found">False when no plugin with the given ID was installed.</param>
/// <param name="LeftoverDirectory">
/// The install directory when it could not be fully deleted (for example because Windows
/// still held the unloaded assembly open); null when every file was removed.
/// </param>
public sealed record PluginUninstallResult(bool Found, string? LeftoverDirectory)
{
    /// <summary>True when the plugin was found and its files are gone.</summary>
    public bool FilesRemoved => Found && LeftoverDirectory is null;
}
