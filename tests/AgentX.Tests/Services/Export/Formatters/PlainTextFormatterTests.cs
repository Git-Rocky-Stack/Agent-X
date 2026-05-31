using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export.Formatters;
using AgentX.Core.Services.Export.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Export.Formatters;

/// <summary>
/// Unit tests for <see cref="PlainTextFormatter"/>.
/// Verifies that the formatter produces output identical to
/// <c>ExportService.BuildPlainText</c>.
/// </summary>
public sealed class PlainTextFormatterTests
{
    private readonly PlainTextFormatter _sut = new();

    private static ConversationEntity CreateConversation(
        string title = "Test Conversation",
        int messageCount = 1,
        string? systemPrompt = null,
        string? modelId = null,
        bool includeCitations = false)
    {
        var messages = new List<MessageEntity>();

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new MessageEntity
            {
                Id = 0,
                Role = "system",
                Content = systemPrompt,
                Timestamp = DateTime.UtcNow,
                SortOrder = 0,
            });
        }

        for (var i = 0; i < messageCount; i++)
        {
            var msg = new MessageEntity
            {
                Id = i + 1,
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"Message {i + 1}",
                Timestamp = DateTime.UtcNow,
                TokenCount = i % 2 == 1 ? 50 * (i + 1) : 0,
                SortOrder = i + 1,
                ModelId = i % 2 == 1 ? modelId : null,
                GenerationTimeMs = i % 2 == 1 ? 120 * (i + 1) : null,
            };

            if (includeCitations && i % 2 == 1)
            {
                msg.CitationsJson = "[{\"fileName\":\"report.docx\",\"pageNumber\":7,\"excerpt\":\"Key finding from the research report about quarterly results\"}]";
            }

            messages.Add(msg);
        }

        return new ConversationEntity
        {
            Id = 1,
            Title = title,
            CreatedAt = new DateTime(2026, 2, 20, 14, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 2, 20, 14, 30, 0, DateTimeKind.Utc),
            MessageCount = messages.Count,
            TokensUsed = 180,
            ModelId = modelId ?? string.Empty,
            SystemPrompt = systemPrompt,
            Messages = messages,
        };
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Properties
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Format_ShouldBePlainText()
    {
        _sut.Format.Should().Be(ExportFormat.PlainText);
    }

    [Fact]
    public void FileExtension_ShouldBeTxt()
    {
        _sut.FileExtension.Should().Be(".txt");
    }

    [Fact]
    public void MimeType_ShouldBeTextPlain()
    {
        _sut.MimeType.Should().Be("text/plain");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ExportConversationAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportConversationAsync_IncludesTitleWithEqualsUnderline()
    {
        // Arrange
        var conversation = CreateConversation(title: "My Chat");
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("My Chat");
        result.Should().Contain("======");
    }

    [Fact]
    public async Task ExportConversationAsync_WithMetadata_IncludesMetaBlock()
    {
        // Arrange
        var conversation = CreateConversation(modelId: "gpt-4");
        var options = new ExportOptions
        {
            IncludeMetadata = true,
        };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("Created:     2026-02-20 14:00:00 UTC");
        result.Should().Contain("Updated:     2026-02-20 14:30:00 UTC");
        result.Should().Contain("Messages:");
        result.Should().Contain("Tokens Used:");
        result.Should().Contain("Model:       gpt-4");
    }

    [Fact]
    public async Task ExportConversationAsync_WithoutMetadata_OmitsMetaBlock()
    {
        // Arrange
        var conversation = CreateConversation(modelId: "gpt-4");
        var options = new ExportOptions { IncludeMetadata = false };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().NotContain("Created:");
        result.Should().NotContain("Model:");
    }

    [Fact]
    public async Task ExportConversationAsync_WithTimestamps_IncludesTimestamps()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions { IncludeTimestamps = true };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert — timestamps are indented
        result.Should().Contain("  ");
        result.Should().Contain("UTC");
    }

    [Fact]
    public async Task ExportConversationAsync_WithModelInfo_IncludesModelLabel()
    {
        // Arrange
        var conversation = CreateConversation(messageCount: 2, modelId: "gpt-4o");
        var options = new ExportOptions
        {
            IncludeModelInfo = true,
            IncludeTimestamps = false,
        };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("  Model: gpt-4o");
    }

    [Fact]
    public async Task ExportConversationAsync_WithSystemPrompt_IncludesSystemPromptBlock()
    {
        // Arrange
        var conversation = CreateConversation(systemPrompt: "You are a helpful assistant.");
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("[System Prompt]");
        result.Should().Contain("You are a helpful assistant.");
    }

    [Fact]
    public async Task ExportConversationAsync_SkipsSystemRoleMessagesInBody()
    {
        // Arrange
        var conversation = CreateConversation(systemPrompt: "directive");
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert — "[System]" should not appear as a role label in the body
        var lines = result.Split('\n');
        var systemLabels = lines.Count(l => l.Trim() == "[System]");
        systemLabels.Should().Be(0);
    }

    [Fact]
    public async Task ExportConversationAsync_UsesRoleLabels()
    {
        // Arrange
        var conversation = CreateConversation(messageCount: 2);
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("[User]");
        result.Should().Contain("[Assistant]");
    }

    [Fact]
    public async Task ExportConversationAsync_WithAssistantMetadata_IncludesTokenAndGeneration()
    {
        // Arrange
        var conversation = CreateConversation(messageCount: 2);
        var options = new ExportOptions { IncludeMetadata = true };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert — metadata is indented in parentheses
        result.Should().Contain("(Tokens:");
        result.Should().Contain("Generation:");
        result.Should().Contain("ms)");
    }

    [Fact]
    public async Task ExportConversationAsync_WithCitations_IncludesCitationsSection()
    {
        // Arrange
        var conversation = CreateConversation(messageCount: 2, includeCitations: true);
        var options = new ExportOptions { IncludeCitations = true };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("Citations:");
        result.Should().Contain("1. report.docx, page 7");
    }

    [Fact]
    public async Task ExportConversationAsync_EndsWithExportFooter()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("Exported from Agent-X on");
    }

    [Fact]
    public async Task ExportConversationAsync_WithTitleOverride_UsesOverrideTitle()
    {
        // Arrange
        var conversation = CreateConversation(title: "Original Title");
        var options = new ExportOptions { Title = "Custom Plain Title" };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("Custom Plain Title");
        result.Should().NotContain("Original Title");
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

    // ══════════════════════════════════════════════════════════════════════
    //  ExportConversationsAsync (batch)
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportConversationsAsync_WithMultipleConversations_IncludesBatchHeader()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            CreateConversation(title: "Conv A"),
            CreateConversation(title: "Conv B"),
        };
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationsAsync(conversations, options);

        // Assert
        result.Should().Contain("Agent-X Conversations Export (2)");
        result.Should().Contain("Exported 2 conversations on");
    }

    [Fact]
    public async Task ExportConversationsAsync_SeparatesWithDashedLine()
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

        // Assert — 60-char dash separator between conversations
        result.Should().Contain(new string('-', 60));
    }

    [Fact]
    public async Task ExportConversationsAsync_WithCustomTitle_UsesCustomTitle()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            CreateConversation(),
        };
        var options = new ExportOptions { Title = "Custom Batch Title" };

        // Act
        var result = await _sut.ExportConversationsAsync(conversations, options);

        // Assert
        result.Should().Contain("Custom Batch Title");
    }
}
