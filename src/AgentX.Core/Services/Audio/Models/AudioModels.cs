namespace AgentX.Core.Services.Audio.Models;

/// <summary>
/// The complete output of a Whisper transcription pass over a single audio file.
/// </summary>
public sealed class TranscriptionResult
{
    /// <summary>
    /// The full transcript text assembled from all segments in order.
    /// Segments are joined with a single space; timestamps are not embedded here —
    /// see <see cref="Segments"/> for time-aligned detail.
    /// </summary>
    public string FullText { get; init; } = string.Empty;

    /// <summary>
    /// Individual time-aligned transcript segments produced by the Whisper decoder.
    /// May be empty when timestamp extraction is disabled via
    /// <see cref="TranscriptionOptions.EnableTimestamps"/>.
    /// </summary>
    public IReadOnlyList<TranscriptionSegment> Segments { get; init; } = [];

    /// <summary>
    /// BCP-47 language tag detected by Whisper (e.g., "en", "fr", "de").
    /// <see langword="null"/> when language detection was suppressed by setting
    /// <see cref="TranscriptionOptions.Language"/> to a fixed value.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Total audio duration in milliseconds as measured during decoding.
    /// Zero when the value could not be determined from the audio stream.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// The Whisper model size identifier that was used to produce this result
    /// (e.g., "base", "small", "medium").
    /// </summary>
    public string ModelUsed { get; init; } = string.Empty;
}

/// <summary>
/// A single time-stamped segment of transcript text emitted by the Whisper decoder.
/// </summary>
public sealed class TranscriptionSegment
{
    /// <summary>
    /// Segment start offset from the beginning of the audio stream, in milliseconds.
    /// </summary>
    public long StartMs { get; init; }

    /// <summary>
    /// Segment end offset from the beginning of the audio stream, in milliseconds.
    /// </summary>
    public long EndMs { get; init; }

    /// <summary>
    /// The transcript text for this segment. Typically a sentence or clause fragment
    /// as determined by the Whisper VAD (voice activity detection) boundaries.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Zero-based speaker index assigned by the diarization pass, or <see langword="null"/>
    /// when <see cref="TranscriptionOptions.EnableSpeakerDiarization"/> is <see langword="false"/>
    /// or diarization was unable to distinguish speakers.
    /// </summary>
    public int? SpeakerId { get; init; }

    /// <summary>
    /// Returns a human-readable representation of this segment for diagnostic purposes.
    /// Format: [HH:MM:SS.fff --> HH:MM:SS.fff] (Speaker N) text
    /// </summary>
    public override string ToString()
    {
        var start = TimeSpan.FromMilliseconds(StartMs);
        var end = TimeSpan.FromMilliseconds(EndMs);
        var speakerPrefix = SpeakerId.HasValue ? $"(Speaker {SpeakerId}) " : string.Empty;
        return $"[{start:hh\\:mm\\:ss\\.fff} --> {end:hh\\:mm\\:ss\\.fff}] {speakerPrefix}{Text}";
    }
}

/// <summary>
/// Configuration options that control the behaviour of a single transcription pass.
/// All properties have sensible defaults suitable for general-purpose English transcription.
/// </summary>
public sealed class TranscriptionOptions
{
    /// <summary>
    /// Whisper GGML model size to load for transcription.
    /// Accepted values: "tiny", "base", "small", "medium", "large".
    /// Larger models produce higher accuracy at the cost of memory and latency.
    /// Defaults to "base", which offers a good balance for most desktop workloads.
    /// </summary>
    public string ModelSize { get; init; } = "base";

    /// <summary>
    /// BCP-47 language tag to force on the Whisper decoder (e.g., "en", "fr", "de").
    /// When <see langword="null"/> (the default), Whisper auto-detects the language
    /// from the first 30 seconds of audio.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// When <see langword="true"/> (the default), the decoder emits per-segment start/end
    /// timestamps and the result's <see cref="TranscriptionResult.Segments"/> list is populated.
    /// Set to <see langword="false"/> for a plain-text-only pass with slightly lower overhead.
    /// </summary>
    public bool EnableTimestamps { get; init; } = true;

    /// <summary>
    /// When <see langword="true"/>, a speaker diarization pass is run after transcription
    /// and each <see cref="TranscriptionSegment.SpeakerId"/> is assigned.
    /// Diarization incurs additional processing time and requires timestamp data, so
    /// <see cref="EnableTimestamps"/> is implicitly treated as <see langword="true"/>
    /// when this is enabled.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool EnableSpeakerDiarization { get; init; } = false;
}

/// <summary>
/// A progress update emitted during a transcription pass.
/// The phase string describes the current pipeline stage in human-readable form.
/// </summary>
public sealed class TranscriptionProgress
{
    /// <summary>
    /// Overall completion percentage in the range [0, 100].
    /// The value advances monotonically as the pipeline moves through its phases.
    /// </summary>
    public double PercentComplete { get; init; }

    /// <summary>
    /// Human-readable label for the current pipeline phase.
    /// Example values: "Validating file...", "Loading model...",
    /// "Transcribing...", "Generating segments...", "Complete".
    /// </summary>
    public string CurrentPhase { get; init; } = string.Empty;

    /// <summary>
    /// The most recently decoded segment, or <see langword="null"/> when the current
    /// phase does not produce segment output (e.g., model loading).
    /// Callers can use this for live streaming of partial transcript results into the UI.
    /// </summary>
    public TranscriptionSegment? Segment { get; init; }
}
