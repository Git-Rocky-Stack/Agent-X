using Microsoft.Extensions.Options;

namespace AgentX.Core.Configuration;

/// <summary>
/// P2-4: <see cref="IRagPromptCatalog"/> implementation backed by
/// <see cref="IOptionsMonitor{TOptions}"/>. Every property read resolves the
/// current option value, so edits to <c>RagPrompts.json</c> take effect on
/// the next prompt site invocation — no process restart required.
/// Falls back to <see cref="RagPromptDefaults"/> when an option is null,
/// empty, or contains only blank lines.
/// </summary>
public sealed class RagPromptCatalog : IRagPromptCatalog
{
    private readonly IOptionsMonitor<RagPromptOptions> _monitor;

    public RagPromptCatalog(IOptionsMonitor<RagPromptOptions> monitor)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
    }

    public string RagSystemPrefix
        => Resolve(_monitor.CurrentValue.RagSystemPrefix, RagPromptDefaults.RagSystemPrefix);

    public string EvalSystem
        => Resolve(_monitor.CurrentValue.EvalSystem, RagPromptDefaults.EvalSystem);

    public string RerankerSystem
        => Resolve(_monitor.CurrentValue.RerankerSystem, RagPromptDefaults.RerankerSystem);

    public string CompressorSystem
        => Resolve(_monitor.CurrentValue.CompressorSystem, RagPromptDefaults.CompressorSystem);

    public string MultiQuerySystem
        => Resolve(_monitor.CurrentValue.MultiQuerySystem, RagPromptDefaults.MultiQuerySystem);

    public string HydeSystem
        => Resolve(_monitor.CurrentValue.HydeSystem, RagPromptDefaults.HydeSystem);

    /// <summary>
    /// Joins multi-line prompt arrays with <c>\n</c>. An empty / null / all-blank
    /// array falls back to the compile-time default — operators don't have to
    /// override every prompt to use this catalog.
    /// </summary>
    private static string Resolve(string[]? lines, string @default)
    {
        if (lines is null || lines.Length == 0) return @default;

        // Treat all-blank arrays as "not set" — protects against an editor
        // accidentally saving a prompt as `["", "", ""]` and silently breaking
        // a downstream LLM call.
        bool allBlank = true;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i])) { allBlank = false; break; }
        }
        if (allBlank) return @default;

        return string.Join("\n", lines);
    }
}
