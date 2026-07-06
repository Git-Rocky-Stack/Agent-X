using System;
using AgentX.Core.Services.Sync.Models;

namespace AgentX.App.Services;

/// <summary>
/// A truthful snapshot of the annunciator subsystems (DESIGN.md: every lamp
/// is data-bound). Values come from typed service queries, never from
/// display strings, so lamp semantics survive localization.
/// </summary>
public sealed record AnnunciatorState(
    int InboxPendingCount,
    bool SyncConfigured,
    SyncState SyncState,
    bool JobsRunning,
    bool JobsLastRunFailed,
    DateTime? LastBackupUtc);

/// <summary>
/// Aggregates the instrument-strip annunciator sources (Inbox, Sync,
/// Workflows, Backup) on a polling cadence: cheap queries every cycle,
/// heavier history queries every fourth cycle. Each source fails soft -
/// a failing subsystem keeps its previous reading instead of taking the
/// cluster down.
/// </summary>
public interface IAnnunciatorService : IDisposable
{
    /// <summary>Raised on each poll cycle with the merged snapshot.</summary>
    event EventHandler<AnnunciatorState>? StateChanged;

    /// <summary>Starts periodic polling; the first cycle is delayed so the UI can render.</summary>
    void StartPolling(int intervalMs = 30_000, int initialDelayMs = 6_000);

    /// <summary>Stops polling and releases the timer.</summary>
    void StopPolling();
}
