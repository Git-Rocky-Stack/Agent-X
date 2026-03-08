namespace AgentX.Core.Data.Entities;

/// <summary>
/// Persists a historical record of every sync pass (export or import) performed by
/// this Agent-X installation.  Used to populate the sync history view and to
/// diagnose failures.
/// </summary>
public class SyncLogEntity
{
    /// <summary>Primary key.</summary>
    public long Id { get; set; }

    /// <summary>UTC timestamp at which the sync pass completed (or failed).</summary>
    public DateTime SyncedAt { get; set; }

    /// <summary>
    /// Direction of the sync pass.
    /// <list type="bullet">
    ///   <item><description><c>"export"</c> — local changes were written to the sync folder.</description></item>
    ///   <item><description><c>"import"</c> — a remote change set was read from the sync folder and applied locally.</description></item>
    /// </list>
    /// </summary>
    public string Direction { get; set; } = string.Empty;

    /// <summary>Number of entity changes successfully applied during this pass.</summary>
    public int ChangesApplied { get; set; }

    /// <summary>Number of conflicts detected between local and incoming remote state.</summary>
    public int ConflictsDetected { get; set; }

    /// <summary>
    /// Number of conflicts that were automatically or manually resolved before
    /// the pass completed.
    /// </summary>
    public int ConflictsResolved { get; set; }

    /// <summary>Wall-clock duration of this sync pass in milliseconds.</summary>
    public double DurationMs { get; set; }

    /// <summary>
    /// Error message if the pass failed, or <see langword="null"/> when
    /// <see cref="IsSuccess"/> is <see langword="true"/>.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// <see langword="true"/> when the pass completed without a fatal error;
    /// <see langword="false"/> when an exception caused the pass to abort.
    /// </summary>
    public bool IsSuccess { get; set; }
}
