using AgentX.Core.Data.Entities;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Analytics;
using AgentX.Core.Services.Analytics.Models;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Plugins;
using AgentX.Core.Services.Sync;
using AgentX.Core.Services.Sync.Models;
using AgentX.Core.Services.Workflows;
using Serilog;

namespace AgentX.App.Services;

/// <summary>
/// Aggregates the app's operational signals into one dashboard-friendly snapshot.
/// </summary>
public sealed class OperationsOverviewService : IOperationsOverviewService
{
    private readonly IAnalyticsService _analyticsService;
    private readonly IInboxService _inboxService;
    private readonly IPluginService _pluginService;
    private readonly ISyncService _syncService;
    private readonly IWorkflowService _workflowService;
    private readonly ILogger _log;

    public OperationsOverviewService(
        IAnalyticsService analyticsService,
        IInboxService inboxService,
        IPluginService pluginService,
        ISyncService syncService,
        IWorkflowService workflowService,
        ILogger logger)
    {
        _analyticsService = analyticsService ?? throw new ArgumentNullException(nameof(analyticsService));
        _inboxService = inboxService ?? throw new ArgumentNullException(nameof(inboxService));
        _pluginService = pluginService ?? throw new ArgumentNullException(nameof(pluginService));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
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
        var syncConfigTask = SafeAsync(
            () => _syncService.GetConfigurationAsync(),
            null as SyncConfiguration,
            "sync configuration");
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
            syncConfigTask,
            pluginTask,
            workflowListTask);

        var conversation = await conversationTask;
        var workflow = await workflowTask;
        var pendingInbox = await inboxTask;
        var syncConfig = await syncConfigTask;
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
            Connectors = BuildConnectorCard(plugins, enabledConnectors, enabledPluginCount)
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

    private static OperationsCardSnapshot BuildConversationIntelligenceCard(ConversationIntelligenceOverview overview)
    {
        var latestSummary = overview.RecentSummaries.FirstOrDefault();
        var status = overview.PendingRefreshes switch
        {
            > 1 => $"{overview.PendingRefreshes} refreshes pending",
            1 => "1 refresh pending",
            _ when overview.StaleConversations > 1 => $"{overview.StaleConversations} stale summaries",
            _ when overview.StaleConversations == 1 => "1 stale summary",
            _ when overview.SummarizedConversations > 0 => "Durable recall current",
            _ => "Durable recall inactive"
        };

        var detail = latestSummary is null
            ? "Open Analytics to inspect summary coverage and recent snapshots."
            : $"{FormatCompactNumber(overview.CurrentSnapshots)} stored snapshots · latest {FormatHelper.TimeAgoWithMonths(latestSummary.GeneratedAt)}";

        return new OperationsCardSnapshot
        {
            Headline = FormatCompactNumber(overview.SummarizedConversations),
            Status = status,
            Detail = detail
        };
    }

    private static OperationsCardSnapshot BuildSyncHealthCard(SyncConfiguration? config, SyncStatus status)
    {
        if (config is null || string.IsNullOrWhiteSpace(config.SyncFolderPath))
        {
            return new OperationsCardSnapshot
            {
                Headline = "Not configured",
                Status = "Collaborative sync is off",
                Detail = "Configure a shared folder to keep multiple installations aligned."
            };
        }

        var headline = status.SyncState switch
        {
            SyncState.Syncing => "Syncing now",
            SyncState.Conflict => "Conflict detected",
            SyncState.Error => "Needs attention",
            _ when status.LastSyncAt.HasValue => FormatHelper.TimeAgoWithMonths(status.LastSyncAt.Value),
            _ => "Configured"
        };

        var syncStatus = status.SyncState switch
        {
            SyncState.Syncing => "Exchange in progress",
            SyncState.Conflict => "Resolve sync conflicts",
            SyncState.Error => "Review sync health",
            _ when status.PendingChanges > 1 => $"{status.PendingChanges} local changes pending",
            _ when status.PendingChanges == 1 => "1 local change pending",
            _ => "Standing by"
        };

        var detail = !string.IsNullOrWhiteSpace(status.ErrorMessage)
            ? status.ErrorMessage
            : config.SyncScope == SyncScope.SelectedCollections
                ? "Scoped to selected collections."
                : "Syncing the full workspace.";

        return new OperationsCardSnapshot
        {
            Headline = headline,
            Status = syncStatus,
            Detail = detail
        };
    }

