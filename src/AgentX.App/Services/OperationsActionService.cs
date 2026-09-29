using AgentX.Core.Documents;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Plugins;
using AgentX.Core.Services.Sync;
using AgentX.Core.Services.Sync.Models;
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
    private readonly ILocalizationService _localization;
    private readonly ILogger _log;

    public OperationsActionService(
        IConversationSummaryService conversationSummaryService,
        IDocumentService documentService,
        IInboxService inboxService,
        IPluginService pluginService,
        ISyncService syncService,
        ILocalizationService localization,
        ILogger logger)
    {
        _conversationSummaryService = conversationSummaryService ?? throw new ArgumentNullException(nameof(conversationSummaryService));
        _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
        _inboxService = inboxService ?? throw new ArgumentNullException(nameof(inboxService));
        _pluginService = pluginService ?? throw new ArgumentNullException(nameof(pluginService));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _log = logger?.ForContext<OperationsActionService>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OperationsActionResult> EnableConnectorAsync(long pluginId, CancellationToken ct = default)
    {
        try
        {
            if (pluginId <= 0)
            {
                return new OperationsActionResult(false, _localization.GetString("Ops_ActionSelectConnector"));
            }

            var plugins = await _pluginService.GetInstalledPluginsAsync().ConfigureAwait(false);
            var plugin = plugins.FirstOrDefault(candidate => candidate.Id == pluginId);
            if (plugin is null)
            {
                return new OperationsActionResult(false, _localization.GetString("Ops_ActionConnectorGone"));
            }

            var displayName = string.IsNullOrWhiteSpace(plugin.Name)
                ? _localization.GetString("Ops_PluginTypeConnector")
                : plugin.Name;

            if (!string.Equals(plugin.PluginType, PluginType.DataConnector.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return new OperationsActionResult(false, _localization.GetString("Ops_ActionEnableInPluginManager", displayName));
            }

            if (plugin.IsEnabled)
            {
                return new OperationsActionResult(true, _localization.GetString("Ops_ActionAlreadyEnabled", displayName));
            }

            ct.ThrowIfCancellationRequested();
            await _pluginService.EnablePluginAsync(pluginId).ConfigureAwait(false);
            return new OperationsActionResult(true, _localization.GetString("Ops_ActionEnabled", displayName));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Operations: connector enable failed for plugin {PluginId}", pluginId);
            return new OperationsActionResult(false, _localization.GetString("Ops_ActionEnableFailed", ex.Message));
        }
    }

    public async Task<OperationsActionResult> GenerateInboxPreviewsAsync(CancellationToken ct = default)
    {
        try
        {
            var pendingCount = await _inboxService.GetPendingCountAsync().ConfigureAwait(false);
            if (pendingCount <= 0)
            {
                return new OperationsActionResult(true, _localization.GetString("Ops_ActionNoPreviewsNeeded"));
            }

            await _inboxService.GenerateAllPreviewsAsync(ct).ConfigureAwait(false);
            return new OperationsActionResult(true, _localization.GetString("Ops_ActionPreviewsGenerated"));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.Warning("Operations: inbox preview generation timed out");
            return new OperationsActionResult(false, _localization.GetString("Ops_ActionPreviewsTimedOut"));
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Operations: inbox preview generation failed");
            return new OperationsActionResult(false, _localization.GetString("Ops_ActionPreviewsFailed", ex.Message));
        }
    }

    public async Task<OperationsActionResult> ReindexImportedDocumentAsync(long documentId, CancellationToken ct = default)
    {
        try
        {
            if (documentId <= 0)
            {
                return new OperationsActionResult(false, _localization.GetString("Ops_ActionSelectDocument"));
            }

            await _documentService.ReindexDocumentAsync(documentId, ct).ConfigureAwait(false);
            return new OperationsActionResult(true, _localization.GetString("Ops_ActionReindexQueued"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Operations: imported document re-index failed for document {DocumentId}", documentId);
            return new OperationsActionResult(false, _localization.GetString("Ops_ActionReindexFailed", ex.Message));
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
                > 1 => _localization.GetString("Ops_ActionSummariesRefreshedMany", refreshed),
                1 => _localization.GetString("Ops_ActionSummaryRefreshedOne"),
                _ => _localization.GetString("Ops_ActionNoSummariesToRefresh")
            };

            return new OperationsActionResult(true, message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.Warning("Operations: conversation summary refresh timed out");
            return new OperationsActionResult(false, _localization.GetString("Ops_ActionSummariesTimedOut"));
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Operations: conversation summary refresh failed");
            return new OperationsActionResult(false, _localization.GetString("Ops_ActionSummariesFailed", ex.Message));
        }
    }

    public async Task<OperationsActionResult> RunManualSyncAsync(CancellationToken ct = default)
    {
        try
        {
            var config = await _syncService.GetConfigurationAsync().ConfigureAwait(false);
            if (config is null || string.IsNullOrWhiteSpace(config.SyncFolderPath))
            {
                return new OperationsActionResult(false, _localization.GetString("Ops_ActionSyncNotConfigured"));
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(10));

            // One real pass: export local changes, then import every peer file.
            var result = await _syncService.SyncNowAsync(timeoutCts.Token).ConfigureAwait(false);

            return new OperationsActionResult(!result.HasProblems, DescribeSyncPass(result));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.Warning("Operations: manual sync timed out");
            return new OperationsActionResult(false, _localization.GetString("Ops_ActionSyncTimedOut"));
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Operations: manual sync failed");
            return new OperationsActionResult(false, _localization.GetString("Ops_ActionSyncFailed", ex.Message));
        }
    }

    /// <summary>What one sync pass did, in the user's language, with any problems first.</summary>
    private string DescribeSyncPass(SyncRunResult result)
    {
        var summary = _localization.GetString(
            "Ops_ActionSyncSummary",
            result.ExportedChanges,
            result.PeerFilesImported,
            result.PeerFilesFound,
            result.ChangesApplied);

        if (!result.HasProblems)
            return _localization.GetString("Ops_ActionSyncComplete", summary);

        var problems = new List<string>();
        if (result.PeerFilesPendingRetry > 0)
            problems.Add(_localization.GetString("Ops_ActionSyncRetry", result.PeerFilesPendingRetry));
        if (result.PeerFilesUnreadable > 0)
            problems.Add(_localization.GetString("Ops_ActionSyncUnreadable", result.PeerFilesUnreadable));
        if (result.ChangesRejected > 0)
            problems.Add(_localization.GetString("Ops_ActionSyncSkipped", result.ChangesRejected));

        return _localization.GetString("Ops_ActionSyncProblems", string.Join(", ", problems), summary);
    }
}
