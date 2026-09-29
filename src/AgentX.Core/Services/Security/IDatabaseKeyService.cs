using System.Threading.Tasks;

namespace AgentX.Core.Services.Security;

public interface IDatabaseKeyService
{
    /// <summary>
    /// Returns existing key material if already provisioned, or provisions a new one
    /// using the given mode. In UserPassphrase mode, passphrase must be non-null.
    /// Provisioning through this method writes the encryption marker immediately, so it must not
    /// be used to turn encryption on for an existing plaintext database: use
    /// <see cref="CreateUncommittedKeyAsync"/> and write the marker after the migration succeeds.
    /// </summary>
    Task<DatabaseKeyMaterial> GetOrCreateKeyAsync(KeyStorageMode mode, string? passphrase = null);

    /// <summary>
    /// Generates new key material for <paramref name="mode"/> WITHOUT writing the encryption
    /// marker. The caller persists <see cref="ProvisionedDatabaseKey.Marker"/> only after the
    /// database has been migrated to the returned key and verified. Throws
    /// <see cref="System.InvalidOperationException"/> when a marker already exists, because a
    /// second key would orphan the database the existing marker unlocks.
    /// </summary>
    Task<ProvisionedDatabaseKey> CreateUncommittedKeyAsync(KeyStorageMode mode, string? passphrase = null);

    /// <summary>
    /// Re-derives the key from an existing passphrase without creating a new one.
    /// The returned key is not validated here: the caller probes it against the
    /// encrypted database and re-prompts on failure. App.xaml.cs does this in
    /// UnlockWithPassphraseLoopAsync, where TryProbeKeyAsync treats SQLite
    /// ErrorCode 26 ("file is not a database") as a wrong passphrase and loops.
    /// </summary>
    Task<DatabaseKeyMaterial> UnlockWithPassphraseAsync(string passphrase);

    /// <summary>Returns true if encryption has been provisioned (even if not currently unlocked).</summary>
    Task<bool> IsProvisionedAsync();

    /// <summary>Returns the provisioned storage mode, or null if not provisioned.</summary>
    Task<KeyStorageMode?> GetProvisionedModeAsync();
}
