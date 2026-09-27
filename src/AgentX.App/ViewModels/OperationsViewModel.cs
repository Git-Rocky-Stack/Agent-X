using System.Globalization;
using AgentX.App.Services;
using AgentX.Core.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class OperationsViewModel : ObservableObject, IDisposable
{
    private readonly IOperationsActionService _operationsActionService;
    private readonly IOperationsDrillInService _operationsDrillInService;
    private readonly IOperationsOverviewService _operationsOverviewService;
    private readonly ILocalizationService _localization;
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

    // The summary, the status tiles and the cards start with their default texts, which the
    // constructor sets in the user's language.
    [ObservableProperty] private string _summaryHeadline = string.Empty;
    [ObservableProperty] private string _summaryDetail = string.Empty;
    [ObservableProperty] private IReadOnlyList<OperationsOverviewStatusTile> _overviewStatusTiles = Array.Empty<OperationsOverviewStatusTile>();
    [ObservableProperty] private IReadOnlyList<OperationsRecommendedActionItem> _recommendedActions = Array.Empty<OperationsRecommendedActionItem>();

    [ObservableProperty] private OperationsCardSnapshot _conversationIntelligence = new();
    [ObservableProperty] private OperationsCardSnapshot _syncHealth = new();
    [ObservableProperty] private OperationsCardSnapshot _ingestionBacklog = new();
    [ObservableProperty] private OperationsCardSnapshot _workflowActivity = new();
    [ObservableProperty] private OperationsCardSnapshot _connectors = new();
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
        ILocalizationService localization,
        ILogger logger)
    {
        _operationsActionService = operationsActionService ?? throw new ArgumentNullException(nameof(operationsActionService));
        _operationsDrillInService = operationsDrillInService ?? throw new ArgumentNullException(nameof(operationsDrillInService));
        _operationsOverviewService = operationsOverviewService ?? throw new ArgumentNullException(nameof(operationsOverviewService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _log = logger?.ForContext<OperationsViewModel>()
               ?? throw new ArgumentNullException(nameof(logger));

        var defaults = CreateFallbackSnapshot();
        ConversationIntelligence = defaults.ConversationIntelligence;
        SyncHealth = defaults.SyncHealth;
        IngestionBacklog = defaults.IngestionBacklog;
        WorkflowActivity = defaults.WorkflowActivity;
        Connectors = defaults.Connectors;
        OverviewStatusTiles = BuildOverviewStatusTiles(defaults);
        SummaryHeadline = _localization.GetString("Ops_SummaryReady");
        SummaryDetail = _localization.GetString("Ops_SummaryReadyDetail");
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
            ErrorMessage = _localization.GetString("Ops_LoadFailed");
            ApplySnapshot(CreateFallbackSnapshot());
            SummaryHeadline = _localization.GetString("Ops_SummaryUnavailable");
            SummaryDetail = _localization.GetString("Ops_SummaryUnavailableDetail");
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
            > 1 => _localization.GetString("Ops_SummaryAttentionMany", attentionAreas),
            1 => _localization.GetString("Ops_SummaryAttentionOne"),
            _ => _localization.GetString("Ops_SummaryNormal")
        };

        SummaryDetail = attentionAreas > 0
            ? BuildAttentionSummary(snapshot)
            : JoinSummaryItems(new[]
            {
                snapshot.ConversationIntelligence.Status,
                snapshot.SyncHealth.Status,
                snapshot.WorkflowActivity.Status
            });
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
                error => _localization.GetString("Ops_RefreshSummariesError", error),
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
                error => _localization.GetString("Ops_GeneratePreviewsError", error),
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
                error => _localization.GetString("Ops_EnableConnectorError", error),
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
                error => _localization.GetString("Ops_ReindexDocumentError", error),
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
                error => _localization.GetString("Ops_ActionSyncFailed", error),
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
                _localization.GetString("Ops_DrillInConversation", preview.Title)));
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
                _localization.GetString("Ops_DrillInInboxItem", preview.Title)));
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
                _localization.GetString("Ops_DrillInDocument", preview.Title)));
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
                _localization.GetString("Ops_DrillInWorkflowRun", preview.Title)));
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
                _localization.GetString("Ops_DrillInSyncEntry", preview.Title)));
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
                _localization.GetString("Ops_DrillInConnector", preview.Title)));
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
    /// exception becomes an error message, worded by <paramref name="describeFailure"/> from the
    /// exception's message, instead of an unobserved command failure.
    /// </summary>
    private async Task RunActionAsync(
        string actionName,
        Func<string, string> describeFailure,
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
            ApplyActionFeedback(new OperationsActionResult(false, describeFailure(ex.Message)));
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

    private OperationsOverviewSnapshot CreateFallbackSnapshot() => new()
    {
        ConversationIntelligence = CreateDefaultConversationCard(),
        SyncHealth = CreateDefaultSyncCard(),
        IngestionBacklog = CreateDefaultBacklogCard(),
        WorkflowActivity = CreateDefaultWorkflowCard(),
        Connectors = CreateDefaultConnectorsCard()
    };

    // The defaults carry the kinds their text stands for, so the checks below, which read the
    // kinds, treat them as before.
    private OperationsCardSnapshot CreateDefaultConversationCard() => new()
    {
        Headline = "0",
        Status = _localization.GetString("Ops_RecallInactive"),
        StatusKind = OperationsStatusKind.RecallInactive,
        Detail = _localization.GetString("Ops_DefaultRecallDetail")
    };

    private OperationsCardSnapshot CreateDefaultSyncCard() => new()
    {
        Headline = _localization.GetString("Ops_SyncNotConfigured"),
        Status = _localization.GetString("Ops_SyncOff"),
        StatusKind = OperationsStatusKind.SyncNotConfigured,
        Detail = _localization.GetString("Ops_SyncSetupHint")
    };

    private OperationsCardSnapshot CreateDefaultBacklogCard() => new()
    {
        Headline = "0",
        Status = _localization.GetString("Ops_BacklogClear"),
        StatusKind = OperationsStatusKind.BacklogClear,
        Detail = _localization.GetString("Ops_BacklogDetailIdle")
    };

    private OperationsCardSnapshot CreateDefaultWorkflowCard() => new()
    {
        Headline = "0",
        Status = _localization.GetString("Ops_WorkflowReady"),
        StatusKind = OperationsStatusKind.WorkflowReadyToAutomate,
        SupportingPrimary = _localization.GetString("Ops_WorkflowsNoRecentRuns"),
        SupportingPrimaryKind = OperationsStatusKind.WorkflowsNoRecentRuns,
        SupportingSecondary = _localization.GetString("Ops_WorkflowAvgUnavailable"),
        Detail = _localization.GetString("Ops_WorkflowCreateHint")
    };

    private OperationsCardSnapshot CreateDefaultConnectorsCard() => new()
    {
        Headline = "0",
        Status = _localization.GetString("Ops_NoPluginsInstalled"),
        StatusKind = OperationsStatusKind.NoPluginsInstalled,
        Detail = _localization.GetString("Ops_ConnectorsDetailInstall")
    };

    private IReadOnlyList<OperationsOverviewStatusTile> BuildOverviewStatusTiles(OperationsOverviewSnapshot snapshot) =>
    [
        new OperationsOverviewStatusTile(
            _localization.GetString("Ops_AreaConversation"),
            snapshot.ConversationIntelligence.Headline,
            snapshot.ConversationIntelligence.Status,
            "Analytics",
            _localization.GetString("Dash_ActionOpenAnalytics")) { StatusToneToken = snapshot.ConversationIntelligence.StatusToneToken },
        new OperationsOverviewStatusTile(
            _localization.GetString("Ops_AreaSync"),
            snapshot.SyncHealth.Headline,
            snapshot.SyncHealth.Status,
            "SyncSettings",
            _localization.GetString("Dash_ActionOpenSync")) { StatusToneToken = snapshot.SyncHealth.StatusToneToken },
        new OperationsOverviewStatusTile(
            _localization.GetString("Ops_AreaBacklog"),
            snapshot.IngestionBacklog.Headline,
            snapshot.IngestionBacklog.Status,
            "Inbox",
            _localization.GetString("Dash_ActionOpenInbox")) { StatusToneToken = snapshot.IngestionBacklog.StatusToneToken },
        new OperationsOverviewStatusTile(
            _localization.GetString("Ops_AreaWorkflows"),
            snapshot.WorkflowActivity.Headline,
            snapshot.WorkflowActivity.Status,
            "Workflows",
            _localization.GetString("Dash_ActionOpenWorkflows")) { StatusToneToken = snapshot.WorkflowActivity.StatusToneToken },
        new OperationsOverviewStatusTile(
            _localization.GetString("Ops_AreaConnectors"),
            snapshot.Connectors.Headline,
            snapshot.Connectors.Status,
            "PluginManager",
            _localization.GetString("Dash_ActionOpenPlugins")) { StatusToneToken = snapshot.Connectors.StatusToneToken }
    ];

    private IReadOnlyList<OperationsRecommendedActionItem> BuildRecommendedActions(OperationsOverviewSnapshot snapshot)
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
                _localization.GetString("Dash_ActionCategoryMemory"),
                "\uE9D2",
                _localization.GetString("Ops_FixRecallTitle"),
                _localization.GetString("Ops_FixRecallDetail"),
                snapshot.ConversationIntelligence.Status,
                _localization.GetString("Ops_FixRefreshSummaries"),
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
                requiresSetup
                    ? _localization.GetString("Dash_ActionCategorySetup")
                    : _localization.GetString("Ops_AreaSync"),
                "\uE895",
                requiresSetup
                    ? _localization.GetString("Ops_FixConfigureSyncTitle")
                    : _localization.GetString("Ops_FixRunSyncTitle"),
                requiresSetup
                    ? _localization.GetString("Ops_FixConfigureSyncDetail")
                    : _localization.GetString("Ops_FixRunSyncDetail"),
                snapshot.SyncHealth.Status,
                requiresSetup
                    ? _localization.GetString("Dash_ActionOpenSync")
                    : _localization.GetString("Ops_FixRunSyncNow"),
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
                _localization.GetString("Ops_AreaBacklog"),
                "\uE8B7",
                _localization.GetString("Ops_FixPreviewsTitle"),
                _localization.GetString("Ops_FixPreviewsDetail"),
                snapshot.IngestionBacklog.Status,
                _localization.GetString("Ops_FixGeneratePreviews"),
                "Inbox",
                OperationsRecommendedActionKind.GenerateInboxPreviews)
            {
                StatusToneToken = snapshot.IngestionBacklog.StatusToneToken
            });
        }

        if (documentNeedingAttention is not null)
        {
            items.Add(new OperationsRecommendedActionItem(
                _localization.GetString("Ops_FixCategoryIndexing"),
                "\uE8B1",
                _localization.GetString("Ops_FixRetryIndexingTitle", documentNeedingAttention.Title),
                _localization.GetString("Ops_FixRetryIndexingDetail"),
                documentNeedingAttention.HealthStatus,
                _localization.GetString("Ops_FixRetryIndex"),
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
                _localization.GetString("Ops_PluginTypeConnector"),
                "\uE943",
                _localization.GetString("Ops_FixEnableConnectorTitle", connectorToEnable.Title),
                _localization.GetString("Ops_FixEnableConnectorDetail"),
                connectorToEnable.Status,
                _localization.GetString("Ops_FixEnableConnector"),
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
                _localization.GetString("Dash_ActionCategoryExpansion"),
                "\uE943",
                _localization.GetString("Dash_ActionConnectTitle"),
                _localization.GetString("Ops_FixConnectDetail"),
                noStatus ? _localization.GetString("QuickAct_StatusNoConnectors") : snapshot.Connectors.Status,
                _localization.GetString("Dash_ActionOpenPlugins"),
                "PluginManager",
                OperationsRecommendedActionKind.Navigate)
            {
                // A tone token, not text: the status converter colors it as it always did.
                StatusToneToken = noStatus ? "No connectors enabled" : snapshot.Connectors.StatusToneToken
            });
        }

        if (failedWorkflowRun is not null)
        {
            items.Add(new OperationsRecommendedActionItem(
                _localization.GetString("Ops_FixCategoryWorkflow"),
                "\uE8C7",
                _localization.GetString("Dash_ActionReviewRunTitle", failedWorkflowRun.Title),
                _localization.GetString("Ops_FixReviewRunDetail"),
                failedWorkflowRun.Status,
                _localization.GetString("Dash_ActionOpenWorkflows"),
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
            var review = _localization.GetString("Dash_ActionCategoryReview");
            items.Add(new OperationsRecommendedActionItem(
                review,
                "\uE9D2",
                _localization.GetString("Ops_FixInspectRecallTitle"),
                _localization.GetString("Ops_FixInspectRecallDetail"),
                snapshot.ConversationIntelligence.Status,
                _localization.GetString("Dash_ActionOpenAnalytics"),
                "Analytics",
                OperationsRecommendedActionKind.Navigate)
            {
                StatusToneToken = snapshot.ConversationIntelligence.StatusToneToken
            });
            items.Add(new OperationsRecommendedActionItem(
                review,
                "\uE895",
                _localization.GetString("Ops_FixReviewSyncTitle"),
                _localization.GetString("Ops_FixReviewSyncDetail"),
                snapshot.SyncHealth.Status,
                _localization.GetString("Dash_ActionOpenSync"),
                "SyncSettings",
                OperationsRecommendedActionKind.Navigate)
            {
                StatusToneToken = snapshot.SyncHealth.StatusToneToken
            });
            items.Add(new OperationsRecommendedActionItem(
                review,
                "\uE8C7",
                _localization.GetString("Ops_FixReviewWorkflowsTitle"),
                _localization.GetString("Ops_FixReviewWorkflowsDetail"),
                snapshot.WorkflowActivity.Status,
                _localization.GetString("Dash_ActionOpenWorkflows"),
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
        var sourceLabel = _localization.GetString("Ops_DrillInRecommendation", action.Title);
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

    private string BuildAttentionSummary(OperationsOverviewSnapshot snapshot)
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
            items.Add(_localization.GetString("Ops_AttentionImportedDocuments"));
        }

        if (NeedsConnectorAttention(snapshot.ConnectorPreviews))
        {
            items.Add(_localization.GetString("Ops_AttentionConnectors"));
        }

        if (NeedsWorkflowRunAttention(snapshot.RecentWorkflowRuns))
        {
            items.Add(_localization.GetString("Ops_AttentionWorkflowRuns"));
        }

        if (items.Count == 0)
        {
            items.Add(snapshot.WorkflowActivity.Status);
        }

        if (items.Count <= 3)
        {
            return JoinSummaryItems(items);
        }

        return JoinSummaryItems(items.Take(3).Append(_localization.GetString("Ops_SummaryMore", items.Count - 3)));
    }

    /// <summary>
    /// Lists the summary's statuses with the separator of the user's language (a comma in
    /// English). They used to be joined with a middle dot.
    /// </summary>
    private string JoinSummaryItems(IEnumerable<string> items) =>
        string.Join(_localization.GetString("Ops_SummarySeparator"), items);

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
