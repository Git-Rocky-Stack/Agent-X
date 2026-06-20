using System.Collections.ObjectModel;
using AgentX.App.Services;
using AgentX.Core.AI;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Search;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Indexing;
using AgentX.Core.Services.Privacy;
using AgentX.Core.Services.TemporalIdentity;
using AgentX.Core.Services.TemporalIdentity.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly IOperationsOverviewService _operationsOverviewService;
    private readonly IOperationsDrillInService? _operationsDrillInService;
    private readonly ITemporalIdentityService _temporalIdentity;
    private readonly IStartupGate _startupGate;
    private readonly IPrivacyStatusService _privacyStatusService;
    private OperationsOverviewSnapshot _operationsSnapshot = new();

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
    [ObservableProperty] private string _appVersion = "1.1.0";
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
    [ObservableProperty] private string _connectorsHeadline = "0";
    [ObservableProperty] private string _connectorsStatus = "No plugins installed";
    [ObservableProperty] private string _connectorsDetail = "Install or enable plugins to bring external data and workflow extensions into the app.";
    [ObservableProperty] private string _workflowHeadline = "0";
    [ObservableProperty] private string _workflowStatus = "Ready to automate";
    [ObservableProperty] private string _workflowRecentActivity = "No recent runs";
    [ObservableProperty] private string _workflowAverageDuration = "Avg duration unavailable";
    [ObservableProperty] private string _workflowDetail = "No workflows available yet.";

    // ── Indexing ─────────────────────────────────────────────
    [ObservableProperty] private int _indexedPercent;
    [ObservableProperty] private int _pendingIndexCount;

    // ── Quick Actions ───────────────────────────────────────
    [ObservableProperty] private string _quickSearchQuery = string.Empty;
    [ObservableProperty] private ObservableCollection<DashboardRecommendedActionItem> _recommendedActions = new();

    // ── Recent Activity ─────────────────────────────────────
    [ObservableProperty] private ObservableCollection<DashboardRecentDocumentItem> _recentDocuments = new();
    [ObservableProperty] private ObservableCollection<DashboardRecentConversationItem> _recentConversations = new();

    // ── Visibility Helpers ──────────────────────────────────
    [ObservableProperty] private bool _hasRecentDocuments;
    [ObservableProperty] private bool _hasRecentConversations;
    [ObservableProperty] private bool _hasFileTypeData;
    [ObservableProperty] private bool _hasCollectionData;
    public bool HasRecommendedActions => RecommendedActions.Count > 0;

    // ── Knowledge Insights ──────────────────────────────────
    [ObservableProperty] private ObservableCollection<DashboardFileTypeBreakdownItem> _fileTypeBreakdown = new();
    [ObservableProperty] private ObservableCollection<DashboardTopCollectionItem> _topCollections = new();

    // ── Temporal Identity: Belief Conflicts ────────────────────
    [ObservableProperty] private ObservableCollection<BeliefConflictDisplayItem> _beliefConflicts = new();
    [ObservableProperty] private bool _hasBeliefConflicts;
    [ObservableProperty] private string _beliefConflictsHeadline = "No conflicts detected";
    [ObservableProperty] private string _beliefConflictsStatus = "Your beliefs are consistent";
    [ObservableProperty] private string _beliefConflictsDetail = "No detected contradictions between your past and current views.";

    // ── Privacy Posture (AX-QA-008) ────────────────────────────
    // State-aware replacement for the former unconditional "no cloud, no exceptions" claim. Driven by
    // IPrivacyStatusService over the user's actual settings; the footer shows the strong local-only
    // assurance only when nothing is configured to leave the machine.
    [ObservableProperty] private bool _isFullyPrivate = true;
    [ObservableProperty] private string _privacyTitle = "100% Private";
    [ObservableProperty]
    private string _privacySummary =
        "All AI processing runs locally on your hardware. Your data never leaves this machine.";
    [ObservableProperty] private ObservableCollection<DashboardPrivacyDisclosureItem> _privacyDisclosures = new();

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
        IOperationsOverviewService operationsOverviewService,
        ITemporalIdentityService temporalIdentity,
        IStartupGate startupGate,
        IPrivacyStatusService privacyStatusService,
        IOperationsDrillInService? operationsDrillInService = null)
    {
        _aiService = aiService;
        _conversationService = conversationService;
        _documentService = documentService;
        _hardwareDetector = hardwareDetector;
        _collectionService = collectionService;
        _indexingService = indexingService;
        _ragPipeline = ragPipeline;
        _operationsOverviewService = operationsOverviewService;
        _temporalIdentity = temporalIdentity;
        _startupGate = startupGate;
        _privacyStatusService = privacyStatusService;
        _operationsDrillInService = operationsDrillInService;
        Log.Debug("DashboardViewModel created with services");
    }

    public async Task InitializeAsync()
    {
        Log.Information("Dashboard initializing...");

        // AX-QA-003 follow-up (dashboard race): MainWindow shows this page's shell immediately —
        // before the awaited startup migration completes — so do NOT touch the database until the
        // migration gate has opened. If startup failed and entered the recovery state the gate is
        // cancelled; skip loading entirely (the app is exiting) rather than query a broken schema.
        try
        {
            await _startupGate.WaitForDataReadyAsync();
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Dashboard initialization skipped — startup did not reach a data-ready state");
            return;
        }

        // Run all data-loading tasks in parallel for faster initialization
        await Task.WhenAll(
            LoadAiStatusAsync(),
            LoadVaultStatsAsync(),
            LoadChatStatsAsync(),
            LoadSystemInfoAsync(),
            LoadRecentActivityAsync(),
            LoadInsightsAsync(),
            LoadIndexingStatusAsync(),
            LoadOperationsOverviewAsync(),
            LoadBeliefConflictsAsync(),
            LoadPrivacyStatusAsync());

        BuildRecommendedActions();

        Log.Information("Dashboard initialized");
    }

    private async Task LoadPrivacyStatusAsync()
    {
        try
        {
            var status = await _privacyStatusService.GetCurrentAsync();

            PrivacyDisclosures.Clear();
            if (status.IsFullyLocal)
            {
                IsFullyPrivate = true;
                PrivacyTitle = "100% Private";
                PrivacySummary =
                    "All AI processing runs locally on your hardware. Your data never leaves this machine.";
            }
            else
            {
                IsFullyPrivate = false;
                PrivacyTitle = "Cloud services active";
                PrivacySummary = "Some features you've enabled send data off this machine:";
                foreach (var disclosure in status.Disclosures)
                {
                    PrivacyDisclosures.Add(new DashboardPrivacyDisclosureItem
                    {
                        Surface = disclosure.Surface,
                        Detail = disclosure.Detail
                    });
                }
            }
        }
        catch (Exception ex)
        {
            // Never silently fall back to the strong "100% private" claim on error — that is exactly
            // the false assurance AX-QA-008 is about. Show an honest, neutral state instead.
            Log.Warning(ex, "Failed to evaluate dashboard privacy status");
            IsFullyPrivate = false;
            PrivacyTitle = "Privacy status unavailable";
            PrivacySummary = "Agent-X couldn't confirm which services are active. Open Settings to review.";
            PrivacyDisclosures.Clear();
        }
    }

    private async Task LoadAiStatusAsync()
    {
        try
        {
            IAiProvider activeProvider;
            try
            {
                activeProvider = _aiService.ActiveProvider;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not been initialized", StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug("Dashboard AI status deferred until AI service initialization completes");
                IsOllamaConnected = false;
                ConnectionStatus = "AI service starting...";
                ActiveModelName = "Initializing...";
                return;
            }

            var connected = await activeProvider.CheckConnectionAsync();
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
            var snapshot = await _operationsOverviewService.GetSnapshotAsync();
            _operationsSnapshot = snapshot;

            ConversationIntelligenceHeadline = snapshot.ConversationIntelligence.Headline;
            ConversationIntelligenceStatus = snapshot.ConversationIntelligence.Status;
            ConversationIntelligenceDetail = snapshot.ConversationIntelligence.Detail;

            SyncHealthHeadline = snapshot.SyncHealth.Headline;
            SyncHealthStatus = snapshot.SyncHealth.Status;
            SyncHealthDetail = snapshot.SyncHealth.Detail;

            InboxHeadline = snapshot.IngestionBacklog.Headline;
            InboxStatus = snapshot.IngestionBacklog.Status;
            InboxDetail = snapshot.IngestionBacklog.Detail;

            ConnectorsHeadline = snapshot.Connectors.Headline;
            ConnectorsStatus = snapshot.Connectors.Status;
            ConnectorsDetail = snapshot.Connectors.Detail;

            WorkflowHeadline = snapshot.WorkflowActivity.Headline;
            WorkflowStatus = snapshot.WorkflowActivity.Status;
            WorkflowRecentActivity = snapshot.WorkflowActivity.SupportingPrimary;
            WorkflowAverageDuration = snapshot.WorkflowActivity.SupportingSecondary;
            WorkflowDetail = snapshot.WorkflowActivity.Detail;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load dashboard operations overview");
            _operationsSnapshot = new OperationsOverviewSnapshot
            {
                ConversationIntelligence = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Durable recall inactive",
                    Detail = "Open Analytics to inspect summary coverage."
                },
                SyncHealth = new OperationsCardSnapshot
                {
                    Headline = "Unavailable",
                    Status = "Sync status unavailable",
                    Detail = "Open Collaborative Sync for details."
                },
                IngestionBacklog = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Queue clear",
                    Detail = "Watch folders and enabled connectors will surface new items here."
                },
                Connectors = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "No plugins installed",
                    Detail = "Open Plugin Manager to enable connectors and extensions."
                },
                WorkflowActivity = new OperationsCardSnapshot
                {
                    Headline = "0",
                    Status = "Ready to automate",
                    SupportingPrimary = "No recent runs",
                    SupportingSecondary = "Avg duration unavailable",
                    Detail = "Open Workflows to create or run automations."
                }
            };
            ConversationIntelligenceHeadline = "0";
            ConversationIntelligenceStatus = "Durable recall inactive";
            ConversationIntelligenceDetail = "Open Analytics to inspect summary coverage.";
            ConnectorsHeadline = "0";
            ConnectorsStatus = "No plugins installed";
            ConnectorsDetail = "Open Plugin Manager to enable connectors and extensions.";
            InboxHeadline = "0";
            InboxStatus = "Queue clear";
            InboxDetail = "Watch folders and enabled connectors will surface new items here.";
            WorkflowHeadline = "0";
            WorkflowStatus = "Ready to automate";
            WorkflowRecentActivity = "No recent runs";
            WorkflowAverageDuration = "Avg duration unavailable";
            WorkflowDetail = "Open Workflows to create or run automations.";
            SyncHealthHeadline = "Unavailable";
            SyncHealthStatus = "Sync status unavailable";
            SyncHealthDetail = "Open Collaborative Sync for details.";
        }
    }

    private void BuildRecommendedActions()
    {
        var items = new List<DashboardRecommendedActionItem>();
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targetInboxItem = _operationsSnapshot.PendingInboxItems.FirstOrDefault(item => item.ItemId > 0);
        var targetImportedDocument = _operationsSnapshot.RecentImportedDocuments.FirstOrDefault(preview =>
            preview.DocumentId > 0 &&
            preview.HealthStatus.Equals("Needs Attention", StringComparison.OrdinalIgnoreCase));
        var targetConnector = _operationsSnapshot.ConnectorPreviews.FirstOrDefault(preview =>
            preview.PluginId > 0 &&
            preview.CanEnableFromOperations);
        var targetWorkflowRun = _operationsSnapshot.RecentWorkflowRuns.FirstOrDefault(preview =>
            preview.WorkflowId > 0 &&
            preview.RunId > 0 &&
            (preview.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
             preview.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)));

        void AddAction(DashboardRecommendedActionItem item)
        {
            if (string.IsNullOrWhiteSpace(item.Route) || !routes.Add(item.Route))
            {
                return;
            }

            items.Add(item);
        }

        if (!IsOllamaConnected)
        {
            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = "Setup",
                IconGlyph = "\uE8BD",
                Title = "Finish local AI setup",
                Detail = "Chat, semantic search, and document intelligence will create more value once a local model is connected.",
                CommandText = "Setup AI",
                Route = "Settings"
            });
        }

        if (PendingIndexCount > 0)
        {
            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = "Attention",
                IconGlyph = "\uE721",
                Title = "Clear the indexing backlog",
                Detail = $"{PendingIndexCount} imported item{(PendingIndexCount == 1 ? string.Empty : "s")} still need indexing review or retry handling.",
                CommandText = targetImportedDocument is null ? "Open Operations" : "Review Document",
                Route = targetImportedDocument is null ? "Operations" : "KnowledgeVault",
                TargetId = targetImportedDocument?.DocumentId ?? 0
            });
        }

        if (TryParsePositiveCount(InboxHeadline, out var inboxCount))
        {
            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = "Attention",
                IconGlyph = "\uE8B7",
                Title = "Triage new incoming content",
                Detail = $"{inboxCount} Smart Inbox item{(inboxCount == 1 ? string.Empty : "s")} are waiting for classification, routing, or preview generation.",
                CommandText = targetInboxItem is null ? "Open Inbox" : "Open Item",
                Route = "Inbox",
                TargetId = targetInboxItem?.ItemId ?? 0
            });
        }

        if (SyncNeedsSetup())
        {
            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = "Setup",
                IconGlyph = "\uE895",
                Title = "Configure workspace sync",
                Detail = "Collaborative sync is not fully ready. Configure it to keep multiple Agent-X installations aligned.",
                CommandText = "Open Sync",
                Route = "SyncSettings"
            });
        }

        if (ConnectorsNeedSetup())
        {
            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = "Expansion",
                IconGlyph = "\uE943",
                Title = "Connect a live source",
                Detail = "Enable plugins and connectors so fresh email, calendar, or external content can flow into the workspace.",
                CommandText = targetConnector is null ? "Open Plugins" : "Open Connector",
                Route = "PluginManager",
                TargetId = targetConnector?.PluginId ?? 0
            });
        }

        if (ConversationIntelligenceNeedsAttention())
        {
            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = "Memory",
                IconGlyph = "\uE9D2",
                Title = "Strengthen durable recall",
                Detail = "Conversation summaries are not yet giving the app enough long-lived memory coverage.",
                CommandText = "Open Analytics",
                Route = "Analytics"
            });
        }

        if (targetWorkflowRun is not null)
        {
            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = "Automation",
                IconGlyph = "\uE8C7",
                Title = $"Review {targetWorkflowRun.Title}",
                Detail = "A recent workflow run failed or was cancelled. Review the run details before trusting that automation again.",
                CommandText = "Review Run",
                Route = "Workflows",
                TargetId = targetWorkflowRun.WorkflowId,
                SecondaryTargetId = targetWorkflowRun.RunId
            });
        }
        else if (WorkflowNeedsSetup())
        {
            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = "Automation",
                IconGlyph = "\uE8C7",
                Title = "Create a repeatable workflow",
                Detail = "Package a recurring task into an automation that can feed results back into the vault.",
                CommandText = "Open Workflows",
                Route = "Workflows"
            });
        }

        if (items.Count < 3)
        {
            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = "Explore",
                IconGlyph = IsOllamaConnected ? "\uE9D9" : "\uE8B5",
                Title = IsOllamaConnected ? "Ask across your vault" : "Import more source material",
                Detail = IsOllamaConnected
                    ? "Use Ask Your Files to turn indexed knowledge into cross-document answers."
                    : "Bring high-value files into the vault so the rest of the intelligence surfaces have more to work with.",
                CommandText = IsOllamaConnected ? "Open Ask Your Files" : "Open Vault",
                Route = IsOllamaConnected ? "AskFiles" : "KnowledgeVault"
            });

            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = "Review",
                IconGlyph = "\uE946",
                Title = "Review system-wide health",
                Detail = "Open Operations for a single place to inspect sync, workflows, connectors, inbox pressure, and recall posture.",
                CommandText = "Open Operations",
                Route = "Operations"
            });

            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = "Insight",
                IconGlyph = "\uE9D2",
                Title = "Review intelligence trends",
                Detail = "Use Analytics to inspect recall coverage, themes, and workflow momentum across the workspace.",
                CommandText = "Open Analytics",
                Route = "Analytics"
            });
        }

        RecommendedActions = new ObservableCollection<DashboardRecommendedActionItem>(items.Take(3));
        OnPropertyChanged(nameof(HasRecommendedActions));
    }

    private bool SyncNeedsSetup() =>
        SyncHealthHeadline.Equals("Not configured", StringComparison.OrdinalIgnoreCase) ||
        SyncHealthHeadline.Equals("Unavailable", StringComparison.OrdinalIgnoreCase) ||
        ContainsAny(SyncHealthStatus, "off", "unavailable", "failed", "conflict", "stale");

    private bool ConnectorsNeedSetup() =>
        ConnectorsHeadline.Equals("0", StringComparison.OrdinalIgnoreCase) ||
        ContainsAny(ConnectorsStatus, "no plugins installed", "no connectors", "disabled");

    private bool ConversationIntelligenceNeedsAttention() =>
        ConversationIntelligenceHeadline.Equals("0", StringComparison.OrdinalIgnoreCase) ||
        ContainsAny(ConversationIntelligenceStatus, "inactive", "needs attention", "stale");

    private bool WorkflowNeedsSetup() =>
        WorkflowHeadline.Equals("0", StringComparison.OrdinalIgnoreCase) ||
        WorkflowRecentActivity.Equals("No recent runs", StringComparison.OrdinalIgnoreCase) ||
        ContainsAny(WorkflowStatus, "ready to automate");

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
    private void NavigateToOperations()
    {
        Log.Debug("Navigate to Operations requested from Dashboard");
        NavigateRequested?.Invoke("Operations");
    }

    [RelayCommand]
    private void NavigateToPluginManager()
    {
        Log.Debug("Navigate to Plugin Manager requested from Dashboard");
        NavigateRequested?.Invoke("PluginManager");
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
    private void OpenRecommendedAction(DashboardRecommendedActionItem? action)
    {
        if (action is null || string.IsNullOrWhiteSpace(action.Route))
        {
            return;
        }

        StageRecommendedActionDrillIn(action);
        Log.Debug("Navigate to {Route} requested from Dashboard recommended actions", action.Route);
        NavigateRequested?.Invoke(action.Route);
    }

    [RelayCommand]
    private Task QuickSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(QuickSearchQuery)) return Task.CompletedTask;
        Log.Debug("Quick search: {Query}", QuickSearchQuery);
        NavigateRequested?.Invoke("Search");
        return Task.CompletedTask;
    }

    private static string FormatCompactNumber(int value) => FormatCompactNumber((long)value);

    private static bool TryParsePositiveCount(string value, out int count)
    {
        if (int.TryParse(value, out count) && count > 0)
        {
            return true;
        }

        count = 0;
        return false;
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatCompactNumber(long value) =>
        value >= 1_000_000 ? $"{value / 1_000_000.0:F1}M"
        : value >= 1_000 ? $"{value / 1_000.0:F1}K"
        : value.ToString();

    private void StageRecommendedActionDrillIn(DashboardRecommendedActionItem action)
    {
        if (_operationsDrillInService is null)
        {
            return;
        }

        var sourceLabel = $"Opened dashboard recommendation \"{action.Title}\"";
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

    // ── Temporal Identity: Belief Conflicts ────────────────────────

    private async Task LoadBeliefConflictsAsync()
    {
        try
        {
            var conflicts = await _temporalIdentity.GetBeliefConflictsAsync();

            if (conflicts.Any())
            {
                BeliefConflicts = new ObservableCollection<BeliefConflictDisplayItem>(
                    conflicts.Take(5).Select(c => new BeliefConflictDisplayItem
                    {
                        Topic = c.Belief?.Topic ?? "Unknown Topic",
                        PreviousStance = c.PreviousStance,
                        CurrentStance = c.CurrentStance,
                        ConflictMagnitude = c.ConflictMagnitude,
                        DetectedAt = c.DetectedAt,
                        HasBeenAcknowledged = c.HasBeenAcknowledged,
                        OriginalConflict = c
                    }));
                HasBeliefConflicts = true;
                BeliefConflictsHeadline = conflicts.Count.ToString();
                BeliefConflictsStatus = "Belief evolution detected";
                BeliefConflictsDetail = $"Your views on {conflicts.Count} topic{(conflicts.Count > 1 ? "s" : "")} have evolved over time.";
            }
            else
            {
                BeliefConflicts = new ObservableCollection<BeliefConflictDisplayItem>();
                HasBeliefConflicts = false;
                BeliefConflictsHeadline = "No conflicts";
                BeliefConflictsStatus = "Your beliefs are consistent";
                BeliefConflictsDetail = "No detected contradictions between your past and current views.";
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load belief conflicts for dashboard");
            BeliefConflicts = new ObservableCollection<BeliefConflictDisplayItem>();
            HasBeliefConflicts = false;
        }
    }

    [RelayCommand]
    private async Task AcknowledgeConflictAsync(BeliefConflictDisplayItem? conflict)
    {
        if (conflict is null) return;

        try
        {
            // Persist the acknowledgement. GetBeliefConflictsAsync filters out acknowledged
            // conflicts at the database level, so without this the dismissed conflict would
            // reappear on the next app launch (KNOWN-ISSUE #8).
            if (conflict.OriginalConflict is not null)
            {
                await _temporalIdentity.AcknowledgeConflictAsync(conflict.OriginalConflict.Id);
                conflict.OriginalConflict.HasBeenAcknowledged = true;
                conflict.OriginalConflict.AcknowledgedAt = DateTime.UtcNow;
            }

            // Remove it from the display
            BeliefConflicts.Remove(conflict);

            if (!BeliefConflicts.Any())
            {
                HasBeliefConflicts = false;
                BeliefConflictsHeadline = "No conflicts";
                BeliefConflictsStatus = "Your beliefs are consistent";
                BeliefConflictsDetail = "All belief conflicts have been acknowledged.";
            }

            Log.Information("Acknowledged belief conflict for topic: {Topic}", conflict.Topic);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to acknowledge belief conflict");
        }
    }

    [RelayCommand]
    private void NavigateToPastSelf()
    {
        Log.Debug("Navigate to Past Self requested from Dashboard");
        NavigateRequested?.Invoke("PastSelf");
    }

    public void Dispose()
    {
        Log.Debug("DashboardViewModel disposed");
    }
}

// ═══════════════════════════════════════════════════════════════════
//  TEMPORAL IDENTITY DISPLAY ITEMS
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Display wrapper for BeliefConflictEntity that includes the Topic from the related Belief.
/// </summary>
public class BeliefConflictDisplayItem
{
    public string Topic { get; set; } = string.Empty;
    public string PreviousStance { get; set; } = string.Empty;
    public string CurrentStance { get; set; } = string.Empty;
    public double ConflictMagnitude { get; set; }
    public DateTime DetectedAt { get; set; }
    public bool HasBeenAcknowledged { get; set; }
    public BeliefConflictEntity? OriginalConflict { get; set; }
}

// ═══════════════════════════════════════════════════════════════════
//  DISPLAY ITEM CLASSES (top-level for x:Bind DataTemplate support)
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// A single privacy disclosure (a feature that sends data off the machine) for display in the
/// dashboard's state-aware privacy footer (AX-QA-008).
/// </summary>
public class DashboardPrivacyDisclosureItem
{
    public string Surface { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

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

/// <summary>
/// Represents a synthesized next-step recommendation shown on the dashboard.
/// </summary>
public class DashboardRecommendedActionItem
{
    public string CategoryLabel { get; init; } = string.Empty;
    public string IconGlyph { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string CommandText { get; init; } = string.Empty;
    public string Route { get; init; } = string.Empty;
    public long TargetId { get; init; }
    public long SecondaryTargetId { get; init; }
}
