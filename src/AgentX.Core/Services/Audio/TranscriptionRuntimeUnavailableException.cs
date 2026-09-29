namespace AgentX.Core.Services.Audio;

/// <summary>
/// Thrown by <see cref="ITranscriptionService.TranscribeFileAsync"/> when the native Whisper
/// runtime cannot be loaded on this computer, so no model can run, whichever is installed. It
/// derives from <see cref="NotSupportedException"/>, which the interface documents for a missing
/// runtime.
/// </summary>
public sealed class TranscriptionRuntimeUnavailableException : NotSupportedException
{
    public TranscriptionRuntimeUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
