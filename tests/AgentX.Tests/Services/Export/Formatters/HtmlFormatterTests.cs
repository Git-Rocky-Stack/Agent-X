using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export.Formatters;
using AgentX.Core.Services.Export.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Export.Formatters;

/// <summary>
/// Unit tests for <see cref="HtmlFormatter"/>.
/// Verifies that the formatter produces styled HTML output via the
/// <see cref="Formats.HtmlExport"/> helper.
/// </summary>
public sealed class HtmlFormatterTests
{
    private readonly HtmlFormatter _sut = new();

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
    public void Format_ShouldBeHtml()
    {
        _sut.Format.Should().Be(ExportFormat.Html);
    }

    [Fact]
    public void FileExtension_ShouldBeHtml()
    {
        _sut.FileExtension.Should().Be(".html");
    }

    [Fact]
    public void MimeType_ShouldBeTextHtml()
    {
        _sut.MimeType.Should().Be("text/html");
    }

    // ====================================================================
    //  ExportConversationAsync (single)
    // ====================================================================

    [Fact]
    public async Task ExportConversationAsync_ProducesHtmlDocument()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("<!DOCTYPE html>");
        result.Should().Contain("</html>");
    }

    [Fact]
    public async Task ExportConversationAsync_ContainsConversationTitle()
    {
        // Arrange
        var conversation = CreateConversation(title: "My Special Chat");
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("My Special Chat");
    }

    [Fact]
    public async Task ExportConversationAsync_ContainsMessageContent()
    {
        // Arrange
        var conversation = CreateConversation(messageCount: 2);
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("Message 1");
        result.Should().Contain("Message 2");
    }

    [Fact]
    public async Task ExportConversationAsync_IncludesUserAndAssistantRoles()
    {
        // Arrange
        var conversation = CreateConversation(messageCount: 2);
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("class=\"message user\"");
        result.Should().Contain("class=\"message assistant\"");
    }

    [Fact]
    public async Task ExportConversationAsync_SkipsSystemMessagesFromBody()
    {
        // Arrange
        var conversation = CreateConversation(messageCount: 2, includeSystemMessage: true);
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert — system message should not appear as a message div
        result.Should().NotContain("class=\"message system\"");
    }

    [Fact]
    public async Task ExportConversationAsync_WithMetadata_IncludesMetadataSection()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions { IncludeMetadata = true };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("class=\"metadata\"");
        result.Should().Contain("Messages:");
        result.Should().Contain("Tokens:");
    }

    [Fact]
    public async Task ExportConversationAsync_WithTimestamps_IncludesTimestampDiv()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions { IncludeTimestamps = true };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("class=\"timestamp\"");
    }

    [Fact]
    public async Task ExportConversationAsync_WithSystemPrompt_IncludesSystemPromptSection()
    {
        // Arrange
        var conversation = CreateConversation();
        conversation.SystemPrompt = "You are an expert assistant.";
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("class=\"system-prompt\"");
        result.Should().Contain("You are an expert assistant.");
    }

    [Fact]
    public async Task ExportConversationAsync_HtmlEncodesSpecialCharacters()
    {
        // Arrange
        var messages = new List<MessageEntity>
        {
            new()
            {
                Id = 1,
                Role = "user",
                Content = "<script>alert('xss')</script>",
                Timestamp = DateTime.UtcNow,
                TokenCount = 5,
                SortOrder = 0,
            },
        };
        var conversation = new ConversationEntity
        {
            Id = 1,
            Title = "Test & <Chat>",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            MessageCount = 1,
            TokensUsed = 5,
            Messages = messages,
        };
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert — title and content should be HTML-encoded
        result.Should().Contain("Test &amp; &lt;Chat&gt;");
        result.Should().Contain("&lt;script&gt;");
        result.Should().NotContain("<script>alert");
    }

    [Fact]
    public async Task ExportConversationAsync_ContainsDocumentHeader()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("<html lang=\"en\">");
        result.Should().Contain("<meta charset=\"UTF-8\"");
        result.Should().Contain("<style>");
    }

    [Fact]
    public async Task ExportConversationAsync_ContainsDocumentFooter()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("class=\"footer\"");
        result.Should().Contain("Exported from Agent-X");
    }

    // ====================================================================
    //  ExportConversationsAsync (batch)
    // ====================================================================

    [Fact]
    public async Task ExportConversationsAsync_ProducesHtmlDocument()
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
        result.Should().Contain("<!DOCTYPE html>");
        result.Should().Contain("</html>");
    }

    [Fact]
    public async Task ExportConversationsAsync_IncludesAllConversationTitles()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            CreateConversation(title: "Alpha"),
            CreateConversation(title: "Beta"),
        };
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationsAsync(conversations, options);

        // Assert
        result.Should().Contain("Alpha");
        result.Should().Contain("Beta");
    }

    [Fact]
    public async Task ExportConversationsAsync_SeparatesConversationsWithDivider()
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
        result.Should().Contain("section-divider");
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
