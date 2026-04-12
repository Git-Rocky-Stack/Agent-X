using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export;
using AgentX.Core.Services.Export.Formats;
using AgentX.Core.Services.Export.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Export;

/// <summary>
/// Unit tests for <see cref="MarkdownExport"/> format strategy.
/// </summary>
public sealed class MarkdownExportTests
{
    private readonly MarkdownExport _exporter;

    public MarkdownExportTests()
    {
        _exporter = new MarkdownExport();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Format and Extension Properties
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Format_ReturnsMarkdown()
    {
        // Act
        var format = _exporter.Format;

        // Assert
        format.Should().Be(ExportFormat.Markdown);
    }

    [Fact]
    public void FileExtension_ReturnsDotMd()
    {
        // Act
        var extension = _exporter.FileExtension;

        // Assert
        extension.Should().Be(".md");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Supports<T>() Method
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Supports_ConversationEntity_ReturnsTrue()
    {
        // Act
        var supports = _exporter.Supports<ConversationEntity>();

        // Assert
        supports.Should().BeTrue();
    }

    [Fact]
    public void Supports_EnumerableConversationEntity_ReturnsTrue()
    {
        // Act
        var supports = _exporter.Supports<IEnumerable<ConversationEntity>>();

        // Assert
        supports.Should().BeTrue();
    }

    [Fact]
    public void Supports_SearchResultExportItem_ReturnsTrue()
    {
        // Act
        var supports = _exporter.Supports<SearchResultExportItem>();

        // Assert
        supports.Should().BeTrue();
    }

    [Fact]
    public void Supports_EnumerableSearchResultExportItem_ReturnsTrue()
    {
        // Act
        var supports = _exporter.Supports<IEnumerable<SearchResultExportItem>>();

        // Assert
        supports.Should().BeTrue();
    }

    [Fact]
    public void Supports_UnsupportedType_ReturnsFalse()
    {
        // Act
        var supports = _exporter.Supports<string>();

        // Assert
        supports.Should().BeFalse();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RenderAsync - Conversation
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderAsync_Conversation_IncludesTitle()
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
        var options = new ExportOptions { IncludeMetadata = true };

        // Act
        var result = await _exporter.RenderAsync(conversation, options);

        // Assert
        var markdown = result.Should().BeOfType<string>().Subject;
        markdown.Should().Contain("# Test Conversation");
    }

    [Fact]
    public async Task RenderAsync_Conversation_WithMetadata_IncludesFrontmatter()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test",
            CreatedAt = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 15, 11, 0, 0, DateTimeKind.Utc),
            MessageCount = 5,
            TokensUsed = 500,
            ModelId = "llama-3.1",
            Messages = new List<MessageEntity>()
        };
        var options = new ExportOptions { IncludeMetadata = true };

        // Act
        var result = await _exporter.RenderAsync(conversation, options);

