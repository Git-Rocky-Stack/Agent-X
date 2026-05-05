using System.Text.Json.Serialization;
using System.Text.Json;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Configuration;
using AgentX.Core.Observability;
using Serilog;

namespace AgentX.Core.Search;

/// <summary>
/// Uses the local LLM as a judge to evaluate RAG response quality across
/// three dimensions: context relevance, faithfulness, and answer relevance.
/// Returns normalized scores (0.0 to 1.0) for each dimension.
/// </summary>
public sealed class RagEvaluator : IRagEvaluator
{
    private readonly IAiService _aiService;
    private readonly IRagConfiguration? _ragConfiguration;
    private readonly ILogger _logger;

    // P2-5: fallback when no IRagConfiguration is registered. Older default
    // was 200 — too aggressive; the judge couldn't see beyond char 200 and
    // returned spurious low context_relevance scores on long chunks.
    private const int FallbackEvalContextCharLimit = 800;

    private const string EvalSystemPrompt =
        """
        You are an impartial quality evaluator for a question-answering system.
        Given a question, retrieved context passages, and a generated answer,
        evaluate on three dimensions:

        1. context_relevance (0-10): How relevant are the retrieved passages to the question?
        2. faithfulness (0-10): Is the answer grounded in the context? Does it avoid making claims not in the passages?
        3. answer_relevance (0-10): How well does the answer address the original question?

        Return ONLY a JSON object: {"context_relevance":N,"faithfulness":N,"answer_relevance":N}
        """;

    // FU-5: provider-side schema enforcement (OpenAI strict json_schema).
    // OpenAI rejects responses that miss required fields, exceed declared
    // ranges, or include extra keys — eliminating a class of parse failures
    // that previously fell through to placeholder defaults.
    private const string EvalJsonSchema =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["context_relevance", "faithfulness", "answer_relevance"],
          "properties": {
            "context_relevance": { "type": "number", "minimum": 0, "maximum": 10 },
            "faithfulness":      { "type": "number", "minimum": 0, "maximum": 10 },
            "answer_relevance":  { "type": "number", "minimum": 0, "maximum": 10 }
          }
        }
        """;

    public RagEvaluator(IAiService aiService, ILogger logger)
        : this(aiService, null, logger)
    {
    }

    public RagEvaluator(IAiService aiService, IRagConfiguration? ragConfiguration, ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _ragConfiguration = ragConfiguration;
        _logger = logger?.ForContext<RagEvaluator>() ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<RagEvalMetrics> EvaluateAsync(
        string question,
        string answer,
        IReadOnlyList<RagContextChunk> contextChunks,
        CancellationToken ct = default)
    {
        // Input validation — return marked-default metrics so aggregators don't
        // pollute their averages with placeholder scores.
        if (string.IsNullOrWhiteSpace(question)
            || string.IsNullOrWhiteSpace(answer)
            || contextChunks is null
            || contextChunks.Count == 0)
        {
            return DefaultMetrics("InputValidation");
        }

        _logger.Debug("Evaluating RAG response quality for question: {Question}",
            question.Length > 80 ? question[..80] + "..." : question);

        string response;
        try
        {
            int charLimit = Math.Max(1, _ragConfiguration?.EvalContextCharLimit ?? FallbackEvalContextCharLimit);
            var contextText = string.Join("\n---\n",
                contextChunks.Select((c, i) => $"[{i + 1}] {Truncate(c.ChunkText, charLimit)}"));

            var messages = new List<ChatMessage>
            {
                new()
                {
                    Role = "user",
                    Content = $"""
                        Question: {question}

                        Retrieved Context:
                        {contextText}

                        Generated Answer:
                        {answer}
                        """
                }
            };

            var options = new ChatOptions
            {
                Temperature = 0.0,
                // 256 tokens is a safe floor for the 3-key JSON output. Local LLMs frequently
                // add a 1-2 sentence preamble or trailing whitespace; 128 was below the floor
                // and caused silent truncation → JSON parse failure → default scores.
                MaxTokens = 256,
                ResponseFormat = ResponseFormat.JsonObject,
                // FU-5: strict provider-side schema validation on OpenAI. Other
                // providers fall back to plain JSON-object mode and rely on the
                // post-deserialize parser below.
                JsonSchema = EvalJsonSchema,
                JsonSchemaName = "rag_eval_metrics",
                // P1-1: the eval system prompt is identical across every call; cache it
                // when the provider supports prompt caching (Anthropic).
                CacheSystemPrompt = true
            };

            response = await _aiService.ChatAsync(messages, EvalSystemPrompt, options, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is a caller-driven signal, not a quality event. Surface it
            // so the caller can propagate / abort instead of swallowing it as a 0.5 score.
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "RAG evaluation LLM call failed; returning default metrics");
            return DefaultMetrics("LlmCallFailure");
        }

        return ParseMetrics(response);
    }

    private RagEvalMetrics ParseMetrics(string response)
    {
        try
        {
            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');

            if (start >= 0 && end > start)
            {
                var json = response[start..(end + 1)];
                var parsed = JsonSerializer.Deserialize<EvalScores>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed is not null)
                {
                    return new RagEvalMetrics
                    {
                        ContextRelevance = Math.Clamp(parsed.ContextRelevance / 10.0, 0, 1),
                        Faithfulness = Math.Clamp(parsed.Faithfulness / 10.0, 0, 1),
                        AnswerRelevance = Math.Clamp(parsed.AnswerRelevance / 10.0, 0, 1),
                        IsDefault = false
                    };
                }
            }

            // Reached here = no JSON braces found in the response. Surface a redacted
            // summary (P2-10) so operators can group failures without exposing chunk
            // PII the model may have echoed back in its response.
            _logger.Warning(
                "RAG eval response contained no JSON object; using defaults. Response summary: {Summary}",
                LogRedaction.ForLog(response));
        }
        catch (Exception ex)
        {
            _logger.Warning(ex,
                "Failed to parse eval JSON; using defaults. Response summary: {Summary}",
                LogRedaction.ForLog(response));
        }

        return DefaultMetrics("JsonParseFailure");
    }

    private static RagEvalMetrics DefaultMetrics(string reason) => new()
    {
        ContextRelevance = 0.5,
        Faithfulness = 0.5,
        AnswerRelevance = 0.5,
        IsDefault = true,
        DefaultReason = reason
    };

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + "...";

    private sealed class EvalScores
    {
        [JsonPropertyName("context_relevance")]
        public double ContextRelevance { get; set; }

        [JsonPropertyName("faithfulness")]
        public double Faithfulness { get; set; }

        [JsonPropertyName("answer_relevance")]
        public double AnswerRelevance { get; set; }
    }
}
