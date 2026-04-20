using AgentX.Core.Services.Chat.Models;

namespace AgentX.App.ViewModels.Coordinators;

/// <summary>
/// Coordinates conversation branching operations: creating forks, loading branch trees,
/// switching between branches, merging insights, and deleting branches.
/// The coordinator owns the business logic; the ChatViewModel subscribes to events
/// for UI state synchronization.
/// </summary>
public interface IBranchingCoordinator
{
    /// <summary>
    /// Raised when the branch tree has changed (branch created, deleted, or merged).
    /// The event argument is the conversation ID whose tree changed.
    /// </summary>
    event EventHandler<long>? BranchTreeChanged;

    /// <summary>
    /// Raised when a notification should be shown to the user.
    /// </summary>
    event EventHandler<NotificationRequestEventArgs>? NotificationRequested;

    /// <summary>
    /// Creates a branch from the specified message in the given conversation.
    /// </summary>
    /// <param name="conversationId">The source conversation to branch from.</param>
    /// <param name="messageId">The message ID at which to fork.</param>
    /// <param name="label">Optional label for the branch.</param>
    /// <returns>A <see cref="BranchResult"/> describing the newly created branch, or null on failure.</returns>
    Task<BranchResult?> BranchFromMessageAsync(long conversationId, long messageId, string? label);

    /// <summary>
    /// Loads the full branch tree for a conversation.
    /// </summary>
    /// <param name="conversationId">The root conversation ID.</param>
    /// <returns>The branch tree, or null if no branches exist or on error.</returns>
    Task<ConversationBranchTree?> LoadBranchTreeAsync(long conversationId);

    /// <summary>
    /// Merges messages from a source branch into a target conversation.
    /// </summary>
    /// <param name="request">The merge request details.</param>
    Task MergeToMainAsync(MergeBranchRequest request);

    /// <summary>
    /// Deletes a branch conversation.
    /// </summary>
    /// <param name="branchConversationId">The branch conversation to delete.</param>
    Task DeleteBranchAsync(long branchConversationId);
}

/// <summary>
/// Result of a branch-from-message operation.
/// </summary>
public sealed class BranchResult
{
    /// <summary>The newly created branch conversation ID.</summary>
    public long BranchConversationId { get; init; }

    /// <summary>The title/label of the new branch.</summary>
    public string Title { get; init; } = string.Empty;
}

/// <summary>
/// Parameter object for the merge-branch command, since [RelayCommand]
/// only supports a single parameter.
/// </summary>
public record MergeBranchRequest(
    long SourceConversationId,
    long TargetConversationId,
    IReadOnlyList<long>? MessageIds = null);
