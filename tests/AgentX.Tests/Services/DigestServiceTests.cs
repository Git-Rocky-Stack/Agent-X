using System.Text.Json;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Intelligence.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class DigestServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Mock<ILogger> _loggerMock;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DigestServiceTests()
    {
        _factory = new TestDbContextFactory();
        _loggerMock = new Mock<ILogger>();
        _loggerMock.Setup(l => l.ForContext<DigestService>()).Returns(_loggerMock.Object);
        _loggerMock.Setup(l => l.ForContext<DigestInsightService>()).Returns(_loggerMock.Object);
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task GenerateDigestAsync_IncludesPeriodOverPeriodTrendData()
    {
        var start = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc);
        await SeedDigestDataAsync(start, end);

        using var db = _factory.CreateContext();
        var digestInsightService = new DigestInsightService(db, _loggerMock.Object);
        var sut = new DigestService(db, _loggerMock.Object, digestInsightService);

        var report = await sut.GenerateDigestAsync(start, end);

        var searches = JsonSerializer.Deserialize<List<DigestSearchTrend>>(report.TopSearchesJson!, JsonOptions);
        var collections = JsonSerializer.Deserialize<List<DigestCollectionTrend>>(report.TopCollectionsJson!, JsonOptions);
        var fileTypes = JsonSerializer.Deserialize<List<DigestFileTypeTrend>>(report.FileTypeBreakdownJson!, JsonOptions);

        searches.Should().NotBeNullOrEmpty();
        searches!.Single(item => item.Query == "database migration")
            .Trend.Should().Be("up");
        searches.Single(item => item.Query == "new feature")
            .Trend.Should().Be("new");

        collections.Should().NotBeNullOrEmpty();
        collections!.Single(item => item.Name == "Engineering")
            .DeltaCount.Should().BeGreaterThan(0);

        fileTypes.Should().NotBeNullOrEmpty();
        fileTypes!.Single(item => item.Type == "pdf")
            .PreviousCount.Should().BeGreaterThan(0);
    }

    private async Task SeedDigestDataAsync(DateTime start, DateTime end)
    {
        var duration = end - start;
        var previousStart = start - duration;
        var previousMid = previousStart.AddDays(2);
        var currentMid = start.AddDays(2);

        using var db = _factory.CreateContext();

        var engineering = new CollectionEntity
        {
            Name = "Engineering",
            CreatedAt = previousStart,
            UpdatedAt = previousStart,
            SortOrder = 0
        };
        db.Collections.Add(engineering);

        var previousPdf = new DocumentEntity
        {
            FileName = "prev.pdf",
            FilePath = "prev.pdf",
            FileType = "pdf",
            ContentHash = "prev-hash",
            FileSizeBytes = 100,
            ImportedAt = previousMid,
            FileModifiedAt = previousMid
        };

        var currentPdf = new DocumentEntity
        {
            FileName = "current.pdf",
            FilePath = "current.pdf",
            FileType = "pdf",
            ContentHash = "current-hash",
            FileSizeBytes = 120,
            ImportedAt = currentMid,
            FileModifiedAt = currentMid
        };

        var currentDocx = new DocumentEntity
        {
            FileName = "current.docx",
            FilePath = "current.docx",
            FileType = "docx",
            ContentHash = "current-docx-hash",
            FileSizeBytes = 80,
            ImportedAt = currentMid,
            FileModifiedAt = currentMid
        };

        db.Documents.AddRange(previousPdf, currentPdf, currentDocx);
        await db.SaveChangesAsync();

        db.DocumentCollections.AddRange(
            new DocumentCollectionEntity
            {
                DocumentId = previousPdf.Id,
                CollectionId = engineering.Id,
                AddedAt = previousMid
            },
            new DocumentCollectionEntity
            {
                DocumentId = currentPdf.Id,
                CollectionId = engineering.Id,
                AddedAt = currentMid
            },
            new DocumentCollectionEntity
            {
                DocumentId = currentDocx.Id,
                CollectionId = engineering.Id,
                AddedAt = currentMid
            });

        db.SearchHistory.AddRange(
            new SearchHistoryEntity
            {
                Query = "database migration",
                ResultCount = 5,
                SearchedAt = previousMid
            },
            new SearchHistoryEntity
            {
                Query = "database migration",
                ResultCount = 6,
                SearchedAt = currentMid
            },
            new SearchHistoryEntity
            {
                Query = "database migration",
                ResultCount = 4,
                SearchedAt = currentMid.AddHours(1)
            },
            new SearchHistoryEntity
            {
                Query = "new feature",
                ResultCount = 3,
                SearchedAt = currentMid
            });

        var conversation = new ConversationEntity
        {
            Title = "Engineering Debug Thread",
            CreatedAt = currentMid,
            UpdatedAt = currentMid,
            MessageCount = 6,
            TokensUsed = 300
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        db.Messages.Add(new MessageEntity
        {
            ConversationId = conversation.Id,
            Role = "assistant",
            Content = "Debug summary",
            Timestamp = currentMid,
            TokenCount = 150,
            SortOrder = 1
        });

        await db.SaveChangesAsync();
    }
}
