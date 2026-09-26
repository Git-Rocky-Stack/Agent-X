using System.Text.Json;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Configuration;
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
    private readonly IRagPromptCatalog? _promptCatalog;
    private readonly IRagConfiguration? _ragConfiguration;
    private readonly ILogger _logger;

    /// <summary>
    /// P2-4: returns the active reranker system prompt — catalog when registered,
    /// compile-time default otherwise.
    /// </summary>
    private string SystemPrompt
        => _promptCatalog?.RerankerSystem ?? RagPromptDefaults.RerankerSystem;

    // FU-5: provider-side schema. Wrapped in an object because OpenAI's
    // strict json_schema mode requires the top-level type to be an object.
    private const string RerankerJsonSchema =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["scores"],
          "properties": {
            "scores": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["id", "score"],
                "properties": {
                  "id":    { "type": "integer", "minimum": 1 },
                  "score": { "type": "number",  "minimum": 0, "maximum": 10 }
                }
              }
            }
          }
        }
        """;

    public LlmReranker(IAiService aiService, ILogger logger)
        : this(aiService, null, logger)
    {
    }

    public LlmReranker(IAiService aiService, IRagPromptCatalog? promptCatalog, ILogger logger)
        : this(aiService, promptCatalog, null, logger)
    {
    }

    public LlmReranker(
        IAiService aiService,
        IRagPromptCatalog? promptCatalog,
        IRagConfiguration? ragConfiguration,
        ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _promptCatalog = promptCatalog;
        _ragConfiguration = ragConfiguration;
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
                // Rag:RerankerMaxTokens when configured; the constant is only the fallback for
                // hosts without IRagConfiguration.
                MaxTokens = _ragConfiguration is { RerankerMaxTokens: > 0 } config
                    ? config.RerankerMaxTokens
                    : AppConstants.RerankerMaxTokens,
                ResponseFormat = ResponseFormat.JsonObject,
                // FU-5: provider-side schema enforcement on OpenAI. Other providers
                // honor the broader ResponseFormat.JsonObject and rely on the
                // tolerant ParseScores below.
                JsonSchema = RerankerJsonSchema,
                JsonSchemaName = "rag_reranker_scores",
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
            // FU-5: response shape is now {"scores":[{"id":N,"score":N}, ...]}.
            // We tolerate both the new wrapped form AND the legacy bare-array form
            // for backwards compat with callers that may not have updated their
            // schema yet. Which form it is depends on which bracket opens the JSON: a bare
            // array also contains '{' (inside each entry), so looking for '{' first sliced
            // "{...},{...}" out of the array, failed to parse, and the array form was never
            // reached.
            var objectStart = response.IndexOf('{');
            var arrayStart = response.IndexOf('[');
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            List<ScoreEntry>? parsed = null;

            if (arrayStart >= 0 && (objectStart < 0 || arrayStart < objectStart))
            {
                // Legacy fallback: bare JSON array
                var arrEnd = response.LastIndexOf(']');
                if (arrEnd > arrayStart)
                {
                    var json = response[arrayStart..(arrEnd + 1)];
                    parsed = JsonSerializer.Deserialize<List<ScoreEntry>>(json, jsonOptions);
                }
            }
            else if (objectStart >= 0)
            {
                var end = response.LastIndexOf('}');
                if (end > objectStart)
                {
                    var json = response[objectStart..(end + 1)];
                    var wrapper = JsonSerializer.Deserialize<ScoresWrapper>(json, jsonOptions);
                    parsed = wrapper?.Scores;
                }
            }

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

            // No parseable JSON — emit a redacted summary (P2-10) so operators
            // can correlate failures by hash without dumping passages the model
            // may have echoed back in its response.
            _logger.Warning(
                "LLM reranker response contained no parseable scores object; falling back to no rerank scores. Response summary: {Summary}",
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

    /// <summary>
    /// FU-5: top-level wrapper for the schema-constrained reranker response
    /// (<c>{"scores":[...]}</c>). OpenAI's strict json_schema mode requires
    /// the root to be an object, which is why we wrap the array.
    /// </summary>
    private sealed class ScoresWrapper
    {
        public List<ScoreEntry>? Scores { get; set; }
    }
}
