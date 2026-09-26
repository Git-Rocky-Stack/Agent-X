namespace AgentX.Core.AI;

/// <summary>
/// Script-aware token estimate shared by <see cref="TokenCounter"/> and
/// <see cref="ContextWindowManager"/>. Latin-script text averages about four characters per
/// token, while CJK characters (Chinese, Japanese, Korean) cost one or more tokens each, so a
/// flat characters-per-token ratio underestimates CJK text several times over and lets prompts
/// overflow the model's context window.
/// </summary>
internal static class TokenEstimator
{
    /// <summary>Characters per token for Latin-script text.</summary>
    public const double DefaultCharsPerToken = 4.0;

    /// <summary>Characters per token for CJK text (conservative: about 1.7 tokens per character).</summary>
    public const double CjkCharsPerToken = 0.6;

    /// <summary>
    /// Estimates tokens as the sum of the Latin-script and CJK parts, each at its own rate.
    /// Rounds up, so the estimate never reports zero tokens for non-empty text.
    /// </summary>
    public static int Estimate(string? text, double charsPerToken = DefaultCharsPerToken)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var cjk = CountCjk(text);
        var other = text.Length - cjk;
        return (int)Math.Ceiling(other / charsPerToken + cjk / CjkCharsPerToken);
    }

    /// <summary>Number of CJK characters in <paramref name="text"/>.</summary>
    public static int CountCjk(string text)
    {
        var count = 0;
        foreach (var c in text)
        {
            if (IsCjk(c))
                count++;
        }

        return count;
    }

    /// <summary>
    /// True for CJK ideographs (including extension A and compatibility ideographs), kana, Hangul,
    /// CJK punctuation and full-width forms.
    /// </summary>
    public static bool IsCjk(char c) =>
        (int)c is >= 0x3000 and <= 0x30FF // CJK punctuation, hiragana, katakana
            or >= 0x3400 and <= 0x4DBF // CJK extension A
            or >= 0x4E00 and <= 0x9FFF // CJK unified ideographs
            or >= 0x1100 and <= 0x11FF // Hangul jamo
            or >= 0xAC00 and <= 0xD7AF // Hangul syllables
            or >= 0xF900 and <= 0xFAFF // CJK compatibility ideographs
            or >= 0xFF00 and <= 0xFFEF; // half-width and full-width forms
}
