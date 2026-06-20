using AgentX.App.ViewModels;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Services.Screen;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.Services.Screen;

/// <summary>
/// Unit tests for <see cref="QuickChatViewModel"/> screen awareness integration.
/// Tests verify that screen context is correctly injected into the AI prompt
/// when screen awareness is available, and that the ViewModel handles disabled
/// or failing screen capture gracefully.
/// </summary>
public sealed class QuickChatViewModelScreenTests
{
    private readonly Mock<IAiService> _mockAiService;
    private readonly Mock<IScreenCaptureService> _mockScreenCapture;

    public QuickChatViewModelScreenTests()
    {
        _mockAiService = new Mock<IAiService>();
        _mockScreenCapture = new Mock<IScreenCaptureService>();
    }

    // ── Constructor ────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_WithScreenCaptureService_AcceptsService()
    {
        // Act
        var vm = CreateViewModel();

        // Assert — should not throw
        vm.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullScreenCaptureService_AcceptsNull()
    {
        // Act
        var vm = new QuickChatViewModel(_mockAiService.Object);

        // Assert — should not throw; screen context simply won't be captured
        vm.Should().NotBeNull();
    }

    // ── Screen context captured flag ────────────────────────────────────────────

    [Fact]
    public async Task SubmitQueryAsync_WithScreenContext_SetsScreenContextCapturedTrue()
    {
        // Arrange
        SetupStreamingResponse("AI response");

        _mockScreenCapture
            .Setup(s => s.CaptureActiveWindowAndOcrAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScreenContextResult
            {
                OcrText = "Hello from screen",
                ActiveWindowTitle = "Test Window",
                CapturedAtUtc = DateTime.UtcNow,
            });

        var vm = CreateViewModel();
        vm.QueryText = "What is on my screen?";

        // Act
        await vm.SubmitQueryCommand.ExecuteAsync(null);

        // Assert
        vm.ScreenContextCaptured.Should().BeTrue();
    }

    [Fact]
    public async Task SubmitQueryAsync_WithEmptyScreenContext_SetsScreenContextCapturedFalse()
    {
        // Arrange
        SetupStreamingResponse("AI response");

        _mockScreenCapture
            .Setup(s => s.CaptureActiveWindowAndOcrAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScreenContextResult()); // Empty

        var vm = CreateViewModel();
        vm.QueryText = "What is on my screen?";

        // Act
        await vm.SubmitQueryCommand.ExecuteAsync(null);

        // Assert
        vm.ScreenContextCaptured.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitQueryAsync_WhenScreenCaptureFails_SetsScreenContextCapturedFalse()
    {
        // Arrange
        SetupStreamingResponse("AI response");

        _mockScreenCapture
            .Setup(s => s.CaptureActiveWindowAndOcrAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Capture failed"));

        var vm = CreateViewModel();
        vm.QueryText = "What is on my screen?";

        // Act
        await vm.SubmitQueryCommand.ExecuteAsync(null);

        // Assert — failure should be caught gracefully, query still proceeds
        vm.ScreenContextCaptured.Should().BeFalse();
        vm.ResponseText.Should().Be("AI response");
    }

    // ── System prompt construction ─────────────────────────────────────────────

    [Fact]
    public async Task SubmitQueryAsync_WithScreenContext_PassesContextToAiService()
    {
        // Arrange
        string? capturedSystemPrompt = null;

        _mockAiService
            .Setup(s => s.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ChatMessage>, string?, ChatOptions?, CancellationToken>(
                (messages, systemPrompt, options, ct) =>
                {
                    capturedSystemPrompt = systemPrompt;
                })
            .Returns((IReadOnlyList<ChatMessage> messages, string? systemPrompt, ChatOptions? options, CancellationToken ct) =>
                StreamTokensAsync("AI response"));

        _mockScreenCapture
            .Setup(s => s.CaptureActiveWindowAndOcrAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScreenContextResult
            {
                OcrText = "Screen OCR text",
                ActiveWindowTitle = "Visual Studio",
                CapturedAtUtc = DateTime.UtcNow,
            });

        var vm = CreateViewModel();
        vm.QueryText = "What is on my screen?";

        // Act
        await vm.SubmitQueryCommand.ExecuteAsync(null);

        // Assert — system prompt should contain screen context markers
        capturedSystemPrompt.Should().NotBeNull();
        capturedSystemPrompt.Should().Contain("--- SCREEN CONTEXT ---");
        capturedSystemPrompt.Should().Contain("Screen OCR text");
        capturedSystemPrompt.Should().Contain("Visual Studio");
    }

    [Fact]
    public async Task SubmitQueryAsync_WithoutScreenContext_DoesNotIncludeScreenMarkers()
    {
        // Arrange
        string? capturedSystemPrompt = null;

        _mockAiService
            .Setup(s => s.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ChatMessage>, string?, ChatOptions?, CancellationToken>(
                (messages, systemPrompt, options, ct) =>
                {
                    capturedSystemPrompt = systemPrompt;
                })
            .Returns((IReadOnlyList<ChatMessage> messages, string? systemPrompt, ChatOptions? options, CancellationToken ct) =>
                StreamTokensAsync("AI response"));

        // No screen capture service (null) — no screen context
        var vm = new QuickChatViewModel(_mockAiService.Object);
        vm.QueryText = "Tell me about AI";

        // Act
        await vm.SubmitQueryCommand.ExecuteAsync(null);

        // Assert — system prompt should NOT contain screen context markers
        capturedSystemPrompt.Should().NotBeNull();
        capturedSystemPrompt.Should().NotContain("--- SCREEN CONTEXT ---");
    }

    // ── Clear resets screen context ─────────────────────────────────────────────

    [Fact]
    public void Clear_ResetsScreenContextCaptured()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.ScreenContextCaptured = true;

        // Act
        vm.ClearCommand.Execute(null);

        // Assert
        vm.ScreenContextCaptured.Should().BeFalse();
    }

    // ── Query still works without screen capture service ────────────────────────

    [Fact]
    public async Task SubmitQueryAsync_WithoutScreenCaptureService_StillWorks()
    {
        // Arrange
        SetupStreamingResponse("AI response without screen context");

        var vm = new QuickChatViewModel(_mockAiService.Object);
        vm.QueryText = "Hello";

        // Act
        await vm.SubmitQueryCommand.ExecuteAsync(null);

        // Assert
        vm.ResponseText.Should().Be("AI response without screen context");
        vm.ScreenContextCaptured.Should().BeFalse();
    }

    // ── Status message ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitQueryAsync_WhenProcessingComplete_StatusIsDone()
    {
        // Arrange
        SetupStreamingResponse("Response text");

        _mockScreenCapture
            .Setup(s => s.CaptureActiveWindowAndOcrAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScreenContextResult());

        var vm = CreateViewModel();
        vm.QueryText = "Test query";

        // Act
        await vm.SubmitQueryCommand.ExecuteAsync(null);

        // Assert
        vm.StatusMessage.Should().Be("Done");
    }

    [Fact]
    public async Task SubmitQueryAsync_WhenScreenCaptureFails_StatusStillDone()
    {
        // Arrange
        SetupStreamingResponse("Response text");

        _mockScreenCapture
            .Setup(s => s.CaptureActiveWindowAndOcrAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Screen capture crashed"));

        var vm = CreateViewModel();
        vm.QueryText = "Test query";

        // Act
        await vm.SubmitQueryCommand.ExecuteAsync(null);

        // Assert — should still succeed with AI response
        vm.StatusMessage.Should().Be("Done");
        vm.ResponseText.Should().Be("Response text");
    }

    // ── Cancellation ────────────────────────────────────────────────────────────

    [Fact]
    public void CancelQuery_StopsProcessing()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act — should not throw even when nothing is in progress
        vm.CancelQuery();

        // Assert
        vm.IsProcessing.Should().BeFalse();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private QuickChatViewModel CreateViewModel()
    {
        return new QuickChatViewModel(_mockAiService.Object, _mockScreenCapture.Object);
    }

    /// <summary>
    /// Creates an <see cref="IAsyncEnumerable{String}"/> that yields the given tokens.
    /// Required because yield return cannot be used inside lambda expressions.
    /// </summary>
    private static async IAsyncEnumerable<string> StreamTokensAsync(params string[] tokens)
    {
        foreach (var token in tokens)
        {
            await Task.Yield();
            yield return token;
        }
    }

    /// <summary>
    /// Sets up the mock AI service to stream the given response tokens.
    /// </summary>
    private void SetupStreamingResponse(string response)
    {
        _mockAiService
            .Setup(s => s.StreamChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string?>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<ChatMessage> messages, string? systemPrompt, ChatOptions? options, CancellationToken ct) =>
                StreamTokensAsync(response));
    }
}
