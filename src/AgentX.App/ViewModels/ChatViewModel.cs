using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using AgentX.App.Helpers;
using AgentX.App.Services;
using AgentX.App.ViewModels.Coordinators;
using AgentX.App.Views;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Audio;
using AgentX.Core.Services.Audio.Models;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Chat.Models;
using AgentX.Core.Services.Feedback;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Privacy;
using AgentX.Core.Services.TemporalIdentity;
using AgentX.Core.Services.TemporalIdentity.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.Wave;
using Serilog;

namespace AgentX.App.ViewModels;

// ═══════════════════════════════════════════════════════════════════════════
// CHAT VIEW MODEL — Thin orchestrator that delegates to 4 coordinators.
//
// ConversationCoordinator — CRUD, pinning, folders, search
// MessagingCoordinator   — send, stream, stop, feedback, delete messages
// VoiceCoordinator       — recording, transcription
// BranchingCoordinator   — branch, merge, delete branches
//
// The ViewModel retains UI state (ObservableProperties, Collections) and
// subscribes to coordinator events for synchronization.
// ═══════════════════════════════════════════════════════════════════════════

public partial class ChatViewModel : ObservableObject, IDisposable
{
    // ── Page State ─────────────────────────────────────────────
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private string _activeModelName = "No model selected";
    [ObservableProperty] private string _connectionStatus = "Disconnected";
    [ObservableProperty] private string _userInput = string.Empty;
    [ObservableProperty] private string _currentStreamingResponse = string.Empty;

    // ── Active Conversation ────────────────────────────────────
    [ObservableProperty] private long? _activeConversationId;
    [ObservableProperty] private string _activeConversationTitle = "New Conversation";
    [ObservableProperty] private string? _activeSystemPrompt;
    [ObservableProperty] private string? _activeSystemPromptName;
    [ObservableProperty] private int _tokenCount;
    [ObservableProperty] private double _generationTimeMs;

    // ── Panel State ────────────────────────────────────────────
    [ObservableProperty] private bool _isConversationPaneOpen = true;
    [ObservableProperty] private bool _showSystemPromptPicker;
    [ObservableProperty] private bool _isContextInspectorOpen;

    // ── Research Mode ──────────────────────────────────────────
    private bool _isResearchMode;
    public bool IsResearchMode
    {
        get => _isResearchMode;
        set
        {
            if (SetProperty(ref _isResearchMode, value))
            {
                OnPropertyChanged(nameof(ResearchModeTooltip));

                // Research Mode decides whether the next message also goes to the web search provider.
                _ = RefreshPrivacyClaimAsync();
            }
        }
    }

    public string ResearchModeTooltip => IsResearchMode
        ? "Research Mode ON: answers add cited web search results (needs Research Mode and a web search provider in Settings)"
        : "Research Mode OFF: answers use your local knowledge only";

    // ── Orchestration Mode ───────────────────────────────────────
    [ObservableProperty] private ChatOrchestrationMode _orchestrationMode = ChatOrchestrationMode.Standard;

    public int OrchestrationModeIndex
    {
        get => (int)OrchestrationMode;
        set
        {
            var normalizedMode = value switch
            {
                1 => ChatOrchestrationMode.MultiAgentParallel,
                2 => ChatOrchestrationMode.MultiAgentDebate,
                _ => ChatOrchestrationMode.Standard
            };

            if (OrchestrationMode != normalizedMode)
            {
                OrchestrationMode = normalizedMode;
            }
        }
    }

    public string OrchestrationModeTooltip => OrchestrationMode switch
    {
        ChatOrchestrationMode.MultiAgentParallel => "Multi-agent parallel mode — researcher, critic, and synthesizer respond together",
        ChatOrchestrationMode.MultiAgentDebate => "Multi-agent debate mode — agents challenge positions before the final synthesis",
        _ => "Solo mode — standard Agent-X chat response"
    };

    // ── Search ─────────────────────────────────────────────────
    [ObservableProperty] private string _conversationSearchQuery = string.Empty;

    // ── Memory ────────────────────────────────────────────────
    [ObservableProperty] private int _memoryCount;

    // ── Privacy claim (empty chat) ────────────────────────────
    // The empty chat claims "100% Private" only while nothing a message sends leaves this
    // computer; otherwise its hint names where messages go instead.
    [ObservableProperty] private bool _isChatPrivate;
    [ObservableProperty] private string _privacyHint = string.Empty;
    private int _privacyClaimVersion;

    // ── Voice Input ───────────────────────────────────────────
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isTranscribing;
    [ObservableProperty] private string _voiceStatusMessage = string.Empty;

    // ── Context Inspector ─────────────────────────────────────
    [ObservableProperty] private bool _hasContextInspection;
    [ObservableProperty] private bool _hasLimitedContextInspection;
    [ObservableProperty] private string _contextInspectionStatus = "No generation context captured yet.";
    [ObservableProperty] private string _contextCapturedAt = "No context captured";
    [ObservableProperty] private string _contextStoryText = string.Empty;
    [ObservableProperty] private ObservableCollection<ChatContextStorySourceDisplayItem> _contextStorySourceChips = new();
    [ObservableProperty] private string _contextSelectedMessages = "0";
    [ObservableProperty] private string _contextAnchorMessages = "0";
    [ObservableProperty] private string _contextOverflowMessages = "0";
    [ObservableProperty] private string _contextEstimatedPromptTokens = "0";
    [ObservableProperty] private string _contextEstimatedMessageTokens = "0";
    [ObservableProperty] private string _contextAssemblyMode = "No context available";
    [ObservableProperty] private string _contextAssemblyExplanation = string.Empty;
    [ObservableProperty] private string _contextCompressionExplanation = string.Empty;
    [ObservableProperty] private string _contextRecallExplanation = string.Empty;
    [ObservableProperty] private string _contextSummaryStatus = "No durable summary captured yet.";
    [ObservableProperty] private string _contextSummaryPreview = string.Empty;
    [ObservableProperty] private string _contextSummaryFreshness = string.Empty;
    [ObservableProperty] private ObservableCollection<string> _contextSummaryKeyPoints = new();
    [ObservableProperty] private bool _hasContextSummary;
    [ObservableProperty] private bool _hasContextSummaryKeyPoints;
    [ObservableProperty] private bool _isRefreshingConversationSummary;
    [ObservableProperty] private string _conversationSummaryRefreshError = string.Empty;
    [ObservableProperty] private string _contextRecallStatus = "No durable recall context captured yet.";
    [ObservableProperty] private ObservableCollection<ChatContextRecallDisplayItem> _contextRecallItems = new();
    [ObservableProperty] private bool _hasContextRecallItems;

    // ── Branching ─────────────────────────────────────────────────
    [ObservableProperty] private string? _pendingBranchLabel;

    // ── Collections ────────────────────────────────────────────
    public ObservableCollection<ChatMessageItem> Messages { get; } = new();
    public ObservableCollection<ConversationListItem> Conversations { get; } = new();
    public ObservableCollection<AiModel> AvailableModels { get; } = new();

    /// <summary>
    /// The model picked in the chat header. Two-way bound to the header ComboBox;
    /// setting it activates that model (see <see cref="OnSelectedModelChanged"/>).
    /// </summary>
    [ObservableProperty] private AiModel? _selectedModel;

    public ObservableCollection<SystemPromptItem> SystemPrompts { get; } = new();
    public ObservableCollection<string> SuggestedQuestions { get; } = new();
    public ObservableCollection<string> FolderNames { get; } = new();

    // ── Folder Filter ──────────────────────────────────────────
    [ObservableProperty] private string? _activeFolderFilter;

    // ── Branching ───────────────────────────────────────────────
    private ConversationBranchTree? _branchTree;
    public ConversationBranchTree? BranchTree
    {
        get => _branchTree;
        set => SetProperty(ref _branchTree, value);
    }

    public bool HasBranches => _branchTree?.TotalBranchCount > 0;
    public ObservableCollection<ConversationBranchTree> ActiveBranches { get; } = new();

