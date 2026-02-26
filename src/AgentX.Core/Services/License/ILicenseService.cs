namespace AgentX.Core.Services.License;

/// <summary>
/// Service responsible for license activation, validation, and querying.
/// Uses offline-first validation — no network calls are required.
/// </summary>
public interface ILicenseService
{
    /// <summary>
    /// Returns the current license info. If no license is activated, returns a Trial-tier license.
    /// </summary>
    Task<LicenseInfo> GetCurrentLicenseAsync();

    /// <summary>
    /// Activates a license key. Validates format, checksum, and stores the activation in the database.
    /// </summary>
    Task<LicenseActivationResult> ActivateLicenseAsync(string licenseKey);

    /// <summary>
    /// Deactivates the current license, removing it from the database and reverting to Trial tier.
    /// </summary>
    Task<bool> DeactivateLicenseAsync();

    /// <summary>
    /// Re-validates the currently stored license (format + checksum). Updates LastValidatedAt on success.
    /// </summary>
    Task<bool> ValidateCurrentLicenseAsync();

    /// <summary>
    /// Generates a deterministic machine fingerprint based on hardware characteristics.
    /// Used as the instance identifier for license activations.
    /// </summary>
    string GetMachineFingerprint();
}

/// <summary>
/// Result of a license activation attempt. Contains success/failure status,
/// a human-readable message, the resulting license info, and any error code.
/// </summary>
public class LicenseActivationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public LicenseInfo? LicenseInfo { get; init; }
    public LicenseActivationError? Error { get; init; }
}

/// <summary>
/// Enumeration of possible license activation errors.
/// </summary>
public enum LicenseActivationError
{
    None,
    InvalidFormat,
    InvalidChecksum,
    AlreadyActivated,
    Expired,
    DatabaseError
}
