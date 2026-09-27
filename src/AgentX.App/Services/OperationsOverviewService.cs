using System.Globalization;
using System.Text;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Analytics;
using AgentX.Core.Services.Analytics.Models;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Plugins;
using AgentX.Core.Services.Sync;
using AgentX.Core.Services.Sync.Models;
using AgentX.Core.Services.Workflows;
using Serilog;

namespace AgentX.App.Services;

/// <summary>
/// Aggregates the app's operational signals into one dashboard-friendly snapshot. The text is
/// written in the user's language; each status also carries an <see cref="OperationsStatusKind"/>
/// so that logic and status colors never read the text.
/// </summary>
public sealed class OperationsOverviewService : IOperationsOverviewService
{
    private readonly IAnalyticsService _analyticsService;
    private readonly IDocumentService _documentService;
    private readonly IInboxService _inboxService;
    private readonly IPluginService _pluginService;
    private readonly ISyncService _syncService;
    private readonly IWorkflowService _workflowService;
    private readonly ILocalizationService _localization;
    private readonly ILogger _log;

    public OperationsOverviewService(
        IAnalyticsService analyticsService,
        IDocumentService documentService,
        IInboxService inboxService,
        IPluginService pluginService,
        ISyncService syncService,
        IWorkflowService workflowService,
        ILocalizationService localization,
        ILogger logger)
    {
        _analyticsService = analyticsService ?? throw new ArgumentNullException(nameof(analyticsService));
        _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
        _inboxService = inboxService ?? throw new ArgumentNullException(nameof(inboxService));
        _pluginService = pluginService ?? throw new ArgumentNullException(nameof(pluginService));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _log = logger?.ForContext<OperationsOverviewService>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OperationsOverviewSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var conversationTask = SafeAsync(
            () => _analyticsService.GetConversationIntelligenceAsync(maxRecent: 3, ct),
            new ConversationIntelligenceOverview(),
            "conversation intelligence");
        var workflowTask = SafeAsync(
            () => _analyticsService.GetWorkflowIntelligenceOverviewAsync(
                maxRecentRuns: 0,
                maxTopWorkflows: 1,
                recentActivityDays: 30,
                ct),
            new WorkflowIntelligenceOverview(),
            "workflow intelligence");
        var inboxTask = SafeAsync(
            () => _inboxService.GetPendingCountAsync(),
            0,
            "ingestion backlog");
        var pendingItemsTask = SafeAsync(
            () => _inboxService.GetAllItemsAsync(statusFilter: "pending", skip: 0, take: 3),
            Array.Empty<InboxItemEntity>() as IReadOnlyList<InboxItemEntity>,
            "pending inbox items");
        var importedItemsTask = SafeAsync(
            () => _inboxService.GetAllItemsAsync(statusFilter: "accepted", skip: 0, take: 8),
            Array.Empty<InboxItemEntity>() as IReadOnlyList<InboxItemEntity>,
            "recent imported documents");
        var syncConfigTask = SafeAsync(
            () => _syncService.GetConfigurationAsync(),
            null as SyncConfiguration,
            "sync configuration");
        var syncHistoryTask = SafeAsync(
            () => _syncService.GetSyncHistoryAsync(3),
            Array.Empty<SyncLogEntity>() as IReadOnlyList<SyncLogEntity>,
            "sync history");
        var pluginTask = SafeAsync(
            () => _pluginService.GetInstalledPluginsAsync(),
            Array.Empty<PluginEntity>() as IReadOnlyList<PluginEntity>,
            "plugin state");
        var workflowListTask = SafeAsync(
            () => _workflowService.GetAllWorkflowsAsync(),
            Array.Empty<WorkflowEntity>() as IReadOnlyList<WorkflowEntity>,
            "workflow list");

        await Task.WhenAll(
            conversationTask,
            workflowTask,
            inboxTask,
            pendingItemsTask,
            importedItemsTask,
            syncConfigTask,
            syncHistoryTask,
            pluginTask,
            workflowListTask);

        var conversation = await conversationTask;
        var workflow = await workflowTask;
        var pendingInbox = await inboxTask;
        var pendingItems = await pendingItemsTask;
        var importedItems = await importedItemsTask;
        var importedDocumentPreviews = await BuildImportedDocumentPreviewsAsync(importedItems, ct).ConfigureAwait(false);
        var syncConfig = await syncConfigTask;
        var syncHistory = await syncHistoryTask;
        var plugins = await pluginTask;
        var workflows = await workflowListTask;
        var syncStatus = _syncService.Status;

        var enabledConnectors = plugins
            .Where(plugin => IsPluginType(plugin, PluginType.DataConnector) && plugin.IsEnabled)
            .ToList();
        var enabledConnectorCount = enabledConnectors.Count;
        var enabledPluginCount = plugins.Count(plugin => plugin.IsEnabled);

        return new OperationsOverviewSnapshot
        {
            ConversationIntelligence = BuildConversationIntelligenceCard(conversation),
            SyncHealth = BuildSyncHealthCard(syncConfig, syncStatus),
            IngestionBacklog = BuildIngestionBacklogCard(pendingInbox, enabledConnectorCount),
            WorkflowActivity = BuildWorkflowCard(workflow, workflows),
            Connectors = BuildConnectorCard(plugins, enabledConnectors, enabledPluginCount),
            RecentConversationSummaries = BuildConversationPreviews(conversation),
            RecentSyncPasses = BuildSyncPreviews(syncHistory),
            PendingInboxItems = BuildInboxPreviews(pendingItems),
            RecentImportedDocuments = importedDocumentPreviews,
            RecentWorkflowRuns = BuildWorkflowRunPreviews(workflow),
            ConnectorPreviews = BuildConnectorPreviews(plugins)
        };
    }

