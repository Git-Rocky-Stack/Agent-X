using System.Text.Json;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export;
using AgentX.Core.Services.Export.Formats;
using AgentX.Core.Services.Export.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Export;

/// <summary>
/// Unit tests for <see cref="JsonExport"/> format strategy.
/// </summary>
public sealed class JsonExportTests
{
    private readonly JsonExport _exporter;

    public JsonExportTests()
    {
        _exporter = new JsonExport();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Format and Extension Properties
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Format_ReturnsJson()
    {
        // Act
        var format = _exporter.Format;

        // Assert
        format.Should().Be(ExportFormat.Json);
    }

    [Fact]
    public void FileExtension_ReturnsDotJson()
    {
        // Act
        var extension = _exporter.FileExtension;

        // Assert
        extension.Should().Be(".json");
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
    public async Task RenderAsync_Conversation_ProducesValidJson()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test Conversation",
            CreatedAt = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 15, 11, 0, 0, DateTimeKind.Utc),
            MessageCount = 2,
            TokensUsed = 100,
            ModelId = "llama-3.1",
            Messages = new List<MessageEntity>
            {
                new MessageEntity
                {
                    Id = 1,
                    Role = "user",
                    Content = "Hello",
                    Timestamp = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
                    TokenCount = 10,
                    SortOrder = 0
                },
                new MessageEntity
                {
                    Id = 2,
                    Role = "assistant",
                    Content = "Hi there!",
                    Timestamp = new DateTime(2026, 1, 15, 10, 31, 0, DateTimeKind.Utc),
                    TokenCount = 15,
                    SortOrder = 1
                }
            }
        };
        var options = new ExportOptions { IncludeMetadata = true };

        // Act
        var result = await _exporter.RenderAsync(conversation, options);

        // Assert
        var json = result.Should().BeOfType<string>().Subject;

        // Should be valid JSON
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();