    private static OperationsCardSnapshot BuildIngestionBacklogCard(int pendingCount, int enabledConnectorCount)
    {
        var detail = pendingCount > 0
            ? "Open Smart Inbox to triage connector and watch-folder imports."
            : enabledConnectorCount > 0
                ? "Connector and watch-folder imports will surface here."
                : "Watch folders and enabled connectors will surface new items here.";

        return new OperationsCardSnapshot
        {
            Headline = FormatCompactNumber(pendingCount),
            Status = pendingCount switch
            {
                > 1 => $"{pendingCount} items awaiting triage",
                1 => "1 item awaiting triage",
                _ => "Queue clear"
            },
            Detail = detail
        };
    }

    private static OperationsCardSnapshot BuildWorkflowCard(
        WorkflowIntelligenceOverview overview,
        IReadOnlyList<WorkflowEntity> workflows)
    {
        var enabledCount = workflows.Count(workflow => workflow.IsEnabled);
        var topWorkflow = overview.TopWorkflows.FirstOrDefault();
        var outcomeRuns = overview.SuccessfulRuns + overview.FailedOrCancelledRuns;

        return new OperationsCardSnapshot
        {
            Headline = FormatCompactNumber(overview.TotalRuns),
            Status = overview.TotalRuns switch
            {
                > 0 when outcomeRuns > 0 => $"{overview.SuccessRate:F0}% success rate",
                > 1 => $"{FormatCompactNumber(overview.TotalRuns)} runs recorded",
                1 => "1 run recorded",
                _ => "Ready to automate"
            },
            SupportingPrimary = overview.ActiveWorkflowsRecently switch
            {
                > 1 => $"{FormatCompactNumber(overview.ActiveWorkflowsRecently)} active / 30d",
                1 => "1 active / 30d",
                _ when enabledCount > 1 => $"{FormatCompactNumber(enabledCount)} enabled",
                _ when enabledCount == 1 => "1 enabled",
                _ => "No recent runs"
            },
            SupportingSecondary = overview.AverageRunDurationMs > 0
                ? $"{FormatCompactDuration(overview.AverageRunDurationMs)} avg run"
                : "Avg duration unavailable",
            Detail = topWorkflow is not null
                ? $"Top workflow: {topWorkflow.WorkflowName} · {FormatCompactNumber(topWorkflow.RunCount)} runs"
                : enabledCount switch
                {
                    > 1 => $"{FormatCompactNumber(enabledCount)} workflows enabled in the builder.",
                    1 => "1 workflow enabled in the builder.",
                    _ => "Create or launch a workflow from Vault or Search to start automating multi-step tasks."
                }
        };
    }

    private static OperationsCardSnapshot BuildConnectorCard(
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

        return new OperationsCardSnapshot
        {
            Headline = FormatCompactNumber(enabledConnectorCount > 0
                ? enabledConnectorCount
                : enabledPluginCount > 0
                    ? enabledPluginCount
                    : plugins.Count),
            Status = enabledConnectorCount switch
            {
                > 1 => $"{enabledConnectorCount} connectors enabled",
                1 => "1 connector enabled",
                _ when enabledPluginCount > 1 => $"{enabledPluginCount} plugins enabled",
                _ when enabledPluginCount == 1 => "1 plugin enabled",
                _ when plugins.Count > 1 => $"{plugins.Count} plugins installed",
                _ when plugins.Count == 1 => "1 plugin installed",
                _ => "No plugins installed"
            },
            Detail = connectorNames.Count > 0
                ? string.Join(" · ", connectorNames)
                : plugins.Count > 0
                    ? "Open Plugin Manager to enable connectors and extensions."
                    : "Install or enable plugins to bring external data and workflow extensions into the app."
        };
    }

    private static bool IsPluginType(PluginEntity plugin, PluginType expectedType) =>
        string.Equals(plugin.PluginType, expectedType.ToString(), StringComparison.OrdinalIgnoreCase);

    private static string FormatCompactNumber(int value) => FormatCompactNumber((long)value);

    private static string FormatCompactNumber(long value) =>
        value >= 1_000_000 ? $"{value / 1_000_000.0:F1}M"
        : value >= 1_000 ? $"{value / 1_000.0:F1}K"
        : value.ToString();

    private static string FormatCompactDuration(double milliseconds)
    {
        if (milliseconds >= 60_000)
        {
            return $"{milliseconds / 60_000.0:F1} min";
        }

        if (milliseconds >= 1_000)
        {
            return $"{milliseconds / 1_000.0:F0}s";
        }

        return $"{milliseconds:F0} ms";
    }
}
