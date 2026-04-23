using System.Diagnostics;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Chat.Models;
using AgentX.Core.Services.Feedback;
using Serilog;

namespace AgentX.App.ViewModels.Coordinators;

/// <summary>
/// Orchestrates message sending, streaming, generation control, feedback, and deletion.
/// Raises events for the ChatViewModel to synchronize UI state.
/// </summary>
public sealed class MessagingCoordinator : IMessagingCoordinator
{
    private readonly IChatService _chatService;
    private readonly IConversationService _conversationService;
    private readonly IAiService _aiService;
    private readonly IFeedbackService _feedbackService;

    private CancellationTokenSource? _generationCts;

    public event EventHandler<string>? TokenReceived;
    public event EventHandler<StreamingCompletedEventArgs>? StreamingCompleted;
    public event EventHandler<string>? GenerationError;
    public event EventHandler<NotificationRequestEventArgs>? NotificationRequested;

    public bool IsGenerating => _generationCts is not null && !_generationCts.IsCancellationRequested;

    public MessagingCoordinator(
        IChatService chatService,
        IConversationService conversationService,
        IAiService aiService,
        IFeedbackService feedbackService)
    {
        _chatService = chatService;
        _conversationService = conversationService;
        _aiService = aiService;
        _feedbackService = feedbackService;
    }

    /// <inheritdoc />
    public async Task<SendMessageResult> SendMessageAsync(
        string userContent,
        long? conversationId,
        string? systemPrompt,
        string? modelId,
        bool isResearchMode)
    {
        var result = new SendMessageResult();
        var responseBuilder = new System.Text.StringBuilder();
        int tokCount = 0;
        var sw = Stopwatch.StartNew();
        long? activeConvId = conversationId;
        string? convTitle = null;
        ChatContextInspectionSnapshot? contextInspection = null;
        long? assistantMessageId = null;

        _generationCts = new CancellationTokenSource();

        try
        {
            if (activeConvId is null)
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
                    activeConvId = newConv.Id;
                    convTitle = newConv.Title;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to create conversation, streaming without persistence");
                }
            }

            // Stream via IChatService (which persists and streams)
            if (activeConvId is not null && _aiService.ActiveProvider is not null)
            {
                var connected = false;
                try
                {
                    connected = await _aiService.ActiveProvider.CheckConnectionAsync();
                }
                catch
                {
                    // Treat as disconnected
                }

                if (connected)
                {
                    await foreach (var token in _chatService.SendMessageAsync(
                        activeConvId.Value, userContent, _generationCts.Token))
                    {
                        responseBuilder.Append(token);
                        tokCount++;
                        TokenReceived?.Invoke(this, token);
                    }

                    contextInspection = _chatService.GetLatestContextInspection(activeConvId.Value);
                    assistantMessageId = await ResolveAssistantMessageIdAsync(
                        activeConvId.Value,
                        responseBuilder.ToString());
                }
                else
                {
                    // Fallback: direct streaming without persistence
                    await StreamDirectAsync(responseBuilder, userContent, systemPrompt, _generationCts.Token);
                    if (activeConvId.HasValue)
                    {
                        contextInspection = ChatContextInspectionSnapshot.CreateLimited(
                            activeConvId.Value,
                            userContent,
                            "provider_disconnected");
                    }
                }
            }
            else
            {
                // Fallback: direct streaming without persistence
                await StreamDirectAsync(responseBuilder, userContent, systemPrompt, _generationCts.Token);
                if (activeConvId.HasValue)
                {
                    contextInspection = ChatContextInspectionSnapshot.CreateLimited(
                        activeConvId.Value,
                        userContent,
                        "no_active_provider");
                }
            }

            sw.Stop();

            var finalContent = responseBuilder.ToString();
            var completedArgs = new StreamingCompletedEventArgs
            {
                ConversationId = activeConvId,
                ResponseContent = finalContent,
                TokenCount = tokCount,
                GenerationTimeMs = sw.Elapsed.TotalMilliseconds,
                ConversationTitle = convTitle,
                ContextInspection = contextInspection,
                AssistantMessageId = assistantMessageId
            };
            StreamingCompleted?.Invoke(this, completedArgs);

