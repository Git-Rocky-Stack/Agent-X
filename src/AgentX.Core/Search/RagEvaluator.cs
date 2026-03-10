using System.Text.Json;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
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
    private readonly ILogger _logger;

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

    public RagEvaluator(IAiService aiService, ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _logger = logger?.ForContext<RagEvaluator>() ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<RagEvalMetrics> EvaluateAsync(
        string question,
        string answer,
        IReadOnlyList<RagContextChunk> contextChunks,
        CancellationToken ct = default)
    {
        _logger.Debug("Evaluating RAG response quality for question: {Question}",
            question.Length > 80 ? question[..80] + "..." : question);

        try
        {
            var contextText = string.Join("\n---\n",
                contextChunks.Select((c, i) => $"[{i + 1}] {Truncate(c.ChunkText, 200)}"));

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
                MaxTokens = 128
            };

            var response = await _aiService.ChatAsync(messages, EvalSystemPrompt, options, ct)
                .ConfigureAwait(false);

            return ParseMetrics(response);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "RAG evaluation failed; returning default metrics");
            return new RagEvalMetrics
            {
                ContextRelevance = 0.5,
                Faithfulness = 0.5,
                AnswerRelevance = 0.5
            };
        }
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
                        AnswerRelevance = Math.Clamp(parsed.AnswerRelevance / 10.0, 0, 1)
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to parse eval JSON; using defaults");
        }

        return new RagEvalMetrics
        {
            ContextRelevance = 0.5,
            Faithfulness = 0.5,
            AnswerRelevance = 0.5
        };
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + "...";

    private sealed class EvalScores
    {
        public double ContextRelevance { get; set; }
        public double Faithfulness { get; set; }
        public double AnswerRelevance { get; set; }
    }
}
