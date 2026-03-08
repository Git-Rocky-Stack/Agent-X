using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Plugins;

/// <summary>
/// Production implementation of <see cref="IPluginService"/>.
/// </summary>
/// <remarks>
/// Assembly isolation strategy:
/// Each plugin is loaded into its own <see cref="PluginLoadContext"/> (a collectible subclass of
/// <see cref="AssemblyLoadContext"/>), so that unloading a plugin releases all types and allows
/// the GC to reclaim the memory. Private assemblies in the plugin directory are resolved locally;
/// all other resolution falls back to the default context so the plugin receives the same
/// singleton instances of any shared host types.
///
/// Thread safety:
/// <see cref="_loadedPlugins"/> is a <see cref="ConcurrentDictionary{TKey,TValue}"/> whose
/// individual <c>TryAdd</c> / <c>TryRemove</c> / <c>TryGetValue</c> operations are atomic.
/// For the load-then-register sequence in <see cref="EnablePluginAsync"/>, a
/// <see cref="SemaphoreSlim"/> guard prevents two concurrent callers from double-loading the
/// same plugin assembly.
/// </remarks>
public sealed class PluginService : IPluginService
{
    // ── Constants ──────────────────────────────────────────────────────────────

    private const string ManifestFileName = "manifest.json";
    private const string PluginsSubDirectory = "Plugins";
    private const string PluginDataSubDirectory = "data";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── Fields ─────────────────────────────────────────────────────────────────

    private readonly AgentXDbContext _dbContext;
    private readonly ILogger _log;

    /// <summary>
    /// Guards the load-then-register sequence in <see cref="EnablePluginAsync"/> so that two
    /// concurrent callers cannot race to double-load the same plugin assembly.
    /// </summary>
    private readonly SemaphoreSlim _enableLock = new(1, 1);

    /// <summary>
    /// Maps plugin ID → a tuple of the active <see cref="IPlugin"/> instance and the
    /// <see cref="AssemblyLoadContext"/> that owns its assembly. Populated by
    /// <see cref="EnablePluginAsync"/> and drained by <see cref="DeactivateAndUnloadAsync"/>.
    /// </summary>
    private readonly ConcurrentDictionary<string, (IPlugin Instance, AssemblyLoadContext Alc)> _loadedPlugins
        = new(StringComparer.Ordinal);

    // ── Constructor ────────────────────────────────────────────────────────────

    /// <summary>
    /// Initializes <see cref="PluginService"/> with the required dependencies.
    /// </summary>
    /// <param name="dbContext">
    /// The application <see cref="AgentXDbContext"/>. Used exclusively for plugin metadata
    /// persistence. Must not be null.
    /// </param>
    /// <param name="logger">
    /// The application-level Serilog <see cref="ILogger"/>. A context-specific child logger
    /// enriched with <c>SourceContext=PluginService</c> is derived via
    /// <see cref="Log.ForContext{T}()"/>.
    /// </param>
    public PluginService(AgentXDbContext dbContext, ILogger logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _log = logger?.ForContext<PluginService>()
               ?? throw new ArgumentNullException(nameof(logger));

        _log.Information("PluginService initialized. PluginBaseDir={PluginBaseDir}", GetPluginBaseDirectory());
    }

    // ── IPluginService: GetInstalledPluginsAsync ───────────────────────────────

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

    // ── IPluginService: InstallPluginAsync ────────────────────────────────────

