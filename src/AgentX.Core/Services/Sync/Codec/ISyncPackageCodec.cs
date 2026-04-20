using AgentX.Core.Services.Sync.Models;

namespace AgentX.Core.Services.Sync.Codec;

/// <summary>
/// Handles serialization, encryption, and integrity verification for
/// Collaborative Sync packages. The codec is responsible for converting
/// between <see cref="SyncChangeSet"/> instances and their encrypted
/// wire format (.axs byte blobs).
/// </summary>
public interface ISyncPackageCodec
{
    /// <summary>
    /// Serialises <paramref name="changeSet"/> to UTF-8 JSON.
    /// </summary>
    /// <param name="changeSet">The change set to serialise.</param>
    /// <returns>UTF-8 encoded JSON bytes.</returns>
    byte[] Serialise(SyncChangeSet changeSet);

    /// <summary>
    /// Deserialises a <see cref="SyncChangeSet"/> from UTF-8 JSON bytes.
    /// </summary>
    /// <param name="jsonBytes">UTF-8 encoded JSON payload.</param>
    /// <returns>The deserialised <see cref="SyncChangeSet"/>.</returns>
    SyncChangeSet Deserialise(byte[] jsonBytes);

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> using AES-256-GCM with a key derived
    /// from <paramref name="passphrase"/> via PBKDF2-SHA256. Returns the complete
    /// .axs wire format (header + ciphertext).
    /// </summary>
    /// <param name="plaintext">UTF-8 JSON bytes to encrypt.</param>
    /// <param name="passphrase">User-supplied passphrase for key derivation.</param>
    /// <returns>Fully framed .axs byte array (magic + version + salt + nonce + tag + ciphertext).</returns>
    byte[] Encrypt(byte[] plaintext, string passphrase);

    /// <summary>
    /// Decrypts an AES-256-GCM encrypted .axs payload produced by <see cref="Encrypt"/>.
    /// </summary>
    /// <param name="cipherData">Complete .axs byte array (header + ciphertext).</param>
    /// <param name="passphrase">User-supplied passphrase for key derivation.</param>
    /// <returns>Decrypted UTF-8 JSON bytes.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// Thrown when the authentication tag does not match (wrong key, corrupted data).
    /// </exception>
    byte[] Decrypt(byte[] cipherData, string passphrase);

    /// <summary>
    /// Validates that <paramref name="data"/> begins with the expected .axs magic
    /// bytes and is long enough to contain a complete header.
    /// </summary>
    /// <param name="data">Raw bytes to validate.</param>
    /// <returns><see langword="true"/> if the header is valid; otherwise <see langword="false"/>.</returns>
    bool IsValidHeader(byte[] data);
}
