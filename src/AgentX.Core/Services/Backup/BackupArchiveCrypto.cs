using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using AgentX.Core.Constants;

namespace AgentX.Core.Services.Backup;

/// <summary>
/// Password encryption for backup archives.
/// <para>
/// Byte layouts, selected by the 8-byte magic:
/// <list type="bullet">
///   <item><b>V3</b> <c>"AGXENC3\0"</c> (current): header = magic(8) + PBKDF2 iteration count
///   (uint32 LE) + plaintext chunk size (uint32 LE) + salt(16) + nonce prefix(7), 39 bytes. It
///   is followed by records of <c>ciphertext + tag(16)</c>, one per chunk. Every record is
///   AES-256-GCM with nonce = prefix(7) + chunk counter (uint32 BE) + final flag(1), and the
///   whole header as associated data. Records are processed one at a time, so archives of any
///   size stream through a fixed buffer; the counter and final flag make reordering, truncation
///   and appended data fail authentication. The iteration count is stored, so it can rise
///   without breaking old archives.</item>
///   <item><b>V2</b> <c>"AGXENC2\0"</c> (read-only): magic + salt(16) + nonce(12) + tag(16) +
///   one AES-256-GCM ciphertext, 100,000 PBKDF2 iterations.</item>
///   <item><b>V1</b> <c>"AGXENC\0\0"</c> (read-only): magic + salt(16) + iv(16) + AES-256-CBC
///   ciphertext, 100,000 PBKDF2 iterations.</item>
/// </list>
/// </para>
/// </summary>
internal static class BackupArchiveCrypto
{
    public const int MagicLength = 8;

    private const int SaltSize = AppConstants.PbkdfSaltBytes;
    private const int KeySize = AppConstants.AesKeyBytes;
    private const int TagSize = AppConstants.GcmTagBytes;
    private const int NonceSize = AppConstants.GcmNonceBytes;
    private const int NoncePrefixSize = NonceSize - sizeof(uint) - 1;
    private const int V3HeaderSize = MagicLength + sizeof(uint) + sizeof(uint) + SaltSize + NoncePrefixSize;

    /// <summary>Plaintext bytes per V3 record.</summary>
    public const int DefaultChunkSize = 1024 * 1024;

    // Limits applied to header values read from an untrusted archive, so a crafted header cannot
    // stall the KDF or force huge allocations.
    private const int MaxIterations = 10_000_000;
    private const int MinChunkSize = 4 * 1024;
    private const int MaxChunkSize = 16 * 1024 * 1024;

    private static readonly byte[] MagicV1 = Encoding.ASCII.GetBytes("AGXENC\0\0");
    private static readonly byte[] MagicV2 = Encoding.ASCII.GetBytes("AGXENC2\0");
    private static readonly byte[] MagicV3 = Encoding.ASCII.GetBytes("AGXENC3\0");

    private const string DecryptionFailedMessage =
        "Backup decryption failed: the password is incorrect or the archive has been tampered with.";

    /// <summary>True when <paramref name="header"/> starts with any encrypted-archive magic.</summary>
    public static bool IsEncrypted(ReadOnlySpan<byte> header)
        => header.StartsWith(MagicV3) || header.StartsWith(MagicV2) || header.StartsWith(MagicV1);

