using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Chat;

/// <summary>
/// EF Core-backed implementation of <see cref="IConversationService"/>.
/// Manages all conversation and message persistence operations.
/// </summary>
public class ConversationService : IConversationService
{
    private readonly AgentXDbContext _db;
    private readonly IConversationRecallService? _conversationRecallService;
    private readonly IConversationSummaryService? _conversationSummaryService;
    private readonly ILogger _log;

    public ConversationService(
        AgentXDbContext db,
        ILogger logger,
        IConversationRecallService? conversationRecallService = null,
        IConversationSummaryService? conversationSummaryService = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _conversationRecallService = conversationRecallService;
        _conversationSummaryService = conversationSummaryService;
        _log = logger?.ForContext<ConversationService>()
               ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ConversationEntity> CreateConversationAsync(
        string? title = null,
        string? systemPrompt = null,
        string? modelId = null)
    {
        try
        {
            var now = DateTime.UtcNow;

            var conversation = new ConversationEntity
            {
                Title = title ?? $"New Conversation {now:yyyy-MM-dd HH:mm}",
                SystemPrompt = systemPrompt,
                ModelId = modelId ?? string.Empty,
                CreatedAt = now,
                UpdatedAt = now,
                IsPinned = false,
                IsArchived = false,
                MessageCount = 0,
                TokensUsed = 0,
            };

            _db.Conversations.Add(conversation);
            await _db.SaveChangesAsync();

            _log.Information(
                "Created conversation {ConversationId} with title '{Title}'",
                conversation.Id, conversation.Title);

            return conversation;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to create conversation");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ConversationEntity?> GetConversationAsync(long conversationId)
    {
        try
        {
            var conversation = await _db.Conversations
                .Include(c => c.Messages.OrderBy(m => m.SortOrder))
                .FirstOrDefaultAsync(c => c.Id == conversationId);

            if (conversation is null)
            {
                _log.Warning("Conversation {ConversationId} not found", conversationId);
            }

            return conversation;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get conversation {ConversationId}", conversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationEntity>> GetAllConversationsAsync(
        bool includeArchived = false)
    {
        try
        {
            var query = _db.Conversations.AsNoTracking().AsQueryable();

            if (!includeArchived)
            {
                query = query.Where(c => !c.IsArchived);
            }

            var conversations = await query
                .OrderByDescending(c => c.IsPinned)
                .ThenByDescending(c => c.UpdatedAt)
                .ToListAsync();

            _log.Debug(
                "Retrieved {Count} conversations (includeArchived={IncludeArchived})",
                conversations.Count, includeArchived);

            return conversations;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get all conversations");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationEntity>> GetRecentConversationsAsync(
        int limit = 5,
        bool includeArchived = false,
        CancellationToken ct = default)
    {
        try
        {
            var normalizedLimit = Math.Max(1, limit);
            var query = _db.Conversations.AsNoTracking().AsQueryable();

            if (!includeArchived)
            {
                query = query.Where(c => !c.IsArchived);
            }

            var conversations = await query
                .OrderByDescending(c => c.UpdatedAt)
                .Take(normalizedLimit)
                .ToListAsync(ct);

            _log.Debug(
                "Retrieved {Count} recent conversations (includeArchived={IncludeArchived}, limit={Limit})",
                conversations.Count, includeArchived, normalizedLimit);

            return conversations;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get recent conversations");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationEntity>> SearchConversationsAsync(string query)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return await GetAllConversationsAsync();
            }

            var searchPattern = $"%{query.Trim()}%";

            // Search by conversation title or message content
            var conversationIds = await _db.Messages
                .Where(m => EF.Functions.Like(m.Content, searchPattern))
                .Select(m => m.ConversationId)
                .Distinct()
                .ToListAsync();

            var conversations = await _db.Conversations
                .Where(c => EF.Functions.Like(c.Title, searchPattern)
                             || conversationIds.Contains(c.Id))
                .Where(c => !c.IsArchived)
                .OrderByDescending(c => c.UpdatedAt)
                .ToListAsync();

            _log.Debug(
                "Search for '{Query}' returned {Count} conversations",
                query, conversations.Count);

            return conversations;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to search conversations with query '{Query}'", query);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpdateConversationTitleAsync(long conversationId, string title)
    {
        try
        {
            var conversation = await _db.Conversations.FindAsync(conversationId);
            if (conversation is null)
            {
                _log.Warning(
                    "Cannot update title: conversation {ConversationId} not found",
                    conversationId);
                return;
            }

            conversation.Title = title;
            conversation.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _log.Information(
                "Updated conversation {ConversationId} title to '{Title}'",
                conversationId, title);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to update title for conversation {ConversationId}", conversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task TogglePinAsync(long conversationId)
    {
        try
        {
            var conversation = await _db.Conversations.FindAsync(conversationId);
            if (conversation is null)
            {
                _log.Warning(
                    "Cannot toggle pin: conversation {ConversationId} not found",
                    conversationId);
                return;
            }

            conversation.IsPinned = !conversation.IsPinned;
            conversation.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _log.Information(
                "Toggled pin for conversation {ConversationId} to {IsPinned}",
                conversationId, conversation.IsPinned);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to toggle pin for conversation {ConversationId}", conversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ArchiveConversationAsync(long conversationId)
    {
        try
        {
            var conversation = await _db.Conversations.FindAsync(conversationId);
            if (conversation is null)
            {
                _log.Warning(
                    "Cannot archive: conversation {ConversationId} not found",
                    conversationId);
                return;
            }

            conversation.IsArchived = true;
            conversation.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _log.Information("Archived conversation {ConversationId}", conversationId);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to archive conversation {ConversationId}", conversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteConversationAsync(long conversationId)
    {
        try
        {
            var conversation = await _db.Conversations.FindAsync(conversationId);
            if (conversation is null)
            {
                _log.Warning(
                    "Cannot delete: conversation {ConversationId} not found",
                    conversationId);
                return;
            }

            // Branches reference their parent through a Restrict foreign key, so the database
            // refuses to delete a conversation that still has branches. Each branch already
            // holds a full copy of the history it was forked from, so it is promoted into the
            // deleted conversation's place in the tree instead of being deleted with it.
            var branches = await _db.Conversations
                .Where(c => c.ParentConversationId == conversationId)
                .ToListAsync();

            await SaveStagedChangesAsync(() =>
            {
                foreach (var branch in branches)
                {
                    branch.ParentConversationId = conversation.ParentConversationId;
                    branch.BranchPointMessageId = conversation.ParentConversationId is null
                        ? null
                        : conversation.BranchPointMessageId;
                }

                _db.Conversations.Remove(conversation);
            });

            _log.Information(
                "Deleted conversation {ConversationId} (cascade deletes messages, {BranchCount} branches promoted)",
                conversationId, branches.Count);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to delete conversation {ConversationId}", conversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MessageEntity>> GetMessagesAsync(long conversationId)
    {
        try
        {
            var messages = await _db.Messages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.SortOrder)
                .ToListAsync();

            _log.Debug(
                "Retrieved {Count} messages for conversation {ConversationId}",
                messages.Count, conversationId);

            return messages;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex, "Failed to get messages for conversation {ConversationId}",
                conversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task AddMessageAsync(
        long conversationId,
        string role,
        string content,
        int? tokenCount = null,
        double? generationTimeMs = null)
    {
        try
        {
            var conversation = await _db.Conversations.FindAsync(conversationId);
            if (conversation is null)
            {
                _log.Error(
                    "Cannot add message: conversation {ConversationId} not found",
                    conversationId);
                throw new InvalidOperationException(
                    $"Conversation {conversationId} not found.");
            }

            // Determine the next sort order for this conversation
            var maxSortOrder = await _db.Messages
                .Where(m => m.ConversationId == conversationId)
                .MaxAsync(m => (int?)m.SortOrder) ?? -1;

            var message = new MessageEntity
            {
                ConversationId = conversationId,
                Role = role,
                Content = content,
                Timestamp = DateTime.UtcNow,
                TokenCount = tokenCount ?? 0,
                GenerationTimeMs = generationTimeMs,
                SortOrder = maxSortOrder + 1,
            };

            _db.Messages.Add(message);

            // Update conversation metadata
            conversation.MessageCount += 1;
            if (tokenCount.HasValue)
            {
                conversation.TokensUsed += tokenCount.Value;
            }
            conversation.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await TryRefreshMessageEmbeddingAsync(message.Id).ConfigureAwait(false);
            await TryMarkSummaryStaleAsync(conversationId);

            _log.Debug(
                "Added {Role} message (SortOrder={SortOrder}, Tokens={Tokens}) to conversation {ConversationId}",
                role, message.SortOrder, message.TokenCount, conversationId);
        }
        catch (InvalidOperationException)
        {
            // Re-throw domain exceptions without wrapping
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex, "Failed to add message to conversation {ConversationId}",
                conversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteMessageAsync(long messageId)
    {
        try
        {
            var message = await _db.Messages.FindAsync(messageId);
            if (message is null)
            {
                _log.Warning("Cannot delete: message {MessageId} not found", messageId);
                return;
            }

            var conversation = await _db.Conversations.FindAsync(message.ConversationId);

            await SaveStagedChangesAsync(() =>
            {
                if (conversation is not null)
                {
                    conversation.MessageCount = Math.Max(0, conversation.MessageCount - 1);
                    conversation.TokensUsed = Math.Max(0, conversation.TokensUsed - message.TokenCount);
                    conversation.UpdatedAt = DateTime.UtcNow;
                }

                _db.Messages.Remove(message);
            });
            await TryMarkSummaryStaleAsync(message.ConversationId, forceFullRefresh: true);

            _log.Information("Deleted message {MessageId} from conversation {ConversationId}",
                messageId, message.ConversationId);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to delete message {MessageId}", messageId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpdateMessageContentAsync(long messageId, string newContent)
    {
        try
        {
            var message = await _db.Messages.FindAsync(messageId);
            if (message is null)
            {
                _log.Warning("Cannot update: message {MessageId} not found", messageId);
                return;
            }

            message.Content = newContent;
            message.Timestamp = DateTime.UtcNow;
            message.Embedding = null;
            message.EmbeddingModel = null;
            message.EmbeddedAt = null;
            await _db.SaveChangesAsync();
            await TryRefreshMessageEmbeddingAsync(message.Id, forceRefresh: true).ConfigureAwait(false);
            await TryMarkSummaryStaleAsync(message.ConversationId, forceFullRefresh: true);

            _log.Information("Updated content of message {MessageId}", messageId);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to update message {MessageId}", messageId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> DeleteMessageAndFollowingAsync(long conversationId, long messageId)
    {
        try
        {
            var anchor = await _db.Messages
                .Where(m => m.Id == messageId && m.ConversationId == conversationId)
                .Select(m => new { m.SortOrder })
                .FirstOrDefaultAsync();

            if (anchor is null)
            {
                _log.Warning(
                    "Cannot truncate: message {MessageId} is not in conversation {ConversationId}",
                    messageId, conversationId);
                return 0;
            }

            var toDelete = await _db.Messages
                .Where(m => m.ConversationId == conversationId && m.SortOrder >= anchor.SortOrder)
                .ToListAsync();

            var conversation = await _db.Conversations.FindAsync(conversationId);

            await SaveStagedChangesAsync(() =>
            {
                if (conversation is not null)
                {
                    conversation.MessageCount = Math.Max(0, conversation.MessageCount - toDelete.Count);
                    conversation.TokensUsed = Math.Max(0, conversation.TokensUsed - toDelete.Sum(m => m.TokenCount));
                    conversation.UpdatedAt = DateTime.UtcNow;
                }

                _db.Messages.RemoveRange(toDelete);
            });
            await TryMarkSummaryStaleAsync(conversationId, forceFullRefresh: true);

            _log.Information(
                "Deleted message {MessageId} and {FollowingCount} following messages in conversation {ConversationId}",
                messageId, toDelete.Count - 1, conversationId);

            return toDelete.Count;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to delete message {MessageId} and the messages after it in conversation {ConversationId}",
                messageId, conversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> GetConversationCountAsync()
    {
        try
        {
            return await _db.Conversations
                .CountAsync(c => !c.IsArchived);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get conversation count");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<long> GetTotalTokensUsedAsync()
    {
        try
        {
            return await _db.Conversations
                .SumAsync(c => c.TokensUsed);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get total tokens used");
            throw;
        }
    }

    // ── Folder / Tag Organization ────────────────────────────────

    /// <inheritdoc />
    public async Task SetConversationFolderAsync(long conversationId, string? folderName)
    {
        try
        {
            var conversation = await _db.Conversations.FindAsync(conversationId);
            if (conversation is null)
            {
                _log.Warning(
                    "Cannot set folder: conversation {ConversationId} not found",
                    conversationId);
                return;
            }

            conversation.FolderName = string.IsNullOrWhiteSpace(folderName) ? null : folderName.Trim();
            conversation.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _log.Information(
                "Set folder for conversation {ConversationId} to '{FolderName}'",
                conversationId, conversation.FolderName ?? "(none)");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to set folder for conversation {ConversationId}", conversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetAllFolderNamesAsync()
    {
        try
        {
            var folders = await _db.Conversations
                .Where(c => c.FolderName != null && c.FolderName != string.Empty)
                .Select(c => c.FolderName!)
                .Distinct()
                .OrderBy(f => f)
                .ToListAsync();

            _log.Debug("Retrieved {Count} distinct folder names", folders.Count);
            return folders;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get folder names");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task AddTagToConversationAsync(long conversationId, long tagId)
    {
        try
        {
            var exists = await _db.ConversationTags
                .AnyAsync(ct => ct.ConversationId == conversationId && ct.TagId == tagId);

            if (exists)
            {
                _log.Debug(
                    "Tag {TagId} already assigned to conversation {ConversationId}",
                    tagId, conversationId);
                return;
            }

            var conversationTag = new Data.Entities.ConversationTagEntity
            {
                ConversationId = conversationId,
                TagId = tagId,
                AssignedAt = DateTime.UtcNow
            };

            _db.ConversationTags.Add(conversationTag);
            await _db.SaveChangesAsync();

            _log.Information(
                "Added tag {TagId} to conversation {ConversationId}",
                tagId, conversationId);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to add tag {TagId} to conversation {ConversationId}",
                tagId, conversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RemoveTagFromConversationAsync(long conversationId, long tagId)
    {
        try
        {
            var conversationTag = await _db.ConversationTags
                .FirstOrDefaultAsync(ct => ct.ConversationId == conversationId && ct.TagId == tagId);

            if (conversationTag is null)
            {
                _log.Warning(
                    "Cannot remove: tag {TagId} not assigned to conversation {ConversationId}",
                    tagId, conversationId);
                return;
            }

            _db.ConversationTags.Remove(conversationTag);
            await _db.SaveChangesAsync();

            _log.Information(
                "Removed tag {TagId} from conversation {ConversationId}",
                tagId, conversationId);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to remove tag {TagId} from conversation {ConversationId}",
                tagId, conversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationEntity>> GetConversationsByFolderAsync(string folderName)
    {
        try
        {
            var conversations = await _db.Conversations
                .Where(c => c.FolderName == folderName && !c.IsArchived)
                .OrderByDescending(c => c.IsPinned)
                .ThenByDescending(c => c.UpdatedAt)
                .ToListAsync();

            _log.Debug(
                "Retrieved {Count} conversations in folder '{FolderName}'",
                conversations.Count, folderName);

            return conversations;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to get conversations for folder '{FolderName}'", folderName);
            throw;
        }
    }

    /// <summary>
    /// Applies <paramref name="stage"/> to the tracker and saves it. The context is shared by the
    /// whole app, so a failed save must not leave its deletes and edits queued: the next save
    /// anywhere would replay the failing statements. On failure every entry the stage made
    /// pending is detached (the next read reloads it) before the exception propagates; changes
    /// that were already pending beforehand belong to other flows and are left alone.
    /// </summary>
    private async Task SaveStagedChangesAsync(Action stage)
    {
        var alreadyPending = _db.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Entity)
            .ToHashSet(ReferenceEqualityComparer.Instance);

        stage();

        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            DiscardStagedChanges(alreadyPending);
            throw;
        }
    }

    private void DiscardStagedChanges(HashSet<object> alreadyPending)
    {
        try
        {
            foreach (var entry in _db.ChangeTracker.Entries().ToList())
            {
                if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
                    !alreadyPending.Contains(entry.Entity))
                {
                    entry.State = EntityState.Detached;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to discard the changes of a failed save");
        }
    }

    private async Task TryMarkSummaryStaleAsync(long conversationId, bool forceFullRefresh = false)
    {
        if (_conversationSummaryService is null)
        {
            return;
        }

        try
        {
            await _conversationSummaryService
                .MarkConversationStaleAsync(conversationId, forceFullRefresh)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warning(
                ex,
                "Failed to update conversation summary state for conversation {ConversationId}",
                conversationId);
        }
    }

    private async Task TryRefreshMessageEmbeddingAsync(long messageId, bool forceRefresh = false)
    {
        if (_conversationRecallService is null)
        {
            return;
        }

        try
        {
            await _conversationRecallService
                .RefreshMessageEmbeddingAsync(messageId, forceRefresh)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warning(
                ex,
                "Failed to refresh message embedding for message {MessageId}",
                messageId);
        }
    }
}
