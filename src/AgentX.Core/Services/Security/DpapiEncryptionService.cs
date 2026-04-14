using System.Security.Cryptography;
using System.Text;

namespace AgentX.Core.Services.Security;

/// <summary>
/// DPAPI-based encryption service for protecting sensitive data such as API keys.
/// Uses <see cref="ProtectedData"/> with <see cref="DataProtectionScope.CurrentUser"/>
/// so that only the current Windows user can decrypt the protected data.
/// </summary>
public class DpapiEncryptionService : IDpapiEncryptionService
{
    private const string EncryptedPrefix = "DPAPI:";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
    public string Encrypt(string plaintext)
    {
        if (plaintext is null)
            return null!;

        byte[] plaintextBytes = Utf8NoBom.GetBytes(plaintext);
        byte[] encryptedBytes = ProtectedData.Protect(plaintextBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        string base64 = Convert.ToBase64String(encryptedBytes);
        return $"{EncryptedPrefix}{base64}";
    }

    /// <inheritdoc />
    public string Decrypt(string ciphertext)
    {
        if (ciphertext is null)
            throw new ArgumentNullException(nameof(ciphertext));

        if (!ciphertext.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"Cannot decrypt value: it does not start with the expected '{EncryptedPrefix}' prefix.");

        string base64 = ciphertext[EncryptedPrefix.Length..];
        byte[] encryptedBytes = Convert.FromBase64String(base64);
        byte[] plaintextBytes = ProtectedData.Unprotect(encryptedBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        return Utf8NoBom.GetString(plaintextBytes);
    }

    /// <inheritdoc />
    public bool IsEncrypted(string value)
    {
        return value is not null && value.StartsWith(EncryptedPrefix, StringComparison.Ordinal);
    }
}