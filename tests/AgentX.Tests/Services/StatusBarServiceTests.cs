using System;
using System.Threading;
using System.Threading.Tasks;
using AgentX.App.Services;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Documents;
using AgentX.Core.Services.Indexing;
using FluentAssertions;
using Moq;
using Xunit;

namespace AgentX.Tests.Services;

public class StatusBarServiceTests
{
    private readonly Mock<IAiService> _aiServiceMock;
    private readonly Mock<IIndexingService> _indexingServiceMock;
    private readonly Mock<IDocumentService> _documentServiceMock;
    private readonly Mock<IAiProvider> _providerMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;

    public StatusBarServiceTests()
    {
        _aiServiceMock = new Mock<IAiService>();
        _indexingServiceMock = new Mock<IIndexingService>();
        _documentServiceMock = new Mock<IDocumentService>();
        _providerMock = new Mock<IAiProvider>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        _aiServiceMock.SetupGet(a => a.ActiveProvider).Returns(_providerMock.Object);

        _serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IAiService)))
            .Returns(_aiServiceMock.Object);
        _serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IIndexingService)))
            .Returns(_indexingServiceMock.Object);
        _serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IDocumentService)))
            .Returns(_documentServiceMock.Object);
    }

    [Fact]
    public void Constructor_ThrowsOnNullServiceProvider()
    {
        var act = () => new StatusBarService(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void IsConnected_DefaultIsFalse()
    {
        var service = CreateService();
        service.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void ActiveModelName_DefaultIsEmpty()
    {
        var service = CreateService();
        service.ActiveModelName.Should().BeEmpty();
    }

    [Fact]
    public async Task PollAsync_WhenConnected_SetsIsConnectedTrue()
    {
        _providerMock.Setup(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _aiServiceMock.SetupGet(a => a.ActiveModelId).Returns("llama3.1:8b");
        _indexingServiceMock.SetupGet(i => i.IsProcessing).Returns(false);
        _documentServiceMock.Setup(d => d.GetTotalDocumentCountAsync()).ReturnsAsync(42L);

        var service = CreateService();
        StatusBarState? capturedState = null;
        service.StateChanged += (_, state) => capturedState = state;

        await service.PollAsync();

        service.IsConnected.Should().BeTrue();
        service.ActiveModelName.Should().Be("llama3.1:8b");
        capturedState.Should().NotBeNull();
        capturedState!.IsConnected.Should().BeTrue();
        capturedState.ActiveModelName.Should().Be("llama3.1:8b");
        capturedState.ConnectionStatus.Should().Contain("llama3.1:8b");
        capturedState.DocumentCount.Should().Be(42);
    }

    [Fact]
    public async Task PollAsync_WhenDisconnected_SetsIsConnectedFalse()
    {
        _providerMock.Setup(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _indexingServiceMock.SetupGet(i => i.IsProcessing).Returns(false);
        _documentServiceMock.Setup(d => d.GetTotalDocumentCountAsync()).ReturnsAsync(0L);

        var service = CreateService();
        await service.PollAsync();

        service.IsConnected.Should().BeFalse();
        service.ActiveModelName.Should().BeEmpty();
    }

    [Fact]
    public async Task PollAsync_WhenConnectedWithoutModelName_ShowsConnectedToOllama()
    {
        _providerMock.Setup(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _aiServiceMock.SetupGet(a => a.ActiveModelId).Returns((string?)null);
        _indexingServiceMock.SetupGet(i => i.IsProcessing).Returns(false);
        _documentServiceMock.Setup(d => d.GetTotalDocumentCountAsync()).ReturnsAsync(0L);

        var service = CreateService();
        StatusBarState? capturedState = null;
        service.StateChanged += (_, state) => capturedState = state;

        await service.PollAsync();

        capturedState.Should().NotBeNull();
        capturedState!.ConnectionStatus.Should().Be("Connected to Ollama");
    }

    [Fact]
    public async Task PollAsync_WhenIndexing_ReportsQueueLength()
    {
        _providerMock.Setup(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _aiServiceMock.SetupGet(a => a.ActiveModelId).Returns("test-model");
        _indexingServiceMock.SetupGet(i => i.IsProcessing).Returns(true);
        _indexingServiceMock.Setup(i => i.GetQueueLengthAsync()).ReturnsAsync(7);
        _documentServiceMock.Setup(d => d.GetTotalDocumentCountAsync()).ReturnsAsync(10L);

        var service = CreateService();
        StatusBarState? capturedState = null;
        service.StateChanged += (_, state) => capturedState = state;

        await service.PollAsync();

        capturedState.Should().NotBeNull();
        capturedState!.IsIndexing.Should().BeTrue();
        capturedState.IndexingQueueLength.Should().Be(7);
    }

    [Fact]
    public async Task PollAsync_WhenNotIndexing_ReportsFalse()
    {
        _providerMock.Setup(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _indexingServiceMock.SetupGet(i => i.IsProcessing).Returns(false);
        _documentServiceMock.Setup(d => d.GetTotalDocumentCountAsync()).ReturnsAsync(0L);

        var service = CreateService();
        StatusBarState? capturedState = null;
        service.StateChanged += (_, state) => capturedState = state;

        await service.PollAsync();

        capturedState.Should().NotBeNull();
        capturedState!.IsIndexing.Should().BeFalse();
        capturedState.IndexingQueueLength.Should().Be(0);
    }

    [Fact]
    public async Task PollAsync_WhenAiServiceThrows_GracefullyHandles()
    {
        _serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IAiService)))
            .Throws(new InvalidOperationException("Service unavailable"));

        var service = CreateService();
        StatusBarState? capturedState = null;
        service.StateChanged += (_, state) => capturedState = state;

        // Should not throw
        await service.PollAsync();

        service.IsConnected.Should().BeFalse();
        capturedState.Should().NotBeNull();
        capturedState!.ConnectionStatus.Should().Be("Ollama not detected");
    }

    [Fact]
    public async Task PollAsync_WhenDocumentCountZero_ReportsZero()
    {
        _providerMock.Setup(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _aiServiceMock.SetupGet(a => a.ActiveModelId).Returns("model");
        _indexingServiceMock.SetupGet(i => i.IsProcessing).Returns(false);
        _documentServiceMock.Setup(d => d.GetTotalDocumentCountAsync()).ReturnsAsync(0L);

        var service = CreateService();
        StatusBarState? capturedState = null;
        service.StateChanged += (_, state) => capturedState = state;

        await service.PollAsync();

        capturedState!.DocumentCount.Should().Be(0);
    }

    [Fact]
    public void Dispose_PreventsFurtherPolling()
    {
        var service = CreateService();
        service.Dispose();

        var act = async () => await service.PollAsync();
        act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void StateChanged_WhenNoSubscribers_DoesNotThrow()
    {
        _providerMock.Setup(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _aiServiceMock.SetupGet(a => a.ActiveModelId).Returns("model");
        _indexingServiceMock.SetupGet(i => i.IsProcessing).Returns(false);
        _documentServiceMock.Setup(d => d.GetTotalDocumentCountAsync()).ReturnsAsync(0L);

        var service = CreateService();

        // Poll without subscribing — should not throw
        var act = async () => await service.PollAsync();
        act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartPolling_RaisesStateChangedRepeatedlyUntilStopped()
    {
        _providerMock.Setup(p => p.CheckConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _aiServiceMock.SetupGet(a => a.ActiveModelId).Returns("polling-model");
        _indexingServiceMock.SetupGet(i => i.IsProcessing).Returns(false);
        _documentServiceMock.Setup(d => d.GetTotalDocumentCountAsync()).ReturnsAsync(0L);

        using var service = CreateService();
        var eventCount = 0;
        var twoEventsObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.StateChanged += (_, _) =>
        {
            if (Interlocked.Increment(ref eventCount) >= 2)
            {
                twoEventsObserved.TrySetResult();
            }
        };

        service.StartPolling(intervalMs: 25, initialDelayMs: 0);

        var completed = await Task.WhenAny(twoEventsObserved.Task, Task.Delay(500));
        service.StopPolling();

        completed.Should().Be(twoEventsObserved.Task);
        Volatile.Read(ref eventCount).Should().BeGreaterThanOrEqualTo(2);
    }

    private StatusBarService CreateService() => new(_serviceProviderMock.Object);
}

