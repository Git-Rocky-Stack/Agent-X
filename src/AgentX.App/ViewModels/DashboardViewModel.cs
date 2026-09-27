using System.Collections.ObjectModel;
using System.Globalization;
using AgentX.App.Services;
using AgentX.Core.AI;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Search;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Indexing;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Privacy;
using AgentX.Core.Services.TemporalIdentity;
using AgentX.Core.Services.TemporalIdentity.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

/// <summary>
/// The Dashboard. Every text it builds comes from the localized resources; without a localization
/// service (unit tests), or for a missing resource, the English text is used.
/// </summary>
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
    private readonly ILocalizationService? _localization;
    private OperationsOverviewSnapshot _operationsSnapshot = new();
    // The recommended actions read the typed status of the cards, never their (translated) text.
    private bool _operationsSnapshotUnavailable;

    // ── AI Status ───────────────────────────────────────────
    // IsOllamaConnected is true while the active provider answers, whichever provider it is. It
    // drives the connection card's status dot.
    [ObservableProperty] private bool _isOllamaConnected;
    [ObservableProperty] private string _activeModelName = string.Empty;
    [ObservableProperty] private string _connectionStatus = string.Empty;

    /// <summary>
    /// What to check about the active provider, shown under the connection card only while that
    /// provider cannot be reached. It used to tell everyone to connect Ollama.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProviderAttentionHint))]
    private string _providerAttentionHint = string.Empty;

    public bool HasProviderAttentionHint => !string.IsNullOrEmpty(ProviderAttentionHint);

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
    [ObservableProperty] private string _gpuName = string.Empty;
    [ObservableProperty] private string _availableRam = string.Empty;
    [ObservableProperty] private bool _hasNpu;
    [ObservableProperty] private string _appVersion = "1.1.0";
    [ObservableProperty] private string _totalRamInfo = string.Empty;
    [ObservableProperty] private string _gpuVramInfo = string.Empty;

    // ── Operations Overview ───────────────────────────────────
    [ObservableProperty] private string _conversationIntelligenceHeadline = "0";
    [ObservableProperty] private string _conversationIntelligenceStatus = string.Empty;
    [ObservableProperty] private string _conversationIntelligenceDetail = string.Empty;
    [ObservableProperty] private string _syncHealthHeadline = string.Empty;
    [ObservableProperty] private string _syncHealthStatus = string.Empty;
    [ObservableProperty] private string _syncHealthDetail = string.Empty;
    [ObservableProperty] private string _inboxHeadline = "0";
    [ObservableProperty] private string _inboxStatus = string.Empty;
    [ObservableProperty] private string _inboxDetail = string.Empty;
    [ObservableProperty] private string _connectorsHeadline = "0";
    [ObservableProperty] private string _connectorsStatus = string.Empty;
    [ObservableProperty] private string _connectorsDetail = string.Empty;
    [ObservableProperty] private string _workflowHeadline = "0";
    [ObservableProperty] private string _workflowStatus = string.Empty;
    [ObservableProperty] private string _workflowRecentActivity = string.Empty;
    [ObservableProperty] private string _workflowAverageDuration = string.Empty;
    [ObservableProperty] private string _workflowDetail = string.Empty;

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
    // Empty until the beliefs have been read: "consistent" is claimed only when beliefs exist and
    // none of them conflicts (LoadBeliefConflictsAsync).
    [ObservableProperty] private ObservableCollection<BeliefConflictDisplayItem> _beliefConflicts = new();
    [ObservableProperty] private bool _hasBeliefConflicts;
    [ObservableProperty] private string _beliefConflictsHeadline = string.Empty;
    [ObservableProperty] private string _beliefConflictsStatus = string.Empty;
    [ObservableProperty] private string _beliefConflictsDetail = string.Empty;

    // ── Privacy Posture (AX-QA-008) ────────────────────────────
    // State-aware replacement for the former unconditional "no cloud, no exceptions" claim. Driven by
    // IPrivacyStatusService over the user's actual settings; the footer shows the strong local-only
    // assurance only when nothing is configured to leave the machine.
    [ObservableProperty] private bool _isFullyPrivate = true;
    [ObservableProperty] private string _privacyTitle = string.Empty;
    [ObservableProperty] private string _privacySummary = string.Empty;
    [ObservableProperty] private ObservableCollection<DashboardPrivacyDisclosureItem> _privacyDisclosures = new();

    // ── Navigation ────────────────────────────────────────────
    public NavigateHandler? NavigateRequested { get; set; }

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
        IOperationsDrillInService? operationsDrillInService = null,
        ILocalizationService? localization = null)
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
        _localization = localization;
        ShowPlaceholderTexts();
        Log.Debug("DashboardViewModel created with services");
    }

    /// <summary>What the cards say until their data has been read.</summary>
    private void ShowPlaceholderTexts()
    {
        ActiveModelName = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_NoModelLoaded"), "Dash_NoModelLoaded", "No model loaded");
        ConnectionStatus = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_CheckingConnection"), "Dash_CheckingConnection", "Checking connection...");

        var detecting = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_Detecting"), "Dash_Detecting", "Detecting...");
        GpuName = detecting;
        AvailableRam = detecting;
        TotalRamInfo = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_TotalRam", "-- GB"), "Dash_TotalRam", "{0} total", "-- GB");
        GpuVramInfo = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_GpuVram", "--"), "Dash_GpuVram", "{0} VRAM", "--");

        ConversationIntelligenceStatus = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_RecallInactive"), "Dash_RecallInactive", "Durable recall inactive");
        ConversationIntelligenceDetail = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_RecallNoSummaries"), "Dash_RecallNoSummaries", "No stored conversation summaries yet.");
        SyncHealthHeadline = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_SyncNotConfigured"), "Dash_SyncNotConfigured", "Not configured");
        SyncHealthStatus = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_SyncOff"), "Dash_SyncOff", "Collaborative sync is off");
        SyncHealthDetail = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_SyncConfigureHint"),
            "Dash_SyncConfigureHint",
            "Configure a shared folder to synchronize multiple installations.");
        InboxStatus = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_InboxQueueClear"), "Dash_InboxQueueClear", "Queue clear");
        InboxDetail = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_InboxNoItems"), "Dash_InboxNoItems", "No items awaiting triage.");
        ConnectorsStatus = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_NoPluginsInstalled"), "Dash_NoPluginsInstalled", "No plugins installed");
        ConnectorsDetail = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_ConnectorsInstallHint"),
            "Dash_ConnectorsInstallHint",
            "Install or enable plugins to bring external data and workflow extensions into the app.");
        WorkflowStatus = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_WorkflowReady"), "Dash_WorkflowReady", "Ready to automate");
        WorkflowRecentActivity = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_WorkflowNoRecentRuns"), "Dash_WorkflowNoRecentRuns", "No recent runs");
        WorkflowAverageDuration = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_WorkflowAvgUnavailable"), "Dash_WorkflowAvgUnavailable", "Avg duration unavailable");
        WorkflowDetail = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_WorkflowNone"), "Dash_WorkflowNone", "No workflows available yet.");

        ShowFullyLocalPrivacy();
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

    private void ShowFullyLocalPrivacy()
    {
        IsFullyPrivate = true;
        PrivacyTitle = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_PrivacyLocalTitle"), "Dash_PrivacyLocalTitle", "100% Private");
        PrivacySummary = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_PrivacyLocalSummary"),
            "Dash_PrivacyLocalSummary",
            "All AI processing runs locally on your hardware. Your data never leaves this machine.");
    }

    private async Task LoadPrivacyStatusAsync()
    {
        try
        {
            var status = await _privacyStatusService.GetCurrentAsync();

            PrivacyDisclosures.Clear();
            if (status.IsFullyLocal)
            {
                ShowFullyLocalPrivacy();
            }
            else
            {
                IsFullyPrivate = false;
                PrivacyTitle = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_PrivacyCloudTitle"), "Dash_PrivacyCloudTitle", "Cloud services active");
                PrivacySummary = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_PrivacyCloudSummary"),
                    "Dash_PrivacyCloudSummary",
                    "Some features you've enabled send data off this machine:");
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
            PrivacyTitle = ProviderStatusText.Resolve(
                _localization?.GetString("Dash_PrivacyUnavailableTitle"),
                "Dash_PrivacyUnavailableTitle",
                "Privacy status unavailable");
            PrivacySummary = ProviderStatusText.Resolve(
                _localization?.GetString("Dash_PrivacyUnavailableSummary"),
                "Dash_PrivacyUnavailableSummary",
                "Agent-X couldn't confirm which services are active. Open Settings to review.");
            PrivacyDisclosures.Clear();
        }
    }

    private async Task LoadAiStatusAsync()
    {
        // The status names the active provider (the built-in model is the default), not Ollama.
        var providerName = ProviderStatusText.GenericName(_localization);
        string? providerId = null;
        var setupRequired = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_SetupRequired"), "Dash_SetupRequired", "Setup required");
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
                ConnectionStatus = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_AiStarting"), "Dash_AiStarting", "AI service starting...");
                ActiveModelName = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_Initializing"), "Dash_Initializing", "Initializing...");
                ProviderAttentionHint = string.Empty;
                return;
            }

            providerId = activeProvider.ProviderId;
            if (!string.IsNullOrWhiteSpace(activeProvider.DisplayName))
            {
                providerName = activeProvider.DisplayName;
            }

            var connected = await activeProvider.CheckConnectionAsync();
            IsOllamaConnected = connected;
            ConnectionStatus = connected
                ? ProviderStatusText.ConnectedTo(_localization, providerName)
                : ProviderStatusText.NotAvailable(_localization, providerName);
            ActiveModelName = connected && !string.IsNullOrEmpty(_aiService.ActiveModelId)
                ? _aiService.ActiveModelId
                : setupRequired;

            // Only a provider that cannot be reached needs attention, and the advice is for it.
            ProviderAttentionHint = connected
                ? string.Empty
                : ProviderStatusText.CheckHint(_localization, providerId, providerName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check AI connection status for dashboard");
            IsOllamaConnected = false;
            ConnectionStatus = ProviderStatusText.NotAvailable(_localization, providerName);
            ActiveModelName = setupRequired;
            ProviderAttentionHint = ProviderStatusText.CheckHint(_localization, providerId, providerName);
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

    /// <summary>
    /// The system card, in the user's language. Core formats sizes but words nothing, so the words
    /// around them and the texts for values Windows did not report come from the resources, the
    /// same ones the Hardware Advisor uses where the text is the same.
    /// </summary>
    private async Task LoadSystemInfoAsync()
    {
        try
        {
            var hw = await _hardwareDetector.DetectAsync();
            var ramNotDetected = ProviderStatusText.Resolve(
                _localization?.GetString("HwAdvisor_RamNotDetected"), "HwAdvisor_RamNotDetected", "Not detected");

            GpuName = IsHardwarePlaceholder(hw.GpuName)
                ? ProviderStatusText.Resolve(
                    _localization?.GetString("HwAdvisor_GpuNotDetected"), "HwAdvisor_GpuNotDetected", "GPU not detected")
                : hw.GpuName.Trim();
            AvailableRam = hw.TotalRamBytes > 0 ? hw.AvailableRamFormatted : ramNotDetected;
            HasNpu = hw.HasNpu;
            TotalRamInfo = hw.TotalRamBytes > 0
                ? ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_TotalRam", hw.TotalRamFormatted), "Dash_TotalRam", "{0} total", hw.TotalRamFormatted)
                : ramNotDetected;
            GpuVramInfo = hw.GpuVramBytes > 0
                ? ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_GpuVram", hw.GpuVramFormatted), "Dash_GpuVram", "{0} VRAM", hw.GpuVramFormatted)
                : ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_IntegratedGpu"), "Dash_IntegratedGpu", "Integrated GPU");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to detect hardware for dashboard");
            var unknown = ProviderStatusText.Resolve(
                _localization?.GetString("HwAdvisor_Unknown"), "HwAdvisor_Unknown", "Unknown");
            GpuName = ProviderStatusText.Resolve(
                _localization?.GetString("HwAdvisor_DetectionFailed"), "HwAdvisor_DetectionFailed", "Detection failed");
            AvailableRam = unknown;
            HasNpu = false;
            TotalRamInfo = unknown;
            GpuVramInfo = unknown;
        }
    }

    /// <summary>
    /// True for an empty GPU name or one of the placeholders detection reports when a read fails or
    /// is blocked, which the Hardware Advisor also treats as "not detected".
    /// </summary>
    private static bool IsHardwarePlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;

        var v = value.Trim();
        return v.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Unknown GPU", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Detection failed", StringComparison.OrdinalIgnoreCase)
            || v.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase);
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

            var untitled = ProviderStatusText.Resolve(
                _localization?.GetString("Dash_UntitledConversation"), "Dash_UntitledConversation", "Untitled Conversation");
            var conversations = await _conversationService.GetRecentConversationsAsync(5);
            var recentConvos = conversations.Take(5).Select(c => new DashboardRecentConversationItem
            {
                Id = c.Id,
                Title = string.IsNullOrWhiteSpace(c.Title) ? untitled : c.Title,
                Preview = c.MessageCount == 1
                    ? ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_ConversationMessagesOne"), "Dash_ConversationMessagesOne", "1 message")
                    : ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_ConversationMessagesMany", c.MessageCount),
                        "Dash_ConversationMessagesMany",
                        "{0} messages",
                        c.MessageCount),
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
            var colors = new[] { "#AA2024", "#58C4BC", "#41E25E", "#FFB000", "#E6E6E6", "#E0252B", "#B3B3B3", "#7F171A" };
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
                BarWidthPercent = c.DocumentCount * 100.0 / maxDocCount,
                CountLabel = c.DocumentCount == 1
                    ? ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_CollectionDocsOne"), "Dash_CollectionDocsOne", "1 doc")
                    : ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_CollectionDocsMany", c.DocumentCount),
                        "Dash_CollectionDocsMany",
                        "{0} docs",
                        c.DocumentCount)
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
            // Report that the state is unknown rather than claiming everything is indexed.
            // Showing "Idle" at 100% here is a green light for a state we never observed,
            // and it hides a genuinely stalled or failing index behind a healthy reading.
            Log.Warning(ex, "Failed to load indexing status for dashboard");
            PendingIndexCount = 0;
            IndexedPercent = 0;
            IndexingStatus = "Status unavailable";
        }
    }

    private async Task LoadOperationsOverviewAsync()
    {
        try
        {
            ApplyOperationsSnapshot(await _operationsOverviewService.GetSnapshotAsync());
            _operationsSnapshotUnavailable = false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load dashboard operations overview");
            ApplyOperationsSnapshot(BuildUnavailableOperationsSnapshot());
            _operationsSnapshotUnavailable = true;
        }
    }

    private void ApplyOperationsSnapshot(OperationsOverviewSnapshot snapshot)
    {
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

    /// <summary>What the operations cards say when the overview could not be read.</summary>
    private OperationsOverviewSnapshot BuildUnavailableOperationsSnapshot()
    {
        var queueClear = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_InboxQueueClear"), "Dash_InboxQueueClear", "Queue clear");
        var noPlugins = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_NoPluginsInstalled"), "Dash_NoPluginsInstalled", "No plugins installed");

        return new OperationsOverviewSnapshot
        {
            ConversationIntelligence = new OperationsCardSnapshot
            {
                Headline = "0",
                StatusKind = OperationsStatusKind.RecallInactive,
                Status = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_RecallInactive"), "Dash_RecallInactive", "Durable recall inactive"),
                Detail = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_RecallOpenAnalytics"),
                    "Dash_RecallOpenAnalytics",
                    "Open Analytics to inspect summary coverage.")
            },
            SyncHealth = new OperationsCardSnapshot
            {
                Headline = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_SyncUnavailableHeadline"), "Dash_SyncUnavailableHeadline", "Unavailable"),
                Status = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_SyncStatusUnavailable"), "Dash_SyncStatusUnavailable", "Sync status unavailable"),
                Detail = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_SyncOpenForDetails"),
                    "Dash_SyncOpenForDetails",
                    "Open Collaborative Sync for details.")
            },
            IngestionBacklog = new OperationsCardSnapshot
            {
                Headline = "0",
                StatusKind = OperationsStatusKind.BacklogClear,
                Status = queueClear,
                Detail = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_InboxWatchFoldersHint"),
                    "Dash_InboxWatchFoldersHint",
                    "Watch folders and enabled connectors will surface new items here.")
            },
            Connectors = new OperationsCardSnapshot
            {
                Headline = "0",
                StatusKind = OperationsStatusKind.NoPluginsInstalled,
                Status = noPlugins,
                Detail = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ConnectorsOpenPluginManager"),
                    "Dash_ConnectorsOpenPluginManager",
                    "Open Plugin Manager to enable connectors and extensions.")
            },
            WorkflowActivity = new OperationsCardSnapshot
            {
                Headline = "0",
                StatusKind = OperationsStatusKind.WorkflowReadyToAutomate,
                SupportingPrimaryKind = OperationsStatusKind.WorkflowsNoRecentRuns,
                Status = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_WorkflowReady"), "Dash_WorkflowReady", "Ready to automate"),
                SupportingPrimary = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_WorkflowNoRecentRuns"), "Dash_WorkflowNoRecentRuns", "No recent runs"),
                SupportingSecondary = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_WorkflowAvgUnavailable"), "Dash_WorkflowAvgUnavailable", "Avg duration unavailable"),
                Detail = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_WorkflowOpenHint"),
                    "Dash_WorkflowOpenHint",
                    "Open Workflows to create or run automations.")
            }
        };
    }

    private void BuildRecommendedActions()
    {
        var items = new List<DashboardRecommendedActionItem>();
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targetInboxItem = _operationsSnapshot.PendingInboxItems.FirstOrDefault(item => item.ItemId > 0);
        var targetImportedDocument = _operationsSnapshot.RecentImportedDocuments.FirstOrDefault(preview =>
            preview.DocumentId > 0 &&
            preview.Health == OperationsDocumentHealth.NeedsAttention);
        var targetConnector = _operationsSnapshot.ConnectorPreviews.FirstOrDefault(preview =>
            preview.PluginId > 0 &&
            preview.CanEnableFromOperations);
        var targetWorkflowRun = _operationsSnapshot.RecentWorkflowRuns.FirstOrDefault(preview =>
            preview.WorkflowId > 0 &&
            preview.RunId > 0 &&
            preview.NeedsReview);

        void AddAction(DashboardRecommendedActionItem item)
        {
            if (string.IsNullOrWhiteSpace(item.Route) || !routes.Add(item.Route))
            {
                return;
            }

            items.Add(item);
        }

        var categorySetup = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_ActionCategorySetup"), "Dash_ActionCategorySetup", "Setup");
        var categoryAttention = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_ActionCategoryAttention"), "Dash_ActionCategoryAttention", "Attention");
        var categoryAutomation = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_ActionCategoryAutomation"), "Dash_ActionCategoryAutomation", "Automation");
        var openOperations = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_ActionOpenOperations"), "Dash_ActionOpenOperations", "Open Operations");
        var openAnalytics = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_ActionOpenAnalytics"), "Dash_ActionOpenAnalytics", "Open Analytics");

        if (!IsOllamaConnected)
        {
            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = categorySetup,
                IconGlyph = "",
                Title = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionAiSetupTitle"), "Dash_ActionAiSetupTitle", "Finish local AI setup"),
                Detail = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionAiSetupDetail"),
                    "Dash_ActionAiSetupDetail",
                    "Chat, semantic search, and document intelligence will create more value once a local model is connected."),
                CommandText = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionAiSetupCommand"), "Dash_ActionAiSetupCommand", "Setup AI"),
                Route = "Settings"
            });
        }

        if (PendingIndexCount > 0)
        {
            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = categoryAttention,
                IconGlyph = "",
                Title = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionIndexingTitle"), "Dash_ActionIndexingTitle", "Clear the indexing backlog"),
                Detail = PendingIndexCount == 1
                    ? ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_ActionIndexingDetailOne"),
                        "Dash_ActionIndexingDetailOne",
                        "1 imported item still needs indexing review or retry handling.")
                    : ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_ActionIndexingDetailMany", PendingIndexCount),
                        "Dash_ActionIndexingDetailMany",
                        "{0} imported items still need indexing review or retry handling.",
                        PendingIndexCount),
                CommandText = targetImportedDocument is null
                    ? openOperations
                    : ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_ActionReviewDocument"), "Dash_ActionReviewDocument", "Review Document"),
                Route = targetImportedDocument is null ? "Operations" : "KnowledgeVault",
                TargetId = targetImportedDocument?.DocumentId ?? 0
            });
        }

        if (TryParsePositiveCount(InboxHeadline, out var inboxCount))
        {
            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = categoryAttention,
                IconGlyph = "",
                Title = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionTriageTitle"), "Dash_ActionTriageTitle", "Triage new incoming content"),
                Detail = inboxCount == 1
                    ? ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_ActionTriageDetailOne"),
                        "Dash_ActionTriageDetailOne",
                        "1 Smart Inbox item is waiting for classification, routing, or preview generation.")
                    : ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_ActionTriageDetailMany", inboxCount),
                        "Dash_ActionTriageDetailMany",
                        "{0} Smart Inbox items are waiting for classification, routing, or preview generation.",
                        inboxCount),
                CommandText = targetInboxItem is null
                    ? ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_ActionOpenInbox"), "Dash_ActionOpenInbox", "Open Inbox")
                    : ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_ActionOpenItem"), "Dash_ActionOpenItem", "Open Item"),
                Route = "Inbox",
                TargetId = targetInboxItem?.ItemId ?? 0
            });
        }

        if (SyncNeedsSetup())
        {
            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = categorySetup,
                IconGlyph = "",
                Title = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionSyncTitle"), "Dash_ActionSyncTitle", "Configure workspace sync"),
                Detail = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionSyncDetail"),
                    "Dash_ActionSyncDetail",
                    "Collaborative sync is not fully ready. Configure it to keep multiple Agent-X installations aligned."),
                CommandText = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionOpenSync"), "Dash_ActionOpenSync", "Open Sync"),
                Route = "SyncSettings"
            });
        }

        if (ConnectorsNeedSetup())
        {
            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionCategoryExpansion"), "Dash_ActionCategoryExpansion", "Expansion"),
                IconGlyph = "",
                Title = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionConnectTitle"), "Dash_ActionConnectTitle", "Connect a live source"),
                Detail = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionConnectDetail"),
                    "Dash_ActionConnectDetail",
                    "Enable plugins and connectors so fresh email, calendar, or external content can flow into the workspace."),
                CommandText = targetConnector is null
                    ? ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_ActionOpenPlugins"), "Dash_ActionOpenPlugins", "Open Plugins")
                    : ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_ActionOpenConnector"), "Dash_ActionOpenConnector", "Open Connector"),
                Route = "PluginManager",
                TargetId = targetConnector?.PluginId ?? 0
            });
        }

        if (ConversationIntelligenceNeedsAttention())
        {
            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionCategoryMemory"), "Dash_ActionCategoryMemory", "Memory"),
                IconGlyph = "",
                Title = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionRecallTitle"), "Dash_ActionRecallTitle", "Strengthen durable recall"),
                Detail = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionRecallDetail"),
                    "Dash_ActionRecallDetail",
                    "Conversation summaries are not yet giving the app enough long-lived memory coverage."),
                CommandText = openAnalytics,
                Route = "Analytics"
            });
        }

        if (targetWorkflowRun is not null)
        {
            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = categoryAutomation,
                IconGlyph = "",
                Title = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionReviewRunTitle", targetWorkflowRun.Title),
                    "Dash_ActionReviewRunTitle",
                    "Review {0}",
                    targetWorkflowRun.Title),
                Detail = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionReviewRunDetail"),
                    "Dash_ActionReviewRunDetail",
                    "A recent workflow run failed or was cancelled. Review the run details before trusting that automation again."),
                CommandText = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionReviewRun"), "Dash_ActionReviewRun", "Review Run"),
                Route = "Workflows",
                TargetId = targetWorkflowRun.WorkflowId,
                SecondaryTargetId = targetWorkflowRun.RunId
            });
        }
        else if (WorkflowNeedsSetup())
        {
            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = categoryAutomation,
                IconGlyph = "",
                Title = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionWorkflowTitle"), "Dash_ActionWorkflowTitle", "Create a repeatable workflow"),
                Detail = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionWorkflowDetail"),
                    "Dash_ActionWorkflowDetail",
                    "Package a recurring task into an automation that can feed results back into the vault."),
                CommandText = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionOpenWorkflows"), "Dash_ActionOpenWorkflows", "Open Workflows"),
                Route = "Workflows"
            });
        }

        if (items.Count < 3)
        {
            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionCategoryExplore"), "Dash_ActionCategoryExplore", "Explore"),
                IconGlyph = IsOllamaConnected ? "" : "",
                Title = IsOllamaConnected
                    ? ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_ActionAskTitle"), "Dash_ActionAskTitle", "Ask across your vault")
                    : ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_ActionImportTitle"), "Dash_ActionImportTitle", "Import more source material"),
                Detail = IsOllamaConnected
                    ? ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_ActionAskDetail"),
                        "Dash_ActionAskDetail",
                        "Use Ask Your Files to turn indexed knowledge into cross-document answers.")
                    : ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_ActionImportDetail"),
                        "Dash_ActionImportDetail",
                        "Bring high-value files into the vault so the rest of the intelligence surfaces have more to work with."),
                CommandText = IsOllamaConnected
                    ? ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_ActionOpenAskFiles"), "Dash_ActionOpenAskFiles", "Open Ask Your Files")
                    : ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_ActionOpenVault"), "Dash_ActionOpenVault", "Open Vault"),
                Route = IsOllamaConnected ? "AskFiles" : "KnowledgeVault"
            });

            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionCategoryReview"), "Dash_ActionCategoryReview", "Review"),
                IconGlyph = "",
                Title = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionHealthTitle"), "Dash_ActionHealthTitle", "Review system-wide health"),
                Detail = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionHealthDetail"),
                    "Dash_ActionHealthDetail",
                    "Open Operations for a single place to inspect sync, workflows, connectors, inbox pressure, and recall posture."),
                CommandText = openOperations,
                Route = "Operations"
            });

            AddAction(new DashboardRecommendedActionItem
            {
                CategoryLabel = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionCategoryInsight"), "Dash_ActionCategoryInsight", "Insight"),
                IconGlyph = "",
                Title = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionTrendsTitle"), "Dash_ActionTrendsTitle", "Review intelligence trends"),
                Detail = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_ActionTrendsDetail"),
                    "Dash_ActionTrendsDetail",
                    "Use Analytics to inspect recall coverage, themes, and workflow momentum across the workspace."),
                CommandText = openAnalytics,
                Route = "Analytics"
            });
        }

        RecommendedActions = new ObservableCollection<DashboardRecommendedActionItem>(items.Take(3));
        OnPropertyChanged(nameof(HasRecommendedActions));
    }

    // Typed status, not the card text: the text follows the UI language. Headlines are counts,
    // so "0" reads the same in every language.
    private bool SyncNeedsSetup() =>
        _operationsSnapshotUnavailable ||
        _operationsSnapshot.SyncHealth.StatusKind is OperationsStatusKind.SyncNotConfigured
            or OperationsStatusKind.SyncConflict
            or OperationsStatusKind.SyncError;

    private bool ConnectorsNeedSetup() =>
        _operationsSnapshot.Connectors.Headline.Equals("0", StringComparison.OrdinalIgnoreCase) ||
        _operationsSnapshot.Connectors.StatusKind is OperationsStatusKind.NoPluginsInstalled
            or OperationsStatusKind.PluginsInstalled;

    private bool ConversationIntelligenceNeedsAttention() =>
        _operationsSnapshot.ConversationIntelligence.Headline.Equals("0", StringComparison.OrdinalIgnoreCase) ||
        _operationsSnapshot.ConversationIntelligence.StatusKind is OperationsStatusKind.RecallInactive
            or OperationsStatusKind.RecallStaleSummaries;

    private bool WorkflowNeedsSetup() =>
        _operationsSnapshot.WorkflowActivity.Headline.Equals("0", StringComparison.OrdinalIgnoreCase) ||
        _operationsSnapshot.WorkflowActivity.SupportingPrimaryKind == OperationsStatusKind.WorkflowsNoRecentRuns ||
        _operationsSnapshot.WorkflowActivity.StatusKind == OperationsStatusKind.WorkflowReadyToAutomate;

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

    /// <summary>
    /// The New Chat tile starts a conversation the way Ctrl+N and the palette's New Conversation
    /// do. A plain navigation reopened whatever thread the cached Chat page had open.
    /// </summary>
    [RelayCommand]
    private void StartNewChat()
    {
        Log.Debug("New chat requested from Dashboard");
        NavigateRequested?.Invoke("Chat", NavigationIntents.NewConversation);
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

    /// <summary>
    /// Hands the dashboard search box's query to the Search page. The query travels as the
    /// navigation payload so the user lands on results rather than an empty search box.
    /// </summary>
    [RelayCommand]
    private void QuickSearch()
    {
        var query = QuickSearchQuery?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        Log.Debug("Quick search: {Query}", query);
        NavigateRequested?.Invoke("Search", query);
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

        var sourceLabel = ProviderStatusText.Resolve(
            _localization?.GetString("Dash_DrillInSourceLabel", action.Title),
            "Dash_DrillInSourceLabel",
            "Opened dashboard recommendation \"{0}\"",
            action.Title);
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

    /// <summary>
    /// Days back <see cref="ITemporalIdentityService.GetActiveTopicsAsync"/> looks for beliefs when
    /// the card asks whether any were recorded at all.
    /// </summary>
    private const int AllRecordedBeliefsDays = 36_500;

    private async Task LoadBeliefConflictsAsync()
    {
        try
        {
            var conflicts = await _temporalIdentity.GetBeliefConflictsAsync();

            if (conflicts.Any())
            {
                var unknownTopic = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_UnknownTopic"), "Dash_UnknownTopic", "Unknown Topic");
                BeliefConflicts = new ObservableCollection<BeliefConflictDisplayItem>(
                    conflicts.Take(5).Select(c => new BeliefConflictDisplayItem
                    {
                        Topic = c.Belief?.Topic ?? unknownTopic,
                        PreviousStance = c.PreviousStance,
                        CurrentStance = c.CurrentStance,
                        ConflictMagnitude = c.ConflictMagnitude,
                        DetectedAt = c.DetectedAt,
                        HasBeenAcknowledged = c.HasBeenAcknowledged,
                        OriginalConflict = c
                    }));
                HasBeliefConflicts = true;
                BeliefConflictsHeadline = conflicts.Count.ToString(CultureInfo.CurrentCulture);
                BeliefConflictsStatus = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_BeliefEvolvedStatus"),
                    "Dash_BeliefEvolvedStatus",
                    "Belief evolution detected");
                BeliefConflictsDetail = conflicts.Count == 1
                    ? ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_BeliefEvolvedDetailOne"),
                        "Dash_BeliefEvolvedDetailOne",
                        "Your view on 1 topic has evolved over time.")
                    : ProviderStatusText.Resolve(
                        _localization?.GetString("Dash_BeliefEvolvedDetailMany", conflicts.Count),
                        "Dash_BeliefEvolvedDetailMany",
                        "Your views on {0} topics have evolved over time.",
                        conflicts.Count);
                return;
            }

            BeliefConflicts = new ObservableCollection<BeliefConflictDisplayItem>();
            HasBeliefConflicts = false;
            BeliefConflictsHeadline = "0";

            // "Consistent" needs something to compare. With no belief recorded nothing was checked,
            // which the card used to report as "Your beliefs are consistent". The query lists every
            // belief observed since it was stamped; the only rows it misses are beliefs seen once
            // before LastObservedAt was set on creation, and a view seen once has nothing to be
            // compared with either, which is what the card says.
            var recordedTopics = await _temporalIdentity.GetActiveTopicsAsync(days: AllRecordedBeliefsDays);
            if (recordedTopics is not { Count: > 0 })
            {
                BeliefConflictsStatus = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_BeliefNoneStatus"),
                    "Dash_BeliefNoneStatus",
                    "No beliefs to compare yet");
                BeliefConflictsDetail = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_BeliefNoneDetail"),
                    "Dash_BeliefNoneDetail",
                    "Agent-X has not recorded your views on any topic more than once, so there is nothing to compare yet.");
                return;
            }

            BeliefConflictsStatus = ProviderStatusText.Resolve(
                _localization?.GetString("Dash_BeliefConsistentStatus"),
                "Dash_BeliefConsistentStatus",
                "Your beliefs are consistent");
            BeliefConflictsDetail = ProviderStatusText.Resolve(
                _localization?.GetString("Dash_BeliefConsistentDetail"),
                "Dash_BeliefConsistentDetail",
                "No detected contradictions between your past and current views.");
        }
        catch (Exception ex)
        {
            // Unknown is not consistent: say the history could not be read.
            Log.Warning(ex, "Failed to load belief conflicts for dashboard");
            BeliefConflicts = new ObservableCollection<BeliefConflictDisplayItem>();
            HasBeliefConflicts = false;
            BeliefConflictsHeadline = string.Empty;
            BeliefConflictsStatus = ProviderStatusText.Resolve(
                _localization?.GetString("Dash_BeliefUnavailableStatus"),
                "Dash_BeliefUnavailableStatus",
                "Belief status unavailable");
            BeliefConflictsDetail = ProviderStatusText.Resolve(
                _localization?.GetString("Dash_BeliefUnavailableDetail"),
                "Dash_BeliefUnavailableDetail",
                "Agent-X could not load your belief history.");
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
                // Acknowledged is not consistent: the views did change.
                HasBeliefConflicts = false;
                BeliefConflictsHeadline = "0";
                BeliefConflictsStatus = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_BeliefAcknowledgedStatus"),
                    "Dash_BeliefAcknowledgedStatus",
                    "No open conflicts");
                BeliefConflictsDetail = ProviderStatusText.Resolve(
                    _localization?.GetString("Dash_BeliefAcknowledgedDetail"),
                    "Dash_BeliefAcknowledgedDetail",
                    "All belief conflicts have been acknowledged.");
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
        "pdf" => "",
        "docx" or "doc" => "",
        "txt" => "",
        "md" => "",
        "cs" or "py" or "js" or "ts" => "",
        "png" or "jpg" or "jpeg" or "gif" => "",
        _ => ""
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

    /// <summary>The document count in words, set by the view model in the user's language.</summary>
    public string CountLabel { get; init; } = string.Empty;
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