            return new SendMessageResult
            {
                ConversationId = activeConvId,
                ResponseContent = finalContent,
                TokenCount = tokCount,
                GenerationTimeMs = sw.Elapsed.TotalMilliseconds,
                ConversationTitle = convTitle,
                ContextInspection = contextInspection,
                AssistantMessageId = assistantMessageId
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            var partialContent = responseBuilder.ToString() + "\n\n[Generation stopped]";
            Log.Debug("Generation cancelled by user");
            if (contextInspection is null && activeConvId.HasValue)
            {
                contextInspection = _chatService.GetLatestContextInspection(activeConvId.Value);
            }

            return new SendMessageResult
            {
                ConversationId = activeConvId,
                ResponseContent = partialContent,
                TokenCount = tokCount,
                GenerationTimeMs = sw.Elapsed.TotalMilliseconds,
                WasCancelled = true,
                ContextInspection = contextInspection
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "Error during message generation");
            var errorMsg = "An error occurred while generating a response. Please check that Ollama is running and a model is loaded.";
            GenerationError?.Invoke(this, errorMsg);
            NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
            {
                Level = "error",
                Title = "Generation Failed",
                Message = "Could not generate a response. Check your AI connection in Settings."
            });
            if (contextInspection is null && activeConvId.HasValue)
            {
                contextInspection = _chatService.GetLatestContextInspection(activeConvId.Value);
            }

            return new SendMessageResult
            {
                ConversationId = activeConvId,
                ResponseContent = errorMsg,
                TokenCount = tokCount,
                GenerationTimeMs = sw.Elapsed.TotalMilliseconds,
                HadError = true,
                ErrorMessage = errorMsg,
                ContextInspection = contextInspection
            };
        }
        finally
        {
            _generationCts?.Dispose();
            _generationCts = null;
        }
    }

    private async Task<long?> ResolveAssistantMessageIdAsync(
        long conversationId,
        string finalContent)
    {
        if (string.IsNullOrWhiteSpace(finalContent))
        {
            return null;
        }

        try
        {
            var messages = await _conversationService.GetMessagesAsync(conversationId);
            return messages
                .LastOrDefault(message => string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                ?.Id;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to resolve assistant message id for conversation {ConversationId}", conversationId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task StopGenerationAsync()
    {
        Log.Debug("Stop generation requested");
        if (_generationCts is not null)
        {
            await _generationCts.CancelAsync();
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
    public async Task DeleteMessageAsync(long messageId)
    {
        Log.Debug("Delete message requested: {MessageId}", messageId);

        if (messageId > 0)
        {
            try
            {
                await _conversationService.DeleteMessageAsync(messageId);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to delete message {MessageId} from database", messageId);
            }
        }
    }

    /// <summary>
    /// Streams a response directly via IAiService when no conversation context or connection is available.
    /// </summary>
    private async Task StreamDirectAsync(
        System.Text.StringBuilder responseBuilder,
        string userContent,
        string? systemPrompt,
        CancellationToken ct)
    {
        try
        {
            var chatMessages = new List<ChatMessage>
            {
                new() { Role = "user", Content = userContent }
            };

            await foreach (var token in _aiService.StreamChatAsync(chatMessages, systemPrompt, ct: ct))
            {
                responseBuilder.Append(token);
                TokenReceived?.Invoke(this, token);
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            if (responseBuilder.Length == 0)
            {
                responseBuilder.AppendLine("Unable to generate a response. Please ensure:");
                responseBuilder.AppendLine();
                responseBuilder.AppendLine("1. **Ollama is installed and running** on your machine");
                responseBuilder.AppendLine("2. **A model is downloaded** (use the Model Manager page)");
                responseBuilder.AppendLine("3. **The endpoint** is correct in Settings (default: http://localhost:11434)");
                responseBuilder.AppendLine();
                responseBuilder.AppendLine("Once connected, Agent-X will stream AI responses directly from your hardware.");
            }
            else
            {
                throw; // Re-throw if we had partial content
            }
        }
    }
}
