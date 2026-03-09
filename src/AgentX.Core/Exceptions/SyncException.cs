namespace AgentX.Core.Exceptions;

/// <summary>
/// Thrown when a data synchronization operation fails.
/// Carries a <see cref="SyncErrorType"/> discriminator so callers can react
/// to specific categories of sync failure (e.g., encryption vs. timeout).
/// </summary>
public class SyncException : AgentXException
{
    private const string Code = "SYNC_ERROR";

    /// <summary>
    /// The specific category of synchronization error that occurred.
    /// </summary>
    public SyncErrorType SyncError { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="SyncException"/>.
    /// </summary>
    /// <param name="syncError">The category of sync failure.</param>
    /// <param name="message">A technical description of what went wrong.</param>
    public SyncException(SyncErrorType syncError, string message)
        : base(
            message: message,
            errorCode: Code,
            userFriendlyMessage: GetUserFriendlyMessage(syncError),
            inner: null)
    {
        SyncError = syncError;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SyncException"/> with an inner exception.
    /// </summary>
    /// <param name="syncError">The category of sync failure.</param>
    /// <param name="message">A technical description of what went wrong.</param>
    /// <param name="inner">The exception that is the cause of the current exception.</param>
    public SyncException(SyncErrorType syncError, string message, Exception inner)
        : base(
            message: message,
            errorCode: Code,
            userFriendlyMessage: GetUserFriendlyMessage(syncError),
            inner: inner)
    {
        SyncError = syncError;
    }

    /// <summary>
    /// Maps each <see cref="SyncErrorType"/> to a user-friendly explanation.
    /// </summary>
    private static string GetUserFriendlyMessage(SyncErrorType syncError) => syncError switch
    {
        SyncErrorType.ConfigurationMissing => "Sync configuration is incomplete. Please check your sync settings.",
        SyncErrorType.EncryptionFailed => "Failed to encrypt data for sync. Your data was not sent.",
        SyncErrorType.DecryptionFailed => "Failed to decrypt synced data. The sync key may be incorrect.",
        SyncErrorType.ConflictDetected => "A sync conflict was detected. Please resolve it before continuing.",
        SyncErrorType.IoError => "A file system error occurred during sync. Please check disk space and permissions.",
        SyncErrorType.Timeout => "The sync operation timed out. Please check your connection and try again.",
        _ => "An unexpected sync error occurred."
    };
}

/// <summary>
/// Classifies the specific category of synchronization failure.
/// </summary>
public enum SyncErrorType
{
    /// <summary>Required sync configuration (e.g., endpoint, credentials) is missing.</summary>
    ConfigurationMissing,

    /// <summary>Data encryption failed before transmission.</summary>
    EncryptionFailed,

    /// <summary>Received data could not be decrypted.</summary>
    DecryptionFailed,

    /// <summary>A merge conflict was detected between local and remote data.</summary>
    ConflictDetected,

    /// <summary>A file system I/O error occurred during the sync process.</summary>
    IoError,

    /// <summary>The sync operation exceeded its allowed time limit.</summary>
    Timeout
}
