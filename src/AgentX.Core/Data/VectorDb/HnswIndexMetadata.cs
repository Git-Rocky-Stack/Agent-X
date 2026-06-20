using System.Text.Json.Serialization;

namespace AgentX.Core.Data.VectorDb;

/// <summary>
/// Metadata for a persisted HNSW index, serialized as JSON alongside the binary index.
/// Used to detect index staleness and validate compatibility on load.
/// </summary>
internal sealed class HnswIndexMetadata
{
    /// <summary>
    /// Schema version for forward-compatible migration.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// The schema version of this metadata file.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; init; } = CurrentVersion;

    /// <summary>
    /// Total number of vectors in the index at the time of persistence.
    /// </summary>
    [JsonPropertyName("count")]
    public long Count { get; init; }

    /// <summary>
    /// HNSW M parameter: maximum connections per layer (except layer 0).
    /// </summary>
    [JsonPropertyName("m")]
    public int M { get; init; } = 16;

    /// <summary>
    /// HNSW EfConstruction parameter: candidate list size during index build.
    /// </summary>
    [JsonPropertyName("efConstruction")]
    public int EfConstruction { get; init; } = 200;

    /// <summary>
    /// The dimensionality of vectors stored in this index.
    /// </summary>
    [JsonPropertyName("dimensions")]
    public int Dimensions { get; init; }

    /// <summary>
    /// UTC timestamp when the index was last persisted to disk.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// The number of deleted entries tracked in the stale set at persistence time.
    /// Used to determine if an index rebuild is needed on load.
    /// </summary>
    [JsonPropertyName("staleCount")]
    public long StaleCount { get; init; }
}
