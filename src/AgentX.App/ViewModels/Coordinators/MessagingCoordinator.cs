using System.Diagnostics;
using System.Text;
using AgentX.App.Services;
using AgentX.Core.AI;
using AgentX.Core.AI.Agents;
using AgentX.Core.AI.Models;
using AgentX.Core.Data.Entities;
using AgentX.Core.Search.Models;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Chat.Models;
using AgentX.Core.Services.Feedback;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Search;
using AgentX.Core.Services.Settings;
using Serilog;

namespace AgentX.App.ViewModels.Coordinators;

/// <summary>
/// Orchestrates message sending, streaming, generation control, feedback, and deletion.
/// Raises events for the ChatViewModel to synchronize UI state.
/// </summary>
public sealed class MessagingCoordinator : IMessagingCoordinator
{
    private static readonly TimeSpan ResearchSearchTimeout = TimeSpan.FromSeconds(15);

    // How much of the conversation a multi-agent answer is given: at most the last few messages,
    // and of those only the newest that fit in the character budget.
    private const int OrchestrationHistoryMessageLimit = 8;
    private const int OrchestrationHistoryCharacterBudget = 6_000;

    private readonly IChatService _chatService;
    private readonly IConversationService _conversationService;
    private readonly IAiService _aiService;
    private readonly IFeedbackService _feedbackService;
    private readonly IMultiAgentOrchestrator? _multiAgentOrchestrator;
    private readonly IWebSearchService? _webSearchService;
    private readonly ISettingsService? _settingsService;
    private readonly ILocalizationService? _localization;

    // The source of the generation currently running. Every generation owns its own source,
    // so a newer one never shares or disposes an older one's, and Stop reaches the newest.
    private CancellationTokenSource? _generationCts;

    // Why Research Mode last could not run at all (web search unavailable, switched off in
    // Settings, or no provider configured). That lasts until the configuration changes, so the
    // notice is shown once instead of on every send; a send with Research Mode off, or a
    // configuration that lets the search run, arms it again.
    private ResearchUnavailableReason? _researchUnavailableNotice;

    public event EventHandler<string>? TokenReceived;
    public event EventHandler<StreamingCompletedEventArgs>? StreamingCompleted;
    public event EventHandler<string>? GenerationError;
    public event EventHandler<NotificationRequestEventArgs>? NotificationRequested;

    public bool IsGenerating => Volatile.Read(ref _generationCts) is { IsCancellationRequested: false };

    public MessagingCoordinator(
        IChatService chatService,
        IConversationService conversationService,
        IAiService aiService,
        IFeedbackService feedbackService,
        IMultiAgentOrchestrator? multiAgentOrchestrator = null,
        IWebSearchService? webSearchService = null,
        ISettingsService? settingsService = null,
        ILocalizationService? localization = null)
    {
        _chatService = chatService;
        _conversationService = conversationService;
        _aiService = aiService;
        _feedbackService = feedbackService;
        _multiAgentOrchestrator = multiAgentOrchestrator;
        _webSearchService = webSearchService;
        _settingsService = settingsService;
        _localization = localization;
    }

    /// <inheritdoc />
    public Task<SendMessageResult> SendMessageAsync(
        string userContent,
        long? conversationId,
        string? systemPrompt,
        string? modelId,
        bool isResearchMode) =>
        SendMessageAsync(
            userContent,
            conversationId,
            systemPrompt,
            modelId,
            isResearchMode,
            ChatOrchestrationMode.Standard);

