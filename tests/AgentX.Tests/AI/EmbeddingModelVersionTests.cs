using AgentX.Core.AI;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.AI;

public sealed class EmbeddingModelVersionTests
{
    [Fact]
    public void FromModel_CreatesCorrectVersion()
    {
        // Act
        var version = EmbeddingModelVersion.FromModel("all-minilm", 384);

        // Assert
        version.ModelName.Should().Be("all-minilm");
        version.Version.Should().Be("1.0");
        version.Dimensions.Should().Be(384);
        version.FullVersion.Should().Be("all-minilm:1.0");
    }

    [Fact]
    public void Legacy_CreatesLegacyMarker()
    {
        // Act
        var version = EmbeddingModelVersion.Legacy(384);

        // Assert
        version.ModelName.Should().Be("legacy");
        version.Version.Should().Be("0.0");
        version.Dimensions.Should().Be(384);
        version.IsLegacy.Should().BeTrue();
    }

    [Fact]
    public void Parse_ValidFormat_ReturnsVersion()
    {
        // Arrange
        var versionString = "all-minilm:1.5";

        // Act
        var version = EmbeddingModelVersion.Parse(versionString);

        // Assert
        version.Should().NotBeNull();
        version!.ModelName.Should().Be("all-minilm");
        version.Version.Should().Be("1.5");
        version.Dimensions.Should().Be(384); // default
    }

    [Fact]
    public void Parse_WithDimensions_ReturnsVersion()
    {
        // Arrange
        var versionString = "nomic-embed-text:2.0:768";

        // Act
        var version = EmbeddingModelVersion.Parse(versionString, 768);

        // Assert
        version.Should().NotBeNull();
        version!.ModelName.Should().Be("nomic-embed-text");
        version.Version.Should().Be("2.0");
        version.Dimensions.Should().Be(768);
    }

    [Fact]
    public void Parse_Null_ReturnsNull()
    {
        // Act
        var result = EmbeddingModelVersion.Parse(null);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Parse_Empty_ReturnsNull()
    {
        // Act
        var result = EmbeddingModelVersion.Parse("");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Parse_ModelOnly_UsesDefaults()
    {
        // Act
        var version = EmbeddingModelVersion.Parse("custom-model", 512);

        // Assert
        version.Should().NotBeNull();
        version!.ModelName.Should().Be("custom-model");
        version.Version.Should().Be("1.0");
        version.Dimensions.Should().Be(512);
    }

    [Fact]
    public void IsCompatibleWith_SameModelAndDimensions_ReturnsTrue()
    {
        // Arrange
        var v1 = EmbeddingModelVersion.FromModel("all-minilm", 384);
        var v2 = EmbeddingModelVersion.FromModel("all-minilm", 384);

        // Act
        var result = v1.IsCompatibleWith(v2);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsCompatibleWith_DifferentModel_ReturnsFalse()
    {
        // Arrange
        var v1 = EmbeddingModelVersion.FromModel("all-minilm", 384);
        var v2 = EmbeddingModelVersion.FromModel("nomic-embed-text", 768);

        // Act
        var result = v1.IsCompatibleWith(v2);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsCompatibleWith_DifferentDimensions_ReturnsFalse()
    {
        // Arrange
        var v1 = EmbeddingModelVersion.FromModel("all-minilm", 384);
        var v2 = EmbeddingModelVersion.FromModel("all-minilm", 768);

        // Act
        var result = v1.IsCompatibleWith(v2);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsCompatibleWith_Null_ReturnsFalse()
    {
        // Arrange
        var v1 = EmbeddingModelVersion.FromModel("all-minilm", 384);

        // Act
        var result = v1.IsCompatibleWith(null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Equals_SameVersion_ReturnsTrue()
    {
        // Arrange
        var v1 = EmbeddingModelVersion.FromModel("all-minilm", 384);
        var v2 = EmbeddingModelVersion.FromModel("all-minilm", 384);

        // Act
        var result = v1.Equals(v2);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_SameVersion_ReturnsSameHash()
    {
        // Arrange
        var v1 = EmbeddingModelVersion.FromModel("all-minilm", 384);
        var v2 = EmbeddingModelVersion.FromModel("all-minilm", 384);

        // Act
        var hash1 = v1.GetHashCode();
        var hash2 = v2.GetHashCode();

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ToString_ReturnsFullVersion()
    {
        // Arrange
        var version = EmbeddingModelVersion.FromModel("all-minilm", 384);

        // Act
        var result = version.ToString();

        // Assert
        result.Should().Be("all-minilm:1.0");
    }
}
