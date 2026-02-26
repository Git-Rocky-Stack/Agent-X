using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Services.Chat;
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

    // ── Search ─────────────────────────────────────────────────
    [ObservableProperty] private string _conversationSearchQuery = string.Empty;

    // ── Collections ────────────────────────────────────────────
    public ObservableCollection<ChatMessageItem> Messages { get; } = new();
    public ObservableCollection<ConversationListItem> Conversations { get; } = new();
    public ObservableCollection<AiModel> AvailableModels { get; } = new();
    public ObservableCollection<SystemPromptItem> SystemPrompts { get; } = new();

    // ── Computed Properties ────────────────────────────────────
    public bool HasNoConversations => Conversations.Count == 0;
    public bool HasNoMessages => Messages.Count == 0;
    public bool HasActiveSystemPrompt => !string.IsNullOrEmpty(ActiveSystemPromptName);
    public bool CanSend => !string.IsNullOrWhiteSpace(UserInput) && !IsGenerating;

    // ── Services ──────────────────────────────────────────────
    private readonly IChatService _chatService;
    private readonly IConversationService _conversationService;
    private readonly IAiService _aiService;
    private readonly IModelManager _modelManager;
    private readonly ISystemPromptService _systemPromptService;

    private CancellationTokenSource? _generationCts;

    public ChatViewModel(
        IChatService chatService,
        IConversationService conversationService,
        IAiService aiService,
        IModelManager modelManager,
        ISystemPromptService systemPromptService)
    {
        _chatService = chatService;
        _conversationService = conversationService;
        _aiService = aiService;
        _modelManager = modelManager;
        _systemPromptService = systemPromptService;
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
                    MessageCount = conv.MessageCount
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

    partial void OnActiveSystemPromptNameChanged(string? value)
    {
        OnPropertyChanged(nameof(HasActiveSystemPrompt));
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
                Messages.Add(new ChatMessageItem
                {
                    Role = msg.Role,
                    Content = msg.Content,
                    Timestamp = msg.Timestamp,
                    IsUser = msg.Role == "user",
                    IsAssistant = msg.Role == "assistant",
                    IsSystem = msg.Role == "system",
                    TokenCount = msg.TokenCount,
                    GenerationTimeMs = msg.GenerationTimeMs ?? 0
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load messages for conversation {ConversationId}", conversationId);
        }

        OnPropertyChanged(nameof(HasNoMessages));
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

    // ═══════════════════════════════════════════════════════════════
    // DISPOSAL
    // ═══════════════════════════════════════════════════════════════

    public void Dispose()
    {
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

    public string Role { get; set; } = string.Empty;

    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }

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
}

/// <summary>
/// Represents a conversation entry in the sidebar list.
/// </summary>
public class ConversationListItem : ObservableObject
{
    private bool _isPinned;
    private string _lastMessage = string.Empty;

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
