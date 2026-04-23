using System.Text;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Services.Intelligence.Models;
using Serilog;

namespace AgentX.Core.Services.Intelligence;

public sealed class DocumentSynthesisService : IDocumentSynthesisService
{
    private readonly IAiService _aiService;
    private readonly ILogger _logger;

    private static readonly ChatOptions AnalysisChatOptions = new()
    {
        Temperature = 0.2,
        MaxTokens = 4096
    };

    private const int CharsPerToken = 4;

    public DocumentSynthesisService(IAiService aiService, ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _logger = logger?.ForContext<DocumentSynthesisService>()
                  ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ComparisonSynthesisResult> SynthesizeComparisonAsync(
        ComparisonSynthesisRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var systemPrompt = BuildSystemPrompt(request.Options);
        var userPrompt = BuildUserPrompt(request.ContentByDocument, request.Options);
        var estimatedPromptTokens = EstimateTokens(systemPrompt + userPrompt);

        _logger.Debug(
            "Synthesizing comparison for {Count} documents (~{PromptTokens} prompt tokens)",
            request.ContentByDocument.Count,
            estimatedPromptTokens);

        var rawResponse = await _aiService.ChatAsync(
            [ChatMessage.User(userPrompt)],
            systemPrompt,
            AnalysisChatOptions,
            ct).ConfigureAwait(false);

        return new ComparisonSynthesisResult
        {
            RawResponse = rawResponse.Trim(),
            EstimatedPromptTokens = estimatedPromptTokens
        };
    }

    private static string BuildSystemPrompt(ComparisonOptions options)
    {
        var detailInstruction = string.Equals(options.DetailLevel, "summary", StringComparison.OrdinalIgnoreCase)
            ? "Keep each list concise - a maximum of 3 bullet points per section."
            : "Be thorough - include all meaningful points in each section.";

        return $$"""
                You are an expert document analyst. Return your comparative analysis as a single valid JSON object.

                CRITICAL:
                - Return JSON only.
                - Do not include markdown, prose, or code fences.
                - Begin with '{' and end with '}'.

                Required schema:
                {
                  "summary": "<string>",
                  "similarities": ["<string>", "..."],
                  "differences": ["<string>", "..."],
                  "contradictions": ["<string>", "..."],
                  "uniquePoints": {
                    "<documentName>": ["<string>", "..."]
                  }
                }

                {{detailInstruction}}
                Use empty arrays when no findings exist.
                Include a uniquePoints key for every document.
                """;
    }

    private static string BuildUserPrompt(
        IReadOnlyDictionary<string, string> contentByDoc,
        ComparisonOptions options)
    {
        var builder = new StringBuilder(4096);

        if (!string.IsNullOrWhiteSpace(options.FocusQuery))
        {
            builder.AppendLine($"FOCUS TOPIC: {options.FocusQuery}");
            builder.AppendLine();
        }

        builder.AppendLine($"Compare the following {contentByDoc.Count} documents and return the JSON analysis:");
        builder.AppendLine();

        var index = 1;
        foreach (var pair in contentByDoc)
        {
            builder.AppendLine($"--- DOCUMENT {index}: {pair.Key} ---");
            builder.AppendLine(pair.Value);
            builder.AppendLine();
            index++;
        }

        builder.AppendLine("Return the JSON comparison object now.");
        return builder.ToString();
    }

    private static long EstimateTokens(string content) => (content.Length + CharsPerToken - 1) / CharsPerToken;
}
