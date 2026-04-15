using AgentX.Core.Search.Models;

namespace AgentX.Core.Search;

/// <summary>
/// Orchestrates the Retrieval-Augmented Generation pipeline:
/// 1. Embeds the user question
/// 2. Retrieves relevant context chunks via semantic search
/// 3. Builds a grounded prompt with context
/// 4. Streams the AI response
/// 5. Extracts citations from the response
/// </summary>
public interface IRagPipeline
{
    /// <summary>
    /// Executes the full RAG pipeline: search for context, build prompt, stream response.
    /// </summary>
    /// <param name="question">The user's natural language question.</param>
    /// <param name="collectionId">Optional collection scope (null = search all).</param>
    /// <param name="onToken">Callback invoked for each streamed token.</param>
    /// <param name="enableResearchMode">
    /// When <c>true</c>, augments the vault context with web search results (Deep Research Mode).
    /// Requires a configured <see cref="IWebSearchService"/> injected into the pipeline.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The complete RAG response with citations and optional web citations.</returns>
    Task<RagResponse> AskAsync(
        string question,
        long? collectionId = null,
        Action<string>? onToken = null,
        bool enableResearchMode = false,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the number of indexed chunks available for RAG queries.
    /// Used to show the user how much knowledge is available.
    /// </summary>
    Task<long> GetIndexedChunkCountAsync(CancellationToken ct = default);
}
