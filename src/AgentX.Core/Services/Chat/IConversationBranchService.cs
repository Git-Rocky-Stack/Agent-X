namespace AgentX.Core.Services.Chat;

using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Chat.Models;

/// <summary>
/// Manages conversation branching operations: creating forks at specific messages,
/// querying branch trees, and merging content between branches.
/// </summary>
public interface IConversationBranchService
{
    /// <summary>
    /// Creates a new branch from an existing conversation, forking at the specified message.
    /// All messages up to and including the branch point are copied to the new conversation.
    /// </summary>
    /// <param name="conversationId">The source conversation to branch from.</param>
    /// <param name="messageId">The message ID at which to fork. All messages up to and including this message are copied.</param>
    /// <param name="branchLabel">An optional label describing the purpose of this branch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly created branch conversation with its copied messages.</returns>
    Task<ConversationEntity> BranchAtMessageAsync(
        long conversationId,
        long messageId,
        string? branchLabel = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all branches (direct children) of a conversation.
    /// </summary>
    /// <param name="conversationId">The parent conversation ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of child branch conversations.</returns>
    Task<IReadOnlyList<ConversationEntity>> GetBranchesAsync(
        long conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the full branch tree for a root conversation (recursive).
    /// If the provided conversation is itself a branch, the tree is rooted at
    /// the ultimate root conversation.
    /// </summary>
    /// <param name="rootConversationId">The root conversation ID to build the tree from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A recursive tree structure of all branches.</returns>
    Task<ConversationBranchTree> GetBranchTreeAsync(
        long rootConversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Finds the root conversation for any conversation in a branch tree.
    /// Walks up the <see cref="ConversationEntity.ParentConversationId"/> chain until null.
    /// </summary>
    /// <param name="conversationId">Any conversation ID in a branch tree.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The root conversation entity (the one with no parent).</returns>
    Task<ConversationEntity> GetRootConversationAsync(
        long conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Copies specific messages from one branch into another conversation.
    /// Used for "merge insights" from a branch back into the main thread.
    /// Messages are appended after the last existing message in the target conversation.
    /// </summary>
    /// <param name="sourceConversationId">The conversation to copy messages from.</param>
    /// <param name="messageIds">The IDs of messages to copy.</param>
    /// <param name="targetConversationId">The conversation to copy messages into.</param>
    /// <param name="ct">Cancellation token.</param>
    Task MergeMessagesAsync(
        long sourceConversationId,
        IReadOnlyList<long> messageIds,
        long targetConversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the count of branches for a conversation (direct children only).
    /// </summary>
    /// <param name="conversationId">The parent conversation ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of direct child branches.</returns>
    Task<int> GetBranchCountAsync(
        long conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Checks whether a specific message has any branches diverging from it.
    /// </summary>
    /// <param name="messageId">The message ID to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if at least one branch uses this message as its branch point.</returns>
    Task<bool> HasBranchesAtMessageAsync(
        long messageId,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a branch and optionally all its sub-branches recursively.
    /// The branch must not be a root conversation (must have a parent).
    /// </summary>
    /// <param name="branchConversationId">The branch conversation to delete.</param>
    /// <param name="recursive">When true, all sub-branches are deleted as well. Defaults to true.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteBranchAsync(
        long branchConversationId,
        bool recursive = true,
        CancellationToken ct = default);
}
