namespace AgentX.Core.Data.Entities;

/// <summary>
/// Persists a record of each backup operation for history tracking and management.
/// </summary>
public class BackupEntity
{
    /// <summary>Primary key.</summary>
    public long Id { get; set; }

    /// <summary>The file name of the backup archive (e.g. agentx-backup-2026-03-07-120000.agentxbak).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>The absolute file path where the backup archive is stored on disk.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Indicates how the backup was triggered: "manual" or "scheduled".</summary>
    public string BackupType { get; set; } = "manual";

    /// <summary>The size of the backup archive in megabytes.</summary>
    public double SizeMB { get; set; }

    /// <summary>UTC timestamp when the backup was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Optional user-supplied notes describing the backup.</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Indicates whether this backup passed integrity validation after creation.
    /// A value of false means the archive may be incomplete or corrupt.
    /// </summary>
    public bool IsValid { get; set; }
}