    /// <inheritdoc />
    public async Task<SendMessageResult> SendMessageAsync(
        string userContent,
        long? conversationId,
        string? systemPrompt,
        string? modelId,
        bool isResearchMode,
        ChatOrchestrationMode orchestrationMode)
    {
        var generation = BeginGeneration();
        var exchange = new Exchange(conversationId);
        if (!isResearchMode)
        {
            _researchUnavailableNotice = null;
        }

        try
        {
            if (exchange.ConversationId is null)
            {
                try
                {
                    var title = userContent.Length > 60
                        ? userContent[..60] + "..."
                        : userContent;
                    var newConv = await _conversationService.CreateConversationAsync(
                        title: title,
                        systemPrompt: systemPrompt,
                        modelId: modelId);
                    exchange.ConversationId = newConv.Id;
                    exchange.ConversationTitle = newConv.Title;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to create conversation, streaming without persistence");
                }
            }

            if (exchange.ConversationId is long persistedId)
            {
                exchange.Baseline = await ReadLatestMessageIdAsync(persistedId);
            }

            var orchestrated = orchestrationMode != ChatOrchestrationMode.Standard;
            var persisted = !orchestrated && exchange.ConversationId is not null && await IsProviderConnectedAsync();

            SupplementalContext? research = null;
            if (isResearchMode && (orchestrated || persisted))
            {
                research = await BuildResearchContextAsync(userContent, generation.Token);
                exchange.WebCitations = research?.Citations;
            }

            if (orchestrated)
            {
                // The new message is saved after its answer, so every saved message is earlier.
                var earlierMessages = await ReadEarlierMessagesAsync(exchange.ConversationId, promptMessageId: null);
                exchange.TokenCount = await RunOrchestratedAsync(
                    exchange,
                    userContent,
                    systemPrompt,
                    orchestrationMode,
                    research?.PromptContext,
                    earlierMessages,
                    isRegeneration: false,
                    replacedMessageId: null,
                    generation);
                if (exchange.ConversationId.HasValue)
                {
                    exchange.ContextInspection = ChatContextInspectionSnapshot.CreateLimited(
                        exchange.ConversationId.Value,
                        userContent,
                        "multi_agent_orchestration");
                }
            }
            else if (persisted)
            {
                // Stream via IChatService (which persists and streams)
                var conversation = exchange.ConversationId!.Value;
                var stream = research is null
                    ? _chatService.SendMessageAsync(conversation, userContent, generation.Token)
                    : _chatService.SendMessageAsync(conversation, userContent, research, generation.Token);
                exchange.TokenCount = await RelayTokensAsync(stream, exchange, generation);
                exchange.ContextInspection = _chatService.GetLatestContextInspection(conversation);
            }
            else
            {
                // Fallback: direct streaming without persistence
                await StreamDirectAsync(exchange.Response, userContent, systemPrompt, generation);
                if (exchange.ConversationId.HasValue)
                {
                    exchange.ContextInspection = ChatContextInspectionSnapshot.CreateLimited(
                        exchange.ConversationId.Value,
                        userContent,
                        _aiService.ActiveProvider is null ? "no_active_provider" : "provider_disconnected");
                }
            }

            return await CompleteAsync(exchange, knownUserMessageId: null, generation);
        }
        catch (Exception ex) when (ex is OperationCanceledException || generation.IsCancellationRequested)
        {
            return await CancelledAsync(exchange, knownUserMessageId: null);
        }
        catch (Exception ex)
        {
            var hint = ActiveProviderCheckHint();
            return await FailedAsync(
                exchange,
                knownUserMessageId: null,
                ex,
                ProviderStatusText.Resolve(
                    _localization?.GetString("Chat_GenerationFailedHint", hint),
                    "Chat_GenerationFailedHint",
                    "An error occurred while generating a response. {0}",
                    hint),
                ProviderStatusText.Resolve(
                    _localization?.GetString("Chat_GenerationFailedToast"),
                    "Chat_GenerationFailedToast",
                    "Could not generate a response. Check your AI connection in Settings."),
                generation);
        }
        finally
        {
            EndGeneration(generation);
        }
    }

    /// <inheritdoc />
    public async Task<SendMessageResult> RegenerateResponseAsync(
        long conversationId,
        long userMessageId,
        string userContent,
        string? systemPrompt,
        ChatOrchestrationMode orchestrationMode)
    {
        var generation = BeginGeneration();
        var exchange = new Exchange(conversationId);

        try
        {
            exchange.Baseline = await ReadLatestMessageIdAsync(conversationId);

            if (orchestrationMode != ChatOrchestrationMode.Standard)
            {
                var replacedMessageId = await FindReplaceableResponseAsync(conversationId, userMessageId);
                var earlierMessages = await ReadEarlierMessagesAsync(conversationId, userMessageId);
                exchange.TokenCount = await RunOrchestratedAsync(
                    exchange,
                    userContent,
                    systemPrompt,
                    orchestrationMode,
                    researchContext: null,
                    earlierMessages,
                    isRegeneration: true,
                    replacedMessageId,
                    generation);
                exchange.ContextInspection = ChatContextInspectionSnapshot.CreateLimited(
                    conversationId,
                    userContent,
                    "multi_agent_orchestration");
            }
            else
            {
                // The offline fallback streams without persisting, which cannot replace a saved
                // answer, so regeneration needs the provider.
                if (!await IsProviderConnectedAsync())
                {
                    throw new InvalidOperationException("The AI provider is not reachable.");
                }

                exchange.TokenCount = await RelayTokensAsync(
                    _chatService.RegenerateResponseAsync(conversationId, userMessageId, generation.Token),
                    exchange,
                    generation);
                exchange.ContextInspection = _chatService.GetLatestContextInspection(conversationId);
            }

            return await CompleteAsync(exchange, userMessageId, generation);
        }
        catch (Exception ex) when (ex is OperationCanceledException || generation.IsCancellationRequested)
        {
            return await CancelledAsync(exchange, userMessageId);
        }
        catch (Exception ex)
        {
            return await FailedAsync(
                exchange,
                userMessageId,
                ex,
                ProviderStatusText.Resolve(
                    _localization?.GetString("Chat_RegenerateFailedHint"),
                    "Chat_RegenerateFailedHint",
                    "An error occurred while generating a new response. The previous response was kept."),
                ProviderStatusText.Resolve(
                    _localization?.GetString("Chat_RegenerateFailedToast"),
                    "Chat_RegenerateFailedToast",
                    "Could not generate a new response, so the previous one was kept. Check your AI connection in Settings."),
                generation);
        }
        finally
        {
            EndGeneration(generation);
        }
    }