    // ── Computed Properties ────────────────────────────────────
    public bool HasNoConversations => Conversations.Count == 0;
    public bool HasNoMessages => Messages.Count == 0;
    public bool HasActiveSystemPrompt => !string.IsNullOrEmpty(ActiveSystemPromptName);
    public bool CanSend => !string.IsNullOrWhiteSpace(UserInput) && !IsGenerating;
    public bool IsVoiceActive => IsRecording || IsTranscribing;
    public bool HasConversationIntelligenceStrip => ActiveConversationId.HasValue;
    public string ConversationIntelligenceStoryText =>
        !ActiveConversationId.HasValue
            ? string.Empty
            : _latestContextInspection?.ContextStoryText
                ?? "No context story is available until Agent-X assembles a response for this conversation.";
    public IReadOnlyList<ChatContextStorySourceDisplayItem> ConversationIntelligenceStorySourceChips =>
        _latestContextInspection?.ContextStorySourceChips
            .Select(chip => new ChatContextStorySourceDisplayItem { Label = chip.Label })
            .ToArray()
        ?? Array.Empty<ChatContextStorySourceDisplayItem>();
    public bool HasConversationIntelligenceStory =>
        !string.IsNullOrWhiteSpace(ConversationIntelligenceStoryText);
    public bool HasConversationIntelligenceStorySourceChips =>
        ConversationIntelligenceStorySourceChips.Count > 0;
    public bool HasContextStory => !string.IsNullOrWhiteSpace(ContextStoryText);
    public bool HasContextStorySourceChips => ContextStorySourceChips.Count > 0;
    public string ConversationIntelligenceBadgeText
    {
        get
        {
            if (!ActiveConversationId.HasValue)
            {
                return string.Empty;
            }

            if (ConversationIntelligenceIsCurrent)
            {
                return "Current";
            }

            if (ConversationIntelligenceIsStale)
            {
                return "Stale";
            }

            if (ConversationIntelligenceIsPending)
            {
                return "Pending";
            }

            return "Unavailable";
        }
    }
    public string ConversationIntelligenceStatusText
    {
        get
        {
            if (!ActiveConversationId.HasValue)
            {
                return string.Empty;
            }

            if (IsRefreshingConversationSummary)
            {
                return "Refreshing durable summary...";
            }

            if (HasConversationSummaryRefreshError)
            {
                return ConversationSummaryRefreshError;
            }

            if (ConversationIntelligenceIsCurrent)
            {
                var keyPointCount = _latestContextInspection?.Summary?.KeyPoints.Count ?? 0;
                return keyPointCount > 0
                    ? $"Summary current • {keyPointCount} key point{(keyPointCount == 1 ? string.Empty : "s")} available"
                    : "Summary current • Ready for deeper inspection";
            }

            if (ConversationIntelligenceIsStale)
            {
                var pendingMessageCount = _latestContextInspection?.Summary?.PendingMessageCount ?? 0;
                return pendingMessageCount > 0
                    ? $"Summary stale • {pendingMessageCount} newer message{(pendingMessageCount == 1 ? string.Empty : "s")} not folded in"
                    : "Summary stale • Waiting for the next refresh";
            }

            if (ConversationIntelligenceIsPending)
            {
                return "Summary refresh pending";
            }

            return _latestContextInspection?.HasLimitedVisibility == true
                ? "Summary unavailable for this response path"
                : "No conversation context captured yet";
        }
    }
    public bool ConversationIntelligenceIsCurrent =>
        ActiveConversationId.HasValue &&
        _latestContextInspection?.Summary is { IsStale: false };
    public bool ConversationIntelligenceIsStale =>
        ActiveConversationId.HasValue &&
        _latestContextInspection?.Summary is { IsStale: true };
    public bool ConversationIntelligenceIsPending =>
        ActiveConversationId.HasValue &&
        _latestContextInspection is not null &&
        !_latestContextInspection.HasLimitedVisibility &&
        _latestContextInspection.Summary is null;
    public bool ConversationIntelligenceIsUnavailable =>
        HasConversationIntelligenceStrip &&
        !ConversationIntelligenceIsCurrent &&
        !ConversationIntelligenceIsStale &&
        !ConversationIntelligenceIsPending;
    public bool ShowConversationSummaryRefreshAction =>
        ActiveConversationId.HasValue &&
        (ConversationIntelligenceIsStale
         || ConversationIntelligenceIsPending
         || ConversationIntelligenceIsUnavailable
         || IsRefreshingConversationSummary
         || HasConversationSummaryRefreshError);
    public bool HasConversationSummaryRefreshError =>
        !string.IsNullOrWhiteSpace(ConversationSummaryRefreshError);
    public bool HasConversationSummaryRefreshStatus =>
        IsRefreshingConversationSummary || HasConversationSummaryRefreshError;
    public string ConversationSummaryRefreshStatusText =>
        IsRefreshingConversationSummary
            ? "Refreshing durable summary..."
            : ConversationSummaryRefreshError;
    public string ConversationSummaryRefreshActionText =>
        IsRefreshingConversationSummary
            ? "Refreshing..."
            : ConversationIntelligenceIsUnavailable || HasConversationSummaryRefreshError
                ? "Retry Summary"
                : "Refresh Summary";
    public bool CanRefreshConversationSummary =>
        ActiveConversationId.HasValue && !IsRefreshingConversationSummary;

    // ── Coordinators ──────────────────────────────────────────
    private readonly IConversationCoordinator _conversationCoordinator;
    private readonly IMessagingCoordinator _messagingCoordinator;
    private readonly IVoiceCoordinator _voiceCoordinator;
    private readonly IBranchingCoordinator _branchingCoordinator;

    // ── Services (retained for model/prompt/connection operations) ──
    private readonly IAiService _aiService;
    private readonly IChatService _chatService;
    private readonly IModelManager _modelManager;
    private readonly ISystemPromptService _systemPromptService;
    private readonly IConversationMemoryService _memoryService;
    private readonly INotificationService _notificationService;
    private readonly ITemporalIdentityService _temporalIdentity;
    private readonly IPrivacyStatusService _privacyStatusService;
    private readonly ILocalizationService _localization;

    // ── Streaming assistant message (for token-by-token updates) ──
    // The generation streaming into the screen. Null when nothing this view model started is
    // running on the thread on screen. Moving off a thread clears it, so tokens and completions
    // that still arrive for the thread left behind are ignored instead of landing on the one
    // now shown.
    private ChatGeneration? _activeGeneration;

    private ChatContextInspectionSnapshot? _latestContextInspection;

    // The coordinator call of the most recent generation. A generation abandoned by a thread
    // switch can take a moment to wind down; the next one waits for it (briefly) so its last
    // tokens and rows cannot be mistaken for the new generation's.
    private Task? _lastGenerationTask;
    private static readonly TimeSpan PreviousGenerationGrace = TimeSpan.FromSeconds(5);

    // ── Which conversation the screen is on, as a generation sees it ──
    // Every move off a thread advances this counter, and a generation carries the value it
    // started under, so work that finishes under a newer value is known to belong to a thread
    // already left (a completion, or messages loaded for a thread the operator moved past).
    private int _conversationEpoch;
    private readonly Dictionary<long, ChatContextInspectionSnapshot> _assistantMessageContextSnapshots = new();
    private bool _disposed;

    // Time the open conversation is on screen, reported to Temporal Identity as engagement when
    // the operator moves off it. The page pauses it while Chat is not the page shown.
    private readonly EngagementTracker _conversationEngagement;
    private bool _isConversationViewShown = true;

    /// <summary>The clock engagement is timed with (a test seam).</summary>
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>Opens a web page in the default browser (a test seam).</summary>
    internal Action<Uri> OpenExternalLink { get; set; } = static link =>
        Process.Start(new ProcessStartInfo { FileName = link.AbsoluteUri, UseShellExecute = true });

    public ChatViewModel(
        IConversationCoordinator conversationCoordinator,
        IMessagingCoordinator messagingCoordinator,
        IVoiceCoordinator voiceCoordinator,
        IBranchingCoordinator branchingCoordinator,
        IAiService aiService,
        IChatService chatService,
        IModelManager modelManager,
        ISystemPromptService systemPromptService,
        IConversationMemoryService memoryService,
        INotificationService notificationService,
        ITemporalIdentityService temporalIdentity,
        IPrivacyStatusService privacyStatusService,
        ILocalizationService localization)
    {
        _conversationCoordinator = conversationCoordinator;
        _messagingCoordinator = messagingCoordinator;
        _voiceCoordinator = voiceCoordinator;
        _branchingCoordinator = branchingCoordinator;
        _aiService = aiService;
        _chatService = chatService;
        _modelManager = modelManager;
        _systemPromptService = systemPromptService;
        _memoryService = memoryService;
        _notificationService = notificationService;
        _temporalIdentity = temporalIdentity;
        _privacyStatusService = privacyStatusService;
        _localization = localization;
        _conversationEngagement = new EngagementTracker(
            temporalIdentity, EngagementTargetType.Conversation, () => UtcNow());

        SubscribeToCoordinatorEvents();
        Log.Debug("ChatViewModel created with coordinators");
    }

    // ═══════════════════════════════════════════════════════════════
    // COORDINATOR EVENT SUBSCRIPTIONS
    // ═══════════════════════════════════════════════════════════════

    private void SubscribeToCoordinatorEvents()
    {
        // ── MessagingCoordinator ─────────────────────────────────
        _messagingCoordinator.TokenReceived += OnTokenReceived;
        _messagingCoordinator.StreamingCompleted += OnStreamingCompleted;
        _messagingCoordinator.GenerationError += OnGenerationError;
        _messagingCoordinator.NotificationRequested += OnMessagingNotification;

        // ── VoiceCoordinator ─────────────────────────────────────
        _voiceCoordinator.RecordingStateChanged += OnRecordingStateChanged;
        _voiceCoordinator.TranscribingStateChanged += OnTranscribingStateChanged;
        _voiceCoordinator.StatusChanged += OnVoiceStatusChanged;
        _voiceCoordinator.NotificationRequested += OnVoiceNotification;

        // ── BranchingCoordinator ─────────────────────────────────
        _branchingCoordinator.BranchTreeChanged += OnBranchTreeChanged;
        _branchingCoordinator.NotificationRequested += OnBranchingNotification;
    }

    /// <summary>
    /// The coordinators are app-wide singletons, so a subscription outlives the page that made
    /// it. A view model left subscribed keeps reacting to every other chat's generations.
    /// </summary>
    private void UnsubscribeFromCoordinatorEvents()
    {
        _messagingCoordinator.TokenReceived -= OnTokenReceived;
        _messagingCoordinator.StreamingCompleted -= OnStreamingCompleted;
        _messagingCoordinator.GenerationError -= OnGenerationError;
        _messagingCoordinator.NotificationRequested -= OnMessagingNotification;

        _voiceCoordinator.RecordingStateChanged -= OnRecordingStateChanged;
        _voiceCoordinator.TranscribingStateChanged -= OnTranscribingStateChanged;
        _voiceCoordinator.StatusChanged -= OnVoiceStatusChanged;
        _voiceCoordinator.NotificationRequested -= OnVoiceNotification;

        _branchingCoordinator.BranchTreeChanged -= OnBranchTreeChanged;
        _branchingCoordinator.NotificationRequested -= OnBranchingNotification;
    }

    private void OnRecordingStateChanged(object? sender, bool isRecording) => IsRecording = isRecording;

    private void OnTranscribingStateChanged(object? sender, bool isTranscribing) => IsTranscribing = isTranscribing;

    private void OnVoiceStatusChanged(object? sender, string message) => VoiceStatusMessage = message;

