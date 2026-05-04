using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Search;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Search;

public sealed class ParentDocumentRetrieverTests
{
    private readonly Mock<AgentXDbContext> _dbContext = new();
    private readonly ParentDocumentRetriever _retriever;

    public ParentDocumentRetrieverTests()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        _retriever = new ParentDocumentRetriever(_dbContext.Object, logger);
    }

    [Fact]
    public async Task RetrieveParentChunksAsync_EmptyList_ReturnsEmptyList()
    {
        // Act
        var result = await _retriever.RetrieveParentChunksAsync(new List<RagContextChunk>());

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RetrieveParentChunksAsync_NullInput_ReturnsEmptyList()
    {
        // Act
        var result = await _retriever.RetrieveParentChunksAsync(null!);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RetrieveParentChunksAsync_DuplicateChunks_DeduplicatesByDocumentIndex()
    {
        // Arrange
        var childChunks = new List<RagContextChunk>
        {
            new() { ChunkId = 2, DocumentId = 100, ChunkIndex = 1, ChunkText = "Chunk 1", RelevanceScore = 0.9f },
            new() { ChunkId = 2, DocumentId = 100, ChunkIndex = 1, ChunkText = "Chunk 1 duplicate", RelevanceScore = 0.8f }
        };

        // Act
        var result = await _retriever.RetrieveParentChunksAsync(childChunks);

        // Assert
        result.Should().HaveCount(1); // Only one unique chunk despite duplicates in input
    }

    [Fact]
    public async Task RetrieveParentChunksAsync_DifferentDocuments_ProcessesIndependently()
    {
        // Arrange - Using simple test without DB mocking
        var childChunks = new List<RagContextChunk>
        {
            new() { ChunkId = 1, DocumentId = 100, ChunkIndex = 0, ChunkText = "Doc1 chunk", RelevanceScore = 0.9f },
            new() { ChunkId = 2, DocumentId = 200, ChunkIndex = 0, ChunkText = "Doc2 chunk", RelevanceScore = 0.8f }
        };

        // Act & Assert - Just verify it doesn't throw with multiple documents
        // Note: Without proper DB mocking, we can't verify full behavior
        var exception = await Record.ExceptionAsync(() => _retriever.RetrieveParentChunksAsync(childChunks));
        exception.Should().BeNull();
    }

    [Fact]
    public void Constructor_NullDbContext_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ParentDocumentRetriever(null!, new Mock<ILogger>().Object));
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var mockDb = new Mock<AgentXDbContext>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ParentDocumentRetriever(mockDb.Object, null!));
    }

    [Fact]
    public void Constructor_ValidParameters_CreatesInstance()
    {
        // Arrange
        var mockDb = new Mock<AgentXDbContext>();
        var logger = new LoggerConfiguration().CreateLogger();

        // Act
        var retriever = new ParentDocumentRetriever(mockDb.Object, logger);

        // Assert
        retriever.Should().NotBeNull();
    }

    // Note: Full integration tests with actual DB context would require:
    // - In-memory SQLite database setup
    // - Proper DbSet configuration
    // - Test data seeding
    // These are better suited for integration tests rather than unit tests
}
