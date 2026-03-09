namespace AgentX.Core.Exceptions;

/// <summary>
/// Thrown when a license operation fails (validation, activation, or enforcement).
/// Carries a <see cref="LicenseErrorType"/> discriminator so callers can distinguish
/// between format errors, checksum failures, expired licenses, and other conditions.
/// </summary>
public class LicenseException : AgentXException
{
    private const string Code = "LICENSE_ERROR";

    /// <summary>
    /// The specific category of license error that occurred.
    /// </summary>
    public LicenseErrorType LicenseError { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="LicenseException"/>.
    /// </summary>
    /// <param name="licenseError">The category of license failure.</param>
    /// <param name="message">A technical description of what went wrong.</param>
    public LicenseException(LicenseErrorType licenseError, string message)
        : base(
            message: message,
            errorCode: Code,
            userFriendlyMessage: GetUserFriendlyMessage(licenseError),
            inner: null)
    {
        LicenseError = licenseError;
    }

    /// <summary>
    /// Maps each <see cref="LicenseErrorType"/> to a user-friendly explanation.
    /// </summary>
    private static string GetUserFriendlyMessage(LicenseErrorType licenseError) => licenseError switch
    {
        LicenseErrorType.InvalidFormat => "The license key format is invalid. Please check that you entered it correctly.",
        LicenseErrorType.InvalidChecksum => "The license key could not be verified. Please double-check your key.",
        LicenseErrorType.AlreadyActivated => "This license key has already been activated.",
        LicenseErrorType.Expired => "Your license has expired. Please renew to continue using premium features.",
        LicenseErrorType.NotActivated => "No active license was found. Please activate a license key.",
        _ => "A license error occurred."
    };
}

/// <summary>
/// Classifies the specific category of license failure.
/// </summary>
public enum LicenseErrorType
{
    /// <summary>The license key string does not match the expected format.</summary>
    InvalidFormat,

    /// <summary>The embedded checksum does not match the computed checksum.</summary>
    InvalidChecksum,

    /// <summary>The license key has already been activated on this machine.</summary>
    AlreadyActivated,

    /// <summary>The license has passed its expiration date.</summary>
    Expired,

    /// <summary>No license has been activated on this machine.</summary>
    NotActivated
}
