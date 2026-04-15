using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Audio;
using AgentX.Core.Services.Audio.Models;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Chat.Models;
using AgentX.Core.Services.Feedback;
using AgentX.App.Helpers;
using AgentX.App.Services;
using AgentX.App.Views;
using NAudio.Wave;
using Serilog;

namespace AgentX.App.ViewModels;

// ═══════════════════════════════════════════════════════════════════════════
// CHAT VIEW MODEL — Comprehensive ViewModel for the AI Chat experience.
//
// Accepts IChatService, IConversationService, IAiService, IModelManager,
// and ISystemPromptService via DI. Falls back to offline/demo state
// gracefully when services encounter errors.
// ═══════════════════════════════════════════════════════════════════════════

public partial class ChatViewModel : ObservableObject, IDisposable
{
    // ── Page State ─────────────────────────────────────────────
    [ObservableProperty] private string _pageTitle = "AI Chat";
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
            }
        }
    }

    public string ResearchModeTooltip => IsResearchMode
        ? "Research Mode ON — answers include web sources"
        : "Research Mode OFF — local vault only";

    [RelayCommand]
    private void ToggleResearchMode()
    {
        IsResearchMode = !IsResearchMode;
    }

    // ── Search ─────────────────────────────────────────────────
    [ObservableProperty] private string _conversationSearchQuery = string.Empty;

    // ── Memory ────────────────────────────────────────────────
    [ObservableProperty] private int _memoryCount;

    // ── Voice Input ───────────────────────────────────────────
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isTranscribing;
    [ObservableProperty] private string _voiceStatusMessage = string.Empty;

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

    // ── Services ──────────────────────────────────────────────
    private readonly IChatService _chatService;
    private readonly IConversationService _conversationService;
    private readonly IAiService _aiService;
    private readonly IModelManager _modelManager;
    private readonly ISystemPromptService _systemPromptService;
    private readonly IConversationMemoryService _memoryService;
    private readonly IFeedbackService _feedbackService;
    private readonly INotificationService _notificationService;
    private readonly ITranscriptionService _transcriptionService;
    private readonly IConversationBranchService _branchService;

    private CancellationTokenSource? _generationCts;

    // ── Voice Recording Resources ──────────────────────────────
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _waveWriter;
    private string? _currentRecordingPath;
    private TaskCompletionSource? _recordingStopTcs;

    public ChatViewModel(
        IChatService chatService,
        IConversationService conversationService,
        IAiService aiService,
        IModelManager modelManager,
        ISystemPromptService systemPromptService,
        IConversationMemoryService memoryService,
        IFeedbackService feedbackService,
        INotificationService notificationService,
        ITranscriptionService transcriptionService,
        IConversationBranchService branchService)
    {
        _chatService = chatService;
        _conversationService = conversationService;
        _aiService = aiService;
        _modelManager = modelManager;
        _systemPromptService = systemPromptService;
        _memoryService = memoryService;
        _feedbackService = feedbackService;
        _notificationService = notificationService;
        _transcriptionService = transcriptionService;
        _branchService = branchService;
        Log.Debug("ChatViewModel created with services");
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
            await LoadFolderNamesAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize ChatViewModel");
        }

        Log.Information("ChatViewModel initialized");
    }

    private async Task LoadConversationsAsync()
    {
        Conversations.Clear();

        try
        {
            var conversations = await _conversationService.GetAllConversationsAsync();
            foreach (var conv in conversations)
            {
                var lastMsg = conv.Messages?.OrderByDescending(m => m.SortOrder).FirstOrDefault();
                Conversations.Add(new ConversationListItem
                {
                    Id = conv.Id,
                    Title = conv.Title,
                    LastMessage = lastMsg?.Content ?? string.Empty,
                    UpdatedAt = conv.UpdatedAt,
                    IsPinned = conv.IsPinned,
                    MessageCount = conv.MessageCount,
                    FolderName = conv.FolderName
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load conversations from service");
        }

        OnPropertyChanged(nameof(HasNoConversations));
    }

    private async Task CheckConnectionStatusAsync()
    {
        try
        {
            var connected = await _aiService.ActiveProvider.CheckConnectionAsync();
            IsConnected = connected;
            ConnectionStatus = connected ? "Connected" : "Disconnected";

            if (connected && !string.IsNullOrEmpty(_aiService.ActiveModelId))
            {
                ActiveModelName = _aiService.ActiveModelId;
            }
            else
            {
                ActiveModelName = "No model selected";
            }
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
            foreach (var model in models)
            {
                AvailableModels.Add(model);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load available models");
        }
    }

    private async Task LoadSystemPromptsAsync()
    {
        SystemPrompts.Clear();

        try
        {
            // Ensure built-in prompts exist
            await _systemPromptService.SeedBuiltInPromptsAsync();

            var prompts = await _systemPromptService.GetAllPromptsAsync();
            foreach (var prompt in prompts)
            {
                SystemPrompts.Add(new SystemPromptItem
                {
                    Id = prompt.Id,
                    Name = prompt.Name,
                    Content = prompt.Content,
                    Category = prompt.Category,
                    IsBuiltIn = prompt.IsBuiltIn,
                    IsFavorite = prompt.IsFavorite
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load system prompts");
        }
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
    {
        OnPropertyChanged(nameof(IsVoiceActive));
    }

    partial void OnActiveSystemPromptNameChanged(string? value)
    {
        OnPropertyChanged(nameof(HasActiveSystemPrompt));
    }

    partial void OnConversationSearchQueryChanged(string value)
    {
        _ = FilterConversationsAsync(value);
    }

    private async Task FilterConversationsAsync(string query)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                // Reload all conversations
                await LoadConversationsAsync();
                return;
            }

            var results = await _conversationService.SearchConversationsAsync(query.Trim());
            Conversations.Clear();

            foreach (var conv in results)
            {
                var lastMsg = conv.Messages?.OrderByDescending(m => m.SortOrder).FirstOrDefault();
                Conversations.Add(new ConversationListItem
                {
                    Id = conv.Id,
                    Title = conv.Title,
                    LastMessage = lastMsg?.Content ?? string.Empty,
                    UpdatedAt = conv.UpdatedAt,
                    IsPinned = conv.IsPinned,
                    MessageCount = conv.MessageCount,
                    FolderName = conv.FolderName
                });
            }

            OnPropertyChanged(nameof(HasNoConversations));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to search conversations");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(UserInput)) return;

        var userContent = UserInput.Trim();
        UserInput = string.Empty;

        Log.Debug("Sending message: {MessagePreview}", userContent.Length > 50
            ? userContent[..50] + "..."
            : userContent);

        // Add user message to the message list
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

        // Create a placeholder for the streaming assistant response
        var assistantMessage = new ChatMessageItem
        {
            Role = "assistant",
            Content = "",
            Timestamp = DateTime.UtcNow,
            IsUser = false,
            IsAssistant = true,
            IsSystem = false,
            IsStreaming = true
        };
        Messages.Add(assistantMessage);

        IsGenerating = true;
        _generationCts = new CancellationTokenSource();

        try
        {
            // Ensure we have a conversation to send messages in
            if (ActiveConversationId is null)
            {
                try
                {
                    var newConv = await _conversationService.CreateConversationAsync(
                        title: userContent.Length > 60 ? userContent[..60] + "..." : userContent,
                        systemPrompt: ActiveSystemPrompt,
                        modelId: _aiService.ActiveModelId);
                    ActiveConversationId = newConv.Id;
                    ActiveConversationTitle = newConv.Title;

                    // Add to the conversation sidebar
                    Conversations.Insert(0, new ConversationListItem
                    {
                        Id = newConv.Id,
                        Title = newConv.Title,
                        LastMessage = userContent,
                        UpdatedAt = DateTime.UtcNow,
                        IsPinned = false,
                        MessageCount = 0
                    });
                    OnPropertyChanged(nameof(HasNoConversations));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to create conversation, streaming without persistence");
                }
            }

            // Try streaming via IChatService (which persists and streams)
            if (ActiveConversationId is not null && IsConnected)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int tokCount = 0;

                await foreach (var token in _chatService.SendMessageAsync(
                    ActiveConversationId.Value, userContent, _generationCts.Token))
                {
                    assistantMessage.Content += token;
                    CurrentStreamingResponse = assistantMessage.Content;
                    OnPropertyChanged(nameof(Messages));
                    tokCount++;
                }

                sw.Stop();
                assistantMessage.TokenCount = tokCount;
                assistantMessage.GenerationTimeMs = sw.Elapsed.TotalMilliseconds;
                TokenCount += tokCount;
                GenerationTimeMs = sw.Elapsed.TotalMilliseconds;

                // Update sidebar
                var convItem = Conversations.FirstOrDefault(c => c.Id == ActiveConversationId);
                if (convItem is not null)
                {
                    convItem.LastMessage = assistantMessage.Content.Length > 80
                        ? assistantMessage.Content[..80] + "..."
                        : assistantMessage.Content;
                }
            }
            else
            {
                // Fallback: use IAiService.StreamChatAsync directly (no persistence)
                await StreamDirectResponseAsync(assistantMessage, userContent, _generationCts.Token);
            }

            // Load suggested follow-up questions and update memory count (non-blocking)
            await LoadSuggestedQuestionsAsync();
            await UpdateMemoryCountAsync();
        }
        catch (OperationCanceledException)
        {
            Log.Debug("Generation cancelled by user");
            assistantMessage.Content += "\n\n[Generation stopped]";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during message generation");
            assistantMessage.Content = "An error occurred while generating a response. Please check that Ollama is running and a model is loaded.";
            _notificationService.ShowError("Generation Failed",
                "Could not generate a response. Check your AI connection in Settings.");
        }
        finally
        {
            assistantMessage.IsStreaming = false;
            IsGenerating = false;
            CurrentStreamingResponse = string.Empty;
            _generationCts?.Dispose();
            _generationCts = null;
        }
    }

    /// <summary>
    /// Streams a response directly via IAiService when no conversation context is available.
    /// </summary>
    private async Task StreamDirectResponseAsync(ChatMessageItem assistantMessage, string userContent, CancellationToken ct)
    {
        try
        {
            var chatMessages = new List<ChatMessage>();
            // Include existing message history (excluding the streaming placeholder)
            foreach (var msg in Messages.Where(m => !m.IsStreaming))
            {
                chatMessages.Add(new ChatMessage { Role = msg.Role, Content = msg.Content });
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int tokCount = 0;

            await foreach (var token in _aiService.StreamChatAsync(
                chatMessages, ActiveSystemPrompt, ct: ct))
            {
                assistantMessage.Content += token;
                CurrentStreamingResponse = assistantMessage.Content;
                OnPropertyChanged(nameof(Messages));
                tokCount++;
            }

            sw.Stop();
            assistantMessage.TokenCount = tokCount;
            assistantMessage.GenerationTimeMs = sw.Elapsed.TotalMilliseconds;
            TokenCount += tokCount;
            GenerationTimeMs = sw.Elapsed.TotalMilliseconds;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // If streaming fails (e.g. no Ollama), show a helpful offline message
            if (string.IsNullOrEmpty(assistantMessage.Content))
            {
                assistantMessage.Content =
                    "Unable to generate a response. Please ensure:\n\n" +
                    "1. **Ollama is installed and running** on your machine\n" +
                    "2. **A model is downloaded** (use the Model Manager page)\n" +
                    "3. **The endpoint** is correct in Settings (default: http://localhost:11434)\n\n" +
                    "Once connected, Agent-X will stream AI responses directly from your hardware.";
            }
            else
            {
                throw; // Re-throw if we had partial content
            }
        }
    }

    [RelayCommand]
    private async Task StopGenerationAsync()
    {
        Log.Debug("Stop generation requested");

        if (_generationCts is not null)
        {
            await _generationCts.CancelAsync();
        }
    }

    [RelayCommand]
    private async Task NewConversationAsync()
    {
        Log.Debug("New conversation requested");

        ActiveConversationId = null;
        ActiveConversationTitle = "New Conversation";
        ActiveSystemPrompt = null;
        ActiveSystemPromptName = null;
        TokenCount = 0;
        GenerationTimeMs = 0;
        Messages.Clear();
        SuggestedQuestions.Clear();
        OnPropertyChanged(nameof(HasNoMessages));

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task DeleteConversationAsync(long conversationId)
    {
        Log.Debug("Delete conversation requested: {ConversationId}", conversationId);

        try
        {
            await _conversationService.DeleteConversationAsync(conversationId);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete conversation from service");
        }

        var item = Conversations.FirstOrDefault(c => c.Id == conversationId);
        if (item is not null)
        {
            Conversations.Remove(item);
            OnPropertyChanged(nameof(HasNoConversations));

            // If the active conversation was deleted, clear it
            if (ActiveConversationId == conversationId)
            {
                await NewConversationAsync();
            }
        }
    }

    [RelayCommand]
    private async Task SelectConversationAsync(long conversationId)
    {
        Log.Debug("Select conversation: {ConversationId}", conversationId);

        var item = Conversations.FirstOrDefault(c => c.Id == conversationId);
        if (item is null) return;

        ActiveConversationId = conversationId;
        ActiveConversationTitle = item.Title;

        Messages.Clear();

        try
        {
            var messages = await _conversationService.GetMessagesAsync(conversationId);
            foreach (var msg in messages)
            {
                var chatItem = new ChatMessageItem
                {
                    MessageId = msg.Id,
                    ConversationId = msg.ConversationId,
                    SortOrder = msg.SortOrder,
                    Role = msg.Role,
                    Content = msg.Content,
                    Timestamp = msg.Timestamp,
                    IsUser = msg.Role == "user",
                    IsAssistant = msg.Role == "assistant",
                    IsSystem = msg.Role == "system",
                    TokenCount = msg.TokenCount,
                    GenerationTimeMs = msg.GenerationTimeMs ?? 0
                };

                // Load existing feedback rating for assistant messages
                if (msg.Role == "assistant" && msg.Id > 0)
                {
                    try
                    {
                        var feedback = await _feedbackService.GetFeedbackForMessageAsync(msg.Id);
                        if (feedback is not null)
                        {
                            chatItem.FeedbackRating = feedback.Rating;
                        }
                    }
                    catch { /* non-critical */ }
                }

                Messages.Add(chatItem);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load messages for conversation {ConversationId}", conversationId);
        }

        OnPropertyChanged(nameof(HasNoMessages));
        await LoadBranchTreeAsync();
    }

    [RelayCommand]
    private async Task TogglePinAsync(long conversationId)
    {
        Log.Debug("Toggle pin: {ConversationId}", conversationId);

        var item = Conversations.FirstOrDefault(c => c.Id == conversationId);
        if (item is not null)
        {
            item.IsPinned = !item.IsPinned;
        }

        try
        {
            await _conversationService.TogglePinAsync(conversationId);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to toggle pin state for conversation {ConversationId}", conversationId);
        }
    }

    [RelayCommand]
    private void SelectModel(string modelId)
    {
        Log.Debug("Select model: {ModelId}", modelId);

        var model = AvailableModels.FirstOrDefault(m => m.Id == modelId);
        if (model is not null)
        {
            ActiveModelName = model.Name;

            // Persist the active model selection
            _ = Task.Run(async () =>
            {
                try
                {
                    await _aiService.SetActiveModelAsync(modelId);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to persist active model selection");
                }
            });
        }
    }

    [RelayCommand]
    private void SelectSystemPrompt(SystemPromptItem? prompt)
    {
        if (prompt is null)
        {
            ActiveSystemPrompt = null;
            ActiveSystemPromptName = null;
            Log.Debug("System prompt cleared");
        }
        else
        {
            ActiveSystemPrompt = prompt.Content;
            ActiveSystemPromptName = prompt.Name;
            Log.Debug("System prompt selected: {PromptName}", prompt.Name);

            // Track usage
            _ = Task.Run(async () =>
            {
                try
                {
                    await _systemPromptService.IncrementUsageAsync(prompt.Id);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to increment system prompt usage");
                }
            });
        }

        ShowSystemPromptPicker = false;
    }

    [RelayCommand]
    private void ToggleConversationPane()
    {
        IsConversationPaneOpen = !IsConversationPaneOpen;
        Log.Debug("Conversation pane toggled: {IsOpen}", IsConversationPaneOpen);
    }

    [RelayCommand]
    private async Task ClearConversationAsync()
    {
        Log.Debug("Clear conversation requested");

        Messages.Clear();
        TokenCount = 0;
        GenerationTimeMs = 0;
        CurrentStreamingResponse = string.Empty;
        OnPropertyChanged(nameof(HasNoMessages));

        await Task.CompletedTask;
    }

    [RelayCommand]
    private void CopyMessage(string? content)
    {
        if (string.IsNullOrEmpty(content)) return;

        Log.Debug("Copy message to clipboard ({Length} chars)", content.Length);

        try
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(content);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy to clipboard");
        }
    }

    [RelayCommand]
    private void ExportConversationToClipboard()
    {
        if (Messages.Count == 0)
        {
            Log.Debug("Export to clipboard: no messages to export");
            return;
        }

        try
        {
            var markdown = BuildConversationMarkdown();
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(markdown);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            Log.Information("Conversation exported to clipboard ({Length} chars)", markdown.Length);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export conversation to clipboard");
        }
    }

    [RelayCommand]
    private async Task ExportConversationToFileAsync()
    {
        if (Messages.Count == 0)
        {
            Log.Debug("Export to file: no messages to export");
            return;
        }

        try
        {
            var markdown = BuildConversationMarkdown();

            var picker = new Windows.Storage.Pickers.FileSavePicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Markdown", new List<string> { ".md" });
            picker.FileTypeChoices.Add("Text", new List<string> { ".txt" });
            picker.SuggestedFileName = SanitizeFileName(ActiveConversationTitle);

            // WinUI 3 requires the window handle
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                await Windows.Storage.FileIO.WriteTextAsync(file, markdown);
                Log.Information("Conversation exported to {Path}", file.Path);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export conversation to file");
        }
    }

    [RelayCommand]
    private async Task RegenerateAsync()
    {
        Log.Debug("Regenerate last response requested");

        if (Messages.Count < 2) return;

        // Remove the last assistant message
        var lastMessage = Messages.LastOrDefault();
        if (lastMessage is { IsAssistant: true })
        {
            Messages.Remove(lastMessage);
        }

        // Find the last user message content and re-send
        var lastUserMessage = Messages.LastOrDefault(m => m.IsUser);
        if (lastUserMessage is not null)
        {
            // Re-send the last user message
            UserInput = lastUserMessage.Content;
            Messages.Remove(lastUserMessage);
            OnPropertyChanged(nameof(HasNoMessages));
            await SendMessageAsync();
        }
    }

    [RelayCommand]
    private async Task RefreshConnectionAsync()
    {
        Log.Debug("Refresh connection requested");

        ConnectionStatus = "Checking...";

        await CheckConnectionStatusAsync();
        await LoadAvailableModelsAsync();
    }

    [RelayCommand]
    private void UseSuggestedQuestion(string question)
    {
        if (!string.IsNullOrWhiteSpace(question))
        {
            UserInput = question;
            SuggestedQuestions.Clear();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // PER-MESSAGE ACTIONS (#18)
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task DeleteMessageAsync(ChatMessageItem? message)
    {
        if (message is null) return;

        Log.Debug("Delete message requested: {MessageId}", message.MessageId);

        // Delete from database if persisted
        if (message.MessageId > 0)
        {
            try
            {
                await _conversationService.DeleteMessageAsync(message.MessageId);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to delete message {MessageId} from database", message.MessageId);
            }
        }

        Messages.Remove(message);
        OnPropertyChanged(nameof(HasNoMessages));
        _notificationService.ShowInfo("Message deleted", "The message has been removed.");
    }

    [RelayCommand]
    private async Task SubmitFeedbackAsync(ChatMessageItem? message)
    {
        if (message is null || !message.IsAssistant || message.MessageId <= 0) return;

        // Toggle: if already positive → negative → none → positive
        var newRating = message.FeedbackRating switch
        {
            "positive" => "negative",
            "negative" => "none",
            _ => "positive"
        };

        Log.Debug("Submit feedback for message {MessageId}: {Rating}", message.MessageId, newRating);

        message.FeedbackRating = newRating;

        try
        {
            await _feedbackService.SubmitFeedbackAsync(
                message.MessageId,
                ActiveConversationId ?? 0,
                newRating);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to submit feedback for message {MessageId}", message.MessageId);
        }
    }

    [RelayCommand]
    private async Task ThumbsUpAsync(ChatMessageItem? message)
    {
        if (message is null || !message.IsAssistant || message.MessageId <= 0) return;

        var newRating = message.FeedbackRating == "positive" ? "none" : "positive";
        message.FeedbackRating = newRating;

        Log.Debug("Thumbs up for message {MessageId}: {Rating}", message.MessageId, newRating);

        try
        {
            await _feedbackService.SubmitFeedbackAsync(
                message.MessageId, ActiveConversationId ?? 0, newRating);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to submit thumbs up for message {MessageId}", message.MessageId);
        }
    }

    [RelayCommand]
    private async Task ThumbsDownAsync(ChatMessageItem? message)
    {
        if (message is null || !message.IsAssistant || message.MessageId <= 0) return;

        var newRating = message.FeedbackRating == "negative" ? "none" : "negative";
        message.FeedbackRating = newRating;

        Log.Debug("Thumbs down for message {MessageId}: {Rating}", message.MessageId, newRating);

        try
        {
            await _feedbackService.SubmitFeedbackAsync(
                message.MessageId, ActiveConversationId ?? 0, newRating);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to submit thumbs down for message {MessageId}", message.MessageId);
        }
    }

    [RelayCommand]
    private async Task RegenerateMessageAsync(ChatMessageItem? message)
    {
        if (message is null || !message.IsAssistant) return;

        Log.Debug("Regenerate specific message requested: {MessageId}", message.MessageId);

        // Find the user message just before this assistant message
        var msgIndex = Messages.IndexOf(message);
        if (msgIndex < 1) return;

        var userMessage = Messages[msgIndex - 1];
        if (!userMessage.IsUser) return;

        // Remove the assistant message
        Messages.Remove(message);

        // Delete from DB if persisted
        if (message.MessageId > 0)
        {
            try
            {
                await _conversationService.DeleteMessageAsync(message.MessageId);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to delete message for regeneration");
            }
        }

        // Re-send using the user message content
        UserInput = userMessage.Content;
        Messages.Remove(userMessage);
        OnPropertyChanged(nameof(HasNoMessages));
        await SendMessageAsync();
    }

    // ═══════════════════════════════════════════════════════════════
    // MESSAGE EDITING (#19)
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private void StartEditMessage(ChatMessageItem? message)
    {
        if (message is null || !message.IsUser) return;

        Log.Debug("Start editing message {MessageId}", message.MessageId);

        // Cancel any other edit in progress
        foreach (var msg in Messages.Where(m => m.IsEditing))
        {
            msg.IsEditing = false;
        }

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

        Log.Debug("Save edited message {MessageId}", message.MessageId);

        // Update the message content
        message.Content = newContent;

        // Update in database
        if (message.MessageId > 0)
        {
            try
            {
                await _conversationService.UpdateMessageContentAsync(message.MessageId, newContent);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to update message content in database");
            }
        }

        // Remove all messages after this one (they are now stale)
        var msgIndex = Messages.IndexOf(message);
        if (msgIndex >= 0 && ActiveConversationId is not null)
        {
            // Delete subsequent messages from DB
            if (message.SortOrder >= 0)
            {
                try
                {
                    await _conversationService.DeleteMessagesAfterAsync(
                        ActiveConversationId.Value, message.SortOrder);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to delete subsequent messages");
                }
            }

            // Remove from UI
            while (Messages.Count > msgIndex + 1)
            {
                Messages.RemoveAt(Messages.Count - 1);
            }
        }

        // Re-generate assistant response
        UserInput = newContent;
        Messages.Remove(message);
        OnPropertyChanged(nameof(HasNoMessages));
        await SendMessageAsync();
    }

    /// <summary>
    /// Loads AI-generated follow-up question suggestions based on the
    /// current conversation and persisted user memories.
    /// </summary>
    private async Task LoadSuggestedQuestionsAsync()
    {
        if (ActiveConversationId is null) return;
        try
        {
            var questions = await _memoryService.GetSuggestedQuestionsAsync(ActiveConversationId.Value);
            SuggestedQuestions.Clear();
            foreach (var q in questions) SuggestedQuestions.Add(q);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load suggested questions");
        }
    }

    /// <summary>
    /// Updates the memory count for display in the UI.
    /// </summary>
    private async Task UpdateMemoryCountAsync()
    {
        try
        {
            MemoryCount = await _memoryService.GetMemoryCountAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to update memory count");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════

    private string BuildConversationMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {ActiveConversationTitle}");
        sb.AppendLine();
        sb.AppendLine($"*Exported from Agent-X on {DateTime.Now:MMMM d, yyyy 'at' h:mm tt}*");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(ActiveSystemPromptName))
        {
            sb.AppendLine($"**System Prompt:** {ActiveSystemPromptName}");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var msg in Messages)
        {
            var roleLabel = msg.Role switch
            {
                "user" => "**You**",
                "assistant" => "**Agent-X**",
                "system" => "**System**",
                _ => $"**{msg.Role}**"
            };

            sb.AppendLine($"### {roleLabel}");
            sb.AppendLine($"*{msg.FormattedTime}*");
            sb.AppendLine();
            sb.AppendLine(msg.Content);
            sb.AppendLine();

            if (msg.IsAssistant && msg.TokenCount > 0)
            {
                sb.AppendLine($"_{msg.FormattedTokens} | {msg.FormattedTokenSpeed}_");
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "conversation" : sanitized;
    }

    // ═══════════════════════════════════════════════════════════════
    // FOLDER ORGANIZATION
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task SetConversationFolderAsync(string? folderName)
    {
        if (ActiveConversationId is null) return;

        Log.Debug("Set conversation folder: {Folder}", folderName ?? "(none)");

        try
        {
            await _conversationService.SetConversationFolderAsync(ActiveConversationId.Value, folderName);
            var item = Conversations.FirstOrDefault(c => c.Id == ActiveConversationId);
            if (item is not null)
            {
                item.FolderName = folderName;
            }
            await LoadFolderNamesAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to set conversation folder");
        }
    }

    [RelayCommand]
    private async Task FilterByFolderAsync(string? folderName)
    {
        ActiveFolderFilter = folderName;
        Log.Debug("Filter by folder: {Folder}", folderName ?? "All");

        if (string.IsNullOrEmpty(folderName))
        {
            await LoadConversationsAsync();
            return;
        }

        try
        {
            var conversations = await _conversationService.GetConversationsByFolderAsync(folderName);
            Conversations.Clear();
            foreach (var conv in conversations)
            {
                var lastMsg = conv.Messages?.OrderByDescending(m => m.SortOrder).FirstOrDefault();
                Conversations.Add(new ConversationListItem
                {
                    Id = conv.Id,
                    Title = conv.Title,
                    LastMessage = lastMsg?.Content ?? string.Empty,
                    UpdatedAt = conv.UpdatedAt,
                    IsPinned = conv.IsPinned,
                    MessageCount = conv.MessageCount,
                    FolderName = conv.FolderName
                });
            }
            OnPropertyChanged(nameof(HasNoConversations));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to filter conversations by folder");
        }
    }

    private async Task LoadFolderNamesAsync()
    {
        try
        {
            var folders = await _conversationService.GetAllFolderNamesAsync();
            FolderNames.Clear();
            foreach (var f in folders) FolderNames.Add(f);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load folder names");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // VOICE INPUT & TRANSCRIPTION
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task ToggleVoiceRecordingAsync()
    {
        if (IsRecording)
        {
            await StopRecordingAndTranscribeAsync();
        }
        else
        {
            StartRecording();
        }
    }

    [RelayCommand]
    private async Task PickAudioFileAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;

        foreach (var ext in _transcriptionService.SupportedFormats)
            picker.FileTypeFilter.Add(ext);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        IsTranscribing = true;
        VoiceStatusMessage = "Transcribing...";

        try
        {
            var result = await _transcriptionService.TranscribeFileAsync(
                file.Path,
                new TranscriptionOptions { ModelSize = "base" },
                progress: new Progress<TranscriptionProgress>(p => VoiceStatusMessage = p.CurrentPhase),
                CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(result.FullText))
            {
                UserInput = result.FullText.Trim();
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("model", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning(ex, "Whisper model not available for file transcription");
            _notificationService.ShowError("Model Required",
                "Download a Whisper model first. Go to Settings > Voice to download one.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Audio file transcription failed");
            _notificationService.ShowError("Transcription Failed",
                $"Could not transcribe the selected file: {ex.Message}");
        }
        finally
        {
            IsTranscribing = false;
            VoiceStatusMessage = string.Empty;
        }
    }

    private void StartRecording()
    {
        try
        {
            _currentRecordingPath = Path.Combine(
                Path.GetTempPath(),
                $"agentx-voice-{Guid.NewGuid():N}.wav");

            _recordingStopTcs = new TaskCompletionSource();

            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 100
            };

            _waveWriter = new WaveFileWriter(_currentRecordingPath, _waveIn.WaveFormat);

            _waveIn.DataAvailable += OnRecordingDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;

            _waveIn.StartRecording();
            IsRecording = true;
            VoiceStatusMessage = "Recording...";

            Log.Debug("Voice recording started: {Path}", _currentRecordingPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start voice recording");
            CleanupRecording();
            _notificationService.ShowError("Recording Failed",
                "Could not start voice recording. Ensure a microphone is connected and permissions are granted.");
        }
    }

    private void OnRecordingDataAvailable(object? sender, WaveInEventArgs e)
    {
        _waveWriter?.Write(e.Buffer, 0, e.BytesRecorded);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _waveWriter?.Dispose();
        _waveWriter = null;

        _waveIn?.Dispose();
        _waveIn = null;

        _recordingStopTcs?.TrySetResult();

        if (e.Exception is not null)
        {
            Log.Error(e.Exception, "Recording stopped with error");
        }
    }

    private async Task StopRecordingAndTranscribeAsync()
    {
        if (_waveIn is null || _currentRecordingPath is null) return;

        Log.Debug("Stopping voice recording for transcription");

        _waveIn.StopRecording();
        IsRecording = false;
        IsTranscribing = true;
        VoiceStatusMessage = "Transcribing...";

        if (_recordingStopTcs is not null)
            await _recordingStopTcs.Task;

        try
        {
            if (File.Exists(_currentRecordingPath))
            {
                var fileInfo = new FileInfo(_currentRecordingPath);
                if (fileInfo.Length > 44) // WAV header is 44 bytes minimum
                {
                    var result = await _transcriptionService.TranscribeFileAsync(
                        _currentRecordingPath,
                        new TranscriptionOptions { ModelSize = "base" },
                        progress: new Progress<TranscriptionProgress>(p =>
                        {
                            VoiceStatusMessage = p.CurrentPhase;
                        }),
                        CancellationToken.None);

                    if (!string.IsNullOrWhiteSpace(result.FullText))
                    {
                        UserInput = result.FullText.Trim();
                        Log.Information("Voice transcription complete: {Length} chars, {Segments} segments",
                            result.FullText.Length, result.Segments.Count);
                    }
                    else
                    {
                        _notificationService.ShowInfo("No Speech Detected",
                            "Could not detect speech in the recording. Try again in a quieter environment.");
                    }
                }
                else
                {
                    _notificationService.ShowInfo("Recording Too Short",
                        "The recording was too short to transcribe. Hold the button longer while speaking.");
                }
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("model", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning(ex, "Whisper model not available");
            _notificationService.ShowError("Model Required",
                "Download a Whisper model first. Go to Settings > Voice to download one.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Voice transcription failed");
            _notificationService.ShowError("Transcription Failed",
                $"Could not transcribe the recording: {ex.Message}");
        }
        finally
        {
            IsTranscribing = false;
            VoiceStatusMessage = string.Empty;
            CleanupRecording();
        }
    }

    private void CleanupRecording()
    {
        _waveWriter?.Dispose();
        _waveWriter = null;

        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnRecordingDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;
            _waveIn.Dispose();
            _waveIn = null;
        }

        if (_currentRecordingPath is not null)
        {
            try { if (File.Exists(_currentRecordingPath)) File.Delete(_currentRecordingPath); }
            catch { /* best effort */ }
            _currentRecordingPath = null;
        }

        _recordingStopTcs = null;
    }

    // ═══════════════════════════════════════════════════════════════
    // CONVERSATION BRANCHING
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task BranchFromMessageAsync(long messageId)
    {
        if (ActiveConversationId is null) return;
        var label = PendingBranchLabel;
        PendingBranchLabel = null;
        try
        {
            var branch = await _branchService.BranchAtMessageAsync(
                ActiveConversationId.Value, messageId, label);
            await LoadBranchTreeAsync();
            _notificationService.ShowInfo("Branch Created", $"Created branch: {branch.Title}");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create branch from message {MessageId}", messageId);
            _notificationService.ShowError("Branch Failed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task LoadBranchTreeAsync()
    {
        if (ActiveConversationId is null) return;
        try
        {
            BranchTree = await _branchService.GetBranchTreeAsync(ActiveConversationId.Value);
            OnPropertyChanged(nameof(HasBranches));
            ActiveBranches.Clear();
            if (BranchTree is not null)
            {
                foreach (var child in BranchTree.Children)
                    ActiveBranches.Add(child);
            }

            MarkBranchPoints();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load branch tree for conversation {ConversationId}", ActiveConversationId);
            _notificationService.ShowError("Branch Load Failed", $"Could not load branches: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SwitchToBranchAsync(long branchConversationId)
    {
        await SelectConversationCommand.ExecuteAsync(branchConversationId);
    }

    [RelayCommand]
    private async Task MergeToMainAsync(MergeBranchRequest request)
    {
        if (request is null) return;
        try
        {
            var messageIds = request.MessageIds;
            if (messageIds is null || messageIds.Count == 0)
            {
                // Load all messages from the source branch when no specific IDs provided
                var messages = await _conversationService.GetMessagesAsync(request.SourceConversationId);
                messageIds = messages.Select(m => m.Id).ToList();
            }

            await _branchService.MergeMessagesAsync(
                request.SourceConversationId, messageIds, request.TargetConversationId);
            _notificationService.ShowInfo("Merge Complete", "Merged insights to main thread");
            await SelectConversationCommand.ExecuteAsync(request.TargetConversationId);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to merge messages from {SourceId} to {TargetId}",
                request.SourceConversationId, request.TargetConversationId);
            _notificationService.ShowError("Merge Failed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteBranchAsync(long branchConversationId)
    {
        try
        {
            await _branchService.DeleteBranchAsync(branchConversationId);
            await LoadBranchTreeAsync();
            _notificationService.ShowInfo("Branch Deleted", "The branch has been removed.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete branch {BranchId}", branchConversationId);
            _notificationService.ShowError("Delete Failed", ex.Message);
        }
    }

    [RelayCommand]
    private void CompareBranches()
    {
        if (BranchTree is null || BranchTree.Children.Count < 1) return;

        var mainBranch = BranchTree;
        var compareBranch = BranchTree.Children[0];

        var window = new Views.BranchCompareWindow(
            mainBranch, compareBranch,
            "Main Thread",
            compareBranch.BranchLabel ?? "Branch");

        window.Activate();
    }

    /// <summary>
    /// Marks messages in the current conversation that are branch points
    /// based on the loaded branch tree data.
    /// </summary>
    private void MarkBranchPoints()
    {
        // Reset all branch point markers
        foreach (var msg in Messages)
        {
            msg.IsBranchPoint = false;
            msg.BranchCountAtPoint = 0;
        }

        if (BranchTree is null) return;

        void MarkFromNode(ConversationBranchTree node)
        {
            foreach (var child in node.Children)
            {
                if (child.BranchPointMessageId is not null)
                {
                    var msg = Messages.FirstOrDefault(m => m.MessageId == child.BranchPointMessageId.Value);
                    if (msg is not null)
                    {
                        msg.IsBranchPoint = true;
                        msg.BranchCountAtPoint += 1;
                    }
                }
                MarkFromNode(child);
            }
        }

        MarkFromNode(BranchTree);
    }

    // ═══════════════════════════════════════════════════════════════
    // DISPOSAL
    // ═══════════════════════════════════════════════════════════════

    public void Dispose()
    {
        if (_waveIn is not null && IsRecording)
        {
            _waveIn.StopRecording();
        }
        CleanupRecording();
        _generationCts?.Cancel();
        _generationCts?.Dispose();
        Log.Debug("ChatViewModel disposed");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// VIEW MODEL ITEM CLASSES
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Represents a single chat message displayed in the UI.
/// </summary>
public class ChatMessageItem : ObservableObject
{
    private string _content = string.Empty;
    private bool _isStreaming;
    private string _feedbackRating = "none";
    private bool _isEditing;
    private string _editContent = string.Empty;

    /// <summary>Database primary key. 0 if not yet persisted.</summary>
    public long MessageId { get; set; }

    /// <summary>Conversation this message belongs to.</summary>
    public long ConversationId { get; set; }

    /// <summary>Sort order within the conversation.</summary>
    public int SortOrder { get; set; }

    public string Role { get; set; } = string.Empty;

    public string Content
    {
        get => _content;
        set
        {
            if (SetProperty(ref _content, value))
            {
                OnPropertyChanged(nameof(ContentSegments));
            }
        }
    }

    /// <summary>
    /// Parsed markdown segments for rich rendering of assistant messages.
    /// Re-computed whenever Content changes.
    /// </summary>
    public List<MarkdownSegment> ContentSegments => MarkdownParser.Parse(Content);

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsUser { get; set; }
    public bool IsAssistant { get; set; }
    public bool IsSystem { get; set; }
    public int TokenCount { get; set; }
    public double GenerationTimeMs { get; set; }

    public bool IsStreaming
    {
        get => _isStreaming;
        set => SetProperty(ref _isStreaming, value);
    }

    /// <summary>
    /// Feedback rating for assistant messages: "positive", "negative", or "none".
    /// </summary>
    public string FeedbackRating
    {
        get => _feedbackRating;
        set
        {
            if (SetProperty(ref _feedbackRating, value))
            {
                OnPropertyChanged(nameof(IsThumbsUp));
                OnPropertyChanged(nameof(IsThumbsDown));
            }
        }
    }

    public bool IsThumbsUp => FeedbackRating == "positive";
    public bool IsThumbsDown => FeedbackRating == "negative";

    /// <summary>Whether this user message is in inline edit mode.</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (SetProperty(ref _isEditing, value))
            {
                OnPropertyChanged(nameof(IsNotEditing));
            }
        }
    }

    public bool IsNotEditing => !IsEditing;

    /// <summary>Content of the edit TextBox while editing.</summary>
    public string EditContent
    {
        get => _editContent;
        set => SetProperty(ref _editContent, value);
    }

    public string FormattedTime => Timestamp.ToLocalTime().ToString("h:mm tt");

    public string FormattedTokens => TokenCount > 0
        ? $"{TokenCount} tokens"
        : string.Empty;

    public string FormattedGenerationTime => GenerationTimeMs > 0
        ? $"{GenerationTimeMs:F0}ms"
        : string.Empty;

    public string FormattedTokenSpeed => TokenCount > 0 && GenerationTimeMs > 0
        ? $"{TokenCount / (GenerationTimeMs / 1000.0):F1} tok/s"
        : string.Empty;

    /// <summary>Whether this message is a point where one or more branches diverge.</summary>
    private bool _isBranchPoint;
    public bool IsBranchPoint
    {
        get => _isBranchPoint;
        set => SetProperty(ref _isBranchPoint, value);
    }

    /// <summary>Number of branches diverging from this message.</summary>
    private int _branchCountAtPoint;
    public int BranchCountAtPoint
    {
        get => _branchCountAtPoint;
        set => SetProperty(ref _branchCountAtPoint, value);
    }

    /// <summary>Web citations associated with this message (from Deep Research Mode).</summary>
    public IReadOnlyList<WebCitation>? WebCitations { get; set; }

    /// <summary>Whether this message has web citations to display.</summary>
    public bool HasWebCitations => WebCitations?.Count > 0;
}

/// <summary>
/// Represents a conversation entry in the sidebar list.
/// </summary>
public class ConversationListItem : ObservableObject
{
    private bool _isPinned;
    private string _lastMessage = string.Empty;
    private string? _folderName;

    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;

    public string LastMessage
    {
        get => _lastMessage;
        set => SetProperty(ref _lastMessage, value);
    }

    public DateTime UpdatedAt { get; set; }

    public bool IsPinned
    {
        get => _isPinned;
        set => SetProperty(ref _isPinned, value);
    }

    public string? FolderName
    {
        get => _folderName;
        set => SetProperty(ref _folderName, value);
    }

    public int MessageCount { get; set; }

    public string FormattedTime
    {
        get
        {
            var span = DateTime.UtcNow - UpdatedAt;
            return span.TotalMinutes < 1 ? "now"
                : span.TotalMinutes < 60 ? $"{(int)span.TotalMinutes}m ago"
                : span.TotalHours < 24 ? $"{(int)span.TotalHours}h ago"
                : span.TotalDays < 7 ? $"{(int)span.TotalDays}d ago"
                : UpdatedAt.ToLocalTime().ToString("MMM d");
        }
    }
}

/// <summary>
/// Represents a system prompt for selection in the UI.
/// </summary>
public class SystemPromptItem : ObservableObject
{
    private bool _isFavorite;

    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = "General";

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    public bool IsBuiltIn { get; set; }

    public string CategoryIcon => Category switch
    {
        "General" => "\uE8BD",
        "Code" => "\uE943",
        "Writing" => "\uE70F",
        "Analysis" => "\uE9D9",
        "Creative" => "\uE790",
        _ => "\uE8BD"
    };
}

/// <summary>
/// Parameter object for the merge-branch command, since [RelayCommand]
/// only supports a single parameter.
/// </summary>
public record MergeBranchRequest(
    long SourceConversationId,
    long TargetConversationId,
    IReadOnlyList<long>? MessageIds = null);
