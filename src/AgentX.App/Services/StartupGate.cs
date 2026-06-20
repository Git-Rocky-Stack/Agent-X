using System;
using System.Threading;
using System.Threading.Tasks;

namespace AgentX.App.Services;

/// <summary>
/// A one-shot gate that data-backed UI awaits before issuing its first database read, so nothing
/// queries the schema until the startup migration gate has passed.
///
/// AX-QA-003 follow-up — closes the dashboard-load-vs-migration race: <c>MainWindow</c> shows the
/// <c>DashboardPage</c> shell immediately (before the awaited migration completes), so without this
/// gate <c>DashboardViewModel.InitializeAsync</c> would fan out reads against a not-yet-migrated
/// schema. <see cref="StartupOrchestrator"/> opens the gate the instant the migration succeeds —
/// before the REST API and connectors start, since data-backed reads only need a valid schema. If
/// startup enters the recovery state the gate is faulted so any waiter is released (the app is
/// exiting anyway) instead of awaiting forever.
/// </summary>
public interface IStartupGate
{
    /// <summary>True once data-backed reads are permitted (the migration succeeded).</summary>
    bool IsDataReady { get; }

    /// <summary>
    /// Completes once data-backed reads are permitted. Returns immediately when the gate is already
    /// open. Throws <see cref="OperationCanceledException"/> if startup failed (recovery state) or if
    /// <paramref name="cancellationToken"/> fires first.
    /// </summary>
    Task WaitForDataReadyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the gate, releasing all waiters. Idempotent; a no-op once the gate is already opened or
    /// failed. Called once by startup after the migration gate passes.
    /// </summary>
    void SignalDataReady();

    /// <summary>
    /// Fails the gate, releasing all waiters with cancellation because startup did not reach a
    /// data-ready state and the app will not run. Idempotent; ignored once the gate is already open.
    /// </summary>
    void SignalStartupFailed();
}

/// <summary>
/// Default <see cref="IStartupGate"/> backed by a single <see cref="TaskCompletionSource"/>. Thread-safe
/// via the TCS's atomic <c>TrySet*</c> transitions; registered as a singleton so the orchestrator and
/// every data-backed consumer share one instance.
/// </summary>
public sealed class StartupGate : IStartupGate
{
    private readonly TaskCompletionSource _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsDataReady => _ready.Task.IsCompletedSuccessfully;

    public async Task WaitForDataReadyAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: already opened (or failed) — observe the result (success or cancellation) now.
        if (_ready.Task.IsCompleted || !cancellationToken.CanBeCanceled)
        {
            await _ready.Task.ConfigureAwait(false);
            return;
        }

        // Race the gate against the caller's cancellation without leaking the registration.
        var cancelTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(
                   static state => ((TaskCompletionSource)state!).TrySetCanceled(),
                   cancelTcs))
        {
            var winner = await Task.WhenAny(_ready.Task, cancelTcs.Task).ConfigureAwait(false);
            await winner.ConfigureAwait(false); // surface success or cancellation from the winner
        }
    }

    public void SignalDataReady() => _ready.TrySetResult();

    public void SignalStartupFailed() => _ready.TrySetCanceled();
}
