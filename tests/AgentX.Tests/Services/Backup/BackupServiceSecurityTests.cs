using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AgentX.Core.Services.Backup;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Backup;

/// <summary>
/// Security tests for <see cref="BackupService"/>: ZIP path-traversal rejection during
/// validation, and the AES-256-GCM authenticated-encryption upgrade (with legacy
/// AES-256-CBC restore preserved).
/// </summary>
public sealed class BackupServiceSecurityTests
{
    // --- Path traversal in document entries ---

    [Fact]
    public void TryValidateDocumentEntries_AcceptsSafeNestedDocuments()
    {
        using var archive = BuildReadArchive(
            ("database/agentx.db", new byte[] { 1 }),
            ("manifest.json", new byte[] { 2 }),
            ("documents/notes/a.txt", Encoding.UTF8.GetBytes("ok")),
            ("documents/sub/dir/b.bin", new byte[] { 3, 4, 5 }));

        BackupService.TryValidateDocumentEntries(archive, out var reason).Should().BeTrue();
        reason.Should().BeNull();
    }

    [Theory]
    [InlineData("documents/../evil.txt")]
    [InlineData("documents/sub/../../evil.txt")]
    [InlineData("documents/C:/evil.txt")]
    [InlineData("documents//evil.txt")]
    public void TryValidateDocumentEntries_RejectsUnsafeEntries(string maliciousEntryName)
    {
        using var archive = BuildReadArchive(
            ("database/agentx.db", new byte[] { 1 }),
            ("manifest.json", new byte[] { 2 }),
            (maliciousEntryName, Encoding.UTF8.GetBytes("pwned")));

        BackupService.TryValidateDocumentEntries(archive, out var reason).Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
    }

    // --- AES-256-GCM authenticated encryption (V3, streamed) ---

