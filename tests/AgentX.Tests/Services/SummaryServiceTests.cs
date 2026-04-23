using AgentX.Core.AI;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Intelligence.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

public sealed class SummaryServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory = new();
    private readonly Mock<IAiService> _aiService = new();
    private readonly Mock<IHierarchicalSummaryService> _hierarchicalSummaryService = new();
    private readonly ILogger _logger = Log.ForContext<SummaryServiceTests>();

    public void Dispose()
    {
        _dbFactory.Dispose();
    }

    [Fact]
    public async Task SummarizeDocumentAsync_uses_layered_summary_result_and_preserves_chunk_order()
    {
        using var db = _dbFactory.CreateContext();
        var document = await SeedDocumentAsync(db);

        IReadOnlyList<string>? capturedSections = null;
        string? capturedTitle = null;

        _hierarchicalSummaryService
            .Setup(service => service.BuildSummaryAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, CancellationToken>((title, sections, _) =>
            {
                capturedTitle = title;
                capturedSections = sections.ToList();
            })
            .ReturnsAsync(new HierarchicalSummaryResult
            {
                DocumentTitle = document.FileName,
                DocumentSummary = "Layered summary output",
                KeyPoints = ["First point", "Second point"],
                TotalSections = 3,
                SectionsIncluded = 3
            });

        var sut = new SummaryService(
            _aiService.Object,
            db,
            _logger,
            _hierarchicalSummaryService.Object);

        var summary = await sut.SummarizeDocumentAsync(document.Id);

        summary.Should().Be("Layered summary output");
        capturedTitle.Should().Be(document.FileName);
        capturedSections.Should().Equal("First chunk", "Second chunk", "Third chunk");
    }

    [Fact]
    public async Task ExtractKeyPointsAsync_returns_key_points_from_layered_summary_result()
    {
        using var db = _dbFactory.CreateContext();
        var document = await SeedDocumentAsync(db);

        _hierarchicalSummaryService
            .Setup(service => service.BuildSummaryAsync(
                document.FileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HierarchicalSummaryResult
            {
                DocumentTitle = document.FileName,
                DocumentSummary = "Layered summary output",
                KeyPoints = ["Alpha insight", "Beta finding"],
                TotalSections = 3,
                SectionsIncluded = 3
            });

        var sut = new SummaryService(
            _aiService.Object,
            db,
            _logger,
            _hierarchicalSummaryService.Object);

        var keyPoints = await sut.ExtractKeyPointsAsync(document.Id);

        keyPoints.Should().Equal("Alpha insight", "Beta finding");
    }

    private static async Task<DocumentEntity> SeedDocumentAsync(AgentX.Core.Data.AgentXDbContext db)
    {
        var document = new DocumentEntity
        {
            FileName = "architecture.md",
            FilePath = @"C:\docs\architecture.md",
            FileType = "md",
            ContentHash = "hash-architecture",
            FileSizeBytes = 2048,
            ImportedAt = new DateTime(2026, 4, 22, 9, 0, 0, DateTimeKind.Utc),
            FileModifiedAt = new DateTime(2026, 4, 22, 9, 0, 0, DateTimeKind.Utc),
            IndexingStatus = "completed"
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync();

        db.DocumentChunks.AddRange(
            new DocumentChunkEntity
            {
                DocumentId = document.Id,
                ChunkIndex = 2,
                Content = "Third chunk",
                StartCharOffset = 20,
                EndCharOffset = 30,
                TokenCount = 3
            },
            new DocumentChunkEntity
            {
                DocumentId = document.Id,
                ChunkIndex = 0,
                Content = "First chunk",
                StartCharOffset = 0,
                EndCharOffset = 10,
                TokenCount = 3
            },
            new DocumentChunkEntity
            {
                DocumentId = document.Id,
                ChunkIndex = 1,
                Content = "Second chunk",
                StartCharOffset = 10,
                EndCharOffset = 20,
                TokenCount = 3
            });

        await db.SaveChangesAsync();
        return document;
    }
}
