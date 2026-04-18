using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AgentX.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentX.Core.Services.Security;

public sealed class DatabaseKeyService : IDatabaseKeyService
{
    private const int KeyLengthBytes = 32;        // 256-bit key for SQLCipher
    private const int SaltLengthBytes = 16;
    private const int Pbkdf2Iterations = 600_000; // OWASP 2023 recommendation for PBKDF2-HMAC-SHA256

    private readonly AgentXDbContext _db;
    private readonly IDpapiEncryptionService _dpapi;

    public DatabaseKeyService(AgentXDbContext db, IDpapiEncryptionService dpapi)
    {
        _db = db;
        _dpapi = dpapi;
    }

    public async Task<DatabaseKeyMaterial> GetOrCreateKeyAsync(KeyStorageMode mode, string? passphrase = null)
    {
        if (mode == KeyStorageMode.UserPassphrase && string.IsNullOrEmpty(passphrase))
            throw new ArgumentException("Passphrase required for UserPassphrase mode.", nameof(passphrase));

        var settings = await EnsureSettingsRowAsync();

        if (settings.EncryptionEnabled && !string.IsNullOrEmpty(settings.EncryptionKeyStorageMode))
        {
            var existingMode = Enum.Parse<KeyStorageMode>(settings.EncryptionKeyStorageMode);
            return existingMode switch
            {
                KeyStorageMode.DpapiWrapped => UnwrapDpapiKey(settings.DpapiWrappedKey!),
                KeyStorageMode.UserPassphrase => DerivePassphraseKey(passphrase!, Convert.FromBase64String(settings.EncryptionSaltBase64!)),
                _ => throw new InvalidOperationException($"Unknown mode: {existingMode}")
            };
        }

        // First-time provisioning
        return mode switch
        {
            KeyStorageMode.DpapiWrapped => await ProvisionDpapiWrappedAsync(settings),
            KeyStorageMode.UserPassphrase => await ProvisionPassphraseAsync(settings, passphrase!),
            _ => throw new InvalidOperationException($"Unknown mode: {mode}")
        };
    }

    public async Task<DatabaseKeyMaterial> UnlockWithPassphraseAsync(string passphrase)
    {
        var settings = await _db.UserSettings.FirstOrDefaultAsync();
        if (settings is null || !settings.EncryptionEnabled || string.IsNullOrEmpty(settings.EncryptionSaltBase64))
            throw new InvalidOperationException("Database encryption is not provisioned in UserPassphrase mode.");
        if (settings.EncryptionKeyStorageMode != nameof(KeyStorageMode.UserPassphrase))
            throw new InvalidOperationException("Provisioned mode is not UserPassphrase.");

        return DerivePassphraseKey(passphrase, Convert.FromBase64String(settings.EncryptionSaltBase64));
    }

    public async Task<bool> IsProvisionedAsync()
    {
        var settings = await _db.UserSettings.FirstOrDefaultAsync();
        return settings is not null && settings.EncryptionEnabled;
    }

    public async Task<KeyStorageMode?> GetProvisionedModeAsync()
    {
        var settings = await _db.UserSettings.FirstOrDefaultAsync();
        if (settings is null || !settings.EncryptionEnabled || string.IsNullOrEmpty(settings.EncryptionKeyStorageMode))
            return null;
        return Enum.Parse<KeyStorageMode>(settings.EncryptionKeyStorageMode);
    }

    private async Task<Data.Entities.UserSettingsEntity> EnsureSettingsRowAsync()
    {
        var settings = await _db.UserSettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            settings = new Data.Entities.UserSettingsEntity();
            _db.UserSettings.Add(settings);
        }
        return settings;
    }

    private async Task<DatabaseKeyMaterial> ProvisionDpapiWrappedAsync(Data.Entities.UserSettingsEntity settings)
    {
        var keyBytes = RandomNumberGenerator.GetBytes(KeyLengthBytes);
        var hexKey = Convert.ToHexString(keyBytes);
        var wrapped = _dpapi.Encrypt(hexKey);

        settings.EncryptionEnabled = true;
        settings.EncryptionKeyStorageMode = nameof(KeyStorageMode.DpapiWrapped);
        settings.DpapiWrappedKey = wrapped;
        settings.EncryptionSaltBase64 = null;
        await _db.SaveChangesAsync();

        return DatabaseKeyMaterial.FromBytes(keyBytes, KeyStorageMode.DpapiWrapped);
    }

    private async Task<DatabaseKeyMaterial> ProvisionPassphraseAsync(Data.Entities.UserSettingsEntity settings, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLengthBytes);

        settings.EncryptionEnabled = true;
        settings.EncryptionKeyStorageMode = nameof(KeyStorageMode.UserPassphrase);
        settings.EncryptionSaltBase64 = Convert.ToBase64String(salt);
        settings.DpapiWrappedKey = null;
        await _db.SaveChangesAsync();

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
