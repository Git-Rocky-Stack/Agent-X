using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Analytics;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class AnalyticsServiceConversationRecallTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly ILogger _logger = Log.ForContext<AnalyticsServiceConversationRecallTests>();

    public void Dispose()
    {
        _dbFactory.Dispose();
    }

    [Fact]
    public async Task GetConversationRecallOverviewAsync_returns_embedding_coverage_metrics()
    {
        using var db = _dbFactory.CreateContext();

        var alpha = await SeedConversationAsync(db, "Alpha");
        var beta = await SeedConversationAsync(db, "Beta");

        await SeedMessageAsync(db, alpha.Id, 0, "user", "Alpha user", embedding: "1.000000,0.000000");
        await SeedMessageAsync(db, alpha.Id, 1, "assistant", "Alpha assistant", embedding: "0.000000,1.000000");
        await SeedMessageAsync(db, beta.Id, 0, "assistant", "Beta assistant");
        await SeedMessageAsync(db, beta.Id, 1, "system", "System event");

        var sut = new AnalyticsService(db, _logger);

        var overview = await sut.GetConversationRecallOverviewAsync();

        overview.EmbeddedMessages.Should().Be(2);
        overview.PendingMessageEmbeddings.Should().Be(1);
        overview.RecallReadyConversations.Should().Be(1);
        overview.LastEmbeddedAt.Should().NotBeNull();
    }

    private static async Task<ConversationEntity> SeedConversationAsync(
        AgentX.Core.Data.AgentXDbContext db,
        string title)
    {
        var conversation = new ConversationEntity
        {
            Title = title,
            ModelId = "llama3.1:8b",
            CreatedAt = new DateTime(2026, 4, 23, 9, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 4, 23, 9, 0, 0, DateTimeKind.Utc),
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
        string content,
        string? embedding = null)
    {
        db.Messages.Add(new MessageEntity
        {
            ConversationId = conversationId,
            Role = role,
            Content = content,
            SortOrder = sortOrder,
            Timestamp = new DateTime(2026, 4, 23, 9, sortOrder + 1, 0, DateTimeKind.Utc),
            TokenCount = 12,
            Embedding = embedding,
            EmbeddingModel = embedding is null ? null : "all-minilm",
            EmbeddedAt = embedding is null ? null : new DateTime(2026, 4, 23, 9, sortOrder + 2, 0, DateTimeKind.Utc)
        });

        var conversation = await db.Conversations.SingleAsync(item => item.Id == conversationId);
        conversation.MessageCount += 1;
        conversation.UpdatedAt = new DateTime(2026, 4, 23, 9, sortOrder + 3, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();
    }
}
