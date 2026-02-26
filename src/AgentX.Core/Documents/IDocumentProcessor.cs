using AgentX.Core.Documents.Models;

namespace AgentX.Core.Documents;

/// <summary>
/// Extracts text content from a specific file type.
/// Each supported format (PDF, DOCX, TXT, MD, Image, Code) gets its own processor.
/// </summary>
public interface IDocumentProcessor
{
    IReadOnlySet<string> SupportedExtensions { get; }
    bool CanProcess(string filePath);
    Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default);
}
