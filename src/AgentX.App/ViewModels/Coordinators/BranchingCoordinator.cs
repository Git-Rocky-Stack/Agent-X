using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Chat.Models;
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

    public event EventHandler<long>? BranchTreeChanged;
    public event EventHandler<NotificationRequestEventArgs>? NotificationRequested;

    public BranchingCoordinator(
        IConversationBranchService branchService,
        IConversationService conversationService)
    {
        _branchService = branchService;
        _conversationService = conversationService;
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
                Title = "Branch Failed",
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
                Title = "Branch Load Failed",
                Message = $"Could not load branches: {ex.Message}"
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
                Title = "Merge Complete",
                Message = "Merged insights to main thread"
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
                Title = "Merge Failed",
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
                Title = "Branch Deleted",
                Message = "The branch has been removed."
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
                Title = "Delete Failed",
                Message = ex.Message
            });
        }
    }
}
