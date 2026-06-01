using System.Security.Cryptography;
using AgentX.Core.AI.Models;
using AgentX.Core.Constants;
using Serilog;

namespace AgentX.Core.AI;

/// <inheritdoc cref="IBuiltInModelBootstrap" />
public sealed class BuiltInModelBootstrap : IBuiltInModelBootstrap
{
    /// <summary>Default built-in model file name — kept in sync with the OFFLINE installer and LocalLlmProvider.</summary>
    public const string DefaultModelFileName = "llama-3.2-3b-instruct-q4_k_m.gguf";

    /// <summary>Default human-friendly model name.</summary>
    public const string DefaultModelDisplayName = "Llama 3.2 3B Instruct (Q4_K_M)";

    /// <summary>Default public download source (HuggingFace, no auth required).</summary>
    public const string DefaultDownloadUrl =
        "https://huggingface.co/hugging-quants/Llama-3.2-3B-Instruct-Q4_K_M-GGUF/resolve/main/llama-3.2-3b-instruct-q4_k_m.gguf";

    // The real Q4_K_M 3B file is ~2.02 GB; this is a display / progress-fallback figure only.
    private const long DefaultExpectedSizeBytes = 2_019_000_000L;

    // Validity floor: a file smaller than this is treated as a truncated/partial leftover and a
    // re-download is triggered. Set well below the real size, well above any plausible partial.
    private const long DefaultMinimumValidBytes = 1_700_000_000L;

    private readonly HttpClient _httpClient;
    private readonly string _modelsDirectory;
    private readonly string _downloadUrl;
    private readonly long _minimumValidBytes;
    private readonly string? _expectedSha256;
    private readonly ILogger _logger;

    /// <inheritdoc />
    public string ModelFileName { get; }

    /// <inheritdoc />
    public string ModelDisplayName { get; }

    /// <inheritdoc />
    public long ExpectedSizeBytes { get; }

    /// <param name="httpClient">HTTP client used for the download (injected so the download is testable).</param>
    /// <param name="modelsDirectory">Directory the model is read from and written to (e.g. <c>%LOCALAPPDATA%\AgentX\Models</c>).</param>
    /// <param name="logger">Serilog logger.</param>
    /// <param name="modelFileName">Override the model file name (defaults to <see cref="DefaultModelFileName"/>).</param>
    /// <param name="modelDisplayName">Override the display name (defaults to <see cref="DefaultModelDisplayName"/>).</param>
    /// <param name="downloadUrl">Override the download URL (defaults to <see cref="DefaultDownloadUrl"/>).</param>
    /// <param name="expectedSizeBytes">Approximate model size for display/progress fallback.</param>
    /// <param name="minimumValidBytes">Size floor below which an on-disk file is considered truncated.</param>
    /// <param name="expectedSha256">Optional SHA-256 (hex) to verify the download against. When null, the size checks are the only integrity gate.</param>
    public BuiltInModelBootstrap(
        HttpClient httpClient,
        string modelsDirectory,
        ILogger logger,
        string? modelFileName = null,
        string? modelDisplayName = null,
        string? downloadUrl = null,
        long expectedSizeBytes = DefaultExpectedSizeBytes,
        long minimumValidBytes = DefaultMinimumValidBytes,
        string? expectedSha256 = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _modelsDirectory = modelsDirectory ?? throw new ArgumentNullException(nameof(modelsDirectory));
        _logger = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<BuiltInModelBootstrap>();
        ModelFileName = string.IsNullOrWhiteSpace(modelFileName) ? DefaultModelFileName : modelFileName!;
        ModelDisplayName = string.IsNullOrWhiteSpace(modelDisplayName) ? DefaultModelDisplayName : modelDisplayName!;
        _downloadUrl = string.IsNullOrWhiteSpace(downloadUrl) ? DefaultDownloadUrl : downloadUrl!;
        ExpectedSizeBytes = expectedSizeBytes;
        _minimumValidBytes = minimumValidBytes;
        _expectedSha256 = expectedSha256;
    }

    /// <inheritdoc />
    public string ModelPath => Path.Combine(_modelsDirectory, ModelFileName);

