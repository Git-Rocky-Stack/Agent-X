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
/// Supported extensions: .mp3, .wav, .m4a, .flac, .ogg, .webm
/// </para>
/// </summary>
public sealed class AudioProcessor : IDocumentProcessor
{
    // ── Static fields ─────────────────────────────────────────────────────────

    private static readonly ILogger Log = Serilog.Log.ForContext<AudioProcessor>();

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".m4a", ".flac", ".ogg", ".webm"
    };

    /// <summary>
    /// Maps audio file extensions to human-readable file type identifiers used in
    /// <see cref="ProcessedDocument.FileType"/>.
    /// </summary>
    private static readonly Dictionary<string, string> FileTypeNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".mp3"]  = "mp3",
            [".wav"]  = "wav",
            [".m4a"]  = "m4a",
            [".flac"] = "flac",
            [".ogg"]  = "ogg",
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
    /// If the Whisper.net runtime is not installed, this method catches the
    /// <see cref="NotSupportedException"/> from the transcription service and stores a
    /// descriptive placeholder in <see cref="ProcessedDocument.ExtractedText"/> so that
    /// the document can still be indexed (as a stub entry) without crashing the indexing queue.
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
            FilePath      = filePath,
            FileName      = Path.GetFileName(filePath),
            FileType      = FileTypeNames.GetValueOrDefault(ext, ext.TrimStart('.')),
            FileSizeBytes = fileInfo.Length,
            PageCount     = 1,
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

            document.ContentHash  = await hashTask.ConfigureAwait(false);
            document.ExtractedText = BuildExtractedText(result, fileInfo.Name);
            document.Language      = result.Language;
            document.WordCount     = CountWords(document.ExtractedText);

            // Use the file name (without extension) as the document title since audio files
            // rarely embed a structural title the way documents or code files do.
            document.ExtractedTitle = Path.GetFileNameWithoutExtension(filePath);

            // Populate metadata with transcription provenance for downstream consumers.
            document.Metadata.CreatedDate  = fileInfo.CreationTimeUtc;
            document.Metadata.ModifiedDate = fileInfo.LastWriteTimeUtc;
            document.Metadata.Custom["audioFormat"]    = ext.TrimStart('.');
            document.Metadata.Custom["modelUsed"]      = result.ModelUsed;
            document.Metadata.Custom["segmentCount"]   = result.Segments.Count.ToString();
            document.Metadata.Custom["durationMs"]     = result.DurationMs.ToString();
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
        catch (NotSupportedException ex)
        {
            // Whisper.net runtime not installed — record a descriptive stub so the document
            // is indexed as a known-but-untranscribed entry rather than silently dropped.
            Log.Warning(
                ex,
                "Whisper.net runtime unavailable; audio file will be indexed without transcript: {FilePath}",
                filePath);

            document.ContentHash  = await hashTask.ConfigureAwait(false);
            document.ExtractedText =
                $"[Audio transcript unavailable — {ex.Message}]";
            document.ExtractedTitle = Path.GetFileNameWithoutExtension(filePath);
            document.WordCount      = 0;
            document.Metadata.CreatedDate  = fileInfo.CreationTimeUtc;
            document.Metadata.ModifiedDate = fileInfo.LastWriteTimeUtc;
            document.Metadata.Custom["audioFormat"] = ext.TrimStart('.');
            document.Metadata.Custom["error"]       = ex.Message;
            document.Metadata.Custom["errorType"]   = "RuntimeNotInstalled";
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not available"))
        {
            // Model not downloaded — record a descriptive stub similar to the runtime case.
            Log.Warning(
                ex,
                "Whisper model not downloaded; audio file will be indexed without transcript: {FilePath}",
                filePath);

            document.ContentHash  = await hashTask.ConfigureAwait(false);
            document.ExtractedText =
                $"[Audio transcript unavailable — Whisper model not downloaded. {ex.Message}]";
            document.ExtractedTitle = Path.GetFileNameWithoutExtension(filePath);
            document.WordCount      = 0;
            document.Metadata.CreatedDate  = fileInfo.CreationTimeUtc;
            document.Metadata.ModifiedDate = fileInfo.LastWriteTimeUtc;
            document.Metadata.Custom["audioFormat"] = ext.TrimStart('.');
            document.Metadata.Custom["error"]       = ex.Message;
            document.Metadata.Custom["errorType"]   = "ModelNotDownloaded";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process audio file: {FilePath}", filePath);

            // Ensure the hash completes even in the error path so the document entity
            // can still be uniquely identified in the database.
            try
            {
                document.ContentHash = await hashTask.ConfigureAwait(false);
            }
            catch (Exception hashEx)
            {
                Log.Warning(hashEx, "Failed to compute content hash during error recovery: {FilePath}", filePath);
                document.ContentHash = string.Empty;
            }

            document.ExtractedText = string.Empty;
            document.Metadata.Custom["error"]     = ex.Message;
            document.Metadata.Custom["errorType"] = ex.GetType().Name;
        }

        return document;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

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
                var end   = TimeSpan.FromMilliseconds(segment.EndMs);

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