    private async Task<T> SafeAsync<T>(Func<Task<T>> load, T fallback, string area)
    {
        try
        {
            return await load().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Operations overview: failed to load {Area}", area);
            return fallback;
        }
    }

    private OperationsCardSnapshot BuildConversationIntelligenceCard(ConversationIntelligenceOverview overview)
    {
        var latestSummary = overview.RecentSummaries.FirstOrDefault();
        var (status, statusKind) = overview.PendingRefreshes switch
        {
            > 1 => (_localization.GetString("Ops_RecallRefreshesPending", overview.PendingRefreshes),
                OperationsStatusKind.RecallRefreshesPending),
            1 => (_localization.GetString("Ops_RecallRefreshPending"), OperationsStatusKind.RecallRefreshesPending),
            _ when overview.StaleConversations > 1 => (
                _localization.GetString("Ops_RecallStaleSummaries", overview.StaleConversations),
                OperationsStatusKind.RecallStaleSummaries),
            _ when overview.StaleConversations == 1 => (
                _localization.GetString("Ops_RecallStaleSummary"),
                OperationsStatusKind.RecallStaleSummaries),
            _ when overview.SummarizedConversations > 0 => (
                _localization.GetString("Ops_RecallCurrent"),
                OperationsStatusKind.RecallCurrent),
            _ => (_localization.GetString("Ops_RecallInactive"), OperationsStatusKind.RecallInactive)
        };

        string detail;
        if (latestSummary is null)
        {
            detail = _localization.GetString("Ops_RecallDetailNoSummaries");
        }
        else
        {
            var snapshots = FormatCompactNumber(overview.CurrentSnapshots);
            var latest = FormatHelper.TimeAgoWithMonths(latestSummary.GeneratedAt);
            detail = _localization.GetString("Ops_RecallDetailSnapshots", snapshots, latest);
        }

        return new OperationsCardSnapshot
        {
            Headline = FormatCompactNumber(overview.SummarizedConversations),
            Status = status,
            StatusKind = statusKind,
            Detail = detail
        };
    }

    private IReadOnlyList<OperationsConversationPreview> BuildConversationPreviews(ConversationIntelligenceOverview overview)
    {
        var items = overview.RecentSummaries
            .Take(3)
            .Select(summary =>
            {
                var (status, statusKind) = summary.HasRefreshError
                    ? (_localization.GetString("Ops_SummaryRefreshError"), OperationsStatusKind.SummaryRefreshError)
                    : summary.IsStale
                        ? (_localization.GetString("Ops_SummaryStale"), OperationsStatusKind.SummaryStale)
                        : summary.PendingMessageCount > 0
                            ? (_localization.GetString("Ops_SummaryPending", summary.PendingMessageCount),
                                OperationsStatusKind.SummaryPending)
                            : (_localization.GetString("Ops_SummaryCurrent"), OperationsStatusKind.SummaryCurrent);
                var generated = FormatHelper.TimeAgoWithMonths(summary.GeneratedAt);

                return new OperationsConversationPreview
                {
                    ConversationId = summary.ConversationId,
                    Title = string.IsNullOrWhiteSpace(summary.Title)
                        ? _localization.GetString("Ops_SummaryUntitled")
                        : summary.Title,
                    Status = status,
                    StatusKind = statusKind,
                    Detail = !string.IsNullOrWhiteSpace(summary.PreviewText)
                        ? _localization.GetString("Ops_TextWithTime", TrimForPreview(summary.PreviewText, 120), generated)
                        : _localization.GetString(
                            "Ops_SummaryCovered", FormatCompactNumber(summary.CoveredMessageCount), generated)
                };
            })
            .ToArray();

        return items.Length > 0
            ? items
            : [new OperationsConversationPreview
            {
                Title = _localization.GetString("Ops_SummariesEmptyTitle"),
                Status = _localization.GetString("Ops_PlaceholderAnalytics"),
                StatusKind = OperationsStatusKind.Placeholder,
                Detail = _localization.GetString("Ops_SummariesEmptyDetail")
            }];
    }

