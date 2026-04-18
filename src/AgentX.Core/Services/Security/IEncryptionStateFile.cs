using System.Threading.Tasks;

namespace AgentX.Core.Services.Security;

/// <summary>
/// Reads and writes the sibling marker file that stores the database encryption state
/// (mode + wrapped key OR salt) OUTSIDE the encrypted database. Stored outside because
/// the encrypted DB cannot be opened without this material.
/// </summary>
public interface IEncryptionStateFile
{
    /// <summary>True if the marker file exists on disk.</summary>
    bool Exists();

    /// <summary>Reads the current marker, or null if it does not exist.</summary>
    EncryptionStateInfo? Read();

    /// <summary>
    /// Writes the given <see cref="EncryptionStateInfo"/> record. Overwrites any
    /// existing marker. Call this LAST in the enable-encryption flow, after
    /// MigrateToEncryptedAsync succeeds — so a failed conversion does not leave a
    /// stale marker claiming encryption is on when it is not.
    /// </summary>
    Task WriteAsync(EncryptionStateInfo info);

    /// <summary>Removes the marker. Used by the disable-encryption flow (future feature).</summary>
    void Delete();
}
