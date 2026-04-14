namespace AgentX.Core.Services.Security;

/// <summary>
/// Reports the security status of API key storage.
/// After DPAPI integration, keys are always encrypted at rest
/// using Windows DPAPI (per-user, per-machine scope).
/// </summary>
public sealed class SecurityStatusService : ISecurityStatusService
{
    /// <inheritdoc />
    public bool AreKeysEncrypted => true;

    /// <inheritdoc />
    public string GetEncryptionStatusDescription()
        => "API keys are encrypted with Windows DPAPI (per-user, per-machine)";
}