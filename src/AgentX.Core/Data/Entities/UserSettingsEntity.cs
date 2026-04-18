namespace AgentX.Core.Data.Entities;

public class UserSettingsEntity
{
    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ValueType { get; set; } = "string"; // string, int, bool, double, json
    public DateTime UpdatedAt { get; set; }

    // C13: SQLCipher-at-rest provisioning fields
    public bool EncryptionEnabled { get; set; }
    public string? EncryptionKeyStorageMode { get; set; }  // "DpapiWrapped" | "UserPassphrase"
    public string? EncryptionSaltBase64 { get; set; }       // 16-byte salt, base64 (UserPassphrase mode only)
    public string? DpapiWrappedKey { get; set; }            // DPAPI-encrypted hex key (DpapiWrapped mode only)
}
