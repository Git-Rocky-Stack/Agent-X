namespace AgentX.Core.Configuration;

/// <summary>
/// Provides centralized configuration for all RAG (Retrieval-Augmented Generation) parameters.
/// Enables runtime tuning of search, chunking, and embedding behavior without code changes.
/// </summary>
public interface IRagConfiguration
{
    // ═══════════════════════════════════════════════════════════════════
    //  Search Configuration
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Default number of results to retrieve from vector search.</summary>
    int DefaultTopK { get; }

    /// <summary>Minimum cosine similarity threshold for including results (0.0-1.0).</summary>
    float DefaultMinScore { get; }

    /// <summary>Maximum TopK allowed for any search request (prevents excessive retrieval).</summary>
    int MaxTopK { get; }

    /// <summary>
    /// Multiplier for retrieving extra results before metadata filtering.
    /// Actual retrieval = TopK * RetrievalMultiplier to compensate for filtered results.
    /// </summary>
    int RetrievalMultiplier { get; }

    // ═══════════════════════════════════════════════════════════════════
    //  Chunking Configuration
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Default target chunk size in tokens (approximate words).</summary>
    int DefaultChunkSize { get; }

    /// <summary>Default token overlap between consecutive chunks.</summary>
    int DefaultChunkOverlap { get; }

    /// <summary>Maximum allowed chunk size (hard limit for safety).</summary>
    int MaxChunkSize { get; }

    /// <summary>Minimum allowed chunk size (prevents tiny, meaningless chunks).</summary>
    int MinChunkSize { get; }

    // ═══════════════════════════════════════════════════════════════════
    //  Embedding Configuration
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Default embedding model name (e.g., "all-minilm", "nomic-embed-text").</summary>
    string DefaultEmbeddingModel { get; }

    /// <summary>Expected embedding dimensions for the default model.</summary>
    int DefaultEmbeddingDimensions { get; }

    /// <summary>
    /// Cache expiration time for query embeddings in minutes.
    /// Cached embeddings avoid re-computing for identical queries.
    /// </summary>
    int EmbeddingCacheExpirationMinutes { get; }

    /// <summary>Maximum number of embeddings to process in a single batch.</summary>
    int EmbeddingBatchSize { get; }

    // ═══════════════════════════════════════════════════════════════════
    //  Context Assembly Configuration
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Weight for semantic similarity in context selection (0.0-1.0).</summary>
    double SemanticWeight { get; }

    /// <summary>Weight for lexical (keyword) overlap in context selection (0.0-1.0).</summary>
    double LexicalWeight { get; }

    /// <summary>Weight for recency in context selection (0.0-1.0).</summary>
    double RecencyWeight { get; }

    /// <summary>Minimum tokens required for recall augmentation.</summary>
    int MinRecallBudgetTokens { get; }

    // ═══════════════════════════════════════════════════════════════════
    //  Semantic Memory Configuration
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Default temporal decay rate for memories (0.0 = no decay, higher = faster fade).</summary>
    double MemoryDecayRate { get; }

    /// <summary>Days before full decay is applied to memory importance.</summary>
    int MemoryDaysBeforeFullDecay { get; }

    /// <summary>Minimum semantic similarity to create associative memory links (0.0-1.0).</summary>
    float AssociativeLinkThreshold { get; }

    /// <summary>Maximum number of memories to retrieve for a query.</summary>
    int MaxMemoriesPerQuery { get; }

    // ═══════════════════════════════════════════════════════════════════
    //  Vector Store Configuration
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Embedding count below which linear scan is used instead of HNSW.</summary>
    int VectorStoreFallbackThreshold { get; }

    /// <summary>
    /// Fraction of stale entries that triggers HNSW index rebuild.
    /// E.g., 0.05 means rebuild if >5% of entries are stale.
    /// </summary>
    double StaleRebuildFraction { get; }

    /// <summary>HNSW M parameter: max connections per layer.</summary>
    int HnswM { get; }

    /// <summary>HNSW EfConstruction parameter: candidate list size during build.</summary>
    int HnswEfConstruction { get; }

    // ═══════════════════════════════════════════════════════════════════
    //  Reranking Configuration
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Whether LLM-based reranking is enabled.</summary>
    bool EnableLlmReranking { get; }

    /// <summary>Maximum tokens to allocate for LLM reranking prompt.</summary>
    int RerankerMaxTokens { get; }

    /// <summary>Maximum HyDE response tokens for hypothetical document generation.</summary>
    int HydeMaxTokens { get; }

    /// <summary>
    /// Maximum number of <c>ContextualCompressor</c> per-chunk LLM calls allowed
    /// to run concurrently. Each chunk requires one LLM round-trip to extract its
    /// relevant portion; without a cap, an N-chunk RAG turn issues N parallel
    /// requests which can saturate local-LLM concurrency. Default 4.
    /// </summary>
    int CompressionConcurrency { get; }

    // ═══════════════════════════════════════════════════════════════════
    //  HyDE Configuration
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Whether HyDE (Hypothetical Document Embeddings) is enabled in the RAG pipeline.
    /// When true and an IHydeService is registered, the pipeline runs HyDE on queries
    /// whose length meets <see cref="HydeMinQueryLength"/>.
    /// </summary>
    bool EnableHyde { get; }

    /// <summary>
    /// Minimum question length (in characters) before HyDE is invoked.
    /// HyDE is most useful on longer / abstract queries; short keyword queries
    /// already match the vector space well enough.
    /// </summary>
    int HydeMinQueryLength { get; }

    // ═══════════════════════════════════════════════════════════════════
    //  Search Routing
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Default search mode used by the RAG pipeline.
    /// Valid values: "Semantic", "Keyword", "Hybrid".
    /// </summary>
    string DefaultSearchMode { get; }

    // ═══════════════════════════════════════════════════════════════════
    //  Privacy / PII
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// When true, RAG context chunks are scanned for PII (emails, phone numbers,
    /// SSNs, credit cards, API keys, IP addresses) and redacted before being
    /// sent to the LLM provider.
    /// </summary>
    bool EnablePiiRedaction { get; }

    /// <summary>
    /// The mask string used when redacting PII (default "***").
    /// </summary>
    string PiiRedactionMask { get; }

    // ═══════════════════════════════════════════════════════════════════
    //  Research Mode Configuration
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Whether web search integration is enabled for Research Mode.</summary>
    bool EnableResearchMode { get; }

    /// <summary>Maximum number of web search results to retrieve in Research Mode.</summary>
    int ResearchMaxWebResults { get; }

    // ═══════════════════════════════════════════════════════════════════
    //  Validation
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Validates all configuration values and throws if invalid.</summary>
    void Validate();
}
