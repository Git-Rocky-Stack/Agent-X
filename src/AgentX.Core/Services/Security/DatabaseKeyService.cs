using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace AgentX.Core.Services.Security;

/// <summary>
/// Provisions, wraps/derives, and unwraps/re-derives the 32-byte SQLCipher database key.
/// All persistent state (mode, DPAPI-wrapped key, PBKDF2 salt, enabledAt) lives in the
/// sibling <see cref="IEncryptionStateFile"/> marker — OUTSIDE the encrypted database —
/// because the encrypted DB cannot be opened until we already have the key.
/// </summary>
/// <remarks>
/// This service intentionally does NOT depend on <c>AgentXDbContext</c>. Storing
/// encryption state inside the DB created a chicken-and-egg deadlock on startup and
/// also collided with the key-value convention used by <c>UserSettings</c>.
/// </remarks>
public sealed class DatabaseKeyService : IDatabaseKeyService
{
    private const int KeyLengthBytes = 32;        // 256-bit key for SQLCipher
    private const int SaltLengthBytes = 16;
    private const int Pbkdf2Iterations = 600_000; // OWASP 2023 recommendation for PBKDF2-HMAC-SHA256

    private readonly IEncryptionStateFile _stateFile;
    private readonly IDpapiEncryptionService _dpapi;

    public DatabaseKeyService(IEncryptionStateFile stateFile, IDpapiEncryptionService dpapi)
    {
        _stateFile = stateFile ?? throw new ArgumentNullException(nameof(stateFile));
        _dpapi = dpapi ?? throw new ArgumentNullException(nameof(dpapi));
    }

    public async Task<DatabaseKeyMaterial> GetOrCreateKeyAsync(KeyStorageMode mode, string? passphrase = null)
    {
        if (mode == KeyStorageMode.UserPassphrase && string.IsNullOrEmpty(passphrase))
            throw new ArgumentException("Passphrase required for UserPassphrase mode.", nameof(passphrase));

        var existing = _stateFile.Read();
        if (existing is not null)
        {
            return existing.StorageMode switch
            {
                KeyStorageMode.DpapiWrapped => UnwrapDpapiKey(
                    existing.DpapiWrappedKey
                        ?? throw new InvalidOperationException("Encryption state marker is in DpapiWrapped mode but DpapiWrappedKey is missing.")),
                KeyStorageMode.UserPassphrase => DerivePassphraseKey(
                    passphrase!,
                    Convert.FromBase64String(
                        existing.SaltBase64
                            ?? throw new InvalidOperationException("Encryption state marker is in UserPassphrase mode but SaltBase64 is missing."))),
                _ => throw new InvalidOperationException($"Unknown mode: {existing.StorageMode}")
            };
        }

        // First-time provisioning — marker does not yet exist.
        return mode switch
        {
            KeyStorageMode.DpapiWrapped => await ProvisionDpapiWrappedAsync(),
            KeyStorageMode.UserPassphrase => await ProvisionPassphraseAsync(passphrase!),
            _ => throw new InvalidOperationException($"Unknown mode: {mode}")
        };
    }

    public Task<DatabaseKeyMaterial> UnlockWithPassphraseAsync(string passphrase)
    {
        if (string.IsNullOrEmpty(passphrase))
            throw new ArgumentException("Passphrase must not be null or empty.", nameof(passphrase));

        var existing = _stateFile.Read()
            ?? throw new InvalidOperationException("Database encryption is not provisioned.");
        if (existing.StorageMode != KeyStorageMode.UserPassphrase)
            throw new InvalidOperationException("Provisioned mode is not UserPassphrase.");
        if (string.IsNullOrEmpty(existing.SaltBase64))
            throw new InvalidOperationException("Encryption state marker is in UserPassphrase mode but SaltBase64 is missing.");

        var salt = Convert.FromBase64String(existing.SaltBase64);
        return Task.FromResult(DerivePassphraseKey(passphrase, salt));
    }

    public Task<bool> IsProvisionedAsync() => Task.FromResult(_stateFile.Exists());

    public Task<KeyStorageMode?> GetProvisionedModeAsync() =>
        Task.FromResult<KeyStorageMode?>(_stateFile.Read()?.StorageMode);

    private async Task<DatabaseKeyMaterial> ProvisionDpapiWrappedAsync()
    {
        var keyBytes = RandomNumberGenerator.GetBytes(KeyLengthBytes);
        var hexKey = Convert.ToHexString(keyBytes);
        var wrapped = _dpapi.Encrypt(hexKey);

        var info = new EncryptionStateInfo(
            Version: EncryptionStateFile.CurrentVersion,
            StorageMode: KeyStorageMode.DpapiWrapped,
            EnabledAt: DateTimeOffset.UtcNow,
            DpapiWrappedKey: wrapped,
            SaltBase64: null);
        await _stateFile.WriteAsync(info);

        return DatabaseKeyMaterial.FromBytes(keyBytes, KeyStorageMode.DpapiWrapped);
    }

    private async Task<DatabaseKeyMaterial> ProvisionPassphraseAsync(string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLengthBytes);

        var info = new EncryptionStateInfo(
            Version: EncryptionStateFile.CurrentVersion,
            StorageMode: KeyStorageMode.UserPassphrase,
            EnabledAt: DateTimeOffset.UtcNow,
            DpapiWrappedKey: null,
            SaltBase64: Convert.ToBase64String(salt));
        await _stateFile.WriteAsync(info);

        return DerivePassphraseKey(passphrase, salt);
    }

    private DatabaseKeyMaterial UnwrapDpapiKey(string wrappedHexKey)
    {
        var hexKey = _dpapi.Decrypt(wrappedHexKey);
        var bytes = Convert.FromHexString(hexKey);
        return DatabaseKeyMaterial.FromBytes(bytes, KeyStorageMode.DpapiWrapped);
    }

    private static DatabaseKeyMaterial DerivePassphraseKey(string passphrase, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(passphrase),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256);
        var keyBytes = pbkdf2.GetBytes(KeyLengthBytes);
        return DatabaseKeyMaterial.FromBytes(keyBytes, KeyStorageMode.UserPassphrase);
    }
}
