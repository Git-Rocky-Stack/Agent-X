using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using AgentX.Core.Constants;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Documents.Models;
using AgentX.Core.Helpers;
using AgentX.Core.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AgentX.Core.Services.Plugins;

/// <summary>
/// Production implementation of <see cref="IPluginService"/>.
/// </summary>
/// <remarks>
/// Assembly isolation strategy:
/// Each plugin is loaded into its own <see cref="PluginLoadContext"/> (a collectible subclass of
/// <see cref="AssemblyLoadContext"/>), so that unloading a plugin releases all types and allows
/// the GC to reclaim the memory. Private assemblies next to the plugin's entry assembly are
/// resolved locally; assemblies the host already provides (AgentX.Core, Serilog,
/// Microsoft.Extensions.*, the framework) always come from the default context so the plugin
/// and the host share one <see cref="IPlugin"/> type.
///
/// Isolation is about type loading only. Plugins run in-process with the user's full rights;
/// there is no file-system or network sandbox, and manifest permissions are informational.
///
/// Thread safety:
/// <see cref="_loadedPlugins"/> is a <see cref="ConcurrentDictionary{TKey,TValue}"/> whose
/// individual <c>TryAdd</c> / <c>TryRemove</c> / <c>TryGetValue</c> operations are atomic.
/// For the load-then-register sequence in <see cref="EnablePluginAsync"/>, a
/// <see cref="SemaphoreSlim"/> guard prevents two concurrent callers from double-loading the
/// same plugin assembly.
/// </remarks>
public sealed class PluginService : IPluginService, IPluginDocumentProcessorSource
{
    // -- Constants -----------------------------------------------------------

    private const string ManifestFileName = "manifest.json";
    private const string PluginsSubDirectory = "Plugins";
    private const string PluginDataSubDirectory = "data";

    /// <summary>Delete attempts uninstall makes (with a GC pass between them) before reporting leftovers.</summary>
    private const int DeleteAttempts = 6;

    /// <summary>GC passes made after an uninstall unload while waiting for the load context to be collected.</summary>
    private const int UnloadCollectionPasses = 10;

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // -- Fields --------------------------------------------------------------

    private readonly AgentXDbContext _dbContext;
    private readonly IServiceProvider _rootServiceProvider;
    private readonly IValidator<PluginManifest> _manifestValidator;
    private readonly ILogger _log;
    private readonly Version _hostVersion;

    /// <summary>
    /// Guards the load-then-register sequence in <see cref="EnablePluginAsync"/> so that two
    /// concurrent callers cannot race to double-load the same plugin assembly.
    /// </summary>
    private readonly SemaphoreSlim _enableLock = new(1, 1);

    /// <summary>
    /// Maps plugin ID to a tuple of the active <see cref="IPlugin"/> instance and the
    /// <see cref="AssemblyLoadContext"/> that owns its assembly. Populated by
    /// <see cref="EnablePluginAsync"/> and drained by <see cref="DeactivateAndUnloadAsync"/>.
    /// </summary>
    private readonly ConcurrentDictionary<string, (IPlugin Instance, AssemblyLoadContext Alc)> _loadedPlugins
        = new(StringComparer.Ordinal);

    /// <summary>Plugin IDs in activation order; shutdown deactivates in reverse.</summary>
    private readonly List<string> _activationOrder = new();
    private readonly object _activationOrderGate = new();

    /// <summary>Longest the host waits for a plugin's InitializeAsync or ActivateAsync.</summary>
    internal TimeSpan LifecycleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Longest the host waits for a plugin's DeactivateAsync before disposing and unloading it
    /// anyway (the promise made in <see cref="IPlugin.DeactivateAsync"/>).
    /// </summary>
    internal TimeSpan DeactivationTimeout { get; set; } = TimeSpan.FromSeconds(10);

    // -- Constructor ---------------------------------------------------------

    /// <summary>
    /// Initializes <see cref="PluginService"/> with the required dependencies.
    /// </summary>
    /// <param name="dbContext">
    /// The application <see cref="AgentXDbContext"/>. Used exclusively for plugin metadata
    /// persistence. Must not be null.
    /// </param>
    /// <param name="rootServiceProvider">
    /// The root <see cref="IServiceProvider"/> used to resolve the few host services approved
    /// for plugins (currently <see cref="AgentX.Core.Services.Inbox.IInboxService"/>) into the
    /// plugin-scoped container. Must not be null.
    /// </param>
    /// <param name="manifestValidator">
    /// Strict <see cref="IValidator{T}"/> for <see cref="PluginManifest"/>, applied to every
    /// package manifest before extraction. Rejects path-injection in the plugin ID and entry
    /// assembly name. Must not be null.
    /// </param>
    /// <param name="logger">
    /// The application-level Serilog <see cref="ILogger"/>. A context-specific child logger
    /// enriched with <c>SourceContext=PluginService</c> is derived via
    /// <see cref="Log.ForContext{T}()"/>.
    /// </param>
    public PluginService(
        AgentXDbContext dbContext,
        IServiceProvider rootServiceProvider,
        IValidator<PluginManifest> manifestValidator,
        ILogger logger)
        : this(dbContext, rootServiceProvider, manifestValidator, logger, AppVersionInfo.Display)
    {
    }

    /// <summary>
    /// Test seam: the same service with an explicit host version for <c>minAppVersion</c> checks.
    /// </summary>
    internal PluginService(
        AgentXDbContext dbContext,
        IServiceProvider rootServiceProvider,
        IValidator<PluginManifest> manifestValidator,
        ILogger logger,
        string hostVersion)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _rootServiceProvider = rootServiceProvider ?? throw new ArgumentNullException(nameof(rootServiceProvider));
        _manifestValidator = manifestValidator ?? throw new ArgumentNullException(nameof(manifestValidator));
        _log = logger?.ForContext<PluginService>()
               ?? throw new ArgumentNullException(nameof(logger));
        _hostVersion = TryParseVersionCore(hostVersion, out var parsed) ? parsed : new Version(0, 0, 0);

