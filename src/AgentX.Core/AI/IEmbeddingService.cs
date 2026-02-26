namespace AgentX.Core.AI;

/// <summary>
/// Generates vector embeddings from text content using a local embedding model.
/// </summary>
public interface IEmbeddingService
{
    int Dimensions { get; }
    string ModelName { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default);
}
