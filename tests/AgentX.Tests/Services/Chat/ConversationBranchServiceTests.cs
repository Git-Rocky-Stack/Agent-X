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
/// Behavioural coverage for <see cref="ConversationBranchService"/> — the EF-Core-backed engine for
/// conversation branching: forking a conversation at a message, querying direct branches / branch
/// trees / roots, merging selected messages between conversations, branch counting / existence
/// checks, and recursive branch deletion.
///
/// <para><b>Harness design.</b> The service is a straight EF orchestrator over a real
/// <see cref="AgentXDbContext"/> (in-memory SQLite via <see cref="TestDbContextFactory"/>, which
/// enforces foreign keys). Two structural facts from the model shape these tests:
/// <list type="bullet">
/// <item>The self-referencing <c>ParentConversation → Branches</c> relationship uses
/// <see cref="DeleteBehavior.Restrict"/> — so a non-recursive delete of a branch that still has
/// children is rejected by the database (<see cref="DbUpdateException"/>), and recursive deletes
/// must remove the deepest descendants first.</item>
/// <item><c>Messages → Conversation</c> uses <see cref="DeleteBehavior.Cascade"/> — so deleting a
/// branch also removes its messages.</item>
/// </list>
/// The injected <see cref="IConversationService"/> is null-guarded by the constructor but not
/// otherwise consumed by any method, so it is supplied as a bare mock. The logger flows through
/// <c>ILogger.ForContext&lt;T&gt;()</c>, so the harness supplies a real silent Serilog logger (a
/// loose mock's <c>ForContext</c> returns null, which the constructor treats as a missing
/// logger).</para>
/// </summary>
public sealed class ConversationBranchServiceTests : IDisposable
{
    private readonly List<BranchHarness> _harnesses = new();

    private BranchHarness NewHarness()
    {
        var h = new BranchHarness();
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

    // ─── Harness ────────────────────────────────────────────────────────────────

    private sealed class BranchHarness : IDisposable
    {
        public TestDbContextFactory Factory { get; } = new();
        public AgentXDbContext Db { get; }
        public Serilog.Core.Logger Logger { get; } = new LoggerConfiguration().CreateLogger();
        public Mock<IConversationService> ConversationService { get; } = new();
        public ConversationBranchService Service { get; }

        public BranchHarness()
        {
            Db = Factory.CreateContext();
            Service = new ConversationBranchService(Db, ConversationService.Object, Logger);
        }

        /// <summary>A fresh context over the same in-memory DB — use to read DB truth.</summary>
        public AgentXDbContext Fresh() => Factory.CreateContext();

        public void Seed(Action<AgentXDbContext> seed)
        {
            using var ctx = Factory.CreateContext();
            seed(ctx);
            ctx.SaveChanges();
        }

        public void Dispose()
        {
            Db.Dispose();
            Factory.Dispose();
            Logger.Dispose();
        }
    }

    // ─── Seed helpers ─────────────────────────────────────────────────────────────

    private static long SeedConv(
        BranchHarness h,
        string title = "Conversation",
        long? parentId = null,
        long? branchPoint = null,
        string? label = null,
        DateTime? createdAt = null,
        string? systemPrompt = null,
        string modelId = "test-model",
        int messageCount = 0,
        long tokensUsed = 0)
    {
        var now = createdAt ?? DateTime.UtcNow;
        var conv = new ConversationEntity
        {
            Title = title,
            SystemPrompt = systemPrompt,
            ModelId = modelId,
            CreatedAt = now,
            UpdatedAt = now,
            ParentConversationId = parentId,
            BranchPointMessageId = branchPoint,
            BranchLabel = label,
            MessageCount = messageCount,
            TokensUsed = tokensUsed,
        };
        h.Seed(ctx => ctx.Conversations.Add(conv));
        return conv.Id;
    }

    private static long SeedMsg(
        BranchHarness h,
        long convId,
        string role = "user",
        string content = "hello",
        int sortOrder = 0,
        int tokenCount = 0,
        double? generationMs = null,
        string? modelId = null,
        string? citationsJson = null)
    {
        var msg = new MessageEntity
        {
            ConversationId = convId,
            Role = role,
            Content = content,
            SortOrder = sortOrder,
            TokenCount = tokenCount,
            Timestamp = DateTime.UtcNow,
            GenerationTimeMs = generationMs,
            ModelId = modelId,
            CitationsJson = citationsJson,
        };
        h.Seed(ctx => ctx.Messages.Add(msg));
        return msg.Id;
    }

    /// <summary>Seeds a root conversation plus its messages; returns the conversation id and message ids.</summary>
    private static (long convId, long[] msgIds) SeedConvWithMessages(
        BranchHarness h,
        string title,
        params (string role, string content, int sort, int tokens)[] msgs)
    {
        var convId = SeedConv(h, title);
        var ids = new long[msgs.Length];
        for (var i = 0; i < msgs.Length; i++)
        {
            ids[i] = SeedMsg(h, convId, msgs[i].role, msgs[i].content, msgs[i].sort, msgs[i].tokens);
        }

        return (convId, ids);
    }

    private static CancellationToken Canceled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        return cts.Token;
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  Constructor guards
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Ctor_NullDb_Throws()
    {
        using var factory = new TestDbContextFactory();
        using var logger = new LoggerConfiguration().CreateLogger();
        var act = () => new ConversationBranchService(null!, Mock.Of<IConversationService>(), logger);
        act.Should().Throw<ArgumentNullException>().WithParameterName("db");
    }

    [Fact]
    public void Ctor_NullConversationService_Throws()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();
        using var logger = new LoggerConfiguration().CreateLogger();
        var act = () => new ConversationBranchService(db, null!, logger);
        act.Should().Throw<ArgumentNullException>().WithParameterName("conversationService");
    }

