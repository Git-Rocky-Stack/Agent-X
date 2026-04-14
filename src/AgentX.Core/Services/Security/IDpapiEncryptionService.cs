namespace AgentX.Core.Services.Security;

/// <summary>
/// Provides DPAPI-based encryption and decryption for sensitive data such as API keys.
/// Uses Windows Data Protection API with CurrentUser scope for secure key storage.
/// </summary>
public interface IDpapiEncryptionService
{
    /// <summary>
    /// Encrypts plaintext using DPAPI and returns a base64-encoded string prefixed with "DPAPI:".
    /// </summary>
    /// <param name="plaintext">The plaintext value to encrypt. Null input returns null.</param>
    /// <returns>A "DPAPI:"-prefixed base64 string, or null if input was null.</returns>
    string Encrypt(string plaintext);

    /// <summary>
    /// Decrypts a DPAPI-encrypted value that was encrypted with <see cref="Encrypt"/>.
    /// </summary>
    /// <param name="ciphertext">A "DPAPI:"-prefixed base64 string to decrypt.</param>
    /// <returns>The original plaintext value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the value does not start with the "DPAPI:" prefix.</exception>
    /// <exception cref="FormatException">Thrown when the base64 portion is invalid.</exception>
    string Decrypt(string ciphertext);

    /// <summary>
    /// Determines whether a value has been encrypted by this service.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns>True if the value starts with the "DPAPI:" prefix; otherwise, false.</returns>
    bool IsEncrypted(string value);
}