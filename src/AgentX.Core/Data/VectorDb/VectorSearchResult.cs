namespace AgentX.Core.Data.VectorDb;

/// <summary>
/// Represents a single result from a vector similarity search.
/// Distance is the raw metric (lower = more similar for cosine distance),
/// while Similarity is the human-friendly inverse (higher = more similar).
/// </summary>
public class VectorSearchResult
{
    /// <summary>
    /// The ID of the document chunk that matched the query.
    /// </summary>
    public long ChunkId { get; set; }

    /// <summary>
    /// The raw distance metric between the query vector and this result.
    /// For cosine distance: 0.0 = identical, 2.0 = opposite.
    /// </summary>
    public double Distance { get; set; }

    /// <summary>
    /// Cosine similarity derived from the distance (1.0 - Distance).
    /// Range: -1.0 to 1.0 where 1.0 = identical.
    /// </summary>
    public double Similarity => 1.0 - Distance;
}
