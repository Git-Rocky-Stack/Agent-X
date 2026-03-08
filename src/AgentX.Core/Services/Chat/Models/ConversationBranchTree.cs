namespace AgentX.Core.Services.Chat.Models;

using AgentX.Core.Data.Entities;

/// <summary>
/// Represents a node in a conversation branch tree. Each node holds a reference
/// to its conversation entity, the branch point metadata, and any child branches.
/// </summary>
public class ConversationBranchTree
{
    /// <summary>
    /// The conversation entity at this tree node.
    /// </summary>
    public ConversationEntity Conversation { get; set; } = null!;

    /// <summary>
    /// The message ID in the parent conversation where this branch diverges.
    /// Null for the root node of the tree.
    /// </summary>
    public long? BranchPointMessageId { get; set; }

    /// <summary>
    /// An optional label describing the purpose of this branch.
    /// Null for the root node of the tree.
    /// </summary>
    public string? BranchLabel { get; set; }

    /// <summary>
    /// Child branches forked from this conversation.
    /// </summary>
    public List<ConversationBranchTree> Children { get; set; } = new();

    /// <summary>
    /// Recursively computed total number of branches in the entire sub-tree.
    /// </summary>
    public int TotalBranchCount => Children.Count + Children.Sum(c => c.TotalBranchCount);
}
