using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Chat;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Chat;

public sealed class ConversationSummaryServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly Mock<IAiService> _aiService = new();
    private readonly ILogger _logger = Log.ForContext<ConversationSummaryServiceTests>();

    public ConversationSummaryServiceTests()
    {
        _aiService.SetupGet(service => service.IsConnected).Returns(true);
    }

    public void Dispose()
    {
        _dbFactory.Dispose();
    }

    [Fact]
    public async Task RefreshConversationSummaryAsync_uses_existing_snapshot_plus_unsummarized_tail()
    {
        using var db = _dbFactory.CreateContext();
        var conversation = await SeedConversationAsync(db, "Release planning");
        await SeedMessageAsync(db, conversation.Id, 0, "user", "We need a release plan.");
        await SeedMessageAsync(db, conversation.Id, 1, "assistant", "Let's define milestones first.");
        await SeedMessageAsync(db, conversation.Id, 2, "user", "Milestone one is the analytics rollout.");

        var firstSnapshot = new ConversationSummarySnapshotEntity
        {
            ConversationId = conversation.Id,
            SnapshotVersion = 1,
            SummaryText = "The conversation is defining a release plan.",
            PreviewText = "Release plan discussion.",
            KeyPointsJson = "[\"Release plan\"]",
            CoveredMessageCount = 2,
            GeneratedAt = new DateTime(2026, 4, 22, 9, 0, 0, DateTimeKind.Utc),
            SourceConversationUpdatedAt = conversation.UpdatedAt,
            IsIncremental = false
        };

        db.ConversationSummarySnapshots.Add(firstSnapshot);
        await db.SaveChangesAsync();

        db.ConversationSummaryStates.Add(new ConversationSummaryStateEntity
        {
            ConversationId = conversation.Id,
            LatestSnapshotId = firstSnapshot.Id,
            LatestSnapshotVersion = 1,
            LastCoveredMessageCount = 2,
            PendingMessageCount = 1,
            IsStale = true,
            LastRefreshRequestedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        string? capturedPrompt = null;
        _aiService
            .Setup(service => service.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ChatMessage>, string?, ChatOptions?, CancellationToken>((messages, _, _, _) =>
            {
                capturedPrompt = messages.Single().Content;
            })
            .ReturnsAsync("""
                {
                  "summary": "The conversation now includes milestone planning for the analytics rollout.",
                  "preview": "Release planning now includes an analytics milestone.",
                  "keyPoints": ["Milestone one is the analytics rollout.", "The team is defining milestones."]
                }
                """);

        var sut = new ConversationSummaryService(db, _aiService.Object, _logger);

        var created = await sut.RefreshConversationSummaryAsync(conversation.Id);

        created.Should().BeTrue();
        capturedPrompt.Should().Contain("EXISTING SUMMARY:");
        capturedPrompt.Should().Contain(firstSnapshot.SummaryText);
        capturedPrompt.Should().Contain("Milestone one is the analytics rollout.");
        capturedPrompt.Should().NotContain("We need a release plan.");

        var state = await db.ConversationSummaryStates
            .Include(item => item.LatestSnapshot)
            .SingleAsync(item => item.ConversationId == conversation.Id);

        state.IsStale.Should().BeFalse();
        state.PendingMessageCount.Should().Be(0);
        state.LastCoveredMessageCount.Should().Be(3);
        state.LatestSnapshotVersion.Should().Be(2);
        state.LatestSnapshot.Should().NotBeNull();
        state.LatestSnapshot!.IsIncremental.Should().BeTrue();
        state.LatestSnapshot.CoveredMessageCount.Should().Be(3);

        var snapshotCount = await db.ConversationSummarySnapshots.CountAsync();
        snapshotCount.Should().Be(2);
    }

    [Fact]
    public async Task RefreshConversationSummaryAsync_on_ai_failure_keeps_existing_snapshot_and_stale_state()
    {
        using var db = _dbFactory.CreateContext();
        var conversation = await SeedConversationAsync(db, "Research sync");
        await SeedMessageAsync(db, conversation.Id, 0, "user", "Summarize our research direction.");
        await SeedMessageAsync(db, conversation.Id, 1, "assistant", "We are focused on persistent memory.");

        var firstSnapshot = new ConversationSummarySnapshotEntity
        {
            ConversationId = conversation.Id,
            SnapshotVersion = 1,
            SummaryText = "Research is focused on persistent memory.",
            PreviewText = "Persistent memory research.",
            KeyPointsJson = "[\"Persistent memory\"]",
            CoveredMessageCount = 1,
            GeneratedAt = new DateTime(2026, 4, 22, 9, 30, 0, DateTimeKind.Utc),
            SourceConversationUpdatedAt = conversation.UpdatedAt,
            IsIncremental = false
        };

        db.ConversationSummarySnapshots.Add(firstSnapshot);
        await db.SaveChangesAsync();

        db.ConversationSummaryStates.Add(new ConversationSummaryStateEntity
        {
            ConversationId = conversation.Id,
            LatestSnapshotId = firstSnapshot.Id,
            LatestSnapshotVersion = 1,
            LastCoveredMessageCount = 1,
            PendingMessageCount = 1,
            IsStale = true,
            LastRefreshRequestedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        _aiService
            .Setup(service => service.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Provider unavailable"));

        var sut = new ConversationSummaryService(db, _aiService.Object, _logger);

        var created = await sut.RefreshConversationSummaryAsync(conversation.Id);

        created.Should().BeFalse();

        var state = await db.ConversationSummaryStates.SingleAsync(item => item.ConversationId == conversation.Id);
        state.IsStale.Should().BeTrue();
        state.LatestSnapshotId.Should().Be(firstSnapshot.Id);
        state.LastError.Should().Contain("Provider unavailable");
        state.ConsecutiveFailureCount.Should().Be(1);

        var snapshotCount = await db.ConversationSummarySnapshots.CountAsync();
        snapshotCount.Should().Be(1);
    }

    [Fact]
    public async Task GetConversationSummaryContextAsync_returns_summary_key_points_and_stale_note()
    {
        using var db = _dbFactory.CreateContext();
        var conversation = await SeedConversationAsync(db, "Context reuse");

        var snapshot = new ConversationSummarySnapshotEntity
        {
            ConversationId = conversation.Id,
            SnapshotVersion = 1,
            SummaryText = "The thread is focused on durable memory reuse in chat.",
            PreviewText = "Durable memory reuse.",
            KeyPointsJson = """["Use stored summary context.","Keep stale state visible."]""",
            CoveredMessageCount = 4,
            GeneratedAt = new DateTime(2026, 4, 22, 10, 0, 0, DateTimeKind.Utc),
            SourceConversationUpdatedAt = conversation.UpdatedAt,
            IsIncremental = false
        };

        db.ConversationSummarySnapshots.Add(snapshot);
        await db.SaveChangesAsync();

        db.ConversationSummaryStates.Add(new ConversationSummaryStateEntity
        {
            ConversationId = conversation.Id,
            LatestSnapshotId = snapshot.Id,
            LatestSnapshotVersion = 1,
            LastCoveredMessageCount = 4,
            PendingMessageCount = 2,
            IsStale = true,
            LastRefreshedAt = snapshot.GeneratedAt
        });
        await db.SaveChangesAsync();

        var sut = new ConversationSummaryService(db, _aiService.Object, _logger);

        var context = await sut.GetConversationSummaryContextAsync(conversation.Id);

        context.Should().Contain("[Durable Conversation Summary]");
        context.Should().Contain("durable memory reuse in chat");
        context.Should().Contain("Key points:");
        context.Should().Contain("Use stored summary context.");
        context.Should().Contain("Freshness note");
        context.Should().Contain("2 newer messages");
    }

    private static async Task<ConversationEntity> SeedConversationAsync(AgentX.Core.Data.AgentXDbContext db, string title)
    {
        var conversation = new ConversationEntity
        {
            Title = title,
            ModelId = "llama3.1:8b",
            CreatedAt = new DateTime(2026, 4, 22, 8, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 4, 22, 8, 0, 0, DateTimeKind.Utc),
            MessageCount = 0,
            TokensUsed = 0
        };

        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation;
    }

    private static async Task SeedMessageAsync(
        AgentX.Core.Data.AgentXDbContext db,
        long conversationId,
        int sortOrder,
        string role,
        string content)
    {
        db.Messages.Add(new MessageEntity
        {
            ConversationId = conversationId,
            Role = role,
            Content = content,
            SortOrder = sortOrder,
            Timestamp = new DateTime(2026, 4, 22, 8, sortOrder, 0, DateTimeKind.Utc),
            TokenCount = 20
        });

        var conversation = await db.Conversations.SingleAsync(item => item.Id == conversationId);
        conversation.MessageCount += 1;
        conversation.UpdatedAt = new DateTime(2026, 4, 22, 8, sortOrder + 1, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();
    }
}
