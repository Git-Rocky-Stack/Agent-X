using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.AI.Routing;
using AgentX.Core.Services.Settings;
using Serilog;

namespace AgentX.Core.Services.Chat;

/// <summary>
/// Orchestrates AI chat interactions by coordinating between <see cref="IAiService"/>
/// for inference and <see cref="IConversationService"/> for persistence.
/// Handles streaming, generation state management, and cancellation.
/// </summary>
public class ChatService : IChatService
{
    private readonly IAiService _aiService;
    private readonly IConversationService _conversationService;
    private readonly ISettingsService _settingsService;
    private readonly IContextWindowManager _contextWindowManager;
    private readonly IConversationMemoryService _memoryService;
    private readonly IModelRouterService? _modelRouterService;
    private readonly ILogger _log;

    private CancellationTokenSource? _generationCts;
    private readonly object _generationLock = new();
    private bool _isGenerating;

    /// <inheritdoc />
    public bool IsGenerating
    {
        get => _isGenerating;
        private set
        {
            if (_isGenerating == value) return;
            _isGenerating = value;
            GenerationStateChanged?.Invoke(this, value);
        }
    }

    /// <inheritdoc />
    public event EventHandler<bool>? GenerationStateChanged;

    /// <summary>
    /// Fires when the model router makes a routing decision during message processing.
    /// Null when routing is disabled or not configured.
    /// </summary>
    public event EventHandler<RoutingDecision>? RoutingDecisionMade;

