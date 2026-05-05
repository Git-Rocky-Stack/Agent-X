using System.Text.Json;
using System.Text.Json.Serialization;
using AgentX.Core.Data;
using AgentX.Core.Services.Settings;
using AgentX.Core.Services.Workspaces.Models;
using Microsoft.Data.Sqlite;
using Serilog;

namespace AgentX.Core.Services.Workspaces;

/// <summary>
/// JSON-backed implementation of <see cref="IWorkspaceService"/>.
/// Workspace metadata is persisted to %LOCALAPPDATA%/AgentX/workspaces.json.
/// Each workspace's private data resides under %LOCALAPPDATA%/AgentX/workspaces/{id}/.
/// All public methods are thread-safe via a file-level <see cref="SemaphoreSlim"/>.
/// </summary>
public sealed class WorkspaceService : IWorkspaceService
{
    // ------------------------------------------------------------------
    // Constants
    // ------------------------------------------------------------------

    private const string WorkspacesFileName = "workspaces.json";
    private const string WorkspacesFolderName = "workspaces";
    private const string DefaultWorkspaceName = "Default";
    private const long DefaultWorkspaceId = 1L;
    private const string WorkspaceDbFileName = "agentx.db";

    // ------------------------------------------------------------------
    // Infrastructure
    // ------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Guards all file reads and writes so concurrent callers cannot corrupt
    /// workspaces.json or observe partial state.
    /// </summary>
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    private readonly string _appDataRoot;
    private readonly string _metadataFilePath;
    private readonly string _workspacesRoot;
    private readonly IEncryptedConnectionFactory _connectionFactory;

    // ------------------------------------------------------------------
    // Internal persistence model
    // ------------------------------------------------------------------

    /// <summary>
    /// Serialized root object written to workspaces.json.
    /// </summary>
    private sealed class WorkspaceStore
    {
        public long ActiveWorkspaceId { get; set; } = DefaultWorkspaceId;
        public List<WorkspaceRecord> Workspaces { get; set; } = [];
    }

