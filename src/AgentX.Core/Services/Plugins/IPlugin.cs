namespace AgentX.Core.Services.Plugins;

/// <summary>
/// Base interface that every AgentX plugin must implement.
/// Plugins are loaded into isolated <see cref="System.Runtime.Loader.AssemblyLoadContext"/>
/// instances and managed by <see cref="IPluginService"/>.
/// </summary>
/// <remarks>
/// Lifecycle order on install/activation:
/// <list type="number">
///   <item><see cref="InitializeAsync"/> — called once after the assembly is loaded, before any UI is shown.</item>
///   <item><see cref="ActivateAsync"/> — called when the user enables the plugin.</item>
///   <item><see cref="DeactivateAsync"/> — called before the plugin is disabled or uninstalled.</item>
///   <item><see cref="IDisposable.Dispose"/> — called after deactivation to free unmanaged resources.</item>
/// </list>
/// </remarks>
public interface IPlugin : IDisposable
{
    /// <summary>
    /// Globally unique identifier for this plugin, matching the <see cref="PluginManifest.Id"/>
    /// declared in the plugin package. Must be a stable value that does not change between versions.
    /// </summary>
    string Id { get; }

    /// <summary>Human-readable display name shown in the Plugin Manager UI.</summary>
    string Name { get; }

    /// <summary>Semantic version string (e.g. "1.2.0") matching the manifest.</summary>
    string Version { get; }

    /// <summary>Display name of the individual or organization that authored the plugin.</summary>
    string Author { get; }

    /// <summary>Short description of what the plugin does, shown in the Plugin Manager UI.</summary>
    string Description { get; }

    /// <summary>Declares which extension point this plugin targets.</summary>
    PluginType Type { get; }

    /// <summary>
    /// Called once immediately after the plugin assembly is loaded into its
    /// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>. Use this method to
    /// perform one-time setup: read persisted configuration, register services, validate
    /// dependencies. Do NOT start background work here — use <see cref="ActivateAsync"/>.
    /// </summary>
    /// <param name="context">
    /// Safe-access context provided by the host. Grants a scoped service provider,
    /// a per-plugin data storage directory, and a pre-configured logger.
    /// </param>
    Task InitializeAsync(IPluginContext context);

    /// <summary>
    /// Called when the user enables the plugin (or on application start if the plugin
    /// was previously enabled). Start background services and register extension points here.
    /// </summary>
    Task ActivateAsync();

    /// <summary>
    /// Called before the plugin is disabled or uninstalled. Stop background services,
    /// flush pending data, and release hold on shared resources. This method must
    /// complete within a reasonable timeout; the host will not wait indefinitely.
    /// </summary>
    Task DeactivateAsync();
}

/// <summary>
/// Classifies the primary extension point that a plugin targets.
/// A plugin may implement multiple roles, but should declare its dominant type here
/// so the Plugin Manager can group and filter plugins correctly.
/// </summary>
public enum PluginType
{
    /// <summary>
    /// Handles parsing and text extraction for one or more document formats
    /// (e.g. a plugin that adds support for AutoCAD DWG files).
    /// </summary>
    DocumentProcessor,

    /// <summary>
    /// Integrates an additional AI inference backend (e.g. a custom OpenAI-compatible endpoint,
    /// Anthropic Claude, or a local GGUF model runner).
    /// </summary>
    AiProvider,

    /// <summary>
    /// Adds one or more entries to the Quick Actions panel, which appear as
    /// single-click commands that operate on the current document or selection.
    /// </summary>
    QuickAction,

    /// <summary>
    /// Provides a custom step type that can be inserted into the Workflow builder,
    /// extending beyond the built-in AiPrompt, Transform, and Filter steps.
    /// </summary>
    WorkflowStep,

    /// <summary>
    /// Supplies a visual theme (color palette, typography overrides) applied
    /// application-wide via WinUI 3 ResourceDictionary injection.
    /// </summary>
    Theme,

    /// <summary>
    /// Catch-all for plugins that do not fit the above categories, or that
    /// span multiple roles and cannot be reduced to a single type.
    /// </summary>
    Custom
}
