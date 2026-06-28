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
/// Behavioural coverage for <see cref="ConversationService"/> — the EF-Core CRUD surface for chat
/// conversations and their messages: create / query / search / rename / pin / archive / delete,
/// message add / delete / edit / truncate (with conversation-metadata bookkeeping), token + count
/// stats, and folder / tag organization.
///
/// <para><b>Harness design.</b> The service is a straight EF orchestrator over a real
/// <see cref="AgentXDbContext"/> (in-memory SQLite via <see cref="TestDbContextFactory"/>). Two
/// collaborators are optional best-effort hooks invoked after message mutations:
/// <see cref="IConversationRecallService"/> (embedding refresh) and
/// <see cref="IConversationSummaryService"/> (summary-staleness) — both mocked so the post-write
/// hooks, their null-skip paths, and their swallowed-failure paths are all exercised. The logger is
/// consumed through <c>ILogger.ForContext&lt;T&gt;()</c>, so the harness supplies a real silent Serilog
/// logger (a loose mock's <c>ForContext</c> returns null, which the constructor treats as a missing
/// logger).</para>
/// </summary>
public sealed class ConversationServiceTests : IDisposable
{
    private readonly List<ConvHarness> _harnesses = new();

    private ConvHarness NewHarness(bool withRecall = true, bool withSummary = true)
    {
        var h = new ConvHarness(withRecall, withSummary);
        _harnesses.Add(h);
        return h;
    }

