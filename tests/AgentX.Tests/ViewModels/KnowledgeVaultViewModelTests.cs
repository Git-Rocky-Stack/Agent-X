using AgentX.App.ViewModels;
using AgentX.Core.AI;
using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Indexing;
using AgentX.Core.Services.Tagging;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class KnowledgeVaultViewModelTests
{
    private readonly Mock<IDocumentService> _documentService = new();
    private readonly Mock<IIndexingService> _indexingService = new();
    private readonly Mock<IAiService> _aiService = new();
    private readonly Mock<IAutoTagService> _autoTagService = new();
    private readonly Mock<ICollectionService> _collectionService = new();

    [Fact]
    public async Task InitializeAsync_batch_loads_tags_for_documents()
    {
        _documentService
            .Setup(service => service.GetAllDocumentsAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<long?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                CreateDocument(1, "alpha.md"),
                CreateDocument(2, "beta.pdf"),
            ]);

        _documentService.Setup(service => service.GetTotalDocumentCountAsync()).ReturnsAsync(2L);
        _documentService.Setup(service => service.GetTotalStorageBytesAsync()).ReturnsAsync(3_072L);

        _indexingService.Setup(service => service.GetQueueLengthAsync()).ReturnsAsync(1);
        _indexingService.SetupGet(service => service.IsProcessing).Returns(false);

        _autoTagService.Setup(service => service.GetTagsForDocumentsAsync(
                It.Is<IReadOnlyList<long>>(ids => ids.Count == 2 && ids[0] == 1L && ids[1] == 2L)))
            .ReturnsAsync(new Dictionary<long, IReadOnlyList<TagEntity>>
            {
                [1] =
                [
                    new TagEntity { Id = 11, Name = "research" }
                ],
                [2] =
                [
                    new TagEntity { Id = 12, Name = "policy" },
                    new TagEntity { Id = 13, Name = "urgent" }
                ]
            });

        _autoTagService.Setup(service => service.GetAllTagsAsync())
            .ReturnsAsync(
            [
                new TagEntity { Id = 11, Name = "research", ColorHex = "#111111" },
                new TagEntity { Id = 12, Name = "policy", ColorHex = "#222222" },
                new TagEntity { Id = 13, Name = "urgent", ColorHex = "#333333" }
            ]);

        _collectionService.Setup(service => service.GetAllCollectionsAsync())
            .ReturnsAsync(Array.Empty<CollectionEntity>());

        var viewModel = new KnowledgeVaultViewModel(
            _documentService.Object,
            _indexingService.Object,
            _aiService.Object,
            _autoTagService.Object,
            _collectionService.Object);

        await viewModel.InitializeAsync();

        _autoTagService.Verify(
            service => service.GetTagsForDocumentsAsync(It.IsAny<IReadOnlyList<long>>()),
            Times.Once);
        _autoTagService.Verify(
            service => service.GetTagsForDocumentAsync(It.IsAny<long>()),
            Times.Never);

        viewModel.Documents.Should().HaveCount(2);
        viewModel.Documents[0].Tags.Should().Equal("research");
        viewModel.Documents[1].Tags.Should().Equal("policy", "urgent");
        viewModel.AllTags.Should().HaveCount(3);
        viewModel.AllTags.Single(tag => tag.Name == "research").DocumentCount.Should().Be(1);
        viewModel.AllTags.Single(tag => tag.Name == "policy").DocumentCount.Should().Be(1);
        viewModel.AllTags.Single(tag => tag.Name == "urgent").DocumentCount.Should().Be(1);
    }

    private static DocumentEntity CreateDocument(long id, string fileName)
    {
        return new DocumentEntity
        {
            Id = id,
            FileName = fileName,
            FilePath = $@"C:\docs\{fileName}",
            FileType = Path.GetExtension(fileName).TrimStart('.'),
            ContentHash = $"hash-{id}",
            FileSizeBytes = 1_024,
            ImportedAt = new DateTime(2026, 4, 22, 8, 0, 0, DateTimeKind.Utc),
            FileModifiedAt = new DateTime(2026, 4, 22, 8, 0, 0, DateTimeKind.Utc),
            IndexingStatus = "completed",
            WordCount = 120,
            PageCount = 1
        };
    }
}
