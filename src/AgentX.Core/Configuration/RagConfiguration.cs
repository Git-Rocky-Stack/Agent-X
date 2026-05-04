using Microsoft.Extensions.Options;

namespace AgentX.Core.Configuration;

/// <summary>
/// Configuration section bindings for RAG parameters from appsettings.json.
/// All values are bound from the "Rag" configuration section.
/// </summary>
public sealed class RagConfigurationOptions
{
    // ═══════════════════════════════════════════════════════════════════
    //  Search Configuration
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Default number of results to retrieve from vector search.</summary>
    public int DefaultTopK { get; set; } = 8;

    /// <summary>Minimum cosine similarity threshold (0.0-1.0).</summary>
    public float DefaultMinScore { get; set; } = 0.25f;

    /// <summary>Maximum TopK allowed for any search request.</summary>
    public int MaxTopK { get; set; } = 50;

    /// <summary>Multiplier for retrieving extra results before filtering.</summary>
    public int RetrievalMultiplier { get; set; } = 3;

    // ═══════════════════════════════════════════════════════════════════
    //  Chunking Configuration
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Default target chunk size in tokens.</summary>
    public int DefaultChunkSize { get; set; } = 512;

    /// <summary>Default token overlap between chunks.</summary>
    public int DefaultChunkOverlap { get; set; } = 50;

    /// <summary>Maximum allowed chunk size.</summary>
    public int MaxChunkSize { get; set; } = 768;

    /// <summary>Minimum allowed chunk size.</summary>
    public int MinChunkSize { get; set; } = 128;

    // ═══════════════════════════════════════════════════════════════════
    //  Embedding Configuration
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Default embedding model name.</summary>
    public string DefaultEmbeddingModel { get; set; } = "all-minilm";

    /// <summary>Expected embedding dimensions.</summary>
    public int DefaultEmbeddingDimensions { get; set; } = 384;

    /// <summary>Cache expiration in minutes (default: 7 days).</summary>
    public int EmbeddingCacheExpirationMinutes { get; set; } = 10080;

    /// <summary>Maximum batch size for embedding generation.</summary>
    public int EmbeddingBatchSize { get; set; } = 32;

    // ═══════════════════════════════════════════════════════════════════
    //  Context Assembly Configuration
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Weight for semantic similarity (0.0-1.0).</summary>
    public double SemanticWeight { get; set; } = 0.68;

    /// <summary>Weight for lexical overlap (0.0-1.0).</summary>
    public double LexicalWeight { get; set; } = 0.22;

    /// <summary>Weight for recency (0.0-1.0).</summary>
    public double RecencyWeight { get; set; } = 0.10;

    /// <summary>Minimum tokens for recall augmentation.</summary>
    public int MinRecallBudgetTokens { get; set; } = 48;

    // ═══════════════════════════════════════════════════════════════════
    //  Semantic Memory Configuration
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Default temporal decay rate.</summary>
    public double MemoryDecayRate { get; set; } = 0.01;

    /// <summary>Days before full decay.</summary>
    public int MemoryDaysBeforeFullDecay { get; set; } = 90;

    /// <summary>Threshold for associative memory links.</summary>
    public float AssociativeLinkThreshold { get; set; } = 0.85f;

    /// <summary>Maximum memories per query.</summary>
    public int MaxMemoriesPerQuery { get; set; } = 10;

    // ═══════════════════════════════════════════════════════════════════
    //  Vector Store Configuration
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Count threshold for linear scan fallback.</summary>
    public int VectorStoreFallbackThreshold { get; set; } = 10000;

    /// <summary>Stale fraction triggering rebuild.</summary>
    public double StaleRebuildFraction { get; set; } = 0.05;

    /// <summary>HNSW M parameter.</summary>
    public int HnswM { get; set; } = 16;

    /// <summary>HNSW EfConstruction parameter.</summary>
    public int HnswEfConstruction { get; set; } = 200;

    // ═══════════════════════════════════════════════════════════════════
    //  Reranking Configuration
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Enable LLM reranking.</summary>
    public bool EnableLlmReranking { get; set; } = true;

    /// <summary>Max tokens for reranking prompt.</summary>
    public int RerankerMaxTokens { get; set; } = 800;

    /// <summary>Max HyDE response tokens.</summary>
    public int HydeMaxTokens { get; set; } = 256;

    /// <summary>Max parallel ContextualCompressor LLM calls per RAG turn.</summary>
    public int CompressionConcurrency { get; set; } = 4;

    // ═══════════════════════════════════════════════════════════════════
    //  HyDE Configuration
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Whether HyDE is enabled in the RAG pipeline.</summary>
    public bool EnableHyde { get; set; } = true;

