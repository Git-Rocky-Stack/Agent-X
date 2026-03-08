using AgentX.Core.Services.Audio.Models;

namespace AgentX.Core.Services.Audio;

/// <summary>
/// Provides local speech-to-text transcription using Whisper models running entirely on-device.
/// <para>
/// Models are stored under %LOCALAPPDATA%/AgentX/Models/Whisper/ and must be downloaded
/// before transcription can begin. Call <see cref="IsModelAvailableAsync"/> to check
/// availability and <see cref="DownloadModelAsync"/> to fetch a model if needed.
/// </para>
/// <para>
/// The full transcription pipeline emits granular progress via <see cref="IProgress{T}"/>
/// of <see cref="TranscriptionProgress"/>, including per-segment callbacks so callers can
/// stream partial results into the UI as they arrive.
/// </para>
/// </summary>
public interface ITranscriptionService
{
    /// <summary>
    /// Gets the set of audio file extensions this service can accept.
    /// Extensions are lower-case and include the leading dot (e.g., ".mp3").
    /// </summary>
    IReadOnlyList<string> SupportedFormats { get; }

    /// <summary>
    /// Transcribes an audio file to text using the specified Whisper model.
    /// </summary>
    /// <param name="audioFilePath">Absolute path to the audio file to transcribe.</param>
    /// <param name="options">
    /// Optional transcription settings. If <see langword="null"/>, defaults are used:
    /// model size "base", auto language detection, timestamps enabled, no diarization.
    /// </param>
    /// <param name="progress">
    /// Optional progress sink. Receives <see cref="TranscriptionProgress"/> updates
    /// as the pipeline advances through loading, transcription, and segment extraction phases.
    /// </param>
    /// <param name="ct">Token used to cancel the transcription mid-flight.</param>
    /// <returns>
    /// A <see cref="TranscriptionResult"/> containing the full transcript text, per-segment
    /// detail, detected language, audio duration, and the model identifier used.
    /// </returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown when <paramref name="audioFilePath"/> does not exist on disk.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// Thrown when the file extension is not in <see cref="SupportedFormats"/> or when
    /// the Whisper.net runtime package is not installed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the requested model has not been downloaded yet.
    /// </exception>
    Task<TranscriptionResult> TranscribeFileAsync(
        string audioFilePath,
        TranscriptionOptions? options = null,
        IProgress<TranscriptionProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns <see langword="true"/> if the GGML model file for the given size is present
    /// in the local model cache directory; <see langword="false"/> otherwise.
    /// </summary>
    /// <param name="modelSize">
    /// Whisper model size identifier. Accepted values: "tiny", "base", "small", "medium", "large".
    /// Defaults to "base".
    /// </param>
    Task<bool> IsModelAvailableAsync(string modelSize = "base");

    /// <summary>
    /// Downloads the GGML Whisper model file from the upstream source to the local cache.
    /// <para>
    /// If the model is already present this method returns immediately without re-downloading.
    /// Progress is reported as a value between 0.0 and 1.0 representing bytes received
    /// relative to total content length.
    /// </para>
    /// </summary>
    /// <param name="modelSize">
    /// Whisper model size identifier. Accepted values: "tiny", "base", "small", "medium", "large".
    /// Defaults to "base".
    /// </param>
    /// <param name="progress">
    /// Optional progress sink. Receives values in the range [0.0, 1.0] as the download advances.
    /// Reports 1.0 on completion.
    /// </param>
    /// <param name="ct">Token used to cancel the download.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="modelSize"/> is not a recognised size identifier.
    /// </exception>
    Task DownloadModelAsync(
        string modelSize = "base",
        IProgress<double>? progress = null,
        CancellationToken ct = default);
}
