namespace AgentX.Core.Configuration;

/// <summary>
/// P2-4: Centralized catalog of RAG prompts. Resolves the active prompt text
/// from <c>RagPrompts.json</c> via <see cref="RagPromptOptions"/> with hot-reload
/// support; falls back to compile-time defaults in <c>RagPromptDefaults</c>
/// when no override is configured. Each property is read at every access so
/// configuration changes take effect on the next prompt site invocation
/// without process restart.
/// </summary>
public interface IRagPromptCatalog
{
    /// <summary>
    /// Static instruction prefix for the main RAG answering pipeline. Long,
    /// stable, and identical across every RAG turn — designed to be cacheable
    /// on Anthropic via the multi-block system prompt path.
    /// </summary>
    string RagSystemPrefix { get; }

    /// <summary>
    /// LLM-as-judge evaluator system prompt — scores context relevance,
    /// faithfulness, and answer relevance.
    /// </summary>
    string EvalSystem { get; }

    /// <summary>
    /// Cross-encoder reranker system prompt — assigns 0-10 relevance scores
    /// to retrieved passages.
    /// </summary>
    string RerankerSystem { get; }

    /// <summary>
    /// Contextual compressor system prompt — extracts only the sentences
    /// from a passage that directly answer the user's question.
    /// </summary>
    string CompressorSystem { get; }

    /// <summary>
    /// Multi-query generator system prompt — produces alternative phrasings
    /// of the user's query for parallel search.
    /// </summary>
    string MultiQuerySystem { get; }

    /// <summary>
    /// HyDE (Hypothetical Document Embeddings) system prompt — generates a
    /// plausible answer passage that is then embedded for retrieval.
    /// </summary>
    string HydeSystem { get; }
}
