using System.Linq;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Feedback;
using Serilog;

namespace AgentX.App.ViewModels.Coordinators;

/// <summary>
/// Orchestrates conversation lifecycle operations: creation, deletion, selection,
/// pinning, folder organization, and search. Raises events that the ChatViewModel
/// subscribes to for UI synchronization.
/// </summary>
public sealed class ConversationCoordinator : IConversationCoordinator
{
    private readonly IConversationService _conversationService;
    private readonly IFeedbackService _feedbackService;

    public event EventHandler? ConversationsChanged;
    public event EventHandler? FolderNamesChanged;
    public event EventHandler<long>? ConversationSelected;

    public ConversationCoordinator(
        IConversationService conversationService,
        IFeedbackService feedbackService)
    {
        _conversationService = conversationService;
        _feedbackService = feedbackService;
    }

    /// <inheritdoc />
    public async Task<ConversationSummary?> CreateConversationAsync(
        string title, string? systemPrompt, string? modelId)
    {
        try
        {
            var conv = await _conversationService.CreateConversationAsync(
                title: title,
                systemPrompt: systemPrompt,
                modelId: modelId);

            return new ConversationSummary
            {
                Id = conv.Id,
                Title = conv.Title,
                LastMessage = string.Empty,
                UpdatedAt = DateTime.UtcNow,
                IsPinned = false,
                MessageCount = 0,
                FolderName = null
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create conversation");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task DeleteConversationAsync(long conversationId)
    {
        Log.Debug("Delete conversation requested: {ConversationId}", conversationId);

        try
        {
            await _conversationService.DeleteConversationAsync(conversationId);
            ConversationsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete conversation {ConversationId}", conversationId);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationSummary>> LoadConversationsAsync()
    {
        var result = new List<ConversationSummary>();

        try
        {
            var conversations = await _conversationService.GetAllConversationsAsync(false);
            foreach (var conv in conversations)
            {
                var lastMsg = conv.Messages?
                    .OrderByDescending(m => m.SortOrder)
                    .FirstOrDefault();

                result.Add(new ConversationSummary
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

        return result;
    }

    /// <inheritdoc />
    public async Task TogglePinAsync(long conversationId)
    {
        Log.Debug("Toggle pin: {ConversationId}", conversationId);

        try
        {
            await _conversationService.TogglePinAsync(conversationId);
            ConversationsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to toggle pin state for conversation {ConversationId}", conversationId);
        }
    }

    /// <inheritdoc />
    public async Task SetConversationFolderAsync(long conversationId, string? folder)
    {
        Log.Debug("Set conversation folder: {Folder}", folder ?? "(none)");

        try
        {
            await _conversationService.SetConversationFolderAsync(conversationId, folder);
            ConversationsChanged?.Invoke(this, EventArgs.Empty);
            FolderNamesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to set conversation folder");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationSummary>> LoadConversationsByFolderAsync(string folder)
    {
        var result = new List<ConversationSummary>();

        try
        {
            var conversations = await _conversationService.GetConversationsByFolderAsync(folder);
            foreach (var conv in conversations)
            {
                var lastMsg = conv.Messages?
                    .OrderByDescending(m => m.SortOrder)
                    .FirstOrDefault();

                result.Add(new ConversationSummary
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
            Log.Warning(ex, "Failed to filter conversations by folder");
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> LoadFolderNamesAsync()
    {
        try
        {
            var folders = await _conversationService.GetAllFolderNamesAsync();
            return folders.ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load folder names");
            return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public async Task UpdateMessageContentAsync(long messageId, string newContent)
    {
        try
        {
            await _conversationService.UpdateMessageContentAsync(messageId, newContent);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to update message content for message {MessageId}", messageId);
        }
    }

    /// <inheritdoc />
    public async Task DeleteMessagesAfterAsync(long conversationId, int sortOrder)
    {
        try
        {
            await _conversationService.DeleteMessagesAfterAsync(conversationId, sortOrder);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete messages after sort order {SortOrder} in conversation {ConversationId}",
                sortOrder, conversationId);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationSummary>> SearchConversationsAsync(string query)
    {
        var result = new List<ConversationSummary>();

        try
        {
            var conversations = await _conversationService.SearchConversationsAsync(query.Trim());
            foreach (var conv in conversations)
            {
                var lastMsg = conv.Messages?
                    .OrderByDescending(m => m.SortOrder)
                    .FirstOrDefault();

                result.Add(new ConversationSummary
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
            Log.Warning(ex, "Failed to search conversations");
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MessageSummary>> LoadMessagesAsync(long conversationId)
    {
        var messages = new List<MessageSummary>();

        try
        {
            var dbMessages = await _conversationService.GetMessagesAsync(conversationId);
            foreach (var msg in dbMessages)
            {
                var rating = "none";

                // Load existing feedback rating for assistant messages
                if (msg.Role == "assistant" && msg.Id > 0)
                {
                    try
                    {
                        var feedback = await _feedbackService.GetFeedbackForMessageAsync(msg.Id);
                        if (feedback is not null)
                        {
                            rating = feedback.Rating;
                        }
                    }
                    catch
                    {
                        // Non-critical — skip feedback loading
                    }
                }

                messages.Add(new MessageSummary
                {
                    MessageId = msg.Id,
                    ConversationId = msg.ConversationId,
                    SortOrder = msg.SortOrder,
                    Role = msg.Role,
                    Content = msg.Content,
                    Timestamp = msg.Timestamp,
                    TokenCount = msg.TokenCount,
                    GenerationTimeMs = msg.GenerationTimeMs ?? 0,
                    FeedbackRating = rating
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load messages for conversation {ConversationId}", conversationId);
        }

        return messages;
    }

    /// <summary>
    /// Notifies that a conversation has been selected (for event propagation).
    /// The ViewModel calls this after it has updated ActiveConversationId.
    /// </summary>
    public void NotifyConversationSelected(long conversationId)
    {
        ConversationSelected?.Invoke(this, conversationId);
    }

    /// <summary>
    /// Raises <see cref="ConversationsChanged"/> so the ViewModel can refresh.
    /// </summary>
    public void RaiseConversationsChanged()
    {
        ConversationsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raises <see cref="FolderNamesChanged"/> so the ViewModel can refresh.
    /// </summary>
    public void RaiseFolderNamesChanged()
    {
        FolderNamesChanged?.Invoke(this, EventArgs.Empty);
    }
}
