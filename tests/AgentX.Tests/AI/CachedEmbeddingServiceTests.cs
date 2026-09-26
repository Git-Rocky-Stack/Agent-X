using AgentX.Core.AI;
using AgentX.Core.Configuration;
using AgentX.Core.Mathematics;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.AI;

public sealed class CachedEmbeddingServiceTests
{
    private readonly Mock<IEmbeddingService> _innerService = new();
    private readonly Mock<IRagConfiguration> _configuration = new();
    private readonly ILogger _logger = Log.ForContext<CachedEmbeddingService>();

    public CachedEmbeddingServiceTests()
    {
        // Setup default configuration
        _configuration.Setup(c => c.EmbeddingCacheExpirationMinutes).Returns(60);
        _configuration.Setup(c => c.DefaultEmbeddingModel).Returns("test-model");

        // Setup inner service
        _innerService.Setup(s => s.Dimensions).Returns(384);
        _innerService.Setup(s => s.ModelName).Returns("test-model");
    }

    [Fact]
    public async Task EmbedAsync_CachesResult()
    {
        // Arrange
        var expectedEmbedding = new float[] { 0.1f, 0.2f, 0.3f };
        _innerService
            .Setup(s => s.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedEmbedding);

        var service = new CachedEmbeddingService(_innerService.Object, _configuration.Object, _logger);

        // Act
        var result1 = await service.EmbedAsync("test text");
        var result2 = await service.EmbedAsync("test text");

        // Assert
        result1.Should().BeSameAs(result2);
        _innerService.Verify(s => s.EmbedAsync("test text", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmbedAsync_DifferentTexts_CachesSeparately()
    {
        // Arrange
        var embedding1 = new float[] { 0.1f, 0.2f, 0.3f };
        var embedding2 = new float[] { 0.4f, 0.5f, 0.6f };
        _innerService
            .Setup(s => s.EmbedAsync("text1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding1);
        _innerService
            .Setup(s => s.EmbedAsync("text2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding2);

        var service = new CachedEmbeddingService(_innerService.Object, _configuration.Object, _logger);

        // Act
        var result1a = await service.EmbedAsync("text1");
        var result1b = await service.EmbedAsync("text1");
        var result2a = await service.EmbedAsync("text2");
        var result2b = await service.EmbedAsync("text2");

        // Assert
        result1a.Should().BeSameAs(result1b);
        result2a.Should().BeSameAs(result2b);
        result1a.Should().NotBeSameAs(result2a);
    }

    [Fact]
    public void GetStatistics_InitialValues_AreZero()
    {
        // Arrange
        var service = new CachedEmbeddingService(_innerService.Object, _configuration.Object, _logger);

        // Act
        var stats = service.GetStatistics();

        // Assert
        stats.Total.Should().Be(0);
        stats.Hits.Should().Be(0);
        stats.Misses.Should().Be(0);
        stats.HitRate.Should().Be(0);
    }

    [Fact]
    public async Task GetStatistics_AfterCacheHit_ReflectsHit()
    {
        // Arrange
        _innerService
            .Setup(s => s.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        var service = new CachedEmbeddingService(_innerService.Object, _configuration.Object, _logger);

        // Act
        await service.EmbedAsync("text");
        await service.EmbedAsync("text"); // Cache hit
        var stats = service.GetStatistics();

        // Assert
        stats.Total.Should().Be(2);
        stats.Hits.Should().Be(1);
        stats.Misses.Should().Be(1);
        stats.HitRate.Should().BeApproximately(0.5, 0.01);
    }

    [Fact]
    public async Task ClearCache_RemovesAllEntries()
    {
        // Arrange
        _innerService
            .Setup(s => s.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        var service = new CachedEmbeddingService(_innerService.Object, _configuration.Object, _logger);

        await service.EmbedAsync("text1");
        await service.EmbedAsync("text2");

        // Act
        service.ClearCache();
        var stats = service.GetStatistics();

        // Assert
        stats.Total.Should().Be(2); // Stats not cleared
        service.GetStatistics().Hits.Should().Be(0); // But cache is empty
    }

    [Fact]
    public void Dimensions_PropagatesFromInner()
    {
        // Arrange
        _innerService.Setup(s => s.Dimensions).Returns(768);

        var service = new CachedEmbeddingService(_innerService.Object, _configuration.Object, _logger);

        // Act
        var result = service.Dimensions;

        // Assert
        result.Should().Be(768);
    }

    [Fact]
    public void ModelName_PropagatesFromInner()
    {
        // Arrange
        _innerService.Setup(s => s.ModelName).Returns("custom-model");

        var service = new CachedEmbeddingService(_innerService.Object, _configuration.Object, _logger);

        // Act
        var result = service.ModelName;

        // Assert
        result.Should().Be("custom-model");
    }

    [Fact]
    public async Task EmbedAsync_EmptyText_ThrowsArgumentException()
    {
        // Arrange
        var service = new CachedEmbeddingService(_innerService.Object, _configuration.Object, _logger);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.EmbedAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => service.EmbedAsync("   "));
    }

    [Fact]
    public async Task EmbedAsync_NullText_ThrowsArgumentException()
    {
        // Arrange
        var service = new CachedEmbeddingService(_innerService.Object, _configuration.Object, _logger);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.EmbedAsync(null!));
    }

    [Fact]
    public async Task EmbedAsync_after_a_model_change_never_returns_the_old_models_vector()
    {
        var version = "ollama:all-minilm:384";
        _innerService.Setup(s => s.ModelVersion).Returns(() => version);
        _innerService
            .Setup(s => s.EmbedAsync("query", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => version.EndsWith("384") ? new float[384] : new float[3072]);

        var service = new CachedEmbeddingService(_innerService.Object, _configuration.Object, _logger);

        (await service.EmbedAsync("query")).Should().HaveCount(384);
        (await service.EmbedAsync("query")).Should().HaveCount(384, "same model: served from cache");

        version = "local:llama-3.2-3b-instruct-q4_k_m.gguf:3072";

        (await service.EmbedAsync("query")).Should().HaveCount(3072, "a new embedding space must be re-embedded");
        _innerService.Verify(s => s.EmbedAsync("query", It.IsAny<CancellationToken>()), Times.Exactly(2));
        service.GetStatistics().CacheSize.Should().Be(1, "vectors of the previous model are dropped");
    }

    [Fact]
    public async Task Cache_is_bounded_and_evicts_the_least_recently_used_entry()
    {
        _innerService.Setup(s => s.ModelVersion).Returns("ollama:all-minilm:384");
        _innerService
            .Setup(s => s.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) => new[] { (float)text.Length });

        var service = new CachedEmbeddingService(_innerService.Object, _configuration.Object, _logger, maxEntries: 3);

        await service.EmbedAsync("a");
        await service.EmbedAsync("bb");
        await service.EmbedAsync("ccc");
        await service.EmbedAsync("a");      // touch "a" so "bb" becomes the oldest
        await service.EmbedAsync("dddd");   // evicts "bb"

        service.GetStatistics().CacheSize.Should().Be(3);

        await service.EmbedAsync("a");
        await service.EmbedAsync("bb");
        _innerService.Verify(s => s.EmbedAsync("a", It.IsAny<CancellationToken>()), Times.Once);
        _innerService.Verify(s => s.EmbedAsync("bb", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Batch_requests_are_counted_so_the_hit_rate_stays_a_fraction()
    {
        _innerService.Setup(s => s.ModelVersion).Returns("ollama:all-minilm:384");
        _innerService
            .Setup(s => s.EmbedBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> texts, CancellationToken _) => texts.Select(t => new[] { (float)t.Length }).ToList());

        var service = new CachedEmbeddingService(_innerService.Object, _configuration.Object, _logger);

        await service.EmbedBatchAsync(new[] { "one", "two" });
        await service.EmbedBatchAsync(new[] { "one", "two", "three" });

        var stats = service.GetStatistics();
        stats.Total.Should().Be(5);
        stats.Hits.Should().Be(2);
        stats.Misses.Should().Be(3);
        stats.HitRate.Should().BeApproximately(0.4, 0.001);
    }

    [Fact]
    public async Task ClearCacheForModel_removes_only_that_models_entries()
    {
        var version = "ollama:all-minilm:384";
        _innerService.Setup(s => s.ModelVersion).Returns(() => version);
        _innerService
            .Setup(s => s.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 1f });

        var service = new CachedEmbeddingService(_innerService.Object, _configuration.Object, _logger);
        await service.EmbedAsync("x");
        await service.EmbedAsync("y");

        service.ClearCacheForModel("some-other:model:1");
        service.GetStatistics().CacheSize.Should().Be(2);

        service.ClearCacheForModel(version);
        service.GetStatistics().CacheSize.Should().Be(0);
    }
}