    public void Dispose()
    {
        foreach (var h in _harnesses)
        {
            h.Dispose();
        }
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private sealed class ConvHarness : IDisposable
    {
        public TestDbContextFactory Factory { get; } = new();
        public AgentXDbContext Db { get; }
        public Serilog.Core.Logger Logger { get; } = new LoggerConfiguration().CreateLogger();
        public Mock<IConversationRecallService> Recall { get; } = new();
        public Mock<IConversationSummaryService> Summary { get; } = new();
        public ConversationService Service { get; }

        public ConvHarness(bool withRecall, bool withSummary)
        {
            Db = Factory.CreateContext();
            Service = new ConversationService(
                Db,
                Logger,
                withRecall ? Recall.Object : null,
                withSummary ? Summary.Object : null);
        }

        public void Seed(Action<AgentXDbContext> seed)
        {
            using var ctx = Factory.CreateContext();
            seed(ctx);
            ctx.SaveChanges();
        }

        public AgentXDbContext Fresh() => Factory.CreateContext();

        public void Dispose()
        {
            Db.Dispose();
            Factory.Dispose();
            Logger.Dispose();
        }
    }

    // ─── Seed helpers ─────────────────────────────────────────────────────────

    private static ConversationEntity NewConv(
        string title = "Conversation",
        bool isArchived = false,
        bool isPinned = false,
        DateTime? updatedAt = null,
        string? folderName = null,
        int messageCount = 0,
        long tokensUsed = 0)
    {
        var now = updatedAt ?? DateTime.UtcNow;
        return new ConversationEntity
        {
            Title = title,
            ModelId = "test-model",
            CreatedAt = now,
            UpdatedAt = now,
            IsArchived = isArchived,
            IsPinned = isPinned,
            FolderName = folderName,
            MessageCount = messageCount,
            TokensUsed = tokensUsed,
        };
    }

    private static MessageEntity NewMsg(
        long conversationId,
        string role = "user",
        string content = "hello",
        int sortOrder = 0,
        int tokenCount = 0)
    {
        return new MessageEntity
        {
            ConversationId = conversationId,
            Role = role,
            Content = content,
            SortOrder = sortOrder,
            TokenCount = tokenCount,
            Timestamp = DateTime.UtcNow,
        };
    }

    /// <summary>Seeds a conversation and returns its generated id.</summary>
    private static long SeedConv(ConvHarness h, ConversationEntity conv)
    {
        h.Seed(ctx => ctx.Conversations.Add(conv));
        return conv.Id;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Constructor guards
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Ctor_NullDb_Throws()
    {
        using var logger = new LoggerConfiguration().CreateLogger();
        var act = () => new ConversationService(null!, logger);
        act.Should().Throw<ArgumentNullException>().WithParameterName("db");
    }

    [Fact]
    public void Ctor_NullLogger_Throws()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();
        var act = () => new ConversationService(db, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  CreateConversationAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateConversationAsync_Defaults_AutoTitlesAndPersists()
    {
        var h = NewHarness();

        var conv = await h.Service.CreateConversationAsync();

        conv.Id.Should().BeGreaterThan(0);
        conv.Title.Should().StartWith("New Conversation");
        conv.ModelId.Should().BeEmpty();
        conv.MessageCount.Should().Be(0);
        conv.TokensUsed.Should().Be(0);
        conv.IsArchived.Should().BeFalse();
        conv.IsPinned.Should().BeFalse();

        using var fresh = h.Fresh();
        (await fresh.Conversations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateConversationAsync_ExplicitValues_AreUsed()
    {
        var h = NewHarness();

        var conv = await h.Service.CreateConversationAsync("My Chat", "be terse", "llama3");

        conv.Title.Should().Be("My Chat");
        conv.SystemPrompt.Should().Be("be terse");
        conv.ModelId.Should().Be("llama3");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  GetConversationAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetConversationAsync_Existing_ReturnsWithOrderedMessages()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var c = NewConv();
            ctx.Conversations.Add(c);
            ctx.SaveChanges();
            id = c.Id;
            ctx.Messages.Add(NewMsg(id, "assistant", "second", sortOrder: 1));
            ctx.Messages.Add(NewMsg(id, "user", "first", sortOrder: 0));
        });

        var conv = await h.Service.GetConversationAsync(id);

        conv.Should().NotBeNull();
        conv!.Messages.Should().HaveCount(2);
        conv.Messages.First().Content.Should().Be("first"); // ordered by SortOrder
    }

    [Fact]
    public async Task GetConversationAsync_Missing_ReturnsNull()
    {
        var h = NewHarness();
        (await h.Service.GetConversationAsync(404)).Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  GetAllConversationsAsync / GetRecentConversationsAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetAllConversationsAsync_ExcludesArchived_PinnedFirst()
    {
        var h = NewHarness();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        h.Seed(ctx =>
        {
            ctx.Conversations.Add(NewConv(title: "pinned", isPinned: true, updatedAt: t0));
            ctx.Conversations.Add(NewConv(title: "recent", updatedAt: t0.AddDays(5)));
            ctx.Conversations.Add(NewConv(title: "archived", isArchived: true, updatedAt: t0.AddDays(9)));
        });

        var result = await h.Service.GetAllConversationsAsync();

        result.Should().HaveCount(2);
        result[0].Title.Should().Be("pinned"); // pinned sorts first despite older UpdatedAt
        result[1].Title.Should().Be("recent");
    }

    [Fact]
    public async Task GetAllConversationsAsync_IncludeArchived_ReturnsAll()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.Conversations.Add(NewConv(title: "active"));
            ctx.Conversations.Add(NewConv(title: "archived", isArchived: true));
        });

        var result = await h.Service.GetAllConversationsAsync(includeArchived: true);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetRecentConversationsAsync_NewestFirst_RespectsLimit()
    {
        var h = NewHarness();
        var t0 = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        h.Seed(ctx =>
        {
            for (int i = 0; i < 4; i++)
            {
                ctx.Conversations.Add(NewConv(title: $"c{i}", updatedAt: t0.AddDays(i)));
            }
        });

        var result = await h.Service.GetRecentConversationsAsync(limit: 2);

        result.Should().HaveCount(2);
        result[0].Title.Should().Be("c3");
        result[1].Title.Should().Be("c2");
    }

    [Fact]
    public async Task GetRecentConversationsAsync_NonPositiveLimit_NormalizesToOne()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.Conversations.Add(NewConv(title: "a"));
            ctx.Conversations.Add(NewConv(title: "b"));
        });

