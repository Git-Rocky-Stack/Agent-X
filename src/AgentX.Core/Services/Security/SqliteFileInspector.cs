using System.Text;

namespace AgentX.Core.Services.Security;

/// <summary>
/// Classifies a database file from its first bytes without opening it through SQLite, so startup
/// recovery can reason about files before any connection (and any key) exists.
/// <para>
/// A plaintext SQLite database always starts with the 16-byte magic <c>"SQLite format 3\0"</c>.
/// SQLCipher (as configured here, without a plaintext header) starts the file with a random salt,
/// so the magic never appears on an encrypted database.
/// </para>
/// </summary>
internal static class SqliteFileInspector
{
    private static readonly byte[] PlaintextMagic = Encoding.ASCII.GetBytes("SQLite format 3\0");

    /// <summary>True when the file exists and starts with the plaintext SQLite magic.</summary>
    public static bool HasPlaintextHeader(string path)
    {
        var header = ReadHeader(path);
        return header is not null
            && header.Length == PlaintextMagic.Length
            && header.AsSpan().SequenceEqual(PlaintextMagic);
    }

    /// <summary>
    /// True when the file looks like a complete SQLCipher database: it has at least one page
    /// (a whole number of 512-byte units, the smallest SQLite page size) and no plaintext magic.
    /// Empty or truncated files do not qualify, so recovery never treats them as authoritative.
    /// </summary>
    public static bool LooksEncrypted(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 512 || info.Length % 512 != 0)
            return false;

        return !HasPlaintextHeader(path);
    }

    private static byte[]? ReadHeader(string path)
    {
        if (!File.Exists(path))
            return null;

        // FileShare.ReadWrite | Delete: SQLite may hold the file open; reading 16 bytes must not
        // conflict with it.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[PlaintextMagic.Length];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);
            if (n == 0)
                break;
            read += n;
        }

        return read == buffer.Length ? buffer : buffer[..read];
    }
}
