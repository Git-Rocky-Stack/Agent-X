namespace AgentX.Core.Services.Audio;

/// <summary>
/// Thrown by <see cref="ITranscriptionService.TranscribeFileAsync"/> when the Whisper model it
/// needs is not installed. It derives from <see cref="InvalidOperationException"/>, the documented
/// "model missing" contract, so callers can tell this case apart by type instead of by message.
/// </summary>
public sealed class TranscriptionModelMissingException : InvalidOperationException
{
    public TranscriptionModelMissingException(string modelSize, string modelPath)
        : base($"The Whisper model '{modelSize}' is not installed (expected at {modelPath}).")
    {
        ModelSize = modelSize;
    }

    /// <summary>The model size that is missing, for example "base".</summary>
    public string ModelSize { get; }
}
