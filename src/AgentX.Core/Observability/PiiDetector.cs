using System.Text.RegularExpressions;
using Serilog;

namespace AgentX.Core.Observability;

/// <summary>
/// Detects and redacts Personally Identifiable Information (PII) from text.
/// Supports emails, phone numbers, SSNs, credit cards, API keys, and custom patterns.
/// </summary>
public sealed class PiiDetector : IPiiDetector
{
    private readonly ILogger _log;
    private readonly List<PiiPattern> _patterns;

    // Precompiled regex patterns for common PII types
    private static readonly Regex EmailRegex = new(
        @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PhoneRegex = new(
        @"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b|\b\+?1?[-.]?\(?\d{3}\)?[-.]?\d{3}[-.]?\d{4}\b",
        RegexOptions.Compiled);

    private static readonly Regex SsnRegex = new(
        @"\b\d{3}[-.]?\d{2}[-.]?\d{4}\b",
        RegexOptions.Compiled);

    private static readonly Regex CreditCardRegex = new(
        @"\b(?:4[0-9]{12}(?:[0-9]{3})?|5[1-5][0-9]{14}|6(?:011|5[0-9]{2})[0-9]{12,15}|3[47][0-9]{13}|3(?:0[0-5]|[68][0-9])[0-9]{11}|(?:2131|1800|35\d{3})\d{11})\b",
        RegexOptions.Compiled);

    private static readonly Regex ApiKeyRegex = new(
        @"\b(AIza[A-Za-z0-9_-]{35}|(?:sk_|pk_|sk_live_|sk_test_)[A-Za-z0-9]{20,60}|Bearer\s+[A-Za-z0-9_-]{20,60})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex IpAddressRegex = new(
        @"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b",
        RegexOptions.Compiled);

    public PiiDetector(ILogger log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _patterns = new List<PiiPattern>();
    }

    /// <inheritdoc />
    public bool ContainsPii(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return EmailRegex.IsMatch(text) ||
               PhoneRegex.IsMatch(text) ||
               SsnRegex.IsMatch(text) ||
               CreditCardRegex.IsMatch(text) ||
               ApiKeyRegex.IsMatch(text) ||
               IpAddressRegex.IsMatch(text) ||
               _patterns.Any(p => p.Regex.IsMatch(text));
    }

    /// <inheritdoc />
    public IReadOnlyList<PiiMatch> DetectPii(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<PiiMatch>();

        var matches = new List<PiiMatch>();

        // Check standard patterns
        AddMatches(matches, EmailRegex.Matches(text), PiiType.Email);
        AddMatches(matches, PhoneRegex.Matches(text), PiiType.PhoneNumber);
        AddMatches(matches, SsnRegex.Matches(text), PiiType.Ssn);
        AddMatches(matches, CreditCardRegex.Matches(text), PiiType.CreditCard);
        AddMatches(matches, ApiKeyRegex.Matches(text), PiiType.ApiKey);
        AddMatches(matches, IpAddressRegex.Matches(text), PiiType.IpAddress);

        // Check custom patterns
        foreach (var pattern in _patterns)
        {
            AddMatches(matches, pattern.Regex.Matches(text), pattern.Type);
        }

        return matches;
    }

    /// <inheritdoc />
    public string RedactPii(string text, string mask = "***")
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var piiMatches = DetectPii(text);

        if (piiMatches.Count == 0)
            return text;

        // Sort by position (descending) to avoid offset issues when replacing
        var sortedMatches = piiMatches.OrderByDescending(m => m.StartIndex).ToList();

        var result = text.ToCharArray();
        foreach (var match in sortedMatches)
        {
            for (int i = match.StartIndex; i < match.EndIndex; i++)
            {
                if (i >= 0 && i < result.Length)
                    result[i] = mask[0];
            }
        }

        return new string(result);
    }

    /// <inheritdoc />
    public void AddCustomPattern(string regex, PiiType type, string? name = null)
    {
        _patterns.Add(new PiiPattern
        {
            Regex = new Regex(regex, RegexOptions.Compiled),
            Type = type,
            Name = name ?? $"Custom_{_patterns.Count}"
        });

        _log.Information("Added custom PII pattern: {Name} ({Type})", name, type);
    }

    /// <inheritdoc />
    public PiiStatistics GetStatistics(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new PiiStatistics();

        var matches = DetectPii(text);

        var stats = new PiiStatistics
        {
            TotalMatches = matches.Count,
            ByType = matches.GroupBy(m => m.Type)
                .ToDictionary(g => g.Key, g => g.Count())
        };

        return stats;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Private helpers
    //════════════════════════════════════════════════════════════════════

    private static void AddMatches(ICollection<PiiMatch> matches, MatchCollection regexMatches, PiiType type)
    {
        foreach (Match match in regexMatches.Cast<Match>())
        {
            matches.Add(new PiiMatch
            {
                Type = type,
                MatchText = match.Value,
                StartIndex = match.Index,
                EndIndex = match.Index + match.Length
            });
        }
    }
}

/// <summary>
/// Interface for PII detection and redaction.
/// </summary>
public interface IPiiDetector
{
    /// <summary>
    /// Checks if text contains any PII.
    /// </summary>
    bool ContainsPii(string text);

    /// <summary>
    /// Detects all PII occurrences in text.
    /// </summary>
    IReadOnlyList<PiiMatch> DetectPii(string text);

    /// <summary>
    /// Redacts PII from text by replacing with mask characters.
    /// </summary>
    string RedactPii(string text, string mask = "***");

    /// <summary>
    /// Adds a custom regex pattern for PII detection.
    /// </summary>
    void AddCustomPattern(string regex, PiiType type, string? name = null);

    /// <summary>
    /// Gets statistics about PII found in text.
    /// </summary>
    PiiStatistics GetStatistics(string text);
}

/// <summary>
/// Represents a detected PII match.
/// </summary>
public class PiiMatch
{
    /// <summary>Type of PII detected.</summary>
    public PiiType Type { get; set; }

    /// <summary>The matched text value.</summary>
    public string MatchText { get; set; } = string.Empty;

    /// <summary>Starting index in the original text.</summary>
    public int StartIndex { get; set; }

    /// <summary>Ending index (exclusive) in the original text.</summary>
    public int EndIndex { get; set; }
}

/// <summary>
/// Categories of PII.
/// </summary>
public enum PiiType
{
    Email,
    PhoneNumber,
    Ssn,
    CreditCard,
    ApiKey,
    IpAddress,
    Custom,
    Other
}

/// <summary>
/// Statistics about PII detected in text.
/// </summary>
public class PiiStatistics
{
    /// <summary>Total number of PII matches found.</summary>
    public int TotalMatches { get; set; }

    /// <summary>Breakdown by PII type.</summary>
    public Dictionary<PiiType, int> ByType { get; set; } = new();
}

/// <summary>
/// Internal representation of a custom PII pattern.
/// </file>
internal sealed class PiiPattern
{
    public required Regex Regex { get; init; }
    public required PiiType Type { get; init; }
    public required string Name { get; init; }
}