    /// <inheritdoc />
    public bool IsInstalled()
    {
        var path = ModelPath;
        if (!File.Exists(path)) return false;
        try
        {
            return new FileInfo(path).Length >= _minimumValidBytes;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Could not stat model file {Path}; treating as not installed", path);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task EnsureInstalledAsync(IProgress<ModelDownloadProgress>? progress = null, CancellationToken ct = default)
    {
        if (IsInstalled())
        {
            var size = SafeLength(ModelPath);
            _logger.Information("Built-in model already present at {Path} ({Bytes:N0} bytes)", ModelPath, size);
            progress?.Report(new ModelDownloadProgress
            {
                ModelId = ModelFileName,
                Status = "Already installed",
                CompletedBytes = size,
                TotalBytes = size
            });
            return;
        }

        await DownloadAsync(progress, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DownloadAsync(IProgress<ModelDownloadProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_modelsDirectory);
        var finalPath = ModelPath;
        var partPath = finalPath + ".part";

        // Remove any stale partial from a previous interrupted attempt before starting fresh.
        TryDelete(partPath);

        _logger.Information("Downloading built-in model {Model} from {Url}", ModelFileName, _downloadUrl);
        progress?.Report(new ModelDownloadProgress
        {
            ModelId = ModelFileName,
            Status = "Connecting...",
            CompletedBytes = 0,
            TotalBytes = ExpectedSizeBytes
        });

        try
        {
            using var response = await _httpClient
                .GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var advertisedLength = response.Content.Headers.ContentLength;
            var totalBytes = advertisedLength is > 0 ? advertisedLength.Value : ExpectedSizeBytes;
            long downloaded = 0;

            await using (var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var fileStream = new FileStream(
                partPath, FileMode.Create, FileAccess.Write, FileShare.None,
                AppConstants.FileStreamBufferSize, useAsync: true))
            {
                var buffer = new byte[AppConstants.FileStreamBufferSize];
                int read;
                while ((read = await contentStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    downloaded += read;
                    progress?.Report(new ModelDownloadProgress
                    {
                        ModelId = ModelFileName,
                        Status = "Downloading...",
                        CompletedBytes = downloaded,
                        TotalBytes = totalBytes
                    });
                }
            }

            // Integrity gate. A complete transfer must (a) match the server's advertised length when
            // it was provided, and (b) clear the validity floor. Either failure rejects the download
            // and removes the partial so the app never loads a truncated model.
            var partLength = new FileInfo(partPath).Length;
            if (advertisedLength is > 0 && partLength != advertisedLength.Value)
            {
                TryDelete(partPath);
                throw new IOException(
                    $"Model download is incomplete: received {partLength:N0} of {advertisedLength.Value:N0} bytes.");
            }
            if (partLength < _minimumValidBytes)
            {
                TryDelete(partPath);
                throw new IOException(
                    $"Model download is implausibly small ({partLength:N0} bytes); refusing to publish it.");
            }

            if (!string.IsNullOrEmpty(_expectedSha256))
            {
                await VerifySha256Async(partPath, _expectedSha256!, ct).ConfigureAwait(false);
            }

            // Atomic publish: never leave a partial file at the path the app loads from.
            File.Move(partPath, finalPath, overwrite: true);

            _logger.Information("Built-in model installed at {Path} ({Bytes:N0} bytes)", finalPath, partLength);
            progress?.Report(new ModelDownloadProgress
            {
                ModelId = ModelFileName,
                Status = "Complete",
                CompletedBytes = partLength,
                TotalBytes = partLength
            });
        }
        catch (OperationCanceledException)
        {
            _logger.Information("Built-in model download cancelled");
            TryDelete(partPath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Built-in model download failed");
            TryDelete(partPath);
            throw;
        }
    }

    private async Task VerifySha256Async(string path, string expectedHex, CancellationToken ct)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            AppConstants.FileStreamBufferSize, useAsync: true);
        var hashBytes = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        var actual = Convert.ToHexString(hashBytes);
        if (!string.Equals(actual, expectedHex, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(path);
            throw new IOException($"Model checksum mismatch: expected {expectedHex}, got {actual}.");
        }
    }

    private static long SafeLength(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Could not delete {Path}", path);
        }
    }
}