        // Verify structure
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("exportMetadata").GetProperty("conversationCount").GetInt32()
            .Should().Be(1);
        doc.RootElement.GetProperty("conversations").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RenderAsync_Conversation_IncludesExportMetadata()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>()
        };
        var options = new ExportOptions { Title = "Custom Title" };

        // Act
        var result = await _exporter.RenderAsync(conversation, options);

        // Assert
        var json = result.Should().BeOfType<string>().Subject;
        var doc = JsonDocument.Parse(json);

        var metadata = doc.RootElement.GetProperty("exportMetadata");
        metadata.GetProperty("title").GetString().Should().Be("Custom Title");
        metadata.GetProperty("exportedBy").GetString().Should().Be("Agent-X");
        metadata.GetProperty("format").GetString().Should().Be("json");
    }

    [Fact]
    public async Task RenderAsync_Conversation_IncludesConversationData()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 42,
            Title = "Test Title",
            SystemPrompt = "You are helpful.",
            ModelId = "llama-3.1",
            IsPinned = true,
            IsArchived = false,
            MessageCount = 5,
            TokensUsed = 500,
            CreatedAt = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 15, 11, 0, 0, DateTimeKind.Utc),
            Messages = new List<MessageEntity>()
        };
        var options = new ExportOptions();

        // Act
        var result = await _exporter.RenderAsync(conversation, options);

        // Assert
        var json = result.Should().BeOfType<string>().Subject;
        var doc = JsonDocument.Parse(json);

        var conv = doc.RootElement.GetProperty("conversations")[0];
        conv.GetProperty("id").GetInt64().Should().Be(42);
        conv.GetProperty("title").GetString().Should().Be("Test Title");
        conv.GetProperty("systemPrompt").GetString().Should().Be("You are helpful.");
        conv.GetProperty("modelId").GetString().Should().Be("llama-3.1");
        conv.GetProperty("isPinned").GetBoolean().Should().BeTrue();
        conv.GetProperty("messageCount").GetInt32().Should().Be(5);
        conv.GetProperty("tokensUsed").GetInt32().Should().Be(500);
    }

    [Fact]
    public async Task RenderAsync_Conversation_WithMetadata_IncludesMessageDetails()
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
                    ModelId = "llama-3.1",
                    CitationsJson = "{\"test\": true}",
                    SortOrder = 0
                }
            }
        };
        var options = new ExportOptions
        {
            IncludeMetadata = true,
            IncludeModelInfo = true,
            IncludeCitations = true
        };

        // Act
        var result = await _exporter.RenderAsync(conversation, options);

        // Assert
        var json = result.Should().BeOfType<string>().Subject;
        var doc = JsonDocument.Parse(json);

        var message = doc.RootElement.GetProperty("conversations")[0]
            .GetProperty("messages")[0];

        message.GetProperty("tokenCount").GetInt32().Should().Be(50);
        message.GetProperty("generationTimeMs").GetInt64().Should().Be(1234);
        message.GetProperty("modelId").GetString().Should().Be("llama-3.1");
        message.GetProperty("citations").GetString().Should().Be("{\"test\": true}");
    }

    [Fact]
    public async Task RenderAsync_Conversation_WithoutMetadata_ExcludesOptionalFields()
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
                    SortOrder = 0
                }
            }
        };
        var options = new ExportOptions
        {
            IncludeMetadata = false,
            IncludeModelInfo = false,
            IncludeCitations = false
        };

        // Act
        var result = await _exporter.RenderAsync(conversation, options);

        // Assert
        var json = result.Should().BeOfType<string>().Subject;
        var doc = JsonDocument.Parse(json);

        var message = doc.RootElement.GetProperty("conversations")[0]
            .GetProperty("messages")[0];

        // Fields should be null/omitted when IncludeMetadata is false
        message.TryGetProperty("tokenCount", out _).Should().BeFalse();
        message.TryGetProperty("generationTimeMs", out _).Should().BeFalse();
        message.TryGetProperty("modelId", out _).Should().BeFalse();
        message.TryGetProperty("citations", out _).Should().BeFalse();
    }

    [Fact]
    public async Task RenderAsync_Conversation_MessagesOrderedBySortOrder()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>
            {
                new MessageEntity { Id = 3, Role = "assistant", Content = "Third", Timestamp = DateTime.UtcNow, SortOrder = 2 },
                new MessageEntity { Id = 1, Role = "user", Content = "First", Timestamp = DateTime.UtcNow, SortOrder = 0 },
                new MessageEntity { Id = 2, Role = "user", Content = "Second", Timestamp = DateTime.UtcNow, SortOrder = 1 }
            }
        };
        var options = new ExportOptions();

        // Act
        var result = await _exporter.RenderAsync(conversation, options);

        // Assert
        var json = result.Should().BeOfType<string>().Subject;
        var doc = JsonDocument.Parse(json);

        var messages = doc.RootElement.GetProperty("conversations")[0]
            .GetProperty("messages");

        messages[0].GetProperty("content").GetString().Should().Be("First");
        messages[1].GetProperty("content").GetString().Should().Be("Second");
        messages[2].GetProperty("content").GetString().Should().Be("Third");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RenderAsync - Multiple Conversations
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderAsync_MultipleConversations_IncludesCorrectCount()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            new ConversationEntity { Id = 1, Title = "First", CreatedAt = DateTime.UtcNow, Messages = new List<MessageEntity>() },
            new ConversationEntity { Id = 2, Title = "Second", CreatedAt = DateTime.UtcNow, Messages = new List<MessageEntity>() },
            new ConversationEntity { Id = 3, Title = "Third", CreatedAt = DateTime.UtcNow, Messages = new List<MessageEntity>() }
        };
        var options = new ExportOptions();

        // Act
        var result = await _exporter.RenderAsync(conversations, options);

        // Assert
        var json = result.Should().BeOfType<string>().Subject;
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("exportMetadata").GetProperty("conversationCount").GetInt32()
            .Should().Be(3);
        doc.RootElement.GetProperty("conversations").GetArrayLength()
            .Should().Be(3);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RenderAsync - Search Results
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderAsync_SearchResult_ProducesValidJson()
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
        var options = new ExportOptions();

        // Act
        var rendered = await _exporter.RenderAsync(result, options);

        // Assert
        var json = rendered.Should().BeOfType<string>().Subject;
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task RenderAsync_SearchResults_IncludesAllResults()
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
        var json = rendered.Should().BeOfType<string>().Subject;
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("results").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task RenderAsync_SearchResults_IncludesRelevanceScore()
    {
        // Arrange
        var results = new List<SearchResultExportItem>
        {
            new SearchResultExportItem
            {
                Query = "test",
                Content = "Result content",
                DocumentName = "Doc.pdf",
                RelevanceScore = 0.85f
            }
        };
        var options = new ExportOptions();

        // Act
        var rendered = await _exporter.RenderAsync(results, options);

        // Assert
        var json = rendered.Should().BeOfType<string>().Subject;
        var doc = JsonDocument.Parse(json);

        var score = doc.RootElement.GetProperty("results")[0]
            .GetProperty("relevanceScore").GetSingle();
        score.Should().Be(0.85f);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  JSON Formatting
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RenderAsync_ProducesIndentedJson()
    {
        // Arrange
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test",
            CreatedAt = DateTime.UtcNow,
            Messages = new List<MessageEntity>()
        };
        var options = new ExportOptions();

        // Act
        var result = await _exporter.RenderAsync(conversation, options);

        // Assert
        var json = result.Should().BeOfType<string>().Subject;
        json.Should().Contain("\n"); // Indented JSON has newlines
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
