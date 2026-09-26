using System.Security.Cryptography;
using System.Text;
using AgentX.Core.Services.Security;

namespace AgentX.Tests.Helpers;

/// <summary>
/// Reversible stand-in for <see cref="DpapiEncryptionService"/> so settings and key tests run on
/// every platform (real DPAPI exists only on Windows). Values keep the real "DPAPI:" prefix
/// contract; the payload is plain base64, which is fine for tests and never used in production.
/// A ciphertext whose payload is in <see cref="Undecryptable"/> fails the way DPAPI fails for a
/// value protected under another Windows account.
/// </summary>
internal sealed class FakeDpapiEncryptionService : IDpapiEncryptionService
{
    private const string Prefix = "DPAPI:";

    /// <summary>Ciphertexts that throw on <see cref="Decrypt"/>.</summary>
    public HashSet<string> Undecryptable { get; } = new(StringComparer.Ordinal);

    public string Encrypt(string plaintext)
    {
        if (plaintext is null)
            return null!;

        return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
    }

    public string Decrypt(string ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        if (!ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Cannot decrypt value: missing prefix.");
        if (Undecryptable.Contains(ciphertext))
            throw new CryptographicException("Key not valid for use in specified state.");

        return Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext[Prefix.Length..]));
    }

    public bool IsEncrypted(string value) => value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);
}
