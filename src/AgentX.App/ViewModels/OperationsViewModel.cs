using System.Globalization;
using AgentX.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class OperationsViewModel : ObservableObject, IDisposable
{
    private readonly IOperationsActionService _operationsActionService;
    private readonly IOperationsDrillInService _operationsDrillInService;
    private readonly IOperationsOverviewService _operationsOverviewService;
    private readonly ILogger _log;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isEnablingConnector;
    [ObservableProperty] private bool _isGeneratingInboxPreviews;
    [ObservableProperty] private bool _isReindexingImportedDocument;
    [ObservableProperty] private bool _isRefreshingConversationSummaries;
    [ObservableProperty] private bool _isRunningManualSync;
    [ObservableProperty] private bool _hasActionMessage;
    [ObservableProperty] private string _actionMessage = string.Empty;
    [ObservableProperty] private bool _hasActionError;
    [ObservableProperty] private string _actionErrorMessage = string.Empty;

    [ObservableProperty] private string _summaryHeadline = "Operations ready";
    [ObservableProperty] private string _summaryDetail = "Unified status for conversation intelligence, sync posture, ingestion backlog, workflows, and connectors.";
    [ObservableProperty]
    private IReadOnlyList<OperationsOverviewStatusTile> _overviewStatusTiles =
        BuildOverviewStatusTiles(CreateFallbackSnapshot());
    [ObservableProperty] private IReadOnlyList<OperationsRecommendedActionItem> _recommendedActions = Array.Empty<OperationsRecommendedActionItem>();

    [ObservableProperty] private OperationsCardSnapshot _conversationIntelligence = CreateDefaultConversationCard();
    [ObservableProperty] private OperationsCardSnapshot _syncHealth = CreateDefaultSyncCard();
    [ObservableProperty] private OperationsCardSnapshot _ingestionBacklog = CreateDefaultBacklogCard();
    [ObservableProperty] private OperationsCardSnapshot _workflowActivity = CreateDefaultWorkflowCard();
    [ObservableProperty] private OperationsCardSnapshot _connectors = CreateDefaultConnectorsCard();
    [ObservableProperty] private IReadOnlyList<OperationsConversationPreview> _recentConversationSummaries = Array.Empty<OperationsConversationPreview>();
    [ObservableProperty] private IReadOnlyList<OperationsSyncPreview> _recentSyncPasses = Array.Empty<OperationsSyncPreview>();
    [ObservableProperty] private IReadOnlyList<OperationsInboxPreview> _pendingInboxItems = Array.Empty<OperationsInboxPreview>();
    [ObservableProperty] private IReadOnlyList<OperationsImportedDocumentPreview> _recentImportedDocuments = Array.Empty<OperationsImportedDocumentPreview>();
    [ObservableProperty] private IReadOnlyList<OperationsWorkflowRunPreview> _recentWorkflowRuns = Array.Empty<OperationsWorkflowRunPreview>();
    [ObservableProperty] private IReadOnlyList<OperationsConnectorPreview> _connectorPreviews = Array.Empty<OperationsConnectorPreview>();
    public bool HasRecommendedActions => RecommendedActions.Count > 0;

    public NavigateHandler? NavigateRequested { get; set; }

    public OperationsViewModel(
        IOperationsActionService operationsActionService,
        IOperationsDrillInService operationsDrillInService,
        IOperationsOverviewService operationsOverviewService,
        ILogger logger)
    {
        _operationsActionService = operationsActionService ?? throw new ArgumentNullException(nameof(operationsActionService));
        _operationsDrillInService = operationsDrillInService ?? throw new ArgumentNullException(nameof(operationsDrillInService));
        _operationsOverviewService = operationsOverviewService ?? throw new ArgumentNullException(nameof(operationsOverviewService));
        _log = logger?.ForContext<OperationsViewModel>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            var snapshot = await _operationsOverviewService.GetSnapshotAsync(ct);
            ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Operations page failed to load snapshot");
            HasError = true;
            ErrorMessage = "Failed to load the operations overview. Open individual surfaces for details or try refreshing.";
            ApplySnapshot(CreateFallbackSnapshot());
            SummaryHeadline = "Operations unavailable";
            SummaryDetail = "Snapshot loading failed, but the individual operations surfaces are still available.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplySnapshot(OperationsOverviewSnapshot snapshot)
    {
        ConversationIntelligence = snapshot.ConversationIntelligence;
        SyncHealth = snapshot.SyncHealth;
        IngestionBacklog = snapshot.IngestionBacklog;
        WorkflowActivity = snapshot.WorkflowActivity;
        Connectors = snapshot.Connectors;
        RecentConversationSummaries = snapshot.RecentConversationSummaries;
        RecentSyncPasses = snapshot.RecentSyncPasses;
        PendingInboxItems = snapshot.PendingInboxItems;
        RecentImportedDocuments = snapshot.RecentImportedDocuments;
        RecentWorkflowRuns = snapshot.RecentWorkflowRuns;
        ConnectorPreviews = snapshot.ConnectorPreviews;
        OverviewStatusTiles = BuildOverviewStatusTiles(snapshot);
        RecommendedActions = BuildRecommendedActions(snapshot);

        var attentionAreas = CountAttentionAreas(snapshot);
        SummaryHeadline = attentionAreas switch
        {
            > 1 => $"{attentionAreas} operational areas need attention",
            1 => "1 operational area needs attention",
            _ => "Operations running normally"
        };

        SummaryDetail = attentionAreas > 0
            ? BuildAttentionSummary(snapshot)
            : $"{snapshot.ConversationIntelligence.Status} · {snapshot.SyncHealth.Status} · {snapshot.WorkflowActivity.Status}";
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    private bool CanGenerateInboxPreviews() =>
        !IsLoading &&
        !IsGeneratingInboxPreviews &&
        !IngestionBacklog.Headline.Equals("0", StringComparison.OrdinalIgnoreCase);

    private bool CanEnableConnector(OperationsConnectorPreview? preview) =>
        !IsLoading &&
        !IsEnablingConnector &&
        preview is { PluginId: > 0, CanEnableFromOperations: true };

    private bool CanRefreshConversationSummaries() =>
        !IsLoading && !IsRefreshingConversationSummaries;

    private bool CanRetryImportedDocumentIndexing(OperationsImportedDocumentPreview? preview) =>
        !IsLoading &&
        !IsReindexingImportedDocument &&
        preview is { CanRetryIndexingFromOperations: true };

    private bool CanRunManualSync() =>
        !IsLoading &&
        !IsRunningManualSync &&
        SyncHealth.StatusKind != OperationsStatusKind.SyncNotConfigured;

    [RelayCommand(CanExecute = nameof(CanRefreshConversationSummaries))]
    private async Task RefreshConversationSummariesAsync(CancellationToken ct = default)
    {
        IsRefreshingConversationSummaries = true;

        try
        {
            await RunActionAsync(
                "Refreshing conversation summaries",
                token => _operationsActionService.RefreshConversationSummariesAsync(ct: token),
                ct);
        }
        finally
        {
            IsRefreshingConversationSummaries = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGenerateInboxPreviews))]
    private async Task GenerateInboxPreviewsAsync(CancellationToken ct = default)
    {
        IsGeneratingInboxPreviews = true;

        try
        {
            await RunActionAsync(
                "Generating inbox previews",
                _operationsActionService.GenerateInboxPreviewsAsync,
                ct);
        }
        finally
        {
            IsGeneratingInboxPreviews = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEnableConnector))]
    private async Task EnableConnectorAsync(OperationsConnectorPreview? preview, CancellationToken ct = default)
    {
        if (preview is null || preview.PluginId <= 0)
        {
            return;
        }

        IsEnablingConnector = true;

        try
        {
            await RunActionAsync(
                "Enabling the connector",
                token => _operationsActionService.EnableConnectorAsync(preview.PluginId, token),
                ct);
        }
        finally
        {
            IsEnablingConnector = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRetryImportedDocumentIndexing))]
    private async Task RetryImportedDocumentIndexingAsync(OperationsImportedDocumentPreview? preview, CancellationToken ct = default)
    {
        if (preview is null || preview.DocumentId <= 0)
        {
            return;
        }

        IsReindexingImportedDocument = true;

        try
        {
            await RunActionAsync(
                "Re-indexing the document",
                token => _operationsActionService.ReindexImportedDocumentAsync(preview.DocumentId, token),
                ct);
        }
        finally
        {
            IsReindexingImportedDocument = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunManualSync))]
    private async Task RunManualSyncAsync(CancellationToken ct = default)
    {
        IsRunningManualSync = true;

        try
        {
            await RunActionAsync(
                "Sync",
                _operationsActionService.RunManualSyncAsync,
                ct);
        }
        finally
        {
            IsRunningManualSync = false;
        }
    }

    [RelayCommand]
    private void NavigateToDashboard() => NavigateRequested?.Invoke("Dashboard");

    [RelayCommand]
    private void NavigateToAnalytics() => NavigateRequested?.Invoke("Analytics");

    [RelayCommand]
    private void NavigateToSyncSettings() => NavigateRequested?.Invoke("SyncSettings");

    [RelayCommand]
    private void NavigateToInbox() => NavigateRequested?.Invoke("Inbox");

    [RelayCommand]
    private void NavigateToKnowledgeVault() => NavigateRequested?.Invoke("KnowledgeVault");

    [RelayCommand]
    private void NavigateToWorkflows() => NavigateRequested?.Invoke("Workflows");

    [RelayCommand]
    private void NavigateToPluginManager() => NavigateRequested?.Invoke("PluginManager");

    [RelayCommand]
    private void OpenOverviewStatusTile(OperationsOverviewStatusTile? tile)
    {
        if (tile is null || string.IsNullOrWhiteSpace(tile.Route))
        {
            return;
        }

        NavigateRequested?.Invoke(tile.Route);
    }

    [RelayCommand]
    private async Task ExecuteRecommendedActionAsync(OperationsRecommendedActionItem? action, CancellationToken ct = default)
    {
        if (action is null)
        {
            return;
        }

        switch (action.Kind)
        {
            case OperationsRecommendedActionKind.RefreshConversationSummaries:
                await RefreshConversationSummariesAsync(ct);
                break;

            case OperationsRecommendedActionKind.RunManualSync:
                await RunManualSyncAsync(ct);
                break;

            case OperationsRecommendedActionKind.GenerateInboxPreviews:
                await GenerateInboxPreviewsAsync(ct);
                break;

            case OperationsRecommendedActionKind.RetryImportedDocumentIndexing:
                {
                    var preview = RecentImportedDocuments.FirstOrDefault(item => item.DocumentId == action.TargetId);
                    if (preview is not null && CanRetryImportedDocumentIndexing(preview))
                    {
                        await RetryImportedDocumentIndexingAsync(preview, ct);
                        break;
                    }

                    NavigateToRecommendedAction(action);

                    break;
                }

            case OperationsRecommendedActionKind.EnableConnector:
                {
                    var preview = ConnectorPreviews.FirstOrDefault(item => item.PluginId == action.TargetId);
                    if (preview is not null && CanEnableConnector(preview))
                    {
                        await EnableConnectorAsync(preview, ct);
                        break;
                    }

                    NavigateToRecommendedAction(action);

                    break;
                }

            case OperationsRecommendedActionKind.Navigate:
            default:
                NavigateToRecommendedAction(action);

                break;
        }
    }

    [RelayCommand]
    private void OpenConversationPreview(OperationsConversationPreview? preview)
    {
        if (preview is null || preview.ConversationId <= 0)
        {
            NavigateRequested?.Invoke("Analytics");
            return;
        }

        _operationsDrillInService.StageConversationRequest(
            new OperationsConversationDrillInRequest(
                preview.ConversationId,
                $"Opened conversation summary \"{preview.Title}\" from Operations"));
        NavigateRequested?.Invoke("Analytics");
    }

    [RelayCommand]
    private void OpenInboxPreview(OperationsInboxPreview? preview)
    {
        if (preview is null || preview.ItemId <= 0)
        {
            NavigateRequested?.Invoke("Inbox");
            return;
        }

        _operationsDrillInService.StageInboxRequest(
            new OperationsInboxDrillInRequest(
                preview.ItemId,
                $"Opened inbox item \"{preview.Title}\" from Operations"));
        NavigateRequested?.Invoke("Inbox");
    }

    [RelayCommand]
    private void OpenImportedDocumentPreview(OperationsImportedDocumentPreview? preview)
    {
        if (preview is null || preview.DocumentId <= 0)
        {
            NavigateRequested?.Invoke("KnowledgeVault");
            return;
        }

        _operationsDrillInService.StageDocumentRequest(
            new OperationsDocumentDrillInRequest(
                preview.DocumentId,
                $"Opened imported document \"{preview.Title}\" from Operations"));
        NavigateRequested?.Invoke("KnowledgeVault");
    }

    [RelayCommand]
    private void OpenWorkflowRunPreview(OperationsWorkflowRunPreview? preview)
    {
        if (preview is null || preview.WorkflowId <= 0 || preview.RunId <= 0)
        {
            NavigateRequested?.Invoke("Workflows");
            return;
        }

        _operationsDrillInService.StageWorkflowRunRequest(
            new OperationsWorkflowRunDrillInRequest(
                preview.WorkflowId,
                preview.RunId,
                $"Opened stored workflow run for \"{preview.Title}\" from Operations"));
        NavigateRequested?.Invoke("Workflows");
    }

    [RelayCommand]
    private void OpenSyncPreview(OperationsSyncPreview? preview)
    {
        if (preview is null || preview.SyncLogId <= 0)
        {
            NavigateRequested?.Invoke("SyncSettings");
            return;
        }

        _operationsDrillInService.StageSyncRequest(
            new OperationsSyncDrillInRequest(
                preview.SyncLogId,
                $"Opened sync history entry \"{preview.Title}\" from Operations"));
        NavigateRequested?.Invoke("SyncSettings");
    }

    [RelayCommand]
    private void OpenConnectorPreview(OperationsConnectorPreview? preview)
    {
        if (preview is null || preview.PluginId <= 0)
        {
            NavigateRequested?.Invoke("PluginManager");
            return;
        }

        _operationsDrillInService.StagePluginRequest(
            new OperationsPluginDrillInRequest(
                preview.PluginId,
                $"Opened connector \"{preview.Title}\" from Operations"));
        NavigateRequested?.Invoke("PluginManager");
    }

    partial void OnIsLoadingChanged(bool value)
    {
        EnableConnectorCommand.NotifyCanExecuteChanged();
        GenerateInboxPreviewsCommand.NotifyCanExecuteChanged();
        RetryImportedDocumentIndexingCommand.NotifyCanExecuteChanged();
        RefreshConversationSummariesCommand.NotifyCanExecuteChanged();
        RunManualSyncCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsEnablingConnectorChanged(bool value) =>
        EnableConnectorCommand.NotifyCanExecuteChanged();

    partial void OnIsGeneratingInboxPreviewsChanged(bool value) =>
        GenerateInboxPreviewsCommand.NotifyCanExecuteChanged();

    partial void OnIsReindexingImportedDocumentChanged(bool value) =>
        RetryImportedDocumentIndexingCommand.NotifyCanExecuteChanged();

    partial void OnIsRefreshingConversationSummariesChanged(bool value) =>
        RefreshConversationSummariesCommand.NotifyCanExecuteChanged();

    partial void OnIsRunningManualSyncChanged(bool value) =>
        RunManualSyncCommand.NotifyCanExecuteChanged();

    partial void OnIngestionBacklogChanged(OperationsCardSnapshot value) =>
        GenerateInboxPreviewsCommand.NotifyCanExecuteChanged();

    partial void OnConnectorPreviewsChanged(IReadOnlyList<OperationsConnectorPreview> value) =>
        EnableConnectorCommand.NotifyCanExecuteChanged();

    partial void OnRecentImportedDocumentsChanged(IReadOnlyList<OperationsImportedDocumentPreview> value) =>
        RetryImportedDocumentIndexingCommand.NotifyCanExecuteChanged();

    partial void OnSyncHealthChanged(OperationsCardSnapshot value) =>
        RunManualSyncCommand.NotifyCanExecuteChanged();

    partial void OnRecommendedActionsChanged(IReadOnlyList<OperationsRecommendedActionItem> value) =>
        OnPropertyChanged(nameof(HasRecommendedActions));

    public void Dispose()
    {
        _log.Debug("OperationsViewModel disposed");
    }

    /// <summary>
    /// Runs one operations action and shows its outcome, then reloads the overview. The awaits
    /// stay on the UI context because the feedback and overview properties are bound; an
    /// exception becomes an error message instead of an unobserved command failure.
    /// </summary>
    private async Task RunActionAsync(
        string actionName,
        Func<CancellationToken, Task<OperationsActionResult>> action,
        CancellationToken ct)
    {
        ClearActionFeedback();

        try
        {
            var result = await action(ct);
            ApplyActionFeedback(result);
            await LoadAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelled by the caller; there is no outcome to report.
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Operations action failed: {Action}", actionName);
            ApplyActionFeedback(new OperationsActionResult(false, $"{actionName} failed: {ex.Message}"));
        }
    }

    private void ApplyActionFeedback(OperationsActionResult result)
    {
        if (result.IsSuccess)
        {
            HasActionMessage = true;
            ActionMessage = result.Message;
            HasActionError = false;
            ActionErrorMessage = string.Empty;
            return;
        }

        HasActionError = true;
        ActionErrorMessage = result.Message;
        HasActionMessage = false;
        ActionMessage = string.Empty;
    }

    private void ClearActionFeedback()
    {
        HasActionMessage = false;
        ActionMessage = string.Empty;
        HasActionError = false;
        ActionErrorMessage = string.Empty;
    }

    private void NavigateToRecommendedAction(OperationsRecommendedActionItem action)
    {
        if (string.IsNullOrWhiteSpace(action.Route))
        {
            return;
        }

        StageRecommendedActionDrillIn(action);
        NavigateRequested?.Invoke(action.Route);
    }

    private static OperationsOverviewSnapshot CreateFallbackSnapshot() => new()
    {
        ConversationIntelligence = CreateDefaultConversationCard(),
        SyncHealth = CreateDefaultSyncCard(),
        IngestionBacklog = CreateDefaultBacklogCard(),
        WorkflowActivity = CreateDefaultWorkflowCard(),
        Connectors = CreateDefaultConnectorsCard()
    };

    // The defaults carry the kinds their English text stands for, so the checks below, which
    // read the kinds, treat them as before.
    private static OperationsCardSnapshot CreateDefaultConversationCard() => new()
    {
        Headline = "0",
        Status = "Durable recall inactive",
        StatusKind = OperationsStatusKind.RecallInactive,
        Detail = "Open Analytics to inspect summary coverage and durable recall detail."
    };

    private static OperationsCardSnapshot CreateDefaultSyncCard() => new()
    {
        Headline = "Not configured",
        Status = "Collaborative sync is off",
        StatusKind = OperationsStatusKind.SyncNotConfigured,
        Detail = "Configure a shared folder to keep multiple installations aligned."
    };

    private static OperationsCardSnapshot CreateDefaultBacklogCard() => new()
    {
        Headline = "0",
        Status = "Queue clear",
        StatusKind = OperationsStatusKind.BacklogClear,
        Detail = "Watch folders and enabled connectors will surface new items here."
    };

    private static OperationsCardSnapshot CreateDefaultWorkflowCard() => new()
    {
        Headline = "0",
        Status = "Ready to automate",
        StatusKind = OperationsStatusKind.WorkflowReadyToAutomate,
        SupportingPrimary = "No recent runs",
        SupportingPrimaryKind = OperationsStatusKind.WorkflowsNoRecentRuns,
        SupportingSecondary = "Avg duration unavailable",
        Detail = "Create or launch a workflow from Vault or Search to start automating multi-step tasks."
    };

    private static OperationsCardSnapshot CreateDefaultConnectorsCard() => new()
    {
        Headline = "0",
        Status = "No plugins installed",
        StatusKind = OperationsStatusKind.NoPluginsInstalled,
        Detail = "Install or enable plugins to bring external data and workflow extensions into the app."
    };

    private static IReadOnlyList<OperationsOverviewStatusTile> BuildOverviewStatusTiles(OperationsOverviewSnapshot snapshot) =>
    [
        new OperationsOverviewStatusTile(
            "Conversation",
            snapshot.ConversationIntelligence.Headline,
            snapshot.ConversationIntelligence.Status,
            "Analytics",
            "Open Analytics") { StatusToneToken = snapshot.ConversationIntelligence.StatusToneToken },
        new OperationsOverviewStatusTile(
            "Sync",
            snapshot.SyncHealth.Headline,
            snapshot.SyncHealth.Status,
            "SyncSettings",
            "Open Sync") { StatusToneToken = snapshot.SyncHealth.StatusToneToken },
        new OperationsOverviewStatusTile(
            "Backlog",
            snapshot.IngestionBacklog.Headline,
            snapshot.IngestionBacklog.Status,
            "Inbox",
            "Open Inbox") { StatusToneToken = snapshot.IngestionBacklog.StatusToneToken },
        new OperationsOverviewStatusTile(
            "Workflows",
            snapshot.WorkflowActivity.Headline,
            snapshot.WorkflowActivity.Status,
            "Workflows",
            "Open Workflows") { StatusToneToken = snapshot.WorkflowActivity.StatusToneToken },
        new OperationsOverviewStatusTile(
            "Connectors",
            snapshot.Connectors.Headline,
            snapshot.Connectors.Status,
            "PluginManager",
            "Open Plugins") { StatusToneToken = snapshot.Connectors.StatusToneToken }
    ];

    private static IReadOnlyList<OperationsRecommendedActionItem> BuildRecommendedActions(OperationsOverviewSnapshot snapshot)
    {
        var items = new List<OperationsRecommendedActionItem>();

        var documentNeedingAttention = snapshot.RecentImportedDocuments.FirstOrDefault(preview =>
            preview.DocumentId > 0 &&
            preview.Health == OperationsDocumentHealth.NeedsAttention);
        var connectorToEnable = snapshot.ConnectorPreviews.FirstOrDefault(preview => preview.CanEnableFromOperations);
        var failedWorkflowRun = snapshot.RecentWorkflowRuns.FirstOrDefault(preview =>
            preview.RunId > 0 &&
            preview.NeedsReview);

        if (NeedsConversationAttention(snapshot.ConversationIntelligence))
        {
            items.Add(new OperationsRecommendedActionItem(
                "Memory",
                "\uE9D2",
                "Refresh durable recall coverage",
                "Conversation summaries are stale or incomplete. Refresh them so the rest of the app sees the latest memory state.",
                snapshot.ConversationIntelligence.Status,
                "Refresh Summaries",
                "Analytics",
                OperationsRecommendedActionKind.RefreshConversationSummaries)
            {
                StatusToneToken = snapshot.ConversationIntelligence.StatusToneToken
            });
        }

        if (NeedsSyncAttention(snapshot.SyncHealth))
        {
            var requiresSetup = snapshot.SyncHealth.StatusKind == OperationsStatusKind.SyncNotConfigured;

            items.Add(new OperationsRecommendedActionItem(
                requiresSetup ? "Setup" : "Sync",
                "\uE895",
                requiresSetup ? "Configure collaborative sync" : "Run a manual sync pass",
                requiresSetup
                    ? "Sync is not fully configured yet. Finish setup so multiple Agent-X installations can stay aligned."
                    : "Sync needs a manual nudge to bring the workspace back into a clean state.",
                snapshot.SyncHealth.Status,
                requiresSetup ? "Open Sync" : "Run Sync Now",
                "SyncSettings",
                requiresSetup
                    ? OperationsRecommendedActionKind.Navigate
                    : OperationsRecommendedActionKind.RunManualSync)
            {
                StatusToneToken = snapshot.SyncHealth.StatusToneToken
            });
        }

        if (ParseCompactNumber(snapshot.IngestionBacklog.Headline) > 0)
        {
            items.Add(new OperationsRecommendedActionItem(
                "Backlog",
                "\uE8B7",
                "Generate AI previews for intake",
                "Turn the current backlog into faster triage decisions by generating previews for the pending inbox items.",
                snapshot.IngestionBacklog.Status,
                "Generate Previews",
                "Inbox",
                OperationsRecommendedActionKind.GenerateInboxPreviews)
            {
                StatusToneToken = snapshot.IngestionBacklog.StatusToneToken
            });
        }

        if (documentNeedingAttention is not null)
        {
            items.Add(new OperationsRecommendedActionItem(
                "Indexing",
                "\uE8B1",
                $"Retry indexing {documentNeedingAttention.Title}",
                "A recently imported document still needs attention before it becomes reliably searchable and reusable elsewhere in the app.",
                documentNeedingAttention.HealthStatus,
                "Retry Index",
                "KnowledgeVault",
                OperationsRecommendedActionKind.RetryImportedDocumentIndexing,
                documentNeedingAttention.DocumentId)
            {
                StatusToneToken = documentNeedingAttention.HealthToneToken
            });
        }

        if (connectorToEnable is not null)
        {
            items.Add(new OperationsRecommendedActionItem(
                "Connector",
                "\uE943",
                $"Enable {connectorToEnable.Title}",
                "Bring the connector online so new external content can start flowing into triage, search, and workflow surfaces.",
                connectorToEnable.Status,
                "Enable Connector",
                "PluginManager",
                OperationsRecommendedActionKind.EnableConnector,
                connectorToEnable.PluginId)
            {
                StatusToneToken = connectorToEnable.StatusToneToken
            });
        }
        else if (snapshot.Connectors.Headline.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                 snapshot.Connectors.StatusKind == OperationsStatusKind.NoPluginsInstalled)
        {
            var noStatus = string.IsNullOrWhiteSpace(snapshot.Connectors.Status);
            items.Add(new OperationsRecommendedActionItem(
                "Expansion",
                "\uE943",
                "Connect a live source",
                "Bring in fresh external content so the rest of the workspace has more real intake to triage, search, and automate.",
                noStatus ? "No connectors enabled" : snapshot.Connectors.Status,
                "Open Plugins",
                "PluginManager",
                OperationsRecommendedActionKind.Navigate)
            {
                StatusToneToken = noStatus ? "No connectors enabled" : snapshot.Connectors.StatusToneToken
            });
        }

        if (failedWorkflowRun is not null)
        {
            items.Add(new OperationsRecommendedActionItem(
                "Workflow",
                "\uE8C7",
                $"Review {failedWorkflowRun.Title}",
                "A recent workflow run needs review before the automation layer is fully healthy again.",
                failedWorkflowRun.Status,
                "Open Workflows",
                "Workflows",
                OperationsRecommendedActionKind.Navigate,
                failedWorkflowRun.WorkflowId,
                failedWorkflowRun.RunId)
            {
                StatusToneToken = failedWorkflowRun.StatusToneToken
            });
        }

        if (items.Count == 0)
        {
            items.Add(new OperationsRecommendedActionItem(
                "Review",
                "\uE9D2",
                "Inspect durable recall coverage",
                "Use Analytics to keep an eye on recall freshness, conversation themes, and workflow intelligence trends.",
                snapshot.ConversationIntelligence.Status,
                "Open Analytics",
                "Analytics",
                OperationsRecommendedActionKind.Navigate)
            {
                StatusToneToken = snapshot.ConversationIntelligence.StatusToneToken
            });
            items.Add(new OperationsRecommendedActionItem(
                "Review",
                "\uE895",
                "Review sync posture",
                "Open Collaborative Sync to verify history, scope, and any local changes pending synchronization.",
                snapshot.SyncHealth.Status,
                "Open Sync",
                "SyncSettings",
                OperationsRecommendedActionKind.Navigate)
            {
                StatusToneToken = snapshot.SyncHealth.StatusToneToken
            });
            items.Add(new OperationsRecommendedActionItem(
                "Review",
                "\uE8C7",
                "Review workflow momentum",
                "Open Workflows to inspect recent runs, tune templates, and keep automation close to real work.",
                snapshot.WorkflowActivity.Status,
                "Open Workflows",
                "Workflows",
                OperationsRecommendedActionKind.Navigate)
            {
                StatusToneToken = snapshot.WorkflowActivity.StatusToneToken
            });
        }

        return items.Take(4).ToArray();
    }

    private static int CountAttentionAreas(OperationsOverviewSnapshot snapshot)
    {
        var count = 0;

        if (NeedsConversationAttention(snapshot.ConversationIntelligence))
        {
            count++;
        }

        if (NeedsSyncAttention(snapshot.SyncHealth))
        {
            count++;
        }

        if (ParseCompactNumber(snapshot.IngestionBacklog.Headline) > 0)
        {
            count++;
        }

        if (NeedsImportedDocumentAttention(snapshot.RecentImportedDocuments))
        {
            count++;
        }

        if (NeedsConnectorAttention(snapshot.ConnectorPreviews))
        {
            count++;
        }

        if (NeedsWorkflowRunAttention(snapshot.RecentWorkflowRuns))
        {
            count++;
        }

        return count;
    }

    // The status texts are in the user's language, so these read the snapshot's kinds.
    private static bool NeedsConversationAttention(OperationsCardSnapshot card) =>
        card.StatusKind is OperationsStatusKind.RecallRefreshesPending or OperationsStatusKind.RecallStaleSummaries;

    private static bool NeedsSyncAttention(OperationsCardSnapshot card) =>
        card.StatusKind is OperationsStatusKind.SyncNotConfigured or OperationsStatusKind.SyncConflict;

    private static bool NeedsImportedDocumentAttention(IReadOnlyList<OperationsImportedDocumentPreview> previews) =>
        previews.Any(preview => preview.DocumentId > 0 &&
                                preview.Health == OperationsDocumentHealth.NeedsAttention);

    private static bool NeedsConnectorAttention(IReadOnlyList<OperationsConnectorPreview> previews) =>
        previews.Any(preview => preview.CanEnableFromOperations);

    private static bool NeedsWorkflowRunAttention(IReadOnlyList<OperationsWorkflowRunPreview> previews) =>
        previews.Any(preview => preview.RunId > 0 && preview.NeedsReview);

    private void StageRecommendedActionDrillIn(OperationsRecommendedActionItem action)
    {
        var sourceLabel = $"Opened Operations recommendation \"{action.Title}\"";
        switch (action.Route)
        {
            case "Inbox" when action.TargetId > 0:
                _operationsDrillInService.StageInboxRequest(
                    new OperationsInboxDrillInRequest(action.TargetId, sourceLabel));
                break;

            case "KnowledgeVault" when action.TargetId > 0:
                _operationsDrillInService.StageDocumentRequest(
                    new OperationsDocumentDrillInRequest(action.TargetId, sourceLabel));
                break;

            case "PluginManager" when action.TargetId > 0:
                _operationsDrillInService.StagePluginRequest(
                    new OperationsPluginDrillInRequest(action.TargetId, sourceLabel));
                break;

            case "Workflows" when action.TargetId > 0 && action.SecondaryTargetId > 0:
                _operationsDrillInService.StageWorkflowRunRequest(
                    new OperationsWorkflowRunDrillInRequest(action.TargetId, action.SecondaryTargetId, sourceLabel));
                break;
        }
    }

    private static string BuildAttentionSummary(OperationsOverviewSnapshot snapshot)
    {
        var items = new List<string>();

        if (NeedsConversationAttention(snapshot.ConversationIntelligence))
        {
            items.Add(snapshot.ConversationIntelligence.Status);
        }

        if (NeedsSyncAttention(snapshot.SyncHealth))
        {
            items.Add(snapshot.SyncHealth.Status);
        }

        if (ParseCompactNumber(snapshot.IngestionBacklog.Headline) > 0)
        {
            items.Add(snapshot.IngestionBacklog.Status);
        }

        if (NeedsImportedDocumentAttention(snapshot.RecentImportedDocuments))
        {
            items.Add("Imported documents need indexing");
        }

        if (NeedsConnectorAttention(snapshot.ConnectorPreviews))
        {
            items.Add("Connectors can be enabled");
        }

        if (NeedsWorkflowRunAttention(snapshot.RecentWorkflowRuns))
        {
            items.Add("Workflow runs need review");
        }

        if (items.Count == 0)
        {
            items.Add(snapshot.WorkflowActivity.Status);
        }

        if (items.Count <= 3)
        {
            return string.Join(" · ", items);
        }

        return string.Join(" · ", items.Take(3).Append($"{items.Count - 3} more"));
    }

    private static int ParseCompactNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.EndsWith("K", StringComparison.Ordinal))
        {
            return double.TryParse(normalized[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var thousands)
                ? (int)Math.Round(thousands * 1_000)
                : 0;
        }

        if (normalized.EndsWith("M", StringComparison.Ordinal))
        {
            return double.TryParse(normalized[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var millions)
                ? (int)Math.Round(millions * 1_000_000)
                : 0;
        }

        return int.TryParse(normalized, out var count)
            ? count
            : double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var valueAsDouble)
                ? (int)Math.Round(valueAsDouble)
                : 0;
    }
}

public sealed record OperationsOverviewStatusTile(
    string Title,
    string Headline,
    string Status,
    string Route,
    string NavigationLabel)
{
    /// <summary>The tone token the status is colored with; the status text is translated.</summary>
    public string StatusToneToken { get; init; } = string.Empty;
}

public enum OperationsRecommendedActionKind
{
    Navigate,
    RefreshConversationSummaries,
    RunManualSync,
    GenerateInboxPreviews,
    RetryImportedDocumentIndexing,
    EnableConnector
}

public sealed record OperationsRecommendedActionItem(
    string CategoryLabel,
    string IconGlyph,
    string Title,
    string Detail,
    string Status,
    string CommandText,
    string Route,
    OperationsRecommendedActionKind Kind,
    long TargetId = 0,
    long SecondaryTargetId = 0)
{
    /// <summary>The tone token the status is colored with; the status text is translated.</summary>
    public string StatusToneToken { get; init; } = string.Empty;
}