    /// <summary>
    /// Everything one generation accumulates: the conversation it runs in, the streamed text,
    /// and what is needed to find the rows it persisted.
    /// </summary>
    private sealed class Exchange
    {
        public Exchange(long? conversationId) => ConversationId = conversationId;

        public long? ConversationId { get; set; }
        public string? ConversationTitle { get; set; }
        public StringBuilder Response { get; } = new();
        public int TokenCount { get; set; }
        public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
        public ChatContextInspectionSnapshot? ContextInspection { get; set; }
        public IReadOnlyList<WebCitation>? WebCitations { get; set; }

        /// <summary>
        /// The newest message id in the conversation before this generation wrote anything,
        /// or null when it could not be read. Rows above it are the ones this generation saved.
        /// </summary>
        public long? Baseline { get; set; }
    }

    private async Task<SendMessageResult> CompleteAsync(
        Exchange exchange,
        long? knownUserMessageId,
        CancellationTokenSource generation)
    {
        exchange.Stopwatch.Stop();
        var (user, assistant) = await ResolvePersistedExchangeAsync(exchange);

        var result = new SendMessageResult
        {
            ConversationId = exchange.ConversationId,
            ResponseContent = exchange.Response.ToString(),
            TokenCount = exchange.TokenCount,
            GenerationTimeMs = exchange.Stopwatch.Elapsed.TotalMilliseconds,
            ConversationTitle = exchange.ConversationTitle,
            ContextInspection = exchange.ContextInspection,
            AssistantMessageId = assistant?.Id,
            AssistantMessageSortOrder = assistant?.SortOrder,
            UserMessageId = knownUserMessageId ?? user?.Id,
            UserMessageSortOrder = knownUserMessageId.HasValue ? null : user?.SortOrder,
            WebCitations = exchange.WebCitations
        };

        // A generation told to stop may still run to its end. By then the chat can be showing
        // another thread, so the completion is only announced while the generation is wanted;
        // the caller still receives the result.
        if (!generation.IsCancellationRequested)
        {
            StreamingCompleted?.Invoke(this, new StreamingCompletedEventArgs
            {
                ConversationId = result.ConversationId,
                ResponseContent = result.ResponseContent,
                TokenCount = result.TokenCount,
                GenerationTimeMs = result.GenerationTimeMs,
                ConversationTitle = result.ConversationTitle,
                ContextInspection = result.ContextInspection,
                AssistantMessageId = result.AssistantMessageId,
                AssistantMessageSortOrder = result.AssistantMessageSortOrder,
                UserMessageId = result.UserMessageId,
                UserMessageSortOrder = result.UserMessageSortOrder,
                WebCitations = result.WebCitations
            });
        }

        return result;
    }

