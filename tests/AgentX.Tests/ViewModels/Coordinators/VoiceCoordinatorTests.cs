using AgentX.App.ViewModels.Coordinators;
using AgentX.Core.Services.Audio;
using AgentX.Core.Services.Audio.Models;
using AgentX.Core.Services.Localization;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels.Coordinators;

public class VoiceCoordinatorTests : IDisposable
{
    private readonly Mock<ITranscriptionService> _transcriptionService;
    private readonly Mock<ILocalizationService> _localization = new();
    private readonly VoiceCoordinator _coordinator;

    public VoiceCoordinatorTests()
    {
        _transcriptionService = new Mock<ITranscriptionService>();
        _transcriptionService.SetupGet(s => s.SupportedFormats)
            .Returns(new List<string> { ".wav", ".mp3" });

        // Resource lookups come back as their keys, followed by their arguments.
        _localization.Setup(l => l.GetString(It.IsAny<string>())).Returns((string key) => key);
        _localization.Setup(l => l.GetString(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] args) => $"{key}: {string.Join(" ", args)}");

        _coordinator = new VoiceCoordinator(_transcriptionService.Object, _localization.Object);
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
            .ThrowsAsync(new TranscriptionModelMissingException("base", "/models/ggml-base.bin"));

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

        // Assert: the status comes from the resources
        statuses.Should().Contain("Voice_Transcribing");
        statuses.Should().Contain(string.Empty); // reset in finally
    }

    [Fact]
    public void DescribePhase_ShowsEveryPhaseTheTranscriptionServiceReports_InTheUsersLanguage()
    {
        // The service names its phases in English; each one it reports must map to a resource.
        var phases = TranscriptionServicePhases();
        phases.Should().Contain(new[] { "Loading model...", "Transcribing..." }, "the scan must find the phases");

        foreach (var phase in phases)
        {
            _coordinator.DescribePhase(phase).Should().StartWith("Voice_", "\"{0}\" is shown to the user", phase);
        }

        _coordinator.DescribePhase("Transcribing...").Should().Be("Voice_Transcribing");
    }

    [Fact]
    public void DescribePhase_ShowsAnUnknownPhaseAsItCame()
    {
        _coordinator.DescribePhase("Warming up...").Should().Be("Warming up...");
    }

    private static IReadOnlyList<string> TranscriptionServicePhases()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "src", "AgentX.Core", "Services", "Audio", "TranscriptionService.cs")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the transcription service source must be found");
        var source = File.ReadAllText(
            Path.Combine(directory!.FullName, "src", "AgentX.Core", "Services", "Audio", "TranscriptionService.cs"));

        return System.Text.RegularExpressions.Regex
            .Matches(source, @"ReportProgress\(\s*progress\s*,[^,]+,\s*""(?<phase>[^""]+)""")
            .Select(match => match.Groups["phase"].Value)
            .Distinct()
            .ToList();
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
            .ThrowsAsync(new TranscriptionModelMissingException("base", "/models/ggml-base.bin"));

        // Act
        await _coordinator.TranscribeFileAsync("/test/audio.wav");

        // Assert: the text comes from the resources, and it names the Model Manager page (it
        // used to send the user to a "Settings > Voice" page that does not exist).
        notification.Should().NotBeNull();
        notification!.Level.Should().Be("error");
        notification.Title.Should().Be("Voice_ModelRequiredTitle");
        notification.Message.Should().Be("Voice_ModelRequiredMessage");
    }

    [Fact]
    public async Task TranscribeFileAsync_DamagedModel_IsReportedAsAFailure_NotAsAMissingModel()
    {
        // A model that is installed but cannot be loaded mentions "model" in its message. The
        // coordinator used to match on that word and ask for a download the user already made.
        NotificationRequestEventArgs? notification = null;
        _coordinator.NotificationRequested += (s, e) => notification = e;

        _transcriptionService
            .Setup(s => s.TranscribeFileAsync(
                It.IsAny<string>(),
                It.IsAny<TranscriptionOptions>(),
                It.IsAny<IProgress<TranscriptionProgress>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The speech-to-text model file could not be loaded."));

        await _coordinator.TranscribeFileAsync("/test/audio.wav");

        notification.Should().NotBeNull();
        notification!.Title.Should().Be("Voice_TranscriptionFailedTitle");
        notification.Message.Should().Be(
            "Voice_FileTranscriptionFailed: The speech-to-text model file could not be loaded.");
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
        notification.Title.Should().Be("Voice_TranscriptionFailedTitle");
        notification.Message.Should().Be("Voice_FileTranscriptionFailed: Network error");
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
            _coordinator.StatusMessage.Should().Be("Voice_Recording");
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
