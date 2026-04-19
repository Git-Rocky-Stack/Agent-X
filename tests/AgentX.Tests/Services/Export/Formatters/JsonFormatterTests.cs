using System.Text.Json;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export.Formatters;
using AgentX.Core.Services.Export.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Export.Formatters;

/// <summary>
/// Unit tests for <see cref="JsonFormatter"/>.
/// Verifies that the formatter produces structured JSON output via the
/// <see cref="Formats.JsonExport"/> helper.
/// </summary>
public sealed class JsonFormatterTests
{
    private readonly JsonFormatter _sut = new();

    private static ConversationEntity CreateConversation(
        string title = "Test Conversation",
        int messageCount = 2,
        bool includeSystemMessage = false)
    {
        var messages = new List<MessageEntity>();

        if (includeSystemMessage)
        {
            messages.Add(new MessageEntity
            {
                Id = 0,
                Role = "system",
                Content = "You are a helpful assistant.",
                Timestamp = DateTime.UtcNow,
                TokenCount = 0,
                SortOrder = 0,
            });
        }

        for (var i = 0; i < messageCount; i++)
        {
            messages.Add(new MessageEntity
            {
                Id = i + 1,
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"Message {i + 1}",
                Timestamp = new DateTime(2026, 3, 10, 9, 0, i, DateTimeKind.Utc),
                TokenCount = (i + 1) * 15,
                ModelId = i % 2 == 1 ? "gpt-4" : null,
                SortOrder = i + 1,
            });
        }

        return new ConversationEntity
        {
            Id = 1,
            Title = title,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            MessageCount = messages.Count,
            TokensUsed = 100,
            Messages = messages,
        };
    }

    // ====================================================================
    //  Properties
    // ====================================================================

    [Fact]
    public void Format_ShouldBeJson()
    {
        _sut.Format.Should().Be(ExportFormat.Json);
    }

    [Fact]
    public void FileExtension_ShouldBeJson()
    {
        _sut.FileExtension.Should().Be(".json");
    }

    [Fact]
    public void MimeType_ShouldBeApplicationJson()
    {
        _sut.MimeType.Should().Be("application/json");
    }

    // ====================================================================
    //  ExportConversationAsync (single)
    // ====================================================================

    [Fact]
    public async Task ExportConversationAsync_ProducesValidJson()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert — should be valid JSON
        var act = () => JsonDocument.Parse(result);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task ExportConversationAsync_ContainsExportMetadata()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("exportMetadata");
        result.Should().Contain("\"format\": \"json\"");
        result.Should().Contain("\"conversationCount\": 1");
        result.Should().Contain("Agent-X");
    }

    [Fact]
    public async Task ExportConversationAsync_ContainsConversationData()
    {
        // Arrange
        var conversation = CreateConversation(title: "My Chat");
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("conversations");
        result.Should().Contain("My Chat");
    }

    [Fact]
    public async Task ExportConversationAsync_ContainsMessages()
    {
        // Arrange
        var conversation = CreateConversation(messageCount: 2);
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("Message 1");
        result.Should().Contain("Message 2");
        result.Should().Contain("user");
        result.Should().Contain("assistant");
    }

    [Fact]
    public async Task ExportConversationAsync_IncludesSystemMessagesInArray()
    {
        // Arrange — system messages are included in the JSON messages array
        var conversation = CreateConversation(messageCount: 2, includeSystemMessage: true);
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert — JSON export includes all messages (system included)
        using var doc = JsonDocument.Parse(result);
        var messages = doc.RootElement
            .GetProperty("conversations").EnumerateArray().First()
            .GetProperty("messages").EnumerateArray().ToList();

        messages.Should().HaveCount(3);
        messages[0].GetProperty("role").GetString().Should().Be("system");
    }

    [Fact]
    public async Task ExportConversationAsync_WithMetadata_IncludesTokenCounts()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions { IncludeMetadata = true };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("tokenCount");
    }

    [Fact]
    public async Task ExportConversationAsync_WithModelInfo_IncludesModelId()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions { IncludeModelInfo = true };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("modelId");
        result.Should().Contain("gpt-4");
    }

    [Fact]
    public async Task ExportConversationAsync_WithoutModelInfo_ExcludesModelId()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions { IncludeModelInfo = false };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert — messages with null modelId should not have the field serialized
        using var doc = JsonDocument.Parse(result);
        var messages = doc.RootElement
            .GetProperty("conversations").EnumerateArray().First()
            .GetProperty("messages").EnumerateArray().ToList();

        // The first message (user) should not have modelId property when IncludeModelInfo is false
        foreach (var msg in messages)
        {
            msg.TryGetProperty("modelId", out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task ExportConversationAsync_UsesTitleFromOptions()
    {
        // Arrange
        var conversation = CreateConversation(title: "Original");
        var options = new ExportOptions { Title = "Custom Title" };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("Custom Title");
    }

    [Fact]
    public async Task ExportConversationAsync_UsesCamelCaseNaming()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert — should use camelCase property names
        result.Should().Contain("exportMetadata");
        result.Should().Contain("conversationCount");
        result.Should().Contain("exportedAt");
    }

    [Fact]
    public async Task ExportConversationAsync_ProducesIndentedJson()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert — indented JSON should contain whitespace/newlines for readability
        result.Should().Contain("\n");
        result.Should().Contain("  ");
    }

    // ====================================================================
    //  ExportConversationsAsync (batch)
    // ====================================================================

    [Fact]
    public async Task ExportConversationsAsync_ProducesValidJson()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            CreateConversation(title: "Chat A"),
            CreateConversation(title: "Chat B"),
        };
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationsAsync(conversations, options);

        // Assert
        var act = () => JsonDocument.Parse(result);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task ExportConversationsAsync_ReportsCorrectConversationCount()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            CreateConversation(title: "Alpha"),
            CreateConversation(title: "Beta"),
            CreateConversation(title: "Gamma"),
        };
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationsAsync(conversations, options);

        // Assert
        result.Should().Contain("\"conversationCount\": 3");
    }

    [Fact]
    public async Task ExportConversationsAsync_ContainsAllConversationTitles()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            CreateConversation(title: "First"),
            CreateConversation(title: "Second"),
        };
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationsAsync(conversations, options);

        // Assert
        result.Should().Contain("First");
        result.Should().Contain("Second");
    }

    [Fact]
    public async Task ExportConversationsAsync_ArrayContainsAllConversations()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            CreateConversation(title: "A"),
            CreateConversation(title: "B"),
        };
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationsAsync(conversations, options);

        // Assert
        using var doc = JsonDocument.Parse(result);
        var convArray = doc.RootElement.GetProperty("conversations").EnumerateArray().ToList();
        convArray.Should().HaveCount(2);
    }

    // ====================================================================
    //  Cancellation
    // ====================================================================

    [Fact]
    public async Task ExportConversationAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = () => _sut.ExportConversationAsync(conversation, options, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExportConversationsAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var conversations = new List<ConversationEntity> { CreateConversation() };
        var options = new ExportOptions();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = () => _sut.ExportConversationsAsync(conversations, options, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
