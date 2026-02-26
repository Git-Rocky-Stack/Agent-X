using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.Services.License;
using AgentX.Core.Services.Settings;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILicenseService _licenseService;

    // ── General ─────────────────────────────────────────────
    [ObservableProperty] private string _ollamaEndpoint = "http://localhost:11434";
    [ObservableProperty] private string _defaultModel = "llama3.2";
    [ObservableProperty] private string _embeddingModel = "all-minilm";
    [ObservableProperty] private string _storagePath = string.Empty;

    // ── Inference ───────────────────────────────────────────
    [ObservableProperty] private double _temperature = 0.7;
    [ObservableProperty] private int _maxTokens = 4096;
    [ObservableProperty] private int _contextWindow = 8192;

    // ── Indexing ────────────────────────────────────────────
    [ObservableProperty] private int _chunkSize = 512;
    [ObservableProperty] private int _chunkOverlap = 50;
    [ObservableProperty] private int _topKResults = 5;
    [ObservableProperty] private bool _autoIndexWatchFolders = true;

    // ── Appearance ──────────────────────────────────────────
    [ObservableProperty] private bool _compactMode;

    // ── License ─────────────────────────────────────────────
    [ObservableProperty] private string _licenseKey = string.Empty;
    [ObservableProperty] private string _licenseTier = "Trial";
    [ObservableProperty] private bool _isLicenseValid;
    [ObservableProperty] private string _licenseStatusMessage = string.Empty;
    [ObservableProperty] private string _licenseCustomerName = string.Empty;
    [ObservableProperty] private string _licenseCustomerEmail = string.Empty;
    [ObservableProperty] private string _licenseActivatedAt = string.Empty;
    [ObservableProperty] private string _licenseDocumentLimit = "50";
    [ObservableProperty] private string _licenseBadgeColor = "#666666";

    // ── App Info ────────────────────────────────────────────
    [ObservableProperty] private string _appVersion = "1.0.0";

    public SettingsViewModel(ISettingsService settingsService, ILicenseService licenseService)
    {
        _settingsService = settingsService;
        _licenseService = licenseService;

        StoragePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentX");

        Log.Debug("SettingsViewModel created");
    }

    public async Task InitializeAsync()
    {
        var settings = await _settingsService.GetSettingsAsync();
        if (settings != null)
        {
            OllamaEndpoint = settings.OllamaEndpoint;
            DefaultModel = settings.DefaultModel;
            EmbeddingModel = settings.EmbeddingModel;
            Temperature = settings.Temperature;
            MaxTokens = settings.MaxTokens;
            ContextWindow = settings.ContextWindow;
            ChunkSize = settings.ChunkSize;
            ChunkOverlap = settings.ChunkOverlap;
            TopKResults = settings.TopKResults;
            AutoIndexWatchFolders = settings.AutoIndexWatchFolders;
        }

        // Load current license info
        await LoadLicenseInfoAsync();

        Log.Information("Settings loaded");
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        var settings = new AppSettings
        {
            OllamaEndpoint = OllamaEndpoint,
            DefaultModel = DefaultModel,
            EmbeddingModel = EmbeddingModel,
            Temperature = Temperature,
            MaxTokens = MaxTokens,
            ContextWindow = ContextWindow,
            ChunkSize = ChunkSize,
            ChunkOverlap = ChunkOverlap,
            TopKResults = TopKResults,
            AutoIndexWatchFolders = AutoIndexWatchFolders,
        };
        await _settingsService.SaveSettingsAsync(settings);
        Log.Information("Settings saved");
    }

    [RelayCommand]
    private async Task ValidateLicenseAsync()
    {
        if (string.IsNullOrWhiteSpace(LicenseKey))
        {
            LicenseStatusMessage = "Please enter a license key.";
            return;
        }

        Log.Debug("License activation requested for key: {KeyPrefix}...",
            LicenseKey[..Math.Min(8, LicenseKey.Length)]);

        LicenseStatusMessage = "Activating license...";

        var result = await _licenseService.ActivateLicenseAsync(LicenseKey);

        LicenseStatusMessage = result.Message;

        if (result.Success && result.LicenseInfo != null)
        {
            ApplyLicenseInfo(result.LicenseInfo);
            Log.Information("License activated via UI: {Tier}", result.LicenseInfo.Tier);
        }
        else
        {
            Log.Warning("License activation failed: {Error}", result.Error);
        }
    }

    [RelayCommand]
    private async Task DeactivateLicenseAsync()
    {
        Log.Debug("License deactivation requested");

        var success = await _licenseService.DeactivateLicenseAsync();

        if (success)
        {
            var trialInfo = await _licenseService.GetCurrentLicenseAsync();
            ApplyLicenseInfo(trialInfo);
            LicenseKey = string.Empty;
            LicenseStatusMessage = "License deactivated. Reverted to Trial tier.";
            Log.Information("License deactivated via UI");
        }
        else
        {
            LicenseStatusMessage = "Failed to deactivate license. Please try again.";
            Log.Warning("License deactivation failed via UI");
        }
    }

    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        OllamaEndpoint = "http://localhost:11434";
        DefaultModel = "llama3.2";
        EmbeddingModel = "all-minilm";
        Temperature = 0.7;
        MaxTokens = 4096;
        ContextWindow = 8192;
        ChunkSize = 512;
        ChunkOverlap = 50;
        TopKResults = 5;
        AutoIndexWatchFolders = true;

        await SaveSettingsAsync();
        Log.Information("Settings reset to defaults");
    }

    // ── Private Helpers ─────────────────────────────────────

    private async Task LoadLicenseInfoAsync()
    {
        try
        {
            var licenseInfo = await _licenseService.GetCurrentLicenseAsync();
            ApplyLicenseInfo(licenseInfo);
            Log.Debug("License info loaded: {Tier}, Activated={IsActivated}",
                licenseInfo.Tier, licenseInfo.IsActivated);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load license info");
            LicenseTier = "Trial";
            IsLicenseValid = false;
            LicenseDocumentLimit = "50";
            LicenseBadgeColor = "#666666";
        }
    }

    private void ApplyLicenseInfo(LicenseInfo info)
    {
        LicenseTier = info.TierDisplayName;
        IsLicenseValid = info.IsActivated;
        LicenseCustomerName = info.CustomerName ?? string.Empty;
        LicenseCustomerEmail = info.CustomerEmail ?? string.Empty;
        LicenseActivatedAt = info.ActivatedAt?.ToString("MMMM d, yyyy") ?? string.Empty;
        LicenseDocumentLimit = info.DocumentLimitDisplay;
        LicenseBadgeColor = info.TierBadgeColor;
    }
}
