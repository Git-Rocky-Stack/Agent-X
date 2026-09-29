using Serilog;

namespace AgentX.Core.Services.Plugins;

/// <summary>
/// Provides a plugin with controlled, safe access to host application resources.
/// An instance of this interface is created per plugin by <see cref="IPluginService"/>
/// and passed to <see cref="IPlugin.InitializeAsync"/> before activation.
/// </summary>
/// <remarks>
/// Design rationale:
/// <list type="bullet">
///   <item>
///     Plugins must never receive the root <see cref="IServiceProvider"/> directly, as this
///     would allow unrestricted access to all registered services. Instead, <see cref="Services"/>
///     is a dedicated child scope containing only the services that are safe for plugin use.
///   </item>
///   <item>
///     <see cref="PluginDataPath"/> is where a plugin should keep its own files. It is a
///     convention, not a sandbox: plugins run in-process with the user's rights, and the
///     host does not restrict file-system or network access. Manifest permissions are
///     informational only.
///   </item>
///   <item>
///     The <see cref="Logger"/> is pre-enriched with the plugin identifier so that all log
///     events emitted by the plugin are automatically tagged in the structured log output.
///   </item>
/// </list>
/// </remarks>
public interface IPluginContext
{
    /// <summary>
    /// A scoped <see cref="IServiceProvider"/> that exposes only the host services
    /// approved for plugin consumption. Plugins should resolve dependencies through
    /// this provider rather than storing direct references to host objects, so that
    /// the host can revoke access cleanly when the plugin is deactivated.
    /// <para>
    /// Currently the only service offered is
    /// <see cref="AgentX.Core.Services.Inbox.IInboxService"/>, for
    /// <see cref="PluginType.DataConnector"/> plugins that push items into the Smart Inbox.
    /// The host's OAuth service is deliberately not offered: it can return the user's stored
    /// Google and Microsoft refresh tokens.
    /// </para>
    /// </summary>
    IServiceProvider Services { get; }

    /// <summary>
    /// Absolute path to a per-plugin directory where the plugin may read and write
    /// its private data (configuration files, caches, persisted state).
    /// The directory is created by the host before <see cref="IPlugin.InitializeAsync"/>
    /// is called, so plugins can assume it exists.
    /// </summary>
    /// <example><c>%LocalAppData%\AgentX\Plugins\com.vendor.myplugin\data\</c></example>
    string PluginDataPath { get; }

    /// <summary>
    /// A Serilog <see cref="ILogger"/> instance pre-enriched with the plugin identifier
    /// and version via <c>ForContext</c>, so that all structured log events emitted
    /// through this logger are automatically tagged with plugin metadata.
    /// </summary>
    Serilog.ILogger Logger { get; }
}
