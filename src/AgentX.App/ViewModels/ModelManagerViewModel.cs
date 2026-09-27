using System.Collections.ObjectModel;
using System.Diagnostics;
using AgentX.App.Services;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Documents;
using AgentX.Core.Helpers;
using AgentX.Core.Services.Audio;
using AgentX.Core.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Windows.ApplicationModel.DataTransfer;

namespace AgentX.App.ViewModels;

public partial class ModelManagerViewModel : ObservableObject, IDisposable
{
    // ── Services ──────────────────────────────────────────────
    private readonly IModelManager _modelManager;
    private readonly IAiService _aiService;
    private readonly ILocalizationService _localization;
    private CancellationTokenSource? _downloadCts;

    // ── Page Properties ────────────────────────────────────────
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private string _downloadModelName = string.Empty;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _downloadStatus = string.Empty;

    // "Checking..." in the user's language, set by the constructor.
    [ObservableProperty] private string _connectionStatus;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private int _totalModels;
    [ObservableProperty] private string _totalModelSize = "0 MB";
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;

    public ObservableCollection<ModelDisplayItem> InstalledModels { get; } = new();

    /// <summary>
    /// The Speech-to-Text Model section: the Whisper model that transcribes imported audio
    /// files and voice input, installed and removed from this page.
    /// </summary>
    public SpeechModelViewModel SpeechModel { get; }

    // ── Constructor ────────────────────────────────────────────
    public ModelManagerViewModel(
        IModelManager modelManager,
        IAiService aiService,
        ITranscriptionService transcriptionService,
        IDocumentService documentService,
        ILocalizationService localization,
        INotificationService? notifications = null)
    {
        _modelManager = modelManager;
        _aiService = aiService;
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _connectionStatus = _localization.GetString("ModelMgr_CheckingConnection");
        SpeechModel = new SpeechModelViewModel(transcriptionService, documentService, localization, notifications);
        Log.Debug("ModelManagerViewModel created with services");
    }

    // ── Initialization ─────────────────────────────────────────
    public async Task InitializeAsync()
    {
        Log.Information("ModelManager initializing...");

        // A local file check, so the section is filled in before the provider round-trips below.
        await SpeechModel.LoadAsync();

        try
        {
            await CheckConnectionAsync();
            await LoadModelsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ModelManager initialization failed");
            ConnectionStatus = _localization.GetString("ModelMgr_ConnectionFailed");
            IsConnected = false;
            SetError(_localization.GetString("ModelMgr_ConnectFailed", ActiveProviderName()));
        }
    }

    /// <summary>
    /// Display name of the active provider (the built-in model, Ollama, OpenAI or Anthropic), so
    /// status and error text never name a provider that is not in use.
    /// </summary>
    private string ActiveProviderName()
    {
        try
        {
            return _aiService.ActiveProvider.DisplayName;
        }
        catch (InvalidOperationException)
        {
            return ProviderStatusText.GenericName(_localization); // not initialized yet
        }
    }

    // ── Connection Check ───────────────────────────────────────
    // Worded like the status strip and the dashboard, through ProviderStatusText.
    private async Task CheckConnectionAsync()
    {
        var providerName = ActiveProviderName();
        try
        {
            var connected = await _aiService.ActiveProvider.CheckConnectionAsync();
            IsConnected = connected;
            ConnectionStatus = connected
                ? ProviderStatusText.ConnectedTo(_localization, providerName)
                : ProviderStatusText.NotAvailable(_localization, providerName);
        }
        catch
        {
            IsConnected = false;
            ConnectionStatus = ProviderStatusText.NotAvailable(_localization, providerName);
        }
    }

