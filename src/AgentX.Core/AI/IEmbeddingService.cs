namespace AgentX.Core.AI;

/// <summary>
/// Generates vector embeddings from text content using a local embedding model.
/// </summary>
public interface IEmbeddingService
{
    int Dimensions { get; }
    string ModelName { get; }

    /// <summary>
    /// Identifies the embedding space of the vectors this service produces, e.g.
    /// <c>"ollama:all-minilm:384"</c>. Used to stamp chunks and by retrieval to exclude chunks
    /// embedded by a different provider, model or vector size.
    /// Format: <c>{ProviderId}:{ModelName}:{Dimensions}</c>.
    /// </summary>
    string ModelVersion { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default);
}