    /// <inheritdoc />
    public async Task<PluginEntity> InstallPluginAsync(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        _log.Information("Installing plugin from package {PackagePath}", packagePath);

        if (!File.Exists(packagePath))
            throw new FileNotFoundException($"Plugin package not found: {packagePath}", packagePath);

        // ── Step 1: read and validate manifest from the archive ────────────────
        var manifest = await ReadManifestFromPackageAsync(packagePath).ConfigureAwait(false);
        ValidateManifest(manifest);

        _log.Information(
            "Manifest validated. PluginId={PluginId} Name={Name} Version={Version} Author={Author}",
            manifest.Id, manifest.Name, manifest.Version, manifest.Author);

        // ── Step 2: reject duplicate installations ─────────────────────────────
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

        // ── Step 3: extract all files to the plugin install directory ──────────
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
            _log.Error(ex, "Extraction failed — removing partially extracted directory {InstallPath}", installPath);
            DeleteDirectoryQuietly(installPath);
            throw new InvalidOperationException(
                $"Failed to extract plugin package '{Path.GetFileName(packagePath)}': {ex.Message}", ex);
        }

        // ── Step 4: create the database record (disabled by default) ───────────
        var entity = new PluginEntity
        {
            PluginId    = manifest.Id,
            Name        = manifest.Name,
            Version     = manifest.Version,
            Author      = manifest.Author,
            Description = manifest.Description,
            PluginType  = manifest.PluginType,
            InstallPath = installPath,
            IsEnabled   = false,
            InstalledAt = DateTime.UtcNow,
        };

        _dbContext.Plugins.Add(entity);
        await _dbContext.SaveChangesAsync().ConfigureAwait(false);

        _log.Information(
            "Plugin installed successfully. PluginId={PluginId} EntityId={EntityId} InstallPath={InstallPath}",
            manifest.Id, entity.Id, installPath);

