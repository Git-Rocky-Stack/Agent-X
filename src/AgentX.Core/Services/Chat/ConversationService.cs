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
    private readonly IConversationSummaryService? _conversationSummaryService;
    private readonly ILogger _log;

    public ConversationService(
        AgentXDbContext db,
        ILogger logger,
        IConversationSummaryService? conversationSummaryService = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
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

            _db.Conversations.Remove(conversation);
            await _db.SaveChangesAsync();

            _log.Information(
                "Deleted conversation {ConversationId} (cascade deletes messages)",
                conversationId);
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
    public async Task DeleteLastAssistantMessageAsync(long conversationId)
    {
        try
        {
            var lastAssistantMessage = await _db.Messages
                .Where(m => m.ConversationId == conversationId && m.Role == "assistant")
                .OrderByDescending(m => m.SortOrder)
                .FirstOrDefaultAsync();

            if (lastAssistantMessage is null)
            {
                _log.Warning(
                    "No assistant message found to delete in conversation {ConversationId}",
                    conversationId);
                return;
            }

            var conversation = await _db.Conversations.FindAsync(conversationId);
            if (conversation is not null)
            {
                conversation.MessageCount = Math.Max(0, conversation.MessageCount - 1);
                conversation.TokensUsed = Math.Max(0, conversation.TokensUsed - lastAssistantMessage.TokenCount);
                conversation.UpdatedAt = DateTime.UtcNow;
            }

            _db.Messages.Remove(lastAssistantMessage);
            await _db.SaveChangesAsync();
            await TryMarkSummaryStaleAsync(conversationId, forceFullRefresh: true);

            _log.Information(
                "Deleted last assistant message (Id={MessageId}) from conversation {ConversationId}",
                lastAssistantMessage.Id, conversationId);
        }
        catch (Exception ex)
        {
            _log.Error(
                ex, "Failed to delete last assistant message from conversation {ConversationId}",
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
            if (conversation is not null)
            {
                conversation.MessageCount = Math.Max(0, conversation.MessageCount - 1);
                conversation.TokensUsed = Math.Max(0, conversation.TokensUsed - message.TokenCount);
                conversation.UpdatedAt = DateTime.UtcNow;
            }

            _db.Messages.Remove(message);
            await _db.SaveChangesAsync();
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
            await _db.SaveChangesAsync();
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
    public async Task DeleteMessagesAfterAsync(long conversationId, int sortOrder)
    {
        try
        {
            var toDelete = await _db.Messages
                .Where(m => m.ConversationId == conversationId && m.SortOrder > sortOrder)
                .ToListAsync();

            if (toDelete.Count == 0) return;

            var conversation = await _db.Conversations.FindAsync(conversationId);
            if (conversation is not null)
            {
                var tokenSum = toDelete.Sum(m => m.TokenCount);
                conversation.MessageCount = Math.Max(0, conversation.MessageCount - toDelete.Count);
                conversation.TokensUsed = Math.Max(0, conversation.TokensUsed - tokenSum);
                conversation.UpdatedAt = DateTime.UtcNow;
            }

            _db.Messages.RemoveRange(toDelete);
            await _db.SaveChangesAsync();
            await TryMarkSummaryStaleAsync(conversationId, forceFullRefresh: true);

            _log.Information(
                "Deleted {Count} messages after SortOrder {SortOrder} in conversation {ConversationId}",
                toDelete.Count, sortOrder, conversationId);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to delete messages after SortOrder {SortOrder} in conversation {ConversationId}",
                sortOrder, conversationId);
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
}
