using System.Text.RegularExpressions;
using AgentX.Core.AI.Models;
using Serilog;

namespace AgentX.Core.AI.Context;

public sealed class SemanticContextSelector : ISemanticContextSelector
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IContextWindowManager _contextWindowManager;
    private readonly ILogger _logger;

    private const double SemanticWeight = 0.68;
    private const double LexicalWeight = 0.22;
    private const double RecencyWeight = 0.10;

    public SemanticContextSelector(
        IEmbeddingService embeddingService,
        IContextWindowManager contextWindowManager,
        ILogger logger)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _contextWindowManager = contextWindowManager ?? throw new ArgumentNullException(nameof(contextWindowManager));
        _logger = logger?.ForContext<SemanticContextSelector>()
                  ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ContextSelectionResult> SelectRelevantContextAsync(
        ContextSelectionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.CandidateMessages.Count == 0 || request.MaxTokenBudget <= 0)
        {
            return new ContextSelectionResult();
        }

        ct.ThrowIfCancellationRequested();

        var scores = await ScoreCandidatesAsync(request, ct).ConfigureAwait(false);
        var selectedPositions = new HashSet<int>();
        var usedTokens = 0;

        foreach (var candidate in scores
                     .OrderByDescending(x => x.Score)
                     .ThenByDescending(x => x.Item.Index))
        {
            ct.ThrowIfCancellationRequested();

            TrySelect(candidate.Position);

            if (selectedPositions.Contains(candidate.Position))
            {
                TrySelectPreferredNeighbor(candidate.Position);
            }
        }

        var selected = scores
            .Where(x => selectedPositions.Contains(x.Position))
            .OrderBy(x => x.Item.Index)
            .Select(x => x.Item)
            .ToList();

        var overflow = request.CandidateMessages
            .Where((_, position) => !selectedPositions.Contains(position))
            .ToList();

        return new ContextSelectionResult
        {
            SelectedMessages = selected,
            OverflowMessages = overflow,
            UsedLexicalFallback = scores.Any(x => x.UsedLexicalFallback),
            EstimatedSelectedTokens = usedTokens
        };

        void TrySelect(int position)
        {
            if (position < 0 || position >= scores.Count || selectedPositions.Contains(position))
            {
                return;
            }

            var candidate = scores[position];
            if (usedTokens + candidate.TokenCount > request.MaxTokenBudget)
            {
                return;
            }

            selectedPositions.Add(position);
            usedTokens += candidate.TokenCount;
        }

        void TrySelectPreferredNeighbor(int position)
        {
            var current = scores[position];
            var preferredNeighborPosition = current.Item.Message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                ? position - 1
                : position + 1;

            if (preferredNeighborPosition < 0 || preferredNeighborPosition >= scores.Count)
            {
                return;
            }

            var neighbor = scores[preferredNeighborPosition];
            if (neighbor.Item.Message.Role.Equals(current.Item.Message.Role, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (neighbor.Score < 0.12)
            {
                return;
            }

            TrySelect(preferredNeighborPosition);
        }
    }

    private async Task<List<ScoredCandidate>> ScoreCandidatesAsync(
        ContextSelectionRequest request,
        CancellationToken ct)
    {
        var lexicalScores = request.CandidateMessages
            .Select(item => ComputeLexicalOverlap(request.CurrentQuery, item.Message.Content))
            .ToArray();

        var recencyDenominator = Math.Max(1, request.CandidateMessages.Count - 1);
        var semanticScores = new double[request.CandidateMessages.Count];
        var usedLexicalFallback = false;

        if (!string.IsNullOrWhiteSpace(request.CurrentQuery))
        {
            try
            {
                var queryEmbedding = await _embeddingService.EmbedAsync(request.CurrentQuery, ct).ConfigureAwait(false);
                var messageEmbeddings = await _embeddingService
                    .EmbedBatchAsync(request.CandidateMessages.Select(m => m.Message.Content), ct)
                    .ConfigureAwait(false);

                for (var i = 0; i < request.CandidateMessages.Count && i < messageEmbeddings.Count; i++)
                {
                    semanticScores[i] = Clamp01(CosineSimilarity(queryEmbedding, messageEmbeddings[i]));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                usedLexicalFallback = true;
                _logger.Warning(ex, "Semantic context scoring failed. Falling back to lexical overlap.");
            }
        }
        else
        {
            usedLexicalFallback = true;
        }

        var scored = new List<ScoredCandidate>(request.CandidateMessages.Count);

        for (var i = 0; i < request.CandidateMessages.Count; i++)
        {
            var item = request.CandidateMessages[i];
            var recencyScore = request.CandidateMessages.Count == 1
                ? 1.0
                : (double)i / recencyDenominator;
            var roleWeight = item.Message.Role switch
            {
                "user" => 0.05,
                "assistant" => 0.03,
                _ => 0.01
            };

            var score = (semanticScores[i] * SemanticWeight) +
                        (lexicalScores[i] * LexicalWeight) +
                        (recencyScore * RecencyWeight) +
                        roleWeight;

            if (string.IsNullOrWhiteSpace(item.Message.Content) || item.Message.Content.Length < 8)
            {
                score -= 0.08;
            }

            scored.Add(new ScoredCandidate(
                i,
                item,
                Math.Max(0, score),
                _contextWindowManager.EstimateTokenCount(new[] { item.Message }),
                usedLexicalFallback));
        }

        return scored;
    }

    private static double ComputeLexicalOverlap(string query, string content)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(content))
        {
            return 0;
        }

        var queryTerms = Tokenize(query);
        if (queryTerms.Count == 0)
        {
            return 0;
        }

        var contentTerms = Tokenize(content);
        if (contentTerms.Count == 0)
        {
            return 0;
        }

        var overlap = queryTerms.Count(term => contentTerms.Contains(term));
        return (double)overlap / queryTerms.Count;
    }

    private static HashSet<string> Tokenize(string value)
    {
        return Regex.Matches(value.ToLowerInvariant(), "[a-z0-9]{3,}")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static double CosineSimilarity(float[] left, float[] right)
    {
        if (left.Length == 0 || right.Length == 0 || left.Length != right.Length)
        {
            return 0;
        }

        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;

        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }

        if (leftNorm <= 0 || rightNorm <= 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }

    private static double Clamp01(double value) => Math.Max(0, Math.Min(1, value));

    private sealed record ScoredCandidate(
        int Position,
        IndexedChatMessage Item,
        double Score,
        int TokenCount,
        bool UsedLexicalFallback);
}
