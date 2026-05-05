using System.Text.Json;
using System.Text.Json.Serialization;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Configuration;
using AgentX.Core.Constants;
using AgentX.Core.Observability;
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
    private readonly IRagPromptCatalog? _promptCatalog;
    private readonly ILogger _logger;

    /// <summary>
    /// P2-4: returns the active compressor system prompt — catalog when
    /// registered, compile-time default otherwise.
    /// </summary>
    private string SystemPrompt
        => _promptCatalog?.CompressorSystem ?? RagPromptDefaults.CompressorSystem;

    // FU-5: provider-side schema. <c>extracted</c> is allowed to be null OR
    // a non-empty string (representing the verbatim relevant sentences); we
    // express that with a union type which OpenAI strict mode supports.
    private const string CompressorJsonSchema =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["relevant", "extracted"],
          "properties": {
            "relevant":  { "type": "boolean" },
            "extracted": { "type": ["string", "null"] }
          }
        }
        """;

    // P2-7: structured-output prompt content lives in RagPromptDefaults.CompressorSystem
    // (compile-time fallback) and RagPrompts.json (runtime override via catalog).
    // The previous version returned a free-text extraction or the literal string
    // "NOT_RELEVANT" — a brittle contract that false-positives any chunk that
    // happens to contain those characters. JSON mode + a typed schema makes
    // the contract explicit and lets the provider's native JSON enforcement do
    // the heavy lifting where available.

    public ContextualCompressor(IAiService aiService, ILogger logger)
        : this(aiService, null, null, logger)
    {
    }

    public ContextualCompressor(IAiService aiService, IRagConfiguration? ragConfiguration, ILogger logger)
        : this(aiService, ragConfiguration, null, logger)
    {
    }

    public ContextualCompressor(
        IAiService aiService,
        IRagConfiguration? ragConfiguration,
        IRagPromptCatalog? promptCatalog,
        ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _ragConfiguration = ragConfiguration;
        _promptCatalog = promptCatalog;
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
                        CacheSystemPrompt = true,
                        // P2-7: ask the provider for native JSON-object mode where it's
                        // available; on fall-through we still parse defensively below.
                        ResponseFormat = ResponseFormat.JsonObject,
                        // FU-5: strict provider-side schema enforcement on OpenAI.
                        JsonSchema = CompressorJsonSchema,
                        JsonSchemaName = "rag_compression_result"
                    };

                    var raw = await _aiService.ChatAsync(messages, SystemPrompt, options, ct)
                        .ConfigureAwait(false);

                    var parsed = TryParse(raw);
                    if (parsed is null)
                    {
                        // Parse failure is treated as a soft pass — keep the original
                        // chunk rather than dropping it. Logging is redacted (P2-10).
                        _logger.Warning(
                            "Compressor JSON parse failed for chunk {ChunkId}; keeping original. Response summary: {Summary}",
                            chunk.ChunkId, LogRedaction.ForLog(raw));
                        outcomes[i] = chunk;
                        return;
                    }

                    if (!parsed.Relevant)
                    {
                        _logger.Debug("Chunk {ChunkId} filtered as not relevant", chunk.ChunkId);
                        outcomes[i] = null; // explicit drop
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(parsed.Extracted))
                    {
                        // Model said "relevant=true" but didn't include extracted text —
                        // an invalid combination. Keep the original chunk to avoid silent
                        // information loss.
                        _logger.Debug(
                            "Chunk {ChunkId}: model returned relevant=true with no extracted text; keeping original",
                            chunk.ChunkId);
                        outcomes[i] = chunk;
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
                        ChunkText = parsed.Extracted.Trim(),
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

    /// <summary>
    /// P2-7: parses the structured-output JSON. Returns null on any parse failure
    /// — caller decides the failure mode (we currently soft-fail by keeping the
    /// original chunk). Tolerates the response containing surrounding prose by
    /// scanning for the first <c>{</c> through the last <c>}</c>.
    /// </summary>
    private static CompressionResult? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        var json = raw[start..(end + 1)];

        try
        {
            return JsonSerializer.Deserialize<CompressionResult>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// P2-7: wire schema for the structured response. <c>Extracted</c> is null
    /// when <c>Relevant</c> is false.
    /// </summary>
    private sealed class CompressionResult
    {
        [JsonPropertyName("relevant")]
        public bool Relevant { get; set; }

        [JsonPropertyName("extracted")]
        public string? Extracted { get; set; }
    }
}