    private OperationsCardSnapshot BuildSyncHealthCard(SyncConfiguration? config, SyncStatus status)
    {
        if (config is null || string.IsNullOrWhiteSpace(config.SyncFolderPath))
        {
            return new OperationsCardSnapshot
            {
                Headline = _localization.GetString("Ops_SyncNotConfigured"),
                Status = _localization.GetString("Ops_SyncOff"),
                StatusKind = OperationsStatusKind.SyncNotConfigured,
                Detail = _localization.GetString("Ops_SyncSetupHint")
            };
        }

        var headline = status.SyncState switch
        {
            SyncState.Syncing => _localization.GetString("Ops_SyncHeadlineSyncing"),
            SyncState.Conflict => _localization.GetString("Ops_SyncHeadlineConflict"),
            SyncState.Error => _localization.GetString("Ops_SyncHeadlineError"),
            _ when status.LastSyncAt.HasValue => FormatHelper.TimeAgoWithMonths(status.LastSyncAt.Value),
            _ => _localization.GetString("Ops_SyncHeadlineConfigured")
        };

        var (syncStatus, statusKind) = status.SyncState switch
        {
            SyncState.Syncing => (_localization.GetString("Ops_SyncStatusRunning"), OperationsStatusKind.SyncRunning),
            SyncState.Conflict => (_localization.GetString("Ops_SyncStatusConflict"), OperationsStatusKind.SyncConflict),
            SyncState.Error => (_localization.GetString("Ops_SyncStatusError"), OperationsStatusKind.SyncError),
            _ when status.PendingChanges > 1 => (
                _localization.GetString("Ops_SyncChangesPending", status.PendingChanges),
                OperationsStatusKind.SyncChangesPending),
            _ when status.PendingChanges == 1 => (
                _localization.GetString("Ops_SyncChangePending"),
                OperationsStatusKind.SyncChangesPending),
            _ => (_localization.GetString("Ops_SyncStandingBy"), OperationsStatusKind.SyncStandingBy)
        };

        var detail = !string.IsNullOrWhiteSpace(status.ErrorMessage)
            ? status.ErrorMessage
            : config.SyncScope == SyncScope.SelectedCollections
                ? _localization.GetString("Ops_SyncScopeSelected")
                : _localization.GetString("Ops_SyncScopeFull");

        return new OperationsCardSnapshot
        {
            Headline = headline,
            Status = syncStatus,
            StatusKind = statusKind,
            Detail = detail
        };
    }

    private IReadOnlyList<OperationsSyncPreview> BuildSyncPreviews(IReadOnlyList<SyncLogEntity> history)
    {
        var items = history
            .Take(3)
            .Select(entry =>
            {
                var (status, statusKind) = !entry.IsSuccess
                    ? (_localization.GetString("Ops_SyncPassFailed"), OperationsStatusKind.SyncPassFailed)
                    : entry.ConflictsDetected == 1
                        ? (_localization.GetString("Ops_SyncPassConflictOne"), OperationsStatusKind.SyncPassConflicts)
                        : entry.ConflictsDetected > 1
                            ? (_localization.GetString("Ops_SyncPassConflictsMany", entry.ConflictsDetected),
                                OperationsStatusKind.SyncPassConflicts)
                            : (_localization.GetString("Ops_SyncPassSucceeded"), OperationsStatusKind.SyncPassSucceeded);
                var changes = FormatCompactNumber(entry.ChangesApplied);
                var duration = FormatCompactDuration(entry.DurationMs);
                var synced = FormatHelper.TimeAgoWithMonths(entry.SyncedAt);

                return new OperationsSyncPreview
                {
                    SyncLogId = entry.Id,
                    Title = BuildSyncPassTitle(entry.Direction),
                    Status = status,
                    StatusKind = statusKind,
                    Detail = !entry.IsSuccess && !string.IsNullOrWhiteSpace(entry.ErrorMessage)
                        ? TrimForPreview(entry.ErrorMessage, 120)
                        : _localization.GetString("Ops_SyncPassDetail", changes, duration, synced)
                };
            })
            .ToArray();

        return items.Length > 0
            ? items
            : [new OperationsSyncPreview
            {
                Title = _localization.GetString("Ops_SyncPassesEmptyTitle"),
                Status = _localization.GetString("Ops_PlaceholderHistory"),
                StatusKind = OperationsStatusKind.Placeholder,
                Detail = _localization.GetString("Ops_SyncPassesEmptyDetail")
            }];
    }

