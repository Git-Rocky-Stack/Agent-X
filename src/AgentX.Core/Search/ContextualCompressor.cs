using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Configuration;
using AgentX.Core.Constants;
using Serilog;

namespace AgentX.Core.Search;

/// <summary>
/// Uses the local LLM to extract only the portions of each context chunk
/// that are directly relevant to answering the user's question. Irrelevant
/// background information is stripped, producing tighter context windows.
/// </summary>
public sealed class ContextualCompressor : IContextualCompressor
{
    private readonly IAiService _aiService;
    private readonly IRagConfiguration? _ragConfiguration;
    private readonly ILogger _logger;

    private const string SystemPrompt =
        """
        Extract ONLY the sentences from the given text that are directly relevant
        to answering the question. Return the extracted text verbatim — do not
        paraphrase, summarize, or add commentary. If no part of the text is
        relevant, respond with exactly "NOT_RELEVANT".
        """;

    public ContextualCompressor(IAiService aiService, ILogger logger)
        : this(aiService, null, logger)
    {
    }

    public ContextualCompressor(IAiService aiService, IRagConfiguration? ragConfiguration, ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _ragConfiguration = ragConfiguration;
        _logger = logger?.ForContext<ContextualCompressor>() ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<List<RagContextChunk>> CompressAsync(
        List<RagContextChunk> chunks,
        string query,
        CancellationToken ct = default)
    {
        if (chunks is null || chunks.Count == 0)
            return new List<RagContextChunk>();

        // P1-3: bounded parallelism. Concurrency cap defaults to 4 — enough to
        // hide LLM latency on cloud providers while not saturating local models
        // (which typically serve a small number of concurrent requests).
        var concurrency = Math.Max(1, _ragConfiguration?.CompressionConcurrency ?? 4);
        _logger.Debug(
            "Compressing {Count} chunks for query relevance (concurrency={Concurrency})",
            chunks.Count, concurrency);

        // Outcome[i] holds the compressed result (or null = drop) for chunks[i].
        // Pre-sized so we can write by index from concurrent tasks safely.
        var outcomes = new RagContextChunk?[chunks.Count];
        using var gate = new SemaphoreSlim(concurrency, concurrency);

        var tasks = new Task[chunks.Count];
        for (int idx = 0; idx < chunks.Count; idx++)
        {
            int i = idx; // capture by value
            var chunk = chunks[i];

            tasks[i] = Task.Run(async () =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    ct.ThrowIfCancellationRequested();

                    var messages = new List<ChatMessage>
                    {
                        new()
                        {
                            Role = "user",
                            Content = $"Question: {query}\n\nText:\n{chunk.ChunkText}"
                        }
                    };

                    var options = new ChatOptions
                    {
                        Temperature = 0.0, // Deterministic extraction
                        MaxTokens = AppConstants.CompressionMaxTokens,
                        // P1-1: the compressor system prompt is static across every chunk
                        // (and every call). Cacheable on Anthropic — at N chunks per RAG
                        // turn, this is the highest-leverage cache point in the pipeline.
                        CacheSystemPrompt = true
                    };

                    var extracted = await _aiService.ChatAsync(messages, SystemPrompt, options, ct)
                        .ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(extracted) ||
                        extracted.Contains("NOT_RELEVANT", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Debug("Chunk {ChunkId} filtered as not relevant", chunk.ChunkId);
                        outcomes[i] = null; // explicit drop
                        return;
                    }

                    outcomes[i] = new RagContextChunk
                    {
                        ChunkId = chunk.ChunkId,
                        DocumentId = chunk.DocumentId,
                        FileName = chunk.FileName,
                        FilePath = chunk.FilePath,
                        PageNumber = chunk.PageNumber,
                        ChunkIndex = chunk.ChunkIndex,
                        ChunkText = extracted.Trim(),
                        RelevanceScore = chunk.RelevanceScore
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to compress chunk {ChunkId}; keeping original", chunk.ChunkId);
                    outcomes[i] = chunk; // fallback: keep uncompressed
                }
                finally
                {
                    gate.Release();
                }
            }, ct);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Re-assemble in original chunk order (preserves rerank ordering).
        var compressed = new List<RagContextChunk>(chunks.Count);
        for (int i = 0; i < outcomes.Length; i++)
        {
            if (outcomes[i] is { } c) compressed.Add(c);
        }

        _logger.Debug("Compression complete: {Input} -> {Output} chunks", chunks.Count, compressed.Count);
        return compressed;
    }
}
