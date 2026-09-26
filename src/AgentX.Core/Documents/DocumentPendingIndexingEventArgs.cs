using AgentX.Core.Documents.Models;

namespace AgentX.Core.Documents;

/// <summary>
/// Event data for <see cref="IDocumentService.DocumentPendingIndexing"/>: a document was
/// imported or reset for re-indexing and is waiting in "pending" status.
/// </summary>
public sealed class DocumentPendingIndexingEventArgs : EventArgs
{
    public DocumentPendingIndexingEventArgs(long documentId, ProcessedDocument? extracted = null)
    {
        DocumentId = documentId;
        Extracted = extracted;
    }

    /// <summary>The document waiting to be indexed.</summary>
    public long DocumentId { get; }

    /// <summary>
    /// The text extraction the import or re-index already performed, when available. The
    /// indexer reuses it instead of running the processor (PDF parsing, OCR, a web fetch)
    /// a second time, provided the source file has not changed in the meantime.
    /// </summary>
    public ProcessedDocument? Extracted { get; }
}