        // Assert
        var markdown = result.Should().BeOfType<string>().Subject;
        markdown.Should().Contain("---");
        markdown.Should().Contain("title: \"Test\"");
        markdown.Should().Contain("created: 2026-01-15T10:30:00Z");
        markdown.Should().Contain("updated: 2026-01-15T11:00:00Z");
        markdown.Should().Contain("messages: 5");
        markdown.Should().Contain("tokens: 500");
        markdown.Should().Contain("model: llama-3.1");
    }

    [Fact]
    public async Task RenderAsync_Conversation_WithoutMetadata_ExcludesFrontmatter()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>()
        };
        var options = new ExportOptions { IncludeMetadata = false };

        // Act
        var result = await _exporter.RenderAsync(conversation, options);

        // Assert
        var markdown = result.Should().BeOfType<string>().Subject;
        markdown.Should().NotMatchRegex(@"^---\s*$"); // Should not have YAML frontmatter
    }

    [Fact]
    public async Task RenderAsync_Conversation_IncludesSystemPrompt()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test",
            SystemPrompt = "You are a helpful assistant.",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>()
        };
        var options = new ExportOptions();

        // Act
        var result = await _exporter.RenderAsync(conversation, options);

        // Assert
        var markdown = result.Should().BeOfType<string>().Subject;
        markdown.Should().Contain("## System Prompt");
        markdown.Should().Contain("> You are a helpful assistant.");
    }

    [Fact]
    public async Task RenderAsync_Conversation_SkipsSystemMessages()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "system",
                    Content = "System instruction",
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
        var result = await _exporter.RenderAsync(conversation, options);

        // Assert
        var markdown = result.Should().BeOfType<string>().Subject;
        markdown.Should().Contain("### User");
        markdown.Should().Contain("Hello");
        markdown.Should().NotContain("### System");
        markdown.Should().NotContain("System instruction");
    }

    [Fact]
    public async Task RenderAsync_Conversation_WithTimestamps_IncludesTimestamps()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "user",
                    Content = "Hello",
                    Timestamp = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
                    SortOrder = 0
                }
            }
        };
        var options = new ExportOptions { IncludeTimestamps = true };

        // Act
        var result = await _exporter.RenderAsync(conversation, options);

        // Assert
        var markdown = result.Should().BeOfType<string>().Subject;
        markdown.Should().Contain("*2026-01-15 10:30:00 UTC*");
    }

    [Fact]
    public async Task RenderAsync_Conversation_WithModelInfo_IncludesModelId()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "assistant",
                    Content = "Hello",
                    Timestamp = DateTime.UtcNow,
                    ModelId = "llama-3.1",
                    SortOrder = 0
                }
            }
        };
        var options = new ExportOptions { IncludeModelInfo = true };

        // Act
        var result = await _exporter.RenderAsync(conversation, options);

        // Assert
        var markdown = result.Should().BeOfType<string>().Subject;
        markdown.Should().Contain("*Model: llama-3.1*");
    }

    [Fact]
    public async Task RenderAsync_Conversation_WithoutModelInfo_ExcludesModelId()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "assistant",
                    Content = "Hello",
                    Timestamp = DateTime.UtcNow,
                    ModelId = "llama-3.1",
                    SortOrder = 0
                }
            }
        };
        var options = new ExportOptions { IncludeModelInfo = false };

        // Act
        var result = await _exporter.RenderAsync(conversation, options);

        // Assert
        var markdown = result.Should().BeOfType<string>().Subject;
        markdown.Should().NotContain("*Model:");
    }

    [Fact]
    public async Task RenderAsync_Conversation_IncludesCitations()
    {
        // Arrange
        var citationsJson = """
            [
                {
                    "fileName": "Test.pdf",
                    "pageNumber": 5,
                    "excerpt": "This is a test excerpt from the document."
                }
            ]
            """;

        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "assistant",
                    Content = "Here's the answer.",
                    Timestamp = DateTime.UtcNow,
                    CitationsJson = citationsJson,
                    SortOrder = 0
                }
            }
        };
        var options = new ExportOptions { IncludeCitations = true };

        // Act
        var result = await _exporter.RenderAsync(conversation, options);

        // Assert
        var markdown = result.Should().BeOfType<string>().Subject;
        markdown.Should().Contain("## Citations");
        markdown.Should().Contain("Test.pdf, page 5");
    }

    [Fact]
    public async Task RenderAsync_Conversation_WithoutCitations_ExcludesCitations()
    {
        // Arrange
        var citationsJson = """
            [
                {
                    "fileName": "Test.pdf",
                    "pageNumber": 5
                }
            ]
            """;

        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "assistant",
                    Content = "Here's the answer.",
                    Timestamp = DateTime.UtcNow,
                    CitationsJson = citationsJson,
                    SortOrder = 0
                }
            }
        };
        var options = new ExportOptions { IncludeCitations = false };

        // Act
        var result = await _exporter.RenderAsync(conversation, options);

        // Assert
        var markdown = result.Should().BeOfType<string>().Subject;
        markdown.Should().NotContain("## Citations");
    }

    [Fact]
    public async Task RenderAsync_Conversation_IncludesAssistantMetadata()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "assistant",
                    Content = "Hello",
                    Timestamp = DateTime.UtcNow,
                    TokenCount = 50,
                    GenerationTimeMs = 1234,
                    SortOrder = 0
                }
            }
        };
        var options = new ExportOptions { IncludeMetadata = true };

        // Act
        var result = await _exporter.RenderAsync(conversation, options);

        // Assert
        var markdown = result.Should().BeOfType<string>().Subject;
        markdown.Should().Contain("Tokens: 50");
        markdown.Should().Contain("Generation: 1234ms");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RenderAsync - Multiple Conversations
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderAsync_MultipleConversations_SeparatesWithDividers()
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
        var options = new ExportOptions();

        // Act
        var result = await _exporter.RenderAsync(conversations, options);

        // Assert
        var markdown = result.Should().BeOfType<string>().Subject;
        markdown.Should().Contain("# First");
        markdown.Should().Contain("# Second");
        markdown.Should().Contain("---");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RenderAsync - Search Results
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderAsync_SearchResult_IncludesRelevanceScore()
    {
        // Arrange
        var result = new SearchResultExportItem
        {
            Query = "test query",
            Content = "Search result content",
            DocumentName = "TestDoc.pdf",
            RelevanceScore = 0.95f,
            Citations = new[] { "TestDoc.pdf, page 3" }
        };
        var options = new ExportOptions { IncludeMetadata = true };

        // Act
        var rendered = await _exporter.RenderAsync(result, options);

        // Assert
        var markdown = rendered.Should().BeOfType<string>().Subject;
        markdown.Should().Contain("**Relevance:** 95.0%");
    }

    [Fact]
    public async Task RenderAsync_SearchResults_RendersMultipleResults()
    {
        // Arrange
        var results = new List<SearchResultExportItem>
        {
            new SearchResultExportItem
            {
                Query = "test",
                Content = "Result 1",
                DocumentName = "Doc1.pdf",
                RelevanceScore = 0.9f
            },
            new SearchResultExportItem
            {
                Query = "test",
                Content = "Result 2",
                DocumentName = "Doc2.pdf",
                RelevanceScore = 0.8f
            }
        };
        var options = new ExportOptions();

        // Act
        var rendered = await _exporter.RenderAsync(results, options);

        // Assert
        var markdown = rendered.Should().BeOfType<string>().Subject;
        markdown.Should().Contain("Result 1: Doc1.pdf");
        markdown.Should().Contain("Result 2: Doc2.pdf");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Cancellation Support
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>()
        };
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = () => _exporter.RenderAsync(conversation, new ExportOptions(), cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
