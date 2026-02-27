using Serilog;

namespace AgentX.Core.Search;

/// <summary>
/// Reranks and deduplicates RAG context chunks to improve answer quality.
///
/// The reranking pipeline applies three transformations in order:
///   1. <b>Near-duplicate removal</b> -- Jaccard similarity on word sets; chunks &gt;85% similar
///      to an already-selected chunk are discarded.
///   2. <b>Query relevance boost</b> -- chunks containing exact query terms receive a score
///      boost (capped at 1.5x original).
///   3. <b>Document diversity adjustment</b> -- if a single document contributes more than 60%
///      of the selected chunks, excess chunks from that document are demoted by 20%.
///
/// After all adjustments the chunks are sorted by effective score (descending) and the top
/// <c>maxChunks</c> are returned with their <see cref="RagContextChunk.RelevanceScore"/>
/// updated to the computed effective score.
/// </summary>
public sealed class RagReranker : IRagReranker
{
    /// <summary>Jaccard similarity threshold above which two chunks are considered near-duplicates.</summary>
    private const double DuplicateJaccardThreshold = 0.85;

    /// <summary>
    /// Maximum proportion of selected chunks that may originate from a single document
    /// before the diversity penalty is applied.
    /// </summary>
    private const double MaxDocumentShareRatio = 0.60;

    /// <summary>Multiplier applied to excess chunks from an over-represented document.</summary>
    private const double DiversityPenaltyFactor = 0.80;

    /// <summary>Minimum word length to consider a query token "significant".</summary>
    private const int MinQueryTokenLength = 3;

    /// <summary>Per-query-term boost added to the relevance multiplier.</summary>
    private const double QueryTermBoostRate = 0.10;

    /// <summary>Maximum total multiplier from query term boosting (1 + boost).</summary>
    private const double MaxQueryBoostMultiplier = 1.50;

    private readonly ILogger _logger;

    public RagReranker(ILogger logger)
    {
        _logger = logger?.ForContext<RagReranker>()
                  ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public List<RagContextChunk> Rerank(List<RagContextChunk> chunks, string query, int maxChunks = 8)
    {
        if (chunks is null || chunks.Count == 0)
            return new List<RagContextChunk>();

        if (string.IsNullOrWhiteSpace(query))
            return chunks.Take(maxChunks).ToList();

        _logger.Debug("Reranking {InputCount} chunks for query, target {MaxChunks}",
            chunks.Count, maxChunks);

        // ── Step 1: Deduplicate (remove near-duplicates) ──────────────────
        var deduplicated = RemoveNearDuplicates(chunks);

        // ── Step 2: Apply query relevance boost ───────────────────────────
        var queryTokens = ExtractQueryTokens(query);
        var scored = ApplyQueryRelevanceBoost(deduplicated, queryTokens);

        // ── Step 3: Apply document diversity adjustment ────────────────────
        scored = ApplyDocumentDiversityAdjustment(scored);

        // ── Step 4: Sort by effective score descending ────────────────────
        scored.Sort((a, b) => b.EffectiveScore.CompareTo(a.EffectiveScore));

        // ── Step 5: Take top maxChunks and return ─────────────────────────
        var result = scored
            .Take(maxChunks)
            .Select(s => new RagContextChunk
            {
                ChunkId = s.Chunk.ChunkId,
                DocumentId = s.Chunk.DocumentId,
                FileName = s.Chunk.FileName,
                FilePath = s.Chunk.FilePath,
                PageNumber = s.Chunk.PageNumber,
                ChunkIndex = s.Chunk.ChunkIndex,
                ChunkText = s.Chunk.ChunkText,
                RelevanceScore = (float)s.EffectiveScore
            })
            .ToList();

        _logger.Debug("Reranking complete: {InputCount} -> {OutputCount} chunks",
            chunks.Count, result.Count);

        return result;
    }

    // ── Near-Duplicate Removal ────────────────────────────────────────────

    /// <summary>
    /// Removes near-duplicate chunks using Jaccard similarity on word sets.
    /// Iterates in order of the input list (which is assumed to be sorted by
    /// descending similarity score). Each chunk is compared against all
    /// previously selected chunks; if its Jaccard similarity with any selected
    /// chunk exceeds <see cref="DuplicateJaccardThreshold"/>, it is discarded.
    /// </summary>
    private List<RagContextChunk> RemoveNearDuplicates(List<RagContextChunk> chunks)
    {
        var selected = new List<RagContextChunk>(chunks.Count);
        var selectedWordSets = new List<HashSet<string>>(chunks.Count);
        var removedCount = 0;

        foreach (var chunk in chunks)
        {
            var wordSet = TokenizeToWordSet(chunk.ChunkText);

            var isDuplicate = false;
            foreach (var existingSet in selectedWordSets)
            {
                if (ComputeJaccardSimilarity(wordSet, existingSet) > DuplicateJaccardThreshold)
                {
                    isDuplicate = true;
                    break;
                }
            }

            if (isDuplicate)
            {
                removedCount++;
                continue;
            }

            selected.Add(chunk);
            selectedWordSets.Add(wordSet);
        }

        if (removedCount > 0)
        {
            _logger.Debug("Removed {N} near-duplicate chunks", removedCount);
        }

        return selected;
    }

    /// <summary>
    /// Tokenizes text into a set of lowercase words, splitting on whitespace and
    /// common punctuation. This produces the "bag of words" used for Jaccard comparison.
    /// </summary>
    private static HashSet<string> TokenizeToWordSet(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isWordChar = i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '\'');

            if (isWordChar)
            {
                if (start < 0)
                    start = i;
            }
            else if (start >= 0)
            {
                var word = text[start..i];
                if (word.Length > 1) // Skip single-character tokens to reduce noise
                {
                    words.Add(word);
                }
                start = -1;
            }
        }

