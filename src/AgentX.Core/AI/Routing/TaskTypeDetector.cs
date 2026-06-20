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
    /// Ordered keyword-to-task-type mappings. Keywords are matched case-insensitively
    /// against the prompt. First match wins. Order matters: more specific keywords first.
    /// </summary>
    private static readonly (string[] Keywords, TaskType Type)[] KeywordMap =
    [
        (["extract", "parse data", "pull data", "entity recognition", "named entity"], TaskType.Extraction),
        (["summarize", "summary", "summarise", "tldr", "tl;dr", "condense", "brief"], TaskType.Summarization),
        (["analyze", "analyse", "analysis", "compare", "comparison", "evaluate", "assess", "review", "critique", "examine"], TaskType.Analysis),
        (["generate embedding", "generate vector", "embed", "embedding", "vectorize", "vector"], TaskType.Embedding),
        (["creative", "creative story", "poem", "fiction", "novel", "imagine", "brainstorm", "lyrics", "haiku"], TaskType.Creative),
        (["write code", "code", "program", "function", "debug", "fix bug", "refactor", "implement", "script", "algorithm", "class ", "method "], TaskType.Code),
        (["write", "generate", "draft", "compose", "create content", "produce", "article", "blog post", "essay"], TaskType.Generation),
    ];

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

        // 2. Keyword matching (case-insensitive, first match wins)
        var lowerPrompt = prompt.ToLowerInvariant();

        foreach (var (keywords, type) in KeywordMap)
        {
            foreach (var keyword in keywords)
            {
                if (lowerPrompt.Contains(keyword.ToLowerInvariant()))
                {
                    return type;
                }
            }
        }

        // 3. Default fallback
        return TaskType.Chat;
    }

    [GeneratedRegex(@"^\[(\w+)\]\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GenerateTagRegex();
}
