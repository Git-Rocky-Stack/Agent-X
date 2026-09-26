namespace AgentX.Core.Documents;

/// <summary>
/// Thrown by an <see cref="IDocumentProcessor"/> when a file cannot be turned into text:
/// it is encrypted, corrupt, empty of extractable text, or its content could not be fetched.
/// The message is written for the user and ends up as the document's indexing error, so a
/// failed extraction is reported as a failed document instead of a "successful" import that
/// silently holds no text.
/// </summary>
public sealed class DocumentExtractionException : Exception
{
    public DocumentExtractionException(string message)
        : base(message)
    {
    }

    public DocumentExtractionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
