using AgentX.Core.Services.Sync.Models;
using Serilog;

namespace AgentX.Core.Services.Sync.Transport;

/// <summary>
/// File-system implementation of <see cref="ISyncTransport"/>.
/// Manages .axs file I/O in the shared sync folder, including writing
/// outgoing sync files, scanning for peer files, and renaming processed
/// files to prevent re-import.
/// </summary>
public sealed class SyncTransport : ISyncTransport
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const string SyncFileExtension = ".axs";
    private const string SyncFilePrefix    = "agentx-sync-";

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly ILogger _log;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises a new <see cref="SyncTransport"/>.
    /// </summary>
    /// <param name="logger">Serilog logger instance.</param>
    public SyncTransport(ILogger logger)
    {
        _log = (logger ?? throw new ArgumentNullException(nameof(logger)))
               .ForContext<SyncTransport>();

        _log.Debug("SyncTransport initialised");
    }

    // ── ISyncTransport: WriteSyncFileAsync ────────────────────────────────────

    /// <inheritdoc />
    public async Task<string> WriteSyncFileAsync(
        string syncFolderPath,
        string deviceId,
        DateTime exportedAt,
        byte[] data,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);

        EnsureFolderExists(syncFolderPath);

        var fileName = BuildSyncFileName(deviceId, exportedAt);
        var filePath = Path.Combine(syncFolderPath, fileName);

        await File.WriteAllBytesAsync(filePath, data, ct).ConfigureAwait(false);

        _log.Debug(
            "SyncTransport.WriteSyncFileAsync: wrote {FileName} ({Bytes} bytes)",
            fileName, data.Length);

        return filePath;
    }

    // ── ISyncTransport: ReadPeerFilesAsync ────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<SyncFilePayload>> ReadPeerFilesAsync(
        string syncFolderPath,
        string localDeviceId,
        CancellationToken ct)
    {
        if (!Directory.Exists(syncFolderPath))
        {
            _log.Warning(
                "SyncTransport.ReadPeerFilesAsync: sync folder does not exist — skipping. Path={Path}",
                syncFolderPath);
            return [];
        }

        var files = Directory
            .EnumerateFiles(syncFolderPath, $"{SyncFilePrefix}*{SyncFileExtension}")
            .OrderBy(f => f) // oldest first via lexical ordering on timestamp
            .ToList();

        _log.Debug(
            "SyncTransport.ReadPeerFilesAsync: found {Count} .axs file(s)",
            files.Count);

        var peerFiles = new List<SyncFilePayload>();

        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();

            var fileName = Path.GetFileNameWithoutExtension(filePath);

            // Skip files this device produced.
            if (fileName.Contains(localDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                _log.Debug(
                    "SyncTransport.ReadPeerFilesAsync: skipping own file {FileName}",
                    fileName);
                continue;
            }

            try
            {
                var data = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);

                peerFiles.Add(new SyncFilePayload
                {
                    FilePath = filePath,
                    Data     = data,
                    FileName = fileName,
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Warning(ex,
                    "SyncTransport.ReadPeerFilesAsync: failed to read {FileName} — skipping",
                    fileName);
            }
        }

        _log.Debug(
            "SyncTransport.ReadPeerFilesAsync: returning {Count} peer file(s)",
            peerFiles.Count);

        return peerFiles;
    }

    // ── ISyncTransport: MarkFileImportedAsync ─────────────────────────────────

    /// <inheritdoc />
    public Task MarkFileImportedAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be empty.", nameof(filePath));

        var processedPath = filePath + ".imported";
        File.Move(filePath, processedPath, overwrite: true);

        _log.Debug(
            "SyncTransport.MarkFileImportedAsync: {File} renamed to .imported",
            Path.GetFileName(filePath));

        return Task.CompletedTask;
    }

    // ── ISyncTransport: EnsureFolderExists ────────────────────────────────────

    /// <inheritdoc />
    public void EnsureFolderExists(string syncFolderPath)
    {
        try
        {
            Directory.CreateDirectory(syncFolderPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Cannot create or access the sync folder at '{syncFolderPath}'.", ex);
        }
    }

    // ── Private: file naming ──────────────────────────────────────────────────

    /// <summary>
    /// Builds the canonical sync file name:
    /// <c>agentx-sync-{deviceId}-{exportedAt:yyyyMMddHHmmssffff}.axs</c>
    /// The sub-second component avoids collisions when multiple exports occur within
    /// the same second.
    /// </summary>
    internal static string BuildSyncFileName(string deviceId, DateTime exportedAt) =>
        $"{SyncFilePrefix}{deviceId}-{exportedAt:yyyyMMddHHmmssffff}{SyncFileExtension}";
}