    /// <summary>Minimum question length (chars) before HyDE is invoked.</summary>
    public int HydeMinQueryLength { get; set; } = 80;

    // ═══════════════════════════════════════════════════════════════════
    //  Search Routing
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Default search mode: "Semantic", "Keyword", or "Hybrid".</summary>
    public string DefaultSearchMode { get; set; } = "Hybrid";

    // ═══════════════════════════════════════════════════════════════════
    //  Privacy / PII
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Whether to redact PII from RAG context before sending to LLM.</summary>
    public bool EnablePiiRedaction { get; set; } = true;

    /// <summary>Mask string used when redacting PII.</summary>
    public string PiiRedactionMask { get; set; } = "***";

    // ═══════════════════════════════════════════════════════════════════
    //  Research Mode Configuration
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Enable web search research mode.</summary>
    public bool EnableResearchMode { get; set; } = false;

    /// <summary>Max web search results.</summary>
    public int ResearchMaxWebResults { get; set; } = 10;
}

/// <summary>
/// Thread-safe implementation of IRagConfiguration backed by IOptionsMonitor.
/// Supports runtime configuration updates without application restart.
/// </summary>
public sealed class RagConfiguration : IRagConfiguration
{
    private readonly RagConfigurationOptions _options;
    private readonly object _lock = new();

