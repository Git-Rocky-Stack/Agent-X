using AgentX.Core.Services.Audio;
using AgentX.Core.Services.Audio.Models;
using AgentX.Core.Services.Localization;
using NAudio.Wave;
using Serilog;

namespace AgentX.App.ViewModels.Coordinators;

/// <summary>
/// Orchestrates voice recording (via NAudio) and transcription (via ITranscriptionService).
/// Raises events for the ChatViewModel to synchronize UI state. Status and notification text
/// come from the string resources; a missing speech-to-text model points the user at the Model
/// Manager page, where it is installed.
/// </summary>
public sealed class VoiceCoordinator : IVoiceCoordinator, IDisposable
{
    private readonly ITranscriptionService _transcriptionService;
    private readonly ILocalizationService _localization;

    // -- NAudio recording resources ------------------------------
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _waveWriter;
    private string? _currentRecordingPath;
    private TaskCompletionSource? _recordingStopTcs;
    private bool _disposed;

    // -- State ----------------------------------------------------
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

    public VoiceCoordinator(ITranscriptionService transcriptionService, ILocalizationService localization)
    {
        _transcriptionService = transcriptionService;
        _localization = localization;
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
        SetStatus(_localization.GetString("Voice_Transcribing"));

        try
        {
            var result = await _transcriptionService.TranscribeFileAsync(
                filePath,
                new TranscriptionOptions { ModelSize = "base" },
                progress: new Progress<TranscriptionProgress>(p => SetStatus(DescribePhase(p.CurrentPhase))),
                CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(result.FullText))
            {
                return result.FullText.Trim();
            }

            return null;
        }
        catch (TranscriptionModelMissingException ex)
        {
            Log.Warning(ex, "Whisper model not available for file transcription");
            NotifyModelRequired();
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Audio file transcription failed");
            NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
            {
                Level = "error",
                Title = _localization.GetString("Voice_TranscriptionFailedTitle"),
                Message = _localization.GetString("Voice_FileTranscriptionFailed", ex.Message)
            });
            return null;
        }
        finally
        {
            SetTranscribing(false);
            SetStatus(string.Empty);
        }
    }

    // -- Recording ------------------------------------------------

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
            SetStatus(_localization.GetString("Voice_Recording"));

            Log.Debug("Voice recording started: {Path}", _currentRecordingPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start voice recording");
            CleanupRecording();
            NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
            {
                Level = "error",
                Title = _localization.GetString("Voice_RecordingFailedTitle"),
                Message = _localization.GetString("Voice_RecordingFailedMessage")
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
        SetStatus(_localization.GetString("Voice_Transcribing"));

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
                        progress: new Progress<TranscriptionProgress>(p => SetStatus(DescribePhase(p.CurrentPhase))),
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
                            Title = _localization.GetString("Voice_NoSpeechTitle"),
                            Message = _localization.GetString("Voice_NoSpeechMessage")
                        });
                    }
                }
                else
                {
                    NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
                    {
                        Level = "info",
                        Title = _localization.GetString("Voice_TooShortTitle"),
                        Message = _localization.GetString("Voice_TooShortMessage")
                    });
                }
            }

            return null;
        }
        catch (TranscriptionModelMissingException ex)
        {
            Log.Warning(ex, "Whisper model not available");
            NotifyModelRequired();
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Voice transcription failed");
            NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
            {
                Level = "error",
                Title = _localization.GetString("Voice_TranscriptionFailedTitle"),
                Message = _localization.GetString("Voice_RecordingTranscriptionFailed", ex.Message)
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

    // -- NAudio event handlers ------------------------------------

    private void OnRecordingDataAvailable(object? sender, WaveInEventArgs e)
    {
        _waveWriter?.Write(e.Buffer, 0, e.BytesRecorded);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _waveWriter?.Dispose();
        _waveWriter = null;

        _recordingStopTcs?.TrySetResult();

        if (_isRecording)
        {
            SetRecording(false);
        }

        if (e.Exception is not null)
        {
            Log.Error(e.Exception, "Recording stopped with error");
        }
    }

    // -- State helpers --------------------------------------------

    /// <summary>
    /// The speech-to-text model is not installed: say where to install it (the Model Manager page).
    /// </summary>
    private void NotifyModelRequired()
    {
        NotificationRequested?.Invoke(this, new NotificationRequestEventArgs
        {
            Level = "error",
            Title = _localization.GetString("Voice_ModelRequiredTitle"),
            Message = _localization.GetString("Voice_ModelRequiredMessage")
        });
    }

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

    /// <summary>
    /// The transcription service names its progress phases in English. The phases it reports are
    /// shown in the user's language; any other phase is shown as it came.
    /// </summary>
    internal string DescribePhase(string phase) => phase switch
    {
        "Validating file..." => _localization.GetString("Voice_PhaseValidatingFile"),
        "Checking model..." => _localization.GetString("Voice_PhaseCheckingModel"),
        "Preparing audio..." => _localization.GetString("Voice_PhasePreparingAudio"),
        "Loading model..." => _localization.GetString("Voice_PhaseLoadingModel"),
        "Transcribing..." => _localization.GetString("Voice_Transcribing"),
        "Finalizing..." => _localization.GetString("Voice_PhaseFinalizing"),
        "Complete" => _localization.GetString("Voice_PhaseComplete"),
        _ => phase
    };

    // -- Cleanup --------------------------------------------------

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
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_waveIn is not null && _isRecording)
        {
            try
            {
                _waveIn.StopRecording();
                _recordingStopTcs?.Task.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to stop voice recording during disposal");
            }
        }

        CleanupRecording();
    }
}
