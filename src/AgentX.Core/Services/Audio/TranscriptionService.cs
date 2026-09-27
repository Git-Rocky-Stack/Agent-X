using AgentX.Core.Helpers;
using AgentX.Core.Services.Audio.Models;
using Serilog;
using Whisper.net;

namespace AgentX.Core.Services.Audio;

/// <summary>
/// Local Whisper-based transcription service, backed by Whisper.net.
/// <para>
/// Model files are stored as GGML binaries under %LOCALAPPDATA%/AgentX/Models/Whisper/.
/// <see cref="DownloadModelAsync"/> fetches a model when the user asks for one (the Model
/// Manager page), <see cref="RemoveModelAsync"/> deletes it, and <see cref="TranscribeFileAsync"/>
/// loads it through <c>WhisperFactory</c> and runs the audio through the processor.
/// </para>
/// <para>
/// Whisper.net reads only 16 kHz integer-PCM WAV, so any other input (MP3, M4A, FLAC, a 44.1 or
/// 48 kHz WAV, a float WAV) is first decoded, mixed to mono and resampled into a temporary WAV
/// by <see cref="WhisperAudioConverter"/>, which is deleted afterwards.
/// </para>
/// <para>
/// <see cref="TranscribeFileAsync"/> throws <see cref="NotSupportedException"/> for an audio
/// container this service does not accept (see <c>AudioFormats</c>) or a file this machine cannot
/// decode, <see cref="TranscriptionRuntimeUnavailableException"/> when the native Whisper runtime
/// cannot be loaded, <see cref="TranscriptionModelMissingException"/> when the requested model is
/// not installed, and <see cref="InvalidOperationException"/> when its file cannot be loaded.
/// </para>
/// </summary>
public sealed class TranscriptionService : ITranscriptionService
{
    // ── Constants ────────────────────────────────────────────────────────────

    /// <summary>
    /// The first four bytes of every whisper.cpp GGML model file: the magic 0x67676d6c ("ggml")
    /// written little-endian. whisper.cpp rejects a file without it ("bad magic"), so a download
    /// that does not start with it (an error page served with status 200, for example) is not
    /// installed as a model.
    /// </summary>
    private static ReadOnlySpan<byte> GgmlMagic => new byte[] { 0x6c, 0x6d, 0x67, 0x67 };

    /// <summary>Suffix of the temporary file a download is written to before it is moved into place.</summary>
    private const string PartialDownloadSuffix = ".download";

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

    /// <summary>Directory holding the GGML model files.</summary>
    private readonly string _modelDirectory;

    /// <summary>Handler model downloads are sent through; null uses a default handler.</summary>
    private readonly HttpMessageHandler? _httpHandler;

    /// <summary>
    /// Serializes downloads and removals, so two downloads never write the same temporary file
    /// and a removal never runs while a download is moving its file into place.
    /// </summary>
    private readonly SemaphoreSlim _modelFileGate = new(1, 1);

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
        : this(
            logger,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AgentX", "Models", "Whisper"),
            httpHandler: null)
    {
    }

