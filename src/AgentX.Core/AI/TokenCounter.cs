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

    // Character-to-token approximation ratios based on TikToken analysis
    // These are conservative estimates that work well for most English text
    private const double DefaultCharsPerToken = 4.0; // ~4 characters per token for English
    private const double ChineseCharsPerToken = 0.6; // ~0.6 Chinese characters per token

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
        var charsPerToken = modelInfo.CharsPerToken;

        // Detect if text contains CJK characters (Chinese, Japanese, Korean)
        var hasCJK = ContainsCJKCharacters(text);

        // Adjust ratio for mixed content
        if (hasCJK)
        {
            // For CJK text, tokens are more dense
            // Estimate based on character distribution
            var cjkRatio = EstimateCJKRatio(text);
            charsPerToken = BlendRatios(DefaultCharsPerToken, ChineseCharsPerToken, cjkRatio);
        }

        // Calculate token count with ceiling to ensure we don't underestimate
        var estimatedTokens = (int)Math.Ceiling(text.Length / charsPerToken);

        _log.Verbose("Token count: {Tokens} tokens for {Length} chars using {Ratio:F2} chars/token for model {Model}",
            estimatedTokens, text.Length, charsPerToken, modelId);

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

    // ═══════════════════════════════════════════════════════════════════
    //  Private helpers
    // ═══════════════════════════════════════════════════════════════════

    private ModelInfo GetModelInfo(string modelId)
    {
        // Try exact match first
        if (_modelContextWindows.TryGetValue(modelId, out var info))
            return info;

        // Try prefix match (e.g., "llama-3.1-8b-q4" matches "llama-3.1-8b")
        foreach (var kvp in _modelContextWindows)
        {
            if (modelId.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

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

    private static bool ContainsCJKCharacters(string text)
    {
        foreach (var c in text)
        {
            if (c >= 0x4E00 && c <= 0x9FFF) // CJK Unified Ideographs
                return true;
            if (c >= 0x3040 && c <= 0x309F) // Hiragana
                return true;
            if (c >= 0x30A0 && c <= 0x30FF) // Katakana
                return true;
            if (c >= 0xAC00 && c <= 0xD7AF) // Hangul Syllables
                return true;
        }
        return false;
    }

    private static double EstimateCJKRatio(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0.0;

        int cjkCount = 0;
        foreach (var c in text)
        {
            if (c >= 0x4E00 && c <= 0x9FFF || // CJK Unified Ideographs
                c >= 0x3040 && c <= 0x309F || // Hiragana
                c >= 0x30A0 && c <= 0x30FF || // Katakana
                c >= 0xAC00 && c <= 0xD7AF)   // Hangul Syllables
            {
                cjkCount++;
            }
        }

        return (double)cjkCount / text.Length;
    }

    private static double BlendRatios(double englishRatio, double cjkRatio, double cjkProportion)
    {
        // Linear interpolation based on CJK character proportion
        return englishRatio * (1.0 - cjkProportion) + cjkRatio * cjkProportion;
    }

    private sealed record ModelInfo(int ContextWindowSize, double CharsPerToken);
}