    // ── Load Models ────────────────────────────────────────────
    private async Task LoadModelsAsync()
    {
        IsLoading = true;
        ClearError();

        try
        {
            var models = await _modelManager.GetInstalledModelsAsync();
            var activeModelId = _aiService.ActiveModelId ?? string.Empty;

            InstalledModels.Clear();
            long totalSize = 0;

            foreach (var model in models)
            {
                totalSize += model.SizeBytes;
                InstalledModels.Add(new ModelDisplayItem
                {
                    Id = model.Id,
                    Name = model.Name,
                    Family = model.Family,
                    SizeFormatted = model.SizeFormatted,
                    QuantizationLevel = model.QuantizationLevel,
                    ParameterCount = model.ParameterCount,
                    ContextLength = model.ContextLength,
                    Digest = model.Digest,
                    ModifiedAtFormatted = FormatHelper.TimeAgoWithMonths(model.ModifiedAt),
                    IsActive = string.Equals(model.Id, activeModelId, StringComparison.OrdinalIgnoreCase)
                });
            }

            TotalModels = InstalledModels.Count;
            TotalModelSize = FormatHelper.FormatBytes(totalSize);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load models");
            SetError(_localization.GetString("ModelMgr_LoadModelsFailed", ActiveProviderName()));
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Refresh Command ────────────────────────────────────────
    [RelayCommand]
    private async Task RefreshModelsAsync()
    {
        Log.Debug("Refresh models requested");
        await InitializeAsync();
    }

    // ── Pull Model Command ─────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanPullModel))]
    private async Task PullModelAsync()
    {
        if (string.IsNullOrWhiteSpace(DownloadModelName)) return;

        var modelName = DownloadModelName.Trim().ToLowerInvariant();
        Log.Information("Pulling model: {ModelName}", modelName);

        IsDownloading = true;
        DownloadProgress = 0;
        DownloadStatus = _localization.GetString("ModelMgr_PreparingDownload", modelName);
        ClearError();

        _downloadCts = new CancellationTokenSource();

        try
        {
            var progressReporter = new Progress<ModelDownloadProgress>(p =>
            {
                DownloadProgress = p.PercentComplete;
                DownloadStatus = FormatDownloadStatus(p);
            });
            await _modelManager.PullModelAsync(modelName, progressReporter, _downloadCts.Token);

            DownloadStatus = _localization.GetString("ModelMgr_Downloaded", modelName);
            DownloadModelName = string.Empty;

            // Refresh model list after download
            await LoadModelsAsync();
        }
        catch (OperationCanceledException)
        {
            DownloadStatus = _localization.GetString("ModelMgr_DownloadCancelled");
            Log.Information("Model download cancelled: {ModelName}", modelName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to pull model: {ModelName}", modelName);
            DownloadStatus = _localization.GetString("ModelMgr_DownloadFailedStatus", ex.Message);
            SetError(_localization.GetString("ModelMgr_DownloadFailed", modelName, ActiveProviderName()));
        }
        finally
        {
            IsDownloading = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    private bool CanPullModel() => !string.IsNullOrWhiteSpace(DownloadModelName) && !IsDownloading;

    partial void OnDownloadModelNameChanged(string value)
    {
        PullModelCommand.NotifyCanExecuteChanged();
    }

    // ── Cancel Download Command ────────────────────────────────
    [RelayCommand]
    private void CancelDownload()
    {
        _downloadCts?.Cancel();
        Log.Information("Download cancellation requested");
    }

    // ── Delete Model Command ───────────────────────────────────
    [RelayCommand]
    private async Task DeleteModelAsync(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return;

        Log.Information("Deleting model: {ModelId}", modelId);
        ClearError();

        try
        {
            await _modelManager.DeleteModelAsync(modelId);

            // Refresh the model list
            await LoadModelsAsync();
            Log.Information("Model deleted: {ModelId}", modelId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete model: {ModelId}", modelId);
            SetError(_localization.GetString("ModelMgr_DeleteFailed", ex.Message));
        }
    }

    // ── Set Active Model Command ───────────────────────────────
    [RelayCommand]
    private async Task SetActiveModelAsync(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return;

        Log.Information("Setting active model: {ModelId}", modelId);

        try
        {
            // Persist the active model selection via the AI service
            await _aiService.SetActiveModelAsync(modelId);

            // Update the UI immediately
            foreach (var model in InstalledModels)
            {
                model.IsActive = string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase);
            }

            // Force collection refresh to update UI bindings
            var items = InstalledModels.ToList();
            InstalledModels.Clear();
            foreach (var item in items)
            {
                InstalledModels.Add(item);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set active model: {ModelId}", modelId);
            SetError(_localization.GetString("ModelMgr_SetActiveFailed", ex.Message));
        }
    }

    // ── Copy Model Name Command ────────────────────────────────
    [RelayCommand]
    private void CopyModelName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(name);
            Clipboard.SetContent(dataPackage);
            Log.Debug("Model name copied: {Name}", name);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to copy model name to clipboard");
        }
    }

    // ── Set Download Model Name (for suggestion chips) ─────────
    [RelayCommand]
    private void SetModelSuggestion(string? modelName)
    {
        if (!string.IsNullOrWhiteSpace(modelName))
        {
            DownloadModelName = modelName;
        }
    }

    // ── Open Ollama Library ────────────────────────────────────
    [RelayCommand]
    private void OpenOllamaLibrary()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://ollama.com/library",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open Ollama library URL");
        }
    }

    // ── Helpers ────────────────────────────────────────────────

    private static string FormatDownloadStatus(ModelDownloadProgress progress)
    {
        if (progress.TotalBytes <= 0)
            return progress.Status;

        var downloaded = FormatHelper.FormatBytes(progress.CompletedBytes);
        var total = FormatHelper.FormatBytes(progress.TotalBytes);
        return $"{progress.Status} - {downloaded} / {total} ({progress.PercentComplete:F1}%)";
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }

    private void ClearError()
    {
        ErrorMessage = string.Empty;
        HasError = false;
    }

    public void Dispose()
    {
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        SpeechModel.Dispose();
        Log.Debug("ModelManagerViewModel disposed");
    }
}

// ── Display Item ───────────────────────────────────────────────
public partial class ModelDisplayItem : ObservableObject
{
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _family = string.Empty;
    [ObservableProperty] private string _sizeFormatted = string.Empty;
    [ObservableProperty] private string _quantizationLevel = string.Empty;
    [ObservableProperty] private int _parameterCount;
    [ObservableProperty] private int _contextLength;
    [ObservableProperty] private string _digest = string.Empty;
    [ObservableProperty] private string _modifiedAtFormatted = string.Empty;
    [ObservableProperty] private bool _isActive;

    /// <summary>
    /// Formatted parameter count for display (e.g. "7B", "13B", "70B").
    /// </summary>
    public string ParameterCountFormatted => ParameterCount switch
    {
        0 => "--",
        < 1000 => $"{ParameterCount}M",
        _ => $"{ParameterCount / 1000.0:F1}B"
    };

    /// <summary>
    /// Short digest for display (first 12 characters).
    /// </summary>
    public string DigestShort => string.IsNullOrEmpty(Digest) ? "--" :
        Digest.Length > 12 ? Digest[..12] : Digest;

    /// <summary>
    /// Formatted context length (e.g. "4K", "8K", "128K").
    /// </summary>
    public string ContextLengthFormatted => ContextLength switch
    {
        0 => "--",
        < 1000 => $"{ContextLength}",
        _ => $"{ContextLength / 1000}K"
    };
}