    private void OnTokenReceived(object? sender, string token)
    {
        if (_activeGeneration is { IsLive: true } generation)
        {
            generation.AssistantMessage.Content += token;
            CurrentStreamingResponse = generation.AssistantMessage.Content;
            OnPropertyChanged(nameof(Messages));
        }
    }

    private void OnStreamingCompleted(object? sender, StreamingCompletedEventArgs e)
    {
        // Only a generation this view model started, still live on the thread it began on, is
        // applied. Anything else was raised for a thread the operator has left, or by another
        // chat sharing the singleton coordinator; adopting it would drag the screen onto that
        // thread and file a sidebar row for it. The stream's output is already persisted, so
        // the thread shows it when it is next opened.
        if (_activeGeneration is not { IsLive: true } generation || generation.Epoch != _conversationEpoch)
        {
            Log.Debug("Discarding a completion this chat is not waiting for");
            return;
        }

        ApplyCompletion(generation, CompletionData.From(e));
    }

    private void OnGenerationError(object? sender, string errorMsg)
    {
        // The send's own result finishes the generation (FinishGeneration); this only shows the
        // error a moment sooner. A failed regeneration keeps the previous answer instead.
        if (_activeGeneration is { IsLive: true, ReplacedAssistantMessage: null } generation)
        {
            generation.AssistantMessage.Content = errorMsg;
            generation.AssistantMessage.IsStreaming = false;
        }
    }

    private void OnMessagingNotification(object? sender, NotificationRequestEventArgs e)
        => ForwardNotification(e);

    private void OnVoiceNotification(object? sender, NotificationRequestEventArgs e)
        => ForwardNotification(e);

    private void OnBranchingNotification(object? sender, NotificationRequestEventArgs e)
        => ForwardNotification(e);

    private void OnBranchTreeChanged(object? sender, long conversationId)
    {
        if (ActiveConversationId == conversationId || ActiveConversationId.HasValue)
            _ = RefreshBranchTreeAsync();
    }

