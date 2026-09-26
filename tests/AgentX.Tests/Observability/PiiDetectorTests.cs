using AgentX.Core.Observability;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Observability;

/// <summary>
/// Tests for <see cref="PiiDetector"/>'s API key coverage. The key-shaped values are built at
/// run time from a prefix and filler so no literal token-shaped strings live in the source.
/// </summary>
public sealed class PiiDetectorTests
{
    private readonly PiiDetector _detector = new(new LoggerConfiguration().CreateLogger());

    public static TheoryData<string> ModernProviderKeys => new()
    {
        "sk-" + Filler(48),                  // OpenAI (legacy user key)
        "sk-proj-" + Filler(64, "_-"),       // OpenAI project key
        "sk-svcacct-" + Filler(64, "_-"),    // OpenAI service account key
        "sk-ant-api03-" + Filler(80, "_-"),  // Anthropic
        "ghp_" + Filler(36),                 // GitHub personal access token
        "gho_" + Filler(36),                 // GitHub OAuth token
        "ghs_" + Filler(36),                 // GitHub server-to-server token
        "github_pat_" + Filler(59, "_"),     // GitHub fine-grained token
        "AKIA" + Upper(16),                  // AWS access key id
        "ASIA" + Upper(16),                  // AWS temporary access key id
    };

    [Theory]
    [MemberData(nameof(ModernProviderKeys))]
    public void DetectPii_FindsModernProviderKeyFormats(string key)
    {
        // These formats were missed, so they passed through redaction into LLM prompts.
        var matches = _detector.DetectPii($"config: token={key} end");

        matches.Should().Contain(m => m.Type == PiiType.ApiKey && m.MatchText == key);
        _detector.ContainsPii($"token {key}").Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ModernProviderKeys))]
    public void RedactPii_MasksModernProviderKeys(string key)
    {
        var redacted = _detector.RedactPii($"export KEY={key}", "*");

        redacted.Should().NotContain(key);
        redacted.Should().StartWith("export KEY=");
    }

    [Theory]
    [InlineData("scikit-learn and sk-learn are python libraries")]
    [InlineData("ask-me-anything sessions")]
    [InlineData("akiaabcdefghijklmnop is lowercase and not an AWS key id")]
    [InlineData("the ghp_ prefix alone is not a token")]
    public void DetectPii_DoesNotFlagOrdinaryText(string text)
    {
        _detector.DetectPii(text).Should().NotContain(m => m.Type == PiiType.ApiKey);
    }

    /// <summary>Filler that always ends on a letter, as real keys do.</summary>
    private static string Filler(int length, string extra = "")
    {
        var alphabet = "aB3dE5gH7jK9mN1pQ" + extra;
        return string.Concat(Enumerable.Range(0, length - 1).Select(i => alphabet[i % alphabet.Length])) + "Z";
    }

    private static string Upper(int length)
    {
        const string alphabet = "QW3RT7YU9PZX5CV2";
        return string.Concat(Enumerable.Range(0, length).Select(i => alphabet[i % alphabet.Length]));
    }
}
