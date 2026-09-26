using AgentX.Core.Data.Entities;

namespace AgentX.Core.Documents;

/// <summary>
/// Outcome of a multi-file import, so callers can report what actually happened instead of
/// assuming every file they passed in was imported.
/// </summary>
public sealed class DocumentImportReport
{
    /// <summary>
    /// Documents created. Includes documents recorded as "failed" because their text could
    /// not be extracted; see <see cref="ExtractionFailedCount"/>.
    /// </summary>
    public List<DocumentEntity> Imported { get; } = new();

    /// <summary>Files skipped because a document with identical content already exists.</summary>
    public List<string> Duplicates { get; } = new();

    /// <summary>Files that could not be imported at all, with the reason.</summary>
    public List<DocumentImportFailure> Failed { get; } = new();

    /// <summary>Imported documents whose text could not be extracted (status "failed").</summary>
    public int ExtractionFailedCount => Imported.Count(d => d.IndexingStatus == "failed");
}

/// <summary>A file that could not be imported, and why.</summary>
public sealed record DocumentImportFailure(string FilePath, string Reason);

/// <summary>
/// Thrown when an imported file has the same content as an existing document. Derives from
/// <see cref="InvalidOperationException"/> and keeps the historical message, so existing
/// handlers that match either continue to work.
/// </summary>
public sealed class DuplicateDocumentException : InvalidOperationException
{
    public DuplicateDocumentException(long existingDocumentId, string existingFileName)
        : base($"A document with identical content already exists: '{existingFileName}' (ID {existingDocumentId}).")
    {
        ExistingDocumentId = existingDocumentId;
        ExistingFileName = existingFileName;
    }

    /// <summary>The document that already holds this content.</summary>
    public long ExistingDocumentId { get; }

    /// <summary>File name of that document.</summary>
    public string ExistingFileName { get; }
}
