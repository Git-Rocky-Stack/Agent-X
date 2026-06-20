using System.Collections.ObjectModel;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class OnboardingViewModel : ObservableObject
{
    // ── Services ─────────────────────────────────────────────
    private readonly IAiService _aiService;
    private readonly ISettingsService _settingsService;
    private readonly IHardwareDetector _hardwareDetector;
    private readonly IBuiltInModelBootstrap _bootstrap;

    // ── Step Navigation ──────────────────────────────────────
    [ObservableProperty] private int _currentStep;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _canGoNext = true;

    // ── Step Visibility ──────────────────────────────────────
    [ObservableProperty] private bool _isStep0Visible = true;
    [ObservableProperty] private bool _isStep1Visible;
    [ObservableProperty] private bool _isStep2Visible;
    [ObservableProperty] private bool _isStep3Visible;
    [ObservableProperty] private bool _isStep4Visible;
    [ObservableProperty] private bool _showNextButton;

    // ── Step 1: Ollama Connection ────────────────────────────
    [ObservableProperty] private string _ollamaEndpoint = "http://localhost:11434";
    [ObservableProperty] private bool _isTestingConnection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionStatusTone))]
    private bool? _isOllamaConnected;

    [ObservableProperty] private string _connectionStatusText = "";
    [ObservableProperty] private bool _showConnectionStatus;

    /// <summary>
    /// Status token for the connection-result dot, mapped to a brush by
    /// StatusToColorConverter: connected = green, failed = red, untested = neutral
    /// (the offline default) instead of a constant green.
    /// </summary>
    public string ConnectionStatusTone => IsOllamaConnected switch
    {
        true => "connected",
        false => "offline",
        _ => "idle"
    };

    // ── Step 2: Model Selection ──────────────────────────────
    [ObservableProperty] private ObservableCollection<OnboardingModelItem> _availableModels = new();
    [ObservableProperty] private string _selectedChatModel = "";
    [ObservableProperty] private string _selectedEmbeddingModel = "";
    [ObservableProperty] private bool _hasModels;
    [ObservableProperty] private bool _isLoadingModels;
    [ObservableProperty] private string _hardwareInfo = "";

    // ── Step 3: Built-in AI & Cloud API Keys ─────────────────
    [ObservableProperty] private bool _isLocalModelAvailable;
    [ObservableProperty] private string _gpuAccelerationInfo = "Detecting hardware...";
    [ObservableProperty] private string _localModelName = "Llama 3.2 3B Instruct";
    [ObservableProperty] private string _localModelStatusText = "Checking...";
    [ObservableProperty] private string _openAiApiKey = "";
    [ObservableProperty] private string _anthropicApiKey = "";

    // First-run built-in model download (SLIM installer ships without the GGUF)
    [ObservableProperty] private bool _canDownloadLocalModel;
    [ObservableProperty] private bool _isDownloadingLocalModel;
    [ObservableProperty] private double _localModelDownloadProgress;
    [ObservableProperty] private string _localModelDownloadStatus = "";

    // ── Step 4: Summary ──────────────────────────────────────
    [ObservableProperty] private string _summaryOllamaStatus = "Not configured";
    [ObservableProperty] private string _summaryChatModel = "Default (llama3.2)";
    [ObservableProperty] private string _summaryEmbeddingModel = "Default (all-minilm)";
    [ObservableProperty] private string _summaryLocalModel = "Not detected";
    [ObservableProperty] private string _summaryCloudProviders = "None configured";

    public OnboardingViewModel(
        IAiService aiService,
        ISettingsService settingsService,
        IHardwareDetector hardwareDetector,
        IBuiltInModelBootstrap builtInModelBootstrap)
    {
        _aiService = aiService;
        _settingsService = settingsService;
        _hardwareDetector = hardwareDetector;
        _bootstrap = builtInModelBootstrap;
        Log.Debug("OnboardingViewModel created");
    }

    // ═══════════════════════════════════════════════════════════
    //  STEP NAVIGATION
    // ═══════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task NextStepAsync()
    {
        if (CurrentStep >= 4) return;

        CurrentStep++;
        UpdateStepVisibility();
        UpdateNavigationState();

        Log.Debug("Onboarding advanced to step {Step}", CurrentStep);

        // Trigger step-specific logic when entering a step
        if (CurrentStep == 2)
        {
            await LoadModelsAsync();
        }
        else if (CurrentStep == 3)
        {
            await CheckBuiltInModelAsync();
        }
        else if (CurrentStep == 4)
        {
            BuildSummary();
        }
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep <= 0) return;

        CurrentStep--;
        UpdateStepVisibility();
        UpdateNavigationState();

        Log.Debug("Onboarding returned to step {Step}", CurrentStep);
    }

    [RelayCommand]
    private async Task SkipOllamaSetupAsync()
    {
        Log.Information("User skipped Ollama setup during onboarding");
        IsOllamaConnected = null;
        ConnectionStatusText = "";
        ShowConnectionStatus = false;

        // Advance to model selection step
        CurrentStep = 2;
        UpdateStepVisibility();
        UpdateNavigationState();
        await LoadModelsAsync();
    }

    private void UpdateStepVisibility()
    {
        IsStep0Visible = CurrentStep == 0;
        IsStep1Visible = CurrentStep == 1;
        IsStep2Visible = CurrentStep == 2;
        IsStep3Visible = CurrentStep == 3;
        IsStep4Visible = CurrentStep == 4;
        ShowNextButton = CurrentStep >= 1 && CurrentStep <= 3;
    }

    private void UpdateNavigationState()
    {
        CanGoBack = CurrentStep > 0;
        CanGoNext = CurrentStep < 4;
    }

    // ═══════════════════════════════════════════════════════════
    //  STEP 1: OLLAMA CONNECTION
    // ═══════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (IsTestingConnection) return;

        IsTestingConnection = true;
        ShowConnectionStatus = true;
        ConnectionStatusText = "Testing connection...";
        Log.Information("Testing Ollama connection at {Endpoint}", OllamaEndpoint);

        try
        {
            // Update the settings with the user's chosen endpoint before testing
            var settings = await _settingsService.GetSettingsAsync();
            settings.OllamaEndpoint = OllamaEndpoint;
            await _settingsService.SaveSettingsAsync(settings);

            var connected = await _aiService.ActiveProvider.CheckConnectionAsync();
            IsOllamaConnected = connected;
            ConnectionStatusText = connected
                ? "Connected to Ollama successfully!"
                : "Could not connect to Ollama. Make sure Ollama is running.";

            Log.Information("Ollama connection test result: {Connected}", connected);
        }
        catch (Exception ex)
        {
            IsOllamaConnected = false;
            ConnectionStatusText = "Connection failed. Check that Ollama is installed and running.";
            Log.Warning(ex, "Ollama connection test failed");
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  STEP 2: MODEL SELECTION
    // ═══════════════════════════════════════════════════════════

    private async Task LoadModelsAsync()
    {
        IsLoadingModels = true;
        AvailableModels.Clear();

        try
        {
            // Load hardware info
            var hw = await _hardwareDetector.DetectAsync();
            HardwareInfo = $"{hw.GpuName}  |  {hw.TotalRamFormatted} RAM  |  {hw.RecommendedMaxModelSize}";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to detect hardware during onboarding");
            HardwareInfo = "Hardware detection unavailable";
        }

        // Only load models if Ollama is connected
        if (IsOllamaConnected == true)
        {
            try
            {
                var models = await _aiService.ActiveProvider.ListModelsAsync();
                foreach (var model in models)
                {
                    var isEmbedding = model.Name.Contains("minilm", StringComparison.OrdinalIgnoreCase)
                        || model.Name.Contains("nomic", StringComparison.OrdinalIgnoreCase)
                        || model.Name.Contains("embed", StringComparison.OrdinalIgnoreCase)
                        || model.Name.Contains("bge", StringComparison.OrdinalIgnoreCase);

                    var isRecommended = model.Name.Contains("llama3", StringComparison.OrdinalIgnoreCase)
                        || model.Name.Contains("mistral", StringComparison.OrdinalIgnoreCase)
                        || model.Name.Contains("phi", StringComparison.OrdinalIgnoreCase)
                        || isEmbedding;

                    AvailableModels.Add(new OnboardingModelItem
                    {
                        ModelId = model.Id,
                        DisplayName = model.Name,
                        Description = $"{model.SizeFormatted} | {model.Family}",
                        IsRecommended = isRecommended,
                        IsEmbeddingModel = isEmbedding
                    });
                }

                HasModels = AvailableModels.Count > 0;

                // Auto-select first recommended chat and embedding models
                if (string.IsNullOrEmpty(SelectedChatModel))
                {
                    var recommended = AvailableModels
                        .FirstOrDefault(m => m.IsRecommended && !m.IsEmbeddingModel);
                    if (recommended != null) SelectedChatModel = recommended.ModelId;
                }

                if (string.IsNullOrEmpty(SelectedEmbeddingModel))
                {
                    var embedding = AvailableModels
                        .FirstOrDefault(m => m.IsEmbeddingModel);
                    if (embedding != null) SelectedEmbeddingModel = embedding.ModelId;
                }

                Log.Information("Loaded {Count} models during onboarding", AvailableModels.Count);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load models during onboarding");
                HasModels = false;
            }
        }
        else
        {
            HasModels = false;
        }

        IsLoadingModels = false;
    }

    // ═══════════════════════════════════════════════════════════
    //  STEP 3: BUILT-IN AI MODEL & CLOUD API KEYS
    // ═══════════════════════════════════════════════════════════

    private async Task CheckBuiltInModelAsync()
    {
        LocalModelStatusText = "Checking...";
        CanDownloadLocalModel = false;

        try
        {
            // The SLIM installer ships without the model; the OFFLINE installer pre-places it.
            // IsInstalled() treats a truncated leftover as "not installed", so we re-offer download.
            IsLocalModelAvailable = _bootstrap.IsInstalled();

            if (IsLocalModelAvailable)
            {
                var sizeMb = new FileInfo(_bootstrap.ModelPath).Length / 1_000_000.0;
                LocalModelStatusText = $"Ready ({sizeMb:F0} MB)";
            }
            else
            {
                LocalModelStatusText =
                    "Not installed yet. Download it for fully-offline AI (~1.9 GB), or just add a cloud API key below.";
                CanDownloadLocalModel = true;
            }

            // Load any existing API keys from settings
            var settings = await _settingsService.GetSettingsAsync();
            if (!string.IsNullOrEmpty(settings.OpenAiApiKey))
            {
                OpenAiApiKey = settings.OpenAiApiKey;
            }
            if (!string.IsNullOrEmpty(settings.AnthropicApiKey))
            {
                AnthropicApiKey = settings.AnthropicApiKey;
            }

            // Detect hardware for GPU info
            var hw = await _hardwareDetector.DetectAsync();
            GpuAccelerationInfo = hw.GpuAccelerationSummary;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check built-in model during onboarding");
            LocalModelStatusText = "Unable to verify model";
            GpuAccelerationInfo = "Hardware detection unavailable";
        }
    }

    /// <summary>
    /// Downloads the built-in GGUF model on demand (first-run flow for the SLIM installer).
    /// Progress is marshalled to the UI thread; on success the local provider becomes active.
    /// </summary>
    [RelayCommand]
    private async Task DownloadLocalModelAsync()
    {
        if (IsDownloadingLocalModel) return;

        IsDownloadingLocalModel = true;
        CanDownloadLocalModel = false;
        LocalModelDownloadProgress = 0;
        LocalModelDownloadStatus = "Starting download…";
        Log.Information("User started built-in model download during onboarding");

        try
        {
            var dispatcher = App.MainWindow?.DispatcherQueue;
            var progress = new Progress<ModelDownloadProgress>(p =>
            {
                void Apply()
                {
                    LocalModelDownloadProgress = p.PercentComplete;
                    LocalModelDownloadStatus = FormatModelDownloadStatus(p);
                }

                if (dispatcher is not null) dispatcher.TryEnqueue(Apply);
                else Apply();
            });

            await _bootstrap.EnsureInstalledAsync(progress);

            IsLocalModelAvailable = _bootstrap.IsInstalled();
            if (IsLocalModelAvailable)
            {
                var sizeMb = new FileInfo(_bootstrap.ModelPath).Length / 1_000_000.0;
                LocalModelStatusText = $"Ready ({sizeMb:F0} MB)";
                LocalModelDownloadStatus = "Download complete.";

                // Make the freshly-downloaded built-in model the active provider.
                var settings = await _settingsService.GetSettingsAsync();
                settings.ActiveProviderId = "local";
                await _settingsService.SaveSettingsAsync(settings);
                try
                {
                    await _aiService.InitializeAsync();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "AI service re-init after model download failed");
                }
            }
            else
            {
                LocalModelDownloadStatus = "Download did not complete. You can retry or use cloud models.";
                CanDownloadLocalModel = true;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Built-in model download failed during onboarding");
            LocalModelDownloadStatus = $"Download failed: {ex.Message}";
            CanDownloadLocalModel = true;
        }
        finally
        {
            IsDownloadingLocalModel = false;
        }
    }

    private static string FormatModelDownloadStatus(ModelDownloadProgress p)
    {
        if (p.TotalBytes <= 0) return p.Status;
        var done = p.CompletedBytes / 1_000_000_000.0;
        var total = p.TotalBytes / 1_000_000_000.0;
        return $"{done:F2} / {total:F2} GB  ({p.PercentComplete:F0}%)";
    }

    // ═══════════════════════════════════════════════════════════
    //  STEP 4: SUMMARY & COMPLETION
    // ═══════════════════════════════════════════════════════════

    private void BuildSummary()
    {
        SummaryOllamaStatus = IsOllamaConnected == true
            ? $"Connected ({OllamaEndpoint})"
            : "Not connected (can be configured in Settings)";

        SummaryChatModel = !string.IsNullOrEmpty(SelectedChatModel)
            ? SelectedChatModel
            : "Default (llama3.2)";

        SummaryEmbeddingModel = !string.IsNullOrEmpty(SelectedEmbeddingModel)
            ? SelectedEmbeddingModel
            : "Default (all-minilm)";

        SummaryLocalModel = IsLocalModelAvailable
            ? $"{LocalModelName} (ready)"
            : "Not found (reinstall to restore)";

        var cloudProviders = new List<string>();
        if (!string.IsNullOrWhiteSpace(OpenAiApiKey))
            cloudProviders.Add("OpenAI");
        if (!string.IsNullOrWhiteSpace(AnthropicApiKey))
            cloudProviders.Add("Anthropic");

        SummaryCloudProviders = cloudProviders.Count > 0
            ? string.Join(", ", cloudProviders)
            : "None (can be added later in Settings)";
    }

    [RelayCommand]
    private async Task CompleteOnboardingAsync()
    {
        Log.Information("Completing onboarding wizard");

        try
        {
            var settings = await _settingsService.GetSettingsAsync();

            // Save Ollama endpoint
            settings.OllamaEndpoint = OllamaEndpoint;

            // Save API keys (trimmed, null if empty)
            var openAiKey = OpenAiApiKey?.Trim();
            var anthropicKey = AnthropicApiKey?.Trim();
            settings.OpenAiApiKey = string.IsNullOrEmpty(openAiKey) ? null : openAiKey;
            settings.AnthropicApiKey = string.IsNullOrEmpty(anthropicKey) ? null : anthropicKey;

            // Set active provider based on what's available
            if (IsLocalModelAvailable)
            {
                settings.ActiveProviderId = "local";
            }
            else if (IsOllamaConnected == true)
            {
                settings.ActiveProviderId = "ollama";
            }
            else if (!string.IsNullOrEmpty(settings.OpenAiApiKey))
            {
                settings.ActiveProviderId = "openai";
            }
            else if (!string.IsNullOrEmpty(settings.AnthropicApiKey))
            {
                settings.ActiveProviderId = "anthropic";
            }

            // Save selected models (if user picked any)
            if (!string.IsNullOrEmpty(SelectedChatModel))
            {
                settings.DefaultModel = SelectedChatModel;
            }

            if (!string.IsNullOrEmpty(SelectedEmbeddingModel))
            {
                settings.EmbeddingModel = SelectedEmbeddingModel;
            }

            // Mark onboarding as complete
            settings.OnboardingCompleted = true;

            await _settingsService.SaveSettingsAsync(settings);

            // Re-initialize AI service to pick up new settings
            try
            {
                await _aiService.InitializeAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AI service re-initialization failed during onboarding completion");
            }

            Log.Information("Onboarding settings saved. Navigating to Dashboard");

            // Navigate to Dashboard via MainWindow
            if (App.MainWindow is MainWindow mainWindow)
            {
                mainWindow.CompleteOnboarding();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to complete onboarding");
        }
    }
}

// ═══════════════════════════════════════════════════════════════════
//  DISPLAY ITEM CLASSES
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Represents an AI model available for selection during onboarding.
/// </summary>
public class OnboardingModelItem
{
    public string ModelId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsRecommended { get; init; }
    public bool IsEmbeddingModel { get; init; }
    public bool IsSelected { get; set; }
}
