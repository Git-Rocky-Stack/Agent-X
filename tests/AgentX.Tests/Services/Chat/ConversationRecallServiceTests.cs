using AgentX.Core.AI;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Chat;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Chat;

public sealed class ConversationRecallServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly Mock<IEmbeddingService> _embeddingService = new();
    private readonly ILogger _logger = Log.ForContext<ConversationRecallServiceTests>();

    public ConversationRecallServiceTests()
    {
        _embeddingService.SetupGet(service => service.ModelName).Returns("all-minilm");
    }

    public void Dispose()
    {
        _dbFactory.Dispose();
    }

    [Fact]
    public async Task RefreshConversationEmbeddingsAsync_persists_embeddings_for_recall_eligible_messages()
    {
        using var db = _dbFactory.CreateContext();
        var conversation = await SeedConversationAsync(db, "Recall seed");
        await SeedMessageAsync(db, conversation.Id, 0, "user", "Dashboard analytics planning");
        await SeedMessageAsync(db, conversation.Id, 1, "assistant", "We should surface recall health.");
        await SeedMessageAsync(db, conversation.Id, 2, "system", "System note");

        _embeddingService
            .Setup(service => service.EmbedBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                [1f, 0f, 0f],
                [0f, 1f, 0f]
            ]);

        var sut = new ConversationRecallService(db, _embeddingService.Object, _logger);

        var refreshed = await sut.RefreshConversationEmbeddingsAsync(conversation.Id);

        refreshed.Should().Be(2);

        var messages = await db.Messages
            .OrderBy(message => message.SortOrder)
            .ToListAsync();

        messages[0].Embedding.Should().NotBeNullOrWhiteSpace();
        messages[0].EmbeddingModel.Should().Be("all-minilm");
        messages[0].EmbeddedAt.Should().NotBeNull();
        messages[1].Embedding.Should().NotBeNullOrWhiteSpace();
        messages[2].Embedding.Should().BeNull();
    }

    [Fact]
    public async Task SearchRelevantMessagesAsync_returns_similarity_ranked_cross_conversation_results()
    {
        using var db = _dbFactory.CreateContext();

        var analytics = await SeedConversationAsync(db, "Analytics roadmap");
        var sync = await SeedConversationAsync(db, "Sync cleanup");

        await SeedMessageAsync(
            db,
            analytics.Id,
            0,
            "assistant",
            "The dashboard should surface analytics and recall health together.",
            embedding: "1.000000,0.000000,0.000000");
        await SeedMessageAsync(
            db,
            sync.Id,
            0,
            "assistant",
            "Sync scope needs a better collection picker.",
            embedding: "0.100000,0.950000,0.000000");

        _embeddingService
            .Setup(service => service.EmbedAsync("dashboard analytics recall", It.IsAny<CancellationToken>()))
            .ReturnsAsync([1f, 0f, 0f]);

        var sut = new ConversationRecallService(db, _embeddingService.Object, _logger);

        var results = await sut.SearchRelevantMessagesAsync(
            "dashboard analytics recall",
            maxResults: 4,
            minSimilarity: 0.6f);

        results.Should().ContainSingle();
        results[0].ConversationTitle.Should().Be("Analytics roadmap");
        results[0].Role.Should().Be("assistant");
        results[0].Similarity.Should().BeGreaterThan(0.9f);
        results[0].ContentPreview.Should().Contain("dashboard");
    }

    private static async Task<ConversationEntity> SeedConversationAsync(
        AgentX.Core.Data.AgentXDbContext db,
        string title)
    {
        var conversation = new ConversationEntity
        {
            Title = title,
            ModelId = "llama3.1:8b",
            CreatedAt = new DateTime(2026, 4, 23, 8, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 4, 23, 8, 0, 0, DateTimeKind.Utc),
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
            Timestamp = new DateTime(2026, 4, 23, 8, sortOrder + 1, 0, DateTimeKind.Utc),
            TokenCount = 24,
            Embedding = embedding,
            EmbeddingModel = embedding is null ? null : "all-minilm",
            EmbeddedAt = embedding is null ? null : new DateTime(2026, 4, 23, 8, sortOrder + 2, 0, DateTimeKind.Utc)
        });

        var conversation = await db.Conversations.SingleAsync(item => item.Id == conversationId);
        conversation.MessageCount += 1;
        conversation.UpdatedAt = new DateTime(2026, 4, 23, 8, sortOrder + 3, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();
    }
}
