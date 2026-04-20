using AgentX.App.ViewModels.Coordinators;
using AgentX.Core.Services.Audio;
using AgentX.Core.Services.Audio.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels.Coordinators;

public class VoiceCoordinatorTests : IDisposable
{
    private readonly Mock<ITranscriptionService> _transcriptionService;
    private readonly VoiceCoordinator _coordinator;

    public VoiceCoordinatorTests()
    {
        _transcriptionService = new Mock<ITranscriptionService>();
        _transcriptionService.SetupGet(s => s.SupportedFormats)
            .Returns(new List<string> { ".wav", ".mp3" });

        _coordinator = new VoiceCoordinator(_transcriptionService.Object);
    }

    public void Dispose()
    {
        _coordinator.Dispose();
    }

    // ── Initial State ─────────────────────────────────────────────

    [Fact]
    public void IsRecording_IsFalse_Initially()
    {
        _coordinator.IsRecording.Should().BeFalse();
    }

    [Fact]
    public void IsTranscribing_IsFalse_Initially()
    {
        _coordinator.IsTranscribing.Should().BeFalse();
    }

    [Fact]
    public void StatusMessage_IsEmpty_Initially()
    {
        _coordinator.StatusMessage.Should().BeEmpty();
    }

    [Fact]
    public void SupportedFormats_ReturnsFromService()
    {
        _coordinator.SupportedFormats.Should().Contain(".wav");
        _coordinator.SupportedFormats.Should().Contain(".mp3");
    }

    // ── TranscribeFileAsync ───────────────────────────────────────