    [Fact]
    public void EncryptBytes_ProducesV3FormatRecordingTheIterationCount()
    {
        var blob = BackupService.EncryptBytes(RandomNumberGenerator.GetBytes(1024), "correct horse");

        blob.Take(8).Should().Equal(Encoding.ASCII.GetBytes("AGXENC3\0"),
            "V3 archives must carry the streamed authenticated-encryption magic header");
        // SE19: the PBKDF2 count lives in the header (600k, matching the database key), so it can
        // rise later without breaking existing archives.
        System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(8)).Should().Be(600_000);
    }

    [Fact]
    public void DecryptBytes_LegacyV2GcmArchive_StillRestores()
    {
        var plaintext = RandomNumberGenerator.GetBytes(2048);
        const string password = "v2-pass";

        BackupService.DecryptBytes(EncryptLegacyGcm(plaintext, password), password).Should().Equal(plaintext);
    }

    [Fact]
    public void DecryptBytes_LegacyV2GcmArchive_WrongPasswordThrows()
    {
        var blob = EncryptLegacyGcm(RandomNumberGenerator.GetBytes(64), "right");

        var act = () => BackupService.DecryptBytes(blob, "wrong");

        act.Should().Throw<InvalidOperationException>().WithMessage("*password is incorrect*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4096)]      // exactly one chunk
    [InlineData(4097)]      // one chunk plus one byte
    [InlineData(3 * 4096)]  // exact multiple: the last full chunk is the final record
    public void StreamedEncryption_RoundTripsAcrossChunkBoundaries(int length)
    {
        var plaintext = RandomNumberGenerator.GetBytes(length);

        var blob = EncryptStreamed(plaintext, "pw", chunkSize: 4096);

        BackupService.DecryptBytes(blob, "pw").Should().Equal(plaintext);
    }

    [Fact]
    public void StreamedEncryption_TruncatedAtAChunkBoundary_FailsAuthentication()
    {
        var blob = EncryptStreamed(RandomNumberGenerator.GetBytes(3 * 4096), "pw", chunkSize: 4096);
        // Header (39) + two full records: dropping the last record must not look like a shorter archive.
        var truncated = blob.Take(39 + 2 * (4096 + 16)).ToArray();

        var act = () => BackupService.DecryptBytes(truncated, "pw");

        act.Should().Throw<InvalidOperationException>().WithMessage("*tampered*");
    }

    [Fact]
    public void StreamedEncryption_AppendedData_FailsAuthentication()
    {
        var blob = EncryptStreamed(RandomNumberGenerator.GetBytes(5000), "pw", chunkSize: 4096);
        var extended = blob.Concat(RandomNumberGenerator.GetBytes(4096 + 16)).ToArray();

        var act = () => BackupService.DecryptBytes(extended, "pw");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void StreamedEncryption_ModifiedHeader_FailsAuthentication()
    {
        var blob = EncryptStreamed(RandomNumberGenerator.GetBytes(100), "pw", chunkSize: 4096);
        blob[12] ^= 0x01; // chunk size field: authenticated as associated data

        var act = () => BackupService.DecryptBytes(blob, "pw");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EncryptBytes_DecryptBytes_RoundTrips()
    {
        var plaintext = RandomNumberGenerator.GetBytes(4096);
        const string password = "S3cur3-P@ssphrase";

        var decrypted = BackupService.DecryptBytes(BackupService.EncryptBytes(plaintext, password), password);

        decrypted.Should().Equal(plaintext);
    }

    [Fact]
    public void DecryptBytes_TamperedCiphertext_Throws()
    {
        var blob = BackupService.EncryptBytes(RandomNumberGenerator.GetBytes(2048), "pw");

        // Flip a bit in the final ciphertext byte — GCM authentication must reject it.
        blob[^1] ^= 0xFF;

        var act = () => BackupService.DecryptBytes(blob, "pw");
        act.Should().Throw<InvalidOperationException>().WithMessage("*tampered*");
    }

    [Fact]
    public void DecryptBytes_WrongPassword_Throws()
    {
        var blob = BackupService.EncryptBytes(RandomNumberGenerator.GetBytes(512), "right-password");

        var act = () => BackupService.DecryptBytes(blob, "wrong-password");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DecryptBytes_LegacyCbcArchive_StillRestores()
    {
        var plaintext = Encoding.UTF8.GetBytes("legacy AES-256-CBC backup payload");
        const string password = "legacy-pass";

        // A V1 archive produced by the previous CBC scheme must still restore.
        var legacy = EncryptLegacyCbc(plaintext, password);

        BackupService.DecryptBytes(legacy, password).Should().Equal(plaintext);
    }

    // --- Helpers ---

    private static ZipArchive BuildReadArchive(params (string name, byte[] data)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(data, 0, data.Length);
            }
        }

        ms.Position = 0;
        // Read archive takes ownership of the MemoryStream (disposed with the archive).
        return new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
    }

    /// <summary>Encrypts with the streamed V3 writer using a small chunk size (and a fast KDF).</summary>
    private static byte[] EncryptStreamed(byte[] plaintext, string password, int chunkSize)
    {
        using var output = new MemoryStream();
        using (var encrypting = BackupArchiveCrypto.CreateEncryptingStream(output, password, iterations: 1_000, chunkSize: chunkSize))
        {
            // Uneven writes exercise the chunk buffering.
            for (var offset = 0; offset < plaintext.Length; offset += 1000)
                encrypting.Write(plaintext, offset, Math.Min(1000, plaintext.Length - offset));
        }

        return output.ToArray();
    }

    /// <summary>
    /// Reproduces the V2 one-shot AES-256-GCM format (100,000 iterations, no iteration count in
    /// the header) written by earlier builds.
    /// </summary>
    private static byte[] EncryptLegacyGcm(byte[] plaintext, string password)
    {
        var magic = Encoding.ASCII.GetBytes("AGXENC2\0");
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using (var gcm = new AesGcm(key, 16))
        {
            gcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        return magic.Concat(salt).Concat(nonce).Concat(tag).Concat(ciphertext).ToArray();
    }

    /// <summary>
    /// Reproduces the original V1 AES-256-CBC archive format so the backward-compatible
    /// restore path can be verified against an authentic legacy blob.
    /// </summary>
    private static byte[] EncryptLegacyCbc(byte[] plaintext, string password)
    {
        const int saltSize = 16, ivSize = 16, iterations = 100_000, keyBits = 256, blockBits = 128;
        var magic = Encoding.ASCII.GetBytes("AGXENC\0\0");
        var salt = RandomNumberGenerator.GetBytes(saltSize);
        var iv = RandomNumberGenerator.GetBytes(ivSize);

        using var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        var key = kdf.GetBytes(keyBits / 8);

        using var aes = Aes.Create();
        aes.KeySize = keyBits;
        aes.BlockSize = blockBits;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;

        using var encryptor = aes.CreateEncryptor();
        var ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);

        var result = new byte[magic.Length + saltSize + ivSize + ciphertext.Length];
        magic.CopyTo(result, 0);
        salt.CopyTo(result, magic.Length);
        iv.CopyTo(result, magic.Length + saltSize);
        ciphertext.CopyTo(result, magic.Length + saltSize + ivSize);
        return result;
    }
}
