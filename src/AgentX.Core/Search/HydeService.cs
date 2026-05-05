using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Configuration;
using AgentX.Core.Constants;
using Serilog;

namespace AgentX.Core.Search;

/// <summary>
/// HyDE (Hypothetical Document Embeddings) implementation.
/// Uses the LLM to generate a plausible answer passage for the user's question,
/// then embeds that passage. The resulting vector is closer in semantic space to
/// actual answer documents than the raw question embedding would be.
/// </summary>
public sealed class HydeService : IHydeService
{
    private readonly IAiService _aiService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IRagPromptCatalog? _promptCatalog;
    private readonly ILogger _logger;

    /// <summary>
    /// P2-4: returns the active HyDE system prompt — catalog when registered,
    /// compile-time default otherwise.
    /// </summary>
    private string SystemPrompt
        => _promptCatalog?.HydeSystem ?? RagPromptDefaults.HydeSystem;

    public HydeService(IAiService aiService, IEmbeddingService embeddingService, ILogger logger)
        : this(aiService, embeddingService, null, logger)
    {
    }

    public HydeService(
        IAiService aiService,
        IEmbeddingService embeddingService,
        IRagPromptCatalog? promptCatalog,
        ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _promptCatalog = promptCatalog;
        _logger = logger?.ForContext<HydeService>() ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> GenerateHypotheticalDocumentAsync(
        string query,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty.", nameof(query));

        _logger.Debug("Generating hypothetical document for: {Query}",
            query.Length > 80 ? query[..80] + "..." : query);

        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = query }
        };

        var options = new ChatOptions
        {
            Temperature = 0.3, // Low temperature for factual content
            MaxTokens = AppConstants.HydeMaxTokens,
            // P1-1: the HyDE system prompt is identical across every call; cache it
            // when the provider supports prompt caching (Anthropic).
            CacheSystemPrompt = true
        };

        var hypotheticalDoc = await _aiService.ChatAsync(messages, SystemPrompt, options, ct)
            .ConfigureAwait(false);

        _logger.Debug("Generated hypothetical document ({Length} chars)", hypotheticalDoc.Length);

        return hypotheticalDoc;
    }

    /// <inheritdoc />
    public async Task<float[]> GenerateHypotheticalEmbeddingAsync(
        string query,
        CancellationToken ct = default)
    {
        var hypotheticalDoc = await GenerateHypotheticalDocumentAsync(query, ct).ConfigureAwait(false);

        var embedding = await _embeddingService.EmbedAsync(hypotheticalDoc, ct)
            .ConfigureAwait(false);

        return embedding;
    }
}
