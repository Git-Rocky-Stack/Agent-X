using AgentX.Core.Documents.Processors;
using AgentX.Core.Services.Audio;
using AgentX.Core.Services.Audio.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.Documents.Processors;

/// <summary>
/// Tests for <see cref="AudioProcessor"/>, the audio-transcription document importer.
/// <para>
/// Like <see cref="WebProcessor"/>, this shipped fully implemented but unregistered, so no
/// user ever reached it. These tests drive the real processor against real temp files with
/// only <see cref="ITranscriptionService"/> mocked, and cover every degraded path: the
/// Whisper runtime missing, the model not downloaded, cancellation, and unexpected faults.
/// </para>
/// </summary>
public sealed class AudioProcessorTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<ITranscriptionService> _transcription = new(MockBehavior.Strict);
    private readonly AudioProcessor _processor;

    public AudioProcessorTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "agentx-audioprocessor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _processor = new AudioProcessor(_transcription.Object);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch (IOException) { }
    }

    // ── CanProcess / SupportedExtensions ─────────────────────────────────────

    [Theory]
    [InlineData("talk.mp3", true)]
    [InlineData("talk.MP3", true)]
    [InlineData("talk.wav", true)]
    [InlineData("talk.m4a", true)]
    [InlineData("talk.flac", true)]
    [InlineData("talk.ogg", true)]
    [InlineData("talk.webm", true)]
    [InlineData("talk.mp4", false)]
    [InlineData("notes.txt", false)]
    [InlineData("no-extension", false)]
    public void CanProcess_MatchesOnlyAudioExtensions(string fileName, bool expected)
    {
        _processor.CanProcess(fileName).Should().Be(expected);
    }

    [Fact]
    public void SupportedExtensions_CoverTheSixAdvertisedAudioFormats()
    {
        _processor.SupportedExtensions.Should().BeEquivalentTo(
            new[] { ".mp3", ".wav", ".m4a", ".flac", ".ogg", ".webm" });
    }

    [Fact]
    public async Task ProcessAsync_MissingFile_ThrowsFileNotFound()
    {
        var act = () => _processor.ProcessAsync(Path.Combine(_tempDirectory, "nope.mp3"));

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_TranscribesAudioIntoASearchableDocument()
    {
        var path = WriteAudio("interview.mp3");
        ExpectTranscription(path, new TranscriptionResult
        {
            FullText = "Hello there. General Kenobi.",
            Language = "en",
            DurationMs = 65_000,
            ModelUsed = "base",
            Segments = new[]
            {
                new TranscriptionSegment { StartMs = 0, EndMs = 2_000, Text = "Hello there." },
                new TranscriptionSegment { StartMs = 2_000, EndMs = 4_000, Text = "General Kenobi." },
            },
        });

        var document = await _processor.ProcessAsync(path);

        document.FileType.Should().Be("mp3");
        document.ExtractedTitle.Should().Be("interview");
        document.Language.Should().Be("en");
        document.ExtractedText.Should().Contain("=== Audio Transcript ===");
        document.ExtractedText.Should().Contain("Hello there.");
        document.ExtractedText.Should().Contain("General Kenobi.");
        document.WordCount.Should().BeGreaterThan(0);
        document.ContentHash.Should().NotBeNullOrWhiteSpace();
        document.Metadata.Custom["audioFormat"].Should().Be("mp3");
        document.Metadata.Custom["modelUsed"].Should().Be("base");
        document.Metadata.Custom["segmentCount"].Should().Be("2");
        document.Metadata.Custom["durationMs"].Should().Be("65000");
        document.Metadata.Custom["detectedLanguage"].Should().Be("en");
    }

    [Fact]
    public async Task ProcessAsync_WithSpeakerIds_RecordsDistinctSpeakerCount()
    {
        var path = WriteAudio("meeting.wav");
        ExpectTranscription(path, new TranscriptionResult
        {
            ModelUsed = "base",
            DurationMs = 1_000,
            Segments = new[]
            {
                new TranscriptionSegment { StartMs = 0, EndMs = 500, Text = "One.", SpeakerId = 1 },
                new TranscriptionSegment { StartMs = 500, EndMs = 900, Text = "Two.", SpeakerId = 2 },
                new TranscriptionSegment { StartMs = 900, EndMs = 1_000, Text = "Again.", SpeakerId = 1 },
            },
        });

        var document = await _processor.ProcessAsync(path);

        document.Metadata.Custom["speakerCount"].Should().Be("2");
    }

    [Fact]
    public async Task ProcessAsync_WithoutSpeakerIds_OmitsSpeakerCount()
    {
        var path = WriteAudio("mono.ogg");
        ExpectTranscription(path, new TranscriptionResult
        {
            ModelUsed = "base",
            Segments = new[] { new TranscriptionSegment { StartMs = 0, EndMs = 10, Text = "Solo." } },
        });

        var document = await _processor.ProcessAsync(path);

        document.Metadata.Custom.Should().NotContainKey("speakerCount");
    }

    [Fact]
    public async Task ProcessAsync_WithoutDetectedLanguage_OmitsTheLanguageKey()
    {
        var path = WriteAudio("unknown.flac");
        ExpectTranscription(path, new TranscriptionResult
        {
            ModelUsed = "base",
            Language = null,
            Segments = new[] { new TranscriptionSegment { StartMs = 0, EndMs = 10, Text = "Hmm." } },
        });

        var document = await _processor.ProcessAsync(path);

        document.Metadata.Custom.Should().NotContainKey("detectedLanguage");
    }

    [Fact]
    public async Task ProcessAsync_WithNoSegments_StillProducesAHeaderOnlyTranscript()
    {
        var path = WriteAudio("silence.m4a");
        ExpectTranscription(path, new TranscriptionResult
        {
            ModelUsed = "base",
            FullText = string.Empty,
            Segments = Array.Empty<TranscriptionSegment>(),
        });

        var document = await _processor.ProcessAsync(path);

        document.ExtractedText.Should().Contain("=== Audio Transcript ===");
        document.Metadata.Custom["segmentCount"].Should().Be("0");
    }

    // ── Degraded paths ───────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_WhisperRuntimeMissing_IndexesAStubInsteadOfFailing()
    {
        var path = WriteAudio("runtime.mp3");
        _transcription
            .Setup(t => t.TranscribeFileAsync(path, It.IsAny<TranscriptionOptions?>(),
                It.IsAny<IProgress<TranscriptionProgress>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException("Whisper.net runtime is not installed."));

        var document = await _processor.ProcessAsync(path);

        document.ExtractedText.Should().Contain("[Audio transcript unavailable");
        document.WordCount.Should().Be(0);
        document.ContentHash.Should().NotBeNullOrWhiteSpace();
        document.Metadata.Custom["errorType"].Should().Be("RuntimeNotInstalled");
    }

    [Fact]
    public async Task ProcessAsync_ModelNotDownloaded_IndexesAStubWithItsOwnErrorType()
    {
        var path = WriteAudio("model.mp3");
        _transcription
            .Setup(t => t.TranscribeFileAsync(path, It.IsAny<TranscriptionOptions?>(),
                It.IsAny<IProgress<TranscriptionProgress>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Model base is not available locally."));

        var document = await _processor.ProcessAsync(path);

        document.ExtractedText.Should().Contain("Whisper model not downloaded");
        document.Metadata.Custom["errorType"].Should().Be("ModelNotDownloaded");
    }

    [Fact]
    public async Task ProcessAsync_InvalidOperationWithoutTheNotAvailableMarker_FallsToTheGenericArm()
    {
        var path = WriteAudio("other.mp3");
        _transcription
            .Setup(t => t.TranscribeFileAsync(path, It.IsAny<TranscriptionOptions?>(),
                It.IsAny<IProgress<TranscriptionProgress>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("something else entirely"));

        var document = await _processor.ProcessAsync(path);

        document.ExtractedText.Should().BeEmpty();
        document.Metadata.Custom["errorType"].Should().Be("InvalidOperationException");
        document.Metadata.Custom["error"].Should().Be("something else entirely");
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedFault_RecordsTheExceptionTypeAndEmptiesText()
    {
        var path = WriteAudio("boom.wav");
        _transcription
            .Setup(t => t.TranscribeFileAsync(path, It.IsAny<TranscriptionOptions?>(),
                It.IsAny<IProgress<TranscriptionProgress>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk fell over"));

        var document = await _processor.ProcessAsync(path);

        document.ExtractedText.Should().BeEmpty();
        document.Metadata.Custom["errorType"].Should().Be("IOException");
    }

    [Fact]
    public async Task ProcessAsync_Cancellation_PropagatesInsteadOfBeingSwallowed()
    {
        var path = WriteAudio("cancel.mp3");
        _transcription
            .Setup(t => t.TranscribeFileAsync(path, It.IsAny<TranscriptionOptions?>(),
                It.IsAny<IProgress<TranscriptionProgress>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => _processor.ProcessAsync(path, new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string WriteAudio(string name)
    {
        var path = Path.Combine(_tempDirectory, name);
        // Content is never decoded here: transcription is mocked, and the processor only
        // needs a real file so FileInfo and the hash task have something to read.
        File.WriteAllBytes(path, new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x01, 0x02, 0x03 });
        return path;
    }

    private void ExpectTranscription(string path, TranscriptionResult result)
    {
        _transcription
            .Setup(t => t.TranscribeFileAsync(path, It.IsAny<TranscriptionOptions?>(),
                It.IsAny<IProgress<TranscriptionProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }
}
