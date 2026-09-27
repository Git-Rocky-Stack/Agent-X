using AgentX.App.Services;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Audio;
using AgentX.Core.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

/// <summary>
/// The Speech-to-Text Model section of the Model Manager. Shows whether the Whisper model that
/// transcribes imported audio files and voice input is installed and how much disk space it takes,
/// downloads it when the user presses Download (nothing downloads it on its own) with progress and
/// Cancel, and removes it.
/// <para>
/// After a download, the audio documents that could not be transcribed because the model was
/// missing are queued for indexing again (<see cref="IDocumentService.RequeueAudioAwaitingSpeechModelAsync"/>)
/// and the user is told how many.
/// </para>
/// </summary>
public sealed partial class SpeechModelViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// The model size the audio importer and voice input transcribe with (the default of
    /// <see cref="Core.Services.Audio.Models.TranscriptionOptions"/>).
    /// </summary>
    internal const string ModelSize = "base";

    private readonly ITranscriptionService _transcription;
    private readonly IDocumentService _documents;
    private readonly ILocalizationService _localization;
    private readonly INotificationService? _notifications;

    private CancellationTokenSource? _downloadCts;

    /// <summary>The whole percent last written to <see cref="StatusText"/> during a download.</summary>
    private int _shownPercent = -1;

    /// <summary>Whether the model file is on disk.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownload), nameof(CanRemove))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand), nameof(RemoveCommand))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownload), nameof(CanRemove))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand), nameof(RemoveCommand), nameof(CancelDownloadCommand))]
    private bool _isDownloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownload), nameof(CanRemove))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand), nameof(RemoveCommand))]
    private bool _isRemoving;

    /// <summary>Download progress, 0 to 100.</summary>
    [ObservableProperty] private double _downloadProgress;

    /// <summary>Installed with its size on disk, not installed, or how far the download is.</summary>
    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>What the last download or removal did; empty when there is nothing to report.</summary>
    [ObservableProperty] private string _resultMessage = string.Empty;

    /// <summary>Why the last download, removal or status check failed; empty when nothing failed.</summary>
    [ObservableProperty] private string _errorMessage = string.Empty;

    public SpeechModelViewModel(
        ITranscriptionService transcription,
        IDocumentService documents,
        ILocalizationService localization,
        INotificationService? notifications = null)
    {
        _transcription = transcription ?? throw new ArgumentNullException(nameof(transcription));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _notifications = notifications;
    }

    /// <summary>Download is offered while the model is missing and nothing else is running.</summary>
    public bool CanDownload => !IsInstalled && !IsDownloading && !IsRemoving;

    /// <summary>Remove is offered while the model is installed and nothing else is running.</summary>
    public bool CanRemove => IsInstalled && !IsDownloading && !IsRemoving;

    /// <summary>
    /// Reads whether the model is installed and how large it is. Leaves the state alone while a
    /// download is running, which reports its own progress.
    /// </summary>
    public async Task LoadAsync()
    {
        if (IsDownloading)
        {
            return;
        }

        try
        {
            var sizeOnDisk = await _transcription.GetInstalledModelSizeAsync(ModelSize);

            IsInstalled = sizeOnDisk is not null;
            StatusText = sizeOnDisk is long bytes
                ? _localization.GetString("ModelMgr_SttInstalled", FormatHelper.FormatBytes(bytes))
                : _localization.GetString("ModelMgr_SttNotInstalled");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not read the state of the speech-to-text model");
            IsInstalled = false;
            StatusText = _localization.GetString("ModelMgr_SttStatusFailed", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        Log.Information("Speech-to-text model download requested");
        ResultMessage = string.Empty;
        ErrorMessage = string.Empty;

        using var cts = new CancellationTokenSource();
        _downloadCts = cts;
        _shownPercent = -1;
        DownloadProgress = 0;
        IsDownloading = true;
        ShowDownloadPercent(0);

        Exception? failure = null;
        var cancelled = false;
        try
        {
            await _transcription.DownloadModelAsync(ModelSize, new Progress<double>(OnDownloadProgress), cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            _downloadCts = null;
            IsDownloading = false;
        }

        // The service leaves no partial file behind, so the disk says what happened.
        await LoadAsync();

        if (cancelled)
        {
            Log.Information("Speech-to-text model download cancelled");
            ResultMessage = _localization.GetString("ModelMgr_SttDownloadCancelled");
            return;
        }

        if (failure is not null)
        {
            Log.Error(failure, "Speech-to-text model download failed");
            ErrorMessage = _localization.GetString("ModelMgr_SttDownloadFailed", failure.Message);
            _notifications?.ShowError(_localization.GetString("ModelMgr_SttDownloadFailedTitle"), ErrorMessage);
            return;
        }

        await QueueWaitingAudioAsync();
    }

    private bool CanCancelDownload() => IsDownloading;

    [RelayCommand(CanExecute = nameof(CanCancelDownload))]
    private void CancelDownload()
    {
        Log.Information("Speech-to-text model download cancellation requested");
        _downloadCts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveAsync()
    {
        Log.Information("Removing the speech-to-text model");
        ResultMessage = string.Empty;
        ErrorMessage = string.Empty;
        IsRemoving = true;

        try
        {
            await _transcription.RemoveModelAsync(ModelSize);
            ResultMessage = _localization.GetString("ModelMgr_SttRemoved");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not remove the speech-to-text model");
            ErrorMessage = _localization.GetString("ModelMgr_SttRemoveFailed", ex.Message);
        }
        finally
        {
            IsRemoving = false;
        }

        await LoadAsync();
    }

    /// <summary>
    /// Sends the audio documents that were waiting for the model back to the indexing queue and
    /// reports how many there were, on the page and as a notification (the download may finish
    /// after the user has left the page).
    /// </summary>
    private async Task QueueWaitingAudioAsync()
    {
        var title = _localization.GetString("ModelMgr_SttReadyTitle");

        int queued;
        try
        {
            queued = await _documents.RequeueAudioAwaitingSpeechModelAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not queue the audio documents waiting for the speech-to-text model");
            ErrorMessage = _localization.GetString("ModelMgr_SttRequeueFailed", ex.Message);
            _notifications?.ShowError(title, ErrorMessage);
            return;
        }

        Log.Information(
            "Speech-to-text model installed; {Count} audio documents queued for transcription", queued);

        ResultMessage = queued > 0
            ? _localization.GetString("ModelMgr_SttQueued", queued)
            : _localization.GetString("ModelMgr_SttReady");
        _notifications?.ShowSuccess(title, ResultMessage, durationMs: 6000);
    }

    private void OnDownloadProgress(double fraction)
    {
        // A report posted just before the download ended can be delivered after it.
        if (!IsDownloading)
        {
            return;
        }

        DownloadProgress = Math.Clamp(fraction, 0.0, 1.0) * 100.0;
        ShowDownloadPercent((int)DownloadProgress);
    }

    private void ShowDownloadPercent(int percent)
    {
        if (percent == _shownPercent)
        {
            return;
        }

        _shownPercent = percent;
        StatusText = _localization.GetString("ModelMgr_SttDownloading", percent);
    }

    /// <summary>Cancels a running download; the service removes its partial file.</summary>
    public void Dispose() => _downloadCts?.Cancel();
}
