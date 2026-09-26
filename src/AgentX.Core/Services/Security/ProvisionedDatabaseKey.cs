namespace AgentX.Core.Services.Security;

/// <summary>
/// Freshly generated database key material together with the encryption marker that describes it.
/// The marker is NOT yet on disk: the enable-encryption flow writes it through
/// <see cref="IEncryptionStateFile.WriteAsync"/> only after the database has been migrated to
/// <see cref="Key"/> and verified, so a failed migration never leaves a marker behind.
/// </summary>
/// <param name="Key">The 32-byte database key to migrate the database to.</param>
/// <param name="Marker">The marker record to persist once the migration has been verified.</param>
public sealed record ProvisionedDatabaseKey(DatabaseKeyMaterial Key, EncryptionStateInfo Marker);
