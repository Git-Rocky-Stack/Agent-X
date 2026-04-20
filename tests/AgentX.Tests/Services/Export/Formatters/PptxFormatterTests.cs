using System.IO.Compression;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export.Formatters;
using AgentX.Core.Services.Export.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Export.Formatters;

/// <summary>
/// Unit tests for <see cref="PptxFormatter"/>.
/// Verifies that the formatter produces valid base64-encoded PPTX output
/// using the OpenXML SDK.
/// </summary>
public sealed class PptxFormatterTests
{
    private readonly PptxFormatter _sut = new();

    private static ConversationEntity CreateConversation(
        string title = "Test Conversation",
        int messageCount = 2,
        string? modelId = null)
    {
        var messages = new List<MessageEntity>();

        for (var i = 0; i < messageCount; i++)
        {
            messages.Add(new MessageEntity
            {
                Id = i + 1,
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"Message {i + 1}",
                Timestamp = DateTime.UtcNow,
                TokenCount = i % 2 == 1 ? 50 * (i + 1) : 0,
                SortOrder = i + 1,
                ModelId = i % 2 == 1 ? modelId : null,
            });
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
            Messages = messages,
        };
    }

    // ====================================================================
    //  Properties
    // ====================================================================

    [Fact]
    public void Format_ShouldBePptx()
    {
        _sut.Format.Should().Be(ExportFormat.Pptx);
    }

    [Fact]
    public void FileExtension_ShouldBePptx()
    {
        _sut.FileExtension.Should().Be(".pptx");
    }

    [Fact]
    public void MimeType_ShouldBePresentation()
    {
        _sut.MimeType.Should().Be("application/vnd.openxmlformats-officedocument.presentationml.presentation");
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
    public async Task ExportConversationAsync_DecodesToValidPptx()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);
        var bytes = Convert.FromBase64String(result);

        // Assert -- PPTX is an OpenXML zip; should be openable as a zip archive
        bytes.Should().NotBeEmpty();

        using var ms = new MemoryStream(bytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        archive.Entries.Should().NotBeEmpty("because a valid PPTX is a zip archive with entries");

        // PPTX must contain [Content_Types].xml and ppt/presentation.xml
        archive.GetEntry("[Content_Types].xml").Should().NotBeNull();
        archive.GetEntry("ppt/presentation.xml").Should().NotBeNull();
    }

    [Fact]
    public async Task ExportConversationAsync_WithMetadata_ProducesValidPptx()
    {
        // Arrange
        var conversation = CreateConversation(modelId: "gpt-4");
        var options = new ExportOptions { IncludeMetadata = true };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);
        var bytes = Convert.FromBase64String(result);

        // Assert
        bytes.Should().NotBeEmpty();

        using var ms = new MemoryStream(bytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        archive.GetEntry("ppt/presentation.xml").Should().NotBeNull();
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
        archive.GetEntry("ppt/presentation.xml").Should().NotBeNull();
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

        // Assert -- should still produce a valid PPTX with just the title slide
        result.Should().NotBeNullOrEmpty();
        var bytes = Convert.FromBase64String(result);
        bytes.Should().NotBeEmpty();

        using var ms = new MemoryStream(bytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        archive.GetEntry("ppt/presentation.xml").Should().NotBeNull();
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
