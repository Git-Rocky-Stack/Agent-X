namespace AgentX.Core.Exceptions;

/// <summary>
/// Base exception for all AgentX-specific errors.
/// Provides a structured error code and an optional user-friendly message
/// suitable for display in the UI layer.
/// </summary>
public class AgentXException : Exception
{
    /// <summary>
    /// A machine-readable error code that uniquely identifies the category of error.
    /// Examples: "ENTITY_NOT_FOUND", "VALIDATION_FAILED", "PLUGIN_ERROR".
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// A human-readable message suitable for display in the UI.
    /// When <c>null</c>, the caller should fall back to a generic error message.
    /// </summary>
    public string? UserFriendlyMessage { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="AgentXException"/> with a detail message.
    /// </summary>
    /// <param name="message">The technical detail message describing the error.</param>
    public AgentXException(string message)
        : base(message)
    {
        ErrorCode = "AGENTX_ERROR";
    }

    /// <summary>
    /// Initializes a new instance of <see cref="AgentXException"/> with a detail message
    /// and an inner exception that caused this error.
    /// </summary>
    /// <param name="message">The technical detail message describing the error.</param>
    /// <param name="inner">The exception that is the cause of the current exception.</param>
    public AgentXException(string message, Exception inner)
        : base(message, inner)
    {
        ErrorCode = "AGENTX_ERROR";
    }

    /// <summary>
    /// Initializes a new instance of <see cref="AgentXException"/> with a detail message
    /// and a structured error code.
    /// </summary>
    /// <param name="message">The technical detail message describing the error.</param>
    /// <param name="errorCode">A machine-readable error code for this error category.</param>
    public AgentXException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="AgentXException"/> with a detail message,
    /// a structured error code, and an inner exception.
    /// </summary>
    /// <param name="message">The technical detail message describing the error.</param>
    /// <param name="errorCode">A machine-readable error code for this error category.</param>
    /// <param name="inner">The exception that is the cause of the current exception.</param>
    public AgentXException(string message, string errorCode, Exception inner)
        : base(message, inner)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="AgentXException"/> with full context:
    /// a detail message, error code, user-friendly message, and inner exception.
    /// </summary>
    /// <param name="message">The technical detail message describing the error.</param>
    /// <param name="errorCode">A machine-readable error code for this error category.</param>
    /// <param name="userFriendlyMessage">
    /// A message suitable for display to end users, or <c>null</c> if not applicable.
    /// </param>
    /// <param name="inner">
    /// The exception that is the cause of the current exception, or <c>null</c> if none.
    /// </param>
    public AgentXException(string message, string errorCode, string? userFriendlyMessage, Exception? inner)
        : base(message, inner)
    {
        ErrorCode = errorCode;
        UserFriendlyMessage = userFriendlyMessage;
    }
}