    /// <summary>"Import sync" or "Export sync"; a direction the sync service does not write is named as stored.</summary>
    private string BuildSyncPassTitle(string direction) => direction.Trim().ToLowerInvariant() switch
    {
        "import" => _localization.GetString("Ops_SyncPassImport"),
        "export" => _localization.GetString("Ops_SyncPassExport"),
        _ => _localization.GetString("Ops_SyncPassOther", ToTitleCase(direction))
    };

    private OperationsCardSnapshot BuildIngestionBacklogCard(int pendingCount, int enabledConnectorCount)
    {
        var detail = pendingCount > 0
            ? _localization.GetString("Ops_BacklogDetailPending")
            : enabledConnectorCount > 0
                ? _localization.GetString("Ops_BacklogDetailConnectors")
                : _localization.GetString("Ops_BacklogDetailIdle");

        var (status, statusKind) = pendingCount switch
        {
            > 1 => (_localization.GetString("Ops_BacklogWaitingMany", pendingCount), OperationsStatusKind.BacklogWaiting),
            1 => (_localization.GetString("Ops_BacklogWaitingOne"), OperationsStatusKind.BacklogWaiting),
            _ => (_localization.GetString("Ops_BacklogClear"), OperationsStatusKind.BacklogClear)
        };

        return new OperationsCardSnapshot
        {
            Headline = FormatCompactNumber(pendingCount),
            Status = status,
            StatusKind = statusKind,
            Detail = detail
        };
    }

    private IReadOnlyList<OperationsInboxPreview> BuildInboxPreviews(IReadOnlyList<InboxItemEntity> items)
    {
        var previews = items
            .Take(3)
            .Select(item =>
            {
                var (source, sourceKind) = DescribeInboxSource(item);
                var added = FormatHelper.TimeAgoWithMonths(item.AddedAt);

                return new OperationsInboxPreview
                {
                    ItemId = item.Id,
                    Title = string.IsNullOrWhiteSpace(item.FileName)
                        ? _localization.GetString("Ops_InboxUntitled")
                        : item.FileName,
                    Status = source,
                    StatusKind = sourceKind,
                    Detail = string.IsNullOrEmpty(item.SuggestedCollectionName)
                        ? _localization.GetString("Ops_InboxDetail", item.FileType, added)
                        : _localization.GetString("Ops_InboxDetailSuggested", item.FileType, item.SuggestedCollectionName, added)
                };
            })
            .ToArray();

        return previews.Length > 0
            ? previews
            : [new OperationsInboxPreview
            {
                Title = _localization.GetString("Ops_InboxEmptyTitle"),
                Status = _localization.GetString("Ops_PlaceholderQueue"),
                StatusKind = OperationsStatusKind.Placeholder,
                Detail = _localization.GetString("Ops_InboxEmptyDetail")
            }];
    }

    private async Task<IReadOnlyList<OperationsImportedDocumentPreview>> BuildImportedDocumentPreviewsAsync(
        IReadOnlyList<InboxItemEntity> items,
        CancellationToken ct)
    {
        var candidates = items
            .Where(item => item.DocumentId.HasValue && item.DocumentId.Value > 0)
            .OrderByDescending(item => item.ProcessedAt ?? item.AddedAt)
            .Take(3)
            .ToArray();

        if (candidates.Length == 0)
        {
            return [new OperationsImportedDocumentPreview
            {
                Title = _localization.GetString("Ops_ImportedEmptyTitle"),
                Status = _localization.GetString("Ops_PlaceholderVault"),
                StatusKind = OperationsStatusKind.Placeholder,
                Detail = _localization.GetString("Ops_ImportedEmptyDetail")
            }];
        }

        var previewTasks = candidates.Select(item => BuildImportedDocumentPreviewAsync(item, ct));
        return await Task.WhenAll(previewTasks).ConfigureAwait(false);
    }