    private async Task<SendMessageResult> CancelledAsync(Exchange exchange, long? knownUserMessageId)
    {
        exchange.Stopwatch.Stop();
        Log.Debug("Generation cancelled by user");

        if (exchange.ContextInspection is null && exchange.ConversationId.HasValue)
        {
            exchange.ContextInspection = _chatService.GetLatestContextInspection(exchange.ConversationId.Value);
        }

        // The prompt is saved before the answer streams, so a stopped send has usually persisted
        // it. Reporting its id lets the chat act on that row instead of treating it as unsaved.
        var (user, _) = await ResolvePersistedExchangeAsync(exchange);
        var partial = exchange.Response.ToString();
        var stopMarker = ProviderStatusText.Resolve(
            _localization?.GetString("Chat_GenerationStopped"),
            "Chat_GenerationStopped",
            "[Generation stopped]");

        return new SendMessageResult
        {
            ConversationId = exchange.ConversationId,
            ResponseContent = partial.Length == 0 ? stopMarker : partial + "\n\n" + stopMarker,
            TokenCount = exchange.TokenCount,
            GenerationTimeMs = exchange.Stopwatch.Elapsed.TotalMilliseconds,
            WasCancelled = true,
            ConversationTitle = exchange.ConversationTitle,
            ContextInspection = exchange.ContextInspection,
            UserMessageId = knownUserMessageId ?? user?.Id,
            UserMessageSortOrder = knownUserMessageId.HasValue ? null : user?.SortOrder
        };
    }

    private async Task<SendMessageResult> FailedAsync(
        Exchange exchange,
        long? knownUserMessageId,
        Exception exception,
        string errorMessage,
        string notificationMessage,
        CancellationTokenSource generation)
    {
        exchange.Stopwatch.Stop();
        Log.Error(exception, "Error during message generation");

        if (!generation.IsCancellationRequested)
        {
            GenerationError?.Invoke(this, errorMessage);
        }

        NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
        {
            Level = "error",
            Title = ProviderStatusText.Resolve(
                _localization?.GetString("Chat_GenerationFailedTitle"),
                "Chat_GenerationFailedTitle",
                "Generation Failed"),
            Message = notificationMessage
        });

        if (exchange.ContextInspection is null && exchange.ConversationId.HasValue)
        {
            exchange.ContextInspection = _chatService.GetLatestContextInspection(exchange.ConversationId.Value);
        }

        var (user, _) = await ResolvePersistedExchangeAsync(exchange);

