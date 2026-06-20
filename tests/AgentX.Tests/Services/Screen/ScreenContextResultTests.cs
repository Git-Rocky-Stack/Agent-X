using AgentX.Core.Services.Screen;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Services.Screen;

/// <summary>
/// Unit tests for <see cref="ScreenContextResult"/>.
/// Tests cover the <see cref="ScreenContextResult.IsEmpty"/> computed property
/// and default value behaviour.
/// </summary>
public sealed class ScreenContextResultTests
{
    // ── IsEmpty ───────────────────────────────────────────────────────────────

    [Fact]
    public void IsEmpty_WhenBothFieldsAreEmpty_ReturnsTrue()
    {
        // Arrange
        var result = new ScreenContextResult();

        // Assert
        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void IsEmpty_WhenOcrTextHasContent_ReturnsFalse()
    {
        // Arrange
        var result = new ScreenContextResult
        {
            OcrText = "Hello world",
            ActiveWindowTitle = string.Empty,
        };

        // Assert
        result.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void IsEmpty_WhenActiveWindowTitleHasContent_ReturnsFalse()
    {
        // Arrange
        var result = new ScreenContextResult
        {
            OcrText = string.Empty,
            ActiveWindowTitle = "Visual Studio",
        };

        // Assert
        result.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void IsEmpty_WhenBothFieldsHaveContent_ReturnsFalse()
    {
        // Arrange
        var result = new ScreenContextResult
        {
            OcrText = "Some OCR text",
            ActiveWindowTitle = "Notepad",
        };

        // Assert
        result.IsEmpty.Should().BeFalse();
    }

    // ── Whitespace handling ────────────────────────────────────────────────────

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("  \n  ")]
    public void IsEmpty_WhenOcrTextIsWhitespaceOnly_ReturnsTrueIfTitleAlsoEmpty(string whitespace)
    {
        // Arrange
        var result = new ScreenContextResult
        {
            OcrText = whitespace,
            ActiveWindowTitle = string.Empty,
        };

        // Assert
        result.IsEmpty.Should().BeTrue();
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    public void IsEmpty_WhenActiveWindowTitleIsWhitespaceOnly_ReturnsTrueIfOcrAlsoEmpty(string whitespace)
    {
        // Arrange
        var result = new ScreenContextResult
        {
            OcrText = string.Empty,
            ActiveWindowTitle = whitespace,
        };

        // Assert
        result.IsEmpty.Should().BeTrue();
    }

    // ── Default values ──────────────────────────────────────────────────────────

    [Fact]
    public void DefaultInstance_HasEmptyOcrText()
    {
        var result = new ScreenContextResult();
        result.OcrText.Should().BeEmpty();
    }

    [Fact]
    public void DefaultInstance_HasEmptyActiveWindowTitle()
    {
        var result = new ScreenContextResult();
        result.ActiveWindowTitle.Should().BeEmpty();
    }

    [Fact]
    public void DefaultInstance_HasUtcTimestamp()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        var result = new ScreenContextResult();

        // Assert
        var after = DateTime.UtcNow.AddSeconds(1);
        result.CapturedAtUtc.Should().BeOnOrAfter(before);
        result.CapturedAtUtc.Should().BeOnOrBefore(after);
    }

    // ── IdeContext ────────────────────────────────────────────────────────────────

    [Fact]
    public void IsEmpty_WithIdeContext_ReturnsFalse()
    {
        // Arrange
        var result = new ScreenContextResult
        {
            IdeContext = new IdeDetection { IdeName = "VS Code" },
        };

        // Assert
        result.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void IsEmpty_WithOcrTextAndIdeContext_ReturnsFalse()
    {
        // Arrange
        var result = new ScreenContextResult
        {
            OcrText = "console.log('hello')",
            IdeContext = new IdeDetection { IdeName = "Visual Studio" },
        };

        // Assert
        result.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void IsEmpty_WithoutIdeContext_ReturnsTrue_WhenOtherFieldsEmpty()
    {
        // Arrange — default instance has no IdeContext and empty strings
        var result = new ScreenContextResult();

        // Assert
        result.IsEmpty.Should().BeTrue();
        result.IdeContext.Should().BeNull();
    }

    // ── Init-set values ─────────────────────────────────────────────────────────

    [Fact]
    public void CapturedAtUtc_CanBeSetToSpecificTime()
    {
        // Arrange
        var specificTime = new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var result = new ScreenContextResult
        {
            OcrText = "test",
            ActiveWindowTitle = "window",
            CapturedAtUtc = specificTime,
        };

        // Assert
        result.CapturedAtUtc.Should().Be(specificTime);
    }
}