    /// <summary>
    /// Keeps models in <paramref name="modelDirectory"/> and downloads them through
    /// <paramref name="httpHandler"/> (the caller keeps ownership of it). Internal so the DI
    /// container, which only sees public constructors, keeps using the one above; tests use it
    /// to download from a stub handler into a temporary directory.
    /// </summary>
    internal TranscriptionService(ILogger logger, string modelDirectory, HttpMessageHandler? httpHandler)
    {
        _log = logger.ForContext<TranscriptionService>();
        _modelDirectory = modelDirectory;
        _httpHandler = httpHandler;
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
    public Task<long?> GetInstalledModelSizeAsync(string modelSize = "base")
    {
        ValidateModelSize(modelSize);

        var modelFile = new FileInfo(GetModelFilePath(modelSize));
        return Task.FromResult(modelFile.Exists ? modelFile.Length : (long?)null);
    }

    /// <inheritdoc />
    public async Task DownloadModelAsync(
        string modelSize = "base",
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        ValidateModelSize(modelSize);

        if (!ModelDownloadUrls.TryGetValue(modelSize, out var downloadUrl))
        {
            // Should not reach here after ValidateModelSize, but guard defensively.
            throw new InvalidOperationException(
                $"No download URL configured for Whisper model size '{modelSize}'.");
        }

        await _modelFileGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await DownloadModelCoreAsync(modelSize, downloadUrl, progress, ct).ConfigureAwait(false);
        }
        finally
        {
            _modelFileGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task RemoveModelAsync(string modelSize = "base")
    {
        ValidateModelSize(modelSize);

        var modelPath = GetModelFilePath(modelSize);
        var partialPath = modelPath + PartialDownloadSuffix;

        await _modelFileGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // A session that ended mid-download can leave its partial file behind.
            if (File.Exists(partialPath))
            {
                TryDeleteTemporaryFile(partialPath);
            }

            if (File.Exists(modelPath))
            {
                File.Delete(modelPath);
                _log.Information("Removed Whisper model '{ModelSize}' from {ModelPath}", modelSize, modelPath);
            }
        }
        finally
        {
            _modelFileGate.Release();
        }
    }

    /// <summary>
    /// Downloads one model while holding <see cref="_modelFileGate"/>. The file is written next
    /// to its final path under a temporary name and moved into place only after it has been
    /// verified, so a cancelled, failed or incomplete download never leaves a file that looks
    /// like an installed model, nor a partial file.
    /// </summary>
    private async Task DownloadModelCoreAsync(
        string modelSize,
        string downloadUrl,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var modelPath = GetModelFilePath(modelSize);

        if (File.Exists(modelPath))
        {
            _log.Information(
                "Whisper model '{ModelSize}' already present at {ModelPath}; skipping download",
                modelSize, modelPath);

            progress?.Report(1.0);
            return;
        }

        _log.Information(
            "Initiating Whisper model download - size: {ModelSize}, url: {Url}, destination: {Destination}",
            modelSize, downloadUrl, modelPath);

        PathHelper.EnsureDirectoryExists(_modelDirectory);

        var tempPath = modelPath + PartialDownloadSuffix;

        try
        {
            await DownloadToFileAsync(modelSize, downloadUrl, tempPath, progress, ct).ConfigureAwait(false);

            // A rename within one directory: the model appears complete or not at all.
            File.Move(tempPath, modelPath, overwrite: true);
        }
        catch (Exception ex)
        {
            TryDeleteTemporaryFile(tempPath);

            if (ex is OperationCanceledException)
            {
                _log.Information("Whisper model download cancelled - size: {ModelSize}", modelSize);
            }
            else
            {
                _log.Error(ex, "Failed to download Whisper model '{ModelSize}' from {Url}", modelSize, downloadUrl);
            }

            throw;
        }

        progress?.Report(1.0);

        _log.Information(
            "Whisper model '{ModelSize}' downloaded successfully to {ModelPath}",
            modelSize, modelPath);
    }

    /// <summary>
    /// Streams the model at <paramref name="downloadUrl"/> into <paramref name="tempPath"/> and
    /// checks the result: the byte count must equal the Content-Length the server announced (when
    /// it announced one) and the file must start with the GGML magic. Throws on any mismatch.
    /// </summary>
    private async Task DownloadToFileAsync(
        string modelSize,
        string downloadUrl,
        string tempPath,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        using var httpClient = _httpHandler is null
            ? new HttpClient()
            : new HttpClient(_httpHandler, disposeHandler: false);
        httpClient.Timeout = TimeSpan.FromMinutes(30);

        using var response = await httpClient
            .GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var expectedBytes = response.Content.Headers.ContentLength;

        // Progress needs a total before the first byte; without a Content-Length the size the
        // model is known to have stands in for it.
        var progressTotal = expectedBytes ?? ApproximateModelBytes.GetValueOrDefault(
            modelSize, ApproximateModelBytes["base"]);

        long bytesReceived = 0;

        await using (var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var fileStream = new FileStream(
            tempPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            useAsync: true))
        {
            var buffer = new byte[81_920];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);

                bytesReceived += bytesRead;

                progress?.Report(progressTotal > 0
                    ? Math.Min(1.0, (double)bytesReceived / progressTotal)
                    : 0.0);
            }

            await fileStream.FlushAsync(ct).ConfigureAwait(false);
        }

