using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export.Formatters;
using AgentX.Core.Services.Export.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Export.Formatters;

/// <summary>
/// Unit tests for <see cref="CsvFormatter"/>.
/// Verifies that the formatter produces output identical to
/// <c>ExportService.BuildConversationCsv</c> (single) and the batch CSV path.
/// </summary>
public sealed class CsvFormatterTests
{
    private readonly CsvFormatter _sut = new();

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

    // ══════════════════════════════════════════════════════════════════════
    //  Properties
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Format_ShouldBeCsv()
    {
        _sut.Format.Should().Be(ExportFormat.Csv);
    }

    [Fact]
    public void FileExtension_ShouldBeCsv()
    {
        _sut.FileExtension.Should().Be(".csv");
    }

    [Fact]
    public void MimeType_ShouldBeTextCsv()
    {
        _sut.MimeType.Should().Be("text/csv");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ExportConversationAsync (single — no ConversationTitle column)
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportConversationAsync_StartsWithHeaderRow()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        var firstLine = result.Split('\n').First().TrimEnd('\r');
        firstLine.Should().Be("Role,Content,Timestamp,Model,Tokens");
    }

    [Fact]
    public async Task ExportConversationAsync_IncludesMessageRows()
    {
        // Arrange
        var conversation = CreateConversation(messageCount: 2);
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("user");
        result.Should().Contain("assistant");
        result.Should().Contain("Message 1");
        result.Should().Contain("Message 2");
    }

    [Fact]
    public async Task ExportConversationAsync_SkipsSystemMessages()
    {
        // Arrange
        var conversation = CreateConversation(messageCount: 2, includeSystemMessage: true);
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert — system message content should not appear in CSV body
        result.Should().NotContain("You are a helpful assistant.");
    }

    [Fact]
    public async Task ExportConversationAsync_DoesNotIncludeConversationTitleColumn()
    {
        // Arrange
        var conversation = CreateConversation(title: "My Special Chat");
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert — single conversation CSV has no ConversationTitle column
        var firstLine = result.Split('\n').First().TrimEnd('\r');
        firstLine.Should().NotContain("ConversationTitle");
        result.Should().NotContain("My Special Chat");
    }

    [Fact]
    public async Task ExportConversationAsync_EscapesCommasInContent()
    {
        // Arrange
        var messages = new List<MessageEntity>
        {
            new()
            {
                Id = 1,
                Role = "user",
                Content = "Hello, world, with commas",
                Timestamp = DateTime.UtcNow,
                TokenCount = 10,
                SortOrder = 0,
            },
        };
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Comma Test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            MessageCount = 1,
            TokensUsed = 10,
            Messages = messages,
        };
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert — content with commas should be double-quoted
        result.Should().Contain("\"Hello, world, with commas\"");
    }

    [Fact]
    public async Task ExportConversationAsync_EscapesQuotesInContent()
    {
        // Arrange
        var messages = new List<MessageEntity>
        {
            new()
            {
                Id = 1,
                Role = "user",
                Content = "He said \"hello\"",
                Timestamp = DateTime.UtcNow,
                TokenCount = 10,
                SortOrder = 0,
            },
        };
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Quote Test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            MessageCount = 1,
            TokensUsed = 10,
            Messages = messages,
        };
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert — embedded quotes should be doubled
        result.Should().Contain("\"He said \"\"hello\"\"\"");
    }

    [Fact]
    public async Task ExportConversationAsync_WithNullModelId_WritesEmptyField()
    {
        // Arrange
        var messages = new List<MessageEntity>
        {
            new()
            {
                Id = 1,
                Role = "user",
                Content = "Hello",
                Timestamp = DateTime.UtcNow,
                TokenCount = 5,
                ModelId = null,
                SortOrder = 0,
            },
        };
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Null Model",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            MessageCount = 1,
            TokensUsed = 5,
            Messages = messages,
        };
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert — null modelId should result in empty quoted field
        var lines = result.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l.TrimEnd('\r'))).ToList();
        lines.Should().HaveCount(2); // header + 1 data row
        // The data row should have the model column as "" and end with token count
        lines[1].TrimEnd('\r').Should().Contain("\"\""); // CsvEscape produces "" for null/empty
        lines[1].TrimEnd('\r').Should().EndWith("5");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ExportConversationsAsync (batch — includes ConversationTitle column)
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportConversationsAsync_StartsWithHeaderRowIncludingConversationTitle()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            CreateConversation(title: "First Chat"),
        };
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationsAsync(conversations, options);

        // Assert
        var firstLine = result.Split('\n').First().TrimEnd('\r');
        firstLine.Should().Be("ConversationTitle,Role,Content,Timestamp,Model,Tokens");
    }

    [Fact]
    public async Task ExportConversationsAsync_IncludesConversationTitleInDataRows()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            CreateConversation(title: "Chat Alpha"),
            CreateConversation(title: "Chat Beta"),
        };
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationsAsync(conversations, options);

        // Assert
        result.Should().Contain("Chat Alpha");
        result.Should().Contain("Chat Beta");
    }

    [Fact]
    public async Task ExportConversationsAsync_SkipsSystemMessages()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            CreateConversation(messageCount: 2, includeSystemMessage: true),
        };
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationsAsync(conversations, options);

        // Assert
        result.Should().NotContain("You are a helpful assistant.");
    }

    [Fact]
    public async Task ExportConversationsAsync_MergesAllConversationsIntoSingleTable()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            CreateConversation(title: "A", messageCount: 2),
            CreateConversation(title: "B", messageCount: 2),
        };
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationsAsync(conversations, options);

        // Assert — header + 2 messages per conversation = 4 data rows
        var nonEmptyLines = result.Split('\n')
            .Where(l => !string.IsNullOrWhiteSpace(l.TrimEnd('\r')))
            .ToList();
        nonEmptyLines.Should().HaveCount(5); // 1 header + 4 data
    }

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
