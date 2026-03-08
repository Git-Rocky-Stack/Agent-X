namespace AgentX.Core.Services.Sync.Models;

// ── Enumerations ──────────────────────────────────────────────────────────────

/// <summary>
/// Represents the current operational state of the sync engine.
/// </summary>
public enum SyncState
{
    /// <summary>The sync engine is idle and not performing any active work.</summary>
    Idle,

    /// <summary>An export or import pass is currently running.</summary>
    Syncing,

    /// <summary>The most recent sync attempt terminated with an unrecoverable error.</summary>
    Error,

    /// <summary>One or more unresolved conflicts exist and require user attention.</summary>
    Conflict,
}

/// <summary>
/// Classifies how a <see cref="SyncChange"/> was produced.
/// </summary>
public enum SyncChangeType
{
    /// <summary>The entity was created after the last sync baseline.</summary>
    Created,

    /// <summary>The entity was modified after the last sync baseline.</summary>
    Updated,

    /// <summary>The entity was deleted after the last sync baseline.</summary>
    Deleted,
}

/// <summary>
/// Defines the scope of entities included in a sync export.
/// </summary>
public enum SyncScope
{
    /// <summary>All supported entity types are included in the sync.</summary>
    All,

    /// <summary>Only the collections listed in <see cref="SyncConfiguration.SelectedCollectionIds"/> are synced.</summary>
    SelectedCollections,
}

/// <summary>
/// Determines how a detected <see cref="SyncConflict"/> is resolved.
/// </summary>
public enum SyncResolution
{
    /// <summary>The conflict has not yet been resolved.</summary>
    Pending,

    /// <summary>The local version of the entity is kept; the remote change is discarded.</summary>
    KeepLocal,

    /// <summary>The remote version of the entity is applied; the local version is overwritten.</summary>
    KeepRemote,

    /// <summary>A merged representation of both changes has been produced and applied.</summary>
    Merged,
}

// ── Configuration & status ────────────────────────────────────────────────────

/// <summary>
/// Persisted configuration for the Collaborative Sync feature.
/// Stored as a JSON blob inside the <c>user_settings</c> table under the key
/// <c>SyncConfiguration</c>.
/// </summary>
public sealed class SyncConfiguration
{
    /// <summary>
    /// Absolute path to the shared sync folder (e.g. a OneDrive or Google Drive directory,
    /// a NAS mount, or a USB drive path).  The application must have read/write access.
    /// </summary>
    public string SyncFolderPath { get; set; } = string.Empty;

    /// <summary>
    /// User-supplied encryption key used to derive the AES-256 key via PBKDF2.
    /// Never persisted in plain text beyond the settings store.
    /// </summary>
    public string EncryptionKey { get; set; } = string.Empty;

    /// <summary>
    /// When <see langword="true"/> the sync engine polls the sync folder automatically
    /// at the configured <see cref="SyncIntervalMinutes"/> interval.
    /// </summary>
    public bool AutoSyncEnabled { get; set; }

    /// <summary>
    /// How often the auto-sync loop runs, in minutes.  Minimum effective value is 1.
    /// Defaults to <c>30</c>.
    /// </summary>
    public int SyncIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Which entities are exported during a sync pass.
    /// </summary>
    public SyncScope SyncScope { get; set; } = SyncScope.All;

    /// <summary>
    /// Comma-separated collection IDs to include when <see cref="SyncScope"/> is
    /// <see cref="SyncScope.SelectedCollections"/>.  <see langword="null"/> or empty
    /// when <see cref="SyncScope"/> is <see cref="SyncScope.All"/>.
    /// </summary>
    public string? SelectedCollectionIds { get; set; }
}

/// <summary>
/// Live, observable snapshot of the sync engine's current state.
/// Raised through <see cref="ISyncService.StatusChanged"/> whenever any field changes.
/// </summary>
public sealed class SyncStatus
{
    /// <summary>UTC timestamp of the last successful sync pass, or <see langword="null"/> if
    /// no sync has completed in this session.</summary>
    public DateTime? LastSyncAt { get; set; }

    /// <summary>Current operational state of the sync engine.</summary>
    public SyncState SyncState { get; set; } = SyncState.Idle;

    /// <summary>Human-readable error message when <see cref="SyncState"/> is
    /// <see cref="SyncState.Error"/>; <see langword="null"/> otherwise.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Number of local changes that have been exported but not yet confirmed
    /// as received by a peer installation.</summary>
    public int PendingChanges { get; set; }

    /// <summary>Wall-clock duration of the most recent sync pass in milliseconds.</summary>
    public double LastSyncDurationMs { get; set; }
}

// ── Change tracking ───────────────────────────────────────────────────────────

/// <summary>
/// A portable, serialisable package of changes produced by one Agent-X installation
/// and consumed by another.  Written to the sync folder as an encrypted <c>.axs</c> file.
/// </summary>
public sealed class SyncChangeSet
{
    /// <summary>
    /// Stable identifier of the device that produced this change set.
    /// Stored in <c>user_settings</c> under the key <c>SyncDeviceId</c>.
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>UTC timestamp at which this change set was exported.</summary>
    public DateTime ExportedAt { get; set; }

    /// <summary>Ordered list of entity changes contained in this package.</summary>
    public List<SyncChange> Changes { get; set; } = [];

    /// <summary>
    /// Monotonically increasing format version for forward-compatibility checks.
    /// Current value: <c>1</c>.
    /// </summary>
    public int Version { get; set; } = 1;
}

/// <summary>
/// A single entity-level change captured for sync purposes.
/// </summary>
public sealed class SyncChange
{
    /// <summary>
    /// Simple name of the EF entity type (e.g. <c>"DocumentEntity"</c>,
    /// <c>"CollectionEntity"</c>).
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Primary key of the affected entity on the originating device.</summary>
    public long EntityId { get; set; }

    /// <summary>How the entity was changed.</summary>
    public SyncChangeType ChangeType { get; set; }

    /// <summary>UTC timestamp when the change occurred on the originating device.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// JSON-serialised representation of the entity at the time of export.
    /// <see langword="null"/> for <see cref="SyncChangeType.Deleted"/> changes
    /// where no data needs to be transferred.
    /// </summary>
    public string? SerializedData { get; set; }
}

// ── Conflict handling ─────────────────────────────────────────────────────────

/// <summary>
/// Describes a situation where both the local installation and a remote change set
/// have independently modified the same entity since the last sync.
/// </summary>
public sealed class SyncConflict
{
    /// <summary>Entity type affected by the conflict.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Primary key of the conflicting entity on the local installation.</summary>
    public long EntityId { get; set; }

    /// <summary>The locally recorded change for this entity.</summary>
    public SyncChange LocalChange { get; set; } = new();

    /// <summary>The incoming remote change for this entity.</summary>
    public SyncChange RemoteChange { get; set; } = new();

    /// <summary>
    /// Resolution chosen by the user or automatically applied.
    /// Starts as <see cref="SyncResolution.Pending"/> until actioned.
    /// </summary>
    public SyncResolution Resolution { get; set; } = SyncResolution.Pending;
}
