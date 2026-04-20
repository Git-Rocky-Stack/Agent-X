using System.IO.Compression;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export.Formatters;
using AgentX.Core.Services.Export.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Export.Formatters;

/// <summary>
/// Unit tests for <see cref="DocxFormatter"/>.
/// Verifies that the formatter produces valid base64-encoded DOCX output
/// using the OpenXML SDK.
/// </summary>
public sealed class DocxFormatterTests
{
    private readonly DocxFormatter _sut = new();

    private static ConversationEntity CreateConversation(
        string title = "Test Conversation",
        int messageCount = 2,
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
                msg.CitationsJson = "[{\"fileName\":\"doc.pdf\",\"pageNumber\":3,\"excerpt\":\"A relevant passage from the document\"}]";
            }

            messages.Add(msg);
        }

        return new ConversationEntity
        {
            Id = 1,
            Title = title,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            MessageCount = messages.Count,
            TokensUsed = 250,
            ModelId = modelId,
            SystemPrompt = systemPrompt,
            Messages = messages,
        };
    }

    // ====================================================================
    //  Properties
    // ====================================================================

    [Fact]
    public void Format_ShouldBeDocx()
    {
        _sut.Format.Should().Be(ExportFormat.Docx);
    }

    [Fact]
    public void FileExtension_ShouldBeDocx()
    {
        _sut.FileExtension.Should().Be(".docx");
    }

    [Fact]
    public void MimeType_ShouldBeWordDocument()
    {
        _sut.MimeType.Should().Be("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    }

    // ====================================================================
    //  ExportConversationAsync (single)
    // ====================================================================

    [Fact]
    public async Task ExportConversationAsync_ReturnsValidBase64()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().NotBeNullOrEmpty();

        // Verify it is valid base64 by decoding without exception
        var act = () => Convert.FromBase64String(result);
        act.Should().NotThrow("because the result should be valid base64");
    }

    [Fact]
    public async Task ExportConversationAsync_DecodesToValidDocx()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);
        var bytes = Convert.FromBase64String(result);

        // Assert -- DOCX is an OpenXML zip; should be openable as a zip archive
        bytes.Should().NotBeEmpty();

        using var ms = new MemoryStream(bytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        archive.Entries.Should().NotBeEmpty("because a valid DOCX is a zip archive with entries");

        // DOCX must contain [Content_Types].xml and word/document.xml
        archive.GetEntry("[Content_Types].xml").Should().NotBeNull();
        archive.GetEntry("word/document.xml").Should().NotBeNull();
    }

    [Fact]
    public async Task ExportConversationAsync_WithMetadata_ProducesValidDocx()
    {
        // Arrange
        var conversation = CreateConversation(modelId: "gpt-4");
        var options = new ExportOptions { IncludeMetadata = true, IncludeTimestamps = true };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);
        var bytes = Convert.FromBase64String(result);

        // Assert
        bytes.Should().NotBeEmpty();

        using var ms = new MemoryStream(bytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        archive.GetEntry("word/document.xml").Should().NotBeNull();
    }

    [Fact]
    public async Task ExportConversationAsync_WithSystemPrompt_ProducesValidDocx()
    {
        // Arrange
        var conversation = CreateConversation(systemPrompt: "You are a helpful coding assistant.");
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);
        var bytes = Convert.FromBase64String(result);

        // Assert
        bytes.Should().NotBeEmpty();

        using var ms = new MemoryStream(bytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        archive.GetEntry("word/document.xml").Should().NotBeNull();
    }

    [Fact]
    public async Task ExportConversationAsync_WithCitations_ProducesValidDocx()
    {
        // Arrange
        var conversation = CreateConversation(messageCount: 3, includeCitations: true);
        var options = new ExportOptions { IncludeCitations = true };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);
        var bytes = Convert.FromBase64String(result);

        // Assert
        bytes.Should().NotBeEmpty();

        using var ms = new MemoryStream(bytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        archive.GetEntry("word/document.xml").Should().NotBeNull();
    }

    // ====================================================================
    //  ExportConversationsAsync (batch)
    // ====================================================================

    [Fact]
    public async Task ExportConversationsAsync_ReturnsValidBase64()
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
        result.Should().NotBeNullOrEmpty();
        var bytes = Convert.FromBase64String(result);
        bytes.Should().NotBeEmpty();

        using var ms = new MemoryStream(bytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        archive.GetEntry("word/document.xml").Should().NotBeNull();
    }

    // ====================================================================
    //  Empty conversation
    // ====================================================================

    [Fact]
    public async Task ExportConversationAsync_HandlesEmptyConversation()
    {
        // Arrange
        var conversation = CreateConversation(messageCount: 0);
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().NotBeNullOrEmpty();
        var bytes = Convert.FromBase64String(result);
        bytes.Should().NotBeEmpty();

        using var ms = new MemoryStream(bytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        archive.GetEntry("word/document.xml").Should().NotBeNull();
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
