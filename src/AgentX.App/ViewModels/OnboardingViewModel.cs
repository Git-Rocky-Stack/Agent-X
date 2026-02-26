using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.Services.Settings;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class OnboardingViewModel : ObservableObject
{
    // ── Services ─────────────────────────────────────────────
    private readonly IAiService _aiService;
    private readonly ISettingsService _settingsService;
    private readonly IHardwareDetector _hardwareDetector;

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
    [ObservableProperty] private bool? _isOllamaConnected;
    [ObservableProperty] private string _connectionStatusText = "";
    [ObservableProperty] private bool _showConnectionStatus;

    // ── Step 2: Model Selection ──────────────────────────────
    [ObservableProperty] private ObservableCollection<OnboardingModelItem> _availableModels = new();
    [ObservableProperty] private string _selectedChatModel = "";
    [ObservableProperty] private string _selectedEmbeddingModel = "";
    [ObservableProperty] private bool _hasModels;
    [ObservableProperty] private bool _isLoadingModels;
    [ObservableProperty] private string _hardwareInfo = "";

    // ── Step 4: Summary ──────────────────────────────────────
    [ObservableProperty] private string _summaryOllamaStatus = "Not configured";
    [ObservableProperty] private string _summaryChatModel = "Default (llama3.2)";
    [ObservableProperty] private string _summaryEmbeddingModel = "Default (all-minilm)";

    public OnboardingViewModel(
        IAiService aiService,
        ISettingsService settingsService,
        IHardwareDetector hardwareDetector)
    {
        _aiService = aiService;
        _settingsService = settingsService;
        _hardwareDetector = hardwareDetector;
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

            // Set the active model if one was selected and Ollama is connected
            if (IsOllamaConnected == true && !string.IsNullOrEmpty(SelectedChatModel))
            {
                try
                {
                    await _aiService.SetActiveModelAsync(SelectedChatModel);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to set active model during onboarding completion");
                }
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
