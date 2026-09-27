using System.Text;
using AgentX.Core.Documents.Models;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Audio;
using AgentX.Core.Services.Audio.Models;
using Serilog;

namespace AgentX.Core.Documents.Processors;

/// <summary>
/// Processes audio files by transcribing them via <see cref="ITranscriptionService"/>
/// and surfacing the resulting transcript as indexable document content.
/// <para>
/// The extracted text follows a structured format:
/// <list type="bullet">
///   <item>A header block containing the file name, detected language, duration, and model used.</item>
///   <item>When timestamps are available, each segment is formatted as
///         <c>[HH:MM:SS --> HH:MM:SS] (Speaker N) text</c> on its own line.</item>
///   <item>When no segments are returned, the raw <see cref="TranscriptionResult.FullText"/>
///         is used directly.</item>
/// </list>
/// This format keeps the plain text human-readable while embedding enough temporal
/// context for downstream chunking and citation generation.
/// </para>
/// <para>
/// A file that cannot be transcribed is reported as a failed extraction
/// (<see cref="DocumentExtractionException"/>), never as a document holding placeholder text:
/// the speech-to-text model not installed (<see cref="SpeechModelMissingError"/>), the Whisper
/// runtime not loadable on this computer, audio that cannot be decoded, or any other fault.
/// </para>
/// <para>
/// Supported extensions: .mp3, .wav, .m4a, .flac, .ogg, .webm
/// </para>
/// </summary>
public sealed class AudioProcessor : IDocumentProcessor
{
    /// <summary>
    /// Indexing error of an audio file that could not be transcribed because the speech-to-text
    /// model is not installed. Once the model is downloaded on the Model Manager page, documents
    /// that failed with exactly this reason are queued again
    /// (<see cref="IDocumentService.RequeueAudioAwaitingSpeechModelAsync"/>).
    /// </summary>
    public const string SpeechModelMissingError =
        "The speech-to-text model is not installed. Install it on the Model Manager page to transcribe this audio file.";

    /// <summary>
    /// Key that earlier versions wrote into the metadata JSON of an audio document they could not
    /// transcribe. They stored a placeholder transcript instead of failing the import, so the key
    /// is how those documents are recognised and queued again once the model is installed.
    /// </summary>
    internal const string LegacyTranscriptionErrorMarker = "\"errorType\":";

    // ── Static fields ─────────────────────────────────────────────────────────

