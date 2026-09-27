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
/// Tests for <see cref="HydeService"/>'s generation budget.
/// </summary>
public sealed class HydeServiceTests
{
    private static readonly ILogger Silent = new LoggerConfiguration().CreateLogger();
    private readonly Mock<IAiService> _ai = new();
    private ChatOptions? _options;

    public HydeServiceTests()
    {
        _ai.Setup(a => a.ChatAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<string?>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ChatMessage>, string?, ChatOptions?, CancellationToken>((_, _, o, _) => _options = o)
            .ReturnsAsync("A hypothetical answer.");
    }

    [Fact]
    public async Task GenerateHypotheticalDocumentAsync_UsesTheConfiguredTokenBudget()
    {
        // Rag:HydeMaxTokens was validated but generation always used the 512-token constant.
        var config = new Mock<IRagConfiguration>();
        config.SetupGet(c => c.HydeMaxTokens).Returns(256);

        await new HydeService(_ai.Object, null, config.Object, Silent)
            .GenerateHypotheticalDocumentAsync("What changed in the travel policy?");

        _options!.MaxTokens.Should().Be(256);
    }

    [Fact]
    public async Task GenerateHypotheticalDocumentAsync_WithoutConfiguration_FallsBackToTheConstant()
    {
        await new HydeService(_ai.Object, Silent)
            .GenerateHypotheticalDocumentAsync("What changed in the travel policy?");

        _options!.MaxTokens.Should().Be(AppConstants.HydeMaxTokens);
    }
}
