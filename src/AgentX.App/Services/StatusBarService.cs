using System;
using System.Threading;
using AgentX.Core.AI;
using AgentX.Core.Documents;
using AgentX.Core.Services.Indexing;
using AgentX.Core.Services.Localization;
using Serilog;

namespace AgentX.App.Services;

/// <summary>
/// Periodically polls the AI service, indexing service, and document service
/// to produce <see cref="StatusBarState"/> snapshots. Raises <see cref="StateChanged"/>
/// on each cycle so the UI layer can update status bar elements without holding
/// service references directly.
/// </summary>
public sealed class StatusBarService : IStatusBarService
{
    private readonly IServiceProvider _serviceProvider;

    private bool _isConnected;
    private string _activeModelName = string.Empty;
    private bool _disposed;
    private CancellationTokenSource? _pollingCts;

    /// <summary>
    /// Creates a new StatusBarService.
    /// The <paramref name="serviceProvider"/> is used to resolve IAiService,
    /// IIndexingService, and IDocumentService on each poll cycle.
    /// </summary>
    public StatusBarService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public bool IsConnected => _isConnected;

    /// <inheritdoc />
    public string ActiveModelName => _activeModelName;

    /// <inheritdoc />
    public event EventHandler<StatusBarState>? StateChanged;

    /// <inheritdoc />
    public void StartPolling(int intervalMs = 30_000, int initialDelayMs = 5_000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(intervalMs);
        ArgumentOutOfRangeException.ThrowIfNegative(initialDelayMs);

        StopPolling();

        _pollingCts = new CancellationTokenSource();
        _ = RunPollingLoopAsync(intervalMs, initialDelayMs, _pollingCts.Token);

        Log.Debug("StatusBarService polling started (interval={IntervalMs}ms, initialDelay={DelayMs}ms)",
            intervalMs, initialDelayMs);
    }

    /// <inheritdoc />
    public void StopPolling()
    {
        _pollingCts?.Cancel();
        _pollingCts?.Dispose();
        _pollingCts = null;

        Log.Debug("StatusBarService polling stopped");
    }

    /// <summary>
    /// Performs a single poll cycle and raises <see cref="StateChanged"/>.
    /// Called by the delayed initial check. Subsequent cycles are driven by
    /// the MainWindow's DispatcherTimer (which calls this method via Tick).
    /// </summary>
    public async Task PollAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var connected = false;
        var modelId = string.Empty;
        var localization = ResolveLocalization();
        // The status names the active provider; it used to say Ollama whatever was active.
        var providerName = ProviderStatusText.GenericName(localization);
        var isIndexing = false;
        var indexingQueueLength = 0;
        var docCount = 0L;

        // --- Connection status ---
        try
        {
            var aiService = (IAiService)_serviceProvider.GetService(typeof(IAiService))!;
            var activeProvider = aiService.ActiveProvider;
            if (!string.IsNullOrWhiteSpace(activeProvider.DisplayName))
            {
                providerName = activeProvider.DisplayName;
            }

            connected = await activeProvider.CheckConnectionAsync();

            if (connected)
            {
                modelId = aiService.ActiveModelId ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Status bar connection check failed");
        }

        // --- Indexing status ---
        try
        {
            var indexingService = (IIndexingService)_serviceProvider.GetService(typeof(IIndexingService))!;
            if (indexingService.IsProcessing)
            {
                isIndexing = true;
                indexingQueueLength = await indexingService.GetQueueLengthAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Indexing status check failed");
        }

        // --- Document count ---
        try
        {
            var docService = (IDocumentService)_serviceProvider.GetService(typeof(IDocumentService))!;
            docCount = await docService.GetTotalDocumentCountAsync();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Document count check failed");
        }

        _isConnected = connected;
        _activeModelName = modelId;

        var state = new StatusBarState(
            connected,
            connected
                ? (!string.IsNullOrEmpty(modelId)
                    ? ProviderStatusText.ConnectedToModel(localization, modelId)
                    : ProviderStatusText.ConnectedTo(localization, providerName))
                : ProviderStatusText.NotAvailable(localization, providerName),
            modelId,
            isIndexing,
            indexingQueueLength,
            docCount);

        StateChanged?.Invoke(this, state);
    }

    /// <summary>The app's localization service, or null (English text) when it cannot be resolved.</summary>
    private ILocalizationService? ResolveLocalization()
    {
        try
        {
            return _serviceProvider.GetService(typeof(ILocalizationService)) as ILocalizationService;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Localization is unavailable to the status strip; using English");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopPolling();
        _disposed = true;
    }

    // ── Private ──────────────────────────────────────────────────

    private async Task RunPollingLoopAsync(int intervalMs, int initialDelayMs, CancellationToken ct)
    {
        try
        {
            if (initialDelayMs > 0)
            {
                await Task.Delay(initialDelayMs, ct);
            }

            do
            {
                await PollAsync();

                if (intervalMs == 0)
                {
                    return;
                }

                await Task.Delay(intervalMs, ct);
            }
            while (!ct.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "StatusBarService polling loop failed");
        }
    }
}
