namespace AgentX.Core.Configuration;

/// <summary>
/// P2-4: Bindable options class for <c>RagPrompts.json</c>. Each property is
/// an optional <c>string[]</c> so multi-line prompts can be expressed in JSON
/// as one-line-per-array-entry — much more readable than escaped newlines.
/// At resolution time the catalog joins each array with <c>\n</c>.
/// </summary>
/// <remarks>
/// Properties are nullable so an operator can override a single prompt
/// without specifying all six. Missing or empty arrays fall through to the
/// compile-time defaults in <see cref="RagPromptDefaults"/>.
/// </remarks>
public sealed class RagPromptOptions
{
    /// <summary>RAG answering system prefix (long, cacheable).</summary>
    public string[]? RagSystemPrefix { get; set; }

    /// <summary>LLM-as-judge evaluator system prompt.</summary>
    public string[]? EvalSystem { get; set; }

    /// <summary>Cross-encoder reranker system prompt.</summary>
    public string[]? RerankerSystem { get; set; }

    /// <summary>Contextual compressor system prompt.</summary>
    public string[]? CompressorSystem { get; set; }

    /// <summary>Multi-query generator system prompt.</summary>
    public string[]? MultiQuerySystem { get; set; }

    /// <summary>HyDE hypothetical-document system prompt.</summary>
    public string[]? HydeSystem { get; set; }
}
