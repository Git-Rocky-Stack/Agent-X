using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Export;
using AgentX.Core.Services.Export.Formatters;
using AgentX.Core.Services.Export.Models;
using AgentX.Core.Services.Settings;
using AgentX.Core.Services.Chat;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Export;

/// <summary>
/// Unit tests for <see cref="ExportService"/>.
/// Tests the thin orchestrator that delegates formatting to <see cref="IExportFormatter"/>
/// implementations while handling file I/O and special-case exports
/// (search results, collections) inline. Every export format is unconditionally
/// available — there is no license gating.
/// </summary>
public sealed class ExportServiceTests : IDisposable
{
    private readonly Mock<IConversationService> _conversationServiceMock;
    private readonly Mock<IDocumentService> _documentServiceMock;
    private readonly Mock<ICollectionService> _collectionServiceMock;
    private readonly Mock<ISettingsService> _settingsServiceMock;
    private readonly Mock<ILogger> _loggerMock;
    private readonly string _tempExportDir;

    public ExportServiceTests()
    {
        _conversationServiceMock = new Mock<IConversationService>();
        _documentServiceMock = new Mock<IDocumentService>();
        _collectionServiceMock = new Mock<ICollectionService>();
        _settingsServiceMock = new Mock<ISettingsService>();
        _loggerMock = new Mock<ILogger>();

        _loggerMock.Setup(l => l.ForContext<ExportService>()).Returns(_loggerMock.Object);

        // Create temp directory for export tests
        _tempExportDir = Path.Combine(Path.GetTempPath(), $"AgentX_ExportTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempExportDir);

        _settingsServiceMock
            .Setup(s => s.GetSettingsAsync())
            .ReturnsAsync(new AppSettings { StoragePath = _tempExportDir });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempExportDir))
        {
            try
            {
                Directory.Delete(_tempExportDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Creates all 8 formatters for testing. Each formatter is a real instance
    /// so the orchestrator tests exercise the full formatter pipeline.
    /// </summary>
    private static IEnumerable<IExportFormatter> CreateFormatters() =>
    [
        new MarkdownFormatter(),
        new PlainTextFormatter(),
        new CsvFormatter(),
        new HtmlFormatter(),
        new JsonFormatter(),
        new PdfFormatter(),
        new DocxFormatter(),
        new PptxFormatter(),
    ];

    private ExportService CreateService() => new(
        _conversationServiceMock.Object,
        _documentServiceMock.Object,
        _collectionServiceMock.Object,
        _settingsServiceMock.Object,
        _loggerMock.Object,
        CreateFormatters()
    );

    // ====================================================================
    //  ExportConversationAsync - Basic Functionality
    // ====================================================================

    [Fact]
    public async Task ExportConversationAsync_WithValidData_ReturnsSuccessResult()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test Conversation",
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

        _conversationServiceMock
            .Setup(s => s.GetConversationAsync(1))
            .ReturnsAsync(conversation);

        var sut = CreateService();
        var options = new ExportOptions
        {
            Format = ExportFormat.Json,
            OutputPath = Path.Combine(_tempExportDir, "test_export.json")
        };

        // Act
        var result = await sut.ExportConversationAsync(1, options);

        // Assert
        result.Success.Should().BeTrue();
        result.FilePath.Should().NotBeNullOrEmpty();
        result.FileSize.Should().BeGreaterThan(0);
        File.Exists(result.FilePath).Should().BeTrue();
    }

    [Fact]
    public async Task ExportConversationAsync_WithNotFoundConversation_ReturnsFailureResult()
    {
        // Arrange
        _conversationServiceMock
            .Setup(s => s.GetConversationAsync(999))
            .ReturnsAsync((ConversationEntity?)null);

        var sut = CreateService();
        var options = new ExportOptions { Format = ExportFormat.Json };

        // Act
        var result = await sut.ExportConversationAsync(999, options);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task ExportConversationAsync_MarkdownFormat_CreatesMarkdownFile()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Markdown Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "user",
                    Content = "Hello",
                    Timestamp = DateTime.UtcNow,
                    SortOrder = 0
                }
            }
        };

        _conversationServiceMock
            .Setup(s => s.GetConversationAsync(1))
            .ReturnsAsync(conversation);

        var sut = CreateService();
        var outputPath = Path.Combine(_tempExportDir, "test.md");
        var options = new ExportOptions
        {
            Format = ExportFormat.Markdown,
            OutputPath = outputPath
        };

