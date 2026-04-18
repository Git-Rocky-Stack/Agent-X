using System;

namespace AgentX.Core.Services.Security;

/// <summary>
/// Derived database key material. Holds 32 raw bytes as a 64-character uppercase hex string.
/// Keys are delivered to SQLCipher via PRAGMA key = "x'<hex>'" — NOT via the Microsoft.Data.Sqlite
/// Password connection-string property (which runs values through PBKDF2 and would produce a
/// different derived key).
/// </summary>
public sealed record DatabaseKeyMaterial(string HexKey, KeyStorageMode Mode)
{
    public static DatabaseKeyMaterial FromBytes(ReadOnlySpan<byte> keyBytes, KeyStorageMode mode)
    {
        if (keyBytes.Length != 32)
            throw new ArgumentException("Database key must be exactly 32 bytes (256 bits).", nameof(keyBytes));
        return new DatabaseKeyMaterial(Convert.ToHexString(keyBytes), mode);
    }
}
