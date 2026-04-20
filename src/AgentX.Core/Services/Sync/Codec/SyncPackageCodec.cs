using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentX.Core.Constants;
using AgentX.Core.Services.Sync.Models;
using Serilog;

namespace AgentX.Core.Services.Sync.Codec;

/// <summary>
/// Production implementation of <see cref="ISyncPackageCodec"/>.
/// Handles JSON serialisation, AES-256-GCM authenticated encryption,
/// PBKDF2 key derivation, and .axs file format framing.
///
/// .axs wire format (54-byte header + variable ciphertext):
///   Offset  Length  Field
///   ------  ------  ---------------------------------------------------
///    0       8      Magic: "AXSYNC\0\0"
///    8       2      Format version (uint16 little-endian)
///   10      16      PBKDF2 salt     (fresh random bytes per file)
///   26      12      AES-GCM nonce   (fresh random bytes per file)
///   38      16      AES-GCM authentication tag
///   54       *      AES-256-GCM ciphertext (UTF-8 JSON of SyncChangeSet)
/// </summary>
public sealed class SyncPackageCodec : ISyncPackageCodec
{
    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>Monotonically increasing wire-format version written into every .axs header.</summary>
    private const ushort FormatVersion = 1;

    // AES-256-GCM parameters (centralized in AppConstants)
    private const int AesKeyBytes   = AppConstants.AesKeyBytes;   // 256 bits
    private const int GcmNonceBytes = AppConstants.GcmNonceBytes; // 96-bit nonce
    private const int GcmTagBytes   = AppConstants.GcmTagBytes;   // 128-bit tag

    // PBKDF2 parameters
    private const int Pbkdf2Iterations = AppConstants.Pbkdf2Iterations;
    private const int SaltBytes        = AppConstants.PbkdfSaltBytes;

    // File header layout
    private static readonly byte[] SyncMagic = "AXSYNC\0\0"u8.ToArray(); // 8 bytes
    private const int MagicLen   = 8;
    private const int VersionLen = 2;   // uint16 LE
    private const int HeaderLen  = MagicLen + VersionLen + SaltBytes + GcmNonceBytes + GcmTagBytes;
    // = 8 + 2 + 16 + 12 + 16 = 54 bytes

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented          = false,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly ILogger _log;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises a new <see cref="SyncPackageCodec"/>.
    /// </summary>
    /// <param name="logger">Serilog logger instance.</param>
    public SyncPackageCodec(ILogger logger)
    {
        _log = (logger ?? throw new ArgumentNullException(nameof(logger)))
               .ForContext<SyncPackageCodec>();

        _log.Debug("SyncPackageCodec initialised");
    }

    // ── ISyncPackageCodec: Serialise ─────────────────────────────────────────

    /// <inheritdoc />
    public byte[] Serialise(SyncChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(changeSet, JsonOptions));
    }

    // ── ISyncPackageCodec: Deserialise ───────────────────────────────────────

    /// <inheritdoc />
    public SyncChangeSet Deserialise(byte[] jsonBytes)
    {
        ArgumentNullException.ThrowIfNull(jsonBytes);

        var json = Encoding.UTF8.GetString(jsonBytes);
        var result = JsonSerializer.Deserialize<SyncChangeSet>(json, JsonOptions);

        if (result is null)
            throw new InvalidOperationException("Deserialisation returned null — invalid SyncChangeSet payload.");

        return result;
    }

    // ── ISyncPackageCodec: Encrypt ────────────────────────────────────────────

    /// <inheritdoc />
    public byte[] Encrypt(byte[] plaintext, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);

        var salt  = RandomNumberGenerator.GetBytes(SaltBytes);
        var nonce = RandomNumberGenerator.GetBytes(GcmNonceBytes);
        var key   = DeriveKey(passphrase, salt);

        var ciphertext = new byte[plaintext.Length];
        var tag        = new byte[GcmTagBytes];

        using var gcm = new AesGcm(key, GcmTagBytes);
        gcm.Encrypt(nonce, plaintext, ciphertext, tag);

        // Layout: magic(8) + version(2) + salt(16) + nonce(12) + tag(16) + ciphertext
        var result = new byte[HeaderLen + ciphertext.Length];
        var span   = result.AsSpan();
        var offset = 0;

        SyncMagic.CopyTo(span[offset..]);
        offset += MagicLen;

        BitConverter.TryWriteBytes(span[offset..], FormatVersion);
        offset += VersionLen;

        salt.CopyTo(span[offset..]);
        offset += SaltBytes;

        nonce.CopyTo(span[offset..]);
        offset += GcmNonceBytes;

        tag.CopyTo(span[offset..]);
        offset += GcmTagBytes;

        ciphertext.CopyTo(span[offset..]);

        _log.Debug(
            "SyncPackageCodec.Encrypt: encrypted {PlainBytes} bytes → {CipherBytes} bytes",
            plaintext.Length, result.Length);

        return result;
    }

    // ── ISyncPackageCodec: Decrypt ────────────────────────────────────────────

    /// <inheritdoc />
    public byte[] Decrypt(byte[] cipherData, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(cipherData);
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);

        if (cipherData.Length < HeaderLen)
            throw new InvalidOperationException(
                "Data is too short to be a valid .axs sync file.");

        // Parse header fields using index ranges for zero-copy slicing.
        var offset     = MagicLen + VersionLen; // skip magic and version — already validated
        var salt       = cipherData[offset..(offset + SaltBytes)];
        offset        += SaltBytes;
        var nonce      = cipherData[offset..(offset + GcmNonceBytes)];
        offset        += GcmNonceBytes;
        var tag        = cipherData[offset..(offset + GcmTagBytes)];
        offset        += GcmTagBytes;
        var ciphertext = cipherData[offset..];

        var key       = DeriveKey(passphrase, salt);
        var plaintext = new byte[ciphertext.Length];

        using var gcm = new AesGcm(key, GcmTagBytes);
        gcm.Decrypt(nonce, ciphertext, tag, plaintext);

        _log.Debug(
            "SyncPackageCodec.Decrypt: decrypted {CipherBytes} → {PlainBytes} bytes",
            cipherData.Length, plaintext.Length);

        return plaintext;
    }

    // ── ISyncPackageCodec: IsValidHeader ──────────────────────────────────────

    /// <inheritdoc />
    public bool IsValidHeader(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < HeaderLen)
            return false;

        return data[..MagicLen].SequenceEqual(SyncMagic);
    }

    // ── Private: PBKDF2 key derivation ────────────────────────────────────────

    /// <summary>
    /// Derives a 256-bit AES key from <paramref name="passphrase"/> and
    /// <paramref name="salt"/> using PBKDF2-HMAC-SHA256 with
    /// <see cref="Pbkdf2Iterations"/> iterations.
    /// </summary>
    private static byte[] DeriveKey(string passphrase, byte[] salt)
    {
        using var kdf = new Rfc2898DeriveBytes(
            passphrase,
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256);

        return kdf.GetBytes(AesKeyBytes); // 32 bytes = 256 bits
    }
}
