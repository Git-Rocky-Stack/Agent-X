using System.Text;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Chat;

/// <summary>
/// Extracts, stores, and retrieves conversation memories using AI-driven
/// analysis. Memories are injected into system prompts to personalize
/// future interactions and generate contextual follow-up suggestions.
/// </summary>
public sealed class ConversationMemoryService : IConversationMemoryService
{
    private readonly AgentXDbContext _db;
    private readonly IAiService _aiService;
    private readonly ILogger _logger;

    public ConversationMemoryService(AgentXDbContext db, IAiService aiService, ILogger logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _logger = logger?.ForContext<ConversationMemoryService>()
                  ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task ExtractMemoriesAsync(long conversationId, CancellationToken ct = default)
    {
        try
        {
            // Get the last few messages from this conversation
            var messages = await _db.Messages
                .Where(m => m.ConversationId == conversationId)
                .OrderByDescending(m => m.SortOrder)
                .Take(10)
                .ToListAsync(ct);

            if (messages.Count < 2) return;

            // Build a summary of recent messages for AI extraction
            var recentContent = string.Join("\n", messages
                .OrderBy(m => m.SortOrder)
                .Select(m => $"{m.Role}: {m.Content}"));

            // Truncate to prevent token overflow
            if (recentContent.Length > 3000)
                recentContent = recentContent[..3000];

            // Ask AI to extract memorable facts
            var extractionPrompt = @"Extract key facts, user preferences, and important context from this conversation that would be useful to remember for future conversations.

Return each memory on a separate line in the format:
category|content

Categories: preference, fact, topic, instruction
Only extract genuinely useful information. Skip greetings and small talk.
If there are no memorable facts, respond with NONE.

Conversation:
" + recentContent;

            var response = await _aiService.ChatAsync(
                new List<ChatMessage> { new() { Role = "user", Content = extractionPrompt, Timestamp = DateTime.UtcNow } },
                systemPrompt: "You are a memory extraction assistant. Extract concise, useful facts from conversations. Be selective - only extract information worth remembering.",
                options: new ChatOptions { Temperature = 0.2, MaxTokens = 500 },
                ct: ct);

            if (string.IsNullOrWhiteSpace(response) || response.Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase))
                return;

            // Parse extracted memories
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('|', 2);
                var category = parts.Length > 1 ? parts[0].Trim().ToLower() : "fact";
                var content = parts.Length > 1 ? parts[1].Trim() : line.Trim();

                if (string.IsNullOrWhiteSpace(content) || content.Length < 5) continue;

                // Validate category
                if (category is not ("preference" or "fact" or "topic" or "instruction"))
                    category = "fact";

                // Check for duplicate/similar memories using a substring match
                var searchSubstring = content.ToLower().Substring(0, Math.Min(content.Length, 30));
                var existing = await _db.Memories
                    .Where(m => m.IsActive && m.Content.ToLower().Contains(searchSubstring))
                    .FirstOrDefaultAsync(ct);

                if (existing != null)
                {
                    // Update existing memory's importance and timestamp
                    existing.Importance = Math.Min(1.0, existing.Importance + 0.1);
                    existing.LastUsedAt = DateTime.UtcNow;
                    continue;
                }

                _db.Memories.Add(new MemoryEntity
                {
                    Content = content,
                    Category = category,
                    SourceConversationId = conversationId,
                    Importance = category == "preference" ? 0.8 : category == "instruction" ? 0.9 : 0.5
                });
            }

            await _db.SaveChangesAsync(ct);
            _logger.Debug("Extracted memories from conversation {ConversationId}", conversationId);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to extract memories from conversation {ConversationId}", conversationId);
        }
    }

    /// <inheritdoc />
    public async Task<string> GetMemoryContextAsync(int maxMemories = 10, CancellationToken ct = default)
    {
        var memories = await _db.Memories
            .Where(m => m.IsActive)
            .OrderByDescending(m => m.Importance)
            .ThenByDescending(m => m.LastUsedAt)
            .Take(maxMemories)
            .ToListAsync(ct);

        if (memories.Count == 0) return string.Empty;

        // Update usage counts
        foreach (var m in memories)
        {
            m.UsageCount++;
            m.LastUsedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);

        // Build context block
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("[User Memory Context - Use these to personalize responses]");
        foreach (var m in memories)
        {
            var label = m.Category switch
            {
                "preference" => "Preference",
                "instruction" => "Instruction",
                "topic" => "Topic of Interest",
                _ => "Known Fact"
            };
            sb.AppendLine($"- {label}: {m.Content}");
        }
        return sb.ToString();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetSuggestedQuestionsAsync(long conversationId, CancellationToken ct = default)
    {
        try
        {
            // Get the last few messages for context
            var lastMessages = await _db.Messages
                .Where(m => m.ConversationId == conversationId)
                .OrderByDescending(m => m.SortOrder)
                .Take(4)
                .ToListAsync(ct);

            if (lastMessages.Count == 0) return [];

            var context = string.Join("\n", lastMessages
                .OrderBy(m => m.SortOrder)
                .Select(m => $"{m.Role}: {(m.Content.Length > 500 ? m.Content[..500] : m.Content)}"));

            // Get memory context for personalization
            var memories = await _db.Memories
                .Where(m => m.IsActive)
                .OrderByDescending(m => m.Importance)
                .Take(5)
                .Select(m => m.Content)
                .ToListAsync(ct);

            var memoryContext = memories.Count > 0
                ? "\nUser interests: " + string.Join(", ", memories)
                : "";

            var prompt = $"Based on this conversation and the user's known interests, suggest 3 natural follow-up questions the user might want to ask. Return ONLY the questions, one per line, no numbering.{memoryContext}\n\nConversation:\n{context}";

            var response = await _aiService.ChatAsync(
                new List<ChatMessage> { new() { Role = "user", Content = prompt, Timestamp = DateTime.UtcNow } },
                systemPrompt: "Generate 3 concise follow-up questions. Return only the questions, one per line.",
                options: new ChatOptions { Temperature = 0.7, MaxTokens = 200 },
                ct: ct);

            if (string.IsNullOrWhiteSpace(response)) return [];

            return response.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(q => q.Length > 5 && q.Length < 200)
                .Take(3)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to generate suggested questions for conversation {ConversationId}", conversationId);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryEntity>> GetAllMemoriesAsync(CancellationToken ct = default)
    {
        return await _db.Memories
            .Where(m => m.IsActive)
            .OrderByDescending(m => m.Importance)
            .ThenByDescending(m => m.CreatedAt)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task DismissMemoryAsync(long memoryId, CancellationToken ct = default)
    {
        var memory = await _db.Memories.FindAsync(new object[] { memoryId }, ct);
        if (memory != null)
        {
            memory.IsActive = false;
            await _db.SaveChangesAsync(ct);
            _logger.Debug("Dismissed memory {MemoryId}", memoryId);
        }
    }

    /// <inheritdoc />
    public async Task<int> GetMemoryCountAsync(CancellationToken ct = default)
    {
        return await _db.Memories.CountAsync(m => m.IsActive, ct);
    }
}
