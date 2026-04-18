using System.Threading.Tasks;

namespace AgentX.Core.Services.Security;

public interface IEncryptionStateFile
{
    /// <summary>True if the marker file exists on disk.</summary>
    bool Exists();

    /// <summary>Reads the current marker, or null if it does not exist.</summary>
    EncryptionStateInfo? Read();

    /// <summary>
    /// Writes the marker for the given mode. Overwrites any existing marker.
    /// Call this LAST in the enable-encryption flow, after MigrateToEncryptedAsync
    /// succeeds — so a failed conversion does not leave a stale marker claiming
    /// encryption is on when it is not.
    /// </summary>
    Task WriteAsync(KeyStorageMode mode);

    /// <summary>Removes the marker. Used by the disable-encryption flow (future feature).</summary>
    void Delete();
}
