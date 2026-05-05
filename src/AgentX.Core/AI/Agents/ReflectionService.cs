using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentX.Core.AI.Models;
using AgentX.Core.Observability;
using Serilog;

namespace AgentX.Core.AI.Agents;

/// <summary>
/// Implementation of reflection service using AI to critique and refine responses.
/// </summary>
public sealed partial class ReflectionService : IReflectionService
{
    private readonly IAiService _aiService;
    private readonly ILogger _log;

    public ReflectionService(IAiService aiService, ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _log = logger?.ForContext<ReflectionService>() ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ReflectionResult> CritiqueResponseAsync(
        string query,
        string response,
        IReadOnlyList<string> context,
        CancellationToken ct = default)
    {
        _log.Debug("Starting critique of response");

        var critiquePrompt = BuildCritiquePrompt(query, response, context);

        try
        {
            var jsonResponse = await _aiService.ChatAsync(
                messages: new List<ChatMessage> { ChatMessage.User(critiquePrompt) },
                systemPrompt: "You are an impartial critic evaluating AI responses. Respond only with valid JSON.",
                options: new ChatOptions
                {
                    Temperature = 0.3,
                    MaxTokens = 1500,
                    ResponseFormat = ResponseFormat.JsonObject
                },
                ct: ct);

            var parsedCritique = ParseCritiqueJson(jsonResponse);

            var result = new ReflectionResult
            {
                QualityScore = parsedCritique.qualityScore,
                Critiques = parsedCritique.critiques
            };

            _log.Information("Critique complete: Quality={QualityScore}, Critiques={Count}",
                result.QualityScore, result.Critiques.Count);

            return result;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Critique generation failed, returning neutral result");
            return new ReflectionResult
            {
                QualityScore = 0.7, // Assume passing if critique fails
                Critiques = new List<ReflectionCritique>()
            };
        }
    }

    /// <inheritdoc />
    public async Task<string> RefineResponseAsync(
        string original,
        IReadOnlyList<ReflectionCritique> critiques,
        CancellationToken ct = default)
    {
        if (critiques.Count == 0)
            return original;

        _log.Debug("Refining response based on {Count} critiques", critiques.Count);

        var refinePrompt = BuildRefinePrompt(original, critiques);

        try
        {
            var refined = await _aiService.ChatAsync(
                messages: new List<ChatMessage> { ChatMessage.User(refinePrompt) },
                systemPrompt: "You are an expert editor improving AI responses based on feedback.",
                options: new ChatOptions { Temperature = 0.5, MaxTokens = 3000 },
                ct: ct);

            _log.Information("Response refined successfully");
            return refined;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Response refinement failed, returning original");
            return original;
        }
    }

    /// <inheritdoc />
    public async Task<string> ReflectAndRefineAsync(
        string query,
        string response,
        IReadOnlyList<string> context,
        CancellationToken ct = default)
    {
        var critique = await CritiqueResponseAsync(query, response, context, ct);

        if (critique.IsPassing)
        {
            _log.Debug("Response passes quality threshold, skipping refinement");
            return response;
        }

        return await RefineResponseAsync(response, critique.Critiques, ct);
    }

    /// <summary>
    /// Builds a prompt for response critique.
    /// </summary>
    private static string BuildCritiquePrompt(string query, string response, IReadOnlyList<string> context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Evaluate the following AI response based on the original query and provided context.");
        sb.AppendLine();
        sb.AppendLine("## Query");
        sb.AppendLine(query);
        sb.AppendLine();

        if (context.Count > 0)
        {
            sb.AppendLine("## Context");
            foreach (var ctx in context.Take(5))
            {
                sb.AppendLine($"- {ctx.Truncate(200)}...");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Response to Critique");
        sb.AppendLine(response);
        sb.AppendLine();

        sb.AppendLine("## Instructions");
        sb.AppendLine("Respond with a JSON object in this exact format:");
        sb.AppendLine();
        sb.AppendLine("{");
        sb.AppendLine("  \"qualityScore\": <number 0-1>,");
        sb.AppendLine("  \"critiques\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"aspect\": <\"Accuracy\"|\"Relevance\"|\"Clarity\"|\"Completeness\"|\"Citation\"|\"Tone\"|\"Factuality\">,");
        sb.AppendLine("      \"severity\": <\"Low\"|\"Medium\"|\"High\">,");
        sb.AppendLine("      \"description\": <what the issue is>,");
        sb.AppendLine("      \"suggestion\": <how to fix it>");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Evaluate for: accuracy, relevance to query, clarity, completeness, proper citations, factual correctness, and grounding in the provided context.");

        return sb.ToString();
    }

    /// <summary>
    /// Builds a prompt for response refinement.
    /// </summary>
    private static string BuildRefinePrompt(string original, IReadOnlyList<ReflectionCritique> critiques)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Improve the following response based on the provided feedback.");
        sb.AppendLine();
        sb.AppendLine("## Original Response");
        sb.AppendLine(original);
        sb.AppendLine();
        sb.AppendLine("## Feedback to Address");

        foreach (var critique in critiques.OrderByDescending(c => c.Severity))
        {
            sb.AppendLine($"- [{critique.Severity}] {critique.Aspect}: {critique.Description}");
            if (!string.IsNullOrEmpty(critique.Suggestion))
            {
                sb.AppendLine($"  Suggestion: {critique.Suggestion}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Instructions");
        sb.AppendLine("Rewrite the response to address all the feedback above.");
        sb.AppendLine("Maintain the core message and tone while improving based on suggestions.");
        sb.AppendLine("Ensure citations are properly formatted and claims are grounded in the context.");

        return sb.ToString();
    }

    /// <summary>
    /// Parses critique JSON response into structured format.
    /// </summary>
    private (double qualityScore, List<ReflectionCritique> critiques) ParseCritiqueJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var qualityScore = root.TryGetProperty("qualityScore", out var qs)
                ? qs.GetDouble()
                : 0.7;

            var critiques = new List<ReflectionCritique>();

            if (root.TryGetProperty("critiques", out var critiquesArray))
            {
                foreach (var item in critiquesArray.EnumerateArray())
                {
                    var critique = new ReflectionCritique
                    {
                        Aspect = item.TryGetProperty("aspect", out var aspect)
                            ? Enum.Parse<CritiqueAspect>(aspect.GetString() ?? "Accuracy", true)
                            : CritiqueAspect.Accuracy,
                        Severity = item.TryGetProperty("severity", out var severity)
                            ? Enum.Parse<CritiqueSeverity>(severity.GetString() ?? "Low", true)
                            : CritiqueSeverity.Low,
                        Description = item.TryGetProperty("description", out var desc)
                            ? desc.GetString() ?? string.Empty
                            : string.Empty,
                        Suggestion = item.TryGetProperty("suggestion", out var sugg)
                            ? sugg.GetString() ?? string.Empty
                            : string.Empty
                    };

                    if (!string.IsNullOrEmpty(critique.Description))
                    {
                        critiques.Add(critique);
                    }
                }
            }

            return (qualityScore, critiques);
        }
        catch (Exception ex)
        {
            // Emit a redacted summary (P2-10) so operators can correlate failures
            // by hash without dumping the entire response — which may echo prompt
            // content the upstream pipeline already redacted.
            _log.Warning(ex,
                "Failed to parse reflection critique JSON; defaulting to qualityScore=0.7 and no critiques. Response summary: {Summary}",
                LogRedaction.ForLog(json));
            return (0.7, new List<ReflectionCritique>());
        }
    }
}

internal static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;
        return value.Substring(0, maxLength);
    }
}
