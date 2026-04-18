using System;

namespace AgentX.Core.Services.Security;

/// <summary>
/// Persisted snapshot of the database encryption state, stored OUTSIDE the encrypted
/// database (as a sibling JSON file alongside <c>agentx.db</c>) so startup can determine
/// the unlock path without first opening the DB.
/// </summary>
/// <remarks>
/// This record carries ALL encryption key state — the wrapped DPAPI key (for
/// <see cref="KeyStorageMode.DpapiWrapped"/>) or the PBKDF2 salt (for
/// <see cref="KeyStorageMode.UserPassphrase"/>). Storing this material inside the
/// encrypted DB creates a chicken-and-egg deadlock: you can't read the DB without
/// the key, and you can't get the key without reading the DB.
/// </remarks>
/// <param name="Version">Schema version of the marker file. Current version is 1.</param>
/// <param name="StorageMode">Which key-storage mode is provisioned.</param>
/// <param name="EnabledAt">Timestamp (UTC) at which encryption was first provisioned.</param>
/// <param name="DpapiWrappedKey">
/// DPAPI-encrypted 32-byte database key (encoded as a hex string before wrapping, with
/// the "DPAPI:" prefix after wrapping). Populated for <see cref="KeyStorageMode.DpapiWrapped"/>
/// mode only. Null otherwise.
/// </param>
/// <param name="SaltBase64">
/// 16-byte PBKDF2 salt, base64-encoded. Populated for
/// <see cref="KeyStorageMode.UserPassphrase"/> mode only. Null otherwise.
/// </param>
public sealed record EncryptionStateInfo(
    int Version,
    KeyStorageMode StorageMode,
    DateTimeOffset EnabledAt,
    string? DpapiWrappedKey,
    string? SaltBase64);