    private static readonly ILogger Log = Serilog.Log.ForContext<AudioProcessor>();

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".m4a", ".flac", ".ogg", ".webm"
    };

    /// <summary>
    /// The <see cref="Data.Entities.DocumentEntity.FileType"/> values of imported audio files:
    /// the extensions above without the dot, lower-case.
    /// </summary>
    internal static readonly string[] FileTypes =
        Extensions.Select(extension => extension.TrimStart('.').ToLowerInvariant()).ToArray();

    /// <summary>
    /// Maps audio file extensions to human-readable file type identifiers used in
    /// <see cref="ProcessedDocument.FileType"/>.
    /// </summary>
    private static readonly Dictionary<string, string> FileTypeNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".mp3"] = "mp3",
            [".wav"] = "wav",
            [".m4a"] = "m4a",
            [".flac"] = "flac",
            [".ogg"] = "ogg",
            [".webm"] = "webm",
        };

    // ── Constructor ───────────────────────────────────────────────────────────

    private readonly ITranscriptionService _transcriptionService;

    /// <summary>
    /// Initialises a new <see cref="AudioProcessor"/>.
    /// </summary>
    /// <param name="transcriptionService">
    /// The transcription service used to convert audio to text.
    /// Typically <see cref="TranscriptionService"/> registered as a singleton in the DI container.
    /// </param>
    public AudioProcessor(ITranscriptionService transcriptionService)
    {
        _transcriptionService = transcriptionService;
    }

    // ── IDocumentProcessor ────────────────────────────────────────────────────

    /// <inheritdoc />
    public IReadOnlySet<string> SupportedExtensions => Extensions;

    /// <inheritdoc />
    public bool CanProcess(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(ext) && Extensions.Contains(ext);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The transcription is executed with default <see cref="TranscriptionOptions"/> (model: "base",
    /// auto language detection, timestamps enabled, no speaker diarization). Callers that require
    /// non-default options should invoke <see cref="ITranscriptionService.TranscribeFileAsync"/>
    /// directly and assemble a <see cref="ProcessedDocument"/> from the result.
    /// </para>
    /// <para>
    /// When the file cannot be transcribed this method throws
    /// <see cref="DocumentExtractionException"/> with a reason written for the user, and the
    /// import records the document as failed with that reason. A placeholder is never returned
    /// as the transcript: it would be chunked, embedded and served by search like real content.
    /// </para>
    /// </remarks>
    public async Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default)
    {
        Log.Debug("Processing audio file: {FilePath}", filePath);

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("Audio file not found.", filePath);

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        var document = new ProcessedDocument
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileType = FileTypeNames.GetValueOrDefault(ext, ext.TrimStart('.')),
            FileSizeBytes = fileInfo.Length,
            PageCount = 1,
        };

        // Start the file hash computation in parallel with transcription so it does not
        // add to the perceived latency on the hot path.
        var hashTask = HashHelper.ComputeFileHashAsync(filePath, ct);

        try
        {
            // Relay transcription progress at the Debug level so the indexing queue can
            // surface phase labels without coupling to the transcription models directly.
            var transcriptionProgress = new Progress<TranscriptionProgress>(p =>
            {
                Log.Debug(
                    "Transcription progress — file: {FileName}, phase: {Phase}, pct: {Percent:F1}%",
                    document.FileName, p.CurrentPhase, p.PercentComplete);
            });

            var result = await _transcriptionService
                .TranscribeFileAsync(
                    filePath,
                    options: null,   // Use service defaults (model: base, timestamps: true)
                    progress: transcriptionProgress,
                    ct: ct)
                .ConfigureAwait(false);

            document.ContentHash = await hashTask.ConfigureAwait(false);
            document.ExtractedText = BuildExtractedText(result, fileInfo.Name);
            document.Language = result.Language;
            document.WordCount = CountWords(document.ExtractedText);

            // Use the file name (without extension) as the document title since audio files
            // rarely embed a structural title the way documents or code files do.
            document.ExtractedTitle = Path.GetFileNameWithoutExtension(filePath);

            // Populate metadata with transcription provenance for downstream consumers.
            document.Metadata.CreatedDate = fileInfo.CreationTimeUtc;
            document.Metadata.ModifiedDate = fileInfo.LastWriteTimeUtc;
            document.Metadata.Custom["audioFormat"] = ext.TrimStart('.');
            document.Metadata.Custom["modelUsed"] = result.ModelUsed;
            document.Metadata.Custom["segmentCount"] = result.Segments.Count.ToString();
            document.Metadata.Custom["durationMs"] = result.DurationMs.ToString();
            document.Metadata.Custom["durationDisplay"] = FormatDuration(result.DurationMs);

            if (!string.IsNullOrWhiteSpace(result.Language))
                document.Metadata.Custom["detectedLanguage"] = result.Language;

            var hasDiarization = result.Segments.Any(s => s.SpeakerId.HasValue);
            if (hasDiarization)
            {
                var speakerCount = result.Segments
                    .Where(s => s.SpeakerId.HasValue)
                    .Select(s => s.SpeakerId!.Value)
                    .Distinct()
                    .Count();

                document.Metadata.Custom["speakerCount"] = speakerCount.ToString();
            }

            Log.Information(
                "Successfully processed audio: {FileName} ({Format}, {Duration}, {SegmentCount} segments, {WordCount} words, model: {Model})",
                document.FileName,
                ext.TrimStart('.').ToUpperInvariant(),
                FormatDuration(result.DurationMs),
                result.Segments.Count,
                document.WordCount,
                result.ModelUsed);
        }
        catch (OperationCanceledException)
        {
            // Let cancellation propagate so the indexing queue can handle it correctly.
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Audio file could not be transcribed: {FilePath}", filePath);

            // The hash is not needed any more, but it still reads the file: let it finish
            // rather than leave it running unobserved.
            try
            {
                await hashTask.ConfigureAwait(false);
            }
            catch (Exception hashEx)
            {
                Log.Debug(hashEx, "Content hash of {FilePath} was not computed", filePath);
            }

            throw ToExtractionFailure(ex, document.FileName);
        }

        return document;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// The extraction failure a transcription failure is recorded as. The missing model gets the
    /// fixed <see cref="SpeechModelMissingError"/>, which the requeue after a model download
    /// matches on; a runtime that cannot load and audio that cannot be decoded already carry a
    /// message written for the user; anything else is prefixed with the file it happened to.
    /// </summary>
    private static DocumentExtractionException ToExtractionFailure(Exception ex, string fileName) => ex switch
    {
        TranscriptionModelMissingException => new DocumentExtractionException(SpeechModelMissingError, ex),
        NotSupportedException => new DocumentExtractionException(ex.Message, ex),
        _ => new DocumentExtractionException($"Could not transcribe '{fileName}': {ex.Message}", ex),
    };

    /// <summary>
    /// Assembles the structured plain-text representation that will be stored in
    /// <see cref="ProcessedDocument.ExtractedText"/> and fed into the chunking pipeline.
    /// </summary>
    private static string BuildExtractedText(TranscriptionResult result, string fileName)
    {
        var sb = new StringBuilder();

        // ── Header block ─────────────────────────────────────────────────────
        // Provides provenance metadata that downstream semantic search can surface
        // in citations. Formatted as a compact, parseable key-value preamble.
        sb.AppendLine("=== Audio Transcript ===");
        sb.Append("File: ").AppendLine(fileName);

        if (!string.IsNullOrWhiteSpace(result.Language))
            sb.Append("Language: ").AppendLine(result.Language.ToUpperInvariant());

        if (result.DurationMs > 0)
            sb.Append("Duration: ").AppendLine(FormatDuration(result.DurationMs));

        if (!string.IsNullOrWhiteSpace(result.ModelUsed))
            sb.Append("Model: whisper-").AppendLine(result.ModelUsed);

        sb.AppendLine("========================");
        sb.AppendLine();

        // ── Transcript body ──────────────────────────────────────────────────

        if (result.Segments.Count > 0)
        {
            // Emit one line per segment so the chunking service can use timestamp
            // markers as natural chunk boundaries if it chooses to split on them.
            foreach (var segment in result.Segments)
            {
                var start = TimeSpan.FromMilliseconds(segment.StartMs);
                var end = TimeSpan.FromMilliseconds(segment.EndMs);

                // Format: [HH:MM:SS --> HH:MM:SS]  or  [HH:MM:SS --> HH:MM:SS] (Speaker N)
                sb.Append('[')
                  .Append(start.ToString(@"hh\:mm\:ss"))
                  .Append(" --> ")
                  .Append(end.ToString(@"hh\:mm\:ss"))
                  .Append(']');

                if (segment.SpeakerId.HasValue)
                {
                    sb.Append(" (Speaker ").Append(segment.SpeakerId.Value + 1).Append(')');
                }

                sb.Append(' ').AppendLine(segment.Text.Trim());
            }
        }
        else if (!string.IsNullOrWhiteSpace(result.FullText))
        {
            // No per-segment data — emit the flat transcript directly.
            sb.AppendLine(result.FullText.Trim());
        }
        else
        {
            sb.AppendLine("[No transcript content]");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Formats a duration in milliseconds to a human-readable string.
    /// Uses "h:mm:ss" when the duration is one hour or longer; "m:ss" otherwise.
    /// </summary>
    private static string FormatDuration(long durationMs)
    {
        if (durationMs <= 0)
            return "0:00";

        var ts = TimeSpan.FromMilliseconds(durationMs);

        return ts.TotalHours >= 1.0
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");
    }

    /// <summary>
    /// Counts words in the extracted text by splitting on whitespace.
    /// Skips the header lines (=== ... ===) when counting to avoid inflating
    /// the word count with metadata tokens.
    /// </summary>
    private static long CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        long wordCount = 0;
        using var reader = new StringReader(text);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            // Skip header/separator lines.
            if (line.StartsWith("===") || line.StartsWith("File:") ||
                line.StartsWith("Language:") || line.StartsWith("Duration:") ||
                line.StartsWith("Model:"))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            wordCount += line.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries).Length;
        }

        return wordCount;
    }
}
