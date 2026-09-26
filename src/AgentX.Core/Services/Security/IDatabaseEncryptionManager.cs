namespace AgentX.Core.Services.Security;

/// <summary>
/// Turns on at-rest encryption for the live application database and reports whether it is on.
/// This is the one entry point the UI uses: it provisions the key, releases the shared database
/// connection, migrates and verifies the file, commits the encryption marker last, and reopens the
/// connection with the key that now matches the file.
/// </summary>
public interface IDatabaseEncryptionManager
{
    /// <summary>True when the encryption marker exists, i.e. the database is encrypted.</summary>
    bool IsEncryptionEnabled { get; }

    /// <summary>The key storage mode recorded in the marker, or null when not encrypted or unreadable.</summary>
    KeyStorageMode? ProvisionedMode { get; }

    /// <summary>
    /// Encrypts the live database with a DPAPI-wrapped key. Idempotent: returns false without
    /// doing anything when encryption is already enabled, true after a successful migration.
    /// On failure the database is left plaintext, no marker is written, the connection is reopened
    /// without a key, and the exception propagates.
    /// </summary>
    Task<bool> EnableEncryptionAsync(CancellationToken ct = default);
}