    private async Task<OperationsImportedDocumentPreview> BuildImportedDocumentPreviewAsync(
        InboxItemEntity item,
        CancellationToken ct)
    {
        DocumentEntity? document = null;

        try
        {
            ct.ThrowIfCancellationRequested();
            document = await _documentService.GetDocumentAsync(item.DocumentId!.Value).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Operations overview: failed to load imported document {DocumentId}", item.DocumentId!.Value);
        }

        var health = BuildImportedDocumentHealth(document);
        var (source, sourceKind) = DescribeInboxSource(item);

        return new OperationsImportedDocumentPreview
        {
            DocumentId = item.DocumentId!.Value,
            Title = string.IsNullOrWhiteSpace(item.FileName)
                ? _localization.GetString("Ops_ImportedUntitled")
                : item.FileName,
            Status = source,
            StatusKind = sourceKind,
            Health = health,
            HealthStatus = health switch
            {
                OperationsDocumentHealth.Searchable => _localization.GetString("Ops_HealthSearchable"),
                OperationsDocumentHealth.Processing => _localization.GetString("Ops_HealthProcessing"),
                _ => _localization.GetString("Ops_HealthNeedsAttention")
            },
            Detail = BuildImportedDocumentDetail(item, document, health)
        };
    }

    private static OperationsDocumentHealth BuildImportedDocumentHealth(DocumentEntity? document) =>
        document switch
        {
            null => OperationsDocumentHealth.NeedsAttention,
            { IndexingStatus: "completed", ChunkCount: > 0 } => OperationsDocumentHealth.Searchable,
            { IndexingStatus: "pending" } => OperationsDocumentHealth.Processing,
            { IndexingStatus: "processing" } => OperationsDocumentHealth.Processing,
            { IndexingStatus: "failed" } => OperationsDocumentHealth.NeedsAttention,
            { IndexingStatus: "completed", ChunkCount: <= 0 } => OperationsDocumentHealth.NeedsAttention,
            _ => OperationsDocumentHealth.NeedsAttention
        };

    private string BuildImportedDocumentHealthDetail(DocumentEntity? document, OperationsDocumentHealth health)
    {
        if (document is null)
        {
            return _localization.GetString("Ops_HealthReviewLink");
        }

        return health switch
        {
            OperationsDocumentHealth.Searchable when document.LastIndexedAt.HasValue =>
                _localization.GetString("Ops_HealthSearchableSince", FormatHelper.TimeAgoWithMonths(document.LastIndexedAt.Value)),
            OperationsDocumentHealth.Searchable => _localization.GetString("Ops_HealthSearchableNow"),
            OperationsDocumentHealth.Processing when string.Equals(document.IndexingStatus, "pending", StringComparison.OrdinalIgnoreCase) =>
                _localization.GetString("Ops_HealthQueued"),
            OperationsDocumentHealth.Processing => _localization.GetString("Ops_HealthIndexing"),
            OperationsDocumentHealth.NeedsAttention when !string.IsNullOrWhiteSpace(document.IndexingError) =>
                TrimForPreview(document.IndexingError, 72),
            _ => _localization.GetString("Ops_HealthReviewIndexing")
        };
    }

