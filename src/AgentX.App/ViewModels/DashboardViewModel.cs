using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.AI;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Search;
using AgentX.Core.Services.Analytics;
using AgentX.Core.Services.Analytics.Models;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Indexing;
using AgentX.Core.Services.Sync;
using AgentX.Core.Services.Sync.Models;
using AgentX.Core.Services.Workflows;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    // ── Services ─────────────────────────────────────────────
    private readonly IAiService _aiService;
    private readonly IConversationService _conversationService;
    private readonly IDocumentService _documentService;
    private readonly IHardwareDetector _hardwareDetector;
    private readonly ICollectionService _collectionService;
    private readonly IIndexingService _indexingService;
    private readonly IRagPipeline _ragPipeline;
    private readonly IAnalyticsService _analyticsService;
    private readonly IInboxService _inboxService;
    private readonly ISyncService _syncService;
    private readonly IWorkflowService _workflowService;

    // ── AI Status ───────────────────────────────────────────
    [ObservableProperty] private bool _isOllamaConnected;
    [ObservableProperty] private string _activeModelName = "No model loaded";
    [ObservableProperty] private string _connectionStatus = "Checking connection...";

    // ── Knowledge Vault Stats ───────────────────────────────
    [ObservableProperty] private int _totalDocuments;
    [ObservableProperty] private int _totalChunks;
    [ObservableProperty] private int _totalCollections;
    [ObservableProperty] private string _totalStorageSize = "0 MB";
    [ObservableProperty] private string _indexingStatus = "Idle";

    // ── Chat Stats ──────────────────────────────────────────
    [ObservableProperty] private int _totalConversations;
    [ObservableProperty] private long _totalTokensUsed;

    // ── System ──────────────────────────────────────────────
    [ObservableProperty] private string _gpuName = "Detecting...";
    [ObservableProperty] private string _availableRam = "Detecting...";
    [ObservableProperty] private bool _hasNpu;
    [ObservableProperty] private string _appVersion = "1.0.0";
    [ObservableProperty] private string _totalRamInfo = "-- GB total";
    [ObservableProperty] private string _gpuVramInfo = "-- VRAM";

    // ── Operations Overview ───────────────────────────────────
    [ObservableProperty] private string _conversationIntelligenceHeadline = "0";
    [ObservableProperty] private string _conversationIntelligenceStatus = "Durable recall inactive";
    [ObservableProperty] private string _conversationIntelligenceDetail = "No stored conversation summaries yet.";
    [ObservableProperty] private string _syncHealthHeadline = "Not configured";
    [ObservableProperty] private string _syncHealthStatus = "Collaborative sync is off";
    [ObservableProperty] private string _syncHealthDetail = "Configure a shared folder to synchronize multiple installations.";
    [ObservableProperty] private string _inboxHeadline = "0";
    [ObservableProperty] private string _inboxStatus = "Queue clear";
    [ObservableProperty] private string _inboxDetail = "No items awaiting triage.";
    [ObservableProperty] private string _workflowHeadline = "0";
    [ObservableProperty] private string _workflowStatus = "Ready to automate";
    [ObservableProperty] private string _workflowDetail = "No workflows available yet.";

    // ── Indexing ─────────────────────────────────────────────
    [ObservableProperty] private int _indexedPercent;
    [ObservableProperty] private int _pendingIndexCount;

    // ── Quick Actions ───────────────────────────────────────
    [ObservableProperty] private string _quickSearchQuery = string.Empty;

    // ── Recent Activity ─────────────────────────────────────
    [ObservableProperty] private ObservableCollection<DashboardRecentDocumentItem> _recentDocuments = new();
    [ObservableProperty] private ObservableCollection<DashboardRecentConversationItem> _recentConversations = new();

    // ── Visibility Helpers ──────────────────────────────────
    [ObservableProperty] private bool _hasRecentDocuments;
    [ObservableProperty] private bool _hasRecentConversations;
    [ObservableProperty] private bool _hasFileTypeData;
    [ObservableProperty] private bool _hasCollectionData;

    // ── Knowledge Insights ──────────────────────────────────
    [ObservableProperty] private ObservableCollection<DashboardFileTypeBreakdownItem> _fileTypeBreakdown = new();
    [ObservableProperty] private ObservableCollection<DashboardTopCollectionItem> _topCollections = new();

    // ── Navigation ────────────────────────────────────────────
    public Action<string>? NavigateRequested { get; set; }

    public DashboardViewModel(
        IAiService aiService,
        IConversationService conversationService,
        IDocumentService documentService,
        IHardwareDetector hardwareDetector,
        ICollectionService collectionService,
        IIndexingService indexingService,
        IRagPipeline ragPipeline,
        IAnalyticsService analyticsService,
        IInboxService inboxService,
        ISyncService syncService,
        IWorkflowService workflowService)
    {
        _aiService = aiService;
        _conversationService = conversationService;
        _documentService = documentService;
        _hardwareDetector = hardwareDetector;
        _collectionService = collectionService;
        _indexingService = indexingService;
        _ragPipeline = ragPipeline;
        _analyticsService = analyticsService;
        _inboxService = inboxService;
        _syncService = syncService;
        _workflowService = workflowService;
        Log.Debug("DashboardViewModel created with services");
    }

    public async Task InitializeAsync()
    {
        Log.Information("Dashboard initializing...");

        // Run all data-loading tasks in parallel for faster initialization
        await Task.WhenAll(
            LoadAiStatusAsync(),
            LoadVaultStatsAsync(),
            LoadChatStatsAsync(),
            LoadSystemInfoAsync(),
            LoadRecentActivityAsync(),
            LoadInsightsAsync(),
            LoadIndexingStatusAsync(),
            LoadOperationsOverviewAsync());

        Log.Information("Dashboard initialized");
    }

    private async Task LoadAiStatusAsync()
    {
        try
        {
            var connected = await _aiService.ActiveProvider.CheckConnectionAsync();
            IsOllamaConnected = connected;
            ConnectionStatus = connected ? "Connected to Ollama" : "Ollama not detected";
            ActiveModelName = connected && !string.IsNullOrEmpty(_aiService.ActiveModelId)
                ? _aiService.ActiveModelId
                : "Setup required";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check AI connection status for dashboard");
            IsOllamaConnected = false;
            ConnectionStatus = "Ollama not detected";
            ActiveModelName = "Setup required";
        }
    }

    private async Task LoadVaultStatsAsync()
    {
        try
        {
            var docCount = await _documentService.GetTotalDocumentCountAsync();
            TotalDocuments = (int)docCount;

            var storageBytes = await _documentService.GetTotalStorageBytesAsync();
            TotalStorageSize = FormatHelper.FormatBytes(storageBytes);

            var collectionCount = await _collectionService.GetCollectionCountAsync();
            TotalCollections = collectionCount;

            var chunkCount = await _ragPipeline.GetIndexedChunkCountAsync();
            TotalChunks = (int)chunkCount;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load vault stats for dashboard");
            TotalDocuments = 0;
            TotalChunks = 0;
            TotalCollections = 0;
            TotalStorageSize = "0 MB";
        }
    }

    private async Task LoadChatStatsAsync()
    {
        try
        {
            TotalConversations = await _conversationService.GetConversationCountAsync();
            TotalTokensUsed = await _conversationService.GetTotalTokensUsedAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load chat stats for dashboard");
            TotalConversations = 0;
            TotalTokensUsed = 0;
        }
    }

    private async Task LoadSystemInfoAsync()
    {
        try
        {
            var hw = await _hardwareDetector.DetectAsync();
            GpuName = hw.GpuName;
            AvailableRam = hw.AvailableRamFormatted;
            HasNpu = hw.HasNpu;
            TotalRamInfo = $"{hw.TotalRamFormatted} total";
            GpuVramInfo = hw.GpuVramBytes > 0 ? $"{hw.GpuVramFormatted} VRAM" : "Integrated GPU";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to detect hardware for dashboard");
            GpuName = "Detection failed";
            AvailableRam = "Unknown";
            HasNpu = false;
            TotalRamInfo = "Unknown";
            GpuVramInfo = "Unknown";
        }
    }

    private async Task LoadRecentActivityAsync()
    {
        try
        {
            var docs = await _documentService.GetRecentDocumentsAsync(5);
            var recentDocs = docs.Take(5).Select(d => new DashboardRecentDocumentItem
            {
                Id = d.Id,
                FileName = d.FileName,
                FileType = d.FileType,
                ImportedAgo = FormatHelper.TimeAgoWithMonths(d.ImportedAt),
                FileSize = FormatHelper.FormatBytes(d.FileSizeBytes)
            });

            RecentDocuments = new ObservableCollection<DashboardRecentDocumentItem>(recentDocs);
            HasRecentDocuments = RecentDocuments.Count > 0;

            var conversations = await _conversationService.GetRecentConversationsAsync(5);
            var recentConvos = conversations.Take(5).Select(c => new DashboardRecentConversationItem
            {
                Id = c.Id,
                Title = string.IsNullOrWhiteSpace(c.Title) ? "Untitled Conversation" : c.Title,
                Preview = $"{c.MessageCount} messages",
                TimeAgo = FormatHelper.TimeAgoWithMonths(c.UpdatedAt),
                MessageCount = c.MessageCount
            });

            RecentConversations = new ObservableCollection<DashboardRecentConversationItem>(recentConvos);
            HasRecentConversations = RecentConversations.Count > 0;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load recent activity for dashboard");
            RecentDocuments = new ObservableCollection<DashboardRecentDocumentItem>();
            RecentConversations = new ObservableCollection<DashboardRecentConversationItem>();
            HasRecentDocuments = false;
            HasRecentConversations = false;
        }
    }

    private async Task LoadInsightsAsync()
    {
        try
        {
            // File type distribution
            var distribution = await _documentService.GetFileTypeDistributionAsync();
            var total = distribution.Values.Sum();

            // Color palette for file types
            var colors = new[] { "#C41E3A", "#3B82F6", "#22C55E", "#F59E0B", "#A855F7", "#EC4899", "#06B6D4", "#F97316" };
            var colorIndex = 0;

            var breakdown = distribution
                .OrderByDescending(kvp => kvp.Value)
                .Select(kvp => new DashboardFileTypeBreakdownItem
                {
                    FileType = kvp.Key.ToUpperInvariant(),
                    Count = kvp.Value,
                    Percentage = total > 0 ? Math.Round(kvp.Value * 100.0 / total, 1) : 0,
                    Color = colors[colorIndex++ % colors.Length]
                });

            FileTypeBreakdown = new ObservableCollection<DashboardFileTypeBreakdownItem>(breakdown);
            HasFileTypeData = FileTypeBreakdown.Count > 0;

            // Top collections by document count
            var allCollections = await _collectionService.GetAllCollectionsAsync();
            var topCols = allCollections
                .OrderByDescending(c => c.DocumentCount)
                .Take(5)
                .ToList();

            var maxDocCount = topCols.FirstOrDefault()?.DocumentCount ?? 1;
            if (maxDocCount == 0) maxDocCount = 1;

            var topColItems = topCols.Select(c => new DashboardTopCollectionItem
            {
                Name = c.Name,
                DocumentCount = c.DocumentCount,
                BarWidthPercent = c.DocumentCount * 100.0 / maxDocCount
            });

            TopCollections = new ObservableCollection<DashboardTopCollectionItem>(topColItems);
            HasCollectionData = TopCollections.Count > 0;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load insights for dashboard");
            FileTypeBreakdown = new ObservableCollection<DashboardFileTypeBreakdownItem>();
            TopCollections = new ObservableCollection<DashboardTopCollectionItem>();
            HasFileTypeData = false;
            HasCollectionData = false;
        }
    }

    private async Task LoadIndexingStatusAsync()
    {
        try
        {
            var queueLength = await _indexingService.GetQueueLengthAsync();
            var processedCount = await _indexingService.GetProcessedCountAsync();
            PendingIndexCount = queueLength;

            var total = processedCount + queueLength;
            IndexedPercent = total > 0 ? (int)Math.Round(processedCount * 100.0 / total) : 100;

            IndexingStatus = _indexingService.IsProcessing
                ? $"Processing ({queueLength} queued)"
                : queueLength > 0
                    ? $"{queueLength} pending"
                    : "All indexed";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load indexing status for dashboard");
            PendingIndexCount = 0;
            IndexedPercent = 100;
            IndexingStatus = "Idle";
        }
    }

    private async Task LoadOperationsOverviewAsync()
    {
        try
        {
            var analyticsSummaryTask = _analyticsService.GetSummaryAsync();
            var conversationIntelligenceTask = _analyticsService.GetConversationIntelligenceAsync(maxRecent: 3);
            var pendingInboxTask = _inboxService.GetPendingCountAsync();
            var workflowsTask = _workflowService.GetAllWorkflowsAsync();
            var syncConfigTask = _syncService.GetConfigurationAsync();

            await Task.WhenAll(
                analyticsSummaryTask,
                conversationIntelligenceTask,
                pendingInboxTask,
                workflowsTask,
                syncConfigTask);

            ApplyConversationIntelligence(await conversationIntelligenceTask);
            ApplyInboxBacklog(await pendingInboxTask);
            ApplyWorkflowActivity(await analyticsSummaryTask, await workflowsTask);
            ApplySyncHealth(await syncConfigTask, _syncService.Status);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load dashboard operations overview");
            ConversationIntelligenceHeadline = "0";
            ConversationIntelligenceStatus = "Durable recall inactive";
            ConversationIntelligenceDetail = "Open Analytics to inspect summary coverage.";
            InboxHeadline = "0";
            InboxStatus = "Queue clear";
            InboxDetail = "No items awaiting triage.";
            WorkflowHeadline = "0";
            WorkflowStatus = "Ready to automate";
            WorkflowDetail = "Open Workflows to create or run automations.";
            SyncHealthHeadline = "Unavailable";
            SyncHealthStatus = "Sync status unavailable";
            SyncHealthDetail = "Open Collaborative Sync for details.";
        }
    }

    // ── Commands ─────────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Log.Debug("Dashboard refresh requested");
        await InitializeAsync();
    }

    [RelayCommand]
    private void SetupAi()
    {
        Log.Debug("Navigate to Settings (Setup AI) requested from Dashboard");
        NavigateRequested?.Invoke("Settings");
    }

    [RelayCommand]
    private void NavigateToChat()
    {
        Log.Debug("Navigate to Chat requested from Dashboard");
        NavigateRequested?.Invoke("Chat");
    }

    [RelayCommand]
    private void NavigateToVault()
    {
        Log.Debug("Navigate to Knowledge Vault requested from Dashboard");
        NavigateRequested?.Invoke("KnowledgeVault");
    }

    [RelayCommand]
    private void NavigateToAskFiles()
    {
        Log.Debug("Navigate to Ask Files requested from Dashboard");
        NavigateRequested?.Invoke("AskFiles");
    }

    [RelayCommand]
    private void NavigateToSearch()
    {
        Log.Debug("Navigate to Search requested from Dashboard");
        NavigateRequested?.Invoke("Search");
    }

    [RelayCommand]
    private void NavigateToQuickActions()
    {
        Log.Debug("Navigate to Quick Actions requested from Dashboard");
        NavigateRequested?.Invoke("QuickActions");
    }

    [RelayCommand]
    private void NavigateToAnalytics()
    {
        Log.Debug("Navigate to Analytics requested from Dashboard");
        NavigateRequested?.Invoke("Analytics");
    }

    [RelayCommand]
    private void NavigateToInbox()
    {
        Log.Debug("Navigate to Smart Inbox requested from Dashboard");
        NavigateRequested?.Invoke("Inbox");
    }

    [RelayCommand]
    private void NavigateToWorkflows()
    {
        Log.Debug("Navigate to Workflows requested from Dashboard");
        NavigateRequested?.Invoke("Workflows");
    }

    [RelayCommand]
    private void NavigateToSyncSettings()
    {
        Log.Debug("Navigate to Collaborative Sync requested from Dashboard");
        NavigateRequested?.Invoke("SyncSettings");
    }

    [RelayCommand]
    private Task QuickSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(QuickSearchQuery)) return Task.CompletedTask;
        Log.Debug("Quick search: {Query}", QuickSearchQuery);
        NavigateRequested?.Invoke("Search");
        return Task.CompletedTask;
    }

    private void ApplyConversationIntelligence(ConversationIntelligenceOverview overview)
    {
        ConversationIntelligenceHeadline = FormatCompactNumber(overview.SummarizedConversations);

        ConversationIntelligenceStatus = overview.PendingRefreshes switch
        {
            > 1 => $"{overview.PendingRefreshes} refreshes pending",
            1 => "1 refresh pending",
            _ when overview.StaleConversations > 1 => $"{overview.StaleConversations} stale summaries",
            _ when overview.StaleConversations == 1 => "1 stale summary",
            _ when overview.SummarizedConversations > 0 => "Durable recall current",
            _ => "Durable recall inactive"
        };

        var latestSummary = overview.RecentSummaries.FirstOrDefault();
        ConversationIntelligenceDetail = latestSummary is null
            ? "Open Analytics to inspect summary coverage and recent snapshots."
            : $"{FormatCompactNumber(overview.CurrentSnapshots)} stored snapshots · latest {FormatHelper.TimeAgoWithMonths(latestSummary.GeneratedAt)}";
    }

    private void ApplyInboxBacklog(int pendingCount)
    {
        InboxHeadline = FormatCompactNumber(pendingCount);
        InboxStatus = pendingCount switch
        {
            > 1 => $"{pendingCount} items awaiting triage",
            1 => "1 item awaiting triage",
            _ => "Queue clear"
        };
        InboxDetail = pendingCount > 0
            ? "Open Smart Inbox to accept, defer, or reject pending items."
            : "Watched folders and connector imports will surface here.";
    }

    private void ApplyWorkflowActivity(AnalyticsSummary summary, IReadOnlyList<Core.Data.Entities.WorkflowEntity> workflows)
    {
        WorkflowHeadline = FormatCompactNumber(summary.TotalWorkflowRuns);

        var enabledCount = workflows.Count(workflow => workflow.IsEnabled);
        var topWorkflow = workflows
            .Where(workflow => workflow.RunCount > 0)
            .OrderByDescending(workflow => workflow.RunCount)
            .FirstOrDefault();

        WorkflowStatus = summary.TotalWorkflowRuns switch
        {
            > 1 => $"{FormatCompactNumber(summary.TotalWorkflowRuns)} runs recorded",
            1 => "1 run recorded",
            _ => "Ready to automate"
        };

        WorkflowDetail = topWorkflow is not null
            ? $"{FormatCompactNumber(enabledCount)} workflows available · top: {topWorkflow.Name}"
            : enabledCount switch
            {
                > 1 => $"{FormatCompactNumber(enabledCount)} workflows available in the builder.",
                1 => "1 workflow available in the builder.",
                _ => "Create or enable a workflow to start automating multi-step tasks."
            };
    }

    private void ApplySyncHealth(SyncConfiguration? config, SyncStatus status)
    {
        if (config is null || string.IsNullOrWhiteSpace(config.SyncFolderPath))
        {
            SyncHealthHeadline = "Not configured";
            SyncHealthStatus = "Collaborative sync is off";
            SyncHealthDetail = "Configure a shared folder to keep multiple installations aligned.";
            return;
        }

        SyncHealthHeadline = status.SyncState switch
        {
            SyncState.Syncing => "Syncing now",
            SyncState.Conflict => "Conflict detected",
            SyncState.Error => "Needs attention",
            _ when status.LastSyncAt.HasValue => FormatHelper.TimeAgoWithMonths(status.LastSyncAt.Value),
            _ => "Configured"
        };

        SyncHealthStatus = status.SyncState switch
        {
            SyncState.Syncing => "Exchange in progress",
            SyncState.Conflict => "Resolve sync conflicts",
            SyncState.Error => "Review sync health",
            _ when status.PendingChanges > 1 => $"{status.PendingChanges} local changes pending",
            _ when status.PendingChanges == 1 => "1 local change pending",
            _ => "Standing by"
        };

        SyncHealthDetail = !string.IsNullOrWhiteSpace(status.ErrorMessage)
            ? status.ErrorMessage
            : config.SyncScope == SyncScope.SelectedCollections
                ? "Scoped to selected collections."
                : "Syncing the full workspace.";
    }

    private static string FormatCompactNumber(int value) => FormatCompactNumber((long)value);

    private static string FormatCompactNumber(long value) =>
        value >= 1_000_000 ? $"{value / 1_000_000.0:F1}M"
        : value >= 1_000 ? $"{value / 1_000.0:F1}K"
        : value.ToString();

    public void Dispose()
    {
        Log.Debug("DashboardViewModel disposed");
    }
}

