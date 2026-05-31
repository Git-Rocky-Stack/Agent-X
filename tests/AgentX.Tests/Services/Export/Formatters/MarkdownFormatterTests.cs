using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export.Formatters;
using AgentX.Core.Services.Export.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Export.Formatters;

/// <summary>
/// Unit tests for <see cref="MarkdownFormatter"/>.
/// Verifies that the formatter produces output identical to
/// <c>ExportService.BuildMarkdown</c>.
/// </summary>
public sealed class MarkdownFormatterTests
{
    private readonly MarkdownFormatter _sut = new();

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
                msg.CitationsJson = "[{\"fileName\":\"doc.pdf\",\"pageNumber\":3,\"excerpt\":\"A relevant passage from the document that provides supporting evidence\"}]";
            }

            messages.Add(msg);
        }

        return new ConversationEntity
        {
            Id = 1,
            Title = title,
            CreatedAt = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 15, 11, 0, 0, DateTimeKind.Utc),
            MessageCount = messages.Count,
            TokensUsed = 250,
            ModelId = modelId ?? string.Empty,
            SystemPrompt = systemPrompt,
            Messages = messages,
        };
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Properties
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Format_ShouldBeMarkdown()
    {
        _sut.Format.Should().Be(ExportFormat.Markdown);
    }

    [Fact]
    public void FileExtension_ShouldBeMd()
    {
        _sut.FileExtension.Should().Be(".md");
    }

    [Fact]
    public void MimeType_ShouldBeTextMarkdown()
    {
        _sut.MimeType.Should().Be("text/markdown");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ExportConversationAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExportConversationAsync_WithMetadata_IncludesHeaderAndMeta()
    {
        // Arrange
        var conversation = CreateConversation(modelId: "gpt-4");
        var options = new ExportOptions
        {
            IncludeMetadata = true,
            IncludeTimestamps = true,
        };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("# Test Conversation");
        result.Should().Contain("**Created:** 2026-01-15 10:30:00 UTC");
        result.Should().Contain("**Updated:** 2026-01-15 11:00:00 UTC");
        result.Should().Contain("**Messages:**");
        result.Should().Contain("**Tokens Used:** 250");
        result.Should().Contain("**Model:** gpt-4");
        result.Should().Contain("## Conversation");
        result.Should().Contain("### User");
        result.Should().Contain("Message 1");
        result.Should().Contain("*Exported from Agent-X on");
    }

    [Fact]
    public async Task ExportConversationAsync_WithoutMetadata_OmitsMetaBlock()
    {
        // Arrange
        var conversation = CreateConversation(modelId: "gpt-4");
        var options = new ExportOptions
        {
            IncludeMetadata = false,
        };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().NotContain("**Created:**");
        result.Should().NotContain("**Model:**");
        result.Should().Contain("# Test Conversation");
        result.Should().Contain("## Conversation");
    }

    [Fact]
    public async Task ExportConversationAsync_WithTimestamps_IncludesTimestamps()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions { IncludeTimestamps = true };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("UTC*");
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
        result.Should().Contain("## System Prompt");
        result.Should().Contain("> You are a helpful assistant.");
    }

    [Fact]
    public async Task ExportConversationAsync_SkipsSystemRoleMessages()
    {
        // Arrange
        var conversation = CreateConversation(systemPrompt: "System directive");
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert — system messages should be shown in the System Prompt section,
        // but "### System" should NOT appear in the conversation body
        var lines = result.Split('\n');
        var systemHeaders = lines.Count(l => l.Trim().StartsWith("### System"));
        systemHeaders.Should().Be(0);
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

        // Assert — assistant message should include model info
        result.Should().Contain("*Model: gpt-4o*");
    }

    [Fact]
    public async Task ExportConversationAsync_WithAssistantMetadata_IncludesTokenAndGeneration()
    {
        // Arrange
        var conversation = CreateConversation(messageCount: 2);
        var options = new ExportOptions { IncludeMetadata = true };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("Tokens:");
        result.Should().Contain("Generation:");
        result.Should().Contain("ms");
    }

    [Fact]
    public async Task ExportConversationAsync_WithCitations_IncludesCitationsFootnotes()
    {
        // Arrange
        var conversation = CreateConversation(messageCount: 2, includeCitations: true);
        var options = new ExportOptions { IncludeCitations = true };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("## Citations");
        result.Should().Contain("1. doc.pdf, page 3");
        result.Should().Contain("A relevant passage from the document that provides supporting evidence");
    }

    [Fact]
    public async Task ExportConversationAsync_WithTitleOverride_UsesOverrideTitle()
    {
        // Arrange
        var conversation = CreateConversation(title: "Original Title");
        var options = new ExportOptions { Title = "Custom Export Title" };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("# Custom Export Title");
        result.Should().NotContain("# Original Title");
    }

    [Fact]
    public async Task ExportConversationAsync_EscapesBracketInTitle()
    {
        // Arrange
        var conversation = CreateConversation(title: "Test [Draft]");
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().Contain("# Test \\[Draft\\]");
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
        result.Should().Contain("# Agent-X Conversations Export (2)");
        result.Should().Contain("*Exported 2 conversations on");
        result.Should().Contain("# Conv A");
        result.Should().Contain("# Conv B");
    }

    [Fact]
    public async Task ExportConversationsAsync_WithCustomTitle_UsesCustomTitle()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            CreateConversation(title: "Conv A"),
        };
        var options = new ExportOptions { Title = "Batch Export" };

        // Act
        var result = await _sut.ExportConversationsAsync(conversations, options);

        // Assert
        result.Should().Contain("# Batch Export");
    }

    [Fact]
    public async Task ExportConversationsAsync_SeparatesConversationsWithHorizontalRule()
    {
        // Arrange
        var conversations = new List<ConversationEntity>
        {
            CreateConversation(title: "First"),
            CreateConversation(title: "Second"),
        };
        var options = new ExportOptions { IncludeMetadata = false };

        // Act
        var result = await _sut.ExportConversationsAsync(conversations, options);

        // Assert — should have at least one "---" separator between conversations
        result.Should().Contain("---");
    }
}
