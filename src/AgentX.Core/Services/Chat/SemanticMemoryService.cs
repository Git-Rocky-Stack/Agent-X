using System.Diagnostics;
using System.Text;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Configuration;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Mathematics;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace AgentX.Core.Services.Chat;

/// <summary>
/// Enhanced semantic memory service with embedding-based retrieval,
/// associative links, and temporal decay for human-like memory.
/// </summary>
public sealed class SemanticMemoryService : ISemanticMemoryService
{
    private readonly AgentXDbContext _db;
    private readonly IAiService _aiService;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger _logger;
    private readonly IRagConfiguration _ragConfiguration;

    // Associative linking threshold (kept as constant since it's a domain-specific threshold)
    private const float AssociativeLinkThreshold = 0.85f;

    // Valid memory categories (expanded from 4 to 20+)
    private static readonly string[] ValidCategories =
    [
        // User-focused
        "user_preference",
        "user_fact",
        "user_topic",
        "user_instruction",

        // Style-focused
        "interaction_style",
        "communication_preference",

        // Domain-focused
        "domain_expertise",
        "project_context",
        "technical_preference",

        // Relationship-focused
        "relationship",
        "goal",
        "constraint",
        "requirement",

        // Temporal-focused
        "episodic_event",
        "learning",
        "correction",
        "affirmation",

        // General
        "fact",
        "preference",
        "topic",
        "instruction",
        "context"
    ];

    public SemanticMemoryService(
        AgentXDbContext db,
        IAiService aiService,
        IEmbeddingService embeddingService,
        ILogger logger,
        IRagConfiguration ragConfiguration)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _logger = logger?.ForContext<SemanticMemoryService>() ?? throw new ArgumentNullException(nameof(logger));
        _ragConfiguration = ragConfiguration ?? throw new ArgumentNullException(nameof(ragConfiguration));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryEntity>> RetrieveRelevantMemoriesAsync(
        string query,
        int maxMemories = 10,
        float minSimilarity = 0.7f,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<MemoryEntity>();

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Step 1: Generate query embedding
            float[] queryEmbedding = await _embeddingService.EmbedAsync(query, ct);

            // Step 2: Get all active memories with embeddings
            var memoriesWithEmbeddings = await _db.Memories
                .AsNoTracking()
                .Where(m => m.IsActive && m.Embedding != null)
                .ToListAsync(ct);

            if (memoriesWithEmbeddings.Count == 0)
                return Array.Empty<MemoryEntity>();

            // Step 3: Calculate semantic similarity for each memory
            var scoredMemories = new List<(MemoryEntity Memory, float Similarity, double EffectiveImportance)>();

            foreach (var memory in memoriesWithEmbeddings)
            {
                if (TryParseEmbedding(memory.Embedding!, out var memoryEmbedding))
                {
                    float similarity = VectorMath.CosineSimilarity(queryEmbedding, memoryEmbedding);
                    if (similarity >= minSimilarity)
                    {
                        double effectiveImportance = GetEffectiveImportance(memory);
                        scoredMemories.Add((memory, similarity, effectiveImportance));
                    }
                }
            }

            // Step 4: Rank by combined score (similarity × effective importance)
            var ranked = scoredMemories
                .OrderByDescending(x => x.Similarity * x.EffectiveImportance)
                .Take(maxMemories)
                .Select(x => x.Memory)
                .ToList();

            // Step 5: Update usage stats (non-critical, don't await)
            _ = Task.Run(async () =>
            {
                try
                {
                    var memoryIds = ranked.Select(m => m.Id).ToList();
                    var now = DateTime.UtcNow;
                    await _db.Memories
                        .Where(m => memoryIds.Contains(m.Id))
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(m => m.UsageCount, m => m.UsageCount + 1)
                            .SetProperty(m => m.LastUsedAt, now), CancellationToken.None);
                }
                catch { /* Ignore failures */ }
            }, CancellationToken.None);

            stopwatch.Stop();
            _logger.Debug(
                "Semantic memory retrieval: {Count} memories for query '{Query}' in {ElapsedMs}ms",
                ranked.Count, Truncate(query, 50), stopwatch.ElapsedMilliseconds);

