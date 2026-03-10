namespace AgentX.Core.Search;

/// <summary>
/// Hypothetical Document Embeddings (HyDE). Generates a hypothetical answer
/// to the user's question, then embeds that answer instead of the raw question.
/// The hypothetical document is closer in embedding space to the actual answer
/// documents, improving retrieval quality for complex questions.
/// </summary>
public interface IHydeService
{
    /// <summary>
    /// Generates a hypothetical answer and returns its embedding vector.
    /// </summary>
    /// <param name="query">The user's question.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The embedding of the hypothetical answer document.</returns>
    Task<float[]> GenerateHypotheticalEmbeddingAsync(
        string query,
        CancellationToken ct = default);
}
