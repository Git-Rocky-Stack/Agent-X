namespace AgentX.Core.Services.Backup.Models;

/// <summary>
/// Options supplied by the caller when requesting a new backup.
/// </summary>
public class BackupOptions
{
    /// <summary>
    /// The directory in which the backup archive will be written.
    /// If null or empty the service falls back to the default storage path.
    /// </summary>
    public string DestinationPath { get; init; } = string.Empty;

    /// <summary>
    /// When provided, the backup archive is AES-256 encrypted using this password.
    /// Leave null to produce an unencrypted ZIP.
    /// </summary>
    public string? EncryptionPassword { get; init; }

    /// <summary>Whether to include document files stored on disk in addition to the database.</summary>
    public bool IncludeDocuments { get; init; } = true;

    /// <summary>Optional notes stored in the backup history record.</summary>
    public string? Notes { get; init; }

    /// <summary>Identifies the trigger source: "manual" or "scheduled".</summary>
    public string BackupType { get; init; } = "manual";
}

/// <summary>
/// The result returned after a backup operation completes (or fails).
/// </summary>
public class BackupResult
{
    /// <summary>True when the backup completed without errors.</summary>
    public bool Success { get; init; }

    /// <summary>The absolute path of the created archive file. Null on failure.</summary>
    public string? BackupFilePath { get; init; }

    /// <summary>The size of the written archive in megabytes.</summary>
    public double SizeMB { get; init; }

    /// <summary>Wall-clock milliseconds elapsed during the operation.</summary>
    public double DurationMs { get; init; }

    /// <summary>Human-readable error message populated when <see cref="Success"/> is false.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>The database ID of the <see cref="AgentX.Core.Data.Entities.BackupEntity"/> row. Zero on failure.</summary>
    public long BackupId { get; init; }
}

/// <summary>
/// The result returned after a restore operation completes (or fails).
/// </summary>
public class RestoreResult
{
    /// <summary>True when the restore completed without errors.</summary>
    public bool Success { get; init; }

    /// <summary>Number of document records present in the restored database.</summary>
    public int RestoredDocumentCount { get; init; }

    /// <summary>Number of conversation records present in the restored database.</summary>
    public int RestoredConversationCount { get; init; }

    /// <summary>Number of workflow records present in the restored database.</summary>
    public int RestoredWorkflowCount { get; init; }

    /// <summary>Wall-clock milliseconds elapsed during the operation.</summary>
    public double DurationMs { get; init; }

    /// <summary>Human-readable error message populated when <see cref="Success"/> is false.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Non-fatal warnings generated during the restore (e.g. missing optional files).</summary>
    public List<string> WarningMessages { get; init; } = new();
}

/// <summary>
/// Describes the current phase and completion percentage of a running backup or restore.
/// Designed to be reported through <see cref="IProgress{T}"/>.
/// </summary>
public class BackupProgress
{
    /// <summary>Short description of the current operation phase (e.g. "Copying database", "Writing archive").</summary>
    public string Phase { get; init; } = string.Empty;

    /// <summary>Completion percentage in the range 0-100.</summary>
    public int PercentComplete { get; init; }

    /// <summary>The name of the item currently being processed, if applicable.</summary>
    public string? CurrentItem { get; init; }
}

/// <summary>
/// An estimate of how large a backup archive will be before it is created.
/// </summary>
public class BackupSizeEstimate
{
    /// <summary>Current size of the SQLite database file in megabytes.</summary>
    public double DatabaseSizeMB { get; init; }

    /// <summary>Combined size of all files under the documents storage folder in megabytes.</summary>
    public double DocumentsSizeMB { get; init; }

    /// <summary>Sum of <see cref="DatabaseSizeMB"/> and <see cref="DocumentsSizeMB"/>.</summary>
    public double TotalEstimatedMB { get; init; }

    /// <summary>Number of document records in the database at estimation time.</summary>
    public int DocumentCount { get; init; }
}

/// <summary>
/// Configuration controlling the automatic scheduled backup behaviour.
/// Persisted as JSON inside <see cref="AgentX.Core.Services.Settings.AppSettings"/> or a dedicated key-value row.
/// </summary>
public class BackupScheduleConfig
{
    /// <summary>Whether scheduled backups are active.</summary>
    public bool Enabled { get; set; }

    /// <summary>How many hours to wait between automatic backups. Default is 168 (weekly).</summary>
    public int IntervalHours { get; set; } = 168;

    /// <summary>
    /// Maximum number of automatic backup archives to retain.
    /// Once this limit is reached the oldest archive is deleted before a new one is created.
    /// </summary>
    public int MaxBackupsToKeep { get; set; } = 5;

    /// <summary>Directory where scheduled backup archives are written.</summary>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>Optional AES-256 encryption password for scheduled backups.</summary>
    public string? EncryptionPassword { get; set; }
}
