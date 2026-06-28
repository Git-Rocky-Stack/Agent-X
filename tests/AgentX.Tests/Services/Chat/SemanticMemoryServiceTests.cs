using AgentX.Core.AI;
using AgentX.Core.Configuration;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Chat;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Chat;

/// <summary>
/// Coverage for <see cref="SemanticMemoryService"/> — the embedding-based semantic memory
/// store with associative links and temporal decay. Backed by an in-memory SQLite
/// <see cref="AgentXDbContext"/>; the embedding/AI collaborators are mocked so cosine
/// similarities are deterministic (length-4 unit-ish vectors), and a real silent Serilog
/// logger is supplied because the constructor consumes <c>logger.ForContext&lt;T&gt;()</c>.
/// </summary>
public sealed class SemanticMemoryServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();
    private readonly AgentXDbContext _db;
    private readonly Serilog.Core.Logger _logger = new LoggerConfiguration().CreateLogger();
    private readonly Mock<IAiService> _ai = new(MockBehavior.Loose);
    private readonly Mock<IEmbeddingService> _embeddings = new(MockBehavior.Loose);
    private readonly Mock<IRagConfiguration> _rag = new(MockBehavior.Loose);

    public SemanticMemoryServiceTests()
    {
        _db = _factory.CreateContext();
        _rag.SetupGet(r => r.MemoryDaysBeforeFullDecay).Returns(30);
        _rag.SetupGet(r => r.MemoryDecayRate).Returns(0.02);
    }

    public void Dispose()
    {
        _db.Dispose();
        _factory.Dispose();
        _logger.Dispose();
    }

    private SemanticMemoryService CreateSut(AgentXDbContext? db = null)
        => new(db ?? _db, _ai.Object, _embeddings.Object, _logger, _rag.Object);

    private void SetupEmbedding(params float[] vector)
        => _embeddings
            .Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(vector);

    private void SetupChat(string response)
        => _ai
            .Setup(a => a.ChatAsync(
                It.IsAny<IReadOnlyList<AgentX.Core.AI.Models.ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<AgentX.Core.AI.Models.ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

    private static MemoryEntity Memory(
        string content,
        string? embedding,
        double importance = 0.5,
        bool active = true,
        DateTime? lastUsed = null,
        string category = "fact") => new()
        {
            Content = content,
            Category = category,
            Embedding = embedding,
            Importance = importance,
            IsActive = active,
            DecayRate = 0.01,
            LastUsedAt = lastUsed ?? new DateTime(2026, 6, 28, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        };

    // ─────────────────────────────────────────────────────────────────────
    //  RetrieveRelevantMemoriesAsync
    // ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task RetrieveRelevant_blank_query_returns_empty(string? query)
    {
        var result = await CreateSut().RetrieveRelevantMemoriesAsync(query!);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RetrieveRelevant_no_embedded_memories_returns_empty()
    {
        // Active memory but no embedding → excluded by the Embedding != null filter.
        _db.Memories.Add(Memory("no embedding here", embedding: null, importance: 0.9));
        await _db.SaveChangesAsync();
        SetupEmbedding(1, 0, 0, 0);

        var result = await CreateSut().RetrieveRelevantMemoriesAsync("anything");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RetrieveRelevant_ranks_by_similarity_times_importance_and_honours_max_and_threshold()
    {
        // Query vector is [1,0,0,0]; cosine similarity is fully controlled by each embedding.
        _db.Memories.AddRange(
            Memory("exact match high importance", "1,0,0,0", importance: 0.9),     // sim 1.0   → 0.90
            Memory("near match high importance", "1,0.5,0,0", importance: 0.9),    // sim 0.894 → 0.805
            Memory("near match low importance", "1,0.2,0,0", importance: 0.5),     // sim 0.981 → 0.49
            Memory("orthogonal below threshold", "0,1,0,0", importance: 0.9),      // sim 0.0   → excluded
            Memory("unparseable embedding", "not,a,number,x", importance: 0.9),    // parse fail → skipped
            Memory("inactive ignored", "1,0,0,0", importance: 1.0, active: false)); // inactive → excluded
        await _db.SaveChangesAsync();
        SetupEmbedding(1, 0, 0, 0);

        var result = await CreateSut().RetrieveRelevantMemoriesAsync(
            "match", maxMemories: 2, minSimilarity: 0.7f);

        result.Should().HaveCount(2);
        result[0].Content.Should().Be("exact match high importance");
        result[1].Content.Should().Be("near match high importance");
    }

    [Fact]
    public async Task RetrieveRelevant_all_below_threshold_returns_empty()
    {
        _db.Memories.Add(Memory("orthogonal", "0,1,0,0", importance: 0.9));
        await _db.SaveChangesAsync();
        SetupEmbedding(1, 0, 0, 0);

        var result = await CreateSut().RetrieveRelevantMemoriesAsync("x", minSimilarity: 0.5f);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RetrieveRelevant_embedding_failure_falls_back_to_importance_order()
    {
        _db.Memories.AddRange(
            Memory("least important", embedding: null, importance: 0.2),
            Memory("most important", embedding: null, importance: 0.95),
            Memory("middle", embedding: null, importance: 0.6));
        await _db.SaveChangesAsync();
        _embeddings
            .Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("embedder offline"));

        var result = await CreateSut().RetrieveRelevantMemoriesAsync("x", maxMemories: 2);

        result.Should().HaveCount(2);
        result[0].Content.Should().Be("most important");
        result[1].Content.Should().Be("middle");
    }

    [Fact]
    public async Task RetrieveRelevant_cancellation_propagates()
    {
        _embeddings
            .Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        Func<Task> act = () => CreateSut().RetrieveRelevantMemoriesAsync("x");

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  RetrieveAssociativeMemoriesAsync
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RetrieveAssociative_traverses_forward_and_reverse_links()
    {
        var a = Memory("seed A", "1,0,0,0");
        var b = Memory("forward B", "0,1,0,0");
        var c = Memory("reverse C", "0,0,1,0");
        _db.Memories.AddRange(a, b, c);
        await _db.SaveChangesAsync();

        a.LinkedMemoryId = b.Id;   // forward edge A → B
        c.LinkedMemoryId = a.Id;   // reverse edge C → A
        await _db.SaveChangesAsync();

        var result = await CreateSut().RetrieveAssociativeMemoriesAsync(a.Id, maxDepth: 2);

        result.Select(m => m.Id).Should().BeEquivalentTo(new[] { a.Id, b.Id, c.Id });
    }

    [Fact]
    public async Task RetrieveAssociative_respects_max_depth_zero()
    {
        var a = Memory("seed A", "1,0,0,0");
        var b = Memory("forward B", "0,1,0,0");
        _db.Memories.AddRange(a, b);
        await _db.SaveChangesAsync();
        a.LinkedMemoryId = b.Id;
        await _db.SaveChangesAsync();

        var result = await CreateSut().RetrieveAssociativeMemoriesAsync(a.Id, maxDepth: 0);

        result.Should().ContainSingle().Which.Id.Should().Be(a.Id);
    }

    [Fact]
    public async Task RetrieveAssociative_unknown_seed_returns_empty()
    {
        var result = await CreateSut().RetrieveAssociativeMemoriesAsync(99999);
        result.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ExtractMemoriesAsync
    // ─────────────────────────────────────────────────────────────────────

    private async Task<long> SeedConversationWithMessagesAsync(params string[] contents)
    {
        var conversation = new ConversationEntity
        {
            Title = "Test",
            ModelId = "test-model",
            CreatedAt = new DateTime(2026, 6, 28, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 6, 28, 0, 0, 0, DateTimeKind.Utc),
        };
        _db.Conversations.Add(conversation);
        await _db.SaveChangesAsync();

        for (var i = 0; i < contents.Length; i++)
        {
            _db.Messages.Add(new MessageEntity
            {
                ConversationId = conversation.Id,
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = contents[i],
                Timestamp = new DateTime(2026, 6, 28, 0, i, 0, DateTimeKind.Utc),
                SortOrder = i,
            });
        }
        await _db.SaveChangesAsync();
        return conversation.Id;
    }

    [Fact]
    public async Task ExtractMemories_fewer_than_two_messages_creates_nothing()
    {
        var convId = await SeedConversationWithMessagesAsync("only one message");
        SetupEmbedding(1, 0, 0, 0);
        SetupChat("user_preference|prefers dark mode|0.9");

        await CreateSut().ExtractMemoriesAsync(convId);

        (await _factory.CreateContext().Memories.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("NONE")]
    [InlineData("none")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExtractMemories_empty_or_none_response_creates_nothing(string response)
    {
        var convId = await SeedConversationWithMessagesAsync("hello there", "hi back");
        SetupEmbedding(1, 0, 0, 0);
        SetupChat(response);

        await CreateSut().ExtractMemoriesAsync(convId);

        (await _factory.CreateContext().Memories.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ExtractMemories_creates_memories_across_all_categories_and_skips_bad_lines()
    {
        var convId = await SeedConversationWithMessagesAsync("question", "answer");
        SetupEmbedding(1, 0, 0, 0);

        // Every valid category exercises a distinct CalculateInitialImportance /
        // GetDecayRateForCategory switch arm; "relationship" and "goal" fall to the
        // default arms. An unknown category and an empty category both normalise to "fact".
        var validCategories = new[]
        {
            "user_preference", "user_fact", "user_topic", "user_instruction",
            "interaction_style", "communication_preference", "domain_expertise",
            "project_context", "technical_preference", "relationship", "goal",
            "constraint", "requirement", "episodic_event", "learning", "correction",
            "affirmation", "fact", "preference", "topic", "instruction", "context",
        };

        var lines = validCategories
            .Select(c => $"{c}|the user mentioned something about {c}|0.9")
            .ToList();
        lines.Add("totally-unknown|content that normalises category to fact|0.9");
        lines.Add("|content with an empty leading category field|0.9");
        lines.Add("fact|abc|0.9");          // content < 5 chars → skipped
        lines.Add("nopipehere just words"); // no delimiter → skipped

        SetupChat(string.Join("\n", lines));

        await CreateSut().ExtractMemoriesAsync(convId);

        await using var verify = _factory.CreateContext();
        // 22 valid categories + unknown→fact + empty→fact = 24 created; two bad lines skipped.
        (await verify.Memories.CountAsync()).Should().Be(24);
        (await verify.Memories.AnyAsync(m => m.Category == "user_preference")).Should().BeTrue();
        (await verify.Memories.AnyAsync(m => m.Category == "relationship")).Should().BeTrue();
        (await verify.Memories.AnyAsync(m => m.Content == "abc")).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractMemories_skips_semantically_duplicate_content()
    {
        // Pre-existing active memory whose embedding equals what the embedder returns
        // for the new content → cosine similarity 1.0 > 0.92 duplicate threshold.
        _db.Memories.Add(Memory("existing fact", "1,0,0,0", importance: 0.5));
        await _db.SaveChangesAsync();
        var convId = await SeedConversationWithMessagesAsync("q", "a");
        SetupEmbedding(1, 0, 0, 0);
        SetupChat("fact|brand new content that duplicates|0.9");

        await CreateSut().ExtractMemoriesAsync(convId);

        // No second memory was added — the duplicate was folded into the existing one.
        (await _factory.CreateContext().Memories.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ExtractMemories_creates_associative_link_for_saved_memory()
    {
        var convId = await SeedConversationWithMessagesAsync("q", "a");
        SetupEmbedding(1, 0.5f, 0, 0); // distinct from any pre-existing memory
        SetupChat("fact|a memorable fact worth keeping around|0.9");

        await CreateSut().ExtractMemoriesAsync(convId);

        await using var verify = _factory.CreateContext();
        var created = await verify.Memories.SingleAsync();
        created.LinkedMemoryId.Should().NotBeNull();
    }

    [Fact]
    public async Task ExtractMemories_truncates_very_long_conversation_content()
    {
        var convId = await SeedConversationWithMessagesAsync(
            new string('x', 4000), new string('y', 4000));
        SetupEmbedding(1, 0, 0, 0);
        SetupChat("NONE");

        await CreateSut().ExtractMemoriesAsync(convId);

        _ai.Verify(a => a.ChatAsync(
            It.IsAny<IReadOnlyList<AgentX.Core.AI.Models.ChatMessage>>(),
            It.IsAny<string?>(),
            It.IsAny<AgentX.Core.AI.Models.ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExtractMemories_swallows_ai_failure()
    {
        var convId = await SeedConversationWithMessagesAsync("q", "a");
        SetupEmbedding(1, 0, 0, 0);
        _ai
            .Setup(a => a.ChatAsync(
                It.IsAny<IReadOnlyList<AgentX.Core.AI.Models.ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<AgentX.Core.AI.Models.ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("model crashed"));

        var act = () => CreateSut().ExtractMemoriesAsync(convId);

        await act.Should().NotThrowAsync();
        (await _factory.CreateContext().Memories.CountAsync()).Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  LinkMemoriesAsync
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LinkMemories_sets_link_when_both_exist()
    {
        var a = Memory("a", "1,0,0,0");
        var b = Memory("b", "0,1,0,0");
        _db.Memories.AddRange(a, b);
        await _db.SaveChangesAsync();

        await CreateSut().LinkMemoriesAsync(a.Id, b.Id);

        var reloaded = await _factory.CreateContext().Memories.FirstAsync(m => m.Id == a.Id);
        reloaded.LinkedMemoryId.Should().Be(b.Id);
    }

    [Fact]
    public async Task LinkMemories_no_op_when_target_missing()
    {
        var a = Memory("a", "1,0,0,0");
        _db.Memories.Add(a);
        await _db.SaveChangesAsync();

        await CreateSut().LinkMemoriesAsync(a.Id, 99999);

        var reloaded = await _factory.CreateContext().Memories.FirstAsync(m => m.Id == a.Id);
        reloaded.LinkedMemoryId.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  ApplyFeedbackAsync
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyFeedback_positive_reinforces_importance_and_slows_decay()
    {
        var m = Memory("reinforce me", "1,0,0,0", importance: 0.5);
        m.DecayRate = 0.01;
        _db.Memories.Add(m);
        await _db.SaveChangesAsync();

        await CreateSut().ApplyFeedbackAsync(m.Id, isPositive: true);

        var reloaded = await _factory.CreateContext().Memories.FirstAsync(x => x.Id == m.Id);
        reloaded.Importance.Should().BeApproximately(0.65, 1e-9);
        reloaded.DecayRate.Should().BeApproximately(0.008, 1e-9);
        reloaded.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyFeedback_negative_lowers_importance_but_keeps_active()
    {
        var m = Memory("mild correction", "1,0,0,0", importance: 0.8);
        _db.Memories.Add(m);
        await _db.SaveChangesAsync();

        await CreateSut().ApplyFeedbackAsync(m.Id, isPositive: false);

        var reloaded = await _factory.CreateContext().Memories.FirstAsync(x => x.Id == m.Id);
        reloaded.Importance.Should().BeApproximately(0.6, 1e-9);
        reloaded.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyFeedback_negative_deactivates_when_importance_drops_below_floor()
    {
        var m = Memory("low value memory", "1,0,0,0", importance: 0.35);
        _db.Memories.Add(m);
        await _db.SaveChangesAsync();

        await CreateSut().ApplyFeedbackAsync(m.Id, isPositive: false);

        var reloaded = await _factory.CreateContext().Memories.FirstAsync(x => x.Id == m.Id);
        reloaded.Importance.Should().BeApproximately(0.15, 1e-9);
        reloaded.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyFeedback_unknown_memory_is_no_op()
    {
        var act = () => CreateSut().ApplyFeedbackAsync(99999, isPositive: true);
        await act.Should().NotThrowAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  GetEffectiveImportance
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetEffectiveImportance_null_returns_zero()
    {
        CreateSut().GetEffectiveImportance(null!).Should().Be(0.0);
    }

    [Fact]
    public void GetEffectiveImportance_recent_memory_is_near_base_importance()
    {
        var m = Memory("fresh", "1,0,0,0", importance: 0.8, lastUsed: DateTime.UtcNow);
        m.DecayRate = 0.01;

        CreateSut().GetEffectiveImportance(m).Should().BeApproximately(0.8, 0.02);
    }

    [Fact]
    public void GetEffectiveImportance_old_memory_decays_and_caps_decay_window()
    {
        // 100 days idle, but the decay window is capped at MemoryDaysBeforeFullDecay (30).
        var m = Memory("stale", "1,0,0,0", importance: 0.8,
            lastUsed: DateTime.UtcNow.AddDays(-100));
        m.DecayRate = 0.01;

        var expected = 0.8 * Math.Exp(-0.01 * 30);
        CreateSut().GetEffectiveImportance(m).Should().BeApproximately(expected, 0.01);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  GetAllMemoriesAsync — see note: the OrderBy uses the un-translatable
    //  instance method GetEffectiveImportance, so EF cannot build SQL for it.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllMemoriesAsync_orders_active_memories_by_effective_importance()
    {
        _db.Memories.AddRange(
            Memory("low", "1,0,0,0", importance: 0.2, lastUsed: DateTime.UtcNow),
            Memory("high", "1,0,0,0", importance: 0.9, lastUsed: DateTime.UtcNow),
            Memory("middle", "1,0,0,0", importance: 0.6, lastUsed: DateTime.UtcNow),
            Memory("dismissed", "1,0,0,0", importance: 1.0, active: false));
        await _db.SaveChangesAsync();

        var all = await CreateSut().GetAllMemoriesAsync();

        all.Should().HaveCount(3);
        all.Select(m => m.Content).Should().ContainInOrder("high", "middle", "low");
        all.Should().NotContain(m => m.Content == "dismissed");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  DismissMemoryAsync / GetMemoryCountAsync
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DismissMemory_soft_deletes_existing_memory()
    {
        var m = Memory("dismiss me", "1,0,0,0");
        _db.Memories.Add(m);
        await _db.SaveChangesAsync();

        await CreateSut().DismissMemoryAsync(m.Id);

        var reloaded = await _factory.CreateContext().Memories.FirstAsync(x => x.Id == m.Id);
        reloaded.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DismissMemory_unknown_memory_is_no_op()
    {
        var act = () => CreateSut().DismissMemoryAsync(99999);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetMemoryCount_counts_only_active_memories()
    {
        _db.Memories.AddRange(
            Memory("active 1", "1,0,0,0"),
            Memory("active 2", "1,0,0,0"),
            Memory("inactive", "1,0,0,0", active: false));
        await _db.SaveChangesAsync();

        (await CreateSut().GetMemoryCountAsync()).Should().Be(2);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Resilience — the warn-and-continue catch arms in the swallowing methods.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Swallowing_methods_do_not_throw_when_context_is_disposed()
    {
        var deadDb = _factory.CreateContext();
        deadDb.Dispose();
        var sut = CreateSut(deadDb);
        SetupEmbedding(1, 0, 0, 0);
        SetupChat("fact|something|0.9");

        (await sut.RetrieveAssociativeMemoriesAsync(1)).Should().BeEmpty();

        await ((Func<Task>)(() => sut.ExtractMemoriesAsync(1))).Should().NotThrowAsync();
        await ((Func<Task>)(() => sut.LinkMemoriesAsync(1, 2))).Should().NotThrowAsync();
        await ((Func<Task>)(() => sut.ApplyFeedbackAsync(1, true))).Should().NotThrowAsync();
    }
}
