using System;
using AgentX.Core.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AgentX.Tests.Configuration;

public sealed class RagConfigurationTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // Arrange
        var options = new RagConfigurationOptions();
        var mockOptionsMonitor = new Mock<IOptionsMonitor<RagConfigurationOptions>>();
        mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(options);
        var configuration = new RagConfiguration(mockOptionsMonitor.Object);

        // Assert & Act
        configuration.DefaultTopK.Should().Be(8);
        configuration.DefaultMinScore.Should().Be(0.25f);
        configuration.MaxTopK.Should().Be(50);
        configuration.RetrievalMultiplier.Should().Be(3);

        configuration.DefaultChunkSize.Should().Be(512);
        configuration.DefaultChunkOverlap.Should().Be(50);
        configuration.MaxChunkSize.Should().Be(768);
        configuration.MinChunkSize.Should().Be(128);

        configuration.DefaultEmbeddingModel.Should().Be("all-minilm");
        configuration.DefaultEmbeddingDimensions.Should().Be(384);
        configuration.EmbeddingCacheExpirationMinutes.Should().Be(10080);
        configuration.EmbeddingBatchSize.Should().Be(32);

        configuration.SemanticWeight.Should().Be(0.68);
        configuration.LexicalWeight.Should().Be(0.22);
        configuration.RecencyWeight.Should().Be(0.10);

        configuration.MemoryDecayRate.Should().Be(0.01);
        configuration.MemoryDaysBeforeFullDecay.Should().Be(90);
        configuration.AssociativeLinkThreshold.Should().Be(0.85f);
        configuration.MaxMemoriesPerQuery.Should().Be(10);

        configuration.VectorStoreFallbackThreshold.Should().Be(10000);
        configuration.StaleRebuildFraction.Should().Be(0.05);
        configuration.HnswM.Should().Be(16);
        configuration.HnswEfConstruction.Should().Be(200);

        configuration.EnableLlmReranking.Should().BeTrue();
        configuration.RerankerMaxTokens.Should().Be(800);
        configuration.HydeMaxTokens.Should().Be(256);

        configuration.EnableResearchMode.Should().BeFalse();
        configuration.ResearchMaxWebResults.Should().Be(10);
    }

    [Fact]
    public void CustomValues_AppliedCorrectly()
    {
        // Arrange
        var options = new RagConfigurationOptions
        {
            DefaultTopK = 15,
            DefaultMinScore = 0.3f,
            MaxTopK = 100,
            DefaultChunkSize = 256,
            DefaultChunkOverlap = 25,
            DefaultEmbeddingModel = "nomic-embed-text",
            DefaultEmbeddingDimensions = 768
        };
        var mockOptionsMonitor = new Mock<IOptionsMonitor<RagConfigurationOptions>>();
        mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(options);
        var configuration = new RagConfiguration(mockOptionsMonitor.Object);

        // Assert
        configuration.DefaultTopK.Should().Be(15);
        configuration.DefaultMinScore.Should().Be(0.3f);
        configuration.DefaultChunkSize.Should().Be(256);
        configuration.DefaultChunkOverlap.Should().Be(25);
        configuration.DefaultEmbeddingModel.Should().Be("nomic-embed-text");
        configuration.DefaultEmbeddingDimensions.Should().Be(768);
    }

    [Fact]
    public void Validate_WithValidValues_DoesNotThrow()
    {
        // Arrange
        var options = new RagConfigurationOptions
        {
            DefaultTopK = 10,
            DefaultMinScore = 0.3f,
            DefaultChunkSize = 256,
            DefaultChunkOverlap = 30
        };
        var mockOptionsMonitor = new Mock<IOptionsMonitor<RagConfigurationOptions>>();
        mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(options);
        var configuration = new RagConfiguration(mockOptionsMonitor.Object);

        // Act & Assert
        Action validate = () => configuration.Validate();
        validate.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithInvalidTopK_Throws()
    {
        // Arrange
        var options = new RagConfigurationOptions { DefaultTopK = 0 };
        var mockOptionsMonitor = new Mock<IOptionsMonitor<RagConfigurationOptions>>();
        mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(options);
        var configuration = new RagConfiguration(mockOptionsMonitor.Object);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => configuration.Validate());
    }

    [Fact]
    public void Validate_WithInvalidMinScore_Throws()
    {
        // Arrange
        var options = new RagConfigurationOptions { DefaultMinScore = 1.5f };
        var mockOptionsMonitor = new Mock<IOptionsMonitor<RagConfigurationOptions>>();
        mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(options);
        var configuration = new RagConfiguration(mockOptionsMonitor.Object);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => configuration.Validate());
    }

    [Fact]
    public void Validate_WithInvalidChunkSize_Throws()
    {
        // Arrange
        var options = new RagConfigurationOptions { DefaultChunkSize = 0 };
        var mockOptionsMonitor = new Mock<IOptionsMonitor<RagConfigurationOptions>>();
        mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(options);
        var configuration = new RagConfiguration(mockOptionsMonitor.Object);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => configuration.Validate());
    }

    [Fact]
    public void Validate_WithOverlapGreaterThanChunkSize_Throws()
    {
        // Arrange
        var options = new RagConfigurationOptions
        {
            MinChunkSize = 50,
            MaxChunkSize = 200,
            DefaultChunkSize = 100,
            DefaultChunkOverlap = 150
        };
        var mockOptionsMonitor = new Mock<IOptionsMonitor<RagConfigurationOptions>>();
        mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(options);
        var configuration = new RagConfiguration(mockOptionsMonitor.Object);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => configuration.Validate());
    }

    [Fact]
    public void Weights_SumToApproximatelyOne()
    {
        // Arrange
        var options = new RagConfigurationOptions();
        var mockOptionsMonitor = new Mock<IOptionsMonitor<RagConfigurationOptions>>();
        mockOptionsMonitor.Setup(x => x.CurrentValue).Returns(options);
        var configuration = new RagConfiguration(mockOptionsMonitor.Object);

        // Act
        var sum = configuration.SemanticWeight + configuration.LexicalWeight + configuration.RecencyWeight;

        // Assert
        sum.Should().BeApproximately(1.0, 0.01);
    }
}
