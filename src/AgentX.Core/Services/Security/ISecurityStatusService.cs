namespace AgentX.Core.Services.Security;

/// <summary>
/// Provides security status information for display in the UI,
/// such as whether API keys are encrypted at rest.
/// </summary>
public interface ISecurityStatusService
{
    /// <summary>
    /// Indicates whether API keys are currently encrypted at rest.
    /// </summary>
    bool AreKeysEncrypted { get; }

    /// <summary>
    /// Returns a human-readable description of the current encryption status.
    /// </summary>
    string GetEncryptionStatusDescription();
}