    public ChatService(
        IAiService aiService,
        IConversationService conversationService,
        ISettingsService settingsService,
        IContextWindowManager contextWindowManager,
        IConversationMemoryService memoryService,
        ILogger logger,
        IModelRouterService? modelRouterService = null)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _conversationService = conversationService ?? throw new ArgumentNullException(nameof(conversationService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _contextWindowManager = contextWindowManager ?? throw new ArgumentNullException(nameof(contextWindowManager));
        _memoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
        _log = logger?.ForContext<ChatService>()
               ?? throw new ArgumentNullException(nameof(logger));
        _modelRouterService = modelRouterService;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> SendMessageAsync(
        long conversationId,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            _log.Warning("Attempted to send empty message to conversation {ConversationId}", conversationId);
            yield break;
        }

        // Create a linked cancellation token so StopGenerationAsync can cancel mid-stream
        CancellationTokenSource linkedCts;
        lock (_generationLock)
        {
            _generationCts?.Cancel();
            _generationCts?.Dispose();
            _generationCts = new CancellationTokenSource();
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _generationCts.Token);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            IsGenerating = true;

            // 1. Persist the user message
            await _conversationService.AddMessageAsync(
                conversationId, "user", userMessage);

            // 2. Load conversation to get system prompt and all message history
            var conversation = await _conversationService.GetConversationAsync(conversationId);
            if (conversation is null)
            {
                _log.Error("Conversation {ConversationId} not found after adding message", conversationId);
                throw new InvalidOperationException(
                    $"Conversation {conversationId} not found.");
            }

            // 3. Build the ChatMessage list from conversation history
            var chatMessages = BuildChatMessages(conversation.Messages);

            // 4. Get system prompt from conversation entity
            var systemPrompt = conversation.SystemPrompt;

            // 4b. Enrich system prompt with user memories
            try
            {
                var memoryContext = await _memoryService.GetMemoryContextAsync(8, linkedCts.Token);
                if (!string.IsNullOrEmpty(memoryContext))
                {
                    systemPrompt = (systemPrompt ?? "") + memoryContext;
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to load memory context for conversation {ConversationId}", conversationId);
            }

            // 5. Build chat options from settings
            var options = await BuildChatOptionsAsync();

            // 5b. Apply model routing if enabled and available
            if (_modelRouterService is not null)
            {
                try
                {
                    var routingSettings = await _settingsService.GetSettingsAsync();
                    if (routingSettings.EnableModelRouting)
                    {
                        var routingDecision = await _modelRouterService.RouteAsync(userMessage, linkedCts.Token);

                        _log.Information(
                            "Auto-routing applied: Provider={ProviderId}, Model={ModelId}, Task={TaskType}, Reason={Reason}",
                            routingDecision.ProviderId, routingDecision.ModelId,
                            routingDecision.TaskType.Name, routingDecision.Reason);

                        // Switch to the routed provider if different from current
                        var switched = await _aiService.SwitchProviderAsync(routingDecision.ProviderId, linkedCts.Token);
                        if (switched)
                        {
                            await _aiService.SetActiveModelAsync(routingDecision.ModelId, linkedCts.Token);
                        }

                        // Notify listeners (UI indicators, telemetry, etc.)
                        RoutingDecisionMade?.Invoke(this, routingDecision);
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Model routing failed, proceeding with current provider");
                }
            }

            // 6. Trim context to fit within model's context window
            var contextWindow = _contextWindowManager.GetEffectiveContextWindow(
                options?.ContextWindow ?? 0);
            var fittedMessages = await _contextWindowManager.FitToContextWindowAsync(
                chatMessages.ToList(), contextWindow, reserveForResponse: 1024, linkedCts.Token);
            chatMessages = fittedMessages;

            // 7. Stream the AI response
            _log.Debug(
                "Starting streaming response for conversation {ConversationId} ({MessageCount} messages in context)",
                conversationId, chatMessages.Count);

            var responseBuilder = new StringBuilder();
            var tokenCount = 0;

            await foreach (var token in _aiService.StreamChatAsync(
                chatMessages, systemPrompt, options, linkedCts.Token))
            {
                responseBuilder.Append(token);
                tokenCount++;
                yield return token;
            }

            stopwatch.Stop();
            var fullResponse = responseBuilder.ToString();

            // 7. Persist the complete assistant response
            if (!string.IsNullOrEmpty(fullResponse))
            {
                await _conversationService.AddMessageAsync(
                    conversationId,
                    "assistant",
                    fullResponse,
                    tokenCount: tokenCount,
                    generationTimeMs: stopwatch.Elapsed.TotalMilliseconds);

                _log.Information(
                    "Completed streaming response for conversation {ConversationId}: {TokenCount} tokens in {ElapsedMs:F0}ms",
                    conversationId, tokenCount, stopwatch.Elapsed.TotalMilliseconds);

                // Extract memories from this conversation (non-blocking)
                _ = Task.Run(async () =>
                {
                    try { await _memoryService.ExtractMemoriesAsync(conversationId); }
                    catch (Exception ex) { _log.Warning(ex, "Background memory extraction failed for conversation {ConversationId}", conversationId); }
                });
            }
            else
            {
                _log.Warning(
                    "AI returned empty response for conversation {ConversationId}",
                    conversationId);
            }
        }
        finally
        {
            stopwatch.Stop();
            IsGenerating = false;
            linkedCts.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<string> SendMessageAndWaitAsync(
        long conversationId,
        string userMessage,
        CancellationToken ct = default)
    {
        var responseBuilder = new StringBuilder();

        await foreach (var token in SendMessageAsync(conversationId, userMessage, ct))
        {
            responseBuilder.Append(token);
        }

        return responseBuilder.ToString();
    }

    /// <inheritdoc />
    public async Task RegenerateLastResponseAsync(
        long conversationId,
        CancellationToken ct = default)
    {
        try
        {
            _log.Information(
                "Regenerating last response for conversation {ConversationId}",
                conversationId);

            // Get messages to find the last user message
            var messages = await _conversationService.GetMessagesAsync(conversationId);
            if (messages.Count == 0)
            {
                _log.Warning(
                    "Cannot regenerate: no messages in conversation {ConversationId}",
                    conversationId);
                return;
            }

            // Delete the last assistant message if it exists
            await _conversationService.DeleteLastAssistantMessageAsync(conversationId);

            // Find the last user message (the one we want to re-send)
            // Re-fetch messages after deletion to get the current state
            var updatedMessages = await _conversationService.GetMessagesAsync(conversationId);
            var lastUserMessage = updatedMessages
                .LastOrDefault(m => m.Role == "user");

            if (lastUserMessage is null)
            {
                _log.Warning(
                    "Cannot regenerate: no user message found in conversation {ConversationId}",
                    conversationId);
                return;
            }

            // Re-load the conversation to get system prompt
            var conversation = await _conversationService.GetConversationAsync(conversationId);
            if (conversation is null)
            {
                _log.Error(
                    "Conversation {ConversationId} not found during regeneration",
                    conversationId);
                return;
            }

            // Build chat messages from the current history (without the deleted assistant message)
            // We do NOT add a new user message -- the last user message is already in history
            var rawMessages = BuildChatMessages(updatedMessages);

            // Build chat options from settings
            var options = await BuildChatOptionsAsync();

            // Trim context to fit within model's context window
            var contextWindow = _contextWindowManager.GetEffectiveContextWindow(
                options?.ContextWindow ?? 0);
            var chatMessages = await _contextWindowManager.FitToContextWindowAsync(
                rawMessages.ToList(), contextWindow, reserveForResponse: 1024, ct);

            // Create a linked cancellation token for stop support
            CancellationTokenSource linkedCts;
            lock (_generationLock)
            {
                _generationCts?.Cancel();
                _generationCts?.Dispose();
                _generationCts = new CancellationTokenSource();
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _generationCts.Token);
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                IsGenerating = true;

                var responseBuilder = new StringBuilder();
                var tokenCount = 0;

                await foreach (var token in _aiService.StreamChatAsync(
                    chatMessages, conversation.SystemPrompt, options, linkedCts.Token))
                {
                    responseBuilder.Append(token);
                    tokenCount++;
                }

                stopwatch.Stop();
                var fullResponse = responseBuilder.ToString();

                if (!string.IsNullOrEmpty(fullResponse))
                {
                    await _conversationService.AddMessageAsync(
                        conversationId,
                        "assistant",
                        fullResponse,
                        tokenCount: tokenCount,
                        generationTimeMs: stopwatch.Elapsed.TotalMilliseconds);

                    _log.Information(
                        "Regenerated response for conversation {ConversationId}: {TokenCount} tokens in {ElapsedMs:F0}ms",
                        conversationId, tokenCount, stopwatch.Elapsed.TotalMilliseconds);
                }
            }
            finally
            {
                stopwatch.Stop();
                IsGenerating = false;
                linkedCts.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            _log.Information(
                "Regeneration cancelled for conversation {ConversationId}",
                conversationId);
        }
        catch (Exception ex)
        {
            _log.Error(
                ex, "Failed to regenerate response for conversation {ConversationId}",
                conversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public Task StopGenerationAsync()
    {
        lock (_generationLock)
        {
            if (_generationCts is not null && !_generationCts.IsCancellationRequested)
            {
                _log.Information("Stopping generation");
                _generationCts.Cancel();
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Converts a collection of <see cref="Data.Entities.MessageEntity"/> to a list
    /// of <see cref="ChatMessage"/> suitable for the AI service.
    /// </summary>
    private static IReadOnlyList<ChatMessage> BuildChatMessages(
        IEnumerable<Data.Entities.MessageEntity> messages)
    {
        return messages
            .OrderBy(m => m.SortOrder)
            .Select(m => new ChatMessage
            {
                Role = m.Role,
                Content = m.Content,
                Timestamp = m.Timestamp,
            })
            .ToList();
    }

    /// <summary>
    /// Reads current settings and constructs <see cref="ChatOptions"/>.
    /// Because ChatOptions is being created by a parallel agent, this method
    /// builds it from the AppSettings values.
    /// </summary>
    private async Task<ChatOptions?> BuildChatOptionsAsync()
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();

            return new ChatOptions
            {
                Temperature = settings.Temperature,
                MaxTokens = settings.MaxTokens,
                ContextWindow = settings.ContextWindow,
            };
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to build chat options from settings, using defaults");
            return null;
        }
    }
}
