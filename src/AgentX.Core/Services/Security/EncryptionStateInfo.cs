using System;

namespace AgentX.Core.Services.Security;

/// <summary>
/// Persisted snapshot of the database encryption state, stored outside the database
/// so startup can determine the unlock path without first opening the DB.
/// </summary>
public sealed record EncryptionStateInfo(
    int Version,
    KeyStorageMode StorageMode,
    DateTimeOffset EnabledAt);