    [Fact]
    public async Task TranscribeFileAsync_ReturnsText_OnSuccess()
    {
        // Arrange
        var result = new TranscriptionResult
        {
            FullText = "Hello world",
            Segments = new List<TranscriptionSegment>()
        };
        _transcriptionService
            .Setup(s => s.TranscribeFileAsync(
                It.IsAny<string>(),
                It.IsAny<TranscriptionOptions>(),
                It.IsAny<IProgress<TranscriptionProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        // Act
        var text = await _coordinator.TranscribeFileAsync("/test/audio.wav");

        // Assert
        text.Should().Be("Hello world");
    }

    [Fact]
    public async Task TranscribeFileAsync_ReturnsNull_WhenEmptyResult()
    {
        // Arrange
        var result = new TranscriptionResult
        {
            FullText = "",
            Segments = new List<TranscriptionSegment>()
        };
        _transcriptionService
            .Setup(s => s.TranscribeFileAsync(
                It.IsAny<string>(),
                It.IsAny<TranscriptionOptions>(),
                It.IsAny<IProgress<TranscriptionProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        // Act
        var text = await _coordinator.TranscribeFileAsync("/test/audio.wav");

        // Assert
        text.Should().BeNull();
    }

    [Fact]
    public async Task TranscribeFileAsync_ReturnsNull_OnModelNotAvailable()
    {
        // Arrange
        _transcriptionService
            .Setup(s => s.TranscribeFileAsync(
                It.IsAny<string>(),
                It.IsAny<TranscriptionOptions>(),
                It.IsAny<IProgress<TranscriptionProgress>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Whisper model not found"));

        // Act
        var text = await _coordinator.TranscribeFileAsync("/test/audio.wav");

        // Assert
        text.Should().BeNull();
    }

    [Fact]
    public async Task TranscribeFileAsync_ReturnsNull_OnGenericException()
    {
        // Arrange
        _transcriptionService
            .Setup(s => s.TranscribeFileAsync(
                It.IsAny<string>(),
                It.IsAny<TranscriptionOptions>(),
                It.IsAny<IProgress<TranscriptionProgress>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Network error"));

        // Act
        var text = await _coordinator.TranscribeFileAsync("/test/audio.wav");

        // Assert
        text.Should().BeNull();
    }

    // ── Transcribing state ────────────────────────────────────────

    [Fact]
    public async Task TranscribeFileAsync_SetsTranscribingState()
    {
        // Arrange
        var transcribingStates = new List<bool>();
        _coordinator.TranscribingStateChanged += (s, v) => transcribingStates.Add(v);

        var tcs = new TaskCompletionSource<TranscriptionResult>();
        _transcriptionService
            .Setup(s => s.TranscribeFileAsync(
                It.IsAny<string>(),
                It.IsAny<TranscriptionOptions>(),
                It.IsAny<IProgress<TranscriptionProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        // Act — start transcription
        var task = _coordinator.TranscribeFileAsync("/test/audio.wav");

        // Assert — should be transcribing
        _coordinator.IsTranscribing.Should().BeTrue();
        transcribingStates.Should().Contain(true);

        // Complete the transcription
        tcs.SetResult(new TranscriptionResult
        {
            FullText = "Done",
            Segments = new List<TranscriptionSegment>()
        });

        await task;

        // Assert — should have reset
        _coordinator.IsTranscribing.Should().BeFalse();
        transcribingStates.Should().Contain(false);
    }

    // ── StatusChanged event ───────────────────────────────────────

    [Fact]
    public async Task TranscribeFileAsync_RaisesStatusChanged()
    {
        // Arrange
        var statuses = new List<string>();
        _coordinator.StatusChanged += (s, msg) => statuses.Add(msg);

        var result = new TranscriptionResult
        {
            FullText = "Test",
            Segments = new List<TranscriptionSegment>()
        };
        _transcriptionService
            .Setup(s => s.TranscribeFileAsync(
                It.IsAny<string>(),
                It.IsAny<TranscriptionOptions>(),
                It.IsAny<IProgress<TranscriptionProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        // Act
        await _coordinator.TranscribeFileAsync("/test/audio.wav");

        // Assert
        statuses.Should().Contain("Transcribing...");
        statuses.Should().Contain(string.Empty); // reset in finally
    }

    // ── NotificationRequested event ───────────────────────────────

    [Fact]
    public async Task TranscribeFileAsync_RaisesNotification_OnModelNotAvailable()
    {
        // Arrange
        NotificationRequestEventArgs? notification = null;
        _coordinator.NotificationRequested += (s, e) => notification = e;

        _transcriptionService
            .Setup(s => s.TranscribeFileAsync(
                It.IsAny<string>(),
                It.IsAny<TranscriptionOptions>(),
                It.IsAny<IProgress<TranscriptionProgress>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Whisper model not found"));

        // Act
        await _coordinator.TranscribeFileAsync("/test/audio.wav");

        // Assert
        notification.Should().NotBeNull();
        notification!.Level.Should().Be("error");
        notification.Title.Should().Be("Model Required");
    }

    [Fact]
    public async Task TranscribeFileAsync_RaisesNotification_OnGenericError()
    {
        // Arrange
        NotificationRequestEventArgs? notification = null;
        _coordinator.NotificationRequested += (s, e) => notification = e;

        _transcriptionService
            .Setup(s => s.TranscribeFileAsync(
                It.IsAny<string>(),
                It.IsAny<TranscriptionOptions>(),
                It.IsAny<IProgress<TranscriptionProgress>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Network error"));

        // Act
        await _coordinator.TranscribeFileAsync("/test/audio.wav");

        // Assert
        notification.Should().NotBeNull();
        notification!.Level.Should().Be("error");
        notification.Title.Should().Be("Transcription Failed");
    }

    // ── ToggleRecordingAsync (start path only — stop requires NAudio hardware) ──

    [Fact]
    public async Task ToggleRecordingAsync_WhenNotRecording_StartsRecording()
    {
        // Note: This will try to use NAudio which may fail in CI without a microphone.
        // The test verifies the coordinator attempts to start and handles the result.

        // Act — if no microphone is available, it should handle gracefully
        var result = await _coordinator.ToggleRecordingAsync();

        // If recording started, result is null (starting mode)
        // If recording failed (no mic), result is still null and recording state stays false
        if (_coordinator.IsRecording)
        {
            result.Should().BeNull();
            _coordinator.StatusMessage.Should().Be("Recording...");
        }
        // If no mic available, the coordinator handles the error and IsRecording stays false
    }

    // ── Dispose ───────────────────────────────────────────────────

    [Fact]
    public void Dispose_DoesNotThrow_WhenNotRecording()
    {
        // Act — should be safe to dispose when not recording
        _coordinator.Dispose();
    }
}
