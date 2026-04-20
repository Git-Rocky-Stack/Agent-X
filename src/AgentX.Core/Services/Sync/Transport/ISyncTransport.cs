using AgentX.Core.Services.Sync.Models;

namespace AgentX.Core.Services.Sync.Transport;

/// <summary>
/// Abstracts the file-system transport layer for Collaborative Sync.
/// Responsible for reading/writing .axs files to the shared sync folder,
/// scanning for peer files, and managing the file lifecycle (e.g. renaming
/// processed files to .imported).
/// </summary>
public interface ISyncTransport
{
    /// <summary>
    /// Writes the given encrypted byte payload to a new .axs file in the sync folder,
    /// using the canonical naming convention:
    /// <c>agentx-sync-{deviceId}-{timestamp:yyyyMMddHHmmssffff}.axs</c>
    /// </summary>
    /// <param name="syncFolderPath">Absolute path to the shared sync folder.</param>
    /// <param name="deviceId">Stable device identifier for file naming.</param>
    /// <param name="exportedAt">UTC timestamp to embed in the filename.</param>
    /// <param name="data">Fully encrypted file payload (header + ciphertext).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The full path of the written file.</returns>
    Task<string> WriteSyncFileAsync(
        string syncFolderPath,
        string deviceId,
        DateTime exportedAt,
        byte[] data,
        CancellationToken ct);

    /// <summary>
    /// Scans the sync folder for .axs files that were NOT produced by
    /// <paramref name="localDeviceId"/> and returns each file's path and
    /// raw encrypted byte content.
    /// </summary>
    /// <param name="syncFolderPath">Absolute path to the shared sync folder.</param>
    /// <param name="localDeviceId">This installation's device ID (used to skip own files).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of peer file payloads, oldest first.</returns>
    Task<IReadOnlyList<SyncFilePayload>> ReadPeerFilesAsync(
        string syncFolderPath,
        string localDeviceId,
        CancellationToken ct);

    /// <summary>
    /// Marks a peer file as processed by renaming it to <c>.axs.imported</c>,
    /// preventing re-import on the next scan cycle.
    /// </summary>
    /// <param name="filePath">The original .axs file path.</param>
    Task MarkFileImportedAsync(string filePath);

    /// <summary>
    /// Validates that the sync folder exists (creates it if necessary) and is
    /// accessible for read/write operations.
    /// </summary>
    /// <param name="syncFolderPath">Absolute path to validate.</param>
    void EnsureFolderExists(string syncFolderPath);
}

/// <summary>
/// Represents a single peer .axs file read from the sync folder,
/// carrying both the file path (for lifecycle management) and the
/// raw encrypted byte content.
/// </summary>
public sealed class SyncFilePayload
{
    /// <summary>Absolute path to the .axs file on disk.</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>Raw encrypted bytes (header + ciphertext).</summary>
    public byte[] Data { get; init; } = [];

    /// <summary>File name without extension, for logging purposes.</summary>
    public string FileName { get; init; } = string.Empty;
}
