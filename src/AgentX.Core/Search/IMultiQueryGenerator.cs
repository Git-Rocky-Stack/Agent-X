namespace AgentX.Core.Search;

/// <summary>
/// Generates multiple query variations from a single user query to improve recall.
/// Different phrasings retrieve different relevant documents that a single query might miss.
/// </summary>
public interface IMultiQueryGenerator
{
    /// <summary>
    /// Generates alternative query phrasings for the given user query.
    /// </summary>
    /// <param name="query">The original user query.</param>
    /// <param name="count">Number of variations to generate (excluding the original).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of query variations including the original query at index 0.</returns>
    Task<IReadOnlyList<string>> GenerateQueryVariationsAsync(
        string query,
        int count = 3,
        CancellationToken ct = default);
}
