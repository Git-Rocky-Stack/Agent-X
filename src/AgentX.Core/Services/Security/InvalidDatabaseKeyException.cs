using System;

namespace AgentX.Core.Services.Security;

/// <summary>
/// Thrown when the supplied key/passphrase cannot open the encrypted database.
/// Callers should prompt the user to re-enter their passphrase. Corresponds
/// to SQLite ErrorCode 26 ("file is not a database") at the driver layer.
/// </summary>
public sealed class InvalidDatabaseKeyException : Exception
{
    public InvalidDatabaseKeyException() : base("The supplied database key is invalid.") { }
    public InvalidDatabaseKeyException(Exception inner) : base("The supplied database key is invalid.", inner) { }
}
