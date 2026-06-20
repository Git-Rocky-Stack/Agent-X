using AgentX.Core.Mathematics;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Mathematics;

public sealed class VectorMathTests
{
    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        // Arrange
        var a = new float[] { 1.0f, 2.0f, 3.0f };
        var b = new float[] { 1.0f, 2.0f, 3.0f };

        // Act
        var result = VectorMath.CosineSimilarity(a, b);

        // Assert
        result.Should().BeApproximately(1.0f, 0.0001f);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        // Arrange
        var a = new float[] { 1.0f, 0.0f, 0.0f };
        var b = new float[] { 0.0f, 1.0f, 0.0f };

        // Act
        var result = VectorMath.CosineSimilarity(a, b);

        // Assert
        result.Should().BeApproximately(0.0f, 0.0001f);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_ReturnsNegativeOne()
    {
        // Arrange
        var a = new float[] { 1.0f, 2.0f, 3.0f };
        var b = new float[] { -1.0f, -2.0f, -3.0f };

        // Act
        var result = VectorMath.CosineSimilarity(a, b);

        // Assert
        result.Should().BeApproximately(-1.0f, 0.0001f);
    }

    [Fact]
    public void CosineSimilarity_DifferentDimensions_ThrowsArgumentException()
    {
        // Arrange
        var a = new float[] { 1.0f, 2.0f };
        var b = new float[] { 1.0f, 2.0f, 3.0f };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => VectorMath.CosineSimilarity(a, b));
    }

    [Fact]
    public void CosineSimilarity_EmptyVectors_ReturnsZero()
    {
        // Arrange
        var a = Array.Empty<float>();
        var b = Array.Empty<float>();

        // Act
        var result = VectorMath.CosineSimilarity(a, b);

        // Assert
        result.Should().Be(0.0f);
    }

    [Fact]
    public void Clamp01_ValueAboveRange_ReturnsOne()
    {
        // Act
        var result = VectorMath.Clamp01(1.5f);

        // Assert
        result.Should().Be(1.0f);
    }

    [Fact]
    public void Clamp01_ValueBelowRange_ReturnsZero()
    {
        // Act
        var result = VectorMath.Clamp01(-0.5f);

        // Assert
        result.Should().Be(0.0f);
    }

    [Fact]
    public void Clamp01_ValueInRange_ReturnsSame()
    {
        // Act
        var result = VectorMath.Clamp01(0.5f);

        // Assert
        result.Should().Be(0.5f);
    }

    [Fact]
    public void Magnitude_SimpleVector_ReturnsCorrectLength()
    {
        // Arrange
        var vector = new float[] { 3.0f, 4.0f }; // 3-4-5 triangle

        // Act
        var result = VectorMath.Magnitude(vector);

        // Assert
        result.Should().BeApproximately(5.0, 0.0001);
    }

    [Fact]
    public void DotProduct_SimpleVectors_ReturnsCorrectProduct()
    {
        // Arrange
        var a = new float[] { 1.0f, 2.0f, 3.0f };
        var b = new float[] { 4.0f, 5.0f, 6.0f };

        // Act
        var result = VectorMath.DotProduct(a, b);

        // Assert
        result.Should().Be(1 * 4 + 2 * 5 + 3 * 6); // 32
    }

    [Fact]
    public void Normalize_UnitVector_ReturnsSameVector()
    {
        // Arrange
        var vector = new float[] { 1.0f, 0.0f, 0.0f };

        // Act
        var originalMag = VectorMath.Normalize(vector.AsSpan());

        // Assert
        vector[0].Should().BeApproximately(1.0f, 0.0001f);
        originalMag.Should().Be(1.0f);
    }

    [Fact]
    public void EuclideanDistance_SamePoints_ReturnsZero()
    {
        // Arrange
        var a = new float[] { 1.0f, 2.0f, 3.0f };
        var b = new float[] { 1.0f, 2.0f, 3.0f };

        // Act
        var result = VectorMath.EuclideanDistance(a, b);

        // Assert
        result.Should().Be(0.0);
    }

    [Fact]
    public void EuclideanDistance_DifferentPoints_ReturnsCorrectDistance()
    {
        // Arrange
        var a = new float[] { 0.0f, 0.0f };
        var b = new float[] { 3.0f, 4.0f };

        // Act
        var result = VectorMath.EuclideanDistance(a, b);

        // Assert
        result.Should().BeApproximately(5.0, 0.0001f); // 3-4-5 triangle
    }

    [Fact]
    public void ManhattanDistance_DifferentPoints_ReturnsCorrectDistance()
    {
        // Arrange
        var a = new float[] { 0.0f, 0.0f };
        var b = new float[] { 3.0f, 4.0f };

        // Act
        var result = VectorMath.ManhattanDistance(a, b);

        // Assert
        result.Should().Be(7.0); // 3 + 4
    }

    [Fact]
    public void CosineSimilarityFromMagnitudes_ValidInput_ReturnsCorrectSimilarity()
    {
        // Arrange
        double dot = 6.0;
        double magA = 3.0;
        double magB = 2.0;

        // Act
        var result = VectorMath.CosineSimilarityFromMagnitudes(dot, magA, magB);

        // Assert
        result.Should().Be(1.0f); // 6 / (3*2) = 1
    }

    [Fact]
    public void CosineSimilarityFromMagnitudes_ZeroMagnitude_ReturnsZero()
    {
        // Arrange
        double dot = 6.0;
        double magA = 0.0;
        double magB = 2.0;

        // Act
        var result = VectorMath.CosineSimilarityFromMagnitudes(dot, magA, magB);

        // Assert
        result.Should().Be(0.0f);
    }

    [Fact]
    public void Clamp_GenericClamp_ClampsCorrectly()
    {
        // Act
        var result1 = VectorMath.Clamp(1.5f, 0.0f, 1.0f);
        var result2 = VectorMath.Clamp(-0.5f, 0.0f, 1.0f);
        var result3 = VectorMath.Clamp(0.5f, 0.0f, 1.0f);

        // Assert
        result1.Should().Be(1.0f);
        result2.Should().Be(0.0f);
        result3.Should().Be(0.5f);
    }

    [Fact]
    public void Min_Max_ReturnsCorrectValues()
    {
        // Act
        var minResult = VectorMath.Min(5, 3);
        var maxResult = VectorMath.Max(5, 3);

        // Assert
        minResult.Should().Be(3);
        maxResult.Should().Be(5);
    }

    [Fact]
    public void Sqrt_ReturnsCorrectSquareRoot()
    {
        // Act
        var result = VectorMath.Sqrt(9.0f);

        // Assert
        result.Should().BeApproximately(3.0f, 0.0001f);
    }

    [Fact]
    public void Round_RoundsCorrectly()
    {
        // Act
        var result1 = VectorMath.Round(3.7f);
        var result2 = VectorMath.Round(3.2f);

        // Assert
        result1.Should().Be(4.0f);
        result2.Should().Be(3.0f);
    }
}
