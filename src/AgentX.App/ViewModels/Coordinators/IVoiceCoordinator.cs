namespace AgentX.App.ViewModels.Coordinators;

/// <summary>
/// Coordinates voice recording and transcription operations.
/// The coordinator manages recording state and delegates transcription to the service.
/// Raises events for the ChatViewModel to synchronize UI state.
/// </summary>
public interface IVoiceCoordinator
{
    /// <summary>Whether a recording is currently in progress.</summary>
    bool IsRecording { get; }

    /// <summary>Whether a transcription is currently in progress.</summary>
    bool IsTranscribing { get; }

    /// <summary>Status message for display in the UI.</summary>
    string StatusMessage { get; }

    /// <summary>Raised when recording state changes. The bool arg is the new IsRecording value.</summary>
    event EventHandler<bool>? RecordingStateChanged;

    /// <summary>Raised when transcribing state changes. The bool arg is the new IsTranscribing value.</summary>
    event EventHandler<bool>? TranscribingStateChanged;

    /// <summary>Raised when the status message changes.</summary>
    event EventHandler<string>? StatusChanged;

    /// <summary>Raised when a notification should be shown.</summary>
    event EventHandler<NotificationRequestEventArgs>? NotificationRequested;

    /// <summary>
    /// Toggles voice recording on/off. If currently recording, stops and transcribes.
    /// If not recording, starts recording.
    /// </summary>
    /// <returns>The transcribed text if stopping, null if starting or no transcription.</returns>
    Task<string?> ToggleRecordingAsync();

    /// <summary>
    /// Transcribes an audio file from disk.
    /// </summary>
    /// <param name="filePath">Path to the audio file.</param>
    /// <returns>The transcribed text, or null if transcription failed.</returns>
    Task<string?> TranscribeFileAsync(string filePath);

    /// <summary>
    /// Gets the supported audio file formats.
    /// </summary>
    IReadOnlyList<string> SupportedFormats { get; }
}