        return new SendMessageResult
        {
            ConversationId = exchange.ConversationId,
            ResponseContent = errorMessage,
            TokenCount = exchange.TokenCount,
            GenerationTimeMs = exchange.Stopwatch.Elapsed.TotalMilliseconds,
            HadError = true,
            ErrorMessage = errorMessage,
            ConversationTitle = exchange.ConversationTitle,
            ContextInspection = exchange.ContextInspection,
            UserMessageId = knownUserMessageId ?? user?.Id,
            UserMessageSortOrder = knownUserMessageId.HasValue ? null : user?.SortOrder
        };
    }

    private async Task<int> RelayTokensAsync(
        IAsyncEnumerable<string> stream,
        Exchange exchange,
        CancellationTokenSource generation)
    {
        var count = 0;
        await foreach (var token in stream)
        {
            exchange.Response.Append(token);
            count++;
            RaiseToken(generation, token);
        }

        return count;
    }

    /// <summary>
    /// Forwards a token unless the generation has been told to stop. Tokens still in flight
    /// after a stop would otherwise land in whatever bubble the chat is streaming into next.
    /// </summary>
    private void RaiseToken(CancellationTokenSource generation, string token)
    {
        if (!generation.IsCancellationRequested)
        {
            TokenReceived?.Invoke(this, token);
        }
    }

    private CancellationTokenSource BeginGeneration()
    {
        var generation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _generationCts, generation);
        if (previous is not null)
        {
            // One generation at a time: a newer one supersedes any still winding down.
            try
            {
                previous.Cancel();
            }
            catch (Exception ex) when (ex is ObjectDisposedException or AggregateException)
            {
                Log.Debug(ex, "Superseded generation had already finished");
            }
        }

        return generation;
    }

    private void EndGeneration(CancellationTokenSource generation)
    {
        Interlocked.CompareExchange(ref _generationCts, null, generation);
        generation.Dispose();
    }

    private async Task<bool> IsProviderConnectedAsync()
    {
        if (_aiService.ActiveProvider is null)
        {
            return false;
        }

        try
        {
            return await _aiService.ActiveProvider.CheckConnectionAsync();
        }
        catch
        {
            // Treat as disconnected
            return false;
        }
    }

    private async Task<int> RunOrchestratedAsync(
        Exchange exchange,
        string userContent,
        string? systemPrompt,
        ChatOrchestrationMode orchestrationMode,
        string? researchContext,
        IReadOnlyList<MessageEntity> earlierMessages,
        bool isRegeneration,
        long? replacedMessageId,
        CancellationTokenSource generation)
    {
        if (_multiAgentOrchestrator is null)
        {
            throw new InvalidOperationException("Multi-agent orchestration is not available in this app session.");
        }

        var strategy = orchestrationMode == ChatOrchestrationMode.MultiAgentDebate
            ? OrchestratorStrategy.Debate
            : OrchestratorStrategy.Parallel;

        // Each agent gets this text and its role, nothing else from the chat, so the text carries
        // the recent conversation too: without it a follow-up question had nothing to refer to.
        var conversation = FormatRecentConversation(earlierMessages);
        var task = conversation is null
            ? userContent
            : $"{conversation}\n\nNew message:\n{userContent}";
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            task = $"{task}\n\nActive system prompt:\n{systemPrompt}";
        }

        if (!string.IsNullOrWhiteSpace(researchContext))
        {
            task = $"{task}\n\n{researchContext}";
        }

        var orchestration = await _multiAgentOrchestrator.RunAsync(
            task,
            BuildDefaultAgentRoles(orchestrationMode),
            strategy,
            generation.Token);

        var succeeded = orchestration.IsSuccess && !string.IsNullOrWhiteSpace(orchestration.FinalAnswer);
        if (!succeeded && isRegeneration)
        {
            // A failure notice must not replace an answer that exists.
            throw new InvalidOperationException(BuildOrchestrationFailureMessage(orchestration));
        }

        // The orchestrator repeats its task at the top of the answer ("Task: ..." or "Topic: ...").
        // Besides the message, the task holds what the agents were given to work with (the recent
        // conversation, the system prompt, web results), so the answer names only the message.
        var finalContent = succeeded
            ? orchestration.FinalAnswer.Replace(task, userContent, StringComparison.Ordinal)
            : BuildOrchestrationFailureMessage(orchestration);

        exchange.Response.Append(finalContent);
        RaiseToken(generation, finalContent);

        var tokenCount = EstimateTokenCount(finalContent);
        if (exchange.ConversationId is long conversationId)
        {
            if (!isRegeneration)
            {
                await _conversationService.AddMessageAsync(conversationId, "user", userContent, null, null);
            }

            // A usable answer is saved with the model that wrote it (the orchestrator's agents all
            // run on the active model) and the web sources it was given.
            await _conversationService.AddMessageAsync(
                conversationId,
                "assistant",
                finalContent,
                tokenCount,
                exchange.Stopwatch.Elapsed.TotalMilliseconds,
                modelId: succeeded ? ActiveModelIdOrNull() : null,
                citationsJson: succeeded ? MessageCitations.Serialize(exchange.WebCitations) : null);

            if (replacedMessageId is long replaced)
            {
                // The new answer is saved, so the old one goes now; if that fails both stay.
                try
                {
                    await _conversationService.DeleteMessageAsync(replaced);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Regenerated response saved, but replaced response {MessageId} could not be removed", replaced);
                }
            }
        }

        return tokenCount;
    }

    /// <summary>
    /// Returns the answer a regeneration of <paramref name="userMessageId"/> replaces, or null
    /// when the prompt closes the thread unanswered. Throws when later messages follow.
    /// </summary>
    private async Task<long?> FindReplaceableResponseAsync(long conversationId, long userMessageId)
    {
        var messages = (await _conversationService.GetMessagesAsync(conversationId)).ToList();
        var promptIndex = messages.FindIndex(message => message.Id == userMessageId);
        if (promptIndex < 0 || !HasRole(messages[promptIndex], "user"))
        {
            throw new InvalidOperationException(
                $"Message {userMessageId} is not a user message in conversation {conversationId}.");
        }

        var following = messages.Skip(promptIndex + 1).ToList();
        if (following.Count > 1 || following.Any(message => !HasRole(message, "assistant")))
        {
            throw new InvalidOperationException(
                $"Only the latest response in conversation {conversationId} can be regenerated.");
        }

        return following.FirstOrDefault()?.Id;
    }

    /// <summary>
    /// The saved messages that come before the one being answered: all of them, or those before
    /// <paramref name="promptMessageId"/> when a saved prompt is answered again. None when there
    /// is no conversation or it cannot be read, so the answer still runs, only without it.
    /// </summary>
    private async Task<IReadOnlyList<MessageEntity>> ReadEarlierMessagesAsync(long? conversationId, long? promptMessageId)
    {
        if (conversationId is not long id)
        {
            return [];
        }

        try
        {
            var messages = await _conversationService.GetMessagesAsync(id);
            return messages.TakeWhile(message => message.Id != promptMessageId).ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read conversation {ConversationId}; the agents answer without it", id);
            return [];
        }
    }

    /// <summary>
    /// The conversation so far as the agents read it, oldest first, each message after the name
    /// of who wrote it: the last <see cref="OrchestrationHistoryMessageLimit"/> messages at most,
    /// and when those do not all fit in <see cref="OrchestrationHistoryCharacterBudget"/>
    /// characters, the newest that do. A newest message longer than the budget on its own is cut
    /// to it. Null when there is nothing to add.
    /// </summary>
    internal static string? FormatRecentConversation(IReadOnlyList<MessageEntity> messages)
    {
        var recent = messages
            .Where(message => (HasRole(message, "user") || HasRole(message, "assistant"))
                && !string.IsNullOrWhiteSpace(message.Content))
            .TakeLast(OrchestrationHistoryMessageLimit)
            .ToList();

        var kept = new List<string>();
        var length = 0;
        for (var i = recent.Count - 1; i >= 0; i--)
        {
            var speaker = HasRole(recent[i], "user") ? "User" : "Assistant";
            var entry = $"{speaker}: {recent[i].Content.Trim()}";
            if (length + entry.Length > OrchestrationHistoryCharacterBudget)
            {
                if (kept.Count == 0)
                {
                    var cut = OrchestrationHistoryCharacterBudget - 3;
                    if (char.IsHighSurrogate(entry[cut - 1]))
                    {
                        cut--;
                    }

                    kept.Add(entry[..cut] + "...");
                }

                break;
            }

            kept.Add(entry);
            length += entry.Length;
        }

        if (kept.Count == 0)
        {
            return null;
        }

        kept.Reverse();
        return "Conversation so far (oldest first):\n\n" + string.Join("\n\n", kept);
    }

    private static IReadOnlyList<AgentRole> BuildDefaultAgentRoles(ChatOrchestrationMode orchestrationMode)
    {
        return orchestrationMode == ChatOrchestrationMode.MultiAgentDebate
            ?
            [
                AgentRole.Researcher(),
                AgentRole.Critic(),
                AgentRole.Creative()
            ]
            :
            [
                AgentRole.Researcher(),
                AgentRole.Critic(),
                AgentRole.Synthesizer()
            ];
    }

    private string BuildOrchestrationFailureMessage(OrchestrationResult orchestration)
    {
        if (orchestration.Errors.Count == 0)
        {
            return ProviderStatusText.Resolve(
                _localization?.GetString("Chat_OrchestrationNoAnswerRetry"),
                "Chat_OrchestrationNoAnswerRetry",
                "Multi-agent orchestration did not return a usable answer. Check the active AI provider and try again.");
        }

        var noAnswer = ProviderStatusText.Resolve(
            _localization?.GetString("Chat_OrchestrationNoAnswer"),
            "Chat_OrchestrationNoAnswer",
            "Multi-agent orchestration did not return a usable answer.");
        return noAnswer + "\n\n" +
               string.Join("\n", orchestration.Errors.Select(error => $"- {error}"));
    }

    private static int EstimateTokenCount(string content)
    {
        var wordCount = content.Split(
            [' ', '\r', '\n', '\t'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

        return Math.Max(1, (int)Math.Ceiling(wordCount * 1.3));
    }

    private static bool HasRole(MessageEntity message, string role) =>
        string.Equals(message.Role, role, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the newest message id in the conversation before a generation writes anything.
    /// Message ids only grow, so every row above it afterwards was written by that generation.
    /// </summary>
    private async Task<long?> ReadLatestMessageIdAsync(long conversationId)
    {
        try
        {
            var messages = await _conversationService.GetMessagesAsync(conversationId);
            return messages.Count == 0 ? 0 : messages.Max(message => message.Id);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read the latest message id for conversation {ConversationId}", conversationId);
            return null;
        }
    }

    /// <summary>
    /// Finds the prompt and answer rows this generation persisted. Only rows written after the
    /// baseline count, so a stopped send that never saved its prompt cannot be matched to an
    /// earlier question, and an empty answer cannot be matched to an earlier reply.
    /// </summary>
    private async Task<(MessageEntity? User, MessageEntity? Assistant)> ResolvePersistedExchangeAsync(Exchange exchange)
    {
        if (exchange.ConversationId is not long conversationId || exchange.Baseline is not long baseline)
        {
            return (null, null);
        }

        try
        {
            var written = (await _conversationService.GetMessagesAsync(conversationId))
                .Where(message => message.Id > baseline)
                .ToList();

            return (
                written.FirstOrDefault(message => HasRole(message, "user")),
                written.LastOrDefault(message => HasRole(message, "assistant")));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to resolve the persisted messages for conversation {ConversationId}", conversationId);
            return (null, null);
        }
    }

    /// <summary>
    /// Fetches web results for Research Mode and formats them as cited context. Research Mode
    /// sends the question off the machine, so it only runs when it is switched on in Settings
    /// (which is what the privacy lamp and disclosures follow) and a provider is configured.
    /// Whenever no web sources are added, the operator is told why instead of getting an
    /// answer that silently lacks them.
    /// </summary>
    private async Task<SupplementalContext?> BuildResearchContextAsync(string query, CancellationToken ct)
    {
        if (_webSearchService is null)
        {
            NotifyResearchUnavailable(ResearchUnavailableReason.WebSearchUnavailable);
            return null;
        }

        var settings = await ReadResearchSettingsAsync();
        if (settings is not { EnableResearchMode: true })
        {
            NotifyResearchUnavailable(ResearchUnavailableReason.TurnedOff);
            return null;
        }

        if (!_webSearchService.IsConfigured)
        {
            NotifyResearchUnavailable(ResearchUnavailableReason.NoProvider);
            return null;
        }

        // The search can run, so a later configuration problem is news again.
        _researchUnavailableNotice = null;

        // Max Search Results decides how many sources an answer gets: 1 to 20, and 10 when unset.
        var resultCount = WebSearchConfiguration.FromSettings(settings).MaxResults;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ResearchSearchTimeout);

        try
        {
            var response = await _webSearchService.SearchAsync(query, resultCount, timeout.Token);
            var results = response.Results
                .Where(result => !string.IsNullOrWhiteSpace(result.Url))
                .Take(resultCount)
                .ToList();

            if (results.Count == 0)
            {
                NotifyNoWebSources(ProviderStatusText.Resolve(
                    _localization?.GetString("Chat_ResearchNoResults"),
                    "Chat_ResearchNoResults",
                    "The web search returned no results, so this answer comes from the AI model and the conversation only."));
                return null;
            }

            var citations = results
                .Select(result => new WebCitation
                {
                    Title = result.Title,
                    Url = result.Url,
                    Snippet = result.Snippet,
                    Source = WebCitationSource.Web
                })
                .ToList();

            return new SupplementalContext(FormatResearchContext(results), citations);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            NotifyNoWebSources(ProviderStatusText.Resolve(
                _localization?.GetString("Chat_ResearchTimedOut"),
                "Chat_ResearchTimedOut",
                "The web search timed out, so this answer comes from the AI model and the conversation only."));
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Research Mode web search failed");
            NotifyNoWebSources(ProviderStatusText.Resolve(
                _localization?.GetString("Chat_ResearchFailed"),
                "Chat_ResearchFailed",
                "The web search failed, so this answer comes from the AI model and the conversation only."));
            return null;
        }
    }

    internal static string FormatResearchContext(IReadOnlyList<WebSearchResult> results)
    {
        var context = new StringBuilder();
        context.AppendLine("[Web Search Results - Research Mode]");
        context.AppendLine(
            "Use these sources where they are relevant. Cite a source inline as [n] and list the URLs " +
            "of the sources you cited at the end of the answer. Say so when they do not cover the question.");

        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            context.AppendLine();
            context.AppendLine($"[{i + 1}] {result.Title}");
            context.AppendLine($"URL: {result.Url}");
            if (!string.IsNullOrWhiteSpace(result.Snippet))
            {
                context.AppendLine(result.Snippet.Trim());
            }
        }

        return context.ToString();
    }

    /// <summary>
    /// The settings Research Mode runs by, or null when they cannot be read, which keeps it off.
    /// </summary>
    private async Task<AppSettings?> ReadResearchSettingsAsync()
    {
        if (_settingsService is null)
        {
            return null;
        }

        try
        {
            return await _settingsService.GetSettingsAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read the Research Mode setting");
            return null;
        }
    }

    /// <summary>
    /// Says why Research Mode cannot run, once: the same reason is not repeated on every send.
    /// </summary>
    private void NotifyResearchUnavailable(ResearchUnavailableReason reason)
    {
        if (_researchUnavailableNotice == reason)
        {
            return;
        }

        _researchUnavailableNotice = reason;
        NotifyNoWebSources(reason switch
        {
            ResearchUnavailableReason.TurnedOff => ProviderStatusText.Resolve(
                _localization?.GetString("Chat_ResearchTurnedOff"),
                "Chat_ResearchTurnedOff",
                "Research Mode is turned off in Settings, so no web search was made. Turn it on in Settings to add web sources to answers."),
            ResearchUnavailableReason.NoProvider => ProviderStatusText.Resolve(
                _localization?.GetString("Chat_ResearchNoProvider"),
                "Chat_ResearchNoProvider",
                "No web search provider is configured. Add a Brave or Serper API key, or a SearXNG address, in Settings to add web sources to answers."),
            _ => ProviderStatusText.Resolve(
                _localization?.GetString("Chat_ResearchNoWebSearch"),
                "Chat_ResearchNoWebSearch",
                "Web search is not available in this session, so answers come from the AI model and the conversation only.")
        });
    }

    private void NotifyNoWebSources(string message) =>
        NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
        {
            Level = "info",
            Title = ProviderStatusText.Resolve(
                _localization?.GetString("Chat_NoWebSourcesTitle"),
                "Chat_NoWebSourcesTitle",
                "No web sources"),
            Message = message
        });

    /// <summary>Why Research Mode cannot run at all, until the configuration changes.</summary>
    private enum ResearchUnavailableReason
    {
        WebSearchUnavailable,
        TurnedOff,
        NoProvider
    }

    /// <inheritdoc />
    public async Task StopGenerationAsync()
    {
        Log.Debug("Stop generation requested");
        var generation = Volatile.Read(ref _generationCts);
        if (generation is null)
        {
            return;
        }

        try
        {
            await generation.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // The generation finished between the read and the cancel.
        }
    }

    /// <inheritdoc />
    public async Task SubmitFeedbackAsync(long messageId, long conversationId, string rating)
    {
        Log.Debug("Submit feedback for message {MessageId}: {Rating}", messageId, rating);

        try
        {
            await _feedbackService.SubmitFeedbackAsync(messageId, conversationId, rating,
                preferredResponse: null, note: null, category: null);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to submit feedback for message {MessageId}", messageId);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteMessageAsync(long messageId)
    {
        Log.Debug("Delete message requested: {MessageId}", messageId);

        if (messageId <= 0)
        {
            return false;
        }

        try
        {
            await _conversationService.DeleteMessageAsync(messageId);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete message {MessageId} from database", messageId);
            return false;
        }
    }

    /// <summary>
    /// What to check when no reply could be generated, for the provider that is active. The
    /// advice used to name Ollama whatever the provider was, so someone on the built-in model or
    /// a cloud provider was told to start Ollama.
    /// </summary>
    private string ActiveProviderCheckHint()
    {
        var (providerId, providerName) = ActiveProviderIdentity();
        return ProviderStatusText.CheckHint(_localization, providerId, providerName);
    }

    private string? ActiveModelIdOrNull()
    {
        var modelId = _aiService.ActiveModelId;
        return string.IsNullOrWhiteSpace(modelId) ? null : modelId;
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

    /// <summary>
    /// Streams a response directly via IAiService when no conversation context or connection is available.
    /// </summary>
    private async Task StreamDirectAsync(
        StringBuilder responseBuilder,
        string userContent,
        string? systemPrompt,
        CancellationTokenSource generation)
    {
        var ct = generation.Token;
        try
        {
            var chatMessages = new List<ChatMessage>
            {
                new() { Role = "user", Content = userContent }
            };

            await foreach (var token in _aiService.StreamChatAsync(chatMessages, systemPrompt, ct: ct))
            {
                responseBuilder.Append(token);
                RaiseToken(generation, token);
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            if (responseBuilder.Length == 0)
            {
                var (_, providerName) = ActiveProviderIdentity();
                responseBuilder.AppendLine(providerName is null
                    ? ProviderStatusText.Resolve(
                        _localization?.GetString("Chat_OfflineNoProvider"),
                        "Chat_OfflineNoProvider",
                        "Unable to generate a response: the AI provider is not available.")
                    : ProviderStatusText.Resolve(
                        _localization?.GetString("Chat_OfflineProviderUnavailable", providerName),
                        "Chat_OfflineProviderUnavailable",
                        "Unable to generate a response: {0} is not available.",
                        providerName));
                responseBuilder.AppendLine();
                responseBuilder.AppendLine(ActiveProviderCheckHint());
            }
            else
            {
                throw; // Re-throw if we had partial content
            }
        }
    }
}
