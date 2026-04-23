using System.Text;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Constants;
using Serilog;

namespace AgentX.Core.Services.Intelligence;

public sealed class HierarchicalSummaryService : IHierarchicalSummaryService
{
    private readonly IAiService _aiService;
    private readonly ILogger _logger;

    private static readonly ChatOptions SummaryChatOptions = new()
    {
        Temperature = 0.2,
        MaxTokens = 1024
    };

    private static readonly ChatOptions KeyPointChatOptions = new()
    {
        Temperature = 0.2,
        MaxTokens = 768
    };

    public HierarchicalSummaryService(IAiService aiService, ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _logger = logger?.ForContext<HierarchicalSummaryService>()
                  ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> SummarizeAsync(
        string documentTitle,
        IReadOnlyList<string> sections,
        CancellationToken ct = default)
    {
        var normalizedSections = NormalizeSections(sections);
        if (normalizedSections.Count == 0)
        {
            return string.Empty;
        }

        if (normalizedSections.Count == 1)
        {
            return await SummarizeSectionAsync(documentTitle, normalizedSections[0], ct).ConfigureAwait(false);
        }

        var sectionSummaries = new List<string>(normalizedSections.Count);
        foreach (var section in normalizedSections.Take(6))
        {
            ct.ThrowIfCancellationRequested();
            sectionSummaries.Add(await SummarizeSectionAsync(documentTitle, section, ct).ConfigureAwait(false));
        }

        var synthesisPrompt = $$"""
                                Combine the following section summaries into one concise document summary.
                                Focus on the main topics, findings, and conclusions.

                                DOCUMENT TITLE: {{documentTitle}}

                                SECTION SUMMARIES:
                                {{string.Join(Environment.NewLine + Environment.NewLine, sectionSummaries)}}
                                """;

        return (await _aiService.ChatAsync(
                [ChatMessage.User(synthesisPrompt)],
                options: SummaryChatOptions,
                ct: ct)
            .ConfigureAwait(false)).Trim();
    }

    public async Task<IReadOnlyList<string>> ExtractKeyPointsAsync(
        string documentTitle,
        IReadOnlyList<string> sections,
        CancellationToken ct = default)
    {
        var normalizedSections = NormalizeSections(sections);
        if (normalizedSections.Count == 0)
        {
            return Array.Empty<string>();
        }

        var synthesisInput = normalizedSections.Count == 1
            ? normalizedSections[0]
            : string.Join(Environment.NewLine + Environment.NewLine,
                normalizedSections.Take(6).Select((section, index) => $"Section {index + 1}: {section}"));

        var prompt = $$"""
                       Extract the key points from the following document content as a numbered list.
                       Each point should be one concise sentence.

                       DOCUMENT TITLE: {{documentTitle}}

                       CONTENT:
                       {{synthesisInput}}
                       """;

        var response = await _aiService.ChatAsync(
            [ChatMessage.User(prompt)],
            options: KeyPointChatOptions,
            ct: ct).ConfigureAwait(false);

        return ParseKeyPoints(response);
    }

    private async Task<string> SummarizeSectionAsync(string documentTitle, string section, CancellationToken ct)
    {
        var prompt = $$"""
                       Summarize this document section concisely.
                       Focus on durable facts, arguments, and conclusions.

                       DOCUMENT TITLE: {{documentTitle}}

                       SECTION:
                       {{section}}
                       """;

        return (await _aiService.ChatAsync(
                [ChatMessage.User(prompt)],
                options: SummaryChatOptions,
                ct: ct)
            .ConfigureAwait(false)).Trim();
    }

    private static List<string> NormalizeSections(IReadOnlyList<string> sections)
    {
        var normalized = new List<string>(sections.Count);
        var remainingChars = AppConstants.MaxDocumentCharsForSummary;

        foreach (var section in sections)
        {
            if (remainingChars <= 0)
            {
                break;
            }

            var content = section?.Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            if (content.Length > remainingChars)
            {
                content = content[..remainingChars];
            }

            normalized.Add(content);
            remainingChars -= content.Length;
        }

        return normalized;
    }

    private static IReadOnlyList<string> ParseKeyPoints(string response)
    {
        var keyPoints = new List<string>();
        foreach (var rawLine in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            line = line.TrimStart('-', '*', ' ');
            while (line.Length > 0 && (char.IsDigit(line[0]) || line[0] is '.' or ')' or ':'))
            {
                line = line[1..].TrimStart();
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                keyPoints.Add(line);
            }
        }

        return keyPoints;
    }
}