    /// <summary>True when the file at <paramref name="path"/> is an encrypted archive.</summary>
    public static bool IsEncryptedFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var header = new byte[MagicLength];
        var read = ReadFull(stream, header);
        return IsEncrypted(header.AsSpan(0, read));
    }

    /// <summary>
    /// Returns a write-only stream that encrypts everything written to it into
    /// <paramref name="output"/> in the V3 format. Disposing the stream writes the final record;
    /// the archive is incomplete (and fails authentication) until then.
    /// </summary>
    public static Stream CreateEncryptingStream(
        Stream output,
        string password,
        int iterations = AppConstants.BackupPbkdf2Iterations,
        int chunkSize = DefaultChunkSize,
        bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);

        return new EncryptingStream(output, password, iterations, chunkSize, leaveOpen);
    }

    /// <summary>Encrypts <paramref name="plaintext"/> in memory (V3).</summary>
    public static byte[] Encrypt(byte[] plaintext, string password)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        using var output = new MemoryStream();
        using (var encrypting = CreateEncryptingStream(output, password))
        {
            encrypting.Write(plaintext);
        }

        return output.ToArray();
    }

    /// <summary>Decrypts an archive held in memory (any version).</summary>
    public static byte[] Decrypt(byte[] data, string password)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (data.AsSpan().StartsWith(MagicV3))
        {
            using var input = new MemoryStream(data, writable: false);
            using var output = new MemoryStream();
            DecryptV3(input, output, password, CancellationToken.None);
            return output.ToArray();
        }

        if (data.AsSpan().StartsWith(MagicV2))
            return DecryptLegacyGcm(data, password);

        if (data.AsSpan().StartsWith(MagicV1))
            return DecryptLegacyCbc(data, password);

        throw new InvalidOperationException("Data is not a recognised encrypted Agent-X backup archive.");
    }

    /// <summary>
    /// Decrypts an archive from <paramref name="input"/> into <paramref name="output"/>. V3 is
    /// streamed record by record; the legacy one-shot formats are read into memory, as they were
    /// written. Throws <see cref="InvalidOperationException"/> for a wrong password or a damaged
    /// archive; the output then holds partial data and must be discarded.
    /// </summary>
    public static void DecryptToStream(Stream input, Stream output, string password, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var magic = new byte[MagicLength];
        var read = ReadFull(input, magic);
        if (read == MagicLength && magic.AsSpan().SequenceEqual(MagicV3))
        {
            DecryptV3(input, output, password, ct, magicAlreadyRead: true);
            return;
        }

        // Legacy formats were produced in memory; rebuild the whole blob and decrypt it at once.
        using var buffer = new MemoryStream();
        buffer.Write(magic, 0, read);
        input.CopyTo(buffer);
        var plaintext = Decrypt(buffer.ToArray(), password);
        output.Write(plaintext);
    }

    private static void DecryptV3(Stream input, Stream output, string password, CancellationToken ct, bool magicAlreadyRead = false)
    {
        var header = new byte[V3HeaderSize];
        MagicV3.CopyTo(header, 0);
        var offset = magicAlreadyRead ? MagicLength : 0;
        if (ReadFull(input, header.AsSpan(offset)) != V3HeaderSize - offset)
            throw new InvalidOperationException("Data is too short to be a valid encrypted archive.");
        if (!header.AsSpan(0, MagicLength).SequenceEqual(MagicV3))
            throw new InvalidOperationException("Data is not a recognised encrypted Agent-X backup archive.");

        var iterations = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(MagicLength));
        var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(MagicLength + sizeof(uint)));
        if (iterations is < 1 or > MaxIterations || chunkSize is < MinChunkSize or > MaxChunkSize)
            throw new InvalidOperationException("The encrypted archive header is invalid.");

        var salt = header.AsSpan(MagicLength + 2 * sizeof(uint), SaltSize).ToArray();
        var noncePrefix = header.AsSpan(MagicLength + 2 * sizeof(uint) + SaltSize, NoncePrefixSize).ToArray();
        var key = DeriveKey(password, salt, (int)iterations);

        var recordSize = (int)chunkSize + TagSize;
        var current = new byte[recordSize];
        var next = new byte[recordSize];
        var plaintext = new byte[chunkSize];
        var nonce = new byte[NonceSize];
        uint counter = 0;

        using var gcm = new AesGcm(key, TagSize);
        var currentLength = ReadFull(input, current);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (currentLength < TagSize)
                throw new InvalidOperationException(DecryptionFailedMessage);

            // Read ahead one record: only the record followed by end of stream is the final one.
            var nextLength = ReadFull(input, next);
            var isFinal = nextLength == 0;
            if (!isFinal && currentLength != recordSize)
                throw new InvalidOperationException(DecryptionFailedMessage);

            var cipherLength = currentLength - TagSize;
            BuildNonce(nonce, noncePrefix, counter, isFinal);
            try
            {
                gcm.Decrypt(
                    nonce,
                    current.AsSpan(0, cipherLength),
                    current.AsSpan(cipherLength, TagSize),
                    plaintext.AsSpan(0, cipherLength),
                    header);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException(DecryptionFailedMessage, ex);
            }

            output.Write(plaintext, 0, cipherLength);

            if (isFinal)
                return;

            (current, next) = (next, current);
            currentLength = nextLength;
            counter = checked(counter + 1);
        }
    }

    private static byte[] DecryptLegacyGcm(byte[] cipherData, string password)
    {
        var headerLen = MagicLength + SaltSize + NonceSize + TagSize;
        if (cipherData.Length < headerLen)
            throw new InvalidOperationException("Data is too short to be a valid encrypted archive.");

        var offset = MagicLength;
        var salt = cipherData[offset..(offset + SaltSize)]; offset += SaltSize;
        var nonce = cipherData[offset..(offset + NonceSize)]; offset += NonceSize;
        var tag = cipherData[offset..(offset + TagSize)]; offset += TagSize;
        var ciphertext = cipherData[offset..];

        var key = DeriveKey(password, salt, AppConstants.Pbkdf2Iterations);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var gcm = new AesGcm(key, TagSize);
            gcm.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(DecryptionFailedMessage, ex);
        }

        return plaintext;
    }

    private static byte[] DecryptLegacyCbc(byte[] cipherData, string password)
    {
        const int ivSize = AppConstants.IvSizeBytes;
        var headerLen = MagicLength + SaltSize + ivSize; // 40 bytes
        if (cipherData.Length < headerLen)
            throw new InvalidOperationException("Data is too short to be a valid encrypted archive.");

        var salt = cipherData[MagicLength..(MagicLength + SaltSize)];
        var iv = cipherData[(MagicLength + SaltSize)..headerLen];
        var ciphertext = cipherData[headerLen..];
        var key = DeriveKey(password, salt, AppConstants.Pbkdf2Iterations);

        try
        {
            using var aes = Aes.Create();
            aes.KeySize = AppConstants.AesKeySizeBits;
            aes.BlockSize = AppConstants.AesBlockSizeBits;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
        }
        catch (CryptographicException ex)
        {
            // CBC has no authentication; a padding error is the only signal of a wrong password.
            throw new InvalidOperationException(DecryptionFailedMessage, ex);
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations)
        => Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeySize);

    private static void BuildNonce(Span<byte> nonce, ReadOnlySpan<byte> prefix, uint counter, bool isFinal)
    {
        prefix.CopyTo(nonce);
        BinaryPrimitives.WriteUInt32BigEndian(nonce[NoncePrefixSize..], counter);
        nonce[NonceSize - 1] = isFinal ? (byte)1 : (byte)0;
    }

    private static int ReadFull(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0)
                break;
            total += read;
        }

        return total;
    }

    /// <summary>
    /// Buffers one chunk at a time. A full chunk is only sealed once more data arrives, so the
    /// chunk sealed at dispose (possibly empty) is always the one marked final.
    /// </summary>
    private sealed class EncryptingStream : Stream
    {
        private readonly Stream _output;
        private readonly bool _leaveOpen;
        private readonly AesGcm _gcm;
        private readonly byte[] _header = new byte[V3HeaderSize];
        private readonly byte[] _noncePrefix;
        private readonly byte[] _buffer;
        private readonly byte[] _cipher;
        private readonly byte[] _tag = new byte[TagSize];
        private readonly byte[] _nonce = new byte[NonceSize];
        private int _buffered;
        private uint _counter;
        private bool _disposed;

        public EncryptingStream(Stream output, string password, int iterations, int chunkSize, bool leaveOpen)
        {
            _output = output;
            _leaveOpen = leaveOpen;
            _buffer = new byte[chunkSize];
            _cipher = new byte[chunkSize];

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            _noncePrefix = RandomNumberGenerator.GetBytes(NoncePrefixSize);

            MagicV3.CopyTo(_header, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(_header.AsSpan(MagicLength), (uint)iterations);
            BinaryPrimitives.WriteUInt32LittleEndian(_header.AsSpan(MagicLength + sizeof(uint)), (uint)chunkSize);
            salt.CopyTo(_header, MagicLength + 2 * sizeof(uint));
            _noncePrefix.CopyTo(_header, MagicLength + 2 * sizeof(uint) + SaltSize);

            _gcm = new AesGcm(DeriveKey(password, salt, iterations), TagSize);
            _output.Write(_header);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_disposed;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            while (!buffer.IsEmpty)
            {
                if (_buffered == _buffer.Length)
                    SealChunk(isFinal: false);

                var take = Math.Min(buffer.Length, _buffer.Length - _buffered);
                buffer[..take].CopyTo(_buffer.AsSpan(_buffered));
                _buffered += take;
                buffer = buffer[take..];
            }
        }

        public override void Flush() => _output.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _disposed = true;
                try
                {
                    SealChunk(isFinal: true);
                    _output.Flush();
                }
                finally
                {
                    _gcm.Dispose();
                    if (!_leaveOpen)
                        _output.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        private void SealChunk(bool isFinal)
        {
            BuildNonce(_nonce, _noncePrefix, _counter, isFinal);
            _gcm.Encrypt(_nonce, _buffer.AsSpan(0, _buffered), _cipher.AsSpan(0, _buffered), _tag, _header);
            _output.Write(_cipher, 0, _buffered);
            _output.Write(_tag);
            _buffered = 0;
            _counter = checked(_counter + 1);
        }
    }
}
