namespace AgentX.Core.Services.Security;

public enum KeyStorageMode
{
    /// <summary>Auto-generated 32-byte key stored DPAPI-wrapped in UserSettings. Transparent to user (Starter/Professional tier default).</summary>
    DpapiWrapped = 0,

    /// <summary>User supplies a passphrase on each launch (Ultimate tier). Key derived via PBKDF2-HMAC-SHA256.</summary>
    UserPassphrase = 1,
}
