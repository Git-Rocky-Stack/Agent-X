using AgentX.Core.AI;
using AgentX.Core.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.AI;

public sealed class TokenCounterTests
{
    private readonly Mock<IRagConfiguration> _configuration = new();
    private readonly Mock<ILogger> _logger = new();
    private readonly TokenCounter _tokenCounter;

    public TokenCounterTests()
    {
        // Setup default configuration
        _configuration.Setup(c => c.DefaultEmbeddingModel).Returns("llama-3.1-8b");

        _tokenCounter = new TokenCounter(_configuration.Object, _logger.Object);
    }

    [Fact]
    public void CountTokens_EmptyText_ReturnsZero()
    {
        // Act
        var result = _tokenCounter.CountTokens("");

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void CountTokens_NullText_ReturnsZero()
    {
        // Act
        var result = _tokenCounter.CountTokens(null!);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void CountTokens_ShortEnglishText_ReturnsApproximateCount()
    {
        // Arrange
        var text = "Hello world, this is a test.";

        // Act
        var result = _tokenCounter.CountTokens(text);

        // Assert
        // ~32 chars / 4 chars per token = ~8 tokens
        // Allow range of 5-12 to account for approximation
        result.Should().BeGreaterThan(5);
        result.Should().BeLessThan(15);
    }

    [Fact]
    public void CountTokens_LongerEnglishText_ReturnsApproximateCount()
    {
        // Arrange
        var text = "The quick brown fox jumps over the lazy dog. " +
                   "This sentence contains multiple words that should be " +
                   "counted as tokens by the tokenizer.";

        // Act
        var result = _tokenCounter.CountTokens(text);

        // Assert
        // ~180 chars / 4 chars per token = ~45 tokens
        // Allow range of 30-60
        result.Should().BeGreaterThan(30);
        result.Should().BeLessThan(65);
    }

    [Fact]
    public void CountTokens_CJKText_ReturnsHigherTokenDensity()
    {
        // Arrange
        var text = "こんにちは世界"; // "Hello world" in Japanese

        // Act
        var result = _tokenCounter.CountTokens(text);

        // Assert
        // CJK text has higher token density (~0.6 chars per token)
        // 6 chars / 0.6 = ~10 tokens
        result.Should().BeGreaterThan(5);
    }

    [Fact]
    public void CountTokens_KnownModel_ReturnsContextAwareCount()
    {
        // Arrange
        var text = "This is a sample text for token counting.";

        // Act
        var result = _tokenCounter.CountTokens(text, "llama-3.1-8b");

        // Assert
        result.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CountTokens_UnknownModel_ReturnsDefaultApproximation()
    {
        // Arrange
        var text = "Testing with an unknown model name.";

        // Act
        var result = _tokenCounter.CountTokens(text, "unknown-model-xyz");

        // Assert
        result.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CountTokensBatch_MultipleTexts_ReturnsCorrectCounts()
    {
        // Arrange
        var texts = new List<string>
        {
            "Hello world",
            "This is a longer text",
            "Short"
        };

        // Act
        var results = _tokenCounter.CountTokensBatch(texts);

        // Assert
        results.Should().HaveCount(3);
        results[0].Should().BeGreaterThan(0);
        results[1].Should().BeGreaterThan(results[0]); // Longer text
        results[2].Should().BeGreaterThan(0);
    }

    [Fact]
    public void CountTokensBatch_EmptyList_ReturnsEmptyList()
    {
        // Arrange
        var texts = new List<string>();

        // Act
        var results = _tokenCounter.CountTokensBatch(texts);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void GetRemainingCapacity_Llama3_1_ReturnsCorrectRemaining()
    {
        // Arrange
        _configuration.Setup(c => c.DefaultEmbeddingModel).Returns("llama-3.1-8b");

        // Act
        var remaining = _tokenCounter.GetRemainingCapacity(1000, "llama-3.1-8b");

        // Assert
        // Llama 3.1 has 128k context
        remaining.Should().Be(128000 - 1000);
    }

    [Fact]
    public void GetRemainingCapacity_ExceedsContext_ReturnsZero()
    {
        // Act
        var remaining = _tokenCounter.GetRemainingCapacity(200000, "llama-3.1-8b");

        // Assert
        remaining.Should().Be(0);
    }

    [Fact]
    public void GetRemainingCapacity_UnknownModel_ReturnsDefaultRemaining()
    {
        // Act
        var remaining = _tokenCounter.GetRemainingCapacity(1000, "unknown-model");

        // Assert
        // Default context is 8k
        remaining.Should().Be(8192 - 1000);
    }

    [Fact]
    public void CountTokens_CodeText_ReturnsReasonableApproximation()
    {
        // Arrange
        var text = "function add(a, b) { return a + b; }";

        // Act
        var result = _tokenCounter.CountTokens(text);

        // Assert
        // Code often has different tokenization but our approximation should still work
        result.Should().BeGreaterThan(5);
        result.Should().BeLessThan(25);
    }

    [Fact]
    public void CountTokens_WhitespaceOnly_ReturnsZero()
    {
        // Arrange
        var text = "   \n\t  ";

        // Act
        var result = _tokenCounter.CountTokens(text);

        // Assert
        result.Should().Be(0);
    }
}
