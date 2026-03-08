using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Chat.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Chat;

/// <summary>
/// EF Core-backed implementation of <see cref="IConversationBranchService"/>.
/// Manages all conversation branching operations including forking, tree queries,
/// message merging, and recursive branch deletion.
/// </summary>
public class ConversationBranchService : IConversationBranchService
{
    private readonly AgentXDbContext _db;
    private readonly IConversationService _conversationService;
    private readonly ILogger _log;

    public ConversationBranchService(
        AgentXDbContext db,
        IConversationService conversationService,
        ILogger logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _conversationService = conversationService
                               ?? throw new ArgumentNullException(nameof(conversationService));
        _log = logger?.ForContext<ConversationBranchService>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ConversationEntity> BranchAtMessageAsync(
        long conversationId,
        long messageId,
        string? branchLabel = null,
        CancellationToken ct = default)
    {
        try
        {
            // Load the source conversation with all messages ordered by SortOrder
            var sourceConversation = await _db.Conversations
                .Include(c => c.Messages.OrderBy(m => m.SortOrder))
                .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

            if (sourceConversation is null)
            {
                _log.Error(
                    "Cannot branch: source conversation {ConversationId} not found",
                    conversationId);
                throw new InvalidOperationException(
                    $"Source conversation {conversationId} not found.");
            }

            // Validate the branch point message exists and belongs to this conversation
            var branchPointMessage = sourceConversation.Messages
                .FirstOrDefault(m => m.Id == messageId);

            if (branchPointMessage is null)
            {
                _log.Error(
                    "Cannot branch: message {MessageId} not found in conversation {ConversationId}",
                    messageId, conversationId);
                throw new InvalidOperationException(
                    $"Message {messageId} not found in conversation {conversationId}.");
            }

            var now = DateTime.UtcNow;

            // Determine the branch title
            var branchTitle = !string.IsNullOrWhiteSpace(branchLabel)
                ? branchLabel
                : $"{sourceConversation.Title} (Branch)";

            // Collect messages up to and including the branch point (by SortOrder)
            var messagesToCopy = sourceConversation.Messages
                .Where(m => m.SortOrder <= branchPointMessage.SortOrder)
                .OrderBy(m => m.SortOrder)
                .ToList();

            // Calculate aggregates for the copied messages
            var copiedMessageCount = messagesToCopy.Count;
            var copiedTokensUsed = messagesToCopy.Sum(m => (long)m.TokenCount);

            // Create the new branch conversation
            var branchConversation = new ConversationEntity
            {
                Title = branchTitle,
                SystemPrompt = sourceConversation.SystemPrompt,
                ModelId = sourceConversation.ModelId,
                CreatedAt = now,
                UpdatedAt = now,
                IsPinned = false,
                IsArchived = false,
                MessageCount = copiedMessageCount,
                TokensUsed = copiedTokensUsed,
                ParentConversationId = conversationId,
                BranchPointMessageId = messageId,
                BranchLabel = branchLabel,
            };

            _db.Conversations.Add(branchConversation);

            // Save first to get the branchConversation.Id assigned
            await _db.SaveChangesAsync(ct);

            // Create new MessageEntity instances for each copied message
            var sortOrder = 0;
            foreach (var sourceMessage in messagesToCopy)
            {
                var copiedMessage = new MessageEntity
                {
                    ConversationId = branchConversation.Id,
                    Role = sourceMessage.Role,
                    Content = sourceMessage.Content,
                    Timestamp = sourceMessage.Timestamp,
                    TokenCount = sourceMessage.TokenCount,
                    GenerationTimeMs = sourceMessage.GenerationTimeMs,
                    ModelId = sourceMessage.ModelId,
                    CitationsJson = sourceMessage.CitationsJson,
                    SortOrder = sortOrder,
                };

                _db.Messages.Add(copiedMessage);
                sortOrder++;
            }

            await _db.SaveChangesAsync(ct);

            _log.Information(
                "Created branch {BranchId} from conversation {ConversationId} at message {MessageId} " +
                "with {MessageCount} copied messages (label: '{BranchLabel}')",
                branchConversation.Id, conversationId, messageId,
                copiedMessageCount, branchLabel ?? "(none)");

            // Reload with messages to return a complete entity
            var result = await _db.Conversations
                .Include(c => c.Messages.OrderBy(m => m.SortOrder))
                .FirstAsync(c => c.Id == branchConversation.Id, ct);

            return result;
        }
        catch (InvalidOperationException)
        {
            // Re-throw domain exceptions without wrapping
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex, "Failed to branch conversation {ConversationId} at message {MessageId}",
                conversationId, messageId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationEntity>> GetBranchesAsync(
        long conversationId,
        CancellationToken ct = default)
    {
        try
        {
            var branches = await _db.Conversations
                .Where(c => c.ParentConversationId == conversationId)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync(ct);

            _log.Debug(
                "Retrieved {Count} branches for conversation {ConversationId}",
                branches.Count, conversationId);

            return branches;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex, "Failed to get branches for conversation {ConversationId}",
                conversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ConversationBranchTree> GetBranchTreeAsync(
        long rootConversationId,
        CancellationToken ct = default)
    {
        try
        {
            var rootConversation = await _db.Conversations
                .FirstOrDefaultAsync(c => c.Id == rootConversationId, ct);

            if (rootConversation is null)
            {
                _log.Error(
                    "Cannot build branch tree: conversation {ConversationId} not found",
                    rootConversationId);
                throw new InvalidOperationException(
                    $"Conversation {rootConversationId} not found.");
            }

            // Load all conversations that belong to this tree.
            // First, find the true root if the given conversation is itself a branch.
            var trueRoot = rootConversation;
            if (trueRoot.ParentConversationId is not null)
            {
                trueRoot = await GetRootConversationInternalAsync(rootConversationId, ct);
            }

            // Load all conversations that could be part of this tree by walking descendants.
            // We load all conversations with a breadth-first approach to avoid N+1 queries.
            var allConversations = await LoadEntireTreeAsync(trueRoot.Id, ct);

            // Build the tree recursively from the loaded data
            var tree = BuildTreeNode(trueRoot, allConversations);

            _log.Debug(
                "Built branch tree for conversation {ConversationId} with {TotalBranches} total branches",
                trueRoot.Id, tree.TotalBranchCount);

            return tree;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex, "Failed to build branch tree for conversation {ConversationId}",
                rootConversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ConversationEntity> GetRootConversationAsync(
        long conversationId,
        CancellationToken ct = default)
    {
        try
        {
            return await GetRootConversationInternalAsync(conversationId, ct);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex, "Failed to get root conversation for {ConversationId}",
                conversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task MergeMessagesAsync(
        long sourceConversationId,
        IReadOnlyList<long> messageIds,
        long targetConversationId,
        CancellationToken ct = default)
    {
        try
        {
            if (messageIds is null || messageIds.Count == 0)
            {
                _log.Warning("MergeMessagesAsync called with no message IDs; nothing to merge");
                return;
            }

            // Validate source conversation exists
            var sourceConversation = await _db.Conversations
                .FirstOrDefaultAsync(c => c.Id == sourceConversationId, ct);

            if (sourceConversation is null)
            {
                _log.Error(
                    "Cannot merge: source conversation {ConversationId} not found",
                    sourceConversationId);
                throw new InvalidOperationException(
                    $"Source conversation {sourceConversationId} not found.");
            }

            // Validate target conversation exists
            var targetConversation = await _db.Conversations
                .FirstOrDefaultAsync(c => c.Id == targetConversationId, ct);

            if (targetConversation is null)
            {
                _log.Error(
                    "Cannot merge: target conversation {ConversationId} not found",
                    targetConversationId);
                throw new InvalidOperationException(
                    $"Target conversation {targetConversationId} not found.");
            }

            // Load the source messages that match the requested IDs
            var sourceMessages = await _db.Messages
                .Where(m => m.ConversationId == sourceConversationId
                            && messageIds.Contains(m.Id))
                .OrderBy(m => m.SortOrder)
                .ToListAsync(ct);

            if (sourceMessages.Count == 0)
            {
                _log.Warning(
                    "No matching messages found in conversation {SourceConversationId} for the provided IDs",
                    sourceConversationId);
                return;
            }

            if (sourceMessages.Count != messageIds.Count)
            {
                var foundIds = sourceMessages.Select(m => m.Id).ToHashSet();
                var missingIds = messageIds.Where(id => !foundIds.Contains(id)).ToList();
                _log.Warning(
                    "Some message IDs were not found in source conversation {SourceConversationId}: {MissingIds}",
                    sourceConversationId, missingIds);
            }

            // Determine the next sort order in the target conversation
            var maxSortOrder = await _db.Messages
                .Where(m => m.ConversationId == targetConversationId)
                .MaxAsync(m => (int?)m.SortOrder, ct) ?? -1;

            var currentSortOrder = maxSortOrder + 1;
            var totalTokensCopied = 0L;

            foreach (var sourceMessage in sourceMessages)
            {
                var copiedMessage = new MessageEntity
                {
                    ConversationId = targetConversationId,
                    Role = sourceMessage.Role,
                    Content = sourceMessage.Content,
                    Timestamp = DateTime.UtcNow,
                    TokenCount = sourceMessage.TokenCount,
                    GenerationTimeMs = sourceMessage.GenerationTimeMs,
                    ModelId = sourceMessage.ModelId,
                    CitationsJson = sourceMessage.CitationsJson,
                    SortOrder = currentSortOrder,
                };

                _db.Messages.Add(copiedMessage);
                currentSortOrder++;
                totalTokensCopied += sourceMessage.TokenCount;
            }

            // Update target conversation metadata
            targetConversation.MessageCount += sourceMessages.Count;
            targetConversation.TokensUsed += totalTokensCopied;
            targetConversation.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            _log.Information(
                "Merged {Count} messages from conversation {SourceId} into conversation {TargetId}",
                sourceMessages.Count, sourceConversationId, targetConversationId);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex, "Failed to merge messages from conversation {SourceId} to {TargetId}",
                sourceConversationId, targetConversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> GetBranchCountAsync(
        long conversationId,
        CancellationToken ct = default)
    {
        try
        {
            var count = await _db.Conversations
                .CountAsync(c => c.ParentConversationId == conversationId, ct);

            _log.Debug(
                "Conversation {ConversationId} has {BranchCount} direct branches",
                conversationId, count);

            return count;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex, "Failed to get branch count for conversation {ConversationId}",
                conversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> HasBranchesAtMessageAsync(
        long messageId,
        CancellationToken ct = default)
    {
        try
        {
            var hasBranches = await _db.Conversations
                .AnyAsync(c => c.BranchPointMessageId == messageId, ct);

            _log.Debug(
                "Message {MessageId} has branches: {HasBranches}",
                messageId, hasBranches);

            return hasBranches;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex, "Failed to check for branches at message {MessageId}",
                messageId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteBranchAsync(
        long branchConversationId,
        bool recursive = true,
        CancellationToken ct = default)
    {
        try
        {
            var branch = await _db.Conversations
                .FirstOrDefaultAsync(c => c.Id == branchConversationId, ct);

            if (branch is null)
            {
                _log.Warning(
                    "Cannot delete branch: conversation {ConversationId} not found",
                    branchConversationId);
                return;
            }

            if (branch.ParentConversationId is null)
            {
                _log.Error(
                    "Cannot delete branch: conversation {ConversationId} is a root conversation, not a branch",
                    branchConversationId);
                throw new InvalidOperationException(
                    $"Conversation {branchConversationId} is a root conversation and cannot be deleted as a branch. " +
                    "Use DeleteConversationAsync instead.");
            }

            if (recursive)
            {
                // Collect all descendant branch IDs in depth-first order
                var branchIdsToDelete = new List<long>();
                await CollectDescendantIdsAsync(branchConversationId, branchIdsToDelete, ct);

                // Delete in reverse order (deepest descendants first) to respect FK constraints
                branchIdsToDelete.Reverse();

                foreach (var descendantId in branchIdsToDelete)
                {
                    var descendant = await _db.Conversations.FindAsync(
                        new object[] { descendantId }, ct);
                    if (descendant is not null)
                    {
                        _db.Conversations.Remove(descendant);
                    }
                }

                _log.Information(
                    "Recursively deleting branch {BranchId} and {DescendantCount} sub-branches",
                    branchConversationId, branchIdsToDelete.Count);
            }

            // Delete the branch itself
            _db.Conversations.Remove(branch);
            await _db.SaveChangesAsync(ct);

            _log.Information(
                "Deleted branch {BranchId} (recursive={Recursive}, cascade deletes messages)",
                branchConversationId, recursive);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex, "Failed to delete branch {BranchConversationId}",
                branchConversationId);
            throw;
        }
    }

    // ────────────────────────────────────────────────────────────────
    // Private helpers
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks up the ParentConversationId chain to find the root conversation.
    /// Includes a safety limit to prevent infinite loops from data corruption.
    /// </summary>
    private async Task<ConversationEntity> GetRootConversationInternalAsync(
        long conversationId,
        CancellationToken ct)
    {
        var current = await _db.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

        if (current is null)
        {
            _log.Error(
                "Cannot find root: conversation {ConversationId} not found",
                conversationId);
            throw new InvalidOperationException(
                $"Conversation {conversationId} not found.");
        }

        // Safety limit to prevent infinite loops from circular references
        const int maxDepth = 100;
        var depth = 0;

        while (current.ParentConversationId is not null)
        {
            depth++;
            if (depth > maxDepth)
            {
                _log.Error(
                    "Circular reference detected while walking parent chain from conversation {ConversationId}",
                    conversationId);
                throw new InvalidOperationException(
                    $"Maximum branch depth ({maxDepth}) exceeded. Possible circular reference in conversation tree.");
            }

            var parent = await _db.Conversations
                .FirstOrDefaultAsync(c => c.Id == current.ParentConversationId, ct);

            if (parent is null)
            {
                _log.Warning(
                    "Parent conversation {ParentId} not found for conversation {ConversationId}; " +
                    "treating current conversation as root",
                    current.ParentConversationId, current.Id);
                break;
            }

            current = parent;
        }

        _log.Debug(
            "Root conversation for {ConversationId} is {RootId} (depth={Depth})",
            conversationId, current.Id, depth);

        return current;
    }

    /// <summary>
    /// Loads all conversations belonging to a branch tree starting from the given root.
    /// Uses a breadth-first approach to minimize database round-trips.
    /// </summary>
    private async Task<List<ConversationEntity>> LoadEntireTreeAsync(
        long rootId,
        CancellationToken ct)
    {
        var allConversations = new List<ConversationEntity>();

        // Load the root
        var root = await _db.Conversations
            .FirstOrDefaultAsync(c => c.Id == rootId, ct);

        if (root is null)
        {
            return allConversations;
        }

        allConversations.Add(root);

        // Breadth-first loading of all descendants
        var currentLevelIds = new List<long> { rootId };

        const int maxIterations = 100;
        var iteration = 0;

        while (currentLevelIds.Count > 0)
        {
            iteration++;
            if (iteration > maxIterations)
            {
                _log.Warning(
                    "Branch tree loading exceeded {MaxIterations} levels; stopping to prevent runaway queries",
                    maxIterations);
                break;
            }

            var children = await _db.Conversations
                .Where(c => c.ParentConversationId != null
                            && currentLevelIds.Contains(c.ParentConversationId.Value))
                .ToListAsync(ct);

            if (children.Count == 0)
            {
                break;
            }

            allConversations.AddRange(children);
            currentLevelIds = children.Select(c => c.Id).ToList();
        }

        return allConversations;
    }

    /// <summary>
    /// Recursively builds a <see cref="ConversationBranchTree"/> node from in-memory data.
    /// </summary>
    private static ConversationBranchTree BuildTreeNode(
        ConversationEntity conversation,
        List<ConversationEntity> allConversations)
    {
        var node = new ConversationBranchTree
        {
            Conversation = conversation,
            BranchPointMessageId = conversation.BranchPointMessageId,
            BranchLabel = conversation.BranchLabel,
        };

        // Find direct children of this conversation
        var children = allConversations
            .Where(c => c.ParentConversationId == conversation.Id)
            .OrderByDescending(c => c.CreatedAt)
            .ToList();

        foreach (var child in children)
        {
            node.Children.Add(BuildTreeNode(child, allConversations));
        }

        return node;
    }

    /// <summary>
    /// Recursively collects all descendant conversation IDs for a given parent.
    /// </summary>
    private async Task CollectDescendantIdsAsync(
        long parentId,
        List<long> collectedIds,
        CancellationToken ct)
    {
        var childIds = await _db.Conversations
            .Where(c => c.ParentConversationId == parentId)
            .Select(c => c.Id)
            .ToListAsync(ct);

        foreach (var childId in childIds)
        {
            collectedIds.Add(childId);
            await CollectDescendantIdsAsync(childId, collectedIds, ct);
        }
    }
}
