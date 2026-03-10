using AgentX.Core.AI;
using AgentX.Core.AI.Models;
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
    private readonly ILogger _logger;

    private const string SystemPrompt =
        """
        Extract ONLY the sentences from the given text that are directly relevant
        to answering the question. Return the extracted text verbatim — do not
        paraphrase, summarize, or add commentary. If no part of the text is
        relevant, respond with exactly "NOT_RELEVANT".
        """;

    public ContextualCompressor(IAiService aiService, ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
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

        _logger.Debug("Compressing {Count} chunks for query relevance", chunks.Count);

        var compressed = new List<RagContextChunk>(chunks.Count);

        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
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
                    MaxTokens = 512
                };

                var extracted = await _aiService.ChatAsync(messages, SystemPrompt, options, ct)
                    .ConfigureAwait(false);

                // Skip chunks that are entirely irrelevant
                if (string.IsNullOrWhiteSpace(extracted) ||
                    extracted.Contains("NOT_RELEVANT", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Debug("Chunk {ChunkId} filtered as not relevant", chunk.ChunkId);
                    continue;
                }

                compressed.Add(new RagContextChunk
                {
                    ChunkId = chunk.ChunkId,
                    DocumentId = chunk.DocumentId,
                    FileName = chunk.FileName,
                    FilePath = chunk.FilePath,
                    PageNumber = chunk.PageNumber,
                    ChunkIndex = chunk.ChunkIndex,
                    ChunkText = extracted.Trim(),
                    RelevanceScore = chunk.RelevanceScore
                });
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to compress chunk {ChunkId}; keeping original", chunk.ChunkId);
                compressed.Add(chunk); // Fall back to uncompressed
            }
        }

        _logger.Debug("Compression complete: {Input} -> {Output} chunks", chunks.Count, compressed.Count);
        return compressed;
    }
}
