using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using AgentX.Core.Services.TemporalIdentity;
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
                OnPropertyChanged(nameof(ResearchModeTooltip));
        }
    }

    public string ResearchModeTooltip => IsResearchMode
        ? "Research Mode ON — answers include web sources"
        : "Research Mode OFF — local vault only";

    [RelayCommand]
    private void ToggleResearchMode() => IsResearchMode = !IsResearchMode;

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

    // ── Streaming assistant message (for token-by-token updates) ──
    private ChatMessageItem? _streamingAssistantMessage;
    private ChatContextInspectionSnapshot? _latestContextInspection;
    private readonly Dictionary<long, ChatContextInspectionSnapshot> _assistantMessageContextSnapshots = new();

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
        ITemporalIdentityService temporalIdentity)
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
        _voiceCoordinator.RecordingStateChanged += (s, isRec) => IsRecording = isRec;
        _voiceCoordinator.TranscribingStateChanged += (s, isTrans) => IsTranscribing = isTrans;
        _voiceCoordinator.StatusChanged += (s, msg) => VoiceStatusMessage = msg;
        _voiceCoordinator.NotificationRequested += OnVoiceNotification;

        // ── BranchingCoordinator ─────────────────────────────────
        _branchingCoordinator.BranchTreeChanged += OnBranchTreeChanged;
        _branchingCoordinator.NotificationRequested += OnBranchingNotification;
    }

    private void OnTokenReceived(object? sender, string token)
    {
        if (_streamingAssistantMessage is not null)
        {
            _streamingAssistantMessage.Content += token;
            CurrentStreamingResponse = _streamingAssistantMessage.Content;
            OnPropertyChanged(nameof(Messages));
        }
    }

    private void OnStreamingCompleted(object? sender, StreamingCompletedEventArgs e)
    {
        if (_streamingAssistantMessage is not null)
        {
            _streamingAssistantMessage.IsStreaming = false;
            _streamingAssistantMessage.TokenCount = e.TokenCount;
            _streamingAssistantMessage.GenerationTimeMs = e.GenerationTimeMs;
            if (e.AssistantMessageId.HasValue)
            {
                _streamingAssistantMessage.MessageId = e.AssistantMessageId.Value;
            }

            ApplyInlineContextNote(_streamingAssistantMessage, e.ContextInspection);
            if (e.AssistantMessageId.HasValue && e.ContextInspection is not null)
            {
                _assistantMessageContextSnapshots[e.AssistantMessageId.Value] = e.ContextInspection;
            }
        }

        TokenCount += e.TokenCount;
        GenerationTimeMs = e.GenerationTimeMs;
        IsGenerating = false;
        CurrentStreamingResponse = string.Empty;

        // Update conversation ID if newly created
        if (e.ConversationId.HasValue && ActiveConversationId != e.ConversationId)
        {
            ActiveConversationId = e.ConversationId;
            ActiveConversationTitle = e.ConversationTitle ?? ActiveConversationTitle;

            Conversations.Insert(0, new ConversationListItem
            {
                Id = e.ConversationId.Value,
                Title = ActiveConversationTitle,
                LastMessage = e.ResponseContent.Length > 80
                    ? e.ResponseContent[..80] + "..."
                    : e.ResponseContent,
                UpdatedAt = DateTime.UtcNow,
                IsPinned = false,
                MessageCount = 0
            });
            OnPropertyChanged(nameof(HasNoConversations));
        }

        // Update sidebar last message
        if (ActiveConversationId.HasValue)
        {
            var convItem = Conversations.FirstOrDefault(c => c.Id == ActiveConversationId);
            if (convItem is not null && !string.IsNullOrEmpty(e.ResponseContent))
            {
                convItem.LastMessage = e.ResponseContent.Length > 80
                    ? e.ResponseContent[..80] + "..."
                    : e.ResponseContent;
            }
        }

        // Temporal Identity: Learn from message and detect insights (non-blocking background)
        if (e.UserMessageId.HasValue && e.ConversationId.HasValue)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _temporalIdentity.LearnFromMessageAsync(e.UserMessageId.Value);
                    await _temporalIdentity.DetectInsightsAsync(e.ConversationId.Value);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to process temporal identity for message {MessageId}", e.UserMessageId.Value);
                }
            });
        }

        _streamingAssistantMessage = null;
        ApplyContextInspection(e.ContextInspection);

        // Load follow-ups and update memory (non-blocking)
        _ = InitializePostSendAsync();
    }

    private void OnGenerationError(object? sender, string errorMsg)
    {
        if (_streamingAssistantMessage is not null)
        {
            _streamingAssistantMessage.Content = errorMsg;
            _streamingAssistantMessage.IsStreaming = false;
        }
        IsGenerating = false;
        CurrentStreamingResponse = string.Empty;
        _streamingAssistantMessage = null;
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
        if (string.IsNullOrWhiteSpace(UserInput)) return;

        var userContent = UserInput.Trim();
        UserInput = string.Empty;

        Log.Debug("Sending message: {MessagePreview}", userContent.Length > 50
            ? userContent[..50] + "..." : userContent);

        // Add user message to UI
        Messages.Add(new ChatMessageItem
        {
            Role = "user",
            Content = userContent,
            Timestamp = DateTime.UtcNow,
            IsUser = true,
            IsAssistant = false,
            IsSystem = false,
            IsStreaming = false
        });
        OnPropertyChanged(nameof(HasNoMessages));

        // Create streaming placeholder
        _streamingAssistantMessage = new ChatMessageItem
        {
            Role = "assistant",
            Content = "",
            Timestamp = DateTime.UtcNow,
            IsUser = false,
            IsAssistant = true,
            IsSystem = false,
            IsStreaming = true
        };
        Messages.Add(_streamingAssistantMessage);

        IsGenerating = true;

        // Delegate to messaging coordinator
        var result = OrchestrationMode == ChatOrchestrationMode.Standard
            ? await _messagingCoordinator.SendMessageAsync(
                userContent, ActiveConversationId, ActiveSystemPrompt,
                _aiService.ActiveModelId, IsResearchMode)
            : await _messagingCoordinator.SendMessageAsync(
                userContent, ActiveConversationId, ActiveSystemPrompt,
                _aiService.ActiveModelId, IsResearchMode, OrchestrationMode);

        if (result.ContextInspection is not null)
        {
            ApplyContextInspection(result.ContextInspection);
        }

        // Handle cancellation/error inline responses
        if (result.WasCancelled && _streamingAssistantMessage is not null)
            _streamingAssistantMessage.Content += "\n\n[Generation stopped]";

        if (result.HadError && _streamingAssistantMessage is not null)
            _streamingAssistantMessage.Content = result.ResponseContent;
    }

    [RelayCommand]
    private async Task StopGenerationAsync()
        => await _messagingCoordinator.StopGenerationAsync();

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS — Conversation
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task NewConversationAsync()
    {
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
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task DeleteConversationAsync(long conversationId)
    {
        await _conversationCoordinator.DeleteConversationAsync(conversationId);
        var item = Conversations.FirstOrDefault(c => c.Id == conversationId);
        if (item is not null)
        {
            Conversations.Remove(item);
            OnPropertyChanged(nameof(HasNoConversations));
            if (ActiveConversationId == conversationId)
                await NewConversationAsync();
        }
    }

    [RelayCommand]
    private async Task SelectConversationAsync(long conversationId)
    {
        var item = Conversations.FirstOrDefault(c => c.Id == conversationId);
        if (item is null) return;

        ActiveConversationId = conversationId;
        ActiveConversationTitle = item.Title;
        Messages.Clear();
        ConversationSummaryRefreshError = string.Empty;

        var messageSummaries = await _conversationCoordinator.LoadMessagesAsync(conversationId);
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
        await _conversationCoordinator.TogglePinAsync(conversationId);
        var item = Conversations.FirstOrDefault(c => c.Id == conversationId);
        if (item is not null) item.IsPinned = !item.IsPinned;
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

    [RelayCommand]
    private void SelectModel(string modelId)
    {
        var model = AvailableModels.FirstOrDefault(m => m.Id == modelId);
        if (model is not null)
        {
            ActiveModelName = model.Name;
            _ = Task.Run(async () =>
            {
                try { await _aiService.SetActiveModelAsync(modelId); }
                catch (Exception ex) { Log.Warning(ex, "Failed to persist active model selection"); }
            });
        }
    }

    [RelayCommand]
    private void SelectSystemPrompt(SystemPromptItem? prompt)
    {
        if (prompt is null) { ActiveSystemPrompt = null; ActiveSystemPromptName = null; }
        else
        {
            ActiveSystemPrompt = prompt.Content;
            ActiveSystemPromptName = prompt.Name;
            _ = Task.Run(async () =>
            {
                try { await _systemPromptService.IncrementUsageAsync(prompt.Id); }
                catch (Exception ex) { Log.Warning(ex, "Failed to increment system prompt usage"); }
            });
        }
        ShowSystemPromptPicker = false;
    }

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS — Per-Message Actions
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task DeleteMessageAsync(ChatMessageItem? message)
    {
        if (message is null) return;
        await _messagingCoordinator.DeleteMessageAsync(message.MessageId);
        if (message.MessageId > 0)
        {
            _assistantMessageContextSnapshots.Remove(message.MessageId);
        }
        Messages.Remove(message);
        OnPropertyChanged(nameof(HasNoMessages));
        _notificationService.ShowInfo("Message deleted", "The message has been removed.");
    }

    [RelayCommand]
    private async Task SubmitFeedbackAsync(ChatMessageItem? message)
    {
        if (message is null || !message.IsAssistant || message.MessageId <= 0) return;
        var newRating = message.FeedbackRating switch
        {
            "positive" => "negative",
            "negative" => "none",
            _ => "positive"
        };
        message.FeedbackRating = newRating;
        await _messagingCoordinator.SubmitFeedbackAsync(message.MessageId, ActiveConversationId ?? 0, newRating);
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
        var msgIndex = Messages.IndexOf(message);
        if (msgIndex < 1) return;
        var userMessage = Messages[msgIndex - 1];
        if (!userMessage.IsUser) return;

        Messages.Remove(message);
        if (message.MessageId > 0)
        {
            _assistantMessageContextSnapshots.Remove(message.MessageId);
            await _messagingCoordinator.DeleteMessageAsync(message.MessageId);
        }

        UserInput = userMessage.Content;
        Messages.Remove(userMessage);
        OnPropertyChanged(nameof(HasNoMessages));
        await SendMessageAsync();
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
        var newContent = message.EditContent.Trim();
        message.IsEditing = false;
        message.Content = newContent;

        if (message.MessageId > 0)
        {
            try { await _conversationCoordinator.UpdateMessageContentAsync(message.MessageId, newContent); }
            catch (Exception ex) { Log.Warning(ex, "Failed to update message content in database"); }
        }

        var msgIndex = Messages.IndexOf(message);
        if (msgIndex >= 0 && ActiveConversationId is not null && message.SortOrder >= 0)
        {
            try { await _conversationCoordinator.DeleteMessagesAfterAsync(ActiveConversationId.Value, message.SortOrder); }
            catch (Exception ex) { Log.Warning(ex, "Failed to delete subsequent messages"); }
            while (Messages.Count > msgIndex + 1) Messages.RemoveAt(Messages.Count - 1);
        }

        UserInput = newContent;
        Messages.Remove(message);
        OnPropertyChanged(nameof(HasNoMessages));
        await SendMessageAsync();
    }

    [RelayCommand]
    private async Task RegenerateAsync()
    {
        if (Messages.Count < 2) return;
        var lastMessage = Messages.LastOrDefault();
        if (lastMessage is { IsAssistant: true }) Messages.Remove(lastMessage);
        var lastUserMessage = Messages.LastOrDefault(m => m.IsUser);
        if (lastUserMessage is not null)
        {
            UserInput = lastUserMessage.Content;
            Messages.Remove(lastUserMessage);
            OnPropertyChanged(nameof(HasNoMessages));
            await SendMessageAsync();
        }
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
        var result = await _branchingCoordinator.BranchFromMessageAsync(ActiveConversationId.Value, messageId, label);
        if (result is not null)
        {
            await RefreshBranchTreeAsync();
            _notificationService.ShowInfo("Branch Created", $"Created branch: {result.Title}");
        }
    }

    [RelayCommand]
    private async Task LoadBranchTreeAsync()
        => await RefreshBranchTreeAsync();

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

    [RelayCommand]
    private void CompareBranches()
    {
        if (BranchTree is null || BranchTree.Children.Count < 1) return;
        var window = new Views.BranchCompareWindow(
            BranchTree, BranchTree.Children[0], "Main Thread",
            BranchTree.Children[0].BranchLabel ?? "Branch");
        window.Activate();
    }

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

    [RelayCommand]
    private async Task ClearConversationAsync()
    {
        Messages.Clear(); TokenCount = 0; GenerationTimeMs = 0;
        CurrentStreamingResponse = string.Empty;
        OnPropertyChanged(nameof(HasNoMessages));
        await Task.CompletedTask;
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
    private void ExportConversationToClipboard()
    {
        if (Messages.Count == 0) return;
        try
        {
            var markdown = BuildConversationMarkdown();
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(markdown);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }
        catch (Exception ex) { Log.Error(ex, "Failed to export conversation to clipboard"); }
    }

    [RelayCommand]
    private async Task ExportConversationToFileAsync()
    {
        if (Messages.Count == 0) return;
        try
        {
            var markdown = BuildConversationMarkdown();
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Markdown", new List<string> { ".md" });
            picker.FileTypeChoices.Add("Text", new List<string> { ".txt" });
            picker.SuggestedFileName = SanitizeFileName(ActiveConversationTitle);
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSaveFileAsync();
            if (file != null) await Windows.Storage.FileIO.WriteTextAsync(file, markdown);
        }
        catch (Exception ex) { Log.Error(ex, "Failed to export conversation to file"); }
    }

    [RelayCommand]
    private async Task RefreshConnectionAsync()
    {
        ConnectionStatus = "Checking...";
        await CheckConnectionStatusAsync();
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
        FeedbackRating = ms.FeedbackRating
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

    private string BuildConversationMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {ActiveConversationTitle}");
        sb.AppendLine($"*Exported from Agent-X on {DateTime.Now:MMMM d, yyyy 'at' h:mm tt}*");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(ActiveSystemPromptName))
            sb.AppendLine($"**System Prompt:** {ActiveSystemPromptName}");
        sb.AppendLine("---");
        foreach (var msg in Messages)
        {
            var roleLabel = msg.Role switch { "user" => "**You**", "assistant" => "**Agent-X**", _ => $"**{msg.Role}**" };
            sb.AppendLine($"### {roleLabel}\n*{msg.FormattedTime}*\n\n{msg.Content}\n");
            if (msg.IsAssistant && msg.TokenCount > 0)
                sb.AppendLine($"_{msg.FormattedTokens} | {msg.FormattedTokenSpeed}_\n");
            sb.AppendLine("---");
        }
        return sb.ToString();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "conversation" : sanitized;
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

    public void Dispose()
    {
        if (_voiceCoordinator is IDisposable voiceDisposable) voiceDisposable.Dispose();
        Log.Debug("ChatViewModel disposed");
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
