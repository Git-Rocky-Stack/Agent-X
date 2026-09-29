using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Chat.Models;
using AgentX.Core.Services.Localization;
using Serilog;

namespace AgentX.App.ViewModels.Coordinators;

/// <summary>
/// Orchestrates conversation branching operations: forking at messages, loading
/// branch trees, merging insights between branches, and deleting branches.
/// Raises events that the ChatViewModel subscribes to for UI synchronization.
/// </summary>
public sealed class BranchingCoordinator : IBranchingCoordinator
{
    private readonly IConversationBranchService _branchService;
    private readonly IConversationService _conversationService;
    private readonly ILocalizationService _localization;

    public event EventHandler<long>? BranchTreeChanged;
    public event EventHandler<NotificationRequestEventArgs>? NotificationRequested;

    public BranchingCoordinator(
        IConversationBranchService branchService,
        IConversationService conversationService,
        ILocalizationService localization)
    {
        _branchService = branchService;
        _conversationService = conversationService;
        _localization = localization;
    }

    /// <inheritdoc />
    public async Task<BranchResult?> BranchFromMessageAsync(
        long conversationId, long messageId, string? label)
    {
        Log.Debug("Branch from message {MessageId} in conversation {ConversationId}", messageId, conversationId);

        try
        {
            var branch = await _branchService.BranchAtMessageAsync(
                conversationId, messageId, label);

            BranchTreeChanged?.Invoke(this, conversationId);

            return new BranchResult
            {
                BranchConversationId = branch.Id,
                Title = branch.Title
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create branch from message {MessageId}", messageId);
            NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
            {
                Level = "error",
                Title = _localization.GetString("Chat_BranchFailedTitle"),
                Message = ex.Message
            });
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<ConversationBranchTree?> LoadBranchTreeAsync(long conversationId)
    {
        try
        {
            var tree = await _branchService.GetBranchTreeAsync(conversationId);
            return tree;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load branch tree for conversation {ConversationId}", conversationId);
            NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
            {
                Level = "error",
                Title = _localization.GetString("Chat_BranchLoadFailedTitle"),
                Message = _localization.GetString("Chat_BranchLoadFailedBody", ex.Message)
            });
            return null;
        }
    }

    /// <inheritdoc />
    public async Task MergeToMainAsync(MergeBranchRequest request)
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

            NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
            {
                Level = "info",
                Title = _localization.GetString("Chat_MergeCompleteTitle"),
                Message = _localization.GetString("Chat_MergeCompleteBody")
            });

            BranchTreeChanged?.Invoke(this, request.TargetConversationId);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to merge messages from {SourceId} to {TargetId}",
                request.SourceConversationId, request.TargetConversationId);
            NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
            {
                Level = "error",
                Title = _localization.GetString("Chat_MergeFailedTitle"),
                Message = ex.Message
            });
        }
    }

    /// <inheritdoc />
    public async Task DeleteBranchAsync(long branchConversationId)
    {
        try
        {
            await _branchService.DeleteBranchAsync(branchConversationId);

            NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
            {
                Level = "info",
                Title = _localization.GetString("Chat_BranchDeletedTitle"),
                Message = _localization.GetString("Chat_BranchDeletedBody")
            });

            // Note: we don't know the root conversation ID here, so the ViewModel
            // should reload the branch tree for the active conversation after this event.
            BranchTreeChanged?.Invoke(this, branchConversationId);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete branch {BranchId}", branchConversationId);
            NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
            {
                Level = "error",
                Title = _localization.GetString("Chat_BranchDeleteFailedTitle"),
                Message = ex.Message
            });
        }
    }
}
