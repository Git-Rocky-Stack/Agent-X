using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using AgentX.Core.AI;
using AgentX.Core.AI.Context;
using AgentX.Core.AI.Models;
using AgentX.Core.AI.Routing;
using AgentX.Core.Constants;
using AgentX.Core.Services.Chat.Models;
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
    private readonly IContextAssemblyService _contextAssemblyService;
    private readonly IConversationMemoryService _memoryService;
    private readonly IConversationSummaryService? _conversationSummaryService;
    private readonly ISemanticMemoryService? _semanticMemoryService;
    private readonly IModelRouterService? _modelRouterService;
    private readonly ILogger _log;

    private CancellationTokenSource? _generationCts;
    // Wave 4b: migrated from `lock (object)` to SemaphoreSlim so cancellation of the
    // previous CTS can be awaited (CancellationTokenSource.CancelAsync awaits any
    // registered cancellation callbacks). The semaphore is *not* reentrant — every
    // critical section in this class is straight-line and does not reacquire the lock.
    private readonly SemaphoreSlim _generationLock = new(1, 1);
    private readonly ConcurrentDictionary<long, ChatContextInspectionSnapshot> _latestContextInspections = new();
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

    /// <inheritdoc />
    public ChatContextInspectionSnapshot? GetLatestContextInspection(long conversationId) =>
        _latestContextInspections.TryGetValue(conversationId, out var snapshot)
            ? snapshot
            : null;

    /// <inheritdoc />
    public async Task<ConversationSummaryRefreshResult> RefreshConversationSummaryInspectionAsync(
        long conversationId,
        CancellationToken ct = default)
    {
        var existingSnapshot = GetLatestContextInspection(conversationId);

        if (_conversationSummaryService is null)
        {
            return ConversationSummaryRefreshResult.Failure(
                existingSnapshot,
                "Summary refresh is unavailable in this app configuration.");
        }

        try
        {
            var refreshed = await _conversationSummaryService
                .RefreshConversationSummaryAsync(conversationId, ct)
                .ConfigureAwait(false);

            if (!refreshed)
            {
                return ConversationSummaryRefreshResult.Failure(
                    existingSnapshot,
                    "Summary refresh failed. Keeping the previous summary state.");
            }

            var summaryInspection = await _conversationSummaryService
                .GetConversationSummaryInspectionAsync(conversationId, ct)
                .ConfigureAwait(false);

            if (summaryInspection is null)
            {
                return ConversationSummaryRefreshResult.Failure(
                    existingSnapshot,
                    "Summary refresh completed, but no updated summary was available.");
            }

            ChatContextInspectionSnapshot updatedSnapshot;
            if (existingSnapshot is not null)
            {
                updatedSnapshot = existingSnapshot with
                {
                    Summary = summaryInspection
                };
            }
            else
            {
                updatedSnapshot = new ChatContextInspectionSnapshot
                {
                    ConversationId = conversationId,
                    CapturedAt = DateTime.UtcNow,
                    CurrentQuery = string.Empty,
                    Summary = summaryInspection,
                    HasLimitedVisibility = true,
                    LimitedVisibilityReason = "summary_only_refresh",
                    AssemblyExplanation = "A durable summary was refreshed without a newly captured response context.",
                    CompressionExplanation = "Compression details are unavailable until a response has been assembled in chat.",
                    RecallExplanation = "Durable recall details are unavailable until a response has been assembled in chat."
                };
            }

            _latestContextInspections[conversationId] = updatedSnapshot;
            return ConversationSummaryRefreshResult.Success(updatedSnapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to refresh summary inspection for conversation {ConversationId}", conversationId);
            return ConversationSummaryRefreshResult.Failure(
                existingSnapshot,
                "Summary refresh failed. Keeping the previous summary state.");
        }
    }

    public ChatService(
        IAiService aiService,
        IConversationService conversationService,
        ISettingsService settingsService,
        IContextAssemblyService contextAssemblyService,
        IConversationMemoryService memoryService,
        ILogger logger,
        IModelRouterService? modelRouterService = null,
        ISemanticMemoryService? semanticMemoryService = null,
        IConversationSummaryService? conversationSummaryService = null)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _conversationService = conversationService ?? throw new ArgumentNullException(nameof(conversationService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _contextAssemblyService = contextAssemblyService ?? throw new ArgumentNullException(nameof(contextAssemblyService));
        _memoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
        _conversationSummaryService = conversationSummaryService;
        _log = logger?.ForContext<ChatService>()
               ?? throw new ArgumentNullException(nameof(logger));
        _modelRouterService = modelRouterService;
        _semanticMemoryService = semanticMemoryService;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<string> SendMessageAsync(
        long conversationId,
        string userMessage,
        CancellationToken ct = default) =>
        SendMessageAsync(conversationId, userMessage, supplementalContext: null, ct);

    /// <inheritdoc />
    public async IAsyncEnumerable<string> SendMessageAsync(
        long conversationId,
        string userMessage,
        SupplementalContext? supplementalContext,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            _log.Warning("Attempted to send empty message to conversation {ConversationId}", conversationId);
            yield break;
        }

        // Create a linked cancellation token so StopGenerationAsync can cancel mid-stream
        var linkedCts = await BeginGenerationAsync(ct).ConfigureAwait(false);
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

            // 3-7. Assemble the context from the full history, stream the reply, persist it
            await foreach (var token in StreamReplyAsync(
                conversationId,
                conversation.SystemPrompt,
                BuildChatMessages(conversation.Messages),
                userMessage,
                supplementalContext,
                replacedMessageId: null,
                stopwatch,
                linkedCts.Token))
            {
                yield return token;
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
    public async IAsyncEnumerable<string> RegenerateResponseAsync(
        long conversationId,
        long userMessageId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var linkedCts = await BeginGenerationAsync(ct).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            IsGenerating = true;

            var conversation = await _conversationService.GetConversationAsync(conversationId)
                ?? throw new InvalidOperationException($"Conversation {conversationId} not found.");

            var messages = conversation.Messages.OrderBy(m => m.SortOrder).ToList();
            var promptIndex = messages.FindIndex(m => m.Id == userMessageId);
            if (promptIndex < 0 || messages[promptIndex].Role != "user")
            {
                throw new InvalidOperationException(
                    $"Message {userMessageId} is not a user message in conversation {conversationId}.");
            }

            // Only the exchange that closes the thread can be regenerated. Anything after the
            // prompt other than its own answer would be left answering a different history.
            var following = messages.Skip(promptIndex + 1).ToList();
            if (following.Count > 1 || following.Any(m => m.Role != "assistant"))
            {
                throw new InvalidOperationException(
                    $"Only the latest response in conversation {conversationId} can be regenerated.");
            }

            // The prompt row is reused rather than re-sent, so regenerating never duplicates it,
            // and the old answer stays in place until the new one has been saved.
            await foreach (var token in StreamReplyAsync(
                conversationId,
                conversation.SystemPrompt,
                BuildChatMessages(messages.Take(promptIndex + 1)),
                messages[promptIndex].Content,
                supplementalContext: null,
                replacedMessageId: following.FirstOrDefault()?.Id,
                stopwatch,
                linkedCts.Token))
            {
                yield return token;
            }
        }
        finally
        {
            stopwatch.Stop();
            IsGenerating = false;
            linkedCts.Dispose();
        }
    }

    /// <summary>
    /// Assembles the context for <paramref name="currentQuery"/>, streams the reply, and persists
    /// it once it is complete, with the model that wrote it and the sources it was given. When
    /// <paramref name="replacedMessageId"/> is set, that answer is removed only after the new one
    /// has been saved, so a stop or a failure keeps it.
    /// </summary>
    private async IAsyncEnumerable<string> StreamReplyAsync(
        long conversationId,
        string? systemPrompt,
        IReadOnlyList<ChatMessage> chatMessages,
        string currentQuery,
        SupplementalContext? supplementalContext,
        long? replacedMessageId,
        Stopwatch stopwatch,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // 5. Build chat options from settings
        var options = await BuildChatOptionsAsync();

        // 5b. Model routing picks the provider and model for this reply only
        var routedTarget = await RouteReplyAsync(currentQuery, ct);

        // 6. Assemble context with semantic selection and graceful fallback
        var memoryContext = await LoadMemoryContextAsync(conversationId, currentQuery, ct);
        var promptContext = supplementalContext?.PromptContext;
        if (!string.IsNullOrWhiteSpace(promptContext))
        {
            memoryContext = string.IsNullOrWhiteSpace(memoryContext)
                ? promptContext
                : memoryContext + Environment.NewLine + Environment.NewLine + promptContext;
        }

        var assembledContext = await _contextAssemblyService.AssembleAsync(
            new ContextAssemblyRequest
            {
                ConversationId = conversationId,
                CurrentQuery = currentQuery,
                SystemPrompt = systemPrompt,
                MemoryContext = memoryContext,
                ConversationMessages = chatMessages.ToList(),
                ContextWindow = options?.ContextWindow ?? 0,
                ReserveForResponse = AppConstants.ContextWindowTokenReserve
            },
            ct);
        chatMessages = assembledContext.Messages;
        systemPrompt = assembledContext.SystemPrompt;

        await CaptureLatestContextInspectionAsync(
            conversationId,
            currentQuery,
            assembledContext,
            ct).ConfigureAwait(false);

        // 7. Stream the AI response
        _log.Debug(
            "Starting streaming response for conversation {ConversationId} ({MessageCount} messages in context)",
            conversationId, chatMessages.Count);

        var responseBuilder = new StringBuilder();
        var tokenCount = 0;

        // The model that writes this reply, saved with it: the routed one, else the active one.
        var answeringModelId = routedTarget?.ModelId ?? ActiveModelIdOrNull();

        var stream = routedTarget is null
            ? _aiService.StreamChatAsync(chatMessages, systemPrompt, options, ct)
            : StreamFromRoutedProviderAsync(routedTarget, chatMessages, systemPrompt, options, ct);

        await foreach (var token in stream)
        {
            responseBuilder.Append(token);
            tokenCount++;
            yield return token;
        }

        stopwatch.Stop();
        var fullResponse = responseBuilder.ToString();

        // 8. Persist the complete assistant response
        if (!string.IsNullOrEmpty(fullResponse))
        {
            await _conversationService.AddMessageAsync(
                conversationId,
                "assistant",
                fullResponse,
                tokenCount: tokenCount,
                generationTimeMs: stopwatch.Elapsed.TotalMilliseconds,
                modelId: answeringModelId,
                citationsJson: MessageCitations.Serialize(supplementalContext?.Citations));

            if (replacedMessageId is long replaced)
            {
                await RemoveReplacedResponseAsync(conversationId, replaced);
            }

            _log.Information(
                "Completed streaming response for conversation {ConversationId}: {TokenCount} tokens in {ElapsedMs:F0}ms",
                conversationId, tokenCount, stopwatch.Elapsed.TotalMilliseconds);

            // Extract memories from this conversation (non-blocking, prefer semantic service)
            _ = Task.Run(async () =>
            {
                try
                {
                    if (_semanticMemoryService is not null)
                    {
                        await _semanticMemoryService.ExtractMemoriesAsync(conversationId);
                    }
                    else
                    {
                        await _memoryService.ExtractMemoriesAsync(conversationId);
                    }
                }
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

    /// <summary>
    /// Asks the model router for this reply's provider and model when routing is on. The
    /// decision applies to this reply only: the app-wide provider, the active model and the
    /// saved settings stay as the operator set them. (It used to switch the app-wide provider
    /// and save the routed model, so every routed message silently changed what Settings showed
    /// as active.) Returns null to answer with the active provider: routing is off, or the
    /// routed provider is not registered or not reachable.
    /// </summary>
    private async Task<RoutedTarget?> RouteReplyAsync(string currentQuery, CancellationToken ct)
    {
        if (_modelRouterService is null)
        {
            return null;
        }

        try
        {
            var routingSettings = await _settingsService.GetSettingsAsync();
            if (!routingSettings.EnableModelRouting)
            {
                return null;
            }

            var routingDecision = await _modelRouterService.RouteAsync(currentQuery, ct);

            _log.Information(
                "Routing this reply: Provider={ProviderId}, Model={ModelId}, Task={TaskType}, Reason={Reason}",
                routingDecision.ProviderId, routingDecision.ModelId,
                routingDecision.TaskType.Name, routingDecision.Reason);

            // Notify listeners (UI indicators, telemetry, etc.)
            RoutingDecisionMade?.Invoke(this, routingDecision);

            var provider = _aiService.GetProvider(routingDecision.ProviderId);
            if (provider is null || string.IsNullOrWhiteSpace(routingDecision.ModelId))
            {
                _log.Warning(
                    "Routed provider {ProviderId} is not registered; answering with the active provider",
                    routingDecision.ProviderId);
                return null;
            }

            if (!await _aiService.IsProviderAvailableAsync(routingDecision.ProviderId, ct))
            {
                _log.Warning(
                    "Routed provider {ProviderId} is not reachable; answering with the active provider",
                    routingDecision.ProviderId);
                return null;
            }

            return new RoutedTarget(provider, routingDecision.ModelId.Trim());
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.Warning(ex, "Model routing failed, answering with the active provider");
            return null;
        }
    }

    /// <summary>
    /// Streams the reply from the routed provider with the routed model, the way
    /// <see cref="IAiService.StreamChatAsync"/> would for the active one: the system prompt goes
    /// first, and the caller's options are copied rather than changed.
    /// </summary>
    private static IAsyncEnumerable<string> StreamFromRoutedProviderAsync(
        RoutedTarget target,
        IReadOnlyList<ChatMessage> chatMessages,
        string? systemPrompt,
        ChatOptions? options,
        CancellationToken ct)
    {
        var messages = new List<ChatMessage>(chatMessages.Count + 1);
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(ChatMessage.System(systemPrompt));
        }

        messages.AddRange(chatMessages);

        var routedOptions = options?.ShallowCopy() ?? new ChatOptions();
        routedOptions.ModelId = target.ModelId;
        return target.Provider.StreamChatAsync(messages, routedOptions, ct);
    }

    /// <summary>The provider and model a routed reply is answered with.</summary>
    private sealed record RoutedTarget(IAiProvider Provider, string ModelId);

    private string? ActiveModelIdOrNull()
    {
        var modelId = _aiService.ActiveModelId;
        return string.IsNullOrWhiteSpace(modelId) ? null : modelId;
    }

    /// <summary>
    /// Removes the answer a regeneration replaced. The new answer is already saved, so a
    /// failure here leaves both in the thread rather than losing either; it is logged, not thrown.
    /// </summary>
    private async Task RemoveReplacedResponseAsync(long conversationId, long replacedMessageId)
    {
        try
        {
            await _conversationService.DeleteMessageAsync(replacedMessageId);
        }
        catch (Exception ex)
        {
            _log.Warning(
                ex,
                "Regenerated response saved, but replaced response {MessageId} in conversation {ConversationId} could not be removed",
                replacedMessageId, conversationId);
        }
    }

    /// <summary>
    /// Cancels any generation still running and returns a token source linked to both the
    /// caller's token and <see cref="StopGenerationAsync"/>.
    /// </summary>
    private async Task<CancellationTokenSource> BeginGenerationAsync(CancellationToken ct)
    {
        await _generationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_generationCts is not null)
                await _generationCts.CancelAsync().ConfigureAwait(false);
            _generationCts?.Dispose();
            _generationCts = new CancellationTokenSource();
            return CancellationTokenSource.CreateLinkedTokenSource(ct, _generationCts.Token);
        }
        finally
        {
            _generationLock.Release();
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
    public async Task StopGenerationAsync()
    {
        await _generationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_generationCts is not null && !_generationCts.IsCancellationRequested)
            {
                _log.Information("Stopping generation");
                await _generationCts.CancelAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _generationLock.Release();
        }
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

    private async Task<string> LoadMemoryContextAsync(
        long conversationId,
        string query,
        CancellationToken ct)
    {
        var contextParts = new List<string>(2);

        try
        {
            if (_semanticMemoryService is not null)
            {
                var relevantMemories = await _semanticMemoryService.RetrieveRelevantMemoriesAsync(
                    query, maxMemories: 8, minSimilarity: 0.65f, ct).ConfigureAwait(false);
                var semanticMemoryContext = FormatMemoriesAsContext(relevantMemories);
                if (!string.IsNullOrWhiteSpace(semanticMemoryContext))
                {
                    contextParts.Add(semanticMemoryContext);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to load memory context for conversation {ConversationId}", conversationId);
        }

        if (_semanticMemoryService is null)
        {
            try
            {
                var memoryContext = await _memoryService.GetMemoryContextAsync(8, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(memoryContext))
                {
                    contextParts.Add(memoryContext);
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to load memory context for conversation {ConversationId}", conversationId);
            }
        }

        if (_conversationSummaryService is not null)
        {
            try
            {
                var summaryContext = await _conversationSummaryService
                    .GetConversationSummaryContextAsync(conversationId, ct)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(summaryContext))
                {
                    contextParts.Add(summaryContext);
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to load durable summary context for conversation {ConversationId}", conversationId);
            }
        }

        return contextParts.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine + Environment.NewLine, contextParts);
    }

    private async Task CaptureLatestContextInspectionAsync(
        long conversationId,
        string currentQuery,
        ContextAssemblyResult assembledContext,
        CancellationToken ct)
    {
        ConversationSummaryInspection? summaryInspection = null;

        if (_conversationSummaryService is not null)
        {
            try
            {
                summaryInspection = await _conversationSummaryService
                    .GetConversationSummaryInspectionAsync(conversationId, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Warning(ex, "Failed to load durable summary inspection for conversation {ConversationId}", conversationId);
            }
        }

        var snapshot = new ChatContextInspectionSnapshot
        {
            ConversationId = conversationId,
            CapturedAt = DateTime.UtcNow,
            CurrentQuery = currentQuery,
            Diagnostics = assembledContext.Diagnostics,
            Summary = summaryInspection,
            RecallMatches = assembledContext.DurableRecallResults
                .Select(result => new ChatContextRecallInspectionItem
                {
                    ConversationId = result.ConversationId,
                    MessageId = result.MessageId,
                    ConversationTitle = result.ConversationTitle,
                    Role = result.Role,
                    ContentPreview = result.ContentPreview,
                    Timestamp = result.Timestamp,
                    Similarity = result.Similarity
                })
                .ToList(),
            AssemblyExplanation = BuildAssemblyExplanation(assembledContext.Diagnostics),
            CompressionExplanation = BuildCompressionExplanation(assembledContext.Diagnostics),
            RecallExplanation = BuildRecallExplanation(assembledContext.Diagnostics)
        };

        _latestContextInspections[conversationId] = snapshot;
    }

    private static string BuildAssemblyExplanation(ContextAssemblyDiagnostics diagnostics)
    {
        if (diagnostics.UsedLegacyFallback)
        {
            return "Agent-X used the legacy context fitting path for this response.";
        }

        if (diagnostics.UsedLexicalFallback)
        {
            return "Agent-X used lexical fallback while selecting message context for this response.";
        }

        return diagnostics.OverflowMessageCount > 0
            ? "Agent-X selected a bounded subset of the thread and evaluated overflow context against the remaining budget."
            : "Agent-X fit the active thread without needing to trim or fall back.";
    }

    private static string BuildCompressionExplanation(ContextAssemblyDiagnostics diagnostics)
    {
        if (diagnostics.AddedOverflowSummary)
        {
            return "Agent-X added a compressed overflow summary to preserve older context within the available budget.";
        }

        return diagnostics.CompressionSkipReason switch
        {
            "history_fit_without_recall" => "The active history fit inside the available budget, so no overflow summary was needed.",
            "compression_error" => "Overflow compression was skipped because compression failed.",
            "summary_exceeded_unused_budget" => "Overflow compression was generated but exceeded the remaining token budget.",
            null or "" => "No overflow summary was added for this response.",
            _ => $"No overflow summary was added ({diagnostics.CompressionSkipReason.Replace('_', ' ')})."
        };
    }

    private static string BuildRecallExplanation(ContextAssemblyDiagnostics diagnostics)
    {
        if (diagnostics.AddedDurableRecall)
        {
            return diagnostics.RecalledMessageCount == 1
                ? "Agent-X added 1 recalled message from another conversation as supporting context."
                : $"Agent-X added {diagnostics.RecalledMessageCount} recalled messages from other conversations as supporting context.";
        }

        return diagnostics.DurableRecallSkipReason switch
        {
            "no_recall_matches" => "Durable recall found no relevant cross-conversation matches for this response.",
            "duplicate_to_selected_context" => "Durable recall found matches, but they duplicated the already selected context.",
            "insufficient_recall_budget" => "Durable recall was skipped because too little token budget remained after core context selection.",
            "recall_budget_exceeded" => "Durable recall found useful matches, but adding them would have exceeded the remaining token budget.",
            "legacy_fallback" => "Durable recall details are unavailable because the legacy context fallback path was used.",
            "recall_service_unavailable" => "Durable recall was unavailable for this response.",
            "no_conversation_id" => "Durable recall was unavailable because the response was not tied to a persisted conversation.",
            null or "" => "Durable recall was not added for this response.",
            _ => $"Durable recall was not added ({diagnostics.DurableRecallSkipReason.Replace('_', ' ')})."
        };
    }

    /// <summary>
    /// Formats semantic memory results as a context block for system prompts.
    /// </summary>
    private static string FormatMemoriesAsContext(IReadOnlyList<Data.Entities.MemoryEntity> memories)
    {
        if (memories.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("[User Memory Context - Use these to personalize responses]");

        foreach (var m in memories)
        {
            var label = m.Category switch
            {
                "user_preference" or "preference" => "Preference",
                "user_instruction" or "instruction" => "Instruction",
                "interaction_style" => "Interaction Style",
                "communication_preference" => "Communication Preference",
                "domain_expertise" => "Expertise",
                "technical_preference" => "Technical Preference",
                "project_context" => "Project Context",
                "correction" => "Correction",
                "affirmation" => "Affirmation",
                "learning" => "Learning",
                "requirement" => "Requirement",
                "constraint" => "Constraint",
                "user_topic" or "topic" => "Topic of Interest",
                "episodic_event" => "Event",
                "user_fact" or "fact" => "Known Fact",
                _ => "Memory"
            };
            sb.AppendLine($"- {label}: {m.Content}");
        }

        return sb.ToString();
    }
}
