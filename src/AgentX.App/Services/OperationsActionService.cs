using AgentX.Core.Services.Chat;
using AgentX.Core.Documents;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Plugins;
using AgentX.Core.Services.Sync;
using Serilog;

namespace AgentX.App.Services;

/// <summary>
/// Shared execution layer for safe Operations-page remediation actions.
/// </summary>
public sealed class OperationsActionService : IOperationsActionService
{
    private readonly IConversationSummaryService _conversationSummaryService;
    private readonly IDocumentService _documentService;
    private readonly IInboxService _inboxService;
    private readonly IPluginService _pluginService;
    private readonly ISyncService _syncService;
    private readonly ILogger _log;

    public OperationsActionService(
        IConversationSummaryService conversationSummaryService,
        IDocumentService documentService,
        IInboxService inboxService,
        IPluginService pluginService,
        ISyncService syncService,
        ILogger logger)
    {
        _conversationSummaryService = conversationSummaryService ?? throw new ArgumentNullException(nameof(conversationSummaryService));
        _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
        _inboxService = inboxService ?? throw new ArgumentNullException(nameof(inboxService));
        _pluginService = pluginService ?? throw new ArgumentNullException(nameof(pluginService));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _log = logger?.ForContext<OperationsActionService>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OperationsActionResult> EnableConnectorAsync(long pluginId, CancellationToken ct = default)
    {
        try
        {
            if (pluginId <= 0)
            {
                return new OperationsActionResult(false, "Select a connector before enabling it.");
            }

            var plugins = await _pluginService.GetInstalledPluginsAsync().ConfigureAwait(false);
            var plugin = plugins.FirstOrDefault(candidate => candidate.Id == pluginId);
            if (plugin is null)
            {
                return new OperationsActionResult(false, "That connector is no longer installed.");
            }

            var displayName = string.IsNullOrWhiteSpace(plugin.Name)
                ? "Connector"
                : plugin.Name;

            if (!string.Equals(plugin.PluginType, PluginType.DataConnector.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return new OperationsActionResult(false, $"{displayName} can only be enabled from Plugin Manager.");
            }

            if (plugin.IsEnabled)
            {
                return new OperationsActionResult(true, $"{displayName} is already enabled.");
            }

            ct.ThrowIfCancellationRequested();
            await _pluginService.EnablePluginAsync(pluginId).ConfigureAwait(false);
            return new OperationsActionResult(true, $"Enabled {displayName}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Operations: connector enable failed for plugin {PluginId}", pluginId);
            return new OperationsActionResult(false, $"Connector enable failed: {ex.Message}");
        }
    }

    public async Task<OperationsActionResult> GenerateInboxPreviewsAsync(CancellationToken ct = default)
    {
        try
        {
            var pendingCount = await _inboxService.GetPendingCountAsync().ConfigureAwait(false);
            if (pendingCount <= 0)
            {
                return new OperationsActionResult(true, "No pending inbox items need preview generation.");
            }

            await _inboxService.GenerateAllPreviewsAsync(ct).ConfigureAwait(false);
            return new OperationsActionResult(true, "Generated AI previews for pending inbox items.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.Warning("Operations: inbox preview generation timed out");
            return new OperationsActionResult(false, "Inbox preview generation timed out.");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Operations: inbox preview generation failed");
            return new OperationsActionResult(false, $"Preview generation failed: {ex.Message}");
        }
    }

    public async Task<OperationsActionResult> ReindexImportedDocumentAsync(long documentId, CancellationToken ct = default)
    {
        try
        {
            if (documentId <= 0)
            {
                return new OperationsActionResult(false, "Select an imported document before retrying indexing.");
            }

            await _documentService.ReindexDocumentAsync(documentId, ct).ConfigureAwait(false);
            return new OperationsActionResult(true, "Queued imported document for re-indexing.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Operations: imported document re-index failed for document {DocumentId}", documentId);
            return new OperationsActionResult(false, $"Re-index failed: {ex.Message}");
        }
    }

    public async Task<OperationsActionResult> RefreshConversationSummariesAsync(
        int maxConversations = 4,
        CancellationToken ct = default)
    {
        try
        {
            var refreshed = await _conversationSummaryService
                .RefreshStaleSummariesAsync(maxConversations, ct)
                .ConfigureAwait(false);

            var message = refreshed switch
            {
                > 1 => $"Refreshed {refreshed} conversation summaries.",
                1 => "Refreshed 1 conversation summary.",
                _ => "No stale conversation summaries needed refresh."
            };

            return new OperationsActionResult(true, message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.Warning("Operations: conversation summary refresh timed out");
            return new OperationsActionResult(false, "Conversation summary refresh timed out.");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Operations: conversation summary refresh failed");
            return new OperationsActionResult(false, $"Summary refresh failed: {ex.Message}");
        }
    }

    public async Task<OperationsActionResult> RunManualSyncAsync(CancellationToken ct = default)
    {
        try
        {
            var config = await _syncService.GetConfigurationAsync().ConfigureAwait(false);
            if (config is null || string.IsNullOrWhiteSpace(config.SyncFolderPath))
            {
                return new OperationsActionResult(false, "Save a sync configuration before running sync from Operations.");
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(10));

            var changeSet = await _syncService.ExportChangesAsync(ct: timeoutCts.Token).ConfigureAwait(false);
            await _syncService.StartAutoSyncAsync(timeoutCts.Token).ConfigureAwait(false);

            return new OperationsActionResult(
                true,
                $"Sync complete — {changeSet.Changes.Count} change(s) exported.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.Warning("Operations: manual sync timed out");
            return new OperationsActionResult(false, "Sync timed out after 10 minutes. Check sync folder connectivity and try again.");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Operations: manual sync failed");
            return new OperationsActionResult(false, $"Sync failed: {ex.Message}");
        }
    }
}