            return ranked;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Semantic memory retrieval failed, falling back to simple retrieval");
            return await FallbackRetrieveAsync(query, maxMemories, ct);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryEntity>> RetrieveAssociativeMemoriesAsync(
        long seedMemoryId,
        int maxDepth = 2,
        CancellationToken ct = default)
    {
        var result = new List<MemoryEntity>();
        var visited = new HashSet<long>();
        var queue = new Queue<(long MemoryId, int Depth)>();
        queue.Enqueue((seedMemoryId, 0));
        visited.Add(seedMemoryId);

        try
        {
            while (queue.Count > 0)
            {
                var (memoryId, depth) = queue.Dequeue();

                if (depth > maxDepth) continue;

                var memory = await _db.Memories
                    .AsNoTracking()
                    .Include(m => m.LinkedMemory)
                    .FirstOrDefaultAsync(m => m.Id == memoryId, ct);

                if (memory is null) continue;

                result.Add(memory);

                // Follow associative link
                if (memory.LinkedMemoryId.HasValue && memory.LinkedMemoryId.Value != memoryId)
                {
                    var linkedId = memory.LinkedMemoryId.Value;
                    if (visited.Add(linkedId))
                    {
                        queue.Enqueue((linkedId, depth + 1));
                    }
                }

                // Find memories that link to this one (reverse traversal)
                var reverseLinks = await _db.Memories
                    .AsNoTracking()
                    .Where(m => m.LinkedMemoryId == memoryId && m.IsActive)
                    .Select(m => m.Id)
                    .ToListAsync(ct);

                foreach (var reverseId in reverseLinks)
                {
                    if (visited.Add(reverseId))
                    {
                        queue.Enqueue((reverseId, depth + 1));
                    }
                }
            }

            _logger.Debug(
                "Associative memory retrieval from seed {SeedId}: {Count} memories found (depth={MaxDepth})",
                seedMemoryId, result.Count, maxDepth);

            return result;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Associative memory retrieval failed");
            return result;
        }
    }

    /// <inheritdoc />
    public async Task ExtractMemoriesAsync(long conversationId, CancellationToken ct = default)
    {
        try
        {
            // Get recent messages from this conversation
            var messages = await _db.Messages
                .Where(m => m.ConversationId == conversationId)
                .OrderByDescending(m => m.SortOrder)
                .Take(10)
                .ToListAsync(ct);

            if (messages.Count < 2) return;

            // Build conversation summary for extraction
            var recentContent = string.Join("\n", messages
                .OrderBy(m => m.SortOrder)
                .Select(m => $"{m.Role}: {m.Content}"));

            if (recentContent.Length > 3000)
                recentContent = recentContent[..3000];

            // Enhanced extraction prompt with expanded categories
            var extractionPrompt = $"""
                Extract key facts, user preferences, and important context from this conversation that would be useful to remember for future conversations.

                Return each memory on a separate line in the format:
                category|content|confidence

                Categories (use the most specific applicable):
                - user_preference: User's preferences (e.g., "Prefers dark mode", "Likes concise answers")
                - user_fact: Factual information about the user (e.g., "Works as a software engineer")
                - user_topic: Topics the user is interested in
                - user_instruction: Explicit instructions the user has given
                - interaction_style: How the user likes to interact (e.g., "Prefers informal tone")
                - communication_preference: Communication format preferences (e.g., "Likes bullet points")
                - domain_expertise: Areas where the user has expertise
                - project_context: Context about the user's projects
                - technical_preference: Technical preferences (e.g., "Prefers C# over Java")
                - episodic_event: Events that occurred in conversation
                - learning: Things the user learned
                - correction: Corrections the user made
                - fact: General factual information
                - preference: General preferences
                - topic: Topics discussed
                - instruction: Instructions given

                Confidence: 0.0-1.0 score indicating how confident you are this is worth remembering.

                Only extract genuinely useful information. Skip greetings and small talk.
                If there are no memorable facts, respond with NONE.

                Conversation:
                {recentContent}
                """;

            var response = await _aiService.ChatAsync(
                [new() { Role = "user", Content = extractionPrompt, Timestamp = DateTime.UtcNow }],
                systemPrompt: "You are a memory extraction assistant. Extract concise, useful facts from conversations with high precision.",
                options: new() { Temperature = 0.2, MaxTokens = 800 },
                ct: ct);

            if (string.IsNullOrWhiteSpace(response) ||
                response.Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase))
                return;

            // Parse and store memories with embeddings
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var newMemories = new List<MemoryEntity>();

            foreach (var line in lines)
            {
                var parts = line.Split('|', 3);
                if (parts.Length < 2) continue;

                var category = NormalizeCategory(parts[0].Trim().ToLower());
                var content = parts[1].Trim();
                var confidence = parts.Length > 3 && double.TryParse(parts[2].Trim(), out var conf)
                    ? Math.Clamp(conf, 0.0, 1.0)
                    : 0.8;

                if (string.IsNullOrWhiteSpace(content) || content.Length < 5) continue;

                // Check for duplicates using semantic similarity (not just substring)
                var existingMemories = await _db.Memories
                    .Where(m => m.IsActive)
                    .ToListAsync(ct);

                var contentEmbedding = await _embeddingService.EmbedAsync(content, ct);
                var isDuplicate = false;

                foreach (var existing in existingMemories)
                {
                    if (!string.IsNullOrEmpty(existing.Embedding) &&
                        TryParseEmbedding(existing.Embedding, out var existingEmbedding))
                    {
                        var similarity = VectorMath.CosineSimilarity(contentEmbedding, existingEmbedding);
                        if (similarity > 0.92f) // High threshold for duplicate detection
                        {
                            isDuplicate = true;
                            // Boost existing memory's importance
                            existing.Importance = Math.Min(1.0, existing.Importance + 0.1);
                            existing.LastUsedAt = DateTime.UtcNow;
                            break;
                        }
                    }
                }

                if (isDuplicate) continue;

                // Create new memory
                var embeddingStr = string.Join(",", contentEmbedding.Select(f => f.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)));
                var importance = CalculateInitialImportance(category, confidence);

                var memory = new MemoryEntity
                {
                    Content = content,
                    Category = category,
                    SourceConversationId = conversationId,
                    Importance = importance,
                    DecayRate = GetDecayRateForCategory(category),
                    Confidence = confidence,
                    Embedding = embeddingStr,
                    CreatedAt = DateTime.UtcNow,
                    LastUsedAt = DateTime.UtcNow
                };

                newMemories.Add(memory);
            }