        if (expectedBytes is long expected && bytesReceived != expected)
        {
            throw new IOException(
                $"The download of the Whisper '{modelSize}' model was incomplete: " +
                $"{bytesReceived} of {expected} bytes arrived.");
        }

        if (!StartsWithGgmlMagic(tempPath))
        {
            throw new InvalidDataException(
                $"The file downloaded for the Whisper '{modelSize}' model is not a Whisper model.");
        }
    }

    /// <summary>
    /// True when the file at <paramref name="path"/> begins with <see cref="GgmlMagic"/>.
    /// </summary>
    private static bool StartsWithGgmlMagic(string path)
    {
        Span<byte> header = stackalloc byte[4];

        using var stream = File.OpenRead(path);
        return stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length
            && header.SequenceEqual(GgmlMagic);
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
            throw new TranscriptionModelMissingException(options.ModelSize, modelPath);
        }

        // Phase 2: Whisper.net reads only 16 kHz integer-PCM WAV. Decode and resample anything
        // else into a temporary file first (10-20%).

        if (options.EnableSpeakerDiarization)
        {
            _log.Warning(
                "Speaker diarization was requested for {FileName}, but the bundled Whisper.net runtime does not support it; segments will carry no speaker ids",
                fileInfo.Name);
        }

        string? convertedPath = null;
        try
        {
            var whisperInputPath = audioFilePath;
            if (!WhisperAudioConverter.IsWhisperReadyWav(audioFilePath))
            {
                ReportProgress(progress, 10.0, "Preparing audio...");
                convertedPath = Path.Combine(PathHelper.GetTempPath(), $"whisper-{Guid.NewGuid():N}.wav");
                await ConvertForWhisperAsync(audioFilePath, convertedPath, ct).ConfigureAwait(false);
                whisperInputPath = convertedPath;
            }

            // Phase 3: Load the model and transcribe (20-90%).

            _log.Debug(
                "Starting Whisper transcription - file: {FilePath}, timestamps: {Timestamps}",
                audioFilePath, options.EnableTimestamps);

            var result = await RunWhisperAsync(
                whisperInputPath, modelPath, options, progress, ct).ConfigureAwait(false);

            ReportProgress(progress, 100.0, "Complete");

            _log.Information(
                "Transcription complete - file: {FileName}, segments: {SegmentCount}, language: {Language}, durationMs: {DurationMs}",
                fileInfo.Name, result.Segments.Count, result.Language, result.DurationMs);

            return result;
        }
        finally
        {
            if (convertedPath is not null)
            {
                TryDeleteTemporaryFile(convertedPath);
            }
        }
    }

    // ── Private pipeline ─────────────────────────────────────────────────────

    /// <summary>
    /// The core Whisper execution boundary. Runs the Whisper model on a Whisper-ready WAV file
    /// and produces a <see cref="TranscriptionResult"/> with text, segments, language, and duration.
    /// Progress in the 30-90% range comes from Whisper's own progress callback.
    /// </summary>
    private async Task<TranscriptionResult> RunWhisperAsync(
        string wavFilePath,
        string modelPath,
        TranscriptionOptions options,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ReportProgress(progress, 20.0, "Loading model...");

        // FromPath loads the native whisper library before anything reads the model, and the
        // model itself is read by the native side, which reports a bad file only when a builder
        // is created. An exception here therefore means the runtime cannot run on this machine.
        WhisperFactory whisperFactory;
        try
        {
            whisperFactory = WhisperFactory.FromPath(modelPath);
        }
        catch (Exception ex)
        {
            throw new TranscriptionRuntimeUnavailableException(
                $"The speech-to-text runtime could not be loaded on this computer: {ex.Message}", ex);
        }

        using var factory = whisperFactory;

        var forcedLanguage = string.IsNullOrWhiteSpace(options.Language)
            || string.Equals(options.Language, "auto", StringComparison.OrdinalIgnoreCase)
                ? null
                : options.Language;

        WhisperProcessorBuilder modelBuilder;
        try
        {
            modelBuilder = whisperFactory.CreateBuilder();
        }
        catch (WhisperModelLoadException ex)
        {
            throw new InvalidOperationException(
                $"The speech-to-text model file '{modelPath}' could not be loaded; it may be damaged " +
                "or incompatible. Remove it on the Model Manager page and download it again.", ex);
        }

        var percent = new TranscriptionPercent();
        var builder = modelBuilder
            .WithLanguage(forcedLanguage ?? "auto")
            .WithProgressHandler(whisperPercent =>
            {
                percent.Value = 30.0 + (Math.Clamp(whisperPercent, 0, 100) * 0.6);
                ReportProgress(progress, percent.Value, "Transcribing...");
            });

        var segments = new List<TranscriptionSegment>();
        string? detectedLanguage = null;

        await using var processor = builder.Build();

        ct.ThrowIfCancellationRequested();
        ReportProgress(progress, 30.0, "Transcribing...");

        await using var fileStream = new FileStream(
            wavFilePath,
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

            if (detectedLanguage is null && !string.IsNullOrWhiteSpace(segment.Language))
            {
                detectedLanguage = segment.Language;
            }

            ReportProgress(progress, percent.Value, "Transcribing...", transcriptSegment);
        }

        ReportProgress(progress, 90.0, "Finalizing...");

        var fullText = string.Join(" ", segments.Select(s => s.Text));

        long durationMs = segments.Count > 0
            ? segments[^1].EndMs
            : 0;

        var language = forcedLanguage ?? detectedLanguage;

        _log.Debug(
            "Whisper transcription complete - file: {FilePath}, segments: {SegmentCount}, " +
            "durationMs: {DurationMs}, language: {Language}",
            wavFilePath, segments.Count, durationMs, language ?? "unknown");

        return new TranscriptionResult
        {
            FullText = fullText,
            Segments = options.EnableTimestamps ? segments : [],
            Language = language,
            DurationMs = durationMs,
            ModelUsed = options.ModelSize,
        };
    }

    /// <summary>
    /// Decodes <paramref name="sourcePath"/> into a Whisper-ready WAV off the calling thread.
    /// A file this machine cannot decode surfaces as <see cref="NotSupportedException"/>, the
    /// same contract as an unsupported container.
    /// </summary>
    private static async Task ConvertForWhisperAsync(string sourcePath, string wavPath, CancellationToken ct)
    {
        try
        {
            await Task.Run(() => WhisperAudioConverter.ConvertToWhisperWav(sourcePath, wavPath, ct), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not NotSupportedException)
        {
            throw new NotSupportedException(
                $"The audio in '{Path.GetFileName(sourcePath)}' could not be decoded. " +
                "The file may be damaged, or no decoder for its codec is installed on this machine.",
                ex);
        }
    }

    private void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(ex, "Could not delete temporary file {Path}", path);
        }
    }

    /// <summary>Latest transcription percentage, written from Whisper's native callback thread.</summary>
    private sealed class TranscriptionPercent
    {
        private double _value = 30.0;

        public double Value
        {
            get => Volatile.Read(ref _value);
            set => Volatile.Write(ref _value, value);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds the canonical file system path for a Whisper GGML model binary.
    /// </summary>
    private string GetModelFilePath(string modelSize)
        => Path.Combine(_modelDirectory, $"ggml-{modelSize.ToLowerInvariant()}.bin");

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
}
