using AgentX.Core.Services.Audio;
using AgentX.Core.Services.Audio.Models;
using NAudio.Wave;
using Serilog;

namespace AgentX.App.ViewModels.Coordinators;

/// <summary>
/// Orchestrates voice recording (via NAudio) and transcription (via ITranscriptionService).
/// Raises events for the ChatViewModel to synchronize UI state.
/// </summary>
public sealed class VoiceCoordinator : IVoiceCoordinator, IDisposable
{
    private readonly ITranscriptionService _transcriptionService;

    // ── NAudio recording resources ──────────────────────────────
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _waveWriter;
    private string? _currentRecordingPath;
    private TaskCompletionSource? _recordingStopTcs;

    // ── State ────────────────────────────────────────────────────
    private bool _isRecording;
    private bool _isTranscribing;
    private string _statusMessage = string.Empty;

    public bool IsRecording => _isRecording;
    public bool IsTranscribing => _isTranscribing;
    public string StatusMessage => _statusMessage;

    public IReadOnlyList<string> SupportedFormats => _transcriptionService.SupportedFormats;

    public event EventHandler<bool>? RecordingStateChanged;
    public event EventHandler<bool>? TranscribingStateChanged;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<NotificationRequestEventArgs>? NotificationRequested;

    public VoiceCoordinator(ITranscriptionService transcriptionService)
    {
        _transcriptionService = transcriptionService;
    }

    /// <inheritdoc />
    public async Task<string?> ToggleRecordingAsync()
    {
        if (_isRecording)
        {
            return await StopRecordingAndTranscribeAsync();
        }
        else
        {
            StartRecording();
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> TranscribeFileAsync(string filePath)
    {
        SetTranscribing(true);
        SetStatus("Transcribing...");

        try
        {
            var result = await _transcriptionService.TranscribeFileAsync(
                filePath,
                new TranscriptionOptions { ModelSize = "base" },
                progress: new Progress<TranscriptionProgress>(p => SetStatus(p.CurrentPhase)),
                CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(result.FullText))
            {
                return result.FullText.Trim();
            }

            return null;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("model", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning(ex, "Whisper model not available for file transcription");
            NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
            {
                Level = "error",
                Title = "Model Required",
                Message = "Download a Whisper model first. Go to Settings > Voice to download one."
            });
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Audio file transcription failed");
            NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
            {
                Level = "error",
                Title = "Transcription Failed",
                Message = $"Could not transcribe the selected file: {ex.Message}"
            });
            return null;
        }
        finally
        {
            SetTranscribing(false);
            SetStatus(string.Empty);
        }
    }

    // ── Recording ────────────────────────────────────────────────

    private void StartRecording()
    {
        try
        {
            _currentRecordingPath = Path.Combine(
                Path.GetTempPath(),
                $"agentx-voice-{Guid.NewGuid():N}.wav");

            _recordingStopTcs = new TaskCompletionSource();

            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 100
            };

            _waveWriter = new WaveFileWriter(_currentRecordingPath, _waveIn.WaveFormat);

            _waveIn.DataAvailable += OnRecordingDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;

            _waveIn.StartRecording();
            SetRecording(true);
            SetStatus("Recording...");

            Log.Debug("Voice recording started: {Path}", _currentRecordingPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start voice recording");
            CleanupRecording();
            NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
            {
                Level = "error",
                Title = "Recording Failed",
                Message = "Could not start voice recording. Ensure a microphone is connected and permissions are granted."
            });
        }
    }

    private async Task<string?> StopRecordingAndTranscribeAsync()
    {
        if (_waveIn is null || _currentRecordingPath is null) return null;

        Log.Debug("Stopping voice recording for transcription");

        _waveIn.StopRecording();
        SetRecording(false);
        SetTranscribing(true);
        SetStatus("Transcribing...");

        if (_recordingStopTcs is not null)
            await _recordingStopTcs.Task;

        try
        {
            if (File.Exists(_currentRecordingPath))
            {
                var fileInfo = new FileInfo(_currentRecordingPath);
                if (fileInfo.Length > 44) // WAV header is 44 bytes minimum
                {
                    var result = await _transcriptionService.TranscribeFileAsync(
                        _currentRecordingPath,
                        new TranscriptionOptions { ModelSize = "base" },
                        progress: new Progress<TranscriptionProgress>(p => SetStatus(p.CurrentPhase)),
                        CancellationToken.None);

                    if (!string.IsNullOrWhiteSpace(result.FullText))
                    {
                        Log.Information("Voice transcription complete: {Length} chars, {Segments} segments",
                            result.FullText.Length, result.Segments.Count);
                        return result.FullText.Trim();
                    }
                    else
                    {
                        NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
                        {
                            Level = "info",
                            Title = "No Speech Detected",
                            Message = "Could not detect speech in the recording. Try again in a quieter environment."
                        });
                    }
                }
                else
                {
                    NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
                    {
                        Level = "info",
                        Title = "Recording Too Short",
                        Message = "The recording was too short to transcribe. Hold the button longer while speaking."
                    });
                }
            }

            return null;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("model", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning(ex, "Whisper model not available");
            NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
            {
                Level = "error",
                Title = "Model Required",
                Message = "Download a Whisper model first. Go to Settings > Voice to download one."
            });
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Voice transcription failed");
            NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
            {
                Level = "error",
                Title = "Transcription Failed",
                Message = $"Could not transcribe the recording: {ex.Message}"
            });
            return null;
        }
        finally
        {
            SetTranscribing(false);
            SetStatus(string.Empty);
            CleanupRecording();
        }
    }

    // ── NAudio event handlers ────────────────────────────────────

    private void OnRecordingDataAvailable(object? sender, WaveInEventArgs e)
    {
        _waveWriter?.Write(e.Buffer, 0, e.BytesRecorded);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _waveWriter?.Dispose();
        _waveWriter = null;

        _waveIn?.Dispose();
        _waveIn = null;

        _recordingStopTcs?.TrySetResult();

        if (e.Exception is not null)
        {
            Log.Error(e.Exception, "Recording stopped with error");
        }
    }

    // ── State helpers ────────────────────────────────────────────

    private void SetRecording(bool value)
    {
        _isRecording = value;
        RecordingStateChanged?.Invoke(this, value);
    }

    private void SetTranscribing(bool value)
    {
        _isTranscribing = value;
        TranscribingStateChanged?.Invoke(this, value);
    }

    private void SetStatus(string message)
    {
        _statusMessage = message;
        StatusChanged?.Invoke(this, message);
    }

    // ── Cleanup ──────────────────────────────────────────────────

    private void CleanupRecording()
    {
        _waveWriter?.Dispose();
        _waveWriter = null;

        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnRecordingDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;
            _waveIn.Dispose();
            _waveIn = null;
        }

        if (_currentRecordingPath is not null)
        {
            try { if (File.Exists(_currentRecordingPath)) File.Delete(_currentRecordingPath); }
            catch { /* best effort */ }
            _currentRecordingPath = null;
        }

        _recordingStopTcs = null;
    }

    /// <summary>
    /// Stops any active recording and cleans up resources.
    /// </summary>
    public void Dispose()
    {
        if (_waveIn is not null && _isRecording)
        {
            _waveIn.StopRecording();
        }
        CleanupRecording();
    }
}
