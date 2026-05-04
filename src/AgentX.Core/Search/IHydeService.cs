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
    /// Generates a hypothetical answer passage for the user's question.
    /// The returned text is suitable for use as an additional retrieval query
    /// in a multi-query RAG pipeline — embedding it puts the search closer in
    /// semantic space to actual answer documents than the raw question would.
    /// </summary>
    /// <param name="query">The user's question.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A 1-2 paragraph hypothetical answer document.</returns>
    Task<string> GenerateHypotheticalDocumentAsync(
        string query,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a hypothetical answer and returns its embedding vector.
    /// Equivalent to <see cref="GenerateHypotheticalDocumentAsync"/> followed
    /// by an embedding pass.
    /// </summary>
    /// <param name="query">The user's question.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The embedding of the hypothetical answer document.</returns>
    Task<float[]> GenerateHypotheticalEmbeddingAsync(
        string query,
        CancellationToken ct = default);
}
