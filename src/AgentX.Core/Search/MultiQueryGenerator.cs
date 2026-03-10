using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using Serilog;

namespace AgentX.Core.Search;

/// <summary>
/// Uses the local LLM to generate multiple phrasings of a user query.
/// Each variation captures a different perspective, improving overall retrieval recall
/// when used across parallel search requests.
/// </summary>
public sealed class MultiQueryGenerator : IMultiQueryGenerator
{
    private readonly IAiService _aiService;
    private readonly ILogger _logger;

    private const string SystemPrompt =
        """
        You are a search query expansion assistant. Given a user question, generate alternative
        phrasings that would help retrieve relevant documents. Each variation should capture a
        different aspect or use different keywords while preserving the original intent.
        Return ONLY the alternative queries, one per line, with no numbering or extra text.
        """;

    public MultiQueryGenerator(IAiService aiService, ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _logger = logger?.ForContext<MultiQueryGenerator>() ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GenerateQueryVariationsAsync(
        string query,
        int count = 3,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new[] { query };

        var queries = new List<string> { query }; // Original always first

        try
        {
            var messages = new List<ChatMessage>
            {
                new() { Role = "user", Content = $"Generate {count} alternative phrasings for this search query:\n\n\"{query}\"" }
            };

            var options = new ChatOptions
            {
                Temperature = 0.7,
                MaxTokens = 256
            };

            var response = await _aiService.ChatAsync(messages, SystemPrompt, options, ct)
                .ConfigureAwait(false);

            var variations = response
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.Length > 5 && !line.StartsWith('#'))
                .Select(line => line.TrimStart('-', '*', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', ')').Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(count)
                .ToList();

            queries.AddRange(variations);

            _logger.Debug("Generated {Count} query variations for: {Query}", variations.Count, query);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Multi-query generation failed; using original query only");
        }

        return queries.AsReadOnly();
    }
}
