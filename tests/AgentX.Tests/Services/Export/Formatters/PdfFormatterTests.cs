using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Export.Formatters;
using AgentX.Core.Services.Export.Models;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Export.Formatters;

/// <summary>
/// Unit tests for <see cref="PdfFormatter"/>.
/// Verifies that the formatter produces base64-encoded PDF output via the
/// <see cref="Formats.PdfExport"/> helper.
/// </summary>
public sealed class PdfFormatterTests
{
    private readonly PdfFormatter _sut = new();

    private static ConversationEntity CreateConversation(
        string title = "Test Conversation",
        int messageCount = 2,
        string? modelId = null,
        bool includeCitations = false)
    {
        var messages = new List<MessageEntity>();

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
    public void Format_ShouldBePdf()
    {
        _sut.Format.Should().Be(ExportFormat.Pdf);
    }

    [Fact]
    public void FileExtension_ShouldBePdf()
    {
        _sut.FileExtension.Should().Be(".pdf");
    }

    [Fact]
    public void MimeType_ShouldBeApplicationPdf()
    {
        _sut.MimeType.Should().Be("application/pdf");
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
    public async Task ExportConversationAsync_DecodesToPdfBytes()
    {
        // Arrange
        var conversation = CreateConversation();
        var options = new ExportOptions();

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);
        var bytes = Convert.FromBase64String(result);

        // Assert -- PDF files start with "%PDF"
        bytes.Should().NotBeEmpty();
        bytes[0].Should().Be((byte)'%');
        bytes[1].Should().Be((byte)'P');
        bytes[2].Should().Be((byte)'D');
        bytes[3].Should().Be((byte)'F');
    }

    [Fact]
    public async Task ExportConversationAsync_WithMetadata_ReturnsValidBase64()
    {
        // Arrange
        var conversation = CreateConversation(modelId: "gpt-4");
        var options = new ExportOptions { IncludeMetadata = true, IncludeTimestamps = true };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().NotBeNullOrEmpty();
        var bytes = Convert.FromBase64String(result);
        bytes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExportConversationAsync_WithCitations_ReturnsValidBase64()
    {
        // Arrange
        var conversation = CreateConversation(messageCount: 3, includeCitations: true);
        var options = new ExportOptions { IncludeCitations = true };

        // Act
        var result = await _sut.ExportConversationAsync(conversation, options);

        // Assert
        result.Should().NotBeNullOrEmpty();
        var bytes = Convert.FromBase64String(result);
        bytes.Should().NotBeEmpty();
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
        bytes[0].Should().Be((byte)'%');
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
