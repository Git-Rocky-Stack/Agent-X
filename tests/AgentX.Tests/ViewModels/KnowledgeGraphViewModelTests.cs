using AgentX.App.ViewModels;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Intelligence.Models;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.ViewModels;

public sealed class KnowledgeGraphViewModelTests
{
    private readonly Mock<IKnowledgeGraphService> _graphService = new();

    [Theory]
    [InlineData(1, 1, "1 node, 1 connection")]
    [InlineData(3, 2, "3 nodes, 2 connections")]
    [InlineData(1, 0, "1 node, 0 connections")]
    public async Task InitializeAsync_summarizes_the_graph_from_the_resources(int nodes, int edges, string expected)
    {
        _graphService
            .Setup(service => service.BuildGraphAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeGraphData
            {
                Nodes = Enumerable.Range(1, nodes).Select(id => new GraphNode { Id = $"n{id}", Label = $"Node {id}" }).ToList(),
                Edges = Enumerable.Range(1, edges).Select(id => new GraphEdge { SourceId = "n1", TargetId = $"n{id}" }).ToList()
            });
        var viewModel = new KnowledgeGraphViewModel(_graphService.Object, EnglishResources.Create());
        viewModel.StatusMessage.Should().Be("Loading graph...");

        await viewModel.InitializeAsync();

        viewModel.StatusMessage.Should().Be(expected);
    }

    [Fact]
    public async Task InitializeAsync_reports_a_failed_build()
    {
        _graphService
            .Setup(service => service.BuildGraphAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("layout failed"));
        var viewModel = new KnowledgeGraphViewModel(_graphService.Object, EnglishResources.Create());

        await viewModel.InitializeAsync();

        viewModel.StatusMessage.Should().Be("Failed to build graph");
        viewModel.IsLoading.Should().BeFalse();
    }
}
