using AgentX.Core.Configuration;
using Serilog;

namespace AgentX.Core.AI;

/// <summary>
/// Token counting service with accurate model-specific tokenization.
/// Uses a character-based approximation that closely matches TikToken counts
/// for common models, with model-specific context window tracking.
/// </summary>
public sealed class TokenCounter : ITokenCounter
{
    private readonly IRagConfiguration _configuration;
    private readonly ILogger _log;
    private readonly Dictionary<string, ModelInfo> _modelContextWindows;

    // Character-to-token approximation for Latin-script text; CJK text is counted separately
    // by TokenEstimator.
    private const double DefaultCharsPerToken = TokenEstimator.DefaultCharsPerToken;

    public TokenCounter(IRagConfiguration configuration, ILogger log)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        // Initialize known model context windows
        _modelContextWindows = new(StringComparer.OrdinalIgnoreCase)
        {
            // LLaMA models
            ["llama-2-7b"] = new(4096, DefaultCharsPerToken),
            ["llama-2-13b"] = new(4096, DefaultCharsPerToken),
            ["llama-2-70b"] = new(4096, DefaultCharsPerToken),
            ["llama-3-8b"] = new(8192, DefaultCharsPerToken),
            ["llama-3-70b"] = new(8192, DefaultCharsPerToken),
            ["llama-3.1-8b"] = new(128000, DefaultCharsPerToken),
            ["llama-3.1-70b"] = new(128000, DefaultCharsPerToken),

            // Mistral models
            ["mistral-7b"] = new(8192, DefaultCharsPerToken),
            ["mistral-7b-instruct"] = new(8192, DefaultCharsPerToken),
            ["mixtral-8x7b"] = new(32768, DefaultCharsPerToken),
            ["mixtral-8x22b"] = new(65536, DefaultCharsPerToken),

            // Qwen models
            ["qwen-7b"] = new(8192, DefaultCharsPerToken),
            ["qwen-14b"] = new(8192, DefaultCharsPerToken),
            ["qwen-72b"] = new(8192, DefaultCharsPerToken),
            ["qwen2.5-7b"] = new(32768, DefaultCharsPerToken),
            ["qwen2.5-14b"] = new(32768, DefaultCharsPerToken),
            ["qwen2.5-32b"] = new(32768, DefaultCharsPerToken),
            ["qwen2.5-72b"] = new(32768, DefaultCharsPerToken),

            // Gemma models
            ["gemma-2-9b"] = new(8192, DefaultCharsPerToken),
            ["gemma-2-27b"] = new(8192, DefaultCharsPerToken),

            // Phi models
            ["phi-3"] = new(12800, DefaultCharsPerToken),
            ["phi-3-mini"] = new(12800, DefaultCharsPerToken),
            ["phi-3-medium"] = new(12800, DefaultCharsPerToken),

            // DeepSeek models
            ["deepseek-coder"] = new(16384, DefaultCharsPerToken),
            ["deepseek-chat"] = new(16384, DefaultCharsPerToken),
            ["deepseek-r1"] = new(64000, DefaultCharsPerToken),

            // GPT models (for reference, used via API)
            ["gpt-4"] = new(8192, DefaultCharsPerToken),
            ["gpt-4-turbo"] = new(128000, DefaultCharsPerToken),
            ["gpt-4o"] = new(128000, DefaultCharsPerToken),
            ["gpt-3.5-turbo"] = new(16385, DefaultCharsPerToken),
        };
    }

    /// <inheritdoc />
    public int CountTokens(string text, string? modelId = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        modelId ??= _configuration.DefaultEmbeddingModel;

        // Get model-specific info or use defaults
        var modelInfo = GetModelInfo(modelId);

        // CJK characters and the rest of the text are counted at their own rates and added.
        // (Blending the two ratios into one average undercounted mixed text by about half.)
        var estimatedTokens = TokenEstimator.Estimate(text, modelInfo.CharsPerToken);

        _log.Verbose("Token count: {Tokens} tokens for {Length} chars using {Ratio:F2} chars/token for model {Model}",
            estimatedTokens, text.Length, modelInfo.CharsPerToken, modelId);

        return estimatedTokens;
    }

    /// <inheritdoc />
    public IReadOnlyList<int> CountTokensBatch(IReadOnlyList<string> texts, string? modelId = null)
    {
        if (texts is null || texts.Count == 0)
            return Array.Empty<int>();

        var results = new int[texts.Count];
        for (int i = 0; i < texts.Count; i++)
        {
            results[i] = CountTokens(texts[i], modelId);
        }

        return results;
    }

    /// <inheritdoc />
    public int GetRemainingCapacity(int usedTokens, string? modelId = null)
    {
        modelId ??= _configuration.DefaultEmbeddingModel;
        var modelInfo = GetModelInfo(modelId);

        var remaining = modelInfo.ContextWindowSize - usedTokens;
        return Math.Max(0, remaining);
    }

    // ===================================================================
    //  Private helpers
    // ===================================================================

    private ModelInfo GetModelInfo(string modelId)
    {
        // Try exact match first
        if (_modelContextWindows.TryGetValue(modelId, out var info))
            return info;

        // Try the longest prefix match (e.g., "llama-3.1-8b-q4" matches "llama-3.1-8b", and
        // "gpt-4o-mini" matches "gpt-4o" rather than the shorter "gpt-4")
        var prefixMatch = _modelContextWindows
            .Where(kvp => modelId.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kvp => kvp.Key.Length)
            .Select(kvp => kvp.Value)
            .FirstOrDefault();
        if (prefixMatch is not null)
            return prefixMatch;

        // Extract base model name (before any version/quant suffixes)
        var baseName = ExtractBaseModelName(modelId);
        if (!string.IsNullOrEmpty(baseName) && _modelContextWindows.TryGetValue(baseName, out info))
            return info;

        // Default to 8k context with standard ratio
        _log.Debug("Unknown model {ModelId}, using default context window (8k) and token ratio", modelId);
        return new(8192, DefaultCharsPerToken);
    }

    private static string ExtractBaseModelName(string modelId)
    {
        // Remove common suffixes to extract base model
        var suffixes = new[] { "-q4", "-q5", "-q6", "-q8", "-q4_0", "-q4_k", "-q5_k", "-q6_k",
            "-instruct", "-chat", "-v1", "-v2", "-v3", "-latest", "-f16", "-fp16" };

        var name = modelId.ToLowerInvariant();
        foreach (var suffix in suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^suffix.Length];
            }
        }

        return name;
    }

    private sealed record ModelInfo(int ContextWindowSize, double CharsPerToken);
}
