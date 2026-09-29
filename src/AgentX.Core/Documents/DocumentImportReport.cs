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

    /// <summary>
    /// Files skipped because a document with identical content already exists, each with the
    /// document it matched, so a caller can use that document instead (for example, add it to
    /// the collection the file was meant for).
    /// </summary>
    public List<DocumentImportDuplicate> Duplicates { get; } = new();

    /// <summary>Files that could not be imported at all, with the reason.</summary>
    public List<DocumentImportFailure> Failed { get; } = new();

    /// <summary>Imported documents whose text could not be extracted (status "failed").</summary>
    public int ExtractionFailedCount => Imported.Count(d => d.IndexingStatus == "failed");
}

/// <summary>A file that could not be imported, and why.</summary>
public sealed record DocumentImportFailure(string FilePath, string Reason);

/// <summary>A file that was not imported because an existing document has the same content.</summary>
/// <param name="FilePath">The file that was skipped.</param>
/// <param name="ExistingDocumentId">The document that already holds this content.</param>
/// <param name="ExistingFileName">File name of that document.</param>
public sealed record DocumentImportDuplicate(string FilePath, long ExistingDocumentId, string ExistingFileName);

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
