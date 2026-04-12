using AgentX.Core.Data.Entities;
using AgentX.Core.Documents;
using AgentX.Core.Services.Export;
using AgentX.Core.Services.Export.Formats;
using AgentX.Core.Services.Export.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Export;

/// <summary>
/// Unit tests for <see cref="HtmlExport"/>.
/// Tests the HTML format strategy implementation.
/// </summary>
public sealed class HtmlExportTests
{
    private readonly HtmlExport _export;

    public HtmlExportTests()
    {
        _export = new HtmlExport();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Basic Properties
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Format_ShouldReturnHtml()
    {
        // Act
        var format = _export.Format;

        // Assert
        format.Should().Be(ExportFormat.Html);
    }

    [Fact]
    public void FileExtension_ShouldReturnHtmlExtension()
    {
        // Act
        var extension = _export.FileExtension;

        // Assert
        extension.Should().Be(".html");
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
    //  RenderAsync - Single Conversation
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderAsync_WithSingleConversation_ReturnsValidHtml()
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

        var options = new ExportOptions
        {
            IncludeMetadata = true,
            IncludeTimestamps = true,
            IncludeModelInfo = true,
            IncludeCitations = true
        };

        // Act
        var result = await _export.RenderAsync(conversation, options);

        // Assert
        var html = result as string;
        html.Should().NotBeNullOrEmpty();
        html.Should().Contain("<!DOCTYPE html>");
        html.Should().Contain("Test Conversation");
        html.Should().Contain("<div class=\"conversation\">");
        html.Should().Contain("</html>");
    }

    [Fact]
    public async Task RenderAsync_WithSingleConversation_IncludesMetadata()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Metadata Test",
            CreatedAt = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2024, 1, 15, 11, 45, 0, DateTimeKind.Utc),
            MessageCount = 5,
            TokensUsed = 1250,
            ModelId = "claude-3-5-sonnet",
            Messages = new List<MessageEntity>()
        };

        var options = new ExportOptions { IncludeMetadata = true };

        // Act
        var result = await _export.RenderAsync(conversation, options);

        // Assert
        var html = result as string;
        html.Should().Contain("Created: 2024-01-15 10:30:00 UTC");
        html.Should().Contain("Updated: 2024-01-15 11:45:00 UTC");
        html.Should().Contain("Messages: 5");
        html.Should().Contain("Tokens: 1,250");
        html.Should().Contain("Model: claude-3-5-sonnet");
    }

    [Fact]
    public async Task RenderAsync_WithSingleConversation_ExcludesMetadata()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "No Metadata Test",
            CreatedAt = DateTime.UtcNow,
            MessageCount = 3,
            TokensUsed = 500,
            ModelId = "claude-3-5-sonnet",
            Messages = new List<MessageEntity>()
        };

        var options = new ExportOptions { IncludeMetadata = false };

        // Act
        var result = await _export.RenderAsync(conversation, options);

        // Assert
        var html = result as string;
        html.Should().NotContain("Created:");
        html.Should().NotContain("Messages: 3");
        html.Should().NotContain("Model: claude-3-5-sonnet");
    }

    [Fact]
    public async Task RenderAsync_WithUserMessage_AppliesUserClass()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "User Message Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "user",
                    Content = "Hello from user",
                    Timestamp = DateTime.UtcNow,
                    SortOrder = 0
                }
            }
        };

        var options = new ExportOptions();

        // Act
        var result = await _export.RenderAsync(conversation, options);

        // Assert
        var html = result as string;
        html.Should().Contain("class=\"message user\"");
        html.Should().Contain("Hello from user");
    }

    [Fact]
    public async Task RenderAsync_WithAssistantMessage_AppliesAssistantClass()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Assistant Message Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "assistant",
                    Content = "Hello from assistant",
                    Timestamp = DateTime.UtcNow,
                    SortOrder = 0
                }
            }
        };

        var options = new ExportOptions();

        // Act
        var result = await _export.RenderAsync(conversation, options);

        // Assert
        var html = result as string;
        html.Should().Contain("class=\"message assistant\"");
        html.Should().Contain("Hello from assistant");
    }

    [Fact]
    public async Task RenderAsync_WithSystemMessage_ExcludesFromBody()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "System Message Test",
            CreatedAt = DateTime.UtcNow,
            SystemPrompt = "You are a helpful assistant",
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "system",
                    Content = "System directive",
                    Timestamp = DateTime.UtcNow,
                    SortOrder = 0
                },
                new MessageEntity
                {
                    Id = 2,
                    Role = "user",
                    Content = "Hello",
                    Timestamp = DateTime.UtcNow,
                    SortOrder = 1
                }
            }
        };

        var options = new ExportOptions();

        // Act
        var result = await _export.RenderAsync(conversation, options);

        // Assert
        var html = result as string;
        html.Should().Contain("System Prompt");
        html.Should().Contain("You are a helpful assistant");
        html.Should().NotContain("System directive");
        html.Should().NotContain("class=\"message system\"");
    }

    [Fact]
    public async Task RenderAsync_WithHtmlSpecialCharacters_EscapesContent()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "HTML Escape Test <script>",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "user",
                    Content = "Test <script>alert('xss')</script>",
                    Timestamp = DateTime.UtcNow,
                    SortOrder = 0
                }
            }
        };

        var options = new ExportOptions();

        // Act
        var result = await _export.RenderAsync(conversation, options);

        // Assert
        var html = result as string;
        html.Should().NotContain("<script>");
        html.Should().Contain("&lt;script&gt;");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RenderAsync - Multiple Conversations
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderAsync_WithMultipleConversations_ReturnsValidHtml()
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

        // Act
        var result = await _export.RenderAsync(conversations, options);

        // Assert
        var html = result as string;
        html.Should().NotBeNullOrEmpty();
        html.Should().Contain("First Conversation");
        html.Should().Contain("Second Conversation");
        html.Should().Contain("class=\"section-divider\"");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RenderAsync - Search Results
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderAsync_WithSearchResults_ReturnsValidHtml()
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

        // Act
        var result = await _export.RenderAsync(results, options);

        // Assert
        var html = result as string;
        html.Should().NotBeNullOrEmpty();
        html.Should().Contain("Search Results");
        html.Should().Contain("Test Document.pdf");
        html.Should().Contain("Relevance: 95.0%");
        html.Should().Contain("Page 1");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Cancellation Support
    // ══════════════════════════════════════════════════════════════════════

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
}
