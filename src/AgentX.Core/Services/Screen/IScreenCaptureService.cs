namespace AgentX.Core.Services.Screen;

/// <summary>
/// Provides screen capture and OCR capabilities for screen-awareness features.
/// </summary>
public interface IScreenCaptureService
{
    /// <summary>
    /// Captures the entire primary screen, performs OCR on the captured image,
    /// and returns the extracted text along with the active window title.
    /// <para>
    /// If screen awareness is disabled in settings, returns an empty
    /// <see cref="ScreenContextResult"/> without capturing.
    /// </para>
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ScreenContextResult"/> containing OCR text and window metadata,
    /// or an empty result if capture fails or is disabled.
    /// </returns>
    Task<ScreenContextResult> CaptureAndOcrAsync(CancellationToken ct = default);

    /// <summary>
    /// Captures only the foreground (active) window, performs OCR on the captured image,
    /// and returns the extracted text along with the active window title.
    /// <para>
    /// If screen awareness is disabled in settings, returns an empty
    /// <see cref="ScreenContextResult"/> without capturing.
    /// </para>
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ScreenContextResult"/> containing OCR text and window metadata,
    /// or an empty result if capture fails or is disabled.
    /// </returns>
    Task<ScreenContextResult> CaptureActiveWindowAndOcrAsync(CancellationToken ct = default);
}
