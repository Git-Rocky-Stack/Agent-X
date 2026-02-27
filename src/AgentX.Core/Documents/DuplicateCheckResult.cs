namespace AgentX.Core.Documents;

/// <summary>
/// Result of a pre-import duplicate check against the knowledge vault.
/// Indicates whether the incoming file matches an existing document by content hash.
/// </summary>
public class DuplicateCheckResult
{
    /// <summary>
    /// True if a matching document was found in the vault.
    /// </summary>
    public bool IsDuplicate { get; set; }

    /// <summary>
    /// True if the match is an exact byte-for-byte duplicate (identical SHA-256 hash).
    /// </summary>
    public bool IsExactMatch { get; set; }

    /// <summary>
    /// The ID of the existing document that matches, if any.
    /// </summary>
    public long? ExistingDocumentId { get; set; }

    /// <summary>
    /// The file name of the existing matching document, if any.
    /// </summary>
    public string? ExistingFileName { get; set; }

    /// <summary>
    /// The similarity score (1.0 for exact match, lower for near-duplicates).
    /// </summary>
    public float MatchScore { get; set; }
}