    public RagConfiguration(IOptionsMonitor<RagConfigurationOptions> optionsMonitor)
    {
        if (optionsMonitor is null)
            throw new ArgumentNullException(nameof(optionsMonitor));

        _options = optionsMonitor.CurrentValue;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  IRagConfiguration Implementation
    // ═══════════════════════════════════════════════════════════════════

    public int DefaultTopK => _options.DefaultTopK;
    public float DefaultMinScore => _options.DefaultMinScore;
    public int MaxTopK => _options.MaxTopK;
    public int RetrievalMultiplier => _options.RetrievalMultiplier;

    public int DefaultChunkSize => _options.DefaultChunkSize;
    public int DefaultChunkOverlap => _options.DefaultChunkOverlap;
    public int MaxChunkSize => _options.MaxChunkSize;
    public int MinChunkSize => _options.MinChunkSize;

    public string DefaultEmbeddingModel => _options.DefaultEmbeddingModel;
    public int DefaultEmbeddingDimensions => _options.DefaultEmbeddingDimensions;
    public int EmbeddingCacheExpirationMinutes => _options.EmbeddingCacheExpirationMinutes;
    public int EmbeddingBatchSize => _options.EmbeddingBatchSize;

    public double SemanticWeight => _options.SemanticWeight;
    public double LexicalWeight => _options.LexicalWeight;
    public double RecencyWeight => _options.RecencyWeight;
    public int MinRecallBudgetTokens => _options.MinRecallBudgetTokens;

    public double MemoryDecayRate => _options.MemoryDecayRate;
    public int MemoryDaysBeforeFullDecay => _options.MemoryDaysBeforeFullDecay;
    public float AssociativeLinkThreshold => _options.AssociativeLinkThreshold;
    public int MaxMemoriesPerQuery => _options.MaxMemoriesPerQuery;

    public int VectorStoreFallbackThreshold => _options.VectorStoreFallbackThreshold;
    public double StaleRebuildFraction => _options.StaleRebuildFraction;
    public int HnswM => _options.HnswM;
    public int HnswEfConstruction => _options.HnswEfConstruction;

    public bool EnableLlmReranking => _options.EnableLlmReranking;
    public int RerankerMaxTokens => _options.RerankerMaxTokens;
    public int HydeMaxTokens => _options.HydeMaxTokens;
    public int CompressionConcurrency => _options.CompressionConcurrency;

    public bool EnableHyde => _options.EnableHyde;
    public int HydeMinQueryLength => _options.HydeMinQueryLength;

    public string DefaultSearchMode => _options.DefaultSearchMode;

    public bool EnablePiiRedaction => _options.EnablePiiRedaction;
    public string PiiRedactionMask => _options.PiiRedactionMask;

    public bool EnableResearchMode => _options.EnableResearchMode;
    public int ResearchMaxWebResults => _options.ResearchMaxWebResults;

    // ═══════════════════════════════════════════════════════════════════
    //  Validation
    // ═══════════════════════════════════════════════════════════════════

    public void Validate()
    {
        var errors = new List<string>();

        // Validate search parameters
        if (DefaultTopK <= 0)
            errors.Add("DefaultTopK must be positive.");
        if (DefaultTopK > MaxTopK)
            errors.Add($"DefaultTopK ({DefaultTopK}) cannot exceed MaxTopK ({MaxTopK}).");
        if (DefaultMinScore < 0 || DefaultMinScore > 1)
            errors.Add("DefaultMinScore must be between 0 and 1.");
        if (RetrievalMultiplier < 1)
            errors.Add("RetrievalMultiplier must be at least 1.");

        // Validate chunking parameters
        if (MinChunkSize <= 0)
            errors.Add("MinChunkSize must be positive.");
        if (MaxChunkSize < MinChunkSize)
            errors.Add($"MaxChunkSize ({MaxChunkSize}) must be >= MinChunkSize ({MinChunkSize}).");
        if (DefaultChunkSize < MinChunkSize || DefaultChunkSize > MaxChunkSize)
            errors.Add($"DefaultChunkSize ({DefaultChunkSize}) must be between MinChunkSize and MaxChunkSize.");
        if (DefaultChunkOverlap >= DefaultChunkSize)
            errors.Add("DefaultChunkOverlap must be less than DefaultChunkSize.");

        // Validate embedding parameters
        if (string.IsNullOrWhiteSpace(DefaultEmbeddingModel))
            errors.Add("DefaultEmbeddingModel cannot be empty.");
        if (DefaultEmbeddingDimensions <= 0)
            errors.Add("DefaultEmbeddingDimensions must be positive.");
        if (EmbeddingCacheExpirationMinutes < 0)
            errors.Add("EmbeddingCacheExpirationMinutes cannot be negative.");
        if (EmbeddingBatchSize <= 0)
            errors.Add("EmbeddingBatchSize must be positive.");

        // Validate context assembly weights
        var totalWeight = SemanticWeight + LexicalWeight + RecencyWeight;
        if (totalWeight < 0.9 || totalWeight > 1.1)
            errors.Add($"Context weights sum to {totalWeight:F2}; should be close to 1.0.");
        if (SemanticWeight < 0 || SemanticWeight > 1)
            errors.Add("SemanticWeight must be between 0 and 1.");
        if (LexicalWeight < 0 || LexicalWeight > 1)
            errors.Add("LexicalWeight must be between 0 and 1.");
        if (RecencyWeight < 0 || RecencyWeight > 1)
            errors.Add("RecencyWeight must be between 0 and 1.");

        // Validate memory parameters
        if (MemoryDecayRate < 0 || MemoryDecayRate > 1)
            errors.Add("MemoryDecayRate must be between 0 and 1.");
        if (MemoryDaysBeforeFullDecay <= 0)
            errors.Add("MemoryDaysBeforeFullDecay must be positive.");
        if (AssociativeLinkThreshold < 0 || AssociativeLinkThreshold > 1)
            errors.Add("AssociativeLinkThreshold must be between 0 and 1.");
        if (MaxMemoriesPerQuery <= 0)
            errors.Add("MaxMemoriesPerQuery must be positive.");

        // Validate vector store parameters
        if (VectorStoreFallbackThreshold < 0)
            errors.Add("VectorStoreFallbackThreshold cannot be negative.");
        if (StaleRebuildFraction < 0 || StaleRebuildFraction > 1)
            errors.Add("StaleRebuildFraction must be between 0 and 1.");
        if (HnswM <= 0)
            errors.Add("HnswM must be positive.");
        if (HnswEfConstruction <= 0)
            errors.Add("HnswEfConstruction must be positive.");

        // Validate reranking parameters
        if (RerankerMaxTokens <= 0)
            errors.Add("RerankerMaxTokens must be positive.");
        if (HydeMaxTokens <= 0)
            errors.Add("HydeMaxTokens must be positive.");
        if (CompressionConcurrency < 1)
            errors.Add("CompressionConcurrency must be at least 1.");

        // Validate HyDE parameters
        if (HydeMinQueryLength < 0)
            errors.Add("HydeMinQueryLength cannot be negative.");

        // Validate search mode
        if (string.IsNullOrWhiteSpace(DefaultSearchMode))
            errors.Add("DefaultSearchMode cannot be empty.");
        else if (!string.Equals(DefaultSearchMode, "Semantic", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(DefaultSearchMode, "Keyword", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(DefaultSearchMode, "Hybrid", StringComparison.OrdinalIgnoreCase))
            errors.Add($"DefaultSearchMode '{DefaultSearchMode}' is invalid. Use 'Semantic', 'Keyword', or 'Hybrid'.");

        // Validate PII parameters
        if (EnablePiiRedaction && string.IsNullOrEmpty(PiiRedactionMask))
            errors.Add("PiiRedactionMask cannot be empty when EnablePiiRedaction is true.");

        // Validate research mode parameters
        if (ResearchMaxWebResults <= 0)
            errors.Add("ResearchMaxWebResults must be positive.");

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"RAG configuration validation failed:{Environment.NewLine}" +
                string.Join(Environment.NewLine, errors.Select((e, i) => $"  {i + 1}. {e}")));
        }
    }
}