// ═══════════════════════════════════════════════════════════════════
//  DISPLAY ITEM CLASSES (top-level for x:Bind DataTemplate support)
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Represents a recently imported document for display on the dashboard.
/// </summary>
public class DashboardRecentDocumentItem
{
    public long Id { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string FileType { get; init; } = string.Empty;
    public string ImportedAgo { get; init; } = string.Empty;
    public string FileSize { get; init; } = string.Empty;

    public string FileTypeIcon => FileType.ToLowerInvariant() switch
    {
        "pdf" => "\uEA90",
        "docx" or "doc" => "\uE8A5",
        "txt" => "\uE8A4",
        "md" => "\uE943",
        "cs" or "py" or "js" or "ts" => "\uE943",
        "png" or "jpg" or "jpeg" or "gif" => "\uEB9F",
        _ => "\uE7C3"
    };
}

/// <summary>
/// Represents a recent AI conversation for display on the dashboard.
/// </summary>
public class DashboardRecentConversationItem
{
    public long Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Preview { get; init; } = string.Empty;
    public string TimeAgo { get; init; } = string.Empty;
    public int MessageCount { get; init; }
}

/// <summary>
/// Represents a file type with its count and percentage for the distribution chart.
/// </summary>
public class DashboardFileTypeBreakdownItem
{
    public string FileType { get; init; } = string.Empty;
    public int Count { get; init; }
    public double Percentage { get; init; }
    public string Color { get; init; } = "#666666";
    public string PercentageLabel => $"{Percentage:F1}%";
    public string CountLabel => $"({Count})";
}

/// <summary>
/// Represents a top collection for the bar chart on the dashboard.
/// </summary>
public class DashboardTopCollectionItem
{
    public string Name { get; init; } = string.Empty;
    public int DocumentCount { get; init; }
    public double BarWidthPercent { get; init; } // 0-100 relative to largest
    public string CountLabel => $"{DocumentCount} docs";
}