        return words;
    }

    /// <summary>
    /// Computes the Jaccard similarity between two word sets.
    /// Jaccard(A, B) = |A intersect B| / |A union B|.
    /// Returns 0.0 if both sets are empty, avoiding division by zero.
    /// </summary>
    private static double ComputeJaccardSimilarity(HashSet<string> setA, HashSet<string> setB)
    {
        if (setA.Count == 0 && setB.Count == 0)
            return 0.0;

        // Count intersection size without allocating a new set.
        // Iterate the smaller set for better performance.
        var smaller = setA.Count <= setB.Count ? setA : setB;
        var larger = setA.Count <= setB.Count ? setB : setA;

        var intersectionCount = 0;
        foreach (var word in smaller)
        {
            if (larger.Contains(word))
                intersectionCount++;
        }

        // |A union B| = |A| + |B| - |A intersect B|
        var unionCount = setA.Count + setB.Count - intersectionCount;

        return unionCount == 0 ? 0.0 : (double)intersectionCount / unionCount;
    }

    // ── Query Relevance Boost ─────────────────────────────────────────────

    /// <summary>
    /// Extracts significant lowercase words from the user query.
    /// Words shorter than <see cref="MinQueryTokenLength"/> characters are excluded
    /// as they are typically stop-words or articles.
    /// </summary>
    private static HashSet<string> ExtractQueryTokens(string query)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var words = TokenizeToWordSet(query);

        foreach (var word in words)
        {
            if (word.Length >= MinQueryTokenLength)
                tokens.Add(word);
        }

        return tokens;
    }

    /// <summary>
    /// Boosts the effective score of each chunk based on how many significant
    /// query terms appear in the chunk text.
    ///
    /// Formula: effectiveScore = originalScore * min(1 + 0.1 * queryTermHits, 1.5)
    /// </summary>
    private static List<ScoredChunk> ApplyQueryRelevanceBoost(
        List<RagContextChunk> chunks,
        HashSet<string> queryTokens)
    {
        var scored = new List<ScoredChunk>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var chunkWords = TokenizeToWordSet(chunk.ChunkText);
            var queryTermHits = 0;

            foreach (var token in queryTokens)
            {
                if (chunkWords.Contains(token))
                    queryTermHits++;
            }

            var boostMultiplier = Math.Min(
                1.0 + QueryTermBoostRate * queryTermHits,
                MaxQueryBoostMultiplier);

            scored.Add(new ScoredChunk
            {
                Chunk = chunk,
                EffectiveScore = chunk.RelevanceScore * boostMultiplier
            });
        }

        return scored;
    }

    // ── Document Diversity Adjustment ─────────────────────────────────────

    /// <summary>
    /// Ensures no single document dominates the context window. If more than 60%
    /// of the chunks originate from the same document, the excess chunks from
    /// that document are penalized (their effective score is reduced by 20%).
    /// The list is then re-sorted by effective score.
    /// </summary>
    private List<ScoredChunk> ApplyDocumentDiversityAdjustment(List<ScoredChunk> scored)
    {
        if (scored.Count == 0)
            return scored;

        // Count chunks per document
        var documentCounts = new Dictionary<long, int>();
        foreach (var item in scored)
        {
            var docId = item.Chunk.DocumentId;
            documentCounts.TryGetValue(docId, out var count);
            documentCounts[docId] = count + 1;
        }

        var totalChunks = scored.Count;
        var maxAllowed = (int)Math.Ceiling(totalChunks * MaxDocumentShareRatio);

        foreach (var (docId, count) in documentCounts)
        {
            if (count <= maxAllowed)
                continue;

            // This document has too many chunks. Demote the lowest-scoring excess chunks.
            // First, collect all chunks for this document, sorted by effective score descending.
            var documentChunks = scored
                .Where(s => s.Chunk.DocumentId == docId)
                .OrderByDescending(s => s.EffectiveScore)
                .ToList();

            var demotedCount = 0;

            // The first 'maxAllowed' chunks keep their score; the rest are penalized.
            for (var i = maxAllowed; i < documentChunks.Count; i++)
            {
                documentChunks[i].EffectiveScore *= DiversityPenaltyFactor;
                demotedCount++;
            }

            if (demotedCount > 0)
            {
                _logger.Debug("Applied diversity adjustment to {N} chunks from document {DocId}",
                    demotedCount, docId);
            }
        }

        // Re-sort after diversity adjustments
        scored.Sort((a, b) => b.EffectiveScore.CompareTo(a.EffectiveScore));

        return scored;
    }

    // ── Internal Types ────────────────────────────────────────────────────

    /// <summary>
    /// Pairs a <see cref="RagContextChunk"/> with a mutable effective score
    /// that is adjusted during the reranking pipeline.
    /// </summary>
    private sealed class ScoredChunk
    {
        public required RagContextChunk Chunk { get; init; }
        public double EffectiveScore { get; set; }
    }
}
