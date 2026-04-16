using AgentX.Core.Services.Screen;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Screen;

/// <summary>
/// Unit tests for <see cref="ScreenCaptureService"/>.
/// <para>
/// Because <see cref="ScreenCaptureService"/> uses P/Invoke for screen capture,
/// these tests focus on the settings-gated behaviour (disabled/enabled) and
/// cancellation handling rather than actual pixel capture, which requires a
/// live desktop session.
/// </para>
/// </summary>
public sealed class ScreenCaptureServiceTests
{
    private readonly Mock<ISettingsService> _mockSettings;
    private readonly ILogger _logger;

    public ScreenCaptureServiceTests()
    {
        _mockSettings = new Mock<ISettingsService>();
        _logger = Log.ForContext<ScreenCaptureServiceTests>();
    }

    // ── Constructor ────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullSettingsService_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new ScreenCaptureService(null!, _logger);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("settingsService");
    }

    // ── CaptureAndOcrAsync — Disabled ──────────────────────────────────────────

    [Fact]
    public async Task CaptureAndOcrAsync_WhenScreenAwarenessDisabled_ReturnsEmptyResult()
    {
        // Arrange
        _mockSettings
            .Setup(s => s.GetSettingsAsync())
            .ReturnsAsync(new AppSettings { EnableScreenAwareness = false });

        var sut = new ScreenCaptureService(_mockSettings.Object, _logger);

        // Act
        var result = await sut.CaptureAndOcrAsync();

        // Assert
        result.IsEmpty.Should().BeTrue();
        result.OcrText.Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureAndOcrAsync_WhenScreenAwarenessDisabled_DoesNotThrow()
    {
        // Arrange
        _mockSettings
            .Setup(s => s.GetSettingsAsync())
            .ReturnsAsync(new AppSettings { EnableScreenAwareness = false });

        var sut = new ScreenCaptureService(_mockSettings.Object, _logger);

        // Act
        var act = async () => await sut.CaptureAndOcrAsync();

        // Assert — should complete without exception
        await act.Should().NotThrowAsync();
    }

    // ── CaptureActiveWindowAndOcrAsync — Disabled ──────────────────────────────

    [Fact]
    public async Task CaptureActiveWindowAndOcrAsync_WhenScreenAwarenessDisabled_ReturnsEmptyResult()
    {
        // Arrange
        _mockSettings
            .Setup(s => s.GetSettingsAsync())
            .ReturnsAsync(new AppSettings { EnableScreenAwareness = false });

        var sut = new ScreenCaptureService(_mockSettings.Object, _logger);

        // Act
        var result = await sut.CaptureActiveWindowAndOcrAsync();

        // Assert
        result.IsEmpty.Should().BeTrue();
        result.OcrText.Should().BeEmpty();
    }

    // ── Cancellation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CaptureAndOcrAsync_WhenPreCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        _mockSettings
            .Setup(s => s.GetSettingsAsync())
            .ReturnsAsync(new AppSettings { EnableScreenAwareness = true });

        var sut = new ScreenCaptureService(_mockSettings.Object, _logger);
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled token

        // Act
        var act = async () => await sut.CaptureAndOcrAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CaptureActiveWindowAndOcrAsync_WhenPreCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        _mockSettings
            .Setup(s => s.GetSettingsAsync())
            .ReturnsAsync(new AppSettings { EnableScreenAwareness = true });

        var sut = new ScreenCaptureService(_mockSettings.Object, _logger);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await sut.CaptureActiveWindowAndOcrAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Settings read failure ───────────────────────────────────────────────────

    [Fact]
    public async Task CaptureAndOcrAsync_WhenSettingsReadFails_DefaultsToDisabled()
    {
        // Arrange
        _mockSettings
            .Setup(s => s.GetSettingsAsync())
            .ThrowsAsync(new InvalidOperationException("Settings unavailable"));

        var sut = new ScreenCaptureService(_mockSettings.Object, _logger);

        // Act
        var result = await sut.CaptureAndOcrAsync();

        // Assert — should gracefully fall back to disabled (no capture)
        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task CaptureActiveWindowAndOcrAsync_WhenSettingsReadFails_DefaultsToDisabled()
    {
        // Arrange
        _mockSettings
            .Setup(s => s.GetSettingsAsync())
            .ThrowsAsync(new InvalidOperationException("Settings unavailable"));

        var sut = new ScreenCaptureService(_mockSettings.Object, _logger);

        // Act
        var result = await sut.CaptureActiveWindowAndOcrAsync();

        // Assert
        result.IsEmpty.Should().BeTrue();
    }

    // ── CaptureAndOcrAsync — Enabled (live desktop) ────────────────────────────
    // These tests verify the service attempts capture when enabled.
    // On a headless CI environment, native P/Invoke calls will likely fail
    // gracefully (returning empty results), which is the expected fallback.

    [Fact]
    public async Task CaptureAndOcrAsync_WhenEnabled_AttemptsCaptureWithoutThrowing()
    {
        // Arrange
        _mockSettings
            .Setup(s => s.GetSettingsAsync())
            .ReturnsAsync(new AppSettings { EnableScreenAwareness = true });

        var sut = new ScreenCaptureService(_mockSettings.Object, _logger);

        // Act & Assert — in headless environments, this will either succeed
        // with screen data or gracefully return an empty result. It must NOT throw.
        var result = await sut.CaptureAndOcrAsync();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task CaptureActiveWindowAndOcrAsync_WhenEnabled_AttemptsCaptureWithoutThrowing()
    {
        // Arrange
        _mockSettings
            .Setup(s => s.GetSettingsAsync())
            .ReturnsAsync(new AppSettings { EnableScreenAwareness = true });

        var sut = new ScreenCaptureService(_mockSettings.Object, _logger);

        // Act & Assert — same graceful fallback expectation as above
        var result = await sut.CaptureActiveWindowAndOcrAsync();
        result.Should().NotBeNull();
    }

    // ── CapturedAtUtc is recent ─────────────────────────────────────────────────

    [Fact]
    public async Task CaptureAndOcrAsync_WhenDisabled_CapturedAtUtc_IsRecent()
    {
        // Arrange
        _mockSettings
            .Setup(s => s.GetSettingsAsync())
            .ReturnsAsync(new AppSettings { EnableScreenAwareness = false });

        var sut = new ScreenCaptureService(_mockSettings.Object, _logger);
        var before = DateTime.UtcNow.AddSeconds(-2);

        // Act
        var result = await sut.CaptureAndOcrAsync();

        // Assert — even for disabled results, the timestamp should be recent
        result.CapturedAtUtc.Should().BeOnOrAfter(before);
    }
}