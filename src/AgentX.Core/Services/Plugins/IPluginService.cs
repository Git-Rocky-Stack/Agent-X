using AgentX.Core.Data.Entities;

namespace AgentX.Core.Services.Plugins;

/// <summary>
/// Orchestrates the full lifecycle of AgentX plugins: installation, uninstallation,
/// enable/disable toggling, and runtime instance management.
/// </summary>
/// <remarks>
/// Implementations must ensure thread safety for all methods because they may be called
/// concurrently from the UI thread and background services.
/// </remarks>
public interface IPluginService
{
    /// <summary>
    /// Returns all plugins that are currently recorded in the database, regardless of
    /// whether they are enabled or actively loaded in memory.
    /// Results are ordered by <see cref="PluginEntity.Name"/> ascending.
    /// </summary>
    /// <returns>A read-only snapshot of all installed plugin records.</returns>
    Task<IReadOnlyList<PluginEntity>> GetInstalledPluginsAsync();

    /// <summary>
    /// Installs a plugin from a <c>.agentx-plugin</c> package file (a renamed ZIP archive).
    /// </summary>
    /// <remarks>
    /// Installation steps performed by the implementation:
    /// <list type="number">
    ///   <item>Validate that <paramref name="packagePath"/> exists and is a readable file.</item>
    ///   <item>Extract and deserialize <c>manifest.json</c> from the archive root.</item>
    ///   <item>Validate mandatory manifest fields (Id, Name, Version, EntryAssembly).</item>
    ///   <item>Refuse a package whose <see cref="PluginManifest.MinAppVersion"/> is newer than the
    ///   running Agent-X, or whose <see cref="PluginManifest.Dependencies"/> are not installed.</item>
    ///   <item>Check that no plugin with the same <see cref="PluginEntity.PluginId"/> is already installed.</item>
    ///   <item>Extract all archive entries to <c>%LocalAppData%\AgentX\Plugins\{manifest.Id}\</c>.</item>
    ///   <item>Create a <see cref="PluginEntity"/> row in the database with <see cref="PluginEntity.IsEnabled"/> = <see langword="false"/>.</item>
    /// </list>
    /// The plugin is intentionally <em>not</em> activated during installation. The caller must
    /// follow up with <see cref="EnablePluginAsync"/> to activate it.
    /// </remarks>
    /// <param name="packagePath">Absolute path to the <c>.agentx-plugin</c> file to install.</param>
    /// <returns>The newly created <see cref="PluginEntity"/> record.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="packagePath"/> is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the package file does not exist.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the manifest is invalid, required fields are missing, the plugin needs a newer
    /// Agent-X or a dependency that is not installed, or a plugin with the same ID is already installed.
    /// </exception>
    Task<PluginEntity> InstallPluginAsync(string packagePath);

    /// <summary>
    /// Uninstalls the plugin identified by its surrogate database key.
    /// </summary>
    /// <remarks>
    /// Uninstallation steps:
    /// <list type="number">
    ///   <item>If the plugin is currently active, call <see cref="IPlugin.DeactivateAsync"/> then <see cref="IPlugin.Dispose"/>.</item>
    ///   <item>Unload the plugin's <see cref="System.Runtime.Loader.AssemblyLoadContext"/> and wait (bounded)
    ///   for it to be collected, so the assembly files are released.</item>
    ///   <item>Delete all files under the plugin's <see cref="PluginEntity.InstallPath"/> directory, retrying
    ///   a bounded number of times while the files are still locked.</item>
    ///   <item>Remove the <see cref="PluginEntity"/> row from the database.</item>
    /// </list>
    /// If the plugin is not found, the method returns a result with <c>Found = false</c>.
    /// </remarks>
    /// <param name="id">The surrogate primary key of the <see cref="PluginEntity"/> to remove.</param>
    /// <returns>
    /// Whether the plugin was found, and the install directory when files could not be deleted
    /// (so the caller can tell the user instead of reporting a clean uninstall).
    /// </returns>
    Task<PluginUninstallResult> UninstallPluginAsync(long id);

    /// <summary>
    /// Enables the plugin with the given database key, loads its assembly into a dedicated
    /// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>, and calls
    /// <see cref="IPlugin.InitializeAsync"/> followed by <see cref="IPlugin.ActivateAsync"/>.
    /// </summary>
    /// <remarks>
    /// If the plugin is already enabled and loaded, this method is a no-op.
    /// <see cref="PluginEntity.IsEnabled"/> and <see cref="PluginEntity.LastActivatedAt"/> are
    /// persisted to the database on success. Initialization and activation are each given a
    /// bounded time.
    /// </remarks>
    /// <param name="id">The surrogate primary key of the <see cref="PluginEntity"/> to enable.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the plugin entity is not found, the plugin needs a newer Agent-X, a declared
    /// dependency is not enabled, the entry assembly cannot be loaded, or no type implementing
    /// <see cref="IPlugin"/> is discovered in the assembly.
    /// </exception>
    Task EnablePluginAsync(long id);

    /// <summary>
    /// Disables the plugin with the given database key.
    /// Calls <see cref="IPlugin.DeactivateAsync"/> and <see cref="IPlugin.Dispose"/>,
    /// unloads the <see cref="System.Runtime.Loader.AssemblyLoadContext"/>,
    /// and persists <see cref="PluginEntity.IsEnabled"/> = <see langword="false"/> to the database.
    /// </summary>
    /// <remarks>
    /// If the plugin is not currently active in memory, only the database flag is updated.
    /// </remarks>
    /// <param name="id">The surrogate primary key of the <see cref="PluginEntity"/> to disable.</param>
    Task DisablePluginAsync(long id);

    /// <summary>
    /// Loads and activates every plugin whose <see cref="PluginEntity.IsEnabled"/> flag is set,
    /// dependencies first. Call once at application start, after the database is ready.
    /// </summary>
    /// <remarks>
    /// A plugin that fails to activate is logged, marked disabled (so the Plugin Manager never
    /// shows a plugin as running when it is not), and reported in the result; the others still
    /// start.
    /// </remarks>
    Task<PluginActivationSummary> ActivateEnabledPluginsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates, disposes, and unloads every active plugin, dependents first, without
    /// changing <see cref="PluginEntity.IsEnabled"/>, so they are activated again on the next
    /// start. Call at application shutdown. Each plugin's DeactivateAsync is bounded in time.
    /// </summary>
    Task DeactivateAllPluginsAsync();

    /// <summary>
    /// Returns all plugin instances that are currently loaded and active in memory.
    /// Plugins that are installed but disabled are not included.
    /// </summary>
    /// <returns>A read-only snapshot of active <see cref="IPlugin"/> instances.</returns>
    Task<IReadOnlyList<IPlugin>> GetActivePluginsAsync();

    /// <summary>
    /// Retrieves the active, loaded instance of a specific plugin cast to <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">
    /// A type that extends <see cref="IPlugin"/>, such as <see cref="IDocumentProcessorPlugin"/>.
    /// </typeparam>
    /// <param name="pluginId">
    /// The stable reverse-DNS plugin identifier (e.g. <c>com.vendor.myplugin</c>),
    /// matching <see cref="PluginEntity.PluginId"/>.
    /// </param>
    /// <returns>
    /// The plugin instance cast to <typeparamref name="T"/>, or <see langword="null"/> if the
    /// plugin is not installed, not currently active, or does not implement <typeparamref name="T"/>.
    /// </returns>
    Task<T?> GetPluginInstanceAsync<T>(string pluginId) where T : class, IPlugin;
}
