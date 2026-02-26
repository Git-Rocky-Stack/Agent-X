using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using Serilog;
using Windows.ApplicationModel.DataTransfer;

namespace AgentX.App.ViewModels;

public partial class ModelManagerViewModel : ObservableObject, IDisposable
{
    // ── Services ──────────────────────────────────────────────
    private readonly IModelManager _modelManager;
    private readonly IAiService _aiService;
    private CancellationTokenSource? _downloadCts;

    // ── Page Properties ────────────────────────────────────────
    [ObservableProperty] private string _pageTitle = "Model Manager";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private string _downloadModelName = string.Empty;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _downloadStatus = string.Empty;
    [ObservableProperty] private string _connectionStatus = "Checking...";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private int _totalModels;
    [ObservableProperty] private string _totalModelSize = "0 MB";
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;

    public ObservableCollection<ModelDisplayItem> InstalledModels { get; } = new();

    // ── Constructor ────────────────────────────────────────────
    public ModelManagerViewModel(IModelManager modelManager, IAiService aiService)
    {
        _modelManager = modelManager;
        _aiService = aiService;
        Log.Debug("ModelManagerViewModel created with services");
    }

    // ── Initialization ─────────────────────────────────────────
    public async Task InitializeAsync()
    {
        Log.Information("ModelManager initializing...");

        try
        {
            await CheckConnectionAsync();
            await LoadModelsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ModelManager initialization failed");
            ConnectionStatus = "Connection failed";
            IsConnected = false;
            SetError("Failed to connect to Ollama. Ensure Ollama is running.");
        }
    }

    // ── Connection Check ───────────────────────────────────────
    private async Task CheckConnectionAsync()
    {
        try
        {
            var connected = await _aiService.ActiveProvider.CheckConnectionAsync();
            IsConnected = connected;
            ConnectionStatus = connected ? "Connected to Ollama" : "Ollama not detected";
        }
        catch
        {
            IsConnected = false;
            ConnectionStatus = "Ollama not detected";
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
                    ModifiedAtFormatted = FormatTimeAgo(model.ModifiedAt),
                    IsActive = string.Equals(model.Id, activeModelId, StringComparison.OrdinalIgnoreCase)
                });
            }

            TotalModels = InstalledModels.Count;
            TotalModelSize = FormatBytes(totalSize);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load models");
            SetError("Failed to load model list. Check Ollama connection.");
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
        DownloadStatus = $"Preparing to download {modelName}...";
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

            DownloadStatus = $"Successfully downloaded {modelName}";
            DownloadModelName = string.Empty;

            // Refresh model list after download
            await LoadModelsAsync();
        }
        catch (OperationCanceledException)
        {
            DownloadStatus = "Download cancelled";
            Log.Information("Model download cancelled: {ModelName}", modelName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to pull model: {ModelName}", modelName);
            DownloadStatus = $"Download failed: {ex.Message}";
            SetError($"Failed to download {modelName}. Ensure Ollama is running and the model name is correct.");
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
            SetError($"Failed to delete model. {ex.Message}");
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
            SetError($"Failed to set active model. {ex.Message}");
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

        var downloaded = FormatBytes(progress.CompletedBytes);
        var total = FormatBytes(progress.TotalBytes);
        return $"{progress.Status} - {downloaded} / {total} ({progress.PercentComplete:F1}%)";
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1_000_000 => $"{bytes / 1_000.0:F1} KB",
            < 1_000_000_000 => $"{bytes / 1_000_000.0:F1} MB",
            _ => $"{bytes / 1_000_000_000.0:F2} GB"
        };
    }

    private static string FormatTimeAgo(DateTime dateTime)
    {
        var span = DateTime.UtcNow - dateTime.ToUniversalTime();
        return span.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)span.TotalMinutes}m ago",
            < 1440 => $"{(int)span.TotalHours}h ago",
            < 43200 => $"{(int)span.TotalDays}d ago",
            _ => $"{(int)(span.TotalDays / 30)}mo ago"
        };
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