    [Fact]
    public void Ctor_NullLogger_Throws()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();
        var act = () => new ConversationBranchService(db, Mock.Of<IConversationService>(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Ctor_ValidArgs_Succeeds()
    {
        using var h = new BranchHarness();
        h.Service.Should().NotBeNull();
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  BranchAtMessageAsync
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BranchAtMessage_SourceConversationNotFound_Throws()
    {
        var h = NewHarness();

        var act = () => h.Service.BranchAtMessageAsync(999, 1);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*conversation 999 not found*");
    }

    [Fact]
    public async Task BranchAtMessage_MessageNotInConversation_Throws()
    {
        var h = NewHarness();
        var convId = SeedConv(h, "Source");
        SeedMsg(h, convId, sortOrder: 0);

        var act = () => h.Service.BranchAtMessageAsync(convId, 424242);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Message 424242 not found*");
    }

    [Fact]
    public async Task BranchAtMessage_MessageFromDifferentConversation_Throws()
    {
        var h = NewHarness();
        var sourceId = SeedConv(h, "Source");
        SeedMsg(h, sourceId, sortOrder: 0);

        var otherId = SeedConv(h, "Other");
        var otherMsgId = SeedMsg(h, otherId, sortOrder: 0);

        // The message exists, but belongs to a different conversation.
        var act = () => h.Service.BranchAtMessageAsync(sourceId, otherMsgId);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task BranchAtMessage_WithLabel_SetsTitleAndBranchMetadata()
    {
        var h = NewHarness();
        var (convId, msgIds) = SeedConvWithMessages(
            h,
            "Original",
            ("user", "Q1", 0, 10),
            ("assistant", "A1", 1, 20),
            ("user", "Q2", 2, 30));

        var branch = await h.Service.BranchAtMessageAsync(convId, msgIds[1], "Explore idea");

        branch.Title.Should().Be("Explore idea");
        branch.BranchLabel.Should().Be("Explore idea");
        branch.ParentConversationId.Should().Be(convId);
        branch.BranchPointMessageId.Should().Be(msgIds[1]);
        branch.IsPinned.Should().BeFalse();
        branch.IsArchived.Should().BeFalse();
        branch.Messages.Should().HaveCount(2); // messages with SortOrder <= 1
    }

    [Fact]
    public async Task BranchAtMessage_WithoutLabel_UsesDefaultTitle_AndNullLabel()
    {
        var h = NewHarness();
        var (convId, msgIds) = SeedConvWithMessages(h, "My Chat", ("user", "hi", 0, 5));

        var branch = await h.Service.BranchAtMessageAsync(convId, msgIds[0]);

        branch.Title.Should().Be("My Chat (Branch)");
        branch.BranchLabel.Should().BeNull();
    }

    [Fact]
    public async Task BranchAtMessage_WhitespaceLabel_UsesDefaultTitle_ButKeepsRawLabel()
    {
        var h = NewHarness();
        var (convId, msgIds) = SeedConvWithMessages(h, "My Chat", ("user", "hi", 0, 5));

        var branch = await h.Service.BranchAtMessageAsync(convId, msgIds[0], "   ");

        // Title falls back to default (whitespace is "no meaningful label")...
        branch.Title.Should().Be("My Chat (Branch)");
        // ...but the raw label value is persisted verbatim onto BranchLabel.
        branch.BranchLabel.Should().Be("   ");
    }

    [Fact]
    public async Task BranchAtMessage_AtFirstMessage_CopiesOnlyBranchPoint()
    {
        var h = NewHarness();
        var (convId, msgIds) = SeedConvWithMessages(
            h,
            "Chat",
            ("user", "first", 0, 7),
            ("assistant", "second", 1, 9));

        var branch = await h.Service.BranchAtMessageAsync(convId, msgIds[0]);

        branch.Messages.Should().HaveCount(1);
        branch.Messages.Single().Content.Should().Be("first");
        branch.MessageCount.Should().Be(1);
        branch.TokensUsed.Should().Be(7);
    }

    [Fact]
    public async Task BranchAtMessage_AtLastMessage_CopiesAllMessages()
    {
        var h = NewHarness();
        var (convId, msgIds) = SeedConvWithMessages(
            h,
            "Chat",
            ("user", "m0", 0, 1),
            ("assistant", "m1", 1, 2),
            ("user", "m2", 2, 3));

        var branch = await h.Service.BranchAtMessageAsync(convId, msgIds[2]);

        branch.Messages.Should().HaveCount(3);
        branch.MessageCount.Should().Be(3);
        branch.TokensUsed.Should().Be(6);
    }

    [Fact]
    public async Task BranchAtMessage_ResequencesSortOrder_AndCopiesFields()
    {
        var h = NewHarness();
        var convId = SeedConv(h, "Chat", systemPrompt: "sys", modelId: "gpt-x");
        SeedMsg(h, convId, role: "user", content: "u", sortOrder: 0, tokenCount: 4,
            generationMs: 12.5, modelId: "gpt-x", citationsJson: "[]");
        var m1 = SeedMsg(h, convId, role: "assistant", content: "a", sortOrder: 1, tokenCount: 6,
            generationMs: 33.0, modelId: "gpt-x", citationsJson: "[{\"u\":\"x\"}]");

        var branch = await h.Service.BranchAtMessageAsync(convId, m1);

        // Branch inherits the system prompt + model from the source.
        branch.SystemPrompt.Should().Be("sys");
        branch.ModelId.Should().Be("gpt-x");

        var copied = branch.Messages.OrderBy(m => m.SortOrder).ToList();
        copied.Should().HaveCount(2);
        copied[0].SortOrder.Should().Be(0);
        copied[1].SortOrder.Should().Be(1);

        var assistant = copied[1];
        assistant.Role.Should().Be("assistant");
        assistant.Content.Should().Be("a");
        assistant.TokenCount.Should().Be(6);
        assistant.GenerationTimeMs.Should().Be(33.0);
        assistant.ModelId.Should().Be("gpt-x");
        assistant.CitationsJson.Should().Be("[{\"u\":\"x\"}]");
        assistant.ConversationId.Should().Be(branch.Id);
    }

    [Fact]
    public async Task BranchAtMessage_UsesSortOrderNotInsertionOrder_ForBranchPoint()
    {
        var h = NewHarness();
        var convId = SeedConv(h, "Chat");
        // Insert out of SortOrder order; branch point is the logically-second message.
        var mSort2 = SeedMsg(h, convId, content: "sort2", sortOrder: 2, tokenCount: 100);
        var mSort0 = SeedMsg(h, convId, content: "sort0", sortOrder: 0, tokenCount: 1);
        var mSort1 = SeedMsg(h, convId, content: "sort1", sortOrder: 1, tokenCount: 10);

        var branch = await h.Service.BranchAtMessageAsync(convId, mSort1);

        // Only SortOrder 0 and 1 are copied (the SortOrder-2 message is excluded).
        branch.Messages.Should().HaveCount(2);
        branch.Messages.Select(m => m.Content).Should().BeEquivalentTo(new[] { "sort0", "sort1" });
        branch.TokensUsed.Should().Be(11);
        _ = (mSort2, mSort0);
    }

    [Fact]
    public async Task BranchAtMessage_PersistsBranchAndMessagesToDatabase()
    {
        var h = NewHarness();
        var (convId, msgIds) = SeedConvWithMessages(
            h, "Chat", ("user", "a", 0, 1), ("assistant", "b", 1, 2));

        var branch = await h.Service.BranchAtMessageAsync(convId, msgIds[1], "saved");

        await using var read = h.Fresh();
        var persisted = await read.Conversations
            .Include(c => c.Messages)
            .FirstAsync(c => c.Id == branch.Id);
        persisted.ParentConversationId.Should().Be(convId);
        persisted.Messages.Should().HaveCount(2);
    }

    [Fact]
    public async Task BranchAtMessage_CanceledToken_Throws()
    {
        var h = NewHarness();
        var (convId, msgIds) = SeedConvWithMessages(h, "Chat", ("user", "a", 0, 1));

        var act = () => h.Service.BranchAtMessageAsync(convId, msgIds[0], null, Canceled());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  GetBranchesAsync
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetBranches_ReturnsDirectChildren_OrderedByCreatedAtDescending()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");
        var older = SeedConv(h, "Older", parentId: rootId, createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = SeedConv(h, "Newer", parentId: rootId, createdAt: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var branches = await h.Service.GetBranchesAsync(rootId);

        branches.Should().HaveCount(2);
        branches[0].Id.Should().Be(newer);
        branches[1].Id.Should().Be(older);
    }

    [Fact]
    public async Task GetBranches_NoBranches_ReturnsEmpty()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");

        var branches = await h.Service.GetBranchesAsync(rootId);

        branches.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBranches_ExcludesGrandchildren()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");
        var childId = SeedConv(h, "Child", parentId: rootId);
        SeedConv(h, "Grandchild", parentId: childId);

        var branches = await h.Service.GetBranchesAsync(rootId);

        branches.Should().ContainSingle().Which.Id.Should().Be(childId);
    }

    [Fact]
    public async Task GetBranches_CanceledToken_Throws()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");

        var act = () => h.Service.GetBranchesAsync(rootId, Canceled());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  GetBranchTreeAsync
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetBranchTree_ConversationNotFound_Throws()
    {
        var h = NewHarness();

        var act = () => h.Service.GetBranchTreeAsync(12345);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Conversation 12345 not found*");
    }

    [Fact]
    public async Task GetBranchTree_LeafRoot_HasEmptyChildrenAndZeroTotal()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");

        var tree = await h.Service.GetBranchTreeAsync(rootId);

        tree.Conversation.Id.Should().Be(rootId);
        tree.Children.Should().BeEmpty();
        tree.TotalBranchCount.Should().Be(0);
        tree.BranchPointMessageId.Should().BeNull();
        tree.BranchLabel.Should().BeNull();
    }

    [Fact]
    public async Task GetBranchTree_RootWithChildren_BuildsNodesAndCountsTotal()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");
        SeedConv(h, "B1", parentId: rootId, branchPoint: 111, label: "first");
        SeedConv(h, "B2", parentId: rootId, branchPoint: 222, label: "second");

        var tree = await h.Service.GetBranchTreeAsync(rootId);

        tree.Children.Should().HaveCount(2);
        tree.TotalBranchCount.Should().Be(2);
        tree.Children.Should().OnlyContain(c => c.BranchLabel != null && c.BranchPointMessageId != null);
    }

    [Fact]
    public async Task GetBranchTree_NestedTree_ComputesRecursiveTotalAndStructure()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");
        var childId = SeedConv(h, "Child", parentId: rootId);
        SeedConv(h, "Grandchild", parentId: childId);

        var tree = await h.Service.GetBranchTreeAsync(rootId);

        tree.TotalBranchCount.Should().Be(2); // child + grandchild
        tree.Children.Should().ContainSingle();
        tree.Children[0].Conversation.Id.Should().Be(childId);
        tree.Children[0].Children.Should().ContainSingle();
    }

    [Fact]
    public async Task GetBranchTree_GivenABranch_RootsTreeAtTrueRoot()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");
        var childId = SeedConv(h, "Child", parentId: rootId);

        // Ask for the tree starting from the *branch*, not the root.
        var tree = await h.Service.GetBranchTreeAsync(childId);

        tree.Conversation.Id.Should().Be(rootId);
        tree.Children.Should().ContainSingle().Which.Conversation.Id.Should().Be(childId);
    }

    [Fact]
    public async Task GetBranchTree_ChildrenOrderedByCreatedAtDescending()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");
        var older = SeedConv(h, "Older", parentId: rootId, createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = SeedConv(h, "Newer", parentId: rootId, createdAt: new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));

        var tree = await h.Service.GetBranchTreeAsync(rootId);

        tree.Children[0].Conversation.Id.Should().Be(newer);
        tree.Children[1].Conversation.Id.Should().Be(older);
    }