            if (newMemories.Count > 0)
            {
                _db.Memories.AddRange(newMemories);
                await _db.SaveChangesAsync(ct);

                // Create associative links for new memories
                await CreateAssociativeLinksAsync(newMemories, ct);

                _logger.Information("Extracted {Count} new memories from conversation {ConversationId}",
                    newMemories.Count, conversationId);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to extract memories from conversation {ConversationId}", conversationId);
        }
    }

    /// <inheritdoc />
    public async Task LinkMemoriesAsync(long memoryId1, long memoryId2, CancellationToken ct = default)
    {
        try
        {
            var memory1 = await _db.Memories.FindAsync(new object[] { memoryId1 }, ct);
            var memory2 = await _db.Memories.FindAsync(new object[] { memoryId2 }, ct);

            if (memory1 is null || memory2 is null) return;

            // Create bidirectional link
            memory1.LinkedMemoryId = memoryId2;
            // memory2 keeps its existing link or stays null

            await _db.SaveChangesAsync(ct);

            _logger.Debug("Linked memories {Id1} and {Id2}", memoryId1, memoryId2);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to link memories {Id1} and {Id2}", memoryId1, memoryId2);
        }
    }

    /// <inheritdoc />
    public async Task ApplyFeedbackAsync(long memoryId, bool isPositive, CancellationToken ct = default)
    {
        try
        {
            var memory = await _db.Memories.FindAsync(new object[] { memoryId }, ct);
            if (memory is null) return;

            if (isPositive)
            {
                // Reinforce: increase importance and reduce decay
                memory.Importance = Math.Min(1.0, memory.Importance + 0.15);
                memory.DecayRate = Math.Max(0.001, memory.DecayRate * 0.8);
            }
            else
            {
                // Correct: decrease importance or dismiss
                memory.Importance = Math.Max(0.1, memory.Importance - 0.2);
                if (memory.Importance < 0.3)
                {
                    memory.IsActive = false;
                }
            }

            await _db.SaveChangesAsync(ct);

            _logger.Debug("Applied feedback to memory {MemoryId}: {Feedback}",
                memoryId, isPositive ? "positive" : "negative");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to apply feedback to memory {MemoryId}", memoryId);
        }
    }

    /// <inheritdoc />
    public double GetEffectiveImportance(MemoryEntity memory)
    {
        if (memory is null) return 0.0;

        var daysSinceLastAccess = (DateTime.UtcNow - memory.LastUsedAt).TotalDays;
        var decayFactor = Math.Exp(-memory.DecayRate * Math.Min(daysSinceLastAccess, _ragConfiguration.MemoryDaysBeforeFullDecay));

        return memory.Importance * decayFactor;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryEntity>> GetAllMemoriesAsync(CancellationToken ct = default)
    {
        // Effective importance is a temporal-decay computation (DateTime.UtcNow + Math.Exp via
        // GetEffectiveImportance) that EF cannot translate to SQL. Materialize the active set
        // first, then rank it in memory — the active-memory set is bounded (user facts), so the
        // client-side sort is cheap and, unlike an in-query OrderBy, actually executes.
        var active = await _db.Memories
            .AsNoTracking()
            .Where(m => m.IsActive)
            .ToListAsync(ct);

        return active
            .OrderByDescending(GetEffectiveImportance)
            .ThenByDescending(m => m.LastUsedAt)
            .ToList();
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

    // ═══════════════════════════════════════════════════════════════════
    //  Private helpers
    // ═══════════════════════════════════════════════════════════════════

    private async Task CreateAssociativeLinksAsync(List<MemoryEntity> newMemories, CancellationToken ct)
    {
        try
        {
            var existingMemories = await _db.Memories
                .AsNoTracking()
                .Where(m => m.IsActive && m.Id != 0 && m.Embedding != null)
                .ToListAsync(ct);

            foreach (var newMemory in newMemories)
            {
                if (string.IsNullOrEmpty(newMemory.Embedding)) continue;
                if (!TryParseEmbedding(newMemory.Embedding, out var newEmbedding)) continue;

                MemoryEntity? bestMatch = null;
                float bestSimilarity = 0f;

                foreach (var existing in existingMemories)
                {
                    if (string.IsNullOrEmpty(existing.Embedding)) continue;
                    if (!TryParseEmbedding(existing.Embedding, out var existingEmbedding)) continue;

                    var similarity = VectorMath.CosineSimilarity(newEmbedding, existingEmbedding);

                    if (similarity > AssociativeLinkThreshold && similarity > bestSimilarity)
                    {
                        bestMatch = existing;
                        bestSimilarity = similarity;
                    }
                }

                if (bestMatch != null)
                {
                    // Reload to attach for update
                    var memoryToUpdate = await _db.Memories.FindAsync(new object[] { newMemory.Id }, ct);
                    if (memoryToUpdate != null)
                    {
                        memoryToUpdate.LinkedMemoryId = bestMatch.Id;
                        _logger.Debug("Created associative link: {NewId} -> {ExistingId} (similarity={Similarity:F2})",
                            newMemory.Id, bestMatch.Id, bestSimilarity);
                    }
                }
            }

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to create associative links");
        }
    }

    private async Task<IReadOnlyList<MemoryEntity>> FallbackRetrieveAsync(
        string query, int maxMemories, CancellationToken ct)
    {
        return await _db.Memories
            .AsNoTracking()
            .Where(m => m.IsActive)
            .OrderByDescending(m => m.Importance)
            .ThenByDescending(m => m.LastUsedAt)
            .Take(maxMemories)
            .ToListAsync(ct);
    }


    private static bool TryParseEmbedding(string embeddingStr, out float[] embedding)
    {
        embedding = Array.Empty<float>();

        if (string.IsNullOrWhiteSpace(embeddingStr)) return false;

        try
        {
            var parts = embeddingStr.Split(',');
            embedding = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (float.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var val))
                {
                    embedding[i] = val;
                }
                else
                {
                    return false;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category)) return "fact";

        var normalized = category.ToLower().Replace(" ", "_").Replace("-", "_");

        return ValidCategories.Contains(normalized) ? normalized : "fact";
    }

    private static double CalculateInitialImportance(string category, double confidence)
    {
        // Base importance by category
        var baseImportance = category switch
        {
            "user_preference" => 0.85,
            "user_instruction" => 0.90,
            "instruction" => 0.85,
            "preference" => 0.80,
            "interaction_style" => 0.75,
            "communication_preference" => 0.75,
            "domain_expertise" => 0.70,
            "correction" => 0.80,
            "requirement" => 0.75,
            "constraint" => 0.70,
            "affirmation" => 0.65,
            "learning" => 0.70,
            "user_fact" => 0.60,
            "project_context" => 0.65,
            "technical_preference" => 0.70,
            "episodic_event" => 0.50,
            "user_topic" => 0.55,
            "topic" => 0.50,
            "fact" => 0.50,
            "context" => 0.45,
            _ => 0.50
        };

        return Math.Clamp(baseImportance * confidence, 0.1, 1.0);
    }

    private double GetDecayRateForCategory(string category)
    {
        // Important preferences/instructions should decay slower
        return category switch
        {
            "user_preference" => 0.005,
            "user_instruction" => 0.003,
            "instruction" => 0.005,
            "preference" => 0.008,
            "interaction_style" => 0.007,
            "communication_preference" => 0.007,
            "correction" => 0.005,
            "requirement" => 0.006,
            "constraint" => 0.008,
            "domain_expertise" => 0.01,
            "technical_preference" => 0.01,
            "affirmation" => 0.015,
            "learning" => 0.012,
            "user_fact" => 0.015,
            "project_context" => 0.02,
            "episodic_event" => 0.03,
            "user_topic" => 0.02,
            "topic" => 0.025,
            "fact" => 0.02,
            "context" => 0.025,
            _ => _ragConfiguration.MemoryDecayRate
        };
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
