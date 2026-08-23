using AgentX.Core.Helpers;
using AgentX.Core.Services.Audio.Models;
using Serilog;
using Whisper.net;

namespace AgentX.Core.Services.Audio;

/// <summary>
/// Local Whisper-based transcription service, backed by Whisper.net.
/// <para>
/// Model files are stored as GGML binaries under %LOCALAPPDATA%/AgentX/Models/Whisper/.
/// <see cref="DownloadModelAsync"/> fetches a model on demand; <see cref="TranscribeFileAsync"/>
/// loads it through <c>WhisperFactory</c> and runs the audio through the processor.
/// </para>
/// <para>
/// <see cref="TranscribeFileAsync"/> throws <see cref="NotSupportedException"/> only for an
/// audio container this service does not accept (see <c>AudioFormats</c>), and
/// <see cref="InvalidOperationException"/> when the requested model is missing or its file
/// cannot be loaded.
/// </para>
/// </summary>
public sealed class TranscriptionService : ITranscriptionService
{
    // ── Constants ────────────────────────────────────────────────────────────

    /// <summary>
    /// Root directory for all Whisper GGML model files.
    /// Resolved at construction time so the path is stable for the lifetime of this instance.
    /// </summary>
    private static readonly string ModelStoragePath = PathHelper.EnsureDirectoryExists(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentX", "Models", "Whisper"));

    /// <summary>
    /// All Whisper model size identifiers accepted by this service, ordered smallest-to-largest.
    /// </summary>
    private static readonly IReadOnlySet<string> ValidModelSizes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "tiny", "base", "small", "medium", "large"
        };