    private string BuildImportedDocumentDetail(
        InboxItemEntity item,
        DocumentEntity? document,
        OperationsDocumentHealth health)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(item.FileType))
        {
            parts.Add(ToTitleCase(item.FileType));
        }

        if (!string.IsNullOrWhiteSpace(item.SuggestedCollectionName))
        {
            parts.Add(_localization.GetString("Ops_ImportedToCollection", item.SuggestedCollectionName));
        }

        parts.Add(BuildImportedDocumentHealthDetail(document, health));
        return string.Join(", ", parts);
    }

    private OperationsCardSnapshot BuildWorkflowCard(
        WorkflowIntelligenceOverview overview,
        IReadOnlyList<WorkflowEntity> workflows)
    {
        var enabledCount = workflows.Count(workflow => workflow.IsEnabled);
        var topWorkflow = overview.TopWorkflows.FirstOrDefault();
        var outcomeRuns = overview.SuccessfulRuns + overview.FailedOrCancelledRuns;

        var (status, statusKind) = overview.TotalRuns switch
        {
            > 0 when outcomeRuns > 0 => (
                _localization.GetString("Ops_WorkflowSuccessRate", overview.SuccessRate.ToString("F0", CultureInfo.CurrentCulture)),
                OperationsStatusKind.WorkflowSuccessRate),
            > 1 => (
                _localization.GetString("Ops_WorkflowRunsRecorded", FormatCompactNumber(overview.TotalRuns)),
                OperationsStatusKind.WorkflowRunsRecorded),
            1 => (_localization.GetString("Ops_WorkflowRunRecorded"), OperationsStatusKind.WorkflowRunsRecorded),
            _ => (_localization.GetString("Ops_WorkflowReady"), OperationsStatusKind.WorkflowReadyToAutomate)
        };

        var (supportingPrimary, supportingPrimaryKind) = overview.ActiveWorkflowsRecently switch
        {
            > 1 => (
                _localization.GetString("Ops_WorkflowsActiveMany", FormatCompactNumber(overview.ActiveWorkflowsRecently)),
                OperationsStatusKind.WorkflowsActiveRecently),
            1 => (_localization.GetString("Ops_WorkflowsActiveOne"), OperationsStatusKind.WorkflowsActiveRecently),
            _ when enabledCount > 1 => (
                _localization.GetString("Ops_WorkflowsEnabledMany", FormatCompactNumber(enabledCount)),
                OperationsStatusKind.WorkflowsEnabled),
            _ when enabledCount == 1 => (_localization.GetString("Ops_WorkflowsEnabledOne"), OperationsStatusKind.WorkflowsEnabled),
            _ => (_localization.GetString("Ops_WorkflowsNoRecentRuns"), OperationsStatusKind.WorkflowsNoRecentRuns)
        };

        string detail;
        if (topWorkflow is not null)
        {
            detail = _localization.GetString(
                "Ops_WorkflowTop", topWorkflow.WorkflowName, FormatCompactNumber(topWorkflow.RunCount));
        }
        else
        {
            detail = enabledCount switch
            {
                > 1 => _localization.GetString("Ops_WorkflowsEnabledInBuilderMany", FormatCompactNumber(enabledCount)),
                1 => _localization.GetString("Ops_WorkflowsEnabledInBuilderOne"),
                _ => _localization.GetString("Ops_WorkflowCreateHint")
            };
        }

        return new OperationsCardSnapshot
        {
            Headline = FormatCompactNumber(overview.TotalRuns),
            Status = status,
            StatusKind = statusKind,
            SupportingPrimary = supportingPrimary,
            SupportingPrimaryKind = supportingPrimaryKind,
            SupportingSecondary = overview.AverageRunDurationMs > 0
                ? _localization.GetString("Ops_WorkflowAvgRun", FormatCompactDuration(overview.AverageRunDurationMs))
                : _localization.GetString("Ops_WorkflowAvgUnavailable"),
            Detail = detail
        };
    }

    private IReadOnlyList<OperationsWorkflowRunPreview> BuildWorkflowRunPreviews(WorkflowIntelligenceOverview overview)
    {
        var items = overview.RecentRuns
            .Take(3)
            .Select(run =>
            {
                var (status, statusKind) = NormalizeWorkflowStatus(run.Status);
                var when = FormatHelper.TimeAgoWithMonths(run.CompletedAt ?? run.StartedAt);

                return new OperationsWorkflowRunPreview
                {
                    WorkflowId = run.WorkflowId,
                    RunId = run.WorkflowRunId,
                    Title = string.IsNullOrWhiteSpace(run.WorkflowName)
                        ? _localization.GetString("Ops_RunUntitled")
                        : run.WorkflowName,
                    Status = status,
                    StatusKind = statusKind,
                    Detail = !string.IsNullOrWhiteSpace(run.PreviewText)
                        ? _localization.GetString("Ops_TextWithTime", TrimForPreview(run.PreviewText, 120), when)
                        : BuildWorkflowTimingDetail(run, when)
                };
            })
            .ToArray();

        return items.Length > 0
            ? items
            : [new OperationsWorkflowRunPreview
            {
                Title = _localization.GetString("Ops_RunsEmptyTitle"),
                Status = _localization.GetString("Ops_PlaceholderHistory"),
                StatusKind = OperationsStatusKind.Placeholder,
                Detail = _localization.GetString("Ops_RunsEmptyDetail")
            }];
    }

    private OperationsCardSnapshot BuildConnectorCard(
        IReadOnlyList<PluginEntity> plugins,
        IReadOnlyList<PluginEntity> enabledConnectors,
        int enabledPluginCount)
    {
        var enabledConnectorCount = enabledConnectors.Count;
        var connectorNames = enabledConnectors
            .Select(plugin => plugin.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        var (status, statusKind) = enabledConnectorCount switch
        {
            > 1 => (_localization.GetString("Ops_ConnectorsEnabledMany", enabledConnectorCount), OperationsStatusKind.ConnectorsEnabled),
            1 => (_localization.GetString("Ops_ConnectorsEnabledOne"), OperationsStatusKind.ConnectorsEnabled),
            _ when enabledPluginCount > 1 => (
                _localization.GetString("Ops_PluginsEnabledMany", enabledPluginCount),
                OperationsStatusKind.PluginsEnabled),
            _ when enabledPluginCount == 1 => (_localization.GetString("Ops_PluginsEnabledOne"), OperationsStatusKind.PluginsEnabled),
            _ when plugins.Count > 1 => (
                _localization.GetString("Ops_PluginsInstalledMany", plugins.Count),
                OperationsStatusKind.PluginsInstalled),
            _ when plugins.Count == 1 => (_localization.GetString("Ops_PluginsInstalledOne"), OperationsStatusKind.PluginsInstalled),
            _ => (_localization.GetString("Ops_NoPluginsInstalled"), OperationsStatusKind.NoPluginsInstalled)
        };

        return new OperationsCardSnapshot
        {
            Headline = FormatCompactNumber(enabledConnectorCount > 0
                ? enabledConnectorCount
                : enabledPluginCount > 0
                    ? enabledPluginCount
                    : plugins.Count),
            Status = status,
            StatusKind = statusKind,
            Detail = connectorNames.Count > 0
                ? string.Join(", ", connectorNames)
                : plugins.Count > 0
                    ? _localization.GetString("Ops_ConnectorsDetailOpenManager")
                    : _localization.GetString("Ops_ConnectorsDetailInstall")
        };
    }

    private IReadOnlyList<OperationsConnectorPreview> BuildConnectorPreviews(IReadOnlyList<PluginEntity> plugins)
    {
        var items = plugins
            .OrderByDescending(plugin => plugin.IsEnabled)
            .ThenByDescending(plugin => plugin.LastActivatedAt ?? plugin.InstalledAt)
            .ThenBy(plugin => plugin.Name)
            .Take(3)
            .Select(plugin =>
            {
                var isConnector = IsPluginType(plugin, PluginType.DataConnector);
                var (status, statusKind) = plugin.IsEnabled
                    ? (_localization.GetString("Ops_ConnectorEnabled"), OperationsStatusKind.ConnectorEnabled)
                    : isConnector
                        ? (_localization.GetString("Ops_ConnectorDisabled"), OperationsStatusKind.ConnectorDisabled)
                        : (_localization.GetString("Ops_PluginInstalled"), OperationsStatusKind.PluginInstalled);

                return new OperationsConnectorPreview
                {
                    PluginId = plugin.Id,
                    IsEnabled = plugin.IsEnabled,
                    CanEnableFromOperations = !plugin.IsEnabled && isConnector,
                    Title = string.IsNullOrWhiteSpace(plugin.Name)
                        ? plugin.PluginId
                        : plugin.Name,
                    Status = status,
                    StatusKind = statusKind,
                    Detail = BuildConnectorDetail(plugin)
                };
            })
            .ToArray();

        return items.Length > 0
            ? items
            : [new OperationsConnectorPreview
            {
                Title = _localization.GetString("Ops_ConnectorsEmptyTitle"),
                Status = _localization.GetString("Ops_PlaceholderPlugins"),
                StatusKind = OperationsStatusKind.Placeholder,
                Detail = _localization.GetString("Ops_ConnectorsEmptyDetail")
            }];
    }

    private string BuildConnectorDetail(PluginEntity plugin)
    {
        var pluginType = FormatPluginType(plugin.PluginType);

        if (!string.IsNullOrWhiteSpace(plugin.Description))
        {
            return _localization.GetString("Ops_ConnectorDetailDescription", pluginType, TrimForPreview(plugin.Description, 120));
        }

        return plugin.LastActivatedAt.HasValue
            ? _localization.GetString(
                "Ops_ConnectorDetailLastActive", pluginType, FormatHelper.TimeAgoWithMonths(plugin.LastActivatedAt.Value))
            : _localization.GetString("Ops_ConnectorDetailVersion", pluginType, plugin.Version);
    }

    private static bool IsPluginType(PluginEntity plugin, PluginType expectedType) =>
        string.Equals(plugin.PluginType, expectedType.ToString(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where an inbox item came from: the category or type its connector recorded, as stored, or
    /// "Pending" in the user's language when it names none.
    /// </summary>
    private (string Label, OperationsStatusKind Kind) DescribeInboxSource(InboxItemEntity item) =>
        BuildInboxSourceLabel(item) is { } source
            ? (source, OperationsStatusKind.Other)
            : (_localization.GetString("Ops_SourcePending"), OperationsStatusKind.SourcePending);

    /// <summary>The category or type the item's connector recorded, title-cased; null when it names neither.</summary>
    private static string? BuildInboxSourceLabel(InboxItemEntity item)
    {
        if (!string.IsNullOrWhiteSpace(item.SourceCategory))
        {
            return ToTitleCase(item.SourceCategory);
        }

        if (!string.IsNullOrWhiteSpace(item.SourceType))
        {
            return ToTitleCase(item.SourceType);
        }

        return null;
    }

    /// <summary>
    /// A run's status in the user's language. A status the workflow engine does not write is shown
    /// as stored and colored from its text.
    /// </summary>
    private (string Label, OperationsStatusKind Kind) NormalizeWorkflowStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return (_localization.GetString("Ops_RunRecorded"), OperationsStatusKind.RunRecorded);
        }

        return status.ToLowerInvariant() switch
        {
            "completed" or "success" => (_localization.GetString("Ops_RunCompleted"), OperationsStatusKind.RunCompleted),
            "failed" => (_localization.GetString("Ops_RunFailed"), OperationsStatusKind.RunFailed),
            "cancelled" or "canceled" => (_localization.GetString("Ops_RunCancelled"), OperationsStatusKind.RunCancelled),
            "running" => (_localization.GetString("Ops_RunRunning"), OperationsStatusKind.RunRunning),
            "pending" => (_localization.GetString("Ops_RunPending"), OperationsStatusKind.RunPending),
            _ => (ToTitleCase(status), OperationsStatusKind.Other)
        };
    }

    private string BuildWorkflowTimingDetail(WorkflowRecentRunMetric run, string when)
    {
        var duration = run.DurationMs.HasValue
            ? FormatCompactDuration(run.DurationMs.Value)
            : _localization.GetString("Ops_RunDurationUnavailable");

        return _localization.GetString("Ops_RunTiming", duration, when);
    }

    private string FormatPluginType(string pluginType)
    {
        if (string.IsNullOrWhiteSpace(pluginType))
        {
            return _localization.GetString("Ops_PluginTypePlugin");
        }

        return pluginType switch
        {
            "DataConnector" => _localization.GetString("Ops_PluginTypeConnector"),
            "WorkflowStep" => _localization.GetString("Ops_PluginTypeWorkflowStep"),
            _ => ToTitleCase(pluginType)
        };
    }

    private static string ToTitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Replace('-', ' ')
            .Replace('_', ' ');

        var builder = new StringBuilder(normalized.Length + 8);
        for (var i = 0; i < normalized.Length; i++)
        {
            var current = normalized[i];
            var hasPrevious = i > 0;
            var previous = hasPrevious ? normalized[i - 1] : '\0';
            if (hasPrevious &&
                char.IsUpper(current) &&
                !char.IsWhiteSpace(previous) &&
                !char.IsUpper(previous))
            {
                builder.Append(' ');
            }

            builder.Append(current);
        }

        return string.Join(
            ' ',
            builder.ToString()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }

    private static string TrimForPreview(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized[..Math.Max(0, maxLength - 3)].TrimEnd()}...";
    }

    private static string FormatCompactNumber(int value) => FormatCompactNumber((long)value);

    private static string FormatCompactNumber(long value) =>
        value >= 1_000_000 ? $"{value / 1_000_000.0:F1}M"
        : value >= 1_000 ? $"{value / 1_000.0:F1}K"
        : value.ToString();

    private string FormatCompactDuration(double milliseconds)
    {
        if (milliseconds >= 60_000)
        {
            return _localization.GetString(
                "Ops_DurationMinutes", (milliseconds / 60_000.0).ToString("F1", CultureInfo.CurrentCulture));
        }

        if (milliseconds >= 1_000)
        {
            return _localization.GetString(
                "Ops_DurationSeconds", (milliseconds / 1_000.0).ToString("F0", CultureInfo.CurrentCulture));
        }

        return _localization.GetString("Ops_DurationMilliseconds", milliseconds.ToString("F0", CultureInfo.CurrentCulture));
    }
}