    /// <summary>
    /// Per-workspace record inside the JSON store.
    /// </summary>
    private sealed class WorkspaceRecord
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public string? Color { get; set; }
        public bool IsDefault { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // ------------------------------------------------------------------
    // Constructor
    // ------------------------------------------------------------------

    /// <summary>
    /// Initialises the service, resolving all paths from the application
    /// storage root returned by <paramref name="settingsService"/>.
    /// </summary>
    /// <param name="settingsService">
    /// Provides access to <see cref="AppSettings.StoragePath"/>, which is
    /// used as the root for all workspace data.
    /// </param>
    /// <param name="connectionFactory">
    /// Encrypted connection factory — required so PRAGMA key is applied when the
    /// service opens per-workspace SQLite connections (e.g. stats queries).
    /// </param>
    public WorkspaceService(ISettingsService settingsService, IEncryptedConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

        // Resolve storage root synchronously from the already-cached settings
        // value. We use GetAwaiter().GetResult() only here in the constructor
        // because DI containers do not support async construction; this call
        // is safe because SettingsService caches on first call and never
        // blocks on I/O after the initial load.
        // Wave 4b: VSTHRD002 suppressed — see rationale above.
#pragma warning disable VSTHRD002
        var settings = settingsService.GetSettingsAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        _appDataRoot = settings.StoragePath;

        _metadataFilePath = Path.Combine(_appDataRoot, WorkspacesFileName);
        _workspacesRoot = Path.Combine(_appDataRoot, WorkspacesFolderName);

        Log.Information(
            "WorkspaceService initialised — metadata: {MetaPath}, storage root: {WorkspacesRoot}",
            _metadataFilePath, _workspacesRoot);
    }

    // ------------------------------------------------------------------
    // IWorkspaceService implementation
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkspaceInfo>> GetWorkspacesAsync()
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var store = await LoadStoreAsync().ConfigureAwait(false);
            return store.Workspaces
                .OrderBy(w => w.CreatedAt)
                .Select(w => MapToInfo(w, store.ActiveWorkspaceId))
                .ToList();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<WorkspaceInfo> CreateWorkspaceAsync(
        string name,
        string? icon = null,
        string? color = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var store = await LoadStoreAsync().ConfigureAwait(false);

            // Derive a new ID: max existing + 1 (safe because IDs are never reused)
            var newId = store.Workspaces.Count > 0
                ? store.Workspaces.Max(w => w.Id) + 1
                : DefaultWorkspaceId + 1;

            var record = new WorkspaceRecord
            {
                Id = newId,
                Name = name.Trim(),
                Icon = icon,
                Color = color,
                IsDefault = false,
                CreatedAt = DateTime.UtcNow,
            };

            store.Workspaces.Add(record);

            // Provision the workspace storage directory eagerly so callers
            // can begin writing data without a separate setup step.
            EnsureWorkspaceDirectory(newId);

            await SaveStoreAsync(store).ConfigureAwait(false);

            var info = MapToInfo(record, store.ActiveWorkspaceId);

            Log.Information(
                "Created workspace {WorkspaceId} '{Name}' (icon={Icon}, color={Color})",
                info.Id, info.Name, info.Icon ?? "<none>", info.Color ?? "<none>");

            return info;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<WorkspaceInfo> GetActiveWorkspaceAsync()
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var store = await LoadStoreAsync().ConfigureAwait(false);
            var active = store.Workspaces.FirstOrDefault(w => w.Id == store.ActiveWorkspaceId);

            if (active is null)
            {
                // Active pointer is stale — fall back to the default workspace.
                Log.Warning(
                    "Active workspace ID {ActiveId} not found in store; falling back to Default",
                    store.ActiveWorkspaceId);

                active = store.Workspaces.First(w => w.IsDefault);
                store.ActiveWorkspaceId = active.Id;
                await SaveStoreAsync(store).ConfigureAwait(false);
            }

            return MapToInfo(active, store.ActiveWorkspaceId);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task SwitchWorkspaceAsync(long workspaceId)
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var store = await LoadStoreAsync().ConfigureAwait(false);
            var target = store.Workspaces.FirstOrDefault(w => w.Id == workspaceId)
                ?? throw new InvalidOperationException(
                    $"Workspace {workspaceId} does not exist.");

            if (store.ActiveWorkspaceId == workspaceId)
            {
                Log.Debug("Workspace {WorkspaceId} is already active — no-op", workspaceId);
                return;
            }

            var previous = store.ActiveWorkspaceId;
            store.ActiveWorkspaceId = workspaceId;
            await SaveStoreAsync(store).ConfigureAwait(false);

            Log.Information(
                "Switched active workspace from {PreviousId} to {WorkspaceId} '{Name}'",
                previous, workspaceId, target.Name);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task RenameWorkspaceAsync(long workspaceId, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var store = await LoadStoreAsync().ConfigureAwait(false);
            var record = store.Workspaces.FirstOrDefault(w => w.Id == workspaceId)
                ?? throw new InvalidOperationException(
                    $"Workspace {workspaceId} does not exist.");

            if (record.IsDefault)
                throw new InvalidOperationException(
                    "The built-in Default workspace cannot be renamed.");

            var oldName = record.Name;
            record.Name = newName.Trim();
            await SaveStoreAsync(store).ConfigureAwait(false);

            Log.Information(
                "Renamed workspace {WorkspaceId} from '{OldName}' to '{NewName}'",
                workspaceId, oldName, record.Name);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdateWorkspaceAppearanceAsync(long workspaceId, string? icon, string? color)
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var store = await LoadStoreAsync().ConfigureAwait(false);
            var record = store.Workspaces.FirstOrDefault(w => w.Id == workspaceId)
                ?? throw new InvalidOperationException(
                    $"Workspace {workspaceId} does not exist.");

            record.Icon = icon;
            record.Color = color;
            await SaveStoreAsync(store).ConfigureAwait(false);

            Log.Information(
                "Updated appearance for workspace {WorkspaceId} — icon={Icon}, color={Color}",
                workspaceId, icon ?? "<none>", color ?? "<none>");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteWorkspaceAsync(long workspaceId)
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var store = await LoadStoreAsync().ConfigureAwait(false);
            var record = store.Workspaces.FirstOrDefault(w => w.Id == workspaceId)
                ?? throw new InvalidOperationException(
                    $"Workspace {workspaceId} does not exist.");

            if (record.IsDefault)
                throw new InvalidOperationException(
                    "The built-in Default workspace cannot be deleted.");

            if (store.ActiveWorkspaceId == workspaceId)
                throw new InvalidOperationException(
                    "Cannot delete the currently active workspace. Switch to a different workspace first.");

            store.Workspaces.Remove(record);
            await SaveStoreAsync(store).ConfigureAwait(false);

            // Remove the private storage directory and all data within it.
            var storagePath = BuildWorkspaceStoragePath(workspaceId);
            if (Directory.Exists(storagePath))
            {
                Directory.Delete(storagePath, recursive: true);
                Log.Information(
                    "Deleted storage directory for workspace {WorkspaceId}: {StoragePath}",
                    workspaceId, storagePath);
            }

            Log.Information("Deleted workspace {WorkspaceId} '{Name}'", workspaceId, record.Name);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<WorkspaceStats> GetWorkspaceStatsAsync(long workspaceId)
    {
        // Validate existence under the lock, then release before the
        // potentially slow database query to avoid holding the file lock
        // longer than necessary.
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var store = await LoadStoreAsync().ConfigureAwait(false);
            if (!store.Workspaces.Any(w => w.Id == workspaceId))
                throw new InvalidOperationException($"Workspace {workspaceId} does not exist.");
        }
        finally
        {
            _fileLock.Release();
        }

        return await QueryWorkspaceStatsAsync(workspaceId).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public string GetWorkspaceStoragePath(long workspaceId)
        => BuildWorkspaceStoragePath(workspaceId);

    // ------------------------------------------------------------------
    // Private helpers — persistence
    // ------------------------------------------------------------------

    /// <summary>
    /// Reads workspaces.json from disk, or seeds a fresh store with the
    /// built-in Default workspace when the file does not yet exist.
    /// Must be called while <see cref="_fileLock"/> is held.
    /// </summary>
    private async Task<WorkspaceStore> LoadStoreAsync()
    {
        if (!File.Exists(_metadataFilePath))
        {
            Log.Information(
                "workspaces.json not found — seeding default workspace store at {MetaPath}",
                _metadataFilePath);

            return await SeedDefaultStoreAsync().ConfigureAwait(false);
        }

        try
        {
            var json = await File.ReadAllTextAsync(_metadataFilePath).ConfigureAwait(false);
            var store = JsonSerializer.Deserialize<WorkspaceStore>(json, JsonOptions);

            if (store is null || store.Workspaces.Count == 0)
            {
                Log.Warning(
                    "workspaces.json was empty or malformed — re-seeding default store");
                return await SeedDefaultStoreAsync().ConfigureAwait(false);
            }

            // Guarantee the Default workspace always exists, even if the file
            // was edited externally and the record was removed.
            if (!store.Workspaces.Any(w => w.IsDefault))
            {
                Log.Warning("Default workspace record missing from store — re-inserting it");
                store.Workspaces.Insert(0, BuildDefaultRecord());
                await SaveStoreAsync(store).ConfigureAwait(false);
            }

            return store;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to deserialise workspaces.json — re-seeding default store");
            return await SeedDefaultStoreAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes the store to disk as indented JSON.
    /// Must be called while <see cref="_fileLock"/> is held.
    /// </summary>
    private async Task SaveStoreAsync(WorkspaceStore store)
    {
        try
        {
            Directory.CreateDirectory(_appDataRoot);
            var json = JsonSerializer.Serialize(store, JsonOptions);
            await File.WriteAllTextAsync(_metadataFilePath, json).ConfigureAwait(false);
            Log.Debug("workspaces.json saved ({WorkspaceCount} workspaces)", store.Workspaces.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save workspaces.json to {MetaPath}", _metadataFilePath);
            throw;
        }
    }

    /// <summary>
    /// Creates a fresh store containing only the Default workspace, persists it,
    /// and provisions the Default workspace's storage directory.
    /// </summary>
    private async Task<WorkspaceStore> SeedDefaultStoreAsync()
    {
        var defaultRecord = BuildDefaultRecord();

        var store = new WorkspaceStore
        {
            ActiveWorkspaceId = DefaultWorkspaceId,
            Workspaces = [defaultRecord],
        };

        EnsureWorkspaceDirectory(DefaultWorkspaceId);
        await SaveStoreAsync(store).ConfigureAwait(false);

        Log.Information("Default workspace store seeded at {MetaPath}", _metadataFilePath);
        return store;
    }

    /// <summary>
    /// Constructs the built-in Default <see cref="WorkspaceRecord"/>.
    /// </summary>
    private static WorkspaceRecord BuildDefaultRecord() => new()
    {
        Id = DefaultWorkspaceId,
        Name = DefaultWorkspaceName,
        Icon = null,
        Color = null,
        IsDefault = true,
        CreatedAt = DateTime.UtcNow,
    };

    // ------------------------------------------------------------------
    // Private helpers — paths and directories
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns %LOCALAPPDATA%/AgentX/workspaces/{workspaceId}/ without
    /// guaranteeing the directory exists.
    /// </summary>
    private string BuildWorkspaceStoragePath(long workspaceId)
        => Path.Combine(_workspacesRoot, workspaceId.ToString());

    /// <summary>
    /// Creates the workspace's private storage directory if it does not exist.
    /// </summary>
    private void EnsureWorkspaceDirectory(long workspaceId)
    {
        var path = BuildWorkspaceStoragePath(workspaceId);
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            Log.Debug("Provisioned workspace storage directory: {Path}", path);
        }
    }

    // ------------------------------------------------------------------
    // Private helpers — model mapping
    // ------------------------------------------------------------------

    /// <summary>
    /// Converts a <see cref="WorkspaceRecord"/> to a <see cref="WorkspaceInfo"/>.
    /// </summary>
    private WorkspaceInfo MapToInfo(WorkspaceRecord record, long activeWorkspaceId)
        => new()
        {
            Id = record.Id,
            Name = record.Name,
            Icon = record.Icon,
            Color = record.Color,
            IsDefault = record.IsDefault,
            IsActive = record.Id == activeWorkspaceId,
            CreatedAt = record.CreatedAt,
            StoragePath = BuildWorkspaceStoragePath(record.Id),
        };

    // ------------------------------------------------------------------
    // Private helpers — stats query
    // ------------------------------------------------------------------

    /// <summary>
    /// Opens the workspace's private SQLite database (if it exists) and
    /// runs lightweight COUNT queries to build <see cref="WorkspaceStats"/>.
    /// Returns zeroed stats when the database file is absent.
    /// </summary>
    private async Task<WorkspaceStats> QueryWorkspaceStatsAsync(long workspaceId)
    {
        var dbPath = Path.Combine(BuildWorkspaceStoragePath(workspaceId), WorkspaceDbFileName);

        if (!File.Exists(dbPath))
        {
            Log.Debug(
                "Workspace {WorkspaceId} has no database yet — returning zeroed stats",
                workspaceId);

            return new WorkspaceStats
            {
                DocumentCount = 0,
                ConversationCount = 0,
                CollectionCount = 0,
                WorkflowCount = 0,
                DatabaseSizeMB = 0,
            };
        }

        var dbSizeMB = Math.Round(
            new FileInfo(dbPath).Length / (1024.0 * 1024.0),
            digits: 3);

        try
        {
            // Open the workspace DB via the encrypted connection factory so PRAGMA key
            // is applied when encryption is enabled. The factory opens read/write; the
            // stats queries below are strictly SELECT so no writes occur.
            await using var connection = _connectionFactory.OpenKeyed(dbPath);

            var documentCount = await QueryTableCountAsync(connection, "documents")
                .ConfigureAwait(false);

            var conversationCount = await QueryTableCountAsync(connection, "conversations",
                    whereClause: "is_archived = 0")
                .ConfigureAwait(false);

            var collectionCount = await QueryTableCountAsync(connection, "collections")
                .ConfigureAwait(false);

            var workflowCount = await QueryTableCountAsync(connection, "workflows")
                .ConfigureAwait(false);

            var stats = new WorkspaceStats
            {
                DocumentCount = documentCount,
                ConversationCount = conversationCount,
                CollectionCount = collectionCount,
                WorkflowCount = workflowCount,
                DatabaseSizeMB = dbSizeMB,
            };

            Log.Debug(
                "Workspace {WorkspaceId} stats — docs={Docs}, convs={Convs}, cols={Cols}, flows={Flows}, db={DbMB:F3}MB",
                workspaceId,
                stats.DocumentCount,
                stats.ConversationCount,
                stats.CollectionCount,
                stats.WorkflowCount,
                stats.DatabaseSizeMB);

            return stats;
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Could not query stats for workspace {WorkspaceId} — returning file-size-only stats",
                workspaceId);

            // Return what we can rather than propagating; the DB may be
            // mid-migration or locked by EF Core's WAL checkpoint.
            return new WorkspaceStats
            {
                DocumentCount = 0,
                ConversationCount = 0,
                CollectionCount = 0,
                WorkflowCount = 0,
                DatabaseSizeMB = dbSizeMB,
            };
        }
    }

    /// <summary>
    /// Executes a scalar COUNT query against <paramref name="tableName"/> and returns
    /// the result, or 0 when the table does not exist in the schema.
    /// </summary>
    /// <param name="connection">An open <see cref="SqliteConnection"/>.</param>
    /// <param name="tableName">Name of the SQLite table to count rows in.</param>
    /// <param name="whereClause">
    /// Optional WHERE clause body (without the WHERE keyword) to filter rows.
    /// </param>
    private static async Task<int> QueryTableCountAsync(
        SqliteConnection connection,
        string tableName,
        string? whereClause = null)
    {
        // Guard against non-existent tables (workspace DB may be on an older schema
        // or the table may not have been created yet by EF Core migrations).
        var tableExistsSql =
            "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=@table;";

        await using var existsCmd = connection.CreateCommand();
        existsCmd.CommandText = tableExistsSql;
        existsCmd.Parameters.AddWithValue("@table", tableName);

        var exists = Convert.ToInt32(await existsCmd.ExecuteScalarAsync().ConfigureAwait(false));
        if (exists == 0)
            return 0;

        var countSql = string.IsNullOrWhiteSpace(whereClause)
            ? $"SELECT COUNT(1) FROM {tableName};"
            : $"SELECT COUNT(1) FROM {tableName} WHERE {whereClause};";

        await using var countCmd = connection.CreateCommand();
        countCmd.CommandText = countSql;

        return Convert.ToInt32(await countCmd.ExecuteScalarAsync().ConfigureAwait(false));
    }
}