        var result = await h.Service.GetRecentConversationsAsync(limit: 0);

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetRecentConversationsAsync_IncludeArchived_ReturnsArchived()
    {
        var h = NewHarness();
        h.Seed(ctx => ctx.Conversations.Add(NewConv(title: "archived", isArchived: true)));

        var result = await h.Service.GetRecentConversationsAsync(limit: 5, includeArchived: true);

        result.Should().ContainSingle();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  SearchConversationsAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SearchConversationsAsync_EmptyQuery_ReturnsAll()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.Conversations.Add(NewConv(title: "one"));
            ctx.Conversations.Add(NewConv(title: "two"));
        });

        var result = await h.Service.SearchConversationsAsync("   ");

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchConversationsAsync_MatchesTitle()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.Conversations.Add(NewConv(title: "Budget planning"));
            ctx.Conversations.Add(NewConv(title: "Holiday ideas"));
        });

        var result = await h.Service.SearchConversationsAsync("budget");

        result.Should().ContainSingle();
        result[0].Title.Should().Be("Budget planning");
    }

    [Fact]
    public async Task SearchConversationsAsync_MatchesMessageContent()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var c = NewConv(title: "Untitled");
            ctx.Conversations.Add(c);
            ctx.SaveChanges();
            id = c.Id;
            ctx.Messages.Add(NewMsg(id, "user", "tell me about quantum computing"));
        });

        var result = await h.Service.SearchConversationsAsync("quantum");

        result.Should().ContainSingle();
        result[0].Id.Should().Be(id);
    }

    [Fact]
    public async Task SearchConversationsAsync_ExcludesArchivedAndNoMatch()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.Conversations.Add(NewConv(title: "secret budget", isArchived: true));
            ctx.Conversations.Add(NewConv(title: "unrelated"));
        });

        var result = await h.Service.SearchConversationsAsync("budget");

        result.Should().BeEmpty(); // the only title match is archived
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Title / pin / archive / delete
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateConversationTitleAsync_Existing_Updates()
    {
        var h = NewHarness();
        var id = SeedConv(h, NewConv(title: "old"));

        await h.Service.UpdateConversationTitleAsync(id, "new title");

        using var fresh = h.Fresh();
        (await fresh.Conversations.FindAsync(id))!.Title.Should().Be("new title");
    }

    [Fact]
    public async Task UpdateConversationTitleAsync_Missing_NoThrow()
    {
        var h = NewHarness();
        await h.Service.Invoking(s => s.UpdateConversationTitleAsync(404, "x")).Should().NotThrowAsync();
    }

    [Fact]
    public async Task TogglePinAsync_FlipsState()
    {
        var h = NewHarness();
        var id = SeedConv(h, NewConv(isPinned: false));

        await h.Service.TogglePinAsync(id);
        using (var fresh = h.Fresh())
        {
            (await fresh.Conversations.FindAsync(id))!.IsPinned.Should().BeTrue();
        }

        await h.Service.TogglePinAsync(id);
        using (var fresh = h.Fresh())
        {
            (await fresh.Conversations.FindAsync(id))!.IsPinned.Should().BeFalse();
        }
    }

    [Fact]
    public async Task TogglePinAsync_Missing_NoThrow()
    {
        var h = NewHarness();
        await h.Service.Invoking(s => s.TogglePinAsync(404)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task ArchiveConversationAsync_SetsArchived()
    {
        var h = NewHarness();
        var id = SeedConv(h, NewConv());

        await h.Service.ArchiveConversationAsync(id);

        using var fresh = h.Fresh();
        (await fresh.Conversations.FindAsync(id))!.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task ArchiveConversationAsync_Missing_NoThrow()
    {
        var h = NewHarness();
        await h.Service.Invoking(s => s.ArchiveConversationAsync(404)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteConversationAsync_RemovesConversationAndCascadesMessages()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var c = NewConv();
            ctx.Conversations.Add(c);
            ctx.SaveChanges();
            id = c.Id;
            ctx.Messages.Add(NewMsg(id, sortOrder: 0));
            ctx.Messages.Add(NewMsg(id, sortOrder: 1));
        });

        await h.Service.DeleteConversationAsync(id);

        using var fresh = h.Fresh();
        (await fresh.Conversations.CountAsync()).Should().Be(0);
        (await fresh.Messages.CountAsync()).Should().Be(0); // cascade
    }

    [Fact]
    public async Task DeleteConversationAsync_Missing_NoThrow()
    {
        var h = NewHarness();
        await h.Service.Invoking(s => s.DeleteConversationAsync(404)).Should().NotThrowAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Messages: get / add
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetMessagesAsync_ReturnsOrderedBySortOrder()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var c = NewConv();
            ctx.Conversations.Add(c);
            ctx.SaveChanges();
            id = c.Id;
            ctx.Messages.Add(NewMsg(id, content: "b", sortOrder: 1));
            ctx.Messages.Add(NewMsg(id, content: "a", sortOrder: 0));
        });

        var messages = await h.Service.GetMessagesAsync(id);

        messages.Should().HaveCount(2);
        messages[0].Content.Should().Be("a");
    }

    [Fact]
    public async Task GetMessagesAsync_EmptyConversation_ReturnsEmpty()
    {
        var h = NewHarness();
        var id = SeedConv(h, NewConv());
        (await h.Service.GetMessagesAsync(id)).Should().BeEmpty();
    }

    [Fact]
    public async Task AddMessageAsync_FirstMessage_SetsSortOrderZeroAndUpdatesMetadata()
    {
        var h = NewHarness();
        var id = SeedConv(h, NewConv());

        await h.Service.AddMessageAsync(id, "user", "hi there", tokenCount: 12, generationTimeMs: 5.0);

        using var fresh = h.Fresh();
        var msg = await fresh.Messages.SingleAsync();
        msg.SortOrder.Should().Be(0);
        msg.TokenCount.Should().Be(12);
        var conv = await fresh.Conversations.FindAsync(id);
        conv!.MessageCount.Should().Be(1);
        conv.TokensUsed.Should().Be(12);

        h.Recall.Verify(r => r.RefreshMessageEmbeddingAsync(msg.Id, false, It.IsAny<CancellationToken>()), Times.Once);
        h.Summary.Verify(s => s.MarkConversationStaleAsync(id, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddMessageAsync_SecondMessage_IncrementsSortOrder()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var c = NewConv(messageCount: 1, tokensUsed: 5);
            ctx.Conversations.Add(c);
            ctx.SaveChanges();
            id = c.Id;
            ctx.Messages.Add(NewMsg(id, sortOrder: 0, tokenCount: 5));
        });

        await h.Service.AddMessageAsync(id, "assistant", "reply", tokenCount: 7);

        using var fresh = h.Fresh();
        var newMsg = await fresh.Messages.OrderByDescending(m => m.SortOrder).FirstAsync();
        newMsg.SortOrder.Should().Be(1);
        (await fresh.Conversations.FindAsync(id))!.TokensUsed.Should().Be(12);
    }

    [Fact]
    public async Task AddMessageAsync_NullTokenCount_DoesNotChangeTokensButCountsMessage()
    {
        var h = NewHarness();
        var id = SeedConv(h, NewConv());

        await h.Service.AddMessageAsync(id, "user", "no tokens", tokenCount: null);

        using var fresh = h.Fresh();
        var conv = await fresh.Conversations.FindAsync(id);
        conv!.MessageCount.Should().Be(1);
        conv.TokensUsed.Should().Be(0);
    }

    [Fact]
    public async Task AddMessageAsync_MissingConversation_ThrowsInvalidOperation()
    {
        var h = NewHarness();
        var act = () => h.Service.AddMessageAsync(404, "user", "orphan");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AddMessageAsync_CollaboratorsThrow_StillPersists()
    {
        var h = NewHarness();
        var id = SeedConv(h, NewConv());
        h.Recall.Setup(r => r.RefreshMessageEmbeddingAsync(It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("recall down"));
        h.Summary.Setup(s => s.MarkConversationStaleAsync(It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("summary down"));

        await h.Service.Invoking(s => s.AddMessageAsync(id, "user", "resilient")).Should().NotThrowAsync();

        using var fresh = h.Fresh();
        (await fresh.Messages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AddMessageAsync_NoCollaborators_PersistsWithoutHooks()
    {
        var h = NewHarness(withRecall: false, withSummary: false);
        var id = SeedConv(h, NewConv());

        await h.Service.AddMessageAsync(id, "user", "lonely");

        using var fresh = h.Fresh();
        (await fresh.Messages.CountAsync()).Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Messages: delete-last-assistant / delete / edit / truncate
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteLastAssistantMessageAsync_RemovesNewestAssistant()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var c = NewConv(messageCount: 3, tokensUsed: 30);
            ctx.Conversations.Add(c);
            ctx.SaveChanges();
            id = c.Id;
            ctx.Messages.Add(NewMsg(id, "user", "q", sortOrder: 0, tokenCount: 10));
            ctx.Messages.Add(NewMsg(id, "assistant", "old answer", sortOrder: 1, tokenCount: 10));
            ctx.Messages.Add(NewMsg(id, "assistant", "new answer", sortOrder: 2, tokenCount: 10));
        });

        await h.Service.DeleteLastAssistantMessageAsync(id);

        using var fresh = h.Fresh();
        (await fresh.Messages.AnyAsync(m => m.Content == "new answer")).Should().BeFalse();
        var conv = await fresh.Conversations.FindAsync(id);
        conv!.MessageCount.Should().Be(2);
        conv.TokensUsed.Should().Be(20);
    }

    [Fact]
    public async Task DeleteLastAssistantMessageAsync_NoAssistant_NoOp()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var c = NewConv(messageCount: 1);
            ctx.Conversations.Add(c);
            ctx.SaveChanges();
            id = c.Id;
            ctx.Messages.Add(NewMsg(id, "user", "only question", sortOrder: 0));
        });

        await h.Service.DeleteLastAssistantMessageAsync(id);

        using var fresh = h.Fresh();
        (await fresh.Messages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DeleteMessageAsync_Existing_RemovesAndDecrementsMetadata()
    {
        var h = NewHarness();
        long convId = 0;
        long msgId = 0;
        h.Seed(ctx =>
        {
            var c = NewConv(messageCount: 1, tokensUsed: 8);
            ctx.Conversations.Add(c);
            ctx.SaveChanges();
            convId = c.Id;
            var m = NewMsg(convId, tokenCount: 8);
            ctx.Messages.Add(m);
            ctx.SaveChanges();
            msgId = m.Id;
        });

        await h.Service.DeleteMessageAsync(msgId);

        using var fresh = h.Fresh();
        (await fresh.Messages.CountAsync()).Should().Be(0);
        var conv = await fresh.Conversations.FindAsync(convId);
        conv!.MessageCount.Should().Be(0);
        conv.TokensUsed.Should().Be(0);
    }

    [Fact]
    public async Task DeleteMessageAsync_Missing_NoThrow()
    {
        var h = NewHarness();
        await h.Service.Invoking(s => s.DeleteMessageAsync(404)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpdateMessageContentAsync_Existing_UpdatesAndClearsEmbedding()
    {
        var h = NewHarness();
        long convId = 0;
        long msgId = 0;
        h.Seed(ctx =>
        {
            var c = NewConv();
            ctx.Conversations.Add(c);
            ctx.SaveChanges();
            convId = c.Id;
            var m = NewMsg(convId, content: "old");
            m.Embedding = "[0.1,0.2]";
            m.EmbeddingModel = "test";
            m.EmbeddedAt = DateTime.UtcNow;
            ctx.Messages.Add(m);
            ctx.SaveChanges();
            msgId = m.Id;
        });

        await h.Service.UpdateMessageContentAsync(msgId, "new content");

        using var fresh = h.Fresh();
        var msg = await fresh.Messages.FindAsync(msgId);
        msg!.Content.Should().Be("new content");
        msg.Embedding.Should().BeNull();
        msg.EmbeddingModel.Should().BeNull();
        msg.EmbeddedAt.Should().BeNull();
        h.Recall.Verify(r => r.RefreshMessageEmbeddingAsync(msgId, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateMessageContentAsync_Missing_NoThrow()
    {
        var h = NewHarness();
        await h.Service.Invoking(s => s.UpdateMessageContentAsync(404, "x")).Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteMessagesAfterAsync_RemovesTailAndAdjustsMetadata()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var c = NewConv(messageCount: 4, tokensUsed: 40);
            ctx.Conversations.Add(c);
            ctx.SaveChanges();
            id = c.Id;
            for (int i = 0; i < 4; i++)
            {
                ctx.Messages.Add(NewMsg(id, content: $"m{i}", sortOrder: i, tokenCount: 10));
            }
        });

        await h.Service.DeleteMessagesAfterAsync(id, sortOrder: 1);

        using var fresh = h.Fresh();
        (await fresh.Messages.CountAsync()).Should().Be(2); // SortOrder 0 and 1 remain
        var conv = await fresh.Conversations.FindAsync(id);
        conv!.MessageCount.Should().Be(2);
        conv.TokensUsed.Should().Be(20);
    }

    [Fact]
    public async Task DeleteMessagesAfterAsync_NothingToDelete_NoOp()
    {
        var h = NewHarness();
        long id = 0;
        h.Seed(ctx =>
        {
            var c = NewConv(messageCount: 1);
            ctx.Conversations.Add(c);
            ctx.SaveChanges();
            id = c.Id;
            ctx.Messages.Add(NewMsg(id, sortOrder: 0));
        });

        await h.Service.DeleteMessagesAfterAsync(id, sortOrder: 5);

        using var fresh = h.Fresh();
        (await fresh.Messages.CountAsync()).Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Stats
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetConversationCountAsync_CountsNonArchived()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.Conversations.Add(NewConv());
            ctx.Conversations.Add(NewConv());
            ctx.Conversations.Add(NewConv(isArchived: true));
        });

        (await h.Service.GetConversationCountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task GetTotalTokensUsedAsync_SumsTokens()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.Conversations.Add(NewConv(tokensUsed: 100));
            ctx.Conversations.Add(NewConv(tokensUsed: 250));
        });

        (await h.Service.GetTotalTokensUsedAsync()).Should().Be(350);
    }

    [Fact]
    public async Task GetTotalTokensUsedAsync_Empty_ReturnsZero()
    {
        var h = NewHarness();
        (await h.Service.GetTotalTokensUsedAsync()).Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Folders
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SetConversationFolderAsync_SetsTrimmedFolder()
    {
        var h = NewHarness();
        var id = SeedConv(h, NewConv());

        await h.Service.SetConversationFolderAsync(id, "  Work  ");

        using var fresh = h.Fresh();
        (await fresh.Conversations.FindAsync(id))!.FolderName.Should().Be("Work");
    }

    [Fact]
    public async Task SetConversationFolderAsync_BlankClearsFolder()
    {
        var h = NewHarness();
        var id = SeedConv(h, NewConv(folderName: "Old"));

        await h.Service.SetConversationFolderAsync(id, "   ");

        using var fresh = h.Fresh();
        (await fresh.Conversations.FindAsync(id))!.FolderName.Should().BeNull();
    }

    [Fact]
    public async Task SetConversationFolderAsync_Missing_NoThrow()
    {
        var h = NewHarness();
        await h.Service.Invoking(s => s.SetConversationFolderAsync(404, "X")).Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetAllFolderNamesAsync_ReturnsDistinctSortedNonEmpty()
    {
        var h = NewHarness();
        h.Seed(ctx =>
        {
            ctx.Conversations.Add(NewConv(folderName: "Zeta"));
            ctx.Conversations.Add(NewConv(folderName: "Alpha"));
            ctx.Conversations.Add(NewConv(folderName: "Alpha"));
            ctx.Conversations.Add(NewConv(folderName: null));
            ctx.Conversations.Add(NewConv(folderName: ""));
        });

        var folders = await h.Service.GetAllFolderNamesAsync();

        folders.Should().Equal("Alpha", "Zeta");
    }

    [Fact]
    public async Task GetConversationsByFolderAsync_ReturnsFolderMembersPinnedFirst()
    {
        var h = NewHarness();
        var t0 = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        h.Seed(ctx =>
        {
            ctx.Conversations.Add(NewConv(title: "pinned", folderName: "Work", isPinned: true, updatedAt: t0));
            ctx.Conversations.Add(NewConv(title: "recent", folderName: "Work", updatedAt: t0.AddDays(3)));
            ctx.Conversations.Add(NewConv(title: "archived", folderName: "Work", isArchived: true, updatedAt: t0.AddDays(9)));
            ctx.Conversations.Add(NewConv(title: "other", folderName: "Home", updatedAt: t0.AddDays(5)));
        });

        var result = await h.Service.GetConversationsByFolderAsync("Work");

        result.Should().HaveCount(2); // excludes archived + other folder
        result[0].Title.Should().Be("pinned");
        result[1].Title.Should().Be("recent");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Tags
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AddTagToConversationAsync_NewAssignment_Creates()
    {
        var h = NewHarness();
        long convId = 0;
        long tagId = 0;
        h.Seed(ctx =>
        {
            var c = NewConv();
            var t = new TagEntity { Name = "important", CreatedAt = DateTime.UtcNow };
            ctx.Conversations.Add(c);
            ctx.Tags.Add(t);
            ctx.SaveChanges();
            convId = c.Id;
            tagId = t.Id;
        });

        await h.Service.AddTagToConversationAsync(convId, tagId);

        using var fresh = h.Fresh();
        (await fresh.ConversationTags.AnyAsync(ct => ct.ConversationId == convId && ct.TagId == tagId))
            .Should().BeTrue();
    }

    [Fact]
    public async Task AddTagToConversationAsync_AlreadyAssigned_NoDuplicate()
    {
        var h = NewHarness();
        long convId = 0;
        long tagId = 0;
        h.Seed(ctx =>
        {
            var c = NewConv();
            var t = new TagEntity { Name = "important", CreatedAt = DateTime.UtcNow };
            ctx.Conversations.Add(c);
            ctx.Tags.Add(t);
            ctx.SaveChanges();
            convId = c.Id;
            tagId = t.Id;
            ctx.ConversationTags.Add(new ConversationTagEntity { ConversationId = convId, TagId = tagId, AssignedAt = DateTime.UtcNow });
        });

        await h.Service.AddTagToConversationAsync(convId, tagId);

        using var fresh = h.Fresh();
        (await fresh.ConversationTags.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RemoveTagFromConversationAsync_Existing_Removes()
    {
        var h = NewHarness();
        long convId = 0;
        long tagId = 0;
        h.Seed(ctx =>
        {
            var c = NewConv();
            var t = new TagEntity { Name = "important", CreatedAt = DateTime.UtcNow };
            ctx.Conversations.Add(c);
            ctx.Tags.Add(t);
            ctx.SaveChanges();
            convId = c.Id;
            tagId = t.Id;
            ctx.ConversationTags.Add(new ConversationTagEntity { ConversationId = convId, TagId = tagId, AssignedAt = DateTime.UtcNow });
        });

        await h.Service.RemoveTagFromConversationAsync(convId, tagId);

        using var fresh = h.Fresh();
        (await fresh.ConversationTags.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RemoveTagFromConversationAsync_NotAssigned_NoThrow()
    {
        var h = NewHarness();
        await h.Service.Invoking(s => s.RemoveTagFromConversationAsync(1, 2)).Should().NotThrowAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Catch arms — every method rethrows on a disposed context
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AllMethods_DbDisposed_RethrowFromCatch()
    {
        var h = NewHarness();
        h.Db.Dispose(); // every query/command now faults inside its try → logged + rethrown

        var calls = new List<Func<Task>>
        {
            () => h.Service.CreateConversationAsync(),
            () => h.Service.GetConversationAsync(1),
            () => h.Service.GetAllConversationsAsync(),
            () => h.Service.GetRecentConversationsAsync(),
            () => h.Service.SearchConversationsAsync("x"),
            () => h.Service.UpdateConversationTitleAsync(1, "t"),
            () => h.Service.TogglePinAsync(1),
            () => h.Service.ArchiveConversationAsync(1),
            () => h.Service.DeleteConversationAsync(1),
            () => h.Service.GetMessagesAsync(1),
            () => h.Service.AddMessageAsync(1, "user", "c"),
            () => h.Service.DeleteLastAssistantMessageAsync(1),
            () => h.Service.DeleteMessageAsync(1),
            () => h.Service.UpdateMessageContentAsync(1, "c"),
            () => h.Service.DeleteMessagesAfterAsync(1, 0),
            () => h.Service.GetConversationCountAsync(),
            () => h.Service.GetTotalTokensUsedAsync(),
            () => h.Service.SetConversationFolderAsync(1, "f"),
            () => h.Service.GetAllFolderNamesAsync(),
            () => h.Service.AddTagToConversationAsync(1, 1),
            () => h.Service.RemoveTagFromConversationAsync(1, 1),
            () => h.Service.GetConversationsByFolderAsync("f"),
        };

        foreach (var call in calls)
        {
            await call.Should().ThrowAsync<Exception>();
        }
    }
}
