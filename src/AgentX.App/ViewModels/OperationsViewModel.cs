using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.App.Services;
using Serilog;
using System.Globalization;

namespace AgentX.App.ViewModels;

public partial class OperationsViewModel : ObservableObject, IDisposable
{
    private readonly IOperationsOverviewService _operationsOverviewService;
    private readonly ILogger _log;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = string.Empty;

    [ObservableProperty] private string _summaryHeadline = "Operations ready";
    [ObservableProperty] private string _summaryDetail = "Unified status for conversation intelligence, sync posture, ingestion backlog, workflows, and connectors.";

    [ObservableProperty] private OperationsCardSnapshot _conversationIntelligence = CreateDefaultConversationCard();
    [ObservableProperty] private OperationsCardSnapshot _syncHealth = CreateDefaultSyncCard();
    [ObservableProperty] private OperationsCardSnapshot _ingestionBacklog = CreateDefaultBacklogCard();
    [ObservableProperty] private OperationsCardSnapshot _workflowActivity = CreateDefaultWorkflowCard();
    [ObservableProperty] private OperationsCardSnapshot _connectors = CreateDefaultConnectorsCard();
    [ObservableProperty] private IReadOnlyList<OperationsConversationPreview> _recentConversationSummaries = Array.Empty<OperationsConversationPreview>();
    [ObservableProperty] private IReadOnlyList<OperationsSyncPreview> _recentSyncPasses = Array.Empty<OperationsSyncPreview>();
    [ObservableProperty] private IReadOnlyList<OperationsInboxPreview> _pendingInboxItems = Array.Empty<OperationsInboxPreview>();
    [ObservableProperty] private IReadOnlyList<OperationsWorkflowRunPreview> _recentWorkflowRuns = Array.Empty<OperationsWorkflowRunPreview>();
    [ObservableProperty] private IReadOnlyList<OperationsConnectorPreview> _connectorPreviews = Array.Empty<OperationsConnectorPreview>();

    public Action<string>? NavigateRequested { get; set; }

    public OperationsViewModel(
        IOperationsOverviewService operationsOverviewService,
        ILogger logger)
    {
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
        RecentWorkflowRuns = snapshot.RecentWorkflowRuns;
        ConnectorPreviews = snapshot.ConnectorPreviews;

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

    [RelayCommand]
    private void NavigateToDashboard() => NavigateRequested?.Invoke("Dashboard");

    [RelayCommand]
    private void NavigateToAnalytics() => NavigateRequested?.Invoke("Analytics");

    [RelayCommand]
    private void NavigateToSyncSettings() => NavigateRequested?.Invoke("SyncSettings");

    [RelayCommand]
    private void NavigateToInbox() => NavigateRequested?.Invoke("Inbox");

    [RelayCommand]
    private void NavigateToWorkflows() => NavigateRequested?.Invoke("Workflows");

    [RelayCommand]
    private void NavigateToPluginManager() => NavigateRequested?.Invoke("PluginManager");

    public void Dispose()
    {
        _log.Debug("OperationsViewModel disposed");
    }

    private static OperationsOverviewSnapshot CreateFallbackSnapshot() => new()
    {
        ConversationIntelligence = CreateDefaultConversationCard(),
        SyncHealth = CreateDefaultSyncCard(),
        IngestionBacklog = CreateDefaultBacklogCard(),
        WorkflowActivity = CreateDefaultWorkflowCard(),
        Connectors = CreateDefaultConnectorsCard()
    };

    private static OperationsCardSnapshot CreateDefaultConversationCard() => new()
    {
        Headline = "0",
        Status = "Durable recall inactive",
        Detail = "Open Analytics to inspect summary coverage and durable recall detail."
    };

    private static OperationsCardSnapshot CreateDefaultSyncCard() => new()
    {
        Headline = "Not configured",
        Status = "Collaborative sync is off",
        Detail = "Configure a shared folder to keep multiple installations aligned."
    };

    private static OperationsCardSnapshot CreateDefaultBacklogCard() => new()
    {
        Headline = "0",
        Status = "Queue clear",
        Detail = "Watch folders and enabled connectors will surface new items here."
    };

    private static OperationsCardSnapshot CreateDefaultWorkflowCard() => new()
    {
        Headline = "0",
        Status = "Ready to automate",
        SupportingPrimary = "No recent runs",
        SupportingSecondary = "Avg duration unavailable",
        Detail = "Create or launch a workflow from Vault or Search to start automating multi-step tasks."
    };

    private static OperationsCardSnapshot CreateDefaultConnectorsCard() => new()
    {
        Headline = "0",
        Status = "No plugins installed",
        Detail = "Install or enable plugins to bring external data and workflow extensions into the app."
    };

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

        return count;
    }

    private static bool NeedsConversationAttention(OperationsCardSnapshot card) =>
        card.Status.Contains("pending", StringComparison.OrdinalIgnoreCase) ||
        card.Status.Contains("stale", StringComparison.OrdinalIgnoreCase);

    private static bool NeedsSyncAttention(OperationsCardSnapshot card) =>
        card.Headline.Equals("Not configured", StringComparison.OrdinalIgnoreCase) ||
        card.Status.Contains("conflict", StringComparison.OrdinalIgnoreCase) ||
        card.Status.Contains("attention", StringComparison.OrdinalIgnoreCase) ||
        card.Status.Contains("off", StringComparison.OrdinalIgnoreCase);

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

        if (items.Count == 0)
        {
            items.Add(snapshot.WorkflowActivity.Status);
        }

        return string.Join(" · ", items.Take(3));
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
