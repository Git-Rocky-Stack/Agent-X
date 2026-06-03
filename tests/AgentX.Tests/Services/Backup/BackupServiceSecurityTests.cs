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
    // ── Path traversal in document entries ───────────────────────────────────

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

    // ── AES-256-GCM authenticated encryption (V2) ────────────────────────────

    [Fact]
    public void EncryptBytes_ProducesV2AuthenticatedFormat()
    {
        var blob = BackupService.EncryptBytes(RandomNumberGenerator.GetBytes(1024), "correct horse");

        blob.Take(8).Should().Equal(Encoding.ASCII.GetBytes("AGXENC2\0"),
            "V2 archives must carry the authenticated-encryption magic header");
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

    // ── Helpers ──────────────────────────────────────────────────────────────

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