        return entity;
    }

    // ── IPluginService: UninstallPluginAsync ──────────────────────────────────

    /// <inheritdoc />
    public async Task UninstallPluginAsync(long id)
    {
        _log.Information("Uninstalling plugin EntityId={EntityId}", id);

        var entity = await _dbContext.Plugins
            .FirstOrDefaultAsync(p => p.Id == id)
            .ConfigureAwait(false);

        if (entity is null)
        {
            _log.Warning("Plugin EntityId={EntityId} not found — nothing to uninstall", id);
            return;
        }

        // ── Step 1: deactivate and unload assembly if the plugin is active ─────
        await DeactivateAndUnloadAsync(entity.PluginId).ConfigureAwait(false);

        // ── Step 2: delete all files on disk ──────────────────────────────────
        if (!string.IsNullOrEmpty(entity.InstallPath) && Directory.Exists(entity.InstallPath))
        {
            _log.Information("Deleting plugin directory {InstallPath}", entity.InstallPath);
            DeleteDirectoryQuietly(entity.InstallPath);
        }

        // ── Step 3: remove the database record ────────────────────────────────
        _dbContext.Plugins.Remove(entity);
        await _dbContext.SaveChangesAsync().ConfigureAwait(false);

        _log.Information("Plugin '{PluginId}' (EntityId={EntityId}) uninstalled", entity.PluginId, id);
    }

    // ── IPluginService: EnablePluginAsync ─────────────────────────────────────

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
            // Already active — no-op to satisfy the idempotency guarantee on the interface.
            if (_loadedPlugins.ContainsKey(entity.PluginId))
            {
                _log.Debug(
                    "Plugin '{PluginId}' is already active — EnablePluginAsync is a no-op",
                    entity.PluginId);
                return;
            }

            // ── Locate and load the entry assembly ────────────────────────────
            var entryAssemblyName = GetEntryAssemblyName(entity);
            var entryDllPath = Path.Combine(entity.InstallPath, entryAssemblyName);

            if (!File.Exists(entryDllPath))
                throw new InvalidOperationException(
                    $"Plugin entry assembly not found at '{entryDllPath}'. " +
                    $"The installation may be corrupt.");

            var loadContext = new PluginLoadContext(entity.InstallPath);
            Assembly assembly;

            try
            {
                assembly = loadContext.LoadFromAssemblyPath(entryDllPath);
            }
            catch (Exception ex)
            {
                loadContext.Unload();
                throw new InvalidOperationException(
                    $"Failed to load assembly '{entryDllPath}' for plugin '{entity.PluginId}': {ex.Message}", ex);
            }

            // ── Discover the single IPlugin implementation ────────────────────
            var pluginType = DiscoverPluginType(assembly, entity.PluginId);
            IPlugin instance;

            try
            {
                instance = (IPlugin)Activator.CreateInstance(pluginType)!;
            }
            catch (Exception ex)
            {
                loadContext.Unload();
                throw new InvalidOperationException(
                    $"Failed to instantiate '{pluginType.FullName}' for plugin '{entity.PluginId}': {ex.Message}", ex);
            }

            // ── Build the plugin context ───────────────────────────────────────
            var dataPath = GetPluginDataPath(entity.PluginId);
            Directory.CreateDirectory(dataPath);

            var pluginLogger = _log
                .ForContext("PluginId", entity.PluginId)
                .ForContext("PluginVersion", entity.Version);

            var pluginContext = new PluginContext(
                pluginDataPath: dataPath,
                logger: pluginLogger);

            // ── Lifecycle: Initialize → Activate ──────────────────────────────
            _log.Information("Initializing plugin '{PluginId}' v{Version}", entity.PluginId, entity.Version);

            try
            {
                await instance.InitializeAsync(pluginContext).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                instance.Dispose();
                loadContext.Unload();
                throw new InvalidOperationException(
                    $"Plugin '{entity.PluginId}' threw during InitializeAsync: {ex.Message}", ex);
            }

            _log.Information("Activating plugin '{PluginId}'", entity.PluginId);

            try
            {
                await instance.ActivateAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Best-effort deactivation before disposal on activation failure.
                try { await instance.DeactivateAsync().ConfigureAwait(false); } catch { /* swallow */ }
                instance.Dispose();
                loadContext.Unload();
                throw new InvalidOperationException(
                    $"Plugin '{entity.PluginId}' threw during ActivateAsync: {ex.Message}", ex);
            }

            // ── Register in the runtime dictionary ────────────────────────────
            _loadedPlugins[entity.PluginId] = (instance, loadContext);
        }
        finally
        {
            _enableLock.Release();
        }

        // ── Persist enabled state and activation timestamp ────────────────────
        entity.IsEnabled = true;
        entity.LastActivatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync().ConfigureAwait(false);

        _log.Information("Plugin '{PluginId}' enabled and activated successfully", entity.PluginId);
    }

    // ── IPluginService: DisablePluginAsync ────────────────────────────────────

    /// <inheritdoc />
    public async Task DisablePluginAsync(long id)
    {
        _log.Information("Disabling plugin EntityId={EntityId}", id);

        var entity = await _dbContext.Plugins
            .FirstOrDefaultAsync(p => p.Id == id)
            .ConfigureAwait(false);

        if (entity is null)
        {
            _log.Warning("Plugin EntityId={EntityId} not found — nothing to disable", id);
            return;
        }

        // Deactivate the runtime instance (safe no-op if not currently loaded).
        await DeactivateAndUnloadAsync(entity.PluginId).ConfigureAwait(false);

        // Persist the disabled state regardless of whether the plugin was active.
        entity.IsEnabled = false;
        await _dbContext.SaveChangesAsync().ConfigureAwait(false);

        _log.Information("Plugin '{PluginId}' (EntityId={EntityId}) disabled", entity.PluginId, id);
    }

    // ── IPluginService: GetActivePluginsAsync ─────────────────────────────────

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

    // ── IPluginService: GetPluginInstance<T> ──────────────────────────────────

    /// <inheritdoc />
    public Task<T?> GetPluginInstance<T>(string pluginId) where T : class, IPlugin
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        if (!_loadedPlugins.TryGetValue(pluginId, out var entry))
        {
            _log.Debug(
                "GetPluginInstance<{Type}>: plugin '{PluginId}' is not active",
                typeof(T).Name, pluginId);
            return Task.FromResult<T?>(null);
        }

        if (entry.Instance is T typed)
            return Task.FromResult<T?>(typed);

        _log.Debug(
            "GetPluginInstance<{Type}>: plugin '{PluginId}' is active but does not implement {Type}",
            typeof(T).Name, pluginId, typeof(T).Name);

        return Task.FromResult<T?>(null);
    }

    // ── Private: deactivation helper ──────────────────────────────────────────

    /// <summary>
    /// Removes the plugin entry from <see cref="_loadedPlugins"/>, calls
    /// <see cref="IPlugin.DeactivateAsync"/> and <see cref="IPlugin.Dispose"/>, then unloads
    /// the plugin's <see cref="AssemblyLoadContext"/>. Safe to call when the plugin is not active.
    /// </summary>
    private async Task DeactivateAndUnloadAsync(string pluginId)
    {
        if (!_loadedPlugins.TryRemove(pluginId, out var entry))
        {
            // Plugin was not active — nothing to do.
            return;
        }

        var (instance, alc) = entry;

        // Deactivate — swallow exceptions so unload always proceeds.
        try
        {
            _log.Information("Deactivating plugin '{PluginId}'", pluginId);
            await instance.DeactivateAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error(ex,
                "Plugin '{PluginId}' threw during DeactivateAsync — continuing unload", pluginId);
        }

        // Dispose — swallow exceptions so ALC unload always proceeds.
        try
        {
            instance.Dispose();
        }
        catch (Exception ex)
        {
            _log.Error(ex,
                "Plugin '{PluginId}' threw during Dispose — continuing unload", pluginId);
        }

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
    }

    // ── Private: manifest helpers ──────────────────────────────────────────────

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
                $"'{ManifestFileName}' deserialized to null — the file may be empty or malformed.");

        return manifest;
    }

    /// <summary>
    /// Validates that all mandatory manifest fields are present and non-empty.
    /// Throws <see cref="InvalidOperationException"/> with a consolidated error message
    /// listing every violation if any required field is missing.
    /// </summary>
    private static void ValidateManifest(PluginManifest manifest)
    {
        var errors = new List<string>(4);

        if (string.IsNullOrWhiteSpace(manifest.Id))
            errors.Add("'id' is required.");
        if (string.IsNullOrWhiteSpace(manifest.Name))
            errors.Add("'name' is required.");
        if (string.IsNullOrWhiteSpace(manifest.Version))
            errors.Add("'version' is required.");
        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
            errors.Add("'entryAssembly' is required.");

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"Plugin manifest is invalid: {string.Join(" ", errors)}");
    }

    // ── Private: file-system helpers ──────────────────────────────────────────

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
            // Directory entries have an empty Name; skip them — Directory.CreateDirectory handles them.
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
    /// Returns the entry assembly file name for the given plugin entity.
    /// Re-reads the on-disk <c>manifest.json</c> as the source of truth because the
    /// <see cref="PluginEntity"/> does not persist the entry assembly name directly.
    /// Falls back to a name derived from the last segment of the reverse-DNS plugin ID
    /// (e.g. <c>com.vendor.myplugin</c> → <c>myplugin.dll</c>) when the manifest cannot be read,
    /// which handles installations created by older schema versions.
    /// </summary>
    private static string GetEntryAssemblyName(PluginEntity entity)
    {
        var manifestPath = Path.Combine(entity.InstallPath, ManifestFileName);

        if (File.Exists(manifestPath))
        {
            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<PluginManifest>(json, ManifestJsonOptions);

                if (!string.IsNullOrWhiteSpace(manifest?.EntryAssembly))
                    return manifest.EntryAssembly;
            }
            catch (Exception ex)
            {
                Log.Warning(ex,
                    "Could not re-read manifest for plugin '{PluginId}' — falling back to derived DLL name",
                    entity.PluginId);
            }
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
    /// Thrown when zero or more than one qualifying type is found.
    /// </exception>
    private static Type DiscoverPluginType(Assembly assembly, string pluginId)
    {
        var candidates = assembly.GetExportedTypes()
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

    // ── Private: path helpers ──────────────────────────────────────────────────

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
        Path.Combine(GetPluginBaseDirectory(), SanitizeDirectorySegment(pluginId));

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

    // ── Private: quiet directory deletion ─────────────────────────────────────

    /// <summary>
    /// Deletes the directory at <paramref name="path"/> recursively, logging a warning
    /// instead of propagating any exception. Used during rollback and uninstallation so
    /// that a file-system error does not abort the overall operation.
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
                "Could not delete directory '{Path}' — manual cleanup may be required", path);
        }
    }

    // ── Private nested type: PluginLoadContext ────────────────────────────────

    /// <summary>
    /// Collectible <see cref="AssemblyLoadContext"/> that isolates a single plugin's assemblies.
    /// Assemblies located in the plugin's install directory are resolved locally via an
    /// <see cref="AssemblyDependencyResolver"/>; all other resolution falls back to the default
    /// context so that the plugin shares the same singleton host-service types.
    /// </summary>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        /// <param name="pluginDirectory">
        /// Absolute path to the plugin's install directory. Used to seed the
        /// <see cref="AssemblyDependencyResolver"/> which consults the plugin's own
        /// <c>.deps.json</c> and runtimeconfig for dependency resolution.
        /// </param>
        public PluginLoadContext(string pluginDirectory)
            : base(name: $"PluginContext-{Path.GetFileName(pluginDirectory)}", isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(pluginDirectory);
        }

        /// <inheritdoc />
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // 1. Attempt to resolve from the plugin's own directory.
            var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (resolvedPath is not null)
                return LoadFromAssemblyPath(resolvedPath);

            // 2. Return null to let the runtime fall back to the default context,
            //    which ensures the plugin shares the host's singleton service instances.
            return null;
        }

        /// <inheritdoc />
        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var resolvedPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return resolvedPath is not null
                ? LoadUnmanagedDllFromPath(resolvedPath)
                : IntPtr.Zero;
        }
    }

    // ── Private nested type: PluginContext ────────────────────────────────────

    /// <summary>
    /// Concrete <see cref="IPluginContext"/> passed to each plugin during
    /// <see cref="IPlugin.InitializeAsync"/>. Provides the plugin's private data directory
    /// and a pre-enriched logger. Service resolution is intentionally scoped to prevent
    /// plugins from accessing unrestricted host internals.
    /// </summary>
    private sealed class PluginContext : IPluginContext
    {
        /// <inheritdoc />
        /// <remarks>
        /// Exposes an empty <see cref="IServiceProvider"/> by default. Plugins that require
        /// host services should declare the appropriate permissions in their manifest and
        /// receive a scoped provider from the host at a future integration point.
        /// </remarks>
        public IServiceProvider Services { get; } = EmptyServiceProvider.Instance;

        /// <inheritdoc />
        public string PluginDataPath { get; }

        /// <inheritdoc />
        public ILogger Logger { get; }

        /// <param name="pluginDataPath">
        /// Absolute path to the plugin's private data directory.
        /// The directory is guaranteed to exist before this constructor is called.
        /// </param>
        /// <param name="logger">
        /// A Serilog <see cref="ILogger"/> instance pre-enriched with the plugin's metadata.
        /// </param>
        public PluginContext(string pluginDataPath, ILogger logger)
        {
            PluginDataPath = pluginDataPath;
            Logger         = logger;
        }
    }

    // ── Private nested type: EmptyServiceProvider ─────────────────────────────

    /// <summary>
    /// Minimal <see cref="IServiceProvider"/> that always returns <see langword="null"/>.
    /// Used as the default <see cref="IPluginContext.Services"/> implementation when no
    /// host service scope is injected into <see cref="PluginService"/>.
    /// </summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        /// <summary>Singleton instance — allocation-free for all callers.</summary>
        public static readonly EmptyServiceProvider Instance = new();

        private EmptyServiceProvider() { }

        /// <inheritdoc />
        public object? GetService(Type serviceType) => null;
    }
}