    [Fact]
    public async Task GetBranchTree_CanceledToken_Throws()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");

        var act = () => h.Service.GetBranchTreeAsync(rootId, Canceled());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  GetRootConversationAsync
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetRootConversation_NotFound_Throws()
    {
        var h = NewHarness();

        var act = () => h.Service.GetRootConversationAsync(777);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*777 not found*");
    }

    [Fact]
    public async Task GetRootConversation_RootReturnsItself()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");

        var root = await h.Service.GetRootConversationAsync(rootId);

        root.Id.Should().Be(rootId);
    }

    [Fact]
    public async Task GetRootConversation_Branch_WalksUpToRoot()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");
        var childId = SeedConv(h, "Child", parentId: rootId);
        var grandchildId = SeedConv(h, "Grandchild", parentId: childId);

        var root = await h.Service.GetRootConversationAsync(grandchildId);

        root.Id.Should().Be(rootId);
    }

    [Fact]
    public async Task GetRootConversation_CircularReference_ThrowsMaxDepth()
    {
        var h = NewHarness();
        var selfId = SeedConv(h, "Loop");
        // Point the conversation's parent at itself — a circular reference.
        h.Seed(ctx =>
        {
            var self = ctx.Conversations.Find(selfId);
            self!.ParentConversationId = selfId;
        });

        var act = () => h.Service.GetRootConversationAsync(selfId);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Maximum branch depth*");
    }

    [Fact]
    public async Task GetRootConversation_OrphanedParent_TreatsCurrentAsRoot()
    {
        var h = NewHarness();
        var parentId = SeedConv(h, "Parent");
        var childId = SeedConv(h, "Child", parentId: parentId);

        // Orphan the child by deleting the parent row with FK enforcement disabled,
        // leaving a dangling ParentConversationId that no row satisfies.
        h.Seed(ctx =>
        {
            ctx.Database.ExecuteSqlRaw("PRAGMA foreign_keys=OFF");
            ctx.Database.ExecuteSqlRaw("DELETE FROM conversations WHERE Id={0}", parentId);
            ctx.Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON");
        });

        var root = await h.Service.GetRootConversationAsync(childId);

        // Parent lookup returns null → the walk stops and treats the child as the root.
        root.Id.Should().Be(childId);
    }

    [Fact]
    public async Task GetRootConversation_CanceledToken_Throws()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");

        var act = () => h.Service.GetRootConversationAsync(rootId, Canceled());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  MergeMessagesAsync
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MergeMessages_NullIds_NoOp()
    {
        var h = NewHarness();
        var sourceId = SeedConv(h, "Source");
        var targetId = SeedConv(h, "Target", messageCount: 0);

        await h.Service.MergeMessagesAsync(sourceId, null!, targetId);

        await using var read = h.Fresh();
        (await read.Messages.CountAsync(m => m.ConversationId == targetId)).Should().Be(0);
    }

    [Fact]
    public async Task MergeMessages_EmptyIds_NoOp()
    {
        var h = NewHarness();
        var sourceId = SeedConv(h, "Source");
        var targetId = SeedConv(h, "Target");

        await h.Service.MergeMessagesAsync(sourceId, Array.Empty<long>(), targetId);

        await using var read = h.Fresh();
        (await read.Messages.CountAsync(m => m.ConversationId == targetId)).Should().Be(0);
    }

    [Fact]
    public async Task MergeMessages_SourceNotFound_Throws()
    {
        var h = NewHarness();
        var targetId = SeedConv(h, "Target");

        var act = () => h.Service.MergeMessagesAsync(999, new long[] { 1 }, targetId);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Source conversation 999 not found*");
    }

    [Fact]
    public async Task MergeMessages_TargetNotFound_Throws()
    {
        var h = NewHarness();
        var sourceId = SeedConv(h, "Source");

        var act = () => h.Service.MergeMessagesAsync(sourceId, new long[] { 1 }, 999);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Target conversation 999 not found*");
    }

    [Fact]
    public async Task MergeMessages_NoMatchingMessages_NoOp()
    {
        var h = NewHarness();
        var sourceId = SeedConv(h, "Source");
        SeedMsg(h, sourceId, sortOrder: 0);
        var targetId = SeedConv(h, "Target");

        // The requested ids don't exist in the source conversation.
        await h.Service.MergeMessagesAsync(sourceId, new long[] { 987654 }, targetId);

        await using var read = h.Fresh();
        (await read.Messages.CountAsync(m => m.ConversationId == targetId)).Should().Be(0);
        (await read.Conversations.FirstAsync(c => c.Id == targetId)).MessageCount.Should().Be(0);
    }

    [Fact]
    public async Task MergeMessages_PartialMatch_MergesFoundMessagesOnly()
    {
        var h = NewHarness();
        var sourceId = SeedConv(h, "Source");
        var m0 = SeedMsg(h, sourceId, content: "keep", sortOrder: 0, tokenCount: 5);
        var targetId = SeedConv(h, "Target");

        // One valid id + one non-existent id.
        await h.Service.MergeMessagesAsync(sourceId, new long[] { m0, 424242 }, targetId);

        await using var read = h.Fresh();
        var merged = await read.Messages.Where(m => m.ConversationId == targetId).ToListAsync();
        merged.Should().ContainSingle().Which.Content.Should().Be("keep");
        (await read.Conversations.FirstAsync(c => c.Id == targetId)).MessageCount.Should().Be(1);
    }

    [Fact]
    public async Task MergeMessages_AppendsToEmptyTarget_StartingAtSortOrderZero()
    {
        var h = NewHarness();
        var sourceId = SeedConv(h, "Source");
        var m0 = SeedMsg(h, sourceId, content: "a", sortOrder: 0, tokenCount: 3);
        var m1 = SeedMsg(h, sourceId, content: "b", sortOrder: 1, tokenCount: 4);
        var targetId = SeedConv(h, "Target");

        await h.Service.MergeMessagesAsync(sourceId, new long[] { m0, m1 }, targetId);

        await using var read = h.Fresh();
        var merged = await read.Messages
            .Where(m => m.ConversationId == targetId)
            .OrderBy(m => m.SortOrder)
            .ToListAsync();
        merged.Select(m => m.SortOrder).Should().Equal(0, 1);
        merged.Select(m => m.Content).Should().Equal("a", "b");

        var target = await read.Conversations.FirstAsync(c => c.Id == targetId);
        target.MessageCount.Should().Be(2);
        target.TokensUsed.Should().Be(7);
    }

    [Fact]
    public async Task MergeMessages_AppendsAfterExistingMessages()
    {
        var h = NewHarness();
        var sourceId = SeedConv(h, "Source");
        var m0 = SeedMsg(h, sourceId, content: "src", sortOrder: 0, tokenCount: 2);

        var targetId = SeedConv(h, "Target", messageCount: 2, tokensUsed: 50);
        SeedMsg(h, targetId, content: "existing0", sortOrder: 0);
        SeedMsg(h, targetId, content: "existing1", sortOrder: 5);

        await h.Service.MergeMessagesAsync(sourceId, new long[] { m0 }, targetId);

        await using var read = h.Fresh();
        var merged = await read.Messages
            .Where(m => m.ConversationId == targetId)
            .OrderBy(m => m.SortOrder)
            .ToListAsync();
        // New message appended after the current max SortOrder (5) → 6.
        merged.Last().Content.Should().Be("src");
        merged.Last().SortOrder.Should().Be(6);

        var target = await read.Conversations.FirstAsync(c => c.Id == targetId);
        target.MessageCount.Should().Be(3);
        target.TokensUsed.Should().Be(52);
    }

    [Fact]
    public async Task MergeMessages_CopiesFields_WithFreshTimestamp_AndTargetConversationId()
    {
        var h = NewHarness();
        var sourceId = SeedConv(h, "Source");
        var m0 = SeedMsg(h, sourceId, role: "assistant", content: "insight", sortOrder: 0,
            tokenCount: 8, generationMs: 42.0, modelId: "m", citationsJson: "[1]");
        var targetId = SeedConv(h, "Target");
        var before = DateTime.UtcNow;

        await h.Service.MergeMessagesAsync(sourceId, new long[] { m0 }, targetId);

        await using var read = h.Fresh();
        var copied = await read.Messages.SingleAsync(m => m.ConversationId == targetId);
        copied.Role.Should().Be("assistant");
        copied.Content.Should().Be("insight");
        copied.TokenCount.Should().Be(8);
        copied.GenerationTimeMs.Should().Be(42.0);
        copied.ModelId.Should().Be("m");
        copied.CitationsJson.Should().Be("[1]");
        copied.ConversationId.Should().Be(targetId);
        copied.Timestamp.Should().BeOnOrAfter(before.AddSeconds(-1));
    }

    [Fact]
    public async Task MergeMessages_OrdersSourceMessagesBySortOrder()
    {
        var h = NewHarness();
        var sourceId = SeedConv(h, "Source");
        // Seed out of order; ids captured in insertion order.
        var late = SeedMsg(h, sourceId, content: "late", sortOrder: 2, tokenCount: 1);
        var early = SeedMsg(h, sourceId, content: "early", sortOrder: 0, tokenCount: 1);
        var targetId = SeedConv(h, "Target");

        await h.Service.MergeMessagesAsync(sourceId, new long[] { late, early }, targetId);

        await using var read = h.Fresh();
        var merged = await read.Messages
            .Where(m => m.ConversationId == targetId)
            .OrderBy(m => m.SortOrder)
            .ToListAsync();
        // Copied in source SortOrder order → "early" first, then "late".
        merged.Select(m => m.Content).Should().Equal("early", "late");
    }

    [Fact]
    public async Task MergeMessages_CanceledToken_Throws()
    {
        var h = NewHarness();
        var sourceId = SeedConv(h, "Source");
        var targetId = SeedConv(h, "Target");

        var act = () => h.Service.MergeMessagesAsync(sourceId, new long[] { 1 }, targetId, Canceled());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  GetBranchCountAsync
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetBranchCount_CountsDirectChildrenOnly()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");
        var childId = SeedConv(h, "C1", parentId: rootId);
        SeedConv(h, "C2", parentId: rootId);
        SeedConv(h, "Grandchild", parentId: childId); // must not be counted

        var count = await h.Service.GetBranchCountAsync(rootId);

        count.Should().Be(2);
    }

    [Fact]
    public async Task GetBranchCount_NoBranches_ReturnsZero()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");

        (await h.Service.GetBranchCountAsync(rootId)).Should().Be(0);
    }

    [Fact]
    public async Task GetBranchCount_CanceledToken_Throws()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");

        var act = () => h.Service.GetBranchCountAsync(rootId, Canceled());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  HasBranchesAtMessageAsync
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HasBranchesAtMessage_True_WhenABranchForksFromThatMessage()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");
        SeedConv(h, "Branch", parentId: rootId, branchPoint: 555);

        (await h.Service.HasBranchesAtMessageAsync(555)).Should().BeTrue();
    }

    [Fact]
    public async Task HasBranchesAtMessage_False_WhenNoBranchForksFromThatMessage()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");
        SeedConv(h, "Branch", parentId: rootId, branchPoint: 555);

        (await h.Service.HasBranchesAtMessageAsync(111)).Should().BeFalse();
    }

    [Fact]
    public async Task HasBranchesAtMessage_CanceledToken_Throws()
    {
        var h = NewHarness();

        var act = () => h.Service.HasBranchesAtMessageAsync(1, Canceled());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  DeleteBranchAsync
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteBranch_NotFound_NoOp()
    {
        var h = NewHarness();

        // Must not throw.
        await h.Service.DeleteBranchAsync(999);
    }

    [Fact]
    public async Task DeleteBranch_RootConversation_Throws()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root"); // no parent → a root, not a branch

        var act = () => h.Service.DeleteBranchAsync(rootId);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*root conversation*");
    }

    [Fact]
    public async Task DeleteBranch_LeafBranch_RemovesBranchAndCascadesMessages()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");
        var branchId = SeedConv(h, "Branch", parentId: rootId);
        SeedMsg(h, branchId, content: "m0", sortOrder: 0);
        SeedMsg(h, branchId, content: "m1", sortOrder: 1);

        await h.Service.DeleteBranchAsync(branchId);

        await using var read = h.Fresh();
        (await read.Conversations.AnyAsync(c => c.Id == branchId)).Should().BeFalse();
        (await read.Messages.AnyAsync(m => m.ConversationId == branchId)).Should().BeFalse();
        (await read.Conversations.AnyAsync(c => c.Id == rootId)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteBranch_Recursive_RemovesBranchAndAllDescendants()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");
        var branchId = SeedConv(h, "Branch", parentId: rootId);
        var childId = SeedConv(h, "SubBranch", parentId: branchId);
        var grandchildId = SeedConv(h, "SubSubBranch", parentId: childId);
        SeedMsg(h, grandchildId, content: "deep", sortOrder: 0);

        await h.Service.DeleteBranchAsync(branchId, recursive: true);

        await using var read = h.Fresh();
        (await read.Conversations.AnyAsync(c => c.Id == branchId)).Should().BeFalse();
        (await read.Conversations.AnyAsync(c => c.Id == childId)).Should().BeFalse();
        (await read.Conversations.AnyAsync(c => c.Id == grandchildId)).Should().BeFalse();
        (await read.Messages.AnyAsync(m => m.ConversationId == grandchildId)).Should().BeFalse();
        // The root above the deleted branch survives.
        (await read.Conversations.AnyAsync(c => c.Id == rootId)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteBranch_NonRecursive_LeafBranch_Removed()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");
        var branchId = SeedConv(h, "Branch", parentId: rootId);

        await h.Service.DeleteBranchAsync(branchId, recursive: false);

        await using var read = h.Fresh();
        (await read.Conversations.AnyAsync(c => c.Id == branchId)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteBranch_NonRecursive_WithChildren_ThrowsAndRollsBack()
    {
        var h = NewHarness();
        var rootId = SeedConv(h, "Root");
        var branchId = SeedConv(h, "Branch", parentId: rootId);
        var childId = SeedConv(h, "SubBranch", parentId: branchId);

        // Deleting the branch alone violates the Restrict FK held by its child.
        var act = () => h.Service.DeleteBranchAsync(branchId, recursive: false);

        await act.Should().ThrowAsync<DbUpdateException>();

        await using var read = h.Fresh();
        (await read.Conversations.AnyAsync(c => c.Id == branchId)).Should().BeTrue();
        (await read.Conversations.AnyAsync(c => c.Id == childId)).Should().BeTrue();
    }
}