    private void ForwardNotification(NotificationRequestEventArgs e)
    {
        switch (e.Level)
        {
            case "error":
                _notificationService.ShowError(e.Title, e.Message);
                break;
            case "info":
                _notificationService.ShowInfo(e.Title, e.Message);
                break;
            default:
                _notificationService.Show(e.Title, e.Message);
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // INITIALIZATION
    // ═══════════════════════════════════════════════════════════════

    public async Task InitializeAsync()
    {
        Log.Information("ChatViewModel initializing...");
        try
        {
            // First, and on every visit: a provider changed in Settings must show up here, and
            // this never throws, so a failure further down cannot leave the claim unevaluated.
            await RefreshPrivacyClaimAsync();
            await LoadConversationsAsync();
            await CheckConnectionStatusAsync();
            await LoadAvailableModelsAsync();
            await LoadSystemPromptsAsync();
            await UpdateMemoryCountAsync();
            await RefreshFolderNamesAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize ChatViewModel");
        }
        Log.Information("ChatViewModel initialized");
    }

    private async Task LoadConversationsAsync()
    {
        var summaries = await _conversationCoordinator.LoadConversationsAsync();
        Conversations.Clear();
        foreach (var s in summaries)
            Conversations.Add(MapToConversationListItem(s));
        OnPropertyChanged(nameof(HasNoConversations));
    }

    private async Task CheckConnectionStatusAsync()
    {
        try
        {
            var connected = await _aiService.ActiveProvider.CheckConnectionAsync();
            IsConnected = connected;
            ConnectionStatus = connected ? "Connected" : "Disconnected";
            ActiveModelName = connected && !string.IsNullOrEmpty(_aiService.ActiveModelId)
                ? _aiService.ActiveModelId
                : "No model selected";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check AI connection status");
            IsConnected = false;
            ConnectionStatus = "Disconnected";
            ActiveModelName = "No model selected";
        }
    }

    private async Task LoadAvailableModelsAsync()
    {
        AvailableModels.Clear();
        try
        {
            var models = await _modelManager.GetInstalledModelsAsync();
            foreach (var model in models) AvailableModels.Add(model);
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to load available models"); }
    }

    private async Task LoadSystemPromptsAsync()
    {
        SystemPrompts.Clear();
        try
        {
            await _systemPromptService.SeedBuiltInPromptsAsync();
            var prompts = await _systemPromptService.GetAllPromptsAsync();
            foreach (var p in prompts)
                SystemPrompts.Add(new SystemPromptItem
                {
                    Id = p.Id,
                    Name = p.Name,
                    Content = p.Content,
                    Category = p.Category,
                    IsBuiltIn = p.IsBuiltIn,
                    IsFavorite = p.IsFavorite
                });
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to load system prompts"); }
    }

    private async Task UpdateMemoryCountAsync()
    {
        try { MemoryCount = await _memoryService.GetMemoryCountAsync(); }
        catch (Exception ex) { Log.Warning(ex, "Failed to update memory count"); }
    }

    /// <summary>
    /// Works out what the empty chat may say about where messages go, for the provider active now
    /// and the Research Mode switch on screen. The page runs it on every visit, so a provider
    /// changed in Settings is reflected; Refresh connection and the Research Mode switch run it too.
    /// </summary>
    private async Task RefreshPrivacyClaimAsync()
    {
        var version = ++_privacyClaimVersion;
        var (providerId, providerName) = ActiveProviderIdentity();

        IReadOnlyList<PromptRecipient>? recipients;
        try
        {
            recipients = await _privacyStatusService.GetChatMessageRecipientsAsync(providerId, IsResearchMode);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to work out where chat messages are sent");
            recipients = null;
        }

        if (version != _privacyClaimVersion)
        {
            // Research Mode was switched meanwhile; the newer evaluation owns the claim.
            return;
        }

        if (recipients is null)
        {
            // A local claim that could not be confirmed is not made.
            IsChatPrivate = false;
            PrivacyHint = _localization.GetString("Chat_PrivacyUnknown");
            return;
        }

        IsChatPrivate = recipients.Count == 0;
        if (recipients.Count == 0)
        {
            PrivacyHint = providerName is null
                ? _localization.GetString("Chat_PrivacyLocalUnnamed")
                : _localization.GetString("Chat_PrivacyLocal", providerName);
            return;
        }

        PrivacyHint = string.Join(" ", recipients.Select(DescribeRecipient));
    }

    private string DescribeRecipient(PromptRecipient recipient)
    {
        var name = recipient.Name ?? string.Empty;
        return recipient.Kind switch
        {
            PromptRecipientKind.CloudAiProvider => _localization.GetString("Chat_PrivacyCloud", name),
            PromptRecipientKind.RemoteOllama => _localization.GetString("Chat_PrivacyRemoteOllama", name),
            PromptRecipientKind.ModelRouting => _localization.GetString("Chat_PrivacyRouting"),
            PromptRecipientKind.SearXng => _localization.GetString("Chat_PrivacySearXng", name),
            _ => _localization.GetString("Chat_PrivacyWebSearch", name),
        };
    }

    /// <summary>The active provider's id and display name, or nulls before the AI service is ready.</summary>
    private (string? ProviderId, string? DisplayName) ActiveProviderIdentity()
    {
        try
        {
            var provider = _aiService.ActiveProvider;
            return (provider?.ProviderId, string.IsNullOrWhiteSpace(provider?.DisplayName) ? null : provider.DisplayName);
        }
        catch (InvalidOperationException)
        {
            return (null, null); // not initialized yet
        }
    }

    private async Task RefreshFolderNamesAsync()
    {
        var names = await _conversationCoordinator.LoadFolderNamesAsync();
        FolderNames.Clear();
        foreach (var f in names) FolderNames.Add(f);
    }

    private async Task InitializePostSendAsync()
    {
        await LoadSuggestedQuestionsAsync();
        await UpdateMemoryCountAsync();
    }

    private async Task LoadSuggestedQuestionsAsync()
    {
        if (ActiveConversationId is null) return;
        try
        {
            var questions = await _memoryService.GetSuggestedQuestionsAsync(ActiveConversationId.Value);
            SuggestedQuestions.Clear();
            foreach (var q in questions) SuggestedQuestions.Add(q);
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to load suggested questions"); }
    }

    // ═══════════════════════════════════════════════════════════════
    // PROPERTY CHANGE HOOKS
    // ═══════════════════════════════════════════════════════════════

    partial void OnUserInputChanged(string value)
    {
        SendMessageCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSend));
    }

    partial void OnIsGeneratingChanged(bool value)
    {
        SendMessageCommand.NotifyCanExecuteChanged();
        StopGenerationCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSend));
    }

    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsVoiceActive));
        ToggleVoiceRecordingCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsTranscribingChanged(bool value)
        => OnPropertyChanged(nameof(IsVoiceActive));

    partial void OnActiveSystemPromptNameChanged(string? value)
        => OnPropertyChanged(nameof(HasActiveSystemPrompt));

    partial void OnActiveConversationIdChanged(long? value)
    {
        ConversationSummaryRefreshError = string.Empty;
        NotifyConversationIntelligenceStripChanged();
        NotifyConversationSummaryRefreshStateChanged();

        // Moving off a conversation records the time it was read; the next one starts timing.
        if (_isConversationViewShown)
        {
            _ = value is long conversationId
                ? _conversationEngagement.OpenAsync(conversationId)
                : _conversationEngagement.CloseAsync();
        }
    }

    /// <summary>
    /// The chat page left the screen: the open conversation stops counting as read, and the
    /// time it was shown is recorded.
    /// </summary>
    public Task PauseConversationEngagementAsync()
    {
        _isConversationViewShown = false;
        return _conversationEngagement.CloseAsync();
    }

    /// <summary>The chat page is on screen again: the open conversation counts as read from now.</summary>
    public void ResumeConversationEngagement()
    {
        _isConversationViewShown = true;
        if (ActiveConversationId is long conversationId)
        {
            _ = _conversationEngagement.OpenAsync(conversationId);
        }
    }

    partial void OnIsRefreshingConversationSummaryChanged(bool value)
        => NotifyConversationSummaryRefreshStateChanged();

    partial void OnConversationSummaryRefreshErrorChanged(string value)
        => NotifyConversationSummaryRefreshStateChanged();

    partial void OnOrchestrationModeChanged(ChatOrchestrationMode value)
    {
        OnPropertyChanged(nameof(OrchestrationModeIndex));
        OnPropertyChanged(nameof(OrchestrationModeTooltip));
    }

    partial void OnContextStoryTextChanged(string value)
        => OnPropertyChanged(nameof(HasContextStory));

    partial void OnContextStorySourceChipsChanged(ObservableCollection<ChatContextStorySourceDisplayItem> value)
        => OnPropertyChanged(nameof(HasContextStorySourceChips));

    partial void OnConversationSearchQueryChanged(string value)
        => _ = FilterConversationsAsync(value);

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS — Messaging
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(UserInput) || IsGenerating) return;

        var userContent = UserInput.Trim();
        UserInput = string.Empty;

        await SendContentAsync(userContent);
    }

    /// <summary>
    /// Sends <paramref name="userContent"/> as a new prompt in the conversation on screen.
    /// Edit and non-persisted regeneration resend through here rather than through
    /// <see cref="UserInput"/>, so a draft the operator is typing is left alone.
    /// </summary>
    private async Task SendContentAsync(string userContent)
    {
        Log.Debug("Sending message: {MessagePreview}", userContent.Length > 50
            ? userContent[..50] + "..." : userContent);

        // Add user message to UI
        var userMessage = new ChatMessageItem
        {
            Role = "user",
            Content = userContent,
            Timestamp = DateTime.UtcNow,
            IsUser = true,
            IsAssistant = false,
            IsSystem = false,
            IsStreaming = false
        };
        Messages.Add(userMessage);
        OnPropertyChanged(nameof(HasNoMessages));

        // Create streaming placeholder
        var assistantMessage = CreateStreamingAssistantMessage();
        Messages.Add(assistantMessage);

        var conversationId = ActiveConversationId;
        var systemPrompt = ActiveSystemPrompt;
        var modelId = _aiService.ActiveModelId;
        var isResearchMode = IsResearchMode;
        var orchestrationMode = OrchestrationMode;

        await RunGenerationAsync(
            new ChatGeneration
            {
                Epoch = _conversationEpoch,
                UserMessage = userMessage,
                AssistantMessage = assistantMessage
            },
            () => orchestrationMode == ChatOrchestrationMode.Standard
                ? _messagingCoordinator.SendMessageAsync(
                    userContent, conversationId, systemPrompt, modelId, isResearchMode)
                : _messagingCoordinator.SendMessageAsync(
                    userContent, conversationId, systemPrompt, modelId, isResearchMode, orchestrationMode));
    }

    private static ChatMessageItem CreateStreamingAssistantMessage() => new()
    {
        Role = "assistant",
        Content = "",
        Timestamp = DateTime.UtcNow,
        IsUser = false,
        IsAssistant = true,
        IsSystem = false,
        IsStreaming = true
    };

    /// <summary>
    /// Runs one generation and settles the screen afterwards, whatever the outcome: a
    /// completion, a stop, or a failure all end with generation state cleared, so Send
    /// returns and Stop is not left pointing at nothing. A generation the operator moved
    /// away from is settled by the move itself and changes nothing here when it ends.
    /// </summary>
    private async Task RunGenerationAsync(ChatGeneration generation, Func<Task<SendMessageResult>> start)
    {
        _activeGeneration = generation;
        IsGenerating = true;

        // A generation abandoned by a thread switch may still be winding down. Its remaining
        // events are ignored while this one is not live yet, and waiting for it keeps the rows
        // it may still write out of this generation's view of the conversation.
        var previous = _lastGenerationTask;
        if (previous is { IsCompleted: false })
        {
            await Task.WhenAny(previous, Task.Delay(PreviousGenerationGrace));
        }

        if (!ReferenceEquals(_activeGeneration, generation))
        {
            return;
        }

        generation.IsLive = true;

        SendMessageResult result;
        try
        {
            var coordinatorCall = start();
            _lastGenerationTask = coordinatorCall;
            result = await coordinatorCall;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Chat generation failed unexpectedly");
            result = new SendMessageResult
            {
                ConversationId = ActiveConversationId,
                HadError = true,
                ResponseContent = "An error occurred while generating a response.",
                ErrorMessage = ex.Message
            };
        }

        FinishGeneration(generation, result);
    }

    private void FinishGeneration(ChatGeneration generation, SendMessageResult result)
    {
        if (!ReferenceEquals(_activeGeneration, generation))
        {
            // The operator left this thread (or started over) while it ran. What it produced is
            // persisted and shows when that thread is opened again; its result, including the
            // context inspection the cancel path back-fills, belongs to that thread.
            return;
        }

        _activeGeneration = null;

        if (result.WasCancelled || result.HadError)
        {
            if (generation.ReplacedAssistantMessage is not null)
            {
                // The coordinator kept the previous answer, so the screen shows it again.
                RestoreReplacedAnswer(generation);
            }
            else if (result.WasCancelled)
            {
                generation.AssistantMessage.Content = string.IsNullOrEmpty(result.ResponseContent)
                    ? AppendStopMarker(generation.AssistantMessage.Content)
                    : result.ResponseContent;
            }
            else
            {
                generation.AssistantMessage.Content = result.ResponseContent;
            }

            // The prompt is saved before the answer streams, so it usually outlives a stop or
            // a failure; stamping it lets edit, branch and delete act on the saved row. A send
            // that created its conversation keeps that conversation rather than orphaning it.
            StampPersistedIdentity(generation.UserMessage, result.UserMessageId, result.UserMessageSortOrder, result.ConversationId);
            AdoptConversation(result.ConversationId, result.ConversationTitle, lastMessage: null);

            if (result.ContextInspection is not null)
            {
                ApplyContextInspection(result.ContextInspection);
            }
        }
        else
        {
            ApplyCompletion(generation, CompletionData.From(result));

            if (generation.ReplacedAssistantMessage is { } replaced)
            {
                if (result.AssistantMessageId is null)
                {
                    // Nothing new was saved (an empty reply), so the previous answer still stands.
                    RestoreReplacedAnswer(generation);
                    _notificationService.ShowInfo(
                        "Response kept",
                        "The model returned no new response, so the previous one was kept.");
                }
                else if (replaced.MessageId > 0)
                {
                    _assistantMessageContextSnapshots.Remove(replaced.MessageId);
                }
            }
        }

        generation.AssistantMessage.IsStreaming = false;
        IsGenerating = false;
        CurrentStreamingResponse = string.Empty;
    }

    private void ApplyCompletion(ChatGeneration generation, CompletionData completion)
    {
        if (generation.IsCompleted)
        {
            return;
        }

        generation.IsCompleted = true;

        var assistantMessage = generation.AssistantMessage;
        assistantMessage.IsStreaming = false;

        // The final text is authoritative: the offline fallback builds its help text without
        // streaming it, and tokens that raced a stop are only in the final text.
        if (!string.IsNullOrEmpty(completion.ResponseContent) &&
            assistantMessage.Content != completion.ResponseContent)
        {
            assistantMessage.Content = completion.ResponseContent;
        }

        assistantMessage.TokenCount = completion.TokenCount;
        assistantMessage.GenerationTimeMs = completion.GenerationTimeMs;
        StampPersistedIdentity(
            assistantMessage,
            completion.AssistantMessageId,
            completion.AssistantMessageSortOrder,
            completion.ConversationId);
        if (completion.WebCitations is { Count: > 0 } citations)
        {
            assistantMessage.WebCitations = citations;
        }

        ApplyInlineContextNote(assistantMessage, completion.ContextInspection);
        if (completion.AssistantMessageId.HasValue && completion.ContextInspection is not null)
        {
            _assistantMessageContextSnapshots[completion.AssistantMessageId.Value] = completion.ContextInspection;
        }

        // Messages sent in this session carry their persisted identity like loaded ones, so
        // delete, edit, branch and regenerate act on the rows they show.
        StampPersistedIdentity(
            generation.UserMessage,
            completion.UserMessageId,
            completion.UserMessageSortOrder,
            completion.ConversationId);

        TokenCount += completion.TokenCount;
        GenerationTimeMs = completion.GenerationTimeMs;

        AdoptConversation(completion.ConversationId, completion.ConversationTitle, completion.ResponseContent);

        // Update sidebar last message
        if (ActiveConversationId.HasValue && !string.IsNullOrEmpty(completion.ResponseContent))
        {
            var convItem = Conversations.FirstOrDefault(c => c.Id == ActiveConversationId);
            if (convItem is not null)
            {
                convItem.LastMessage = PreviewOf(completion.ResponseContent);
            }
        }

        // Temporal Identity learns from each prompt once. A regeneration answers a prompt it
        // has already learned from; learning again would count the same message twice.
        if (!generation.IsRegeneration &&
            completion.UserMessageId.HasValue &&
            completion.ConversationId.HasValue)
        {
            LearnFromPromptInBackground(completion.UserMessageId.Value, completion.ConversationId.Value);
        }

        ApplyContextInspection(completion.ContextInspection);

        // Load follow-ups and update memory (non-blocking)
        _ = InitializePostSendAsync();
    }

    /// <summary>
    /// Takes over the conversation a send created when the screen had none yet, and lists it
    /// in the sidebar unless it is already there.
    /// </summary>
    private void AdoptConversation(long? conversationId, string? conversationTitle, string? lastMessage)
    {
        if (conversationId is not long id || ActiveConversationId == id)
        {
            return;
        }

        ActiveConversationId = id;
        ActiveConversationTitle = conversationTitle ?? ActiveConversationTitle;

        if (Conversations.All(c => c.Id != id))
        {
            Conversations.Insert(0, new ConversationListItem
            {
                Id = id,
                Title = ActiveConversationTitle,
                LastMessage = PreviewOf(lastMessage),
                UpdatedAt = DateTime.UtcNow,
                IsPinned = false,
                MessageCount = 0
            });
            OnPropertyChanged(nameof(HasNoConversations));
        }
    }

    private static void StampPersistedIdentity(
        ChatMessageItem message,
        long? messageId,
        int? sortOrder,
        long? conversationId)
    {
        if (messageId is > 0)
        {
            message.MessageId = messageId.Value;
        }

        if (sortOrder.HasValue)
        {
            message.SortOrder = sortOrder.Value;
        }

        if (conversationId.HasValue)
        {
            message.ConversationId = conversationId.Value;
        }
    }

    private void RestoreReplacedAnswer(ChatGeneration generation)
    {
        var index = Messages.IndexOf(generation.AssistantMessage);
        if (index >= 0 && generation.ReplacedAssistantMessage is not null)
        {
            Messages[index] = generation.ReplacedAssistantMessage;
        }
    }

    private static string AppendStopMarker(string content) =>
        string.IsNullOrEmpty(content)
            ? "[Generation stopped]"
            : content + "\n\n[Generation stopped]";

    private static string PreviewOf(string? content) =>
        string.IsNullOrEmpty(content)
            ? string.Empty
            : content.Length > 80 ? content[..80] + "..." : content;

    /// <summary>
    /// Temporal Identity learns from the prompt: the beliefs it states (Past Self and the
    /// dashboard's belief-conflict panel read these), the operator's voice, and insight moments
    /// in the thread. Each step runs even when an earlier one fails. The service keeps nothing in
    /// the shared change tracker, so this can run off the UI thread.
    /// </summary>
    private void LearnFromPromptInBackground(long userMessageId, long conversationId)
    {
        _ = Task.Run(async () =>
        {
            await RunLearningStepAsync(() => _temporalIdentity.ProcessMessageAsync(userMessageId), "belief tracking", userMessageId);
            await RunLearningStepAsync(() => _temporalIdentity.LearnFromMessageAsync(userMessageId), "voice learning", userMessageId);
            await RunLearningStepAsync(() => _temporalIdentity.DetectInsightsAsync(conversationId), "insight detection", userMessageId);
        });
    }

    private static async Task RunLearningStepAsync(Func<Task> step, string stepName, long userMessageId)
    {
        try
        {
            await step();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Temporal identity {Step} failed for message {MessageId}", stepName, userMessageId);
        }
    }

    /// <summary>
    /// Moves the screen off whatever generation is running: it is stopped, and anything it
    /// still reports is ignored. Leaves Send usable at once rather than after the stop lands.
    /// </summary>
    private async Task LeaveActiveGenerationAsync()
    {
        var generation = _activeGeneration;
        _activeGeneration = null;
        _conversationEpoch++;

        if (generation is not null || IsGenerating)
        {
            if (generation is not null)
            {
                generation.AssistantMessage.IsStreaming = false;
            }

            await _messagingCoordinator.StopGenerationAsync();
        }

        IsGenerating = false;
        CurrentStreamingResponse = string.Empty;
    }

    private void NotifyGenerationInProgress() =>
        _notificationService.ShowInfo(
            "Response in progress",
            "Stop the current response or wait for it to finish, then try again.");

    [RelayCommand]
    private async Task StopGenerationAsync()
        => await _messagingCoordinator.StopGenerationAsync();

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS — Conversation
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task NewConversationAsync()
    {
        // A generation belonging to the thread being left must not follow the operator into
        // the blank one: left running, its completion would carry the old thread's id into the
        // null one below and the new conversation would silently become the old one.
        await LeaveActiveGenerationAsync();

        ActiveConversationId = null;
        ActiveConversationTitle = "New Conversation";
        ActiveSystemPrompt = null;
        ActiveSystemPromptName = null;
        TokenCount = 0;
        GenerationTimeMs = 0;
        Messages.Clear();
        SuggestedQuestions.Clear();
        ConversationSummaryRefreshError = string.Empty;
        ResetContextInspection();
        OnPropertyChanged(nameof(HasNoMessages));
    }

    [RelayCommand]
    private async Task DeleteConversationAsync(long conversationId)
    {
        var isOpen = ActiveConversationId == conversationId;
        if (isOpen && IsGenerating)
        {
            // Nothing may still be writing into the thread being deleted.
            await LeaveActiveGenerationAsync();
        }

        if (!await _conversationCoordinator.DeleteConversationAsync(conversationId))
        {
            _notificationService.ShowError(
                "Delete failed",
                "The conversation could not be deleted and is still in your history.");
            return;
        }

        var item = Conversations.FirstOrDefault(c => c.Id == conversationId);
        if (item is not null)
        {
            Conversations.Remove(item);
            OnPropertyChanged(nameof(HasNoConversations));
        }

        if (isOpen)
        {
            // Time spent in a conversation that no longer exists is not recorded against it.
            _conversationEngagement.Discard();
            await NewConversationAsync();
        }
        else if (ActiveConversationId.HasValue)
        {
            // The open thread may have been a branch of the deleted one, or its parent.
            await RefreshBranchTreeAsync();
        }
    }

    [RelayCommand]
    private async Task SelectConversationAsync(long conversationId)
    {
        if (conversationId <= 0) return;

        // The sidebar is only a cache of the conversation list: a caller arriving before it has
        // loaded (Jump-To on a cold Chat page) or naming a thread it does not show (a filtered
        // list) still opens the conversation it asked for.
        var title = Conversations.FirstOrDefault(c => c.Id == conversationId)?.Title;
        if (title is null)
        {
            var summary = await _conversationCoordinator.LoadConversationSummaryAsync(conversationId);
            if (summary is null)
            {
                _notificationService.ShowInfo(
                    "Conversation not found",
                    "The conversation could not be opened. It may have been deleted.");
                return;
            }

            title = summary.Title;
        }

        // Picking another thread moves the screen off any running generation just as Ctrl+N
        // does, so it stops that generation and clears the generation state with it.
        await LeaveActiveGenerationAsync();
        var epoch = _conversationEpoch;

        ActiveConversationId = conversationId;
        ActiveConversationTitle = title;
        Messages.Clear();
        ConversationSummaryRefreshError = string.Empty;

        var messageSummaries = await _conversationCoordinator.LoadMessagesAsync(conversationId);
        if (epoch != _conversationEpoch)
        {
            // Another thread was opened while this one loaded.
            return;
        }

        foreach (var ms in messageSummaries)
        {
            var messageItem = MapToChatMessageItem(ms);
            ReapplyInlineContextNote(messageItem);
            Messages.Add(messageItem);
        }

        OnPropertyChanged(nameof(HasNoMessages));
        LoadContextInspectionForConversation(conversationId);
        await RefreshBranchTreeAsync();
    }

    [RelayCommand]
    private async Task TogglePinAsync(long conversationId)
    {
        if (!await _conversationCoordinator.TogglePinAsync(conversationId))
        {
            _notificationService.ShowError(
                "Pin not changed",
                "The conversation's pinned state could not be changed.");
            return;
        }

        var item = Conversations.FirstOrDefault(c => c.Id == conversationId);
        if (item is null) return;

        item.IsPinned = !item.IsPinned;

        // Keep pinned conversations first, the order the list is loaded in.
        var from = Conversations.IndexOf(item);
        var to = item.IsPinned ? 0 : Conversations.Count(c => c.IsPinned);
        if (from != to)
        {
            Conversations.Move(from, to);
        }
    }

    [RelayCommand]
    private async Task SetConversationFolderAsync(string? folderName)
    {
        if (ActiveConversationId is null) return;
        await _conversationCoordinator.SetConversationFolderAsync(ActiveConversationId.Value, folderName);
        var item = Conversations.FirstOrDefault(c => c.Id == ActiveConversationId);
        if (item is not null) item.FolderName = folderName;
        await RefreshFolderNamesAsync();
    }

    [RelayCommand]
    private async Task FilterByFolderAsync(string? folderName)
    {
        ActiveFolderFilter = folderName;
        if (string.IsNullOrEmpty(folderName)) { await LoadConversationsAsync(); return; }

        var summaries = await _conversationCoordinator.LoadConversationsByFolderAsync(folderName);
        Conversations.Clear();
        foreach (var s in summaries) Conversations.Add(MapToConversationListItem(s));
        OnPropertyChanged(nameof(HasNoConversations));
    }

    private async Task FilterConversationsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) { await LoadConversationsAsync(); return; }

        var results = await _conversationCoordinator.SearchConversationsAsync(query);
        Conversations.Clear();
        foreach (var s in results) Conversations.Add(MapToConversationListItem(s));
        OnPropertyChanged(nameof(HasNoConversations));
    }

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS — Model & Prompt Selection
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Opens the conversation handed over by whatever navigated here (Jump-To, the
    /// command palette). Without this the chosen conversation is discarded at the page
    /// boundary and Chat opens on whatever thread happened to be active.
    /// </summary>
    public async Task ApplyNavigationParameterAsync(object? parameter)
    {
        // The palette's "New Conversation" and the global Ctrl+N arrive with this
        // intent; without it they would open whatever thread was last active.
        if (parameter is string intent && intent == NavigationIntents.NewConversation)
        {
            await NewConversationAsync();
            return;
        }

        if (parameter is not long conversationId || conversationId <= 0)
        {
            return;
        }

        await SelectConversationAsync(conversationId);
    }

    [RelayCommand]
    private async Task SelectModelAsync(string modelId)
    {
        var model = AvailableModels.FirstOrDefault(m => m.Id == modelId);
        if (model is null)
        {
            Log.Debug("Ignoring selection of unknown model {ModelId}", modelId);
            return;
        }

        ActiveModelName = model.Name;

        try
        {
            await _aiService.SetActiveModelAsync(modelId);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist active model selection");
        }
    }

    [RelayCommand]
    private async Task SelectSystemPromptAsync(SystemPromptItem? prompt)
    {
        ShowSystemPromptPicker = false;

        if (prompt is null)
        {
            ActiveSystemPrompt = null;
            ActiveSystemPromptName = null;
            return;
        }

        ActiveSystemPrompt = prompt.Content;
        ActiveSystemPromptName = prompt.Name;

        // A single counter update, awaited here rather than raced against the chat's own
        // queries on the shared context from a background task.
        try { await _systemPromptService.IncrementUsageAsync(prompt.Id); }
        catch (Exception ex) { Log.Warning(ex, "Failed to increment system prompt usage"); }
    }

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS — Per-Message Actions
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task DeleteMessageAsync(ChatMessageItem? message)
    {
        if (message is null) return;

        if (_activeGeneration is { } generation &&
            (ReferenceEquals(message, generation.UserMessage) || ReferenceEquals(message, generation.AssistantMessage)))
        {
            // Its row is still being written; deleting now would race the save.
            NotifyGenerationInProgress();
            return;
        }

        if (message.MessageId <= 0)
        {
            // Never saved (an offline answer, a stopped or failed response), so the screen is
            // the only place it exists.
            Messages.Remove(message);
            OnPropertyChanged(nameof(HasNoMessages));
            _notificationService.ShowInfo(
                "Message removed",
                "This message was never saved, so it was only removed from the screen.");
            return;
        }

        if (!await _messagingCoordinator.DeleteMessageAsync(message.MessageId))
        {
            _notificationService.ShowError(
                "Delete failed",
                "The message could not be deleted and is still in this conversation.");
            return;
        }

        _assistantMessageContextSnapshots.Remove(message.MessageId);
        Messages.Remove(message);
        OnPropertyChanged(nameof(HasNoMessages));
        _notificationService.ShowInfo("Message deleted", "The message has been removed.");
    }

    [RelayCommand]
    private async Task ThumbsUpAsync(ChatMessageItem? message)
    {
        if (message is null || !message.IsAssistant || message.MessageId <= 0) return;
        var newRating = message.FeedbackRating == "positive" ? "none" : "positive";
        message.FeedbackRating = newRating;
        await _messagingCoordinator.SubmitFeedbackAsync(message.MessageId, ActiveConversationId ?? 0, newRating);
    }

    [RelayCommand]
    private async Task ThumbsDownAsync(ChatMessageItem? message)
    {
        if (message is null || !message.IsAssistant || message.MessageId <= 0) return;
        var newRating = message.FeedbackRating == "negative" ? "none" : "negative";
        message.FeedbackRating = newRating;
        await _messagingCoordinator.SubmitFeedbackAsync(message.MessageId, ActiveConversationId ?? 0, newRating);
    }

    [RelayCommand]
    private async Task RegenerateMessageAsync(ChatMessageItem? message)
    {
        if (message is null || !message.IsAssistant) return;
        if (IsGenerating)
        {
            NotifyGenerationInProgress();
            return;
        }

        var msgIndex = Messages.IndexOf(message);
        if (msgIndex < 1) return;
        var userMessage = Messages[msgIndex - 1];
        if (!userMessage.IsUser) return;

        if (msgIndex != Messages.Count - 1)
        {
            // A new answer can only replace the one that closes the thread: anything after an
            // earlier answer was a reply to it.
            _notificationService.ShowInfo(
                "Only the latest response can be regenerated",
                "Use Branch from here on an earlier message to explore a different answer.");
            return;
        }

        if (ActiveConversationId is long conversationId && userMessage.MessageId > 0)
        {
            // The saved prompt is answered again in place: it is not persisted a second time,
            // and the old answer stays (on screen and in the thread) until the new one is saved.
            var replacement = CreateStreamingAssistantMessage();
            Messages[msgIndex] = replacement;

            var promptId = userMessage.MessageId;
            var promptContent = userMessage.Content;
            var systemPrompt = ActiveSystemPrompt;
            var orchestrationMode = OrchestrationMode;

            await RunGenerationAsync(
                new ChatGeneration
                {
                    Epoch = _conversationEpoch,
                    UserMessage = userMessage,
                    AssistantMessage = replacement,
                    ReplacedAssistantMessage = message
                },
                () => _messagingCoordinator.RegenerateResponseAsync(
                    conversationId, promptId, promptContent, systemPrompt, orchestrationMode));
            return;
        }

        // The prompt was never saved (an offline answer, or a send that failed before saving
        // it), so there is no saved exchange to replace: the prompt is simply sent again.
        if (message.MessageId > 0 && !await _messagingCoordinator.DeleteMessageAsync(message.MessageId))
        {
            _notificationService.ShowError(
                "Regenerate failed",
                "The previous response could not be removed, so nothing was regenerated.");
            return;
        }

        Messages.Remove(message);
        Messages.Remove(userMessage);
        OnPropertyChanged(nameof(HasNoMessages));
        await SendContentAsync(userMessage.Content);
    }

    [RelayCommand]
    private void StartEditMessage(ChatMessageItem? message)
    {
        if (message is null || !message.IsUser) return;
        foreach (var msg in Messages.Where(m => m.IsEditing)) msg.IsEditing = false;
        message.EditContent = message.Content;
        message.IsEditing = true;
    }

    [RelayCommand]
    private void CancelEditMessage(ChatMessageItem? message)
    {
        if (message is null) return;
        message.IsEditing = false;
        message.EditContent = string.Empty;
    }

    [RelayCommand]
    private async Task SaveEditMessageAsync(ChatMessageItem? message)
    {
        if (message is null || !message.IsUser || string.IsNullOrWhiteSpace(message.EditContent)) return;
        if (IsGenerating)
        {
            NotifyGenerationInProgress();
            return;
        }

        var msgIndex = Messages.IndexOf(message);
        if (msgIndex < 0) return;
        var newContent = message.EditContent.Trim();

        // Resending persists the new text as a fresh message, so the edited row goes too, with
        // everything after it. The cut starts at the first saved message from the edited one
        // on and is keyed by its persisted id alone: a message never saved has no row to cut
        // from. Nothing is resent unless the cut succeeded.
        var firstSaved = Messages.Skip(msgIndex).FirstOrDefault(m => m.MessageId > 0);
        if (firstSaved is not null && ActiveConversationId is long conversationId &&
            !await _conversationCoordinator.DeleteMessageAndFollowingAsync(conversationId, firstSaved.MessageId))
        {
            _notificationService.ShowError(
                "Edit not sent",
                "The conversation could not be updated, so the edited message was not sent. Try again.");
            return;
        }

        message.IsEditing = false;
        while (Messages.Count > msgIndex)
        {
            var removed = Messages[^1];
            if (removed.MessageId > 0)
            {
                _assistantMessageContextSnapshots.Remove(removed.MessageId);
            }

            Messages.RemoveAt(Messages.Count - 1);
        }

        OnPropertyChanged(nameof(HasNoMessages));
        await SendContentAsync(newContent);
    }

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS — Voice
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task ToggleVoiceRecordingAsync()
    {
        var transcription = await _voiceCoordinator.ToggleRecordingAsync();
        if (!string.IsNullOrWhiteSpace(transcription))
            UserInput = transcription;
    }

    [RelayCommand]
    private async Task PickAudioFileAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
        foreach (var ext in _voiceCoordinator.SupportedFormats) picker.FileTypeFilter.Add(ext);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        var transcription = await _voiceCoordinator.TranscribeFileAsync(file.Path);
        if (!string.IsNullOrWhiteSpace(transcription))
            UserInput = transcription;
    }

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS — Branching
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task BranchFromMessageAsync(long messageId)
    {
        if (ActiveConversationId is null) return;
        var label = PendingBranchLabel;
        PendingBranchLabel = null;
        if (messageId <= 0)
        {
            _notificationService.ShowInfo(
                "Cannot branch here",
                "This message was never saved, so a branch cannot start from it.");
            return;
        }

        var result = await _branchingCoordinator.BranchFromMessageAsync(ActiveConversationId.Value, messageId, label);
        if (result is not null)
        {
            await RefreshBranchTreeAsync();
            _notificationService.ShowInfo("Branch Created", $"Created branch: {result.Title}");
        }
    }

    [RelayCommand]
    private async Task SwitchToBranchAsync(long branchConversationId)
        => await SelectConversationCommand.ExecuteAsync(branchConversationId);

    [RelayCommand]
    private async Task MergeToMainAsync(MergeBranchRequest request)
    {
        await _branchingCoordinator.MergeToMainAsync(request);
        if (request is not null)
            await SelectConversationCommand.ExecuteAsync(request.TargetConversationId);
    }

    [RelayCommand]
    private async Task DeleteBranchAsync(long branchConversationId)
    {
        await _branchingCoordinator.DeleteBranchAsync(branchConversationId);
        await RefreshBranchTreeAsync();
    }

    /// <summary>
    /// Compares the branch on screen with the thread it was branched from. On the main thread no
    /// branch is on screen, so its only branch is compared, or, when there are several, the operator
    /// is asked to open the one to compare. This used to compare the main thread with the first
    /// branch whichever thread was open.
    /// </summary>
    [RelayCommand]
    private void CompareBranches()
    {
        if (BranchTree is not { } tree || tree.Children.Count < 1) return;

        var (branch, thread) = ActiveConversationId is long activeId
            ? FindBranchNode(tree, activeId, parent: null)
            : (null, null);

        if (branch is null || thread is null)
        {
            // The main thread is on screen.
            if (tree.Children.Count > 1)
            {
                _notificationService.ShowInfo(
                    _localization.GetString("Chat_CompareOpenBranchTitle"),
                    _localization.GetString("Chat_CompareOpenBranchBody"));
                return;
            }

            thread = tree;
            branch = tree.Children[0];
        }

        ShowBranchComparison(
            thread,
            branch,
            ReferenceEquals(thread, tree) ? "Main Thread" : BranchTitle(thread),
            BranchTitle(branch));
    }

    /// <summary>Opens the side-by-side comparison window (a test seam).</summary>
    internal Action<ConversationBranchTree, ConversationBranchTree, string, string> ShowBranchComparison { get; set; } =
        static (thread, branch, threadTitle, branchTitle) =>
            new Views.BranchCompareWindow(thread, branch, threadTitle, branchTitle).Activate();

    /// <summary>The node of <paramref name="conversationId"/> in the tree and the thread it branched from.</summary>
    private static (ConversationBranchTree? Node, ConversationBranchTree? Parent) FindBranchNode(
        ConversationBranchTree node, long conversationId, ConversationBranchTree? parent)
    {
        if (node.Conversation?.Id == conversationId)
        {
            return (node, parent);
        }

        foreach (var child in node.Children)
        {
            var found = FindBranchNode(child, conversationId, node);
            if (found.Node is not null)
            {
                return found;
            }
        }

        return (null, null);
    }

    private static string BranchTitle(ConversationBranchTree branch) =>
        !string.IsNullOrWhiteSpace(branch.BranchLabel) ? branch.BranchLabel
        : !string.IsNullOrWhiteSpace(branch.Conversation?.Title) ? branch.Conversation.Title
        : "Branch";

    private async Task RefreshBranchTreeAsync()
    {
        if (ActiveConversationId is null) return;
        BranchTree = await _branchingCoordinator.LoadBranchTreeAsync(ActiveConversationId.Value);
        OnPropertyChanged(nameof(HasBranches));
        ActiveBranches.Clear();
        if (BranchTree is not null)
        {
            foreach (var child in BranchTree.Children) ActiveBranches.Add(child);
        }
        MarkBranchPoints();
    }

    private void MarkBranchPoints()
    {
        foreach (var msg in Messages) { msg.IsBranchPoint = false; msg.BranchCountAtPoint = 0; }
        if (BranchTree is null) return;

        void MarkFromNode(ConversationBranchTree node)
        {
            foreach (var child in node.Children)
            {
                if (child.BranchPointMessageId is not null)
                {
                    var msg = Messages.FirstOrDefault(m => m.MessageId == child.BranchPointMessageId.Value);
                    if (msg is not null) { msg.IsBranchPoint = true; msg.BranchCountAtPoint += 1; }
                }
                MarkFromNode(child);
            }
        }
        MarkFromNode(BranchTree);
    }

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS — UI Helpers
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private void ToggleConversationPane()
        => IsConversationPaneOpen = !IsConversationPaneOpen;

    [RelayCommand]
    private void ToggleContextInspector()
    {
        if (IsContextInspectorOpen)
        {
            IsContextInspectorOpen = false;
            return;
        }

        if (ActiveConversationId.HasValue)
        {
            LoadContextInspectionForConversation(ActiveConversationId.Value);
        }

        IsContextInspectorOpen = true;
    }

    [RelayCommand]
    private void InspectInlineContext(ChatMessageItem? message)
    {
        if (message is null || !message.IsAssistant || message.MessageId <= 0)
        {
            return;
        }

        if (!_assistantMessageContextSnapshots.TryGetValue(message.MessageId, out var snapshot))
        {
            return;
        }

        ApplyContextInspection(
            snapshot,
            updateLatestContextSnapshot: false,
            selectedAssistantResponse: true);
        IsContextInspectorOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanRefreshConversationSummary))]
    private async Task RefreshConversationSummaryAsync()
    {
        if (!ActiveConversationId.HasValue)
        {
            return;
        }

        IsRefreshingConversationSummary = true;
        ConversationSummaryRefreshError = string.Empty;

        try
        {
            var result = await _chatService
                .RefreshConversationSummaryInspectionAsync(ActiveConversationId.Value);

            if (result.Succeeded && result.Snapshot is not null)
            {
                ApplyContextInspection(result.Snapshot);
                return;
            }

            ConversationSummaryRefreshError = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "Summary refresh failed. Keeping the previous summary state."
                : result.ErrorMessage;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to refresh conversation summary for conversation {ConversationId}", ActiveConversationId.Value);
            ConversationSummaryRefreshError = "Summary refresh failed. Keeping the previous summary state.";
        }
        finally
        {
            IsRefreshingConversationSummary = false;
        }
    }

    /// <summary>
    /// Opens a web source listed under an answer. Only http and https addresses are links
    /// (<see cref="WebCitationChip.Link"/>), so nothing else a search result carries is launched.
    /// </summary>
    [RelayCommand]
    private void OpenWebCitation(WebCitationChip? chip)
    {
        if (chip?.Link is not { } link)
        {
            return;
        }

        try
        {
            OpenExternalLink(link);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open web source {Url}", link.AbsoluteUri);
        }
    }

    [RelayCommand]
    private void CopyMessage(string? content)
    {
        if (string.IsNullOrEmpty(content)) return;
        try
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(content);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }
        catch (Exception ex) { Log.Error(ex, "Failed to copy to clipboard"); }
    }

    [RelayCommand]
    private async Task RefreshConnectionAsync()
    {
        ConnectionStatus = "Checking...";
        await CheckConnectionStatusAsync();
        await RefreshPrivacyClaimAsync();
        await LoadAvailableModelsAsync();
    }

    [RelayCommand]
    private void UseSuggestedQuestion(string question)
    {
        if (!string.IsNullOrWhiteSpace(question)) { UserInput = question; SuggestedQuestions.Clear(); }
    }

    // ═══════════════════════════════════════════════════════════════
    // MAPPING HELPERS
    // ═══════════════════════════════════════════════════════════════

    private static ConversationListItem MapToConversationListItem(ConversationSummary s) => new()
    {
        Id = s.Id,
        Title = s.Title,
        LastMessage = s.LastMessage,
        UpdatedAt = s.UpdatedAt,
        IsPinned = s.IsPinned,
        MessageCount = s.MessageCount,
        FolderName = s.FolderName
    };

    private static ChatMessageItem MapToChatMessageItem(MessageSummary ms) => new()
    {
        MessageId = ms.MessageId,
        ConversationId = ms.ConversationId,
        SortOrder = ms.SortOrder,
        Role = ms.Role,
        Content = ms.Content,
        Timestamp = ms.Timestamp,
        IsUser = ms.Role == "user",
        IsAssistant = ms.Role == "assistant",
        IsSystem = ms.Role == "system",
        TokenCount = ms.TokenCount,
        GenerationTimeMs = ms.GenerationTimeMs,
        FeedbackRating = ms.FeedbackRating,
        WebCitations = ms.WebCitations.Count > 0 ? ms.WebCitations : null
    };

    private void ReapplyInlineContextNote(ChatMessageItem message)
    {
        if (!message.IsAssistant || message.MessageId <= 0)
        {
            ClearInlineContextNote(message);
            return;
        }

        if (_assistantMessageContextSnapshots.TryGetValue(message.MessageId, out var snapshot))
        {
            ApplyInlineContextNote(message, snapshot);
            return;
        }

        ClearInlineContextNote(message);
    }

    private static void ApplyInlineContextNote(
        ChatMessageItem message,
        ChatContextInspectionSnapshot? snapshot)
    {
        if (!message.IsAssistant || snapshot is null)
        {
            ClearInlineContextNote(message);
            return;
        }

        message.InlineContextStoryText = snapshot.ContextStoryText;
        message.InlineContextStorySourceChips = snapshot.ContextStorySourceChips
            .Select(chip => chip.Label)
            .ToArray();
    }

    private static void ClearInlineContextNote(ChatMessageItem message)
    {
        message.InlineContextStoryText = string.Empty;
        message.InlineContextStorySourceChips = Array.Empty<string>();
    }

    private static string BuildRelativeTimeLabel(DateTime timestamp)
    {
        var elapsed = DateTime.UtcNow - timestamp;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)} min ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{Math.Max(1, (int)elapsed.TotalHours)} hr ago";
        }

        var days = Math.Max(1, (int)elapsed.TotalDays);
        return days == 1 ? "1 day ago" : $"{days} days ago";
    }

    private void LoadContextInspectionForConversation(long conversationId) =>
        ApplyContextInspection(_chatService.GetLatestContextInspection(conversationId));

    private void ApplyContextInspection(
        ChatContextInspectionSnapshot? snapshot,
        bool updateLatestContextSnapshot = true,
        bool selectedAssistantResponse = false)
    {
        if (snapshot is null)
        {
            ResetContextInspection(updateLatestContextSnapshot);
            return;
        }

        if (updateLatestContextSnapshot)
        {
            _latestContextInspection = snapshot;
        }

        HasContextInspection = true;
        HasLimitedContextInspection = snapshot.HasLimitedVisibility;
        ContextInspectionStatus = BuildContextInspectionStatus(snapshot, selectedAssistantResponse);
        ContextCapturedAt = BuildRelativeTimeLabel(snapshot.CapturedAt);
        ContextStoryText = snapshot.ContextStoryText;
        ContextStorySourceChips = new ObservableCollection<ChatContextStorySourceDisplayItem>(
            snapshot.ContextStorySourceChips.Select(chip => new ChatContextStorySourceDisplayItem
            {
                Label = chip.Label
            }));
        ContextSelectedMessages = snapshot.Diagnostics.SelectedMessageCount.ToString();
        ContextAnchorMessages = snapshot.Diagnostics.AnchorMessageCount.ToString();
        ContextOverflowMessages = snapshot.Diagnostics.OverflowMessageCount.ToString();
        ContextEstimatedPromptTokens = snapshot.Diagnostics.EstimatedPromptTokens.ToString();
        ContextEstimatedMessageTokens = snapshot.Diagnostics.EstimatedMessageTokens.ToString();
        ContextAssemblyMode = snapshot.HasLimitedVisibility
            ? "Limited visibility"
            : snapshot.Diagnostics.UsedLegacyFallback
                ? "Legacy fallback"
                : snapshot.Diagnostics.UsedLexicalFallback
                    ? "Lexical fallback"
                    : "Structured context assembly";
        ContextAssemblyExplanation = snapshot.AssemblyExplanation;
        ContextCompressionExplanation = snapshot.CompressionExplanation;
        ContextRecallExplanation = snapshot.RecallExplanation;

        if (snapshot.Summary is null)
        {
            HasContextSummary = false;
            HasContextSummaryKeyPoints = false;
            ContextSummaryStatus = "No durable summary snapshot was available for this response.";
            ContextSummaryPreview = string.Empty;
            ContextSummaryFreshness = string.Empty;
            ContextSummaryKeyPoints = new ObservableCollection<string>();
        }
        else
        {
            HasContextSummary = true;
            ContextSummaryStatus = snapshot.Summary.IsStale
                ? "Durable summary exists but may lag behind the latest thread."
                : "Durable summary was current when this response was assembled.";
            ContextSummaryPreview = string.IsNullOrWhiteSpace(snapshot.Summary.PreviewText)
                ? snapshot.Summary.SummaryText
                : snapshot.Summary.PreviewText;
            ContextSummaryFreshness = snapshot.Summary.IsStale && snapshot.Summary.PendingMessageCount > 0
                ? $"{snapshot.Summary.PendingMessageCount} newer message{(snapshot.Summary.PendingMessageCount == 1 ? string.Empty : "s")} not yet folded in"
                : $"Captured {BuildRelativeTimeLabel(snapshot.Summary.GeneratedAt)}";
            ContextSummaryKeyPoints = new ObservableCollection<string>(snapshot.Summary.KeyPoints);
            HasContextSummaryKeyPoints = ContextSummaryKeyPoints.Count > 0;
        }

        ContextRecallItems = new ObservableCollection<ChatContextRecallDisplayItem>(
            snapshot.RecallMatches.Select(match => new ChatContextRecallDisplayItem
            {
                ConversationLabel = $"{match.ConversationTitle} · {(match.Role == "assistant" ? "Assistant" : "User")}",
                PreviewText = match.ContentPreview,
                SimilarityLabel = $"{Math.Round(match.Similarity * 100)}% match",
                TimestampLabel = BuildRelativeTimeLabel(match.Timestamp)
            }));
        HasContextRecallItems = ContextRecallItems.Count > 0;
        ContextRecallStatus = HasContextRecallItems
            ? $"{ContextRecallItems.Count} recalled message{(ContextRecallItems.Count == 1 ? string.Empty : "s")} used"
            : snapshot.RecallExplanation;
        NotifyConversationIntelligenceStripChanged();
    }

    private void ResetContextInspection(bool updateLatestContextSnapshot = true)
    {
        if (updateLatestContextSnapshot)
        {
            _latestContextInspection = null;
        }

        HasContextInspection = false;
        HasLimitedContextInspection = false;
        ContextInspectionStatus = "No generation context captured yet.";
        ContextCapturedAt = "No context captured";
        ContextStoryText = ActiveConversationId.HasValue
            ? "No context story is available until Agent-X assembles a response for this conversation."
            : string.Empty;
        ContextStorySourceChips = new ObservableCollection<ChatContextStorySourceDisplayItem>();
        ContextSelectedMessages = "0";
        ContextAnchorMessages = "0";
        ContextOverflowMessages = "0";
        ContextEstimatedPromptTokens = "0";
        ContextEstimatedMessageTokens = "0";
        ContextAssemblyMode = "No context available";
        ContextAssemblyExplanation = string.Empty;
        ContextCompressionExplanation = string.Empty;
        ContextRecallExplanation = string.Empty;
        ContextSummaryStatus = "No durable summary captured yet.";
        ContextSummaryPreview = string.Empty;
        ContextSummaryFreshness = string.Empty;
        ContextSummaryKeyPoints = new ObservableCollection<string>();
        HasContextSummary = false;
        HasContextSummaryKeyPoints = false;
        ContextRecallStatus = "No durable recall context captured yet.";
        ContextRecallItems = new ObservableCollection<ChatContextRecallDisplayItem>();
        HasContextRecallItems = false;
        NotifyConversationIntelligenceStripChanged();
    }

    private static string BuildContextInspectionStatus(
        ChatContextInspectionSnapshot snapshot,
        bool selectedAssistantResponse)
    {
        if (snapshot.HasLimitedVisibility)
        {
            var limitedVisibilityLabel =
                $"limited visibility: {snapshot.LimitedVisibilityReason?.Replace('_', ' ') ?? "reduced path"}";
            return selectedAssistantResponse
                ? $"Context captured for the selected assistant response ({limitedVisibilityLabel})."
                : $"Limited visibility: {snapshot.LimitedVisibilityReason?.Replace('_', ' ') ?? "reduced path"}";
        }

        return selectedAssistantResponse
            ? "Context captured for the selected assistant response."
            : "Latest response context captured";
    }

    private void NotifyConversationIntelligenceStripChanged()
    {
        OnPropertyChanged(nameof(HasConversationIntelligenceStrip));
        OnPropertyChanged(nameof(ConversationIntelligenceBadgeText));
        OnPropertyChanged(nameof(ConversationIntelligenceStatusText));
        OnPropertyChanged(nameof(ConversationIntelligenceStoryText));
        OnPropertyChanged(nameof(ConversationIntelligenceStorySourceChips));
        OnPropertyChanged(nameof(HasConversationIntelligenceStory));
        OnPropertyChanged(nameof(HasConversationIntelligenceStorySourceChips));
        OnPropertyChanged(nameof(ConversationIntelligenceIsCurrent));
        OnPropertyChanged(nameof(ConversationIntelligenceIsStale));
        OnPropertyChanged(nameof(ConversationIntelligenceIsPending));
        OnPropertyChanged(nameof(ConversationIntelligenceIsUnavailable));
        OnPropertyChanged(nameof(ShowConversationSummaryRefreshAction));
        OnPropertyChanged(nameof(ConversationSummaryRefreshActionText));
        RefreshConversationSummaryCommand.NotifyCanExecuteChanged();
    }

    private void NotifyConversationSummaryRefreshStateChanged()
    {
        OnPropertyChanged(nameof(HasConversationSummaryRefreshError));
        OnPropertyChanged(nameof(HasConversationSummaryRefreshStatus));
        OnPropertyChanged(nameof(ConversationSummaryRefreshStatusText));
        OnPropertyChanged(nameof(ConversationSummaryRefreshActionText));
        OnPropertyChanged(nameof(ConversationIntelligenceStatusText));
        OnPropertyChanged(nameof(ShowConversationSummaryRefreshAction));
        OnPropertyChanged(nameof(CanRefreshConversationSummary));
        RefreshConversationSummaryCommand.NotifyCanExecuteChanged();
    }

    // ═══════════════════════════════════════════════════════════════
    // DISPOSAL
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Detaches this view model from the singleton coordinators. The coordinators themselves
    /// belong to the DI container and serve the next chat page, so they are not disposed here:
    /// disposing the voice coordinator from a view model left every later chat without voice.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        UnsubscribeFromCoordinatorEvents();
        _activeGeneration = null;

        // The page records the open conversation when it leaves the screen; nothing is written
        // while the view model is being torn down.
        _conversationEngagement.Discard();
        Log.Debug("ChatViewModel disposed");
    }

    /// <summary>
    /// One generation started from this view model: the bubbles it streams into and the
    /// conversation epoch it belongs to.
    /// </summary>
    private sealed class ChatGeneration
    {
        public required int Epoch { get; init; }
        public required ChatMessageItem UserMessage { get; init; }
        public required ChatMessageItem AssistantMessage { get; init; }

        /// <summary>
        /// For a regeneration, the answer being replaced. It stays in the thread until the new
        /// one is saved, and comes back on screen when the new one does not arrive.
        /// </summary>
        public ChatMessageItem? ReplacedAssistantMessage { get; init; }

        public bool IsRegeneration => ReplacedAssistantMessage is not null;

        /// <summary>False while an abandoned earlier generation is still winding down.</summary>
        public bool IsLive { get; set; }

        /// <summary>Whether the completion was applied, from the event or from the result.</summary>
        public bool IsCompleted { get; set; }
    }

    /// <summary>
    /// A completed generation as reported by the coordinator's event or by its result.
    /// </summary>
    private sealed record CompletionData(
        long? ConversationId,
        string? ConversationTitle,
        string ResponseContent,
        int TokenCount,
        double GenerationTimeMs,
        ChatContextInspectionSnapshot? ContextInspection,
        long? AssistantMessageId,
        int? AssistantMessageSortOrder,
        long? UserMessageId,
        int? UserMessageSortOrder,
        IReadOnlyList<WebCitation>? WebCitations)
    {
        public static CompletionData From(StreamingCompletedEventArgs e) => new(
            e.ConversationId,
            e.ConversationTitle,
            e.ResponseContent,
            e.TokenCount,
            e.GenerationTimeMs,
            e.ContextInspection,
            e.AssistantMessageId,
            e.AssistantMessageSortOrder,
            e.UserMessageId,
            e.UserMessageSortOrder,
            e.WebCitations);

        public static CompletionData From(SendMessageResult result) => new(
            result.ConversationId,
            result.ConversationTitle,
            result.ResponseContent,
            result.TokenCount,
            result.GenerationTimeMs,
            result.ContextInspection,
            result.AssistantMessageId,
            result.AssistantMessageSortOrder,
            result.UserMessageId,
            result.UserMessageSortOrder,
            result.WebCitations);
    }
}

public sealed class ChatContextRecallDisplayItem
{
    public string ConversationLabel { get; init; } = string.Empty;
    public string PreviewText { get; init; } = string.Empty;
    public string SimilarityLabel { get; init; } = string.Empty;
    public string TimestampLabel { get; init; } = string.Empty;
}

public sealed class ChatContextStorySourceDisplayItem
{
    public string Label { get; init; } = string.Empty;
}
