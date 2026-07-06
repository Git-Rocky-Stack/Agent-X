using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentX.Core.Services.Analytics;
using AgentX.Core.Services.Backup;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Sync;
using AgentX.Core.Services.Sync.Models;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AgentX.App.Services;

/// <summary>
/// Default <see cref="IAnnunciatorService"/>: resolves the typed subsystem
/// services per cycle (matching the StatusBarService pattern) and merges
/// their readings into an <see cref="AnnunciatorState"/>.
/// Cheap sources (inbox pending count, sync status) run every cycle;
/// heavier sources (backup history, workflow run intelligence) run every
/// fourth cycle and hold their last reading in between.
/// </summary>
public sealed class AnnunciatorService : IAnnunciatorService
{
    private const int HeavyCycleEvery = 4;

    private readonly IServiceProvider _serviceProvider;
    private bool _disposed;
    private CancellationTokenSource? _pollingCts;
    private int _cycle;

    // Last known readings (fail-soft carriers)
    private int _inboxPending;
    private bool _syncConfigured;
    private SyncState _syncState = SyncState.Idle;
    private bool _jobsRunning;
    private bool _jobsLastRunFailed;
    private DateTime? _lastBackupUtc;

    public AnnunciatorService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public event EventHandler<AnnunciatorState>? StateChanged;

    /// <inheritdoc />
    public void StartPolling(int intervalMs = 30_000, int initialDelayMs = 6_000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(intervalMs);
        ArgumentOutOfRangeException.ThrowIfNegative(initialDelayMs);

        StopPolling();
        _pollingCts = new CancellationTokenSource();
        _ = RunPollingLoopAsync(intervalMs, initialDelayMs, _pollingCts.Token);
    }

    /// <inheritdoc />
    public void StopPolling()
    {
        _pollingCts?.Cancel();
        _pollingCts?.Dispose();
        _pollingCts = null;
    }

    private async Task RunPollingLoopAsync(int intervalMs, int initialDelayMs, CancellationToken ct)
    {
        try
        {
            await Task.Delay(initialDelayMs, ct).ConfigureAwait(false);
            while (!ct.IsCancellationRequested)
            {
                await PollOnceAsync(ct).ConfigureAwait(false);
                await Task.Delay(intervalMs, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Annunciator polling loop terminated unexpectedly");
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();

        // Cheap sources: every cycle.
        try
        {
            var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
            _inboxPending = await inbox.GetPendingCountAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Annunciator: inbox pending count unavailable; keeping previous reading");
        }

        try
        {
            var sync = scope.ServiceProvider.GetRequiredService<ISyncService>();
            var config = await sync.GetConfigurationAsync().ConfigureAwait(false);
            _syncConfigured = config is not null;
            _syncState = sync.Status.SyncState;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Annunciator: sync status unavailable; keeping previous reading");
        }

        // Heavier sources: every fourth cycle (and the first).
        if (_cycle % HeavyCycleEvery == 0)
        {
            try
            {
                var backup = scope.ServiceProvider.GetRequiredService<IBackupService>();
                var history = await backup.GetBackupHistoryAsync().ConfigureAwait(false);
                _lastBackupUtc = history.Count > 0
                    ? history.Max(b => b.CreatedAt)
                    : null;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Annunciator: backup history unavailable; keeping previous reading");
            }

            try
            {
                var analytics = scope.ServiceProvider.GetRequiredService<IAnalyticsService>();
                var overview = await analytics.GetWorkflowIntelligenceOverviewAsync(
                    maxRecentRuns: 3,
                    maxTopWorkflows: 0,
                    recentActivityDays: 1,
                    ct).ConfigureAwait(false);
                _jobsRunning = overview.RecentRuns.Any(r =>
                    string.Equals(r.Status, "running", StringComparison.OrdinalIgnoreCase));
                var latest = overview.RecentRuns
                    .OrderByDescending(r => r.StartedAt)
                    .FirstOrDefault();
                _jobsLastRunFailed = latest is not null &&
                    (latest.HasErrorPreview ||
                     latest.Status.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                     latest.Status.Contains("cancel", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Annunciator: workflow intelligence unavailable; keeping previous reading");
            }
        }

        _cycle++;

        StateChanged?.Invoke(this, new AnnunciatorState(
            _inboxPending,
            _syncConfigured,
            _syncState,
            _jobsRunning,
            _jobsLastRunFailed,
            _lastBackupUtc));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPolling();
    }
}