    /// <summary>
    /// Maps each model size to its HuggingFace download URL for the quantised GGML file.
    /// These are the standard ggerganov/whisper.cpp model URLs used by the community.
    /// Replace with your preferred mirror or private model hosting as needed.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ModelDownloadUrls =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tiny"] = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin",
            ["base"] = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin",
            ["small"] = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin",
            ["medium"] = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin",
            ["large"] = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3.bin",
        };

    /// <summary>
    /// Approximate model file sizes in bytes used to seed progress reporting before
    /// the HTTP Content-Length header is received.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, long> ApproximateModelBytes =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["tiny"] = 75_000_000L,
            ["base"] = 142_000_000L,
            ["small"] = 466_000_000L,
            ["medium"] = 1_528_000_000L,
            ["large"] = 3_094_000_000L,
        };

    // ── Supported formats ────────────────────────────────────────────────────

    private static readonly IReadOnlyList<string> AudioFormats =
    [
        ".mp3", ".wav", ".m4a", ".flac", ".ogg", ".webm"
    ];

    private static readonly IReadOnlySet<string> AudioFormatsSet =
        new HashSet<string>(AudioFormats, StringComparer.OrdinalIgnoreCase);

    // ── Fields ───────────────────────────────────────────────────────────────

    private readonly ILogger _log;

    // ── Constructor ──────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises a new <see cref="TranscriptionService"/> instance.
    /// </summary>
    /// <param name="logger">
    /// Serilog logger. Enriched with a <c>SourceContext</c> property scoped to this type.
    /// Obtain via <c>Serilog.Log.ForContext&lt;TranscriptionService&gt;()</c> or inject
    /// through the DI container.
    /// </param>
    public TranscriptionService(ILogger logger)
    {
        _log = logger.ForContext<TranscriptionService>();
    }

    // ── ITranscriptionService ────────────────────────────────────────────────

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedFormats => AudioFormats;

    /// <inheritdoc />
    public Task<bool> IsModelAvailableAsync(string modelSize = "base")
    {
        var modelPath = GetModelFilePath(modelSize);
        var exists = File.Exists(modelPath);

        _log.Debug(
            "Model availability check — size: {ModelSize}, path: {ModelPath}, exists: {Exists}",
            modelSize, modelPath, exists);

        return Task.FromResult(exists);
    }

    /// <inheritdoc />
    public async Task DownloadModelAsync(
        string modelSize = "base",
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        ValidateModelSize(modelSize);

        var modelPath = GetModelFilePath(modelSize);

        if (File.Exists(modelPath))
        {
            _log.Information(
                "Whisper model '{ModelSize}' already present at {ModelPath}; skipping download",
                modelSize, modelPath);

            progress?.Report(1.0);
            return;
        }

        if (!ModelDownloadUrls.TryGetValue(modelSize, out var downloadUrl))
        {
            // Should not reach here after ValidateModelSize, but guard defensively.
            throw new InvalidOperationException(
                $"No download URL configured for Whisper model size '{modelSize}'.");
        }

        _log.Information(
            "Initiating Whisper model download — size: {ModelSize}, url: {Url}, destination: {Destination}",
            modelSize, downloadUrl, modelPath);

        // Ensure the parent directory exists before streaming to disk.
        PathHelper.EnsureDirectoryExists(Path.GetDirectoryName(modelPath)!);

        var approximateBytes = ApproximateModelBytes.GetValueOrDefault(
            modelSize, ApproximateModelBytes["base"]);

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30),
        };

        try
        {
            using var response = await httpClient
                .GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? approximateBytes;
            var tempPath = modelPath + ".download";

            try
            {
                await using var contentStream = await response.Content
                    .ReadAsStreamAsync(ct)
                    .ConfigureAwait(false);

                await using var fileStream = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81_920,
                    useAsync: true);

                var buffer = new byte[81_920];
                long bytesReceived = 0;
                int bytesRead;

                while ((bytesRead = await contentStream
                           .ReadAsync(buffer, ct)
                           .ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct)
                        .ConfigureAwait(false);

                    bytesReceived += bytesRead;

                    var fraction = totalBytes > 0
                        ? Math.Min(1.0, (double)bytesReceived / totalBytes)
                        : 0.0;

                    progress?.Report(fraction);
                }

                await fileStream.FlushAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // Clean up partial download on failure or cancellation.
                if (File.Exists(tempPath))
                    File.Delete(tempPath);

                throw;
            }

            // Atomic rename: only replace the target after a complete write.
            if (File.Exists(modelPath))
                File.Delete(modelPath);

            File.Move(tempPath, modelPath);

            progress?.Report(1.0);

            _log.Information(
                "Whisper model '{ModelSize}' downloaded successfully to {ModelPath}",
                modelSize, modelPath);
        }
        catch (OperationCanceledException)
        {
            _log.Information(
                "Whisper model download cancelled — size: {ModelSize}", modelSize);
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(
                ex,
                "Failed to download Whisper model '{ModelSize}' from {Url}",
                modelSize, downloadUrl);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<TranscriptionResult> TranscribeFileAsync(
        string audioFilePath,
        TranscriptionOptions? options = null,
        IProgress<TranscriptionProgress>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new TranscriptionOptions();

        // ── Phase 0: Validate inputs (0%) ────────────────────────────────────

        ReportProgress(progress, 0.0, "Validating file...");

        if (string.IsNullOrWhiteSpace(audioFilePath))
            throw new ArgumentException("Audio file path must not be null or whitespace.", nameof(audioFilePath));

        var fileInfo = new FileInfo(audioFilePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("Audio file not found.", audioFilePath);

        var extension = Path.GetExtension(audioFilePath);
        if (string.IsNullOrEmpty(extension) || !AudioFormatsSet.Contains(extension))
        {
            throw new NotSupportedException(
                $"Audio format '{extension}' is not supported. " +
                $"Supported formats: {string.Join(", ", AudioFormats)}");
        }

        _log.Information(
            "Transcription requested — file: {FileName}, size: {FileSizeBytes} bytes, model: {ModelSize}, language: {Language}",
            fileInfo.Name, fileInfo.Length, options.ModelSize, options.Language ?? "auto");

        // ── Phase 1: Check model availability (5%) ────────────────────────────

        ReportProgress(progress, 5.0, "Checking model...");

        var modelPath = GetModelFilePath(options.ModelSize);
        if (!File.Exists(modelPath))
        {
            throw new InvalidOperationException(
                $"Whisper model '{options.ModelSize}' is not available at {modelPath}. " +
                $"Call DownloadModelAsync(\"{options.ModelSize}\") first.");
        }

        // ── Phase 2: Load model (10–30%) ──────────────────────────────────────

        ReportProgress(progress, 10.0, "Loading model...");

        _log.Debug(
            "Loading Whisper model from {ModelPath}", modelPath);

        ct.ThrowIfCancellationRequested();

        // Simulate model load phase for progress fidelity.
        // In the integrated implementation this range is consumed by the factory call.
        await SimulatePhaseAsync(progressStart: 10.0, progressEnd: 30.0, steps: 4,
            label: "Loading model...", progress, ct).ConfigureAwait(false);

        // ── Phase 3: Transcribe audio (30–90%) ────────────────────────────────

        ReportProgress(progress, 30.0, "Transcribing...");

        _log.Debug(
            "Starting Whisper transcription — file: {FilePath}, timestamps: {Timestamps}, diarization: {Diarization}",
            audioFilePath, options.EnableTimestamps, options.EnableSpeakerDiarization);

        ct.ThrowIfCancellationRequested();

        var result = await RunWhisperAsync(
            audioFilePath, modelPath, options, progress, ct).ConfigureAwait(false);

        // ── Phase 4: Finalise (90–100%) ───────────────────────────────────────

        ReportProgress(progress, 90.0, "Generating segments...");
        ct.ThrowIfCancellationRequested();
        await SimulatePhaseAsync(progressStart: 90.0, progressEnd: 100.0, steps: 2,
            label: "Generating segments...", progress, ct).ConfigureAwait(false);

        ReportProgress(progress, 100.0, "Complete");

        _log.Information(
            "Transcription complete — file: {FileName}, segments: {SegmentCount}, language: {Language}, durationMs: {DurationMs}",
            fileInfo.Name, result.Segments.Count, result.Language, result.DurationMs);

        return result;
    }

    // ── Private pipeline ─────────────────────────────────────────────────────

    /// <summary>
    /// The core Whisper execution boundary. Runs the Whisper model on the given audio file
    /// and produces a <see cref="TranscriptionResult"/> with text, segments, language, and duration.
    /// </summary>
    private async Task<TranscriptionResult> RunWhisperAsync(
        string audioFilePath,
        string modelPath,
        TranscriptionOptions options,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // ── Whisper.net execution ──────────────────────────────────────────────

        WhisperFactory whisperFactory;
        try
        {
            whisperFactory = WhisperFactory.FromPath(modelPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to load Whisper model from '{modelPath}'. " +
                "The model file may be corrupt or incompatible. " +
                "Try deleting the file and downloading it again.", ex);
        }

        using var factory = whisperFactory;

        var builder = whisperFactory.CreateBuilder()
            .WithLanguage(options.Language ?? "auto");

        var segments = new List<TranscriptionSegment>();

        await using var processor = builder.Build();

        ct.ThrowIfCancellationRequested();

        // Process the audio file via FileStream and iterate over segments
        await using var fileStream = new FileStream(
            audioFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            useAsync: true);

        await foreach (var segment in processor.ProcessAsync(fileStream, ct).ConfigureAwait(false))
        {
            var transcriptSegment = new TranscriptionSegment
            {
                StartMs = (long)segment.Start.TotalMilliseconds,
                EndMs = (long)segment.End.TotalMilliseconds,
                Text = segment.Text.Trim(),
            };

            segments.Add(transcriptSegment);

            // Progress: 30–90% range during transcription
            var pct = Math.Min(90.0, 30.0 + segments.Count * 2.0);
            ReportProgress(progress, pct, "Transcribing...", transcriptSegment);
        }

        // ── Assemble result ────────────────────────────────────────────────────

        var fullText = string.Join(" ", segments.Select(s => s.Text));

        long durationMs = segments.Count > 0
            ? segments[^1].EndMs
            : 0;

        // Language: if user forced a specific language, report that; otherwise null (auto-detected)
        string? detectedLanguage = options.Language;

        _log.Debug(
            "Whisper transcription complete — file: {FilePath}, segments: {SegmentCount}, " +
            "durationMs: {DurationMs}, language: {Language}",
            audioFilePath, segments.Count, durationMs, detectedLanguage ?? "auto-detected");

        return new TranscriptionResult
        {
            FullText = fullText,
            Segments = segments,
            Language = detectedLanguage,
            DurationMs = durationMs,
            ModelUsed = options.ModelSize,
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds the canonical file system path for a Whisper GGML model binary.
    /// </summary>
    private static string GetModelFilePath(string modelSize)
        => Path.Combine(ModelStoragePath, $"ggml-{modelSize.ToLowerInvariant()}.bin");

    /// <summary>
    /// Validates that the provided model size string is one of the accepted identifiers.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="modelSize"/> is not recognised.
    /// </exception>
    private static void ValidateModelSize(string modelSize)
    {
        if (!ValidModelSizes.Contains(modelSize))
        {
            throw new ArgumentException(
                $"'{modelSize}' is not a valid Whisper model size. " +
                $"Accepted values: {string.Join(", ", ValidModelSizes)}.",
                nameof(modelSize));
        }
    }

    /// <summary>
    /// Emits a single <see cref="TranscriptionProgress"/> update to the provided sink.
    /// No-ops when <paramref name="progress"/> is <see langword="null"/>.
    /// </summary>
    private static void ReportProgress(
        IProgress<TranscriptionProgress>? progress,
        double percent,
        string phase,
        TranscriptionSegment? segment = null)
    {
        progress?.Report(new TranscriptionProgress
        {
            PercentComplete = percent,
            CurrentPhase = phase,
            Segment = segment,
        });
    }

    /// <summary>
    /// Advances a progress range incrementally across a fixed number of steps to give
    /// the UI smooth feedback during phases that don't emit natural checkpoints
    /// (e.g., model loading). Each step awaits a <see cref="Task.Yield"/> so the caller's
    /// UI thread remains responsive.
    /// </summary>
    private static async Task SimulatePhaseAsync(
        double progressStart,
        double progressEnd,
        int steps,
        string label,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken ct)
    {
        if (progress is null || steps <= 0)
            return;

        var step = (progressEnd - progressStart) / steps;

        for (var i = 1; i <= steps; i++)
        {
            ct.ThrowIfCancellationRequested();
            var pct = Math.Min(progressEnd, progressStart + step * i);
            ReportProgress(progress, pct, label);
            await Task.Yield();
        }
    }
}
