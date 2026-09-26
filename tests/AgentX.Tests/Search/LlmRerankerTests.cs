using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Configuration;
using AgentX.Core.Constants;
using AgentX.Core.Search;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Search;

/// <summary>
/// Tests for <see cref="LlmReranker"/>: how the model's score response is parsed and which
/// token budget the scoring call uses.
/// </summary>
public sealed class LlmRerankerTests
{
    private static readonly ILogger Silent = new LoggerConfiguration().CreateLogger();
    private readonly Mock<IAiService> _ai = new();

    [Fact]
    public async Task RerankAsync_WrappedScoresObject_ReordersByModelScore()
    {
        RespondWith("""{"scores":[{"id":1,"score":1},{"id":2,"score":2},{"id":3,"score":10}]}""");

        var result = await new LlmReranker(_ai.Object, Silent).RerankAsync(Chunks(), "q");

        result.First().ChunkId.Should().Be(3);
    }

    [Fact]
    public async Task RerankAsync_LegacyBareArray_IsParsedToo()
    {
        // The bare-array branch was unreachable: '{' inside the first entry was found first,
        // "{...},{...}" was parsed as an object, failed, and no scores were applied.
        RespondWith("""[{"id":1,"score":1},{"id":2,"score":2},{"id":3,"score":10}]""");

        var result = await new LlmReranker(_ai.Object, Silent).RerankAsync(Chunks(), "q");

        result.First().ChunkId.Should().Be(3);
    }

    [Fact]
    public async Task RerankAsync_UsesTheConfiguredTokenBudget()
    {
        // Rag:RerankerMaxTokens was validated but the call always used the 256-token constant.
        ChatOptions? options = null;
        _ai.Setup(a => a.ChatAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ChatMessage>, string?, ChatOptions?, CancellationToken>((_, _, o, _) => options = o)
            .ReturnsAsync("""{"scores":[]}""");
        var config = new Mock<IRagConfiguration>();
        config.SetupGet(c => c.RerankerMaxTokens).Returns(800);

        await new LlmReranker(_ai.Object, null, config.Object, Silent).RerankAsync(Chunks(), "q");

        options!.MaxTokens.Should().Be(800);
    }

    [Fact]
    public async Task RerankAsync_WithoutConfiguration_FallsBackToTheConstant()
    {
        ChatOptions? options = null;
        _ai.Setup(a => a.ChatAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ChatMessage>, string?, ChatOptions?, CancellationToken>((_, _, o, _) => options = o)
            .ReturnsAsync("""{"scores":[]}""");

        await new LlmReranker(_ai.Object, Silent).RerankAsync(Chunks(), "q");

        options!.MaxTokens.Should().Be(AppConstants.RerankerMaxTokens);
    }

    private void RespondWith(string response)
        => _ai.Setup(a => a.ChatAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

    private static List<RagContextChunk> Chunks() => Enumerable.Range(1, 3)
        .Select(i => new RagContextChunk { ChunkId = i, DocumentId = i, ChunkText = $"passage {i}", RelevanceScore = 0.5f })
        .ToList();
}