        // Act
        var result = await sut.ExportConversationAsync(1, options);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(outputPath).Should().BeTrue();
        var content = await File.ReadAllTextAsync(outputPath);
        content.Should().Contain("# Markdown Test");
    }

    [Fact]
    public async Task ExportTextArtifactAsync_WithMarkdownFormat_CreatesMarkdownFile()
    {
        var sut = CreateService();
        var outputPath = Path.Combine(_tempExportDir, "workflow-result.md");
        var artifact = new TextArtifactExportItem
        {
            Title = "Workflow Result",
            Content = "final draft",
            Metadata = new Dictionary<string, string>
            {
                ["Workflow"] = "Document Review",
                ["Context"] = "Showing latest execution result"
            }
        };
        var options = new ExportOptions
        {
            Format = ExportFormat.Markdown,
            OutputPath = outputPath,
            IncludeMetadata = true
        };

        var result = await sut.ExportTextArtifactAsync(artifact, options);

        result.Success.Should().BeTrue();
        File.Exists(outputPath).Should().BeTrue();
        var content = await File.ReadAllTextAsync(outputPath);
        content.Should().Contain("# Workflow Result");
        content.Should().Contain("**Workflow:** Document Review");
        content.Should().Contain("final draft");
    }

    [Fact]
    public async Task ExportTextArtifactAsync_WithUnsupportedPdfFormat_ReturnsFailureResult()
    {
        var sut = CreateService();
        var artifact = new TextArtifactExportItem
        {
            Title = "Workflow Result",
            Content = "final draft"
        };
        var options = new ExportOptions
        {
            Format = ExportFormat.Pdf,
            OutputPath = Path.Combine(_tempExportDir, "workflow-result.pdf")
        };

        var result = await sut.ExportTextArtifactAsync(artifact, options);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unsupported export format");
    }

    [Fact]
    public async Task ExportConversationAsync_HtmlFormat_CreatesHtmlFile()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "HTML Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "user",
                    Content = "Hello",
                    Timestamp = DateTime.UtcNow,
                    SortOrder = 0
                }
            }
        };

        _conversationServiceMock
            .Setup(s => s.GetConversationAsync(1))
            .ReturnsAsync(conversation);

        var sut = CreateService();
        var outputPath = Path.Combine(_tempExportDir, "test.html");
        var options = new ExportOptions
        {
            Format = ExportFormat.Html,
            OutputPath = outputPath
        };

        // Act
        var result = await sut.ExportConversationAsync(1, options);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(outputPath).Should().BeTrue();
        var content = await File.ReadAllTextAsync(outputPath);
        content.Should().Contain("<!DOCTYPE html>");
        content.Should().Contain("HTML Test");
    }

    [Fact]
    public async Task ExportConversationAsync_PlainTextFormat_CreatesTxtFile()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Plain Text Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "user",
                    Content = "Hello",
                    Timestamp = DateTime.UtcNow,
                    SortOrder = 0
                }
            }
        };

        _conversationServiceMock
            .Setup(s => s.GetConversationAsync(1))
            .ReturnsAsync(conversation);

        var sut = CreateService();
        var outputPath = Path.Combine(_tempExportDir, "test.txt");
        var options = new ExportOptions
        {
            Format = ExportFormat.PlainText,
            OutputPath = outputPath
        };

        // Act
        var result = await sut.ExportConversationAsync(1, options);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(outputPath).Should().BeTrue();
        var content = await File.ReadAllTextAsync(outputPath);
        content.Should().Contain("Plain Text Test");
    }

    [Fact]
    public async Task ExportConversationAsync_CsvFormat_CreatesCsvFile()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "CSV Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "user",
                    Content = "Hello",
                    Timestamp = DateTime.UtcNow,
                    TokenCount = 10,
                    ModelId = "test-model",
                    SortOrder = 0
                }
            }
        };

        _conversationServiceMock
            .Setup(s => s.GetConversationAsync(1))
            .ReturnsAsync(conversation);

        var sut = CreateService();
        var outputPath = Path.Combine(_tempExportDir, "test.csv");
        var options = new ExportOptions
        {
            Format = ExportFormat.Csv,
            OutputPath = outputPath
        };

        // Act
        var result = await sut.ExportConversationAsync(1, options);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(outputPath).Should().BeTrue();
        var content = await File.ReadAllTextAsync(outputPath);
        content.Should().StartWith("Role,Content,Timestamp,Model,Tokens");
        content.Should().Contain("user");
        content.Should().Contain("Hello");
    }

    [Fact]
    public async Task ExportConversationAsync_UnsupportedFormat_ReturnsFailureResult()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>()
        };

        _conversationServiceMock
            .Setup(s => s.GetConversationAsync(1))
            .ReturnsAsync(conversation);

        var sut = CreateService();
        var options = new ExportOptions
        {
            Format = (ExportFormat)999 // Invalid format
        };

        // Act
        var result = await sut.ExportConversationAsync(1, options);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unsupported export format");
    }

    // ====================================================================
    //  Export Always Allowed (no license gating)
    // ====================================================================

    [Theory]
    [InlineData(ExportFormat.Markdown, "always_md.md")]
    [InlineData(ExportFormat.Html, "always_html.html")]
    [InlineData(ExportFormat.Pdf, "always_pdf.pdf")]
    public async Task ExportConversationAsync_FormerlyGatedFormats_AlwaysSucceed(ExportFormat format, string fileName)
    {
        // Previously PDF/Markdown/HTML were gated behind a paid license tier. Agent-X
        // is now free and open-source: every export format must succeed for everyone.
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Open Source Export",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity { Id = 1, Role = "user", Content = "Test", Timestamp = DateTime.UtcNow, SortOrder = 0 }
            }
        };

        _conversationServiceMock
            .Setup(s => s.GetConversationAsync(1))
            .ReturnsAsync(conversation);

        var sut = CreateService();
        var outputPath = Path.Combine(_tempExportDir, fileName);
        var options = new ExportOptions { Format = format, OutputPath = outputPath };

        var result = await sut.ExportConversationAsync(1, options);

        result.Success.Should().BeTrue();
        File.Exists(outputPath).Should().BeTrue();
    }

    [Fact]
    public async Task ExportConversationsAsync_HtmlFormat_AlwaysSucceeds()
    {
        // Batch HTML export was formerly gated; it must now succeed unconditionally.
        var conversations = new List<ConversationEntity>
        {
            new ConversationEntity { Id = 1, Title = "First", CreatedAt = DateTime.UtcNow, Messages = new List<MessageEntity>() },
            new ConversationEntity { Id = 2, Title = "Second", CreatedAt = DateTime.UtcNow, Messages = new List<MessageEntity>() }
        };
        _conversationServiceMock.Setup(s => s.GetConversationAsync(1)).ReturnsAsync(conversations[0]);
        _conversationServiceMock.Setup(s => s.GetConversationAsync(2)).ReturnsAsync(conversations[1]);

        var sut = CreateService();
        var options = new ExportOptions
        {
            Format = ExportFormat.Html,
            OutputPath = Path.Combine(_tempExportDir, "always_batch.html")
        };

        var result = await sut.ExportConversationsAsync(new[] { 1L, 2L }, options);

        result.Success.Should().BeTrue();
        File.Exists(result.FilePath).Should().BeTrue();
    }

    [Fact]
    public async Task ExportSearchResultsAsync_MarkdownFormat_AlwaysSucceeds()
    {
        // Markdown search-result export was formerly gated behind a paid tier; it must
        // now succeed unconditionally. (PDF/HTML are not supported for search results
        // by design — a format-capability limit unrelated to licensing.)
        var results = new List<SearchResultExportItem>
        {
            new SearchResultExportItem { Query = "test", Content = "Test", DocumentName = "Test.pdf" }
        };

        var sut = CreateService();
        var options = new ExportOptions
        {
            Format = ExportFormat.Markdown,
            OutputPath = Path.Combine(_tempExportDir, "always_search.md")
        };

        var result = await sut.ExportSearchResultsAsync("test", results, options);

        result.Success.Should().BeTrue();
        File.Exists(result.FilePath).Should().BeTrue();
    }

    // ====================================================================
    //  ExportConversationsAsync - Batch Export
    // ====================================================================

    [Fact]
    public async Task ExportConversationsAsync_WithEmptyList_ReturnsFailureResult()
    {
        // Arrange
        var sut = CreateService();
        var options = new ExportOptions { Format = ExportFormat.Json };

        // Act
        var result = await sut.ExportConversationsAsync(Array.Empty<long>(), options);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No conversation IDs");
    }

    [Fact]
    public async Task ExportConversationsAsync_WithValidIds_ReturnsSuccessResult()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            new ConversationEntity
            {
                Id = 1,
                Title = "First",
                CreatedAt = DateTime.UtcNow,
                Messages = new List<MessageEntity>()
            },
            new ConversationEntity
            {
                Id = 2,
                Title = "Second",
                CreatedAt = DateTime.UtcNow,
                Messages = new List<MessageEntity>()
            }
        };

        _conversationServiceMock
            .Setup(s => s.GetConversationAsync(1))
            .ReturnsAsync(conversations[0]);
        _conversationServiceMock
            .Setup(s => s.GetConversationAsync(2))
            .ReturnsAsync(conversations[1]);

        var sut = CreateService();
        var options = new ExportOptions
        {
            Format = ExportFormat.Json,
            OutputPath = Path.Combine(_tempExportDir, "batch_export.json")
        };

        // Act
        var result = await sut.ExportConversationsAsync(new[] { 1L, 2L }, options);

        // Assert
        result.Success.Should().BeTrue();
        result.FilePath.Should().NotBeNullOrEmpty();
        File.Exists(result.FilePath).Should().BeTrue();
    }

    [Fact]
    public async Task ExportConversationsAsync_WithSomeMissingConversations_SkipsMissingAndExportsFound()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Found",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>()
        };

        _conversationServiceMock
            .Setup(s => s.GetConversationAsync(1))
            .ReturnsAsync(conversation);
        _conversationServiceMock
            .Setup(s => s.GetConversationAsync(999))
            .ReturnsAsync((ConversationEntity?)null);

        var sut = CreateService();
        var options = new ExportOptions
        {
            Format = ExportFormat.Json,
            OutputPath = Path.Combine(_tempExportDir, "partial_export.json")
        };

        // Act
        var result = await sut.ExportConversationsAsync(new[] { 1L, 999L }, options);

        // Assert
        result.Success.Should().BeTrue(); // Partial success is still success
        File.Exists(result.FilePath).Should().BeTrue();
    }

    // ====================================================================
    //  ExportSearchResultsAsync
    // ====================================================================

    [Fact]
    public async Task ExportSearchResultsAsync_WithEmptyQuery_ReturnsFailureResult()
    {
        // Arrange
        var sut = CreateService();
        var results = new List<SearchResultExportItem>
        {
            new SearchResultExportItem
            {
                Content = "Test",
                DocumentName = "Test.pdf"
            }
        };
        var options = new ExportOptions { Format = ExportFormat.Json };

        // Act
        var result = await sut.ExportSearchResultsAsync("", results, options);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public async Task ExportSearchResultsAsync_WithEmptyResults_ReturnsFailureResult()
    {
        // Arrange
        var sut = CreateService();
        var options = new ExportOptions { Format = ExportFormat.Json };

        // Act
        var result = await sut.ExportSearchResultsAsync("test query", Array.Empty<SearchResultExportItem>(), options);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No search results");
    }

    [Fact]
    public async Task ExportSearchResultsAsync_WithValidData_ReturnsSuccessResult()
    {
        // Arrange
        var results = new List<SearchResultExportItem>
        {
            new SearchResultExportItem
            {
                Query = "test",
                Content = "Search result content",
                DocumentName = "TestDoc.pdf",
                RelevanceScore = 0.95f
            }
        };

        var sut = CreateService();
        var options = new ExportOptions
        {
            Format = ExportFormat.Markdown,
            OutputPath = Path.Combine(_tempExportDir, "search_results.md")
        };

        // Act
        var result = await sut.ExportSearchResultsAsync("test query", results, options);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(result.FilePath).Should().BeTrue();
    }

    // ====================================================================
    //  ExportCollectionAsync
    // ====================================================================

    [Fact]
    public async Task ExportCollectionAsync_WithNotFoundCollection_ReturnsFailureResult()
    {
        // Arrange
        _collectionServiceMock
            .Setup(s => s.GetCollectionAsync(999))
            .ReturnsAsync((CollectionEntity?)null);

        var sut = CreateService();
        var options = new ExportOptions { Format = ExportFormat.Markdown };

        // Act
        var result = await sut.ExportCollectionAsync(999, options);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task ExportCollectionAsync_WithValidCollection_CreatesZipFile()
    {
        // Arrange
        var collection = new CollectionEntity
        {
            Id = 1,
            Name = "Test Collection",
            Description = "A test collection",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _collectionServiceMock
            .Setup(s => s.GetCollectionAsync(1))
            .ReturnsAsync(collection);
        _collectionServiceMock
            .Setup(s => s.GetDocumentsInCollectionAsync(1))
            .ReturnsAsync(new List<DocumentEntity>());

        var sut = CreateService();
        var options = new ExportOptions
        {
            Format = ExportFormat.Markdown, // ZIP for non-CSV formats
            OutputPath = Path.Combine(_tempExportDir, "collection.zip")
        };

        // Act
        var result = await sut.ExportCollectionAsync(1, options);

        // Assert
        result.Success.Should().BeTrue();
        result.FilePath.Should().EndWith(".zip");
        File.Exists(result.FilePath).Should().BeTrue();
    }

    [Fact]
    public async Task ExportCollectionAsync_CsvFormat_CreatesCsvFile()
    {
        // Arrange
        var collection = new CollectionEntity
        {
            Id = 1,
            Name = "CSV Collection",
            CreatedAt = DateTime.UtcNow
        };

        _collectionServiceMock
            .Setup(s => s.GetCollectionAsync(1))
            .ReturnsAsync(collection);
        _collectionServiceMock
            .Setup(s => s.GetDocumentsInCollectionAsync(1))
            .ReturnsAsync(new List<DocumentEntity>
            {
                new DocumentEntity
                {
                    Id = 1,
                    FileName = "test.pdf",
                    FilePath = "/tmp/test.pdf",
                    FileType = "pdf",
                    FileSizeBytes = 1024,
                    PageCount = 5,
                    WordCount = 500,
                    ImportedAt = DateTime.UtcNow,
                    IndexingStatus = "completed"
                }
            });

        var sut = CreateService();
        var options = new ExportOptions
        {
            Format = ExportFormat.Csv,
            OutputPath = Path.Combine(_tempExportDir, "collection.csv")
        };

        // Act
        var result = await sut.ExportCollectionAsync(1, options);

        // Assert
        result.Success.Should().BeTrue();
        result.FilePath.Should().EndWith(".csv");
        File.Exists(result.FilePath).Should().BeTrue();
    }

    // ====================================================================
    //  Format As Markdown/Html (In-Memory)
    // ====================================================================

    [Fact]
    public async Task FormatConversationAsMarkdownAsync_WithValidConversation_ReturnsMarkdownString()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Markdown Format Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "user",
                    Content = "Hello",
                    Timestamp = DateTime.UtcNow,
                    SortOrder = 0
                }
            }
        };

        _conversationServiceMock
            .Setup(s => s.GetConversationAsync(1))
            .ReturnsAsync(conversation);

        var sut = CreateService();

        // Act
        var markdown = await sut.FormatConversationAsMarkdownAsync(1, includeMeta: true);

        // Assert
        markdown.Should().NotBeNullOrEmpty();
        markdown.Should().Contain("# Markdown Format Test");
    }

    [Fact]
    public async Task FormatConversationAsMarkdownAsync_WithNotFoundConversation_ReturnsEmptyString()
    {
        // Arrange
        _conversationServiceMock
            .Setup(s => s.GetConversationAsync(999))
            .ReturnsAsync((ConversationEntity?)null);

        var sut = CreateService();

        // Act
        var markdown = await sut.FormatConversationAsMarkdownAsync(999, includeMeta: true);

        // Assert
        markdown.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task FormatConversationAsHtmlAsync_WithValidConversation_ReturnsHtmlString()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "HTML Format Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "user",
                    Content = "Hello",
                    Timestamp = DateTime.UtcNow,
                    SortOrder = 0
                }
            }
        };

        _conversationServiceMock
            .Setup(s => s.GetConversationAsync(1))
            .ReturnsAsync(conversation);

        var sut = CreateService();

        // Act
        var html = await sut.FormatConversationAsHtmlAsync(1, includeMeta: true);

        // Assert
        html.Should().NotBeNullOrEmpty();
        html.Should().Contain("<!DOCTYPE html>");
    }

    [Fact]
    public async Task FormatConversationAsHtmlAsync_WithNotFoundConversation_ReturnsEmptyString()
    {
        // Arrange
        _conversationServiceMock
            .Setup(s => s.GetConversationAsync(999))
            .ReturnsAsync((ConversationEntity?)null);

        var sut = CreateService();

        // Act
        var html = await sut.FormatConversationAsHtmlAsync(999, includeMeta: true);

        // Assert
        html.Should().BeNullOrEmpty();
    }

    // ====================================================================
    //  Cancellation Support
    // ====================================================================

    [Fact]
    public async Task ExportConversationAsync_WithCancelledToken_ReturnsFailureResult()
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

        _conversationServiceMock
            .Setup(s => s.GetConversationAsync(1))
            .ReturnsAsync(conversation);

        var sut = CreateService();
        var options = new ExportOptions { Format = ExportFormat.Json };

        // Act
        var result = await sut.ExportConversationAsync(1, options, cts.Token);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cancelled");
    }
}
