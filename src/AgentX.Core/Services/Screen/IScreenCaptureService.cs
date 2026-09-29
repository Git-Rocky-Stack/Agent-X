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

    /// <summary>
    /// Captures the given window, performs OCR on the captured image, and returns the
    /// extracted text along with that window's title. Used when the window to read was
    /// chosen earlier, for example the one in front when Quick Chat was summoned, since by
    /// the time a query runs the foreground window is the caller's own.
    /// <para>
    /// If screen awareness is disabled in settings, or <paramref name="windowHandle"/> is
    /// zero, returns an empty <see cref="ScreenContextResult"/> without capturing.
    /// </para>
    /// </summary>
    /// <param name="windowHandle">The native handle (HWND) of the window to capture.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ScreenContextResult> CaptureWindowAndOcrAsync(IntPtr windowHandle, CancellationToken ct = default);
}
