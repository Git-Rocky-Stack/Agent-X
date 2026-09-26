using System.Text.RegularExpressions;

namespace AgentX.Core.AI.Routing;

/// <summary>
/// Detects the <see cref="TaskType"/> of a prompt using explicit tag overrides
/// (e.g. <c>[analysis]</c> prefix) or keyword heuristics. Falls back to
/// <see cref="TaskType.Chat"/> for empty or unrecognized prompts.
/// </summary>
public sealed partial class TaskTypeDetector : ITaskTypeDetector
{
    /// <summary>
    /// Pattern for explicit task type tags at the start of a prompt.
    /// Matches tags like [analysis], [code], [creative], etc.
    /// </summary>
    private static readonly Regex TagPattern = GenerateTagRegex();

    /// <summary>
    /// Ordered keyword-to-task-type mappings. Keywords are matched case-insensitively as whole
    /// words (plus a plain s/es/d/ed/ing ending), so "script" does not match "description" and
    /// "code" does not match "decode". First match wins. Order matters: more specific keywords first.
    /// </summary>
    private static readonly (string[] Keywords, TaskType Type)[] KeywordMap =
    [
        (["extract", "parse data", "pull data", "entity recognition", "named entity"], TaskType.Extraction),
        (["summarize", "summary", "summarise", "tldr", "tl;dr", "condense", "brief"], TaskType.Summarization),
        (["analyze", "analyse", "analysis", "compare", "comparison", "evaluate", "assess", "review", "critique", "examine"], TaskType.Analysis),
        (["generate embedding", "generate vector", "embed", "embedding", "vectorize", "vector"], TaskType.Embedding),
        (["creative", "creative story", "poem", "fiction", "novel", "imagine", "brainstorm", "lyrics", "haiku"], TaskType.Creative),
        (["write code", "code", "coding", "program", "programming", "function", "debug", "debugging", "fix bug", "refactor", "implement", "script", "algorithm"], TaskType.Code),
        (["write", "generate", "draft", "compose", "create content", "produce", "article", "blog post", "essay"], TaskType.Generation),
    ];

    private static readonly (Regex Pattern, TaskType Type)[] KeywordPatterns =
        KeywordMap.Select(entry => (BuildKeywordPattern(entry.Keywords), entry.Type)).ToArray();

    /// <inheritdoc />
    public TaskType Detect(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return TaskType.Chat;

        // 1. Check for explicit tag override: [tasktype] at the start
        var tagMatch = TagPattern.Match(prompt);
        if (tagMatch.Success)
        {
            var tagName = tagMatch.Groups[1].Value;
            var taskType = TaskType.FromString(tagName);
            return taskType;
        }

        // 2. Keyword matching (case-insensitive whole words, first match wins). Everyday uses of
        // "code" (zip code, dress code, code of conduct) are removed first so they do not route
        // an ordinary question as a programming task.
        var text = NonProgrammingCodeRegex().Replace(prompt, " ");

        foreach (var (pattern, type) in KeywordPatterns)
        {
            if (pattern.IsMatch(text))
            {
                return type;
            }
        }

        // 3. Default fallback
        return TaskType.Chat;
    }

    private static Regex BuildKeywordPattern(IEnumerable<string> keywords)
    {
        var alternatives = keywords.Select(keyword => Regex.Escape(keyword.Trim()).Replace(@"\ ", @"\s+"));
        return new Regex(
            $@"\b(?:{string.Join("|", alternatives)})(?:s|es|d|ed|ing)?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    [GeneratedRegex(@"^\[(\w+)\]\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GenerateTagRegex();

    [GeneratedRegex(
        @"\b(?:zip|postal|post|area|dress|country|promo|promotional|coupon|discount|voucher|gift|verification|security|access|pin|tax)\s+codes?\b|\bcodes?\s+of\s+(?:conduct|ethics|practice)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonProgrammingCodeRegex();
}
