using System.Security.Cryptography;
using System.Text;

namespace AgentX.Core.Observability;

/// <summary>
/// Privacy-sensitive log helpers (P2-10).
/// <para>
/// LLM responses can echo back chunks of source content — including PII the system
/// just redacted upstream. Dumping raw responses into Warning/Error logs (which
/// are typically retained longer than Debug logs and shipped off-machine) recreates
/// the leak we worked to prevent. <see cref="ForLog(string?, int)"/> emits a short
/// diagnostic surface — head, length, and a SHA-256 prefix for log correlation —
/// instead of the full payload.
/// </para>
/// </summary>
internal static class LogRedaction
{
    private const int DefaultHeadChars = 80;
    private const int HashBytes = 4; // 8 hex chars — enough for grouping equivalent failures

    /// <summary>
    /// Returns a privacy-aware summary suitable for embedding in log messages.
    /// Format: <c>"&lt;first N chars&gt;… [len=NNN hash=ABCDEF12]"</c>.
    /// </summary>
    public static string ForLog(string? text, int headChars = DefaultHeadChars)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "<empty>";
        }

        var safeHead = headChars <= 0 ? 0 : headChars;
        var head = text.Length <= safeHead ? text : text[..safeHead];

        // Strip newlines from the head so a single log line stays a single line.
        head = head.Replace('\r', ' ').Replace('\n', ' ');

        var hash = ShortHash(text);
        var ellipsis = text.Length > safeHead ? "…" : string.Empty;

        return $"{head}{ellipsis} [len={text.Length} hash={hash}]";
    }

    private static string ShortHash(string text)
    {
        Span<byte> hash = stackalloc byte[32];
        if (SHA256.TryHashData(Encoding.UTF8.GetBytes(text), hash, out _))
        {
            return Convert.ToHexString(hash[..HashBytes]);
        }

        return "????????";
    }
}
