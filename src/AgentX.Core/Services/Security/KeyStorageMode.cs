namespace AgentX.Core.Services.Security;

public enum KeyStorageMode
{
    /// <summary>Auto-generated 32-byte key stored DPAPI-wrapped in UserSettings. Transparent to the user and tied to their Windows account. This is the universal default, available to everyone.</summary>
    DpapiWrapped = 0,

    /// <summary>User supplies a passphrase on each launch. Key derived via PBKDF2-HMAC-SHA256. Retained for compatibility with vaults provisioned by older builds.</summary>
    UserPassphrase = 1,
}
