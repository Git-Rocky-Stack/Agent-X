using AgentX.Core.Services.Intelligence.Models;

namespace AgentX.Core.Services.Intelligence;

/// <summary>
/// Detects duplicate and near-duplicate documents in the knowledge vault.
/// Supports both exact-match detection via content hashes and semantic
/// near-duplicate detection via vector embedding similarity.
/// </summary>
public interface IDuplicateDetectionService
{
    /// <summary>
    /// Scans all documents and groups those with identical content hashes.
    /// This is an efficient operation that requires no AI inference -- it relies
    /// solely on the SHA-256 content hashes computed during document import.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A list of duplicate groups, each containing two or more documents that share
    /// the same content hash. Returns an empty list if no duplicates are found.
    /// </returns>
    Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(CancellationToken ct = default);

    /// <summary>
    /// Finds documents that are near-duplicates based on semantic similarity.
    /// Uses vector embeddings to identify documents whose content is similar
    /// but not necessarily byte-for-byte identical (e.g., reformatted versions,
    /// minor edits, or different file formats of the same content).
    /// </summary>
    /// <param name="similarityThreshold">
    /// The minimum cosine similarity (0.0 to 1.0) required to consider two documents
    /// as near-duplicates. Defaults to 0.9 (90% similarity).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A list of near-duplicate groups. Each group contains documents whose embeddings
    /// exceed the similarity threshold. Returns an empty list if no near-duplicates are found.
    /// </returns>
    /// <remarks>
    /// This operation is more expensive than exact-hash detection as it requires loading
    /// and comparing vector embeddings. The scan is capped at the first 500 documents
    /// to avoid excessive computation time.
    /// </remarks>
    Task<IReadOnlyList<DuplicateGroup>> FindNearDuplicatesAsync(
        float similarityThreshold = 0.9f, CancellationToken ct = default);
}
