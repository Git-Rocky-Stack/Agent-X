using System.Text.Json;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Constants;
using AgentX.Core.Observability;
using Serilog;

namespace AgentX.Core.Search;

/// <summary>
/// Uses the local LLM as a cross-encoder to score query-document relevance.
/// Processes chunks in a single batch prompt asking the model to rank them,
/// producing more accurate relevance ordering than embedding similarity alone.
/// </summary>
public sealed class LlmReranker : ILlmReranker
{
    private readonly IAiService _aiService;
    private readonly ILogger _logger;

    private const string SystemPrompt =
        """
        You are a relevance scoring assistant. For each numbered passage below, rate how
        relevant it is to answering the given question on a scale of 0-10 where:
        0 = completely irrelevant, 10 = directly answers the question.

        Return ONLY a JSON array of objects with "id" (the passage number) and "score" (0-10).
        Example: [{"id":1,"score":8},{"id":2,"score":3}]
        """;

    public LlmReranker(IAiService aiService, ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _logger = logger?.ForContext<LlmReranker>() ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<List<RagContextChunk>> RerankAsync(
        List<RagContextChunk> chunks,
        string query,
        int maxChunks = 8,
        CancellationToken ct = default)
    {
        if (chunks is null || chunks.Count == 0)
            return new List<RagContextChunk>();

        if (chunks.Count <= 2)
            return chunks; // Not enough to meaningfully rerank

        _logger.Debug("LLM reranking {Count} chunks", chunks.Count);

        try
        {
            // Build the scoring prompt with all passages
            var passagesText = string.Join("\n\n", chunks.Select((c, i) =>
                $"[Passage {i + 1}]\n{Truncate(c.ChunkText, 300)}"));

            var messages = new List<ChatMessage>
            {
                new()
                {
                    Role = "user",
                    Content = $"Question: {query}\n\nPassages:\n{passagesText}"
                }
            };

            var options = new ChatOptions
            {
                Temperature = 0.0,
                MaxTokens = AppConstants.RerankerMaxTokens,
                ResponseFormat = ResponseFormat.JsonObject,
                // P1-1: the reranker system prompt is identical across every call; cache it
                // when the provider supports prompt caching (Anthropic).
                CacheSystemPrompt = true
            };

            var response = await _aiService.ChatAsync(messages, SystemPrompt, options, ct)
                .ConfigureAwait(false);

            // Parse the JSON scores
            var scores = ParseScores(response, chunks.Count);

            if (scores.Count > 0)
            {
                // Apply LLM scores to chunks
                var scored = chunks.Select((chunk, i) =>
                {
                    var llmScore = scores.TryGetValue(i + 1, out var s) ? s : 5.0;
                    var combinedScore = chunk.RelevanceScore * 0.4 + (llmScore / 10.0) * 0.6;
                    return (Chunk: chunk, Score: combinedScore);
                })
                .OrderByDescending(x => x.Score)
                .Take(maxChunks)
                .Select(x => new RagContextChunk
                {
                    ChunkId = x.Chunk.ChunkId,
                    DocumentId = x.Chunk.DocumentId,
                    FileName = x.Chunk.FileName,
                    FilePath = x.Chunk.FilePath,
                    PageNumber = x.Chunk.PageNumber,
                    ChunkIndex = x.Chunk.ChunkIndex,
                    ChunkText = x.Chunk.ChunkText,
                    RelevanceScore = (float)x.Score
                })
                .ToList();

                _logger.Debug("LLM reranking complete: {Count} chunks returned", scored.Count);
                return scored;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "LLM reranking failed; returning chunks in original order");
        }

        return chunks.Take(maxChunks).ToList();
    }

    private Dictionary<int, double> ParseScores(string response, int chunkCount)
    {
        var scores = new Dictionary<int, double>();

        try
        {
            var start = response.IndexOf('[');
            var end = response.LastIndexOf(']');

            if (start >= 0 && end > start)
            {
                var json = response[start..(end + 1)];
                var parsed = JsonSerializer.Deserialize<List<ScoreEntry>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed is not null)
                {
                    foreach (var entry in parsed)
                    {
                        if (entry.Id >= 1 && entry.Id <= chunkCount)
                        {
                            scores[entry.Id] = Math.Clamp(entry.Score, 0, 10);
                        }
                    }

                    return scores;
                }
            }

            // No JSON array found — emit a redacted summary (P2-10) so operators
            // can correlate failures by hash without dumping passages the model
            // may have echoed back in its response.
            _logger.Warning(
                "LLM reranker response contained no JSON array; falling back to no rerank scores. Response summary: {Summary}",
                LogRedaction.ForLog(response));
        }
        catch (Exception ex)
        {
            _logger.Warning(ex,
                "Failed to parse LLM reranker JSON; falling back to no rerank scores. Response summary: {Summary}",
                LogRedaction.ForLog(response));
        }

        return scores;
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    private sealed class ScoreEntry
    {
        public int Id { get; set; }
        public double Score { get; set; }
    }
}