        _log.Information("PluginService initialized. PluginBaseDir={PluginBaseDir}", GetPluginBaseDirectory());
    }

    // -- IPluginService: GetInstalledPluginsAsync -----------------------------

    /// <inheritdoc />
    public async Task<IReadOnlyList<PluginEntity>> GetInstalledPluginsAsync()
    {
        _log.Debug("Retrieving installed plugins from database");

        var plugins = await _dbContext.Plugins
            .OrderBy(p => p.Name)
            .AsNoTracking()
            .ToListAsync()
            .ConfigureAwait(false);

        _log.Debug("Found {Count} installed plugin(s)", plugins.Count);
        return plugins;
    }

    // -- IPluginService: InstallPluginAsync -----------------------------------

    /// <inheritdoc />
    public async Task<PluginEntity> InstallPluginAsync(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        _log.Information("Installing plugin from package {PackagePath}", packagePath);

        if (!File.Exists(packagePath))
            throw new FileNotFoundException($"Plugin package not found: {packagePath}", packagePath);

        // Step 1: read and strictly validate manifest from the archive.
        var manifest = await ReadManifestFromPackageAsync(packagePath).ConfigureAwait(false);

        var validation = _manifestValidator.Validate(manifest);
        if (!validation.IsValid)
        {
            var details = string.Join(" ", validation.Errors.Select(e => $"{e.FieldName}: {e.Message}"));
            throw new InvalidOperationException($"Plugin manifest is invalid: {details}");
        }

        _log.Information(
            "Manifest validated. PluginId={PluginId} Name={Name} Version={Version} Author={Author}",
            manifest.Id, manifest.Name, manifest.Version, manifest.Author);

        // Step 1b: the manifest's compatibility contract. A plugin built for a newer host, or
        // one whose dependencies are not installed, is refused before any file is written.
        EnsureHostVersionSatisfies(manifest.Id, manifest.MinAppVersion);
        await EnsureDependenciesInstalledAsync(manifest).ConfigureAwait(false);

        // Step 2: reject duplicate installations.
        var existing = await _dbContext.Plugins
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PluginId == manifest.Id)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            throw new InvalidOperationException(
                $"Plugin '{manifest.Id}' (version {existing.Version}) is already installed. " +
                $"Uninstall the existing plugin before installing a new version.");
        }

        // Step 3: extract all files to the plugin install directory.
        var installPath = GetPluginInstallPath(manifest.Id);
        Directory.CreateDirectory(installPath);

        _log.Information("Extracting plugin files to {InstallPath}", installPath);

        try
        {
            await ExtractPackageAsync(packagePath, installPath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Roll back the partially extracted directory so no corrupted installation is left behind.
            _log.Error(ex, "Extraction failed - removing partially extracted directory {InstallPath}", installPath);
            DeleteDirectoryQuietly(installPath);
            throw new InvalidOperationException(
                $"Failed to extract plugin package '{Path.GetFileName(packagePath)}': {ex.Message}", ex);
        }

        // Step 3b: extract README content for documentation viewer.
        string? readmeContent = null;

        // Prefer a standalone README file in the extracted directory over the manifest field.
        var readmePath = Directory.GetFiles(installPath, "README*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();

        if (readmePath != null)
        {
            try
            {
                var readmeBytes = await File.ReadAllBytesAsync(readmePath).ConfigureAwait(false);
                if (readmeBytes.Length <= AppConstants.MaxPluginReadmeBytes)
                {
                    readmeContent = System.Text.Encoding.UTF8.GetString(readmeBytes);
                    _log.Debug("Extracted README from file ({Bytes} bytes) for plugin '{PluginId}'",
                        readmeBytes.Length, manifest.Id);
                }
                else
                {
                    _log.Warning(
                        "README file for plugin '{PluginId}' exceeds {MaxBytes} byte limit ({ActualBytes} bytes) - skipped",
                        manifest.Id, AppConstants.MaxPluginReadmeBytes, readmeBytes.Length);
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to read README file for plugin '{PluginId}'", manifest.Id);
            }
        }
        else if (!string.IsNullOrWhiteSpace(manifest.Readme))
        {
            // Fall back to the inline readme field from the manifest.
            var manifestReadmeBytes = System.Text.Encoding.UTF8.GetByteCount(manifest.Readme);
            if (manifestReadmeBytes <= AppConstants.MaxPluginReadmeBytes)
            {
                readmeContent = manifest.Readme;
                _log.Debug("Using inline manifest readme ({Bytes} bytes) for plugin '{PluginId}'",
                    manifestReadmeBytes, manifest.Id);
            }
            else
            {
                _log.Warning(
                    "Inline manifest readme for plugin '{PluginId}' exceeds {MaxBytes} byte limit - skipped",
                    manifest.Id, AppConstants.MaxPluginReadmeBytes);
            }
        }

        // Step 4: create the database record (disabled by default).
        var entity = new PluginEntity
        {
            PluginId = manifest.Id,
            Name = manifest.Name,
            Version = manifest.Version,
            Author = manifest.Author,
            Description = manifest.Description,
            PluginType = manifest.PluginType,
            InstallPath = installPath,
            IsEnabled = false,
            InstalledAt = DateTime.UtcNow,
            ReadmeContent = readmeContent,
        };

        _dbContext.Plugins.Add(entity);
        await _dbContext.SaveChangesAsync().ConfigureAwait(false);

        _log.Information(
            "Plugin installed successfully. PluginId={PluginId} EntityId={EntityId} InstallPath={InstallPath}",
            manifest.Id, entity.Id, installPath);

        return entity;
    }

    // -- IPluginService: UninstallPluginAsync ---------------------------------

    /// <inheritdoc />
    public async Task<PluginUninstallResult> UninstallPluginAsync(long id)
    {
        _log.Information("Uninstalling plugin EntityId={EntityId}", id);

        var entity = await _dbContext.Plugins
            .FirstOrDefaultAsync(p => p.Id == id)
            .ConfigureAwait(false);

        if (entity is null)
        {
            _log.Warning("Plugin EntityId={EntityId} not found - nothing to uninstall", id);
            return new PluginUninstallResult(Found: false, LeftoverDirectory: null);
        }

        // Step 1: deactivate and unload the assembly, then wait for the collectible load
        // context to be collected so Windows releases the DLL file handles before deletion.
        await DeactivateAndUnloadAsync(entity.PluginId, waitForUnload: true).ConfigureAwait(false);

        // Step 2: delete all files on disk.
        // Containment guard: only ever recursively delete a directory that lives under the
        // plugin base directory, so a malformed stored InstallPath cannot wipe arbitrary paths.
        string? leftoverDirectory = null;
        if (!string.IsNullOrEmpty(entity.InstallPath) && Directory.Exists(entity.InstallPath))
        {
            if (PathHelper.IsPathContained(GetPluginBaseDirectory(), entity.InstallPath))
            {
                _log.Information("Deleting plugin directory {InstallPath}", entity.InstallPath);
                if (!await DeleteDirectoryWithRetryAsync(entity.InstallPath).ConfigureAwait(false))
                {
                    leftoverDirectory = entity.InstallPath;
                }
            }
            else
            {
                leftoverDirectory = entity.InstallPath;
                _log.Error(
                    "Refusing to delete plugin directory outside the plugin base directory: {InstallPath}",
                    entity.InstallPath);
            }
        }

        // Step 3: remove the database record.
        _dbContext.Plugins.Remove(entity);
        await _dbContext.SaveChangesAsync().ConfigureAwait(false);

        _log.Information("Plugin '{PluginId}' (EntityId={EntityId}) uninstalled", entity.PluginId, id);
        return new PluginUninstallResult(Found: true, LeftoverDirectory: leftoverDirectory);
    }

    // -- IPluginService: EnablePluginAsync ------------------------------------

    /// <inheritdoc />
    public async Task EnablePluginAsync(long id)
    {
        _log.Information("Enabling plugin EntityId={EntityId}", id);

        var entity = await _dbContext.Plugins
            .FirstOrDefaultAsync(p => p.Id == id)
            .ConfigureAwait(false);

        if (entity is null)
            throw new InvalidOperationException($"Plugin with EntityId={id} was not found.");

        // Guard the load-then-TryAdd sequence against concurrent callers.
        await _enableLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Already active: no-op to satisfy the idempotency guarantee on the interface.
            if (_loadedPlugins.ContainsKey(entity.PluginId))
            {
                _log.Debug(
                    "Plugin '{PluginId}' is already active - EnablePluginAsync is a no-op",
                    entity.PluginId);
                return;
            }

            await ActivateCoreAsync(entity).ConfigureAwait(false);
        }
        finally
        {
            _enableLock.Release();
        }

        // Persist enabled state and activation timestamp.
        entity.IsEnabled = true;
        entity.LastActivatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync().ConfigureAwait(false);

        _log.Information("Plugin '{PluginId}' enabled and activated successfully", entity.PluginId);
    }

    // -- IPluginService: DisablePluginAsync -----------------------------------

    /// <inheritdoc />
    public async Task DisablePluginAsync(long id)
    {
        _log.Information("Disabling plugin EntityId={EntityId}", id);

        var entity = await _dbContext.Plugins
            .FirstOrDefaultAsync(p => p.Id == id)
            .ConfigureAwait(false);

        if (entity is null)
        {
            _log.Warning("Plugin EntityId={EntityId} not found - nothing to disable", id);
            return;
        }

        // Deactivate the runtime instance (safe no-op if not currently loaded).
        await DeactivateAndUnloadAsync(entity.PluginId, waitForUnload: false).ConfigureAwait(false);

        // Persist the disabled state regardless of whether the plugin was active.
        entity.IsEnabled = false;
        await _dbContext.SaveChangesAsync().ConfigureAwait(false);

        _log.Information("Plugin '{PluginId}' (EntityId={EntityId}) disabled", entity.PluginId, id);
    }

    // -- IPluginService: application start and shutdown -----------------------

    /// <inheritdoc />
    public async Task<PluginActivationSummary> ActivateEnabledPluginsAsync(CancellationToken cancellationToken = default)
    {
        var enabled = await _dbContext.Plugins
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var activated = new List<string>();
        var failed = new List<PluginActivationFailure>();

        if (enabled.Count == 0)
        {
            _log.Debug("No enabled plugins to activate");
            return new PluginActivationSummary(activated, failed);
        }

        _log.Information("Activating {Count} enabled plugin(s)", enabled.Count);

        // Dependencies first, so a plugin's declared dependencies are already running when
        // its own dependency check runs.
        foreach (var entity in OrderByDependencies(enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _enableLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_loadedPlugins.ContainsKey(entity.PluginId))
                {
                    await ActivateCoreAsync(entity).ConfigureAwait(false);
                    entity.LastActivatedAt = DateTime.UtcNow;
                }

                activated.Add(entity.PluginId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Marking the plugin disabled keeps the Plugin Manager truthful: an enabled row
                // must mean a running plugin. Enabling it again retries and shows the error.
                _log.Error(ex,
                    "Enabled plugin '{PluginId}' could not be activated at startup; marking it disabled",
                    entity.PluginId);
                entity.IsEnabled = false;
                failed.Add(new PluginActivationFailure(entity.PluginId, ex.Message));
            }
            finally
            {
                _enableLock.Release();
            }
        }

        await _dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

        _log.Information(
            "Plugin startup activation finished. Activated={Activated} Failed={Failed}",
            activated.Count, failed.Count);

        return new PluginActivationSummary(activated, failed);
    }

    /// <inheritdoc />
    public async Task DeactivateAllPluginsAsync()
    {
        await _enableLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Dependents were activated after their dependencies, so reverse order stops them first.
            List<string> order;
            lock (_activationOrderGate)
            {
                order = Enumerable.Reverse(_activationOrder).ToList();
            }

            order.AddRange(_loadedPlugins.Keys.Where(id => !order.Contains(id, StringComparer.Ordinal)));

            foreach (var pluginId in order)
            {
                await DeactivateAndUnloadAsync(pluginId, waitForUnload: false).ConfigureAwait(false);
            }

            if (order.Count > 0)
            {
                _log.Information("Deactivated {Count} plugin(s) for shutdown", order.Count);
            }
        }
        finally
        {
            _enableLock.Release();
        }
    }

    // -- IPluginService: GetActivePluginsAsync --------------------------------

    /// <inheritdoc />
    public Task<IReadOnlyList<IPlugin>> GetActivePluginsAsync()
    {
        // ConcurrentDictionary.Values produces a snapshot; no additional locking required.
        IReadOnlyList<IPlugin> snapshot = _loadedPlugins.Values
            .Select(entry => entry.Instance)
            .ToList();

        _log.Debug("GetActivePluginsAsync returning {Count} active plugin(s)", snapshot.Count);
        return Task.FromResult(snapshot);
    }

    // -- IPluginService: GetPluginInstanceAsync<T> ----------------------------

    /// <inheritdoc />
    public Task<T?> GetPluginInstanceAsync<T>(string pluginId) where T : class, IPlugin
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        if (!_loadedPlugins.TryGetValue(pluginId, out var entry))
        {
            _log.Debug(
                "GetPluginInstanceAsync<{Type}>: plugin '{PluginId}' is not active",
                typeof(T).Name, pluginId);
            return Task.FromResult<T?>(null);
        }

        if (entry.Instance is T typed)
            return Task.FromResult<T?>(typed);

        _log.Debug(
            "GetPluginInstanceAsync<{Type}>: plugin '{PluginId}' is active but does not implement {Type}",
            typeof(T).Name, pluginId, typeof(T).Name);

        return Task.FromResult<T?>(null);
    }

    // -- IPluginDocumentProcessorSource ---------------------------------------

    /// <inheritdoc />
    public IReadOnlyList<IDocumentProcessor> GetDocumentProcessors()
    {
        List<string> order;
        lock (_activationOrderGate)
        {
            order = _activationOrder.ToList();
        }

        var processors = new List<IDocumentProcessor>();
        foreach (var pluginId in order)
        {
            if (_loadedPlugins.TryGetValue(pluginId, out var entry) && entry.Instance is IDocumentProcessor processor)
            {
                processors.Add(new GuardedPluginDocumentProcessor(pluginId, processor, _log));
            }
        }

        return processors;
    }

    // -- Private: activation --------------------------------------------------

    /// <summary>
    /// Loads, initializes, activates, and registers one plugin. Every failure unloads the
    /// plugin's load context before the exception leaves this method. Callers hold
    /// <see cref="_enableLock"/>.
    /// </summary>
    private async Task ActivateCoreAsync(PluginEntity entity)
    {
        var manifest = ReadInstalledManifest(entity);
        if (manifest is not null)
        {
            EnsureHostVersionSatisfies(entity.PluginId, manifest.MinAppVersion);
            EnsureDependenciesActive(entity.PluginId, manifest);
        }

        // Locate and load the entry assembly.
        var entryAssemblyName = GetEntryAssemblyName(entity, manifest);
        var entryDllPath = Path.Combine(entity.InstallPath, entryAssemblyName);

        // Hard guard: the resolved assembly path must stay inside the plugin's install
        // directory. GetEntryAssemblyName already rejects non-bare names; this is the
        // last line of defense before loading executable code into the process.
        if (!PathHelper.IsPathContained(entity.InstallPath, entryDllPath))
            throw new InvalidOperationException(
                $"Refusing to load plugin entry assembly outside its install directory: '{entryDllPath}'.");

        if (!File.Exists(entryDllPath))
            throw new InvalidOperationException(
                $"Plugin entry assembly not found at '{entryDllPath}'. " +
                $"The installation may be corrupt.");

        var loadContext = new PluginLoadContext(entryDllPath);
        IPlugin instance;

        try
        {
            Assembly assembly;
            try
            {
                assembly = loadContext.LoadFromAssemblyPath(entryDllPath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to load assembly '{entryDllPath}' for plugin '{entity.PluginId}': {ex.Message}", ex);
            }

            // Discover the single IPlugin implementation.
            var pluginType = DiscoverPluginType(assembly, entity.PluginId);

            try
            {
                instance = (IPlugin)Activator.CreateInstance(pluginType)!;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to instantiate '{pluginType.FullName}' for plugin '{entity.PluginId}': {ex.Message}", ex);
            }
        }
        catch
        {
            // Includes a failed type discovery: the context must not stay loaded until restart.
            loadContext.Unload();
            throw;
        }

        // Build the plugin context.
        var dataPath = GetPluginDataPath(entity.PluginId);
        Directory.CreateDirectory(dataPath);

        var pluginLogger = _log
            .ForContext("PluginId", entity.PluginId)
            .ForContext("PluginVersion", entity.Version);

        // Build a scoped service provider for the plugin, surfacing only the host services
        // that are approved for plugin consumption. IOAuthService is deliberately absent:
        // its GetCredentialAsync hands out the user's Google and Microsoft refresh tokens,
        // which third-party code must never see.
        var pluginServices = new ServiceCollection();

        // Register IInboxService for DataConnector plugins that need to push
        // external items into the Smart Inbox pipeline.
        var inboxService = _rootServiceProvider.GetService<AgentX.Core.Services.Inbox.IInboxService>();
        if (inboxService is not null)
            pluginServices.AddSingleton(inboxService);

        var pluginServiceProvider = pluginServices.BuildServiceProvider();

        var pluginContext = new PluginContext(
            pluginDataPath: dataPath,
            services: pluginServiceProvider,
            logger: pluginLogger);

        // Lifecycle: Initialize, then Activate.
        _log.Information("Initializing plugin '{PluginId}' v{Version}", entity.PluginId, entity.Version);

        try
        {
            await RunLifecycleStepAsync(() => instance.InitializeAsync(pluginContext), LifecycleTimeout).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SafeDispose(instance, entity.PluginId);
            loadContext.Unload();
            throw new InvalidOperationException(
                $"Plugin '{entity.PluginId}' threw during InitializeAsync: {DescribeLifecycleFailure(ex, LifecycleTimeout)}", ex);
        }

        _log.Information("Activating plugin '{PluginId}'", entity.PluginId);

        try
        {
            await RunLifecycleStepAsync(instance.ActivateAsync, LifecycleTimeout).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Best-effort deactivation before disposal on activation failure.
            try
            {
                await RunLifecycleStepAsync(instance.DeactivateAsync, DeactivationTimeout).ConfigureAwait(false);
            }
            catch
            {
                // Already failing; the activation error is the one reported.
            }

            SafeDispose(instance, entity.PluginId);
            loadContext.Unload();
            throw new InvalidOperationException(
                $"Plugin '{entity.PluginId}' threw during ActivateAsync: {DescribeLifecycleFailure(ex, LifecycleTimeout)}", ex);
        }

        // Register in the runtime dictionary.
        _loadedPlugins[entity.PluginId] = (instance, loadContext);
        lock (_activationOrderGate)
        {
            _activationOrder.Remove(entity.PluginId);
            _activationOrder.Add(entity.PluginId);
        }
    }

    /// <summary>
    /// Runs one plugin lifecycle call with a time limit. A call that throws synchronously
    /// surfaces the same way as one that faults.
    /// </summary>
    private static async Task RunLifecycleStepAsync(Func<Task> step, TimeSpan timeout)
    {
        var task = step() ?? Task.CompletedTask;
        await task.WaitAsync(timeout).ConfigureAwait(false);
    }

    private static string DescribeLifecycleFailure(Exception ex, TimeSpan timeout) =>
        ex is TimeoutException
            ? $"it did not finish within {timeout.TotalSeconds:0} seconds."
            : ex.Message;

    /// <summary>
    /// Orders plugins so each one's declared dependencies come first. A dependency cycle
    /// cannot be satisfied; its members are still returned and fail their dependency check.
    /// </summary>
    private IReadOnlyList<PluginEntity> OrderByDependencies(IReadOnlyList<PluginEntity> plugins)
    {
        var byId = plugins.ToDictionary(p => p.PluginId, StringComparer.Ordinal);
        var dependencies = plugins.ToDictionary(
            p => p.PluginId,
            p => GetDeclaredDependencies(ReadInstalledManifest(p), p.PluginId),
            StringComparer.Ordinal);

        var ordered = new List<PluginEntity>(plugins.Count);
        var state = new Dictionary<string, bool>(StringComparer.Ordinal); // false = visiting, true = done

        void Visit(PluginEntity plugin)
        {
            if (state.ContainsKey(plugin.PluginId))
                return;

            state[plugin.PluginId] = false;
            foreach (var dependencyId in dependencies[plugin.PluginId])
            {
                if (byId.TryGetValue(dependencyId, out var dependency))
                    Visit(dependency);
            }

            state[plugin.PluginId] = true;
            ordered.Add(plugin);
        }

        foreach (var plugin in plugins)
            Visit(plugin);

        return ordered;
    }

    // -- Private: manifest contract checks ------------------------------------

    /// <summary>
    /// Refuses a plugin whose <c>minAppVersion</c> is newer than this build of Agent-X, or
    /// is not a version at all.
    /// </summary>
    private void EnsureHostVersionSatisfies(string pluginId, string? minAppVersion)
    {
        if (string.IsNullOrWhiteSpace(minAppVersion))
            return;

        if (!TryParseVersionCore(minAppVersion, out var required))
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginId}' declares an invalid minAppVersion '{minAppVersion}'. " +
                "Use a version such as '2.1.0'.");
        }

        if (_hostVersion < required)
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginId}' requires Agent-X {minAppVersion} or later; " +
                $"this is Agent-X {_hostVersion.ToString(3)}. Update Agent-X to use this plugin.");
        }
    }

    /// <summary>Refuses an install whose declared dependencies are not installed.</summary>
    private async Task EnsureDependenciesInstalledAsync(PluginManifest manifest)
    {
        var dependencies = GetDeclaredDependencies(manifest, manifest.Id);
        if (dependencies.Count == 0)
            return;

        var installed = await _dbContext.Plugins
            .AsNoTracking()
            .Where(p => dependencies.Contains(p.PluginId))
            .Select(p => p.PluginId)
            .ToListAsync()
            .ConfigureAwait(false);

        var missing = dependencies.Except(installed, StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Plugin '{manifest.Id}' depends on plugin(s) that are not installed: " +
                $"{string.Join(", ", missing)}. Install them first.");
        }
    }

    /// <summary>Refuses activation while a declared dependency is not running.</summary>
    private void EnsureDependenciesActive(string pluginId, PluginManifest manifest)
    {
        var missing = GetDeclaredDependencies(manifest, pluginId)
            .Where(dependencyId => !_loadedPlugins.ContainsKey(dependencyId))
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginId}' depends on plugin(s) that are not installed and enabled: " +
                $"{string.Join(", ", missing)}. Enable them first.");
        }
    }

    private static IReadOnlyList<string> GetDeclaredDependencies(PluginManifest? manifest, string pluginId) =>
        manifest?.Dependencies?
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d.Trim())
            .Where(d => !string.Equals(d, pluginId, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList()
        ?? (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>
    /// Parses the numeric core of a SemVer string ("2.1.0-beta+build" becomes 2.1.0). A bare
    /// major version ("2") is read as 2.0.0.
    /// </summary>
    internal static bool TryParseVersionCore(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var core = value.Trim();
        var cut = core.IndexOfAny(['-', '+']);
        if (cut >= 0)
            core = core[..cut];
        if (!core.Contains('.'))
            core += ".0";

        if (!Version.TryParse(core, out var parsed))
            return false;

        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        return true;
    }

    // -- Private: deactivation helper -----------------------------------------

    /// <summary>
    /// Removes the plugin entry from <see cref="_loadedPlugins"/>, calls
    /// <see cref="IPlugin.DeactivateAsync"/> (bounded by <see cref="DeactivationTimeout"/>) and
    /// <see cref="IPlugin.Dispose"/>, then unloads the plugin's <see cref="AssemblyLoadContext"/>.
    /// Safe to call when the plugin is not active. With <paramref name="waitForUnload"/>, also
    /// waits (bounded) for the load context to be collected, which releases the assembly files.
    /// </summary>
    private async Task DeactivateAndUnloadAsync(string pluginId, bool waitForUnload)
    {
        var unloadedContext = await DeactivateAndStartUnloadAsync(pluginId).ConfigureAwait(false);
        if (unloadedContext is not null && waitForUnload)
        {
            WaitForUnload(unloadedContext, pluginId);
        }
    }

    /// <summary>
    /// The part of <see cref="DeactivateAndUnloadAsync"/> that touches the plugin instance.
    /// Kept separate so no reference to the instance or its load context outlives it, and
    /// returns only a weak reference that lets the caller watch the context being collected.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<WeakReference?> DeactivateAndStartUnloadAsync(string pluginId)
    {
        if (!_loadedPlugins.TryRemove(pluginId, out var entry))
        {
            // Plugin was not active; nothing to do.
            return null;
        }

        lock (_activationOrderGate)
        {
            _activationOrder.Remove(pluginId);
        }

        var (instance, alc) = entry;

        // Deactivate with a time limit; swallow failures so unload always proceeds.
        try
        {
            _log.Information("Deactivating plugin '{PluginId}'", pluginId);
            await RunLifecycleStepAsync(instance.DeactivateAsync, DeactivationTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _log.Warning(
                "Plugin '{PluginId}' did not finish DeactivateAsync within {Seconds} seconds - continuing unload",
                pluginId, DeactivationTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            _log.Error(ex,
                "Plugin '{PluginId}' threw during DeactivateAsync - continuing unload", pluginId);
        }

        // Dispose; swallow exceptions so ALC unload always proceeds.
        SafeDispose(instance, pluginId);

        // Unload the collectible AssemblyLoadContext.
        try
        {
            alc.Unload();
            _log.Debug("AssemblyLoadContext for plugin '{PluginId}' unloaded", pluginId);
        }
        catch (Exception ex)
        {
            _log.Warning(ex,
                "Could not unload AssemblyLoadContext for plugin '{PluginId}'", pluginId);
        }

        return new WeakReference(alc);
    }

    /// <summary>
    /// Unload completes only when the GC collects the load context; until then Windows keeps
    /// the plugin's DLLs open. Forces a bounded number of collections and logs when something
    /// still references plugin types.
    /// </summary>
    private void WaitForUnload(WeakReference unloadedContext, string pluginId)
    {
        for (var pass = 0; pass < UnloadCollectionPasses && unloadedContext.IsAlive; pass++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        if (unloadedContext.IsAlive)
        {
            _log.Warning(
                "Load context for plugin '{PluginId}' is still alive after unload; something still references " +
                "its types, so its files may stay locked until Agent-X restarts",
                pluginId);
        }
    }

    private void SafeDispose(IPlugin instance, string pluginId)
    {
        try
        {
            instance.Dispose();
        }
        catch (Exception ex)
        {
            _log.Error(ex,
                "Plugin '{PluginId}' threw during Dispose - continuing unload", pluginId);
        }
    }

    // -- Private: manifest helpers --------------------------------------------

    /// <summary>
    /// Opens the .agentx-plugin ZIP archive and deserializes <c>manifest.json</c> from
    /// the archive root without extracting any other files to disk.
    /// </summary>
    private static async Task<PluginManifest> ReadManifestFromPackageAsync(string packagePath)
    {
        Log.Debug("Reading manifest from package {PackagePath}", packagePath);

        using var stream = new FileStream(
            packagePath,
            FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 65_536,
            useAsync: true);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        var manifestEntry = archive.GetEntry(ManifestFileName)
            ?? throw new InvalidOperationException(
                $"Plugin package does not contain a '{ManifestFileName}' at the archive root. " +
                $"Ensure the file is packed directly at the root of the .agentx-plugin archive.");

        using var entryStream = manifestEntry.Open();
        using var reader = new StreamReader(entryStream);

        var json = await reader.ReadToEndAsync().ConfigureAwait(false);

        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, ManifestJsonOptions)
            ?? throw new InvalidOperationException(
                $"'{ManifestFileName}' deserialized to null - the file may be empty or malformed.");

        return manifest;
    }

    /// <summary>
    /// Re-reads the installed plugin's on-disk <c>manifest.json</c>, the source of truth for
    /// the entry assembly name, <c>minAppVersion</c>, and dependencies (the
    /// <see cref="PluginEntity"/> does not persist them). Returns null when the file is
    /// missing or malformed; installations created by older schema versions have no manifest.
    /// </summary>
    private static PluginManifest? ReadInstalledManifest(PluginEntity entity)
    {
        var manifestPath = Path.Combine(entity.InstallPath, ManifestFileName);
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            var json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize<PluginManifest>(json, ManifestJsonOptions);
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "Could not re-read manifest for plugin '{PluginId}' - falling back to derived DLL name",
                entity.PluginId);
            return null;
        }
    }

    // -- Private: file-system helpers -----------------------------------------

    /// <summary>
    /// Extracts all entries from the plugin ZIP archive to <paramref name="destinationDirectory"/>,
    /// preserving directory structure. Existing files are overwritten.
    /// Guards against zip-slip path traversal by verifying every resolved destination path
    /// starts with the destination directory.
    /// </summary>
    private static async Task ExtractPackageAsync(string packagePath, string destinationDirectory)
    {
        // Normalize destination so the StartsWith guard is reliable on all path separator styles.
        var normalizedDestination = Path.GetFullPath(destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        using var stream = new FileStream(
            packagePath,
            FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 65_536,
            useAsync: true);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        foreach (var entry in archive.Entries)
        {
            // Directory entries have an empty Name; skip them (Directory.CreateDirectory handles them).
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var destinationPath = Path.GetFullPath(
                Path.Combine(destinationDirectory, entry.FullName));

            // Zip-slip guard: abort the entire extraction if any entry escapes the target directory.
            if (!destinationPath.StartsWith(normalizedDestination, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Plugin package contains a path-traversal entry: '{entry.FullName}'. " +
                    $"Installation aborted.");
            }

            var entryDirectory = Path.GetDirectoryName(destinationPath)!;
            Directory.CreateDirectory(entryDirectory);

            using var entryStream = entry.Open();
            using var fileStream = new FileStream(
                destinationPath,
                FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 65_536,
                useAsync: true);

            await entryStream.CopyToAsync(fileStream).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns the entry assembly file name for the given plugin entity, from its installed
    /// manifest. Falls back to a name derived from the last segment of the reverse-DNS plugin
    /// ID (e.g. <c>com.vendor.myplugin</c> becomes <c>myplugin.dll</c>) when the manifest cannot
    /// be read, which handles installations created by older schema versions.
    /// </summary>
    private static string GetEntryAssemblyName(PluginEntity entity, PluginManifest? manifest)
    {
        // Only trust the on-disk manifest's entry assembly when it is a bare .dll file
        // name. A tampered manifest with a path/traversal value falls back to the derived
        // name and is, in any case, re-checked for containment at load time.
        var declared = manifest?.EntryAssembly;
        if (!string.IsNullOrWhiteSpace(declared)
            && PathHelper.IsBareFileName(declared)
            && declared.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return declared;
        }

        if (!string.IsNullOrWhiteSpace(declared))
        {
            Log.Warning(
                "Ignoring unsafe entryAssembly '{EntryAssembly}' in manifest for plugin '{PluginId}' - using derived name",
                declared, entity.PluginId);
        }

        // Fallback: derive the DLL name from the last segment of the reverse-DNS identifier.
        var lastSegment = entity.PluginId.Contains('.')
            ? entity.PluginId[(entity.PluginId.LastIndexOf('.') + 1)..]
            : entity.PluginId;

        return $"{lastSegment}.dll";
    }

    /// <summary>
    /// Scans the loaded assembly for a concrete, non-abstract, exported type that implements
    /// <see cref="IPlugin"/> and exposes a public parameterless constructor.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when zero or more than one qualifying type is found, or when the assembly's
    /// types cannot be read (typically a missing dependency).
    /// </exception>
    private static Type DiscoverPluginType(Assembly assembly, string pluginId)
    {
        Type[] exported;
        try
        {
            exported = assembly.GetExportedTypes();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not read the types in the entry assembly for plugin '{pluginId}': {ex.Message}", ex);
        }

        var candidates = exported
            .Where(t => t.IsClass
                     && !t.IsAbstract
                     && typeof(IPlugin).IsAssignableFrom(t)
                     && t.GetConstructor(Type.EmptyTypes) is not null)
            .ToList();

        return candidates.Count switch
        {
            0 => throw new InvalidOperationException(
                    $"No public, non-abstract class implementing {nameof(IPlugin)} with a " +
                    $"parameterless constructor was found in the entry assembly for plugin '{pluginId}'."),
            1 => candidates[0],
            _ => throw new InvalidOperationException(
                    $"Multiple types implementing {nameof(IPlugin)} were found in plugin '{pluginId}'. " +
                    $"A plugin package must expose exactly one entry-point type. Found: " +
                    $"{string.Join(", ", candidates.Select(t => t.FullName))}"),
        };
    }

    // -- Private: path helpers ------------------------------------------------

    /// <summary>
    /// Returns the base directory for all plugin installations:
    /// <c>%LocalAppData%\AgentX\Plugins\</c>.
    /// </summary>
    private static string GetPluginBaseDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentX",
            PluginsSubDirectory);

    /// <summary>
    /// Returns the absolute install path for a specific plugin:
    /// <c>%LocalAppData%\AgentX\Plugins\{pluginId}\</c>.
    /// The plugin ID is sanitized to replace characters invalid in directory names.
    /// </summary>
    private static string GetPluginInstallPath(string pluginId) =>
        PathHelper.ResolveContainedPath(GetPluginBaseDirectory(), SanitizeDirectorySegment(pluginId));

    /// <summary>
    /// Returns the absolute path to the plugin's private data directory:
    /// <c>%LocalAppData%\AgentX\Plugins\{pluginId}\data\</c>.
    /// Kept inside the install directory so that uninstalling a plugin removes everything in
    /// a single recursive directory deletion.
    /// </summary>
    private static string GetPluginDataPath(string pluginId) =>
        Path.Combine(GetPluginInstallPath(pluginId), PluginDataSubDirectory);

    /// <summary>
    /// Replaces characters that are invalid in directory/file names with underscores.
    /// Plugin IDs follow reverse-DNS conventions (e.g. <c>com.vendor.myplugin</c>), which are
    /// safe on all supported platforms; this method provides a hard guard against edge cases.
    /// </summary>
    private static string SanitizeDirectorySegment(string pluginId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(pluginId.Select(c => invalid.Contains(c) ? '_' : c));
    }

    // -- Private: directory deletion ------------------------------------------

    /// <summary>
    /// Deletes the directory at <paramref name="path"/> recursively, logging a warning
    /// instead of propagating any exception. Used during install rollback, where no plugin
    /// assembly has been loaded from the directory.
    /// </summary>
    private static void DeleteDirectoryQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "Could not delete directory '{Path}' - manual cleanup may be required", path);
        }
    }

    /// <summary>
    /// Deletes an uninstalled plugin's directory. A just-unloaded assembly can stay mapped
    /// until the GC collects its load context, so a failed attempt is followed by a GC pass
    /// and a short back-off, a bounded number of times. Returns false (after logging the
    /// files that remain) when the directory could not be removed.
    /// </summary>
    private async Task<bool> DeleteDirectoryWithRetryAsync(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= DeleteAttempts)
                {
                    var remaining = Directory.Exists(path)
                        ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Take(20).ToList()
                        : new List<string>();
                    if (remaining.Count == 0)
                        return true;

                    _log.Error(ex,
                        "Could not fully delete plugin directory {InstallPath}; remaining files: {Files}. " +
                        "Delete the folder manually after restarting Agent-X",
                        path, remaining);
                    return false;
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt)).ConfigureAwait(false);
            }
        }
    }

    // -- Private nested type: PluginLoadContext --------------------------------

    /// <summary>
    /// Collectible <see cref="AssemblyLoadContext"/> that isolates a single plugin's assemblies.
    /// Dependencies are resolved from the plugin's entry assembly directory through an
    /// <see cref="AssemblyDependencyResolver"/>; assemblies the host already provides are left
    /// to the default context so that the plugin shares the host's contract types.
    /// </summary>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        /// <summary>Simple names of every assembly the host application ships or has loaded.</summary>
        private static readonly Lazy<HashSet<string>> HostAssemblyNames = new(LoadHostAssemblyNames);

        private readonly AssemblyDependencyResolver? _resolver;
        private readonly string _pluginDirectory;

        /// <param name="entryAssemblyPath">
        /// Absolute path to the plugin's entry DLL. The resolver must be seeded with this file,
        /// not its folder: it reads <c>{entry}.deps.json</c> and probes the entry assembly's
        /// directory for the plugin's private dependencies.
        /// </param>
        public PluginLoadContext(string entryAssemblyPath)
            : base(name: $"PluginContext-{Path.GetFileNameWithoutExtension(entryAssemblyPath)}", isCollectible: true)
        {
            _pluginDirectory = Path.GetDirectoryName(entryAssemblyPath)!;
            try
            {
                _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
            }
            catch (InvalidOperationException ex)
            {
                // Hosts without the component resolver still get directory probing below.
                Log.Warning(ex, "Plugin dependency resolver unavailable for {EntryAssembly}; probing its folder only",
                    entryAssemblyPath);
            }
        }

        /// <inheritdoc />
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // 1. Contract assemblies come from the host so IPlugin and friends keep one identity,
            //    even when the plugin folder carries its own copy of AgentX.Core.dll.
            if (IsProvidedByHost(assemblyName))
                return null;

            // 2. The plugin's private dependencies, from its deps.json or its folder.
            var resolvedPath = _resolver?.ResolveAssemblyToPath(assemblyName);
            if (resolvedPath is null && !string.IsNullOrEmpty(assemblyName.Name))
            {
                var candidate = Path.Combine(_pluginDirectory, assemblyName.Name + ".dll");
                if (File.Exists(candidate))
                    resolvedPath = candidate;
            }

            // 3. Anything else falls back to the default context.
            return resolvedPath is not null ? LoadFromAssemblyPath(resolvedPath) : null;
        }

        /// <inheritdoc />
        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var resolvedPath = _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName);
            return resolvedPath is not null
                ? LoadUnmanagedDllFromPath(resolvedPath)
                : IntPtr.Zero;
        }

        /// <summary>
        /// True for assemblies the plugin must share with the host: AgentX.Core, Serilog,
        /// Microsoft.Extensions.*, and everything else the host ships (its trusted platform
        /// assemblies, which include the framework).
        /// </summary>
        internal static bool IsProvidedByHost(AssemblyName assemblyName)
        {
            var name = assemblyName.Name;
            if (string.IsNullOrEmpty(name))
                return false;

            return name.Equals("AgentX.Core", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Serilog", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Microsoft.Extensions.", StringComparison.OrdinalIgnoreCase)
                || HostAssemblyNames.Value.Contains(name);
        }

        private static HashSet<string> LoadHostAssemblyNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
            {
                foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                    names.Add(Path.GetFileNameWithoutExtension(path));
            }

            foreach (var assembly in Default.Assemblies)
            {
                var name = assembly.GetName().Name;
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }

            return names;
        }
    }

    // -- Private nested type: GuardedPluginDocumentProcessor -------------------

    /// <summary>
    /// Wraps a plugin's <see cref="IDocumentProcessor"/> for the host pipeline. Selection calls
    /// (<see cref="CanProcess"/>, <see cref="SupportedExtensions"/>) are guarded so a faulty plugin
    /// cannot break processor selection for other files; <see cref="ProcessAsync"/> is passed
    /// through so a failure fails only that import.
    /// </summary>
    private sealed class GuardedPluginDocumentProcessor : IDocumentProcessor
    {
        private static readonly IReadOnlySet<string> NoExtensions = new HashSet<string>();

        private readonly string _pluginId;
        private readonly IDocumentProcessor _inner;
        private readonly ILogger _log;

        public GuardedPluginDocumentProcessor(string pluginId, IDocumentProcessor inner, ILogger log)
        {
            _pluginId = pluginId;
            _inner = inner;
            _log = log;
        }

        public IReadOnlySet<string> SupportedExtensions
        {
            get
            {
                try
                {
                    return _inner.SupportedExtensions ?? NoExtensions;
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Plugin '{PluginId}' threw from SupportedExtensions", _pluginId);
                    return NoExtensions;
                }
            }
        }

        public bool CanProcess(string filePath)
        {
            try
            {
                return _inner.CanProcess(filePath);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Plugin '{PluginId}' threw from CanProcess for {FilePath}", _pluginId, filePath);
                return false;
            }
        }

        public Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default) =>
            _inner.ProcessAsync(filePath, ct);
    }

    // -- Private nested type: PluginContext -----------------------------------

    /// <summary>
    /// Concrete <see cref="IPluginContext"/> passed to each plugin during
    /// <see cref="IPlugin.InitializeAsync"/>. Provides the plugin's private data directory
    /// and a pre-enriched logger. Service resolution is intentionally scoped to prevent
    /// plugins from accessing unrestricted host internals.
    /// </summary>
    private sealed class PluginContext : IPluginContext
    {
        /// <inheritdoc />
        public IServiceProvider Services { get; }

        /// <inheritdoc />
        public string PluginDataPath { get; }

        /// <inheritdoc />
        public ILogger Logger { get; }

        /// <param name="pluginDataPath">
        /// Absolute path to the plugin's private data directory.
        /// The directory is guaranteed to exist before this constructor is called.
        /// </param>
        /// <param name="services">
        /// A scoped <see cref="IServiceProvider"/> containing only the host services
        /// approved for plugin consumption (currently <c>IInboxService</c>).
        /// </param>
        /// <param name="logger">
        /// A Serilog <see cref="ILogger"/> instance pre-enriched with the plugin's metadata.
        /// </param>
        public PluginContext(string pluginDataPath, IServiceProvider services, ILogger logger)
        {
            PluginDataPath = pluginDataPath;
            Services = services;
            Logger = logger;
        }
    }
}
