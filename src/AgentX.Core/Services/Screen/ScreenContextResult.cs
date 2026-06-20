namespace AgentX.Core.Services.Screen;

/// <summary>
/// Represents the result of a screen capture and OCR operation,
/// containing the extracted text and metadata about the captured context.
/// </summary>
public class ScreenContextResult
{
    /// <summary>
    /// Text extracted from the screen region via OCR.
    /// Empty if OCR produced no text or if capture was skipped.
    /// </summary>
    public string OcrText { get; init; } = string.Empty;

    /// <summary>
    /// Title of the active foreground window at the time of capture.
    /// Empty if the window title could not be retrieved.
    /// </summary>
    public string ActiveWindowTitle { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the screen context was captured.
    /// </summary>
    public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Structured IDE context detected from <see cref="ActiveWindowTitle"/>.
    /// <c>null</c> if the active window is not a recognized IDE.
    /// </summary>
    public IdeDetection? IdeContext { get; init; }

    /// <summary>
    /// Indicates whether all context fields are empty or unset,
    /// meaning no useful context was captured.
    /// </summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(OcrText)
        && string.IsNullOrWhiteSpace(ActiveWindowTitle)
        && IdeContext is null;
}
