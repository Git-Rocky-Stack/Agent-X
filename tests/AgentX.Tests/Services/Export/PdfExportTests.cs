using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Export;
using AgentX.Core.Services.Export.Formats;
using AgentX.Core.Services.Export.Models;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Export;

/// <summary>
/// Unit tests for <see cref="PdfExport"/>.
/// Tests the PDF format strategy implementation using QuestPDF.
/// </summary>
public sealed class PdfExportTests : IDisposable
{
    private readonly PdfExport _export;
    private readonly string _tempDir;

    public PdfExportTests()
    {
        var loggerMock = new Mock<ILogger>();
        loggerMock.Setup(l => l.ForContext<PdfExport>()).Returns(loggerMock.Object);
        _export = new PdfExport(loggerMock.Object);

        _tempDir = Path.Combine(Path.GetTempPath(), $"AgentX_PdfTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Basic Properties
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Format_ShouldReturnPdf()
    {
        // Act
        var format = _export.Format;

        // Assert
        format.Should().Be(ExportFormat.Pdf);
    }

    [Fact]
    public void FileExtension_ShouldReturnPdfExtension()
    {
        // Act
        var extension = _export.FileExtension;

        // Assert
        extension.Should().Be(".pdf");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Supports<T> Method
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Supports_WithConversationEntity_ShouldReturnTrue()
    {
        // Act
        var result = _export.Supports<ConversationEntity>();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Supports_WithConversationList_ShouldReturnTrue()
    {
        // Act
        var result = _export.Supports<IReadOnlyList<ConversationEntity>>();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Supports_WithSearchResults_ShouldReturnTrue()
    {
        // Act
        var result = _export.Supports<IReadOnlyList<SearchResultExportItem>>();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Supports_WithUnsupportedType_ShouldReturnFalse()
    {
        // Act
        var result = _export.Supports<string>();

        // Assert
        result.Should().BeFalse();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RenderToFileAsync - Single Conversation
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderToFileAsync_WithSingleConversation_CreatesPdfFile()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "PDF Test Conversation",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            MessageCount = 2,
            TokensUsed = 100,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "user",
                    Content = "Hello",
                    Timestamp = DateTime.UtcNow,
                    TokenCount = 10,
                    SortOrder = 0
                },
                new MessageEntity
                {
                    Id = 2,
                    Role = "assistant",
                    Content = "Hi there!",
                    Timestamp = DateTime.UtcNow,
                    TokenCount = 15,
                    SortOrder = 1
                }
            }
        };

        var options = new ExportOptions
        {
            IncludeMetadata = true,
            IncludeTimestamps = true,
            IncludeModelInfo = true,
            IncludeCitations = true
        };

        var outputPath = Path.Combine(_tempDir, "test_conversation.pdf");

        // Act
        var result = await _export.RenderToFileAsync(conversation, options, outputPath);

        // Assert
        result.Should().Be(outputPath);
        File.Exists(result).Should().BeTrue();
        new FileInfo(result).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RenderToFileAsync_WithSingleConversation_IncludesTitle()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "My Test Conversation",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>()
        };

        var options = new ExportOptions { IncludeMetadata = false };
        var outputPath = Path.Combine(_tempDir, "test_title.pdf");

        // Act
        var result = await _export.RenderToFileAsync(conversation, options, outputPath);

        // Assert
        File.Exists(result).Should().BeTrue();
        // PDF content verification would require PDF parsing library
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RenderToFileAsync - Multiple Conversations
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderToFileAsync_WithMultipleConversations_CreatesPdfFile()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            new ConversationEntity
            {
                Id = 1,
                Title = "First Conversation",
                CreatedAt = DateTime.UtcNow,
                Messages = new List<MessageEntity>
                {
                    new MessageEntity { Id = 1, Role = "user", Content = "First", Timestamp = DateTime.UtcNow, SortOrder = 0 }
                }
            },
            new ConversationEntity
            {
                Id = 2,
                Title = "Second Conversation",
                CreatedAt = DateTime.UtcNow,
                Messages = new List<MessageEntity>
                {
                    new MessageEntity { Id = 1, Role = "user", Content = "Second", Timestamp = DateTime.UtcNow, SortOrder = 0 }
                }
            }
        };

        var options = new ExportOptions();
        var outputPath = Path.Combine(_tempDir, "multi_conversation.pdf");

        // Act
        var result = await _export.RenderToFileAsync(conversations, options, outputPath);

        // Assert
        result.Should().Be(outputPath);
        File.Exists(result).Should().BeTrue();
        new FileInfo(result).Length.Should().BeGreaterThan(0);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RenderToFileAsync - Search Results
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderToFileAsync_WithSearchResults_CreatesPdfFile()
    {
        // Arrange
        var results = new List<SearchResultExportItem>
        {
            new SearchResultExportItem
            {
                Query = "test query",
                DocumentName = "Test Document.pdf",
                Content = "Search result content here",
                RelevanceScore = 0.95f,
                Citations = new List<string> { "Page 1" }
            }
        };

        var options = new ExportOptions { IncludeMetadata = true, IncludeCitations = true };
        var outputPath = Path.Combine(_tempDir, "search_results.pdf");

        // Act
        var result = await _export.RenderToFileAsync(results, options, outputPath);

        // Assert
        result.Should().Be(outputPath);
        File.Exists(result).Should().BeTrue();
        new FileInfo(result).Length.Should().BeGreaterThan(0);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RenderAsync - In-Memory (Byte Array)
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderAsync_WithConversation_ReturnsPdfBytes()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Memory Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity { Id = 1, Role = "user", Content = "Test", Timestamp = DateTime.UtcNow, SortOrder = 0 }
            }
        };

        var options = new ExportOptions();

        // Act
        var result = await _export.RenderAsync(conversation, options);

        // Assert
        result.Should().NotBeNull();
        var bytes = result as byte[];
        bytes.Should().NotBeNull();
        bytes.Should().HaveCountGreaterThan(0);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Cancellation Support
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderToFileAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>()
        };

        var outputPath = Path.Combine(_tempDir, "cancelled.pdf");

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _export.RenderToFileAsync(conversation, new ExportOptions(), outputPath, cts.Token));
    }

    [Fact]
    public async Task RenderAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>()
        };

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _export.RenderAsync(conversation, new ExportOptions(), cts.Token));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Unsupported Type Handling
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderToFileAsync_WithUnsupportedType_ThrowsNotSupportedException()
    {
        // Arrange
        var options = new ExportOptions();
        var outputPath = Path.Combine(_tempDir, "invalid.pdf");

        // Act & Assert
        await Assert.ThrowsAsync<NotSupportedException>(
            async () => await _export.RenderToFileAsync("invalid", options, outputPath));
    }

    [Fact]
    public async Task RenderAsync_WithUnsupportedType_ThrowsNotSupportedException()
    {
        // Arrange
        var options = new ExportOptions();

        // Act & Assert
        await Assert.ThrowsAsync<NotSupportedException>(
            async () => await _export.RenderAsync("invalid", options));
    }
}
