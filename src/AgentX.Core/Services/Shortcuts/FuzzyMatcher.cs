using System;
using System.Collections.Generic;
using System.Linq;

namespace AgentX.Core.Services.Shortcuts;

/// <summary>
/// Subsequence-scoring fuzzy matcher inspired by VS Code's quick-pick ranker.
/// Higher score = better match. Score of 0 = no match.
/// Scoring boosts: word-boundary matches, consecutive matches, prefix matches, short haystacks.
/// </summary>
public static class FuzzyMatcher
{
    public record ScoredItem<T>(T Item, int Score);

    public static int Score(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle)) return 1; // empty query matches everything weakly
        if (string.IsNullOrEmpty(haystack)) return 0;

        var h = haystack.ToLowerInvariant();
        var n = needle.ToLowerInvariant();

        int score = 0;
        int hi = 0;
        int consecutive = 0;
        int firstMatchAt = -1;
        bool purePrefix = true; // flips false on any haystack skip

        for (int ni = 0; ni < n.Length; ni++)
        {
            var target = n[ni];
            bool found = false;
            while (hi < h.Length)
            {
                if (h[hi] == target)
                {
                    if (firstMatchAt == -1) firstMatchAt = hi;
                    bool isBoundary = hi == 0 || h[hi - 1] == ' ' || h[hi - 1] == '-' || h[hi - 1] == '_';
                    if (isBoundary) score += 3;
                    if (consecutive > 0) score += 2;
                    if (hi == 0 && ni == 0) score += 5;
                    score += 1;
                    consecutive++;
                    hi++;
                    found = true;
                    break;
                }
                else
                {
                    consecutive = 0;
                    purePrefix = false;
                    hi++;
                }
            }
            if (!found) return 0; // any query char not found → no match
        }

        // Pure-prefix bonus: needle matched consecutively starting at haystack[0].
        // Without this, long-needle suffix matches can out-score short-needle prefix
        // matches purely by accumulating more per-char points.
        if (purePrefix && firstMatchAt == 0) score += n.Length * 2;

        if (haystack.Length < 20) score += 1;
        return score;
    }

    public static IEnumerable<ScoredItem<T>> Rank<T>(
        IEnumerable<T> items,
        Func<T, string> labelSelector,
        string query)
    {
        return items
            .Select(i => new ScoredItem<T>(i, Score(labelSelector(i), query)))
            .Where(s => s.Score > 0)
            .OrderByDescending(s => s.Score)
            .ThenBy(s => labelSelector(s.Item).Length);
    }
}
