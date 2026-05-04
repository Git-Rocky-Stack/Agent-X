using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Configuration;
using AgentX.Core.Search;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Search;

public sealed class RagEvaluatorTests
{
    private readonly Mock<IAiService> _aiService = new();
    private readonly RagEvaluator _evaluator;

    public RagEvaluatorTests()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        _evaluator = new RagEvaluator(_aiService.Object, logger);
    }

    [Fact]
    public async Task EvaluateAsync_ValidInput_ReturnsMetricsInRange()
    {
        // Arrange
        var question = "What is the capital of France?";
        var answer = "The capital of France is Paris.";
        var contextChunks = new List<RagContextChunk>
        {
            new() { ChunkId = 1, ChunkText = "Paris is the capital city of France.", RelevanceScore = 0.9f }
        };

        _aiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"context_relevance":8,"faithfulness":9,"answer_relevance":8}""");

        // Act
        var result = await _evaluator.EvaluateAsync(question, answer, contextChunks);

        // Assert
        result.ContextRelevance.Should().BeGreaterThanOrEqualTo(0.0);
        result.ContextRelevance.Should().BeLessThanOrEqualTo(1.0);
        result.Faithfulness.Should().BeGreaterThanOrEqualTo(0.0);
        result.Faithfulness.Should().BeLessThanOrEqualTo(1.0);
        result.AnswerRelevance.Should().BeGreaterThanOrEqualTo(0.0);
        result.AnswerRelevance.Should().BeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public async Task EvaluateAsync_PerfectResponse_ReturnsHighScores()
    {
        // Arrange
        var question = "What is 2+2?";
        var answer = "2+2 equals 4.";
        var contextChunks = new List<RagContextChunk>
        {
            new() { ChunkId = 1, ChunkText = "2+2=4", RelevanceScore = 1.0f }
        };

        _aiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"context_relevance":10,"faithfulness":10,"answer_relevance":10}""");

        // Act
        var result = await _evaluator.EvaluateAsync(question, answer, contextChunks);

        // Assert
        result.ContextRelevance.Should().BeApproximately(1.0, 0.01);
        result.Faithfulness.Should().BeApproximately(1.0, 0.01);
        result.AnswerRelevance.Should().BeApproximately(1.0, 0.01);
        result.OverallScore.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public async Task EvaluateAsync_PoorResponse_ReturnsLowScores()
    {
        // Arrange
        var question = "What is the capital of France?";
        var answer = "The capital of Germany is Berlin."; // Wrong answer
        var contextChunks = new List<RagContextChunk>
        {
            new() { ChunkId = 1, ChunkText = "Paris is the capital of France.", RelevanceScore = 0.9f }
        };

        _aiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"context_relevance":2,"faithfulness":1,"answer_relevance":2}""");

        // Act
        var result = await _evaluator.EvaluateAsync(question, answer, contextChunks);

        // Assert
        result.ContextRelevance.Should().BeLessThan(0.5);
        result.Faithfulness.Should().BeLessThan(0.5);
        result.AnswerRelevance.Should().BeLessThan(0.5);
    }

    [Fact]
    public async Task EvaluateAsync_LLMFailure_ReturnsDefaultScores()
    {
        // Arrange
        var question = "Test question";
        var answer = "Test answer";
        var contextChunks = new List<RagContextChunk>
        {
            new() { ChunkId = 1, ChunkText = "Test context", RelevanceScore = 0.5f }
        };

        _aiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM unavailable"));

        // Act
        var result = await _evaluator.EvaluateAsync(question, answer, contextChunks);

        // Assert
        result.ContextRelevance.Should().Be(0.5);
        result.Faithfulness.Should().Be(0.5);
        result.AnswerRelevance.Should().Be(0.5);
    }

    [Fact]
    public async Task EvaluateAsync_InvalidJson_ReturnsDefaultScores()
    {
        // Arrange
        var question = "Test question";
        var answer = "Test answer";
        var contextChunks = new List<RagContextChunk>
        {
            new() { ChunkId = 1, ChunkText = "Test context", RelevanceScore = 0.5f }
        };

        _aiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("This is not valid JSON at all");

        // Act
        var result = await _evaluator.EvaluateAsync(question, answer, contextChunks);

        // Assert
        result.ContextRelevance.Should().Be(0.5);
        result.Faithfulness.Should().Be(0.5);
        result.AnswerRelevance.Should().Be(0.5);
    }

    [Fact]
    public async Task EvaluateAsync_EmptyQuestion_ThrowsArgumentException()
    {
        // Arrange
        var contextChunks = new List<RagContextChunk>
        {
            new() { ChunkId = 1, ChunkText = "Context", RelevanceScore = 0.5f }
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _evaluator.EvaluateAsync("", "Answer", contextChunks));
    }

    [Fact]
    public async Task EvaluateAsync_EmptyAnswer_ThrowsArgumentException()
    {
        // Arrange
        var contextChunks = new List<RagContextChunk>
        {
            new() { ChunkId = 1, ChunkText = "Context", RelevanceScore = 0.5f }
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _evaluator.EvaluateAsync("Question", "", contextChunks));
    }

    [Fact]
    public async Task EvaluateAsync_EmptyContextChunks_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _evaluator.EvaluateAsync("Question", "Answer", new List<RagContextChunk>()));
    }

    [Fact]
    public async Task EvaluateAsync_NullContextChunks_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _evaluator.EvaluateAsync("Question", "Answer", null!));
    }

    [Fact]
    public async Task EvaluateAsync_MultipleChunks_TruncatesForPrompt()
    {
        // Arrange
        var question = "What is the summary?";
        var answer = "Summary text.";
        var contextChunks = Enumerable.Range(1, 20)
            .Select(i => new RagContextChunk
            {
                ChunkId = i,
                ChunkText = $"Context chunk {i} with some text content.",
                RelevanceScore = 0.8f
            })
            .ToList<RagContextChunk>();

        _aiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"context_relevance":7,"faithfulness":8,"answer_relevance":7}""");

        // Act
        var result = await _evaluator.EvaluateAsync(question, answer, contextChunks);

        // Assert
        result.ContextRelevance.Should().BeGreaterThan(0);
        _aiService.Verify(s => s.ChatAsync(
            It.IsAny<IReadOnlyList<ChatMessage>>(),
            It.IsAny<string>(),
            It.IsAny<ChatOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EvaluateAsync_CallsLLMWithCorrectParameters()
    {
        // Arrange
        var question = "Test question?";
        var answer = "Test answer.";
        var contextChunks = new List<RagContextChunk>
        {
            new() { ChunkId = 1, ChunkText = "Test context", RelevanceScore = 0.9f }
        };

        _aiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"context_relevance":5,"faithfulness":5,"answer_relevance":5}""");

        // Act
        await _evaluator.EvaluateAsync(question, answer, contextChunks);

        // Assert
        _aiService.Verify(s => s.ChatAsync(
            It.Is<IReadOnlyList<ChatMessage>>(msg => msg.Count == 1),
            It.IsAny<string>(),
            It.Is<ChatOptions>(opts => opts.Temperature == 0.0f),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EvaluateAsync_OverallScore_CalculatesWeightedAverage()
    {
        // Arrange
        var question = "Test";
        var answer = "Test";
        var contextChunks = new List<RagContextChunk>
        {
            new() { ChunkId = 1, ChunkText = "Test", RelevanceScore = 0.5f }
        };

        _aiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"context_relevance":8,"faithfulness":6,"answer_relevance":10}""");

        // Act
        var result = await _evaluator.EvaluateAsync(question, answer, contextChunks);

        // Assert
        // Overall = 0.3 * 0.8 + 0.4 * 0.6 + 0.3 * 1.0 = 0.24 + 0.24 + 0.3 = 0.78
        result.OverallScore.Should().BeApproximately(0.78, 0.01);
    }

    [Fact]
    public async Task EvaluateAsync_ScoresAboveTen_ClampedToOne()
    {
        // Arrange
        var question = "Test";
        var answer = "Test";
        var contextChunks = new List<RagContextChunk>
        {
            new() { ChunkId = 1, ChunkText = "Test", RelevanceScore = 0.5f }
        };

        _aiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"context_relevance":15,"faithfulness":12,"answer_relevance":20}""");

        // Act
        var result = await _evaluator.EvaluateAsync(question, answer, contextChunks);

        // Assert
        result.ContextRelevance.Should().Be(1.0);
        result.Faithfulness.Should().Be(1.0);
        result.AnswerRelevance.Should().Be(1.0);
    }

    [Fact]
    public async Task EvaluateAsync_ScoresBelowZero_ClampedToZero()
    {
        // Arrange
        var question = "Test";
        var answer = "Test";
        var contextChunks = new List<RagContextChunk>
        {
            new() { ChunkId = 1, ChunkText = "Test", RelevanceScore = 0.5f }
        };

        _aiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"context_relevance":-5,"faithfulness":0,"answer_relevance":-2}""");

        // Act
        var result = await _evaluator.EvaluateAsync(question, answer, contextChunks);

        // Assert
        result.ContextRelevance.Should().Be(0.0);
        result.Faithfulness.Should().Be(0.0);
        result.AnswerRelevance.Should().Be(0.0);
    }

    [Fact]
    public async Task EvaluateAsync_PartialJson_ParsesAvailableFields()
    {
        // Arrange
        var question = "Test";
        var answer = "Test";
        var contextChunks = new List<RagContextChunk>
        {
            new() { ChunkId = 1, ChunkText = "Test", RelevanceScore = 0.5f }
        };

        _aiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"context_relevance":7,"faithfulness":8}"""); // Missing answer_relevance

        // Act
        var result = await _evaluator.EvaluateAsync(question, answer, contextChunks);

        // Assert
        // Should return defaults when fields are missing
        result.ContextRelevance.Should().Be(0.7);
        result.Faithfulness.Should().Be(0.8);
        result.AnswerRelevance.Should().Be(0.5); // Default
    }

    [Fact]
    public async Task EvaluateAsync_CancellationRequested_PropagatesCancellation()
    {
        // Arrange
        var question = "Test";
        var answer = "Test";
        var contextChunks = new List<RagContextChunk>
        {
            new() { ChunkId = 1, ChunkText = "Test", RelevanceScore = 0.5f }
        };

        var cts = new CancellationTokenSource();
        cts.Cancel();

        _aiService
            .Setup(s => s.ChatAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<string>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _evaluator.EvaluateAsync(question, answer, contextChunks, cts.Token));
    }
}
