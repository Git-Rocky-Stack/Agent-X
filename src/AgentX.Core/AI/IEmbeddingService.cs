namespace AgentX.Core.AI;

/// <summary>
/// Generates vector embeddings from text content using a local embedding model.
/// </summary>
public interface IEmbeddingService
{
    int Dimensions { get; }
    string ModelName { get; }

    /// <summary>
    /// Full version identifier for the active embedding model, e.g.
    /// <c>"all-minilm:1.0"</c>. Used by retrieval to detect chunks
    /// embedded with an incompatible model and exclude them from results.
    /// Format: <c>{ModelName}:{SchemaVersion}</c>.
    /// </summary>
    string ModelVersion { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default);
}
