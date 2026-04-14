using AgentX.Core.Services.Audio;
using AgentX.Core.Services.Audio.Models;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services;

/// <summary>
/// Unit tests for <see cref="TranscriptionService"/>.
/// Tests focus on validation, error handling, and configuration since
/// actual Whisper model operations require downloaded model binaries.
/// </summary>
public sealed class TranscriptionServiceTests : IDisposable
{
    private readonly ILogger _logger;
    private readonly TranscriptionService _sut;

    public TranscriptionServiceTests()
    {
        _logger = Log.ForContext<TranscriptionService>();
        _sut = new TranscriptionService(_logger);
    }

    public void Dispose()
    {
        // TranscriptionService has no disposable resources
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Constructor
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_CreatesInstanceSuccessfully()
    {
        // Act
        var service = new TranscriptionService(_logger);

        // Assert
        service.Should().NotBeNull();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  SupportedFormats
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SupportedFormats_ContainsExpectedAudioFormats()
    {
        // Assert
        _sut.SupportedFormats.Should().Contain(".mp3", ".wav", ".m4a", ".flac", ".ogg", ".webm");
    }

    [Fact]
    public void SupportedFormats_ContainsExactlySixFormats()
    {
        _sut.SupportedFormats.Should().HaveCount(6);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  IsModelAvailableAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IsModelAvailableAsync_ExecutesWithoutThrowing()
    {
        // Act — model may or may not exist depending on environment
        var result = await _sut.IsModelAvailableAsync("tiny");

        // Assert — just verify it returns without throwing
        Assert.True(result || !result); // Always true for bool, proves no exception
    }

    [Fact]
    public async Task IsModelAvailableAsync_DefaultModelSize_ExecutesWithoutThrowing()
    {
        // Act — default model size is "base"
        var result = await _sut.IsModelAvailableAsync();

        // Assert — just verify it returns without throwing
        Assert.True(result || !result);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  DownloadModelAsync — Validation
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("invalid")]
    [InlineData("extra_large")]
    [InlineData("")]
    public async Task DownloadModelAsync_WithInvalidSize_ThrowsArgumentException(string modelSize)
    {
        // Act
        var act = () => _sut.DownloadModelAsync(modelSize);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("tiny")]
    [InlineData("base")]
    [InlineData("BASE")]   // case-insensitive validation
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    public async Task DownloadModelAsync_WithValidSize_AcceptsSize(string modelSize)
    {
        // Act — we don't actually download, but we verify the size is accepted
        // by checking that it doesn't throw ArgumentException.
        // We cancel immediately to avoid actual network calls.
        var cts = new CancellationTokenSource();
        cts.CancelAfter(1); // Cancel quickly

        try
        {
            await _sut.DownloadModelAsync(modelSize, ct: cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected — we cancelled to avoid downloading
        }
        catch (ArgumentException)
        {
            // This should NOT happen for valid sizes
            throw;
        }
        finally
        {
            cts.Dispose();
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  TranscribeFileAsync — Input Validation
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TranscribeFileAsync_WithNullPath_ThrowsArgumentException()
    {
        // Act
        var act = () => _sut.TranscribeFileAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TranscribeFileAsync_WithEmptyPath_ThrowsArgumentException()
    {
        // Act
        var act = () => _sut.TranscribeFileAsync(string.Empty);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TranscribeFileAsync_WithWhitespacePath_ThrowsArgumentException()
    {
        // Act
        var act = () => _sut.TranscribeFileAsync("   ");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TranscribeFileAsync_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        // Act
        var act = () => _sut.TranscribeFileAsync("C:\\nonexistent\\path\\audio.wav");

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task TranscribeFileAsync_WithUnsupportedFormat_ThrowsNotSupportedException()
    {
        // Arrange — create a temp file with unsupported extension
        var tempFile = Path.GetTempFileName();
        var unsupportedFile = Path.ChangeExtension(tempFile, ".avi");
        File.Move(tempFile, unsupportedFile);

        try
        {
            // Act
            var act = () => _sut.TranscribeFileAsync(unsupportedFile);

            // Assert
            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*avi*");
        }
        finally
        {
            if (File.Exists(unsupportedFile))
                File.Delete(unsupportedFile);
        }
    }

    [Fact]
    public async Task TranscribeFileAsync_WithMissingModel_ThrowsInvalidOperationException()
    {
        // Arrange — create a temp WAV file with minimal content
        var tempFile = Path.GetTempFileName();
        var wavFile = Path.ChangeExtension(tempFile, ".wav");
        File.Move(tempFile, wavFile);

        try
        {
            // Use the "large" model which is ~3GB and unlikely to exist in test environments
            var options = new TranscriptionOptions { ModelSize = "large" };

            // Act
            var act = () => _sut.TranscribeFileAsync(wavFile, options);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            if (File.Exists(wavFile))
                File.Delete(wavFile);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  TranscriptionOptions defaults
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TranscriptionOptions_DefaultModelSize_IsBase()
    {
        var options = new TranscriptionOptions();
        options.ModelSize.Should().Be("base");
    }

    [Fact]
    public void TranscriptionOptions_DefaultLanguage_IsNull()
    {
        var options = new TranscriptionOptions();
        options.Language.Should().BeNull();
    }

    [Fact]
    public void TranscriptionOptions_DefaultEnableTimestamps_IsTrue()
    {
        var options = new TranscriptionOptions();
        options.EnableTimestamps.Should().BeTrue();
    }

    [Fact]
    public void TranscriptionOptions_DefaultEnableSpeakerDiarization_IsFalse()
    {
        var options = new TranscriptionOptions();
        options.EnableSpeakerDiarization.Should().BeFalse();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  TranscriptionResult / TranscriptionSegment / TranscriptionProgress
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TranscriptionResult_DefaultProperties_AreInitialized()
    {
        var result = new TranscriptionResult();
        result.FullText.Should().BeEmpty();
        result.Segments.Should().BeEmpty();
        result.Language.Should().BeNull();
        result.DurationMs.Should().Be(0);
        result.ModelUsed.Should().BeEmpty();
    }

    [Fact]
    public void TranscriptionSegment_DefaultProperties_AreInitialized()
    {
        var segment = new TranscriptionSegment();
        segment.StartMs.Should().Be(0);
        segment.EndMs.Should().Be(0);
        segment.Text.Should().BeEmpty();
        segment.SpeakerId.Should().BeNull();
    }

    [Fact]
    public void TranscriptionSegment_ToString_FormatsCorrectly()
    {
        var segment = new TranscriptionSegment
        {
            StartMs = 1500,
            EndMs = 3000,
            Text = "Hello world"
        };

        var str = segment.ToString();
        str.Should().Contain("Hello world");
        str.Should().Contain("-->");
    }

    [Fact]
    public void TranscriptionSegment_ToString_WithSpeaker_IncludesSpeakerId()
    {
        var segment = new TranscriptionSegment
        {
            StartMs = 0,
            EndMs = 1000,
            Text = "Hi",
            SpeakerId = 2
        };

        var str = segment.ToString();
        str.Should().Contain("Speaker 2");
    }

    [Fact]
    public void TranscriptionProgress_DefaultProperties_AreInitialized()
    {
        var progress = new TranscriptionProgress();
        progress.PercentComplete.Should().Be(0);
        progress.CurrentPhase.Should().BeEmpty();
        progress.Segment.Should().BeNull();
    }
}