using System;

namespace AgentX.App.Services;

/// <summary>
/// Represents a snapshot of the status bar state at a point in time.
/// Subscribers use this to update UI elements (connection indicator, model name,
/// indexing progress, document count).
/// </summary>
public sealed record StatusBarState(
    bool IsConnected,
    string ConnectionStatus,
    string ActiveModelName,
    bool IsIndexing,
    int IndexingQueueLength,
    long DocumentCount);

/// <summary>
/// Manages periodic polling of AI service connection, indexing status, and document count.
/// Raises <see cref="StateChanged"/> on each poll cycle so the UI layer can update
/// its status bar elements. Keeps the polling logic out of MainWindow.
/// </summary>
public interface IStatusBarService : IDisposable
{
    /// <summary>
    /// Whether the AI service was connected on the last successful poll.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// The active model name from the last successful poll, or <see cref="string.Empty"/>.
    /// </summary>
    string ActiveModelName { get; }

    /// <summary>
    /// Starts periodic polling at the given interval.
    /// The first check is delayed by <paramref name="initialDelayMs"/> to allow
    /// the UI to render before the first network call.
    /// </summary>
    void StartPolling(int intervalMs = 30_000, int initialDelayMs = 5_000);

    /// <summary>
    /// Stops polling and releases the timer.
    /// </summary>
    void StopPolling();

    /// <summary>
    /// Raised on each successful poll with the computed state snapshot.
    /// UI subscribers use this to update status bar elements.
    /// </summary>
    event EventHandler<StatusBarState>? StateChanged;
}
