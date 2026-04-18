using System.Threading.Tasks;

namespace AgentX.Core.Services.Security;

public interface IDatabaseKeyService
{
    /// <summary>
    /// Returns existing key material if already provisioned, or provisions a new one
    /// using the given mode. In UserPassphrase mode, passphrase must be non-null.
    /// </summary>
    Task<DatabaseKeyMaterial> GetOrCreateKeyAsync(KeyStorageMode mode, string? passphrase = null);

    /// <summary>
    /// Re-derives the key from an existing passphrase without creating a new one.
    /// The returned key should be probed against the encrypted DB; if the probe fails
    /// the caller should throw InvalidDatabaseKeyException and prompt the user again.
    /// </summary>
    Task<DatabaseKeyMaterial> UnlockWithPassphraseAsync(string passphrase);

    /// <summary>Returns true if encryption has been provisioned (even if not currently unlocked).</summary>
    Task<bool> IsProvisionedAsync();

    /// <summary>Returns the provisioned storage mode, or null if not provisioned.</summary>
    Task<KeyStorageMode?> GetProvisionedModeAsync();
}
