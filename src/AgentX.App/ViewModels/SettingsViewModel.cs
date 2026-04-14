using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.AI.Routing;
using AgentX.Core.Services.License;
using AgentX.Core.Services.Security;
using AgentX.Core.Services.Settings;
using AgentX.App.Services;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILicenseService _licenseService;
    private readonly IAiService _aiService;
    private readonly ICostTracker _costTracker;
    private readonly IThemeService _themeService;
    private readonly ISecurityStatusService _securityStatusService;
    private readonly IModelRouterService? _modelRouterService;

    // ── Active Provider ──────────────────────────────────────
    [ObservableProperty] private int _activeProviderIndex;
    [ObservableProperty] private string _activeProviderId = "ollama";

    // ── Ollama ────────────────────────────────────────────────
    [ObservableProperty] private string _ollamaEndpoint = "http://localhost:11434";
    [ObservableProperty] private string _defaultModel = "llama3.2";
    [ObservableProperty] private string _embeddingModel = "all-minilm";
    [ObservableProperty] private string _storagePath = string.Empty;
    [ObservableProperty] private string _ollamaConnectionStatus = string.Empty;

    // ── OpenAI ────────────────────────────────────────────────
    [ObservableProperty] private string _openAiApiKey = string.Empty;
    [ObservableProperty] private string _openAiEndpoint = "https://api.openai.com/v1/";
    [ObservableProperty] private string _openAiDefaultModel = "gpt-4o-mini";
    [ObservableProperty] private string _openAiConnectionStatus = string.Empty;

    // ── Anthropic ─────────────────────────────────────────────
    [ObservableProperty] private string _anthropicApiKey = string.Empty;
    [ObservableProperty] private string _anthropicEndpoint = "https://api.anthropic.com/v1/";
    [ObservableProperty] private string _anthropicDefaultModel = "claude-sonnet-4-20250514";
    [ObservableProperty] private string _anthropicConnectionStatus = string.Empty;

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
    [ObservableProperty] private int _themeIndex;

    // ── Cost Tracking ────────────────────────────────────────
    [ObservableProperty] private string _totalCostDisplay = "$0.00";
    [ObservableProperty] private string _todayCostDisplay = "$0.00";
    [ObservableProperty] private string _totalTokensDisplay = "0";

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

    // ── Security Status ────────────────────────────────────
    [ObservableProperty] private bool _areKeysEncrypted;
    [ObservableProperty] private string _encryptionStatusDescription = string.Empty;

    // ── Multi-Model Routing ──────────────────────────────
    [ObservableProperty] private bool _enableModelRouting;
    [ObservableProperty] private string _activeRoutingProfileId = "balanced";
    [ObservableProperty] private int _routingProfileIndex;

    // ── App Info ────────────────────────────────────────────
    [ObservableProperty] private string _appVersion = "1.0.0";

    /// <summary>
    /// Provider display names for the ComboBox items.
    /// Order must match the index mapping in ProviderIndexToId / ProviderIdToIndex.
    /// </summary>
    public List<string> ProviderOptions { get; } = new() { "Ollama (Local)", "OpenAI", "Anthropic Claude" };
    public List<string> ThemeOptions { get; } = new() { "Dark", "Light", "System Default" };

    /// <summary>
    /// Routing profile display names for the ComboBox. Order must match RoutingProfileIndexToId.
    /// </summary>
    public List<string> RoutingProfileOptions { get; } = RoutingProfile.AllDefaults
        .Select(p => p.DisplayName).ToList();

    public SettingsViewModel(
        ISettingsService settingsService,
        ILicenseService licenseService,
        IAiService aiService,
        ICostTracker costTracker,
        IThemeService themeService,
        ISecurityStatusService securityStatusService,
        IModelRouterService? modelRouterService = null)
    {
        _settingsService = settingsService;
        _licenseService = licenseService;
        _aiService = aiService;
        _costTracker = costTracker;
        _themeService = themeService;
        _securityStatusService = securityStatusService;
        _modelRouterService = modelRouterService;

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
            // Provider settings
            ActiveProviderId = settings.ActiveProviderId ?? "ollama";
            ActiveProviderIndex = ProviderIdToIndex(ActiveProviderId);

            // Ollama
            OllamaEndpoint = settings.OllamaEndpoint;
            DefaultModel = settings.DefaultModel;
            EmbeddingModel = settings.EmbeddingModel;

            // OpenAI
            OpenAiApiKey = settings.OpenAiApiKey ?? string.Empty;
            OpenAiEndpoint = settings.OpenAiEndpoint;
            OpenAiDefaultModel = settings.OpenAiDefaultModel ?? "gpt-4o-mini";

            // Anthropic
            AnthropicApiKey = settings.AnthropicApiKey ?? string.Empty;
            AnthropicEndpoint = settings.AnthropicEndpoint;
            AnthropicDefaultModel = settings.AnthropicDefaultModel ?? "claude-sonnet-4-20250514";

            // Inference
            Temperature = settings.Temperature;
            MaxTokens = settings.MaxTokens;
            ContextWindow = settings.ContextWindow;

            // Indexing
            ChunkSize = settings.ChunkSize;
            ChunkOverlap = settings.ChunkOverlap;
            TopKResults = settings.TopKResults;
            AutoIndexWatchFolders = settings.AutoIndexWatchFolders;

            // Multi-Model Routing
            EnableModelRouting = settings.EnableModelRouting;
            ActiveRoutingProfileId = settings.ActiveRoutingProfileId ?? "balanced";
            RoutingProfileIndex = RoutingProfileIdToIndex(ActiveRoutingProfileId);
        }

        // Load cost tracking data
        RefreshCostDisplay();

        // Load current license info
        await LoadLicenseInfoAsync();

        // Load theme preference
        ThemeIndex = _themeService.CurrentTheme switch
        {
            Microsoft.UI.Xaml.ElementTheme.Dark => 0,
            Microsoft.UI.Xaml.ElementTheme.Light => 1,
            _ => 2
        };

        // Load security status
        AreKeysEncrypted = _securityStatusService.AreKeysEncrypted;
        EncryptionStatusDescription = _securityStatusService.GetEncryptionStatusDescription();

        Log.Information("Settings loaded");
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        // Load existing settings to preserve fields not managed by this view
        // (e.g. OnboardingCompleted, StoragePath)
        var settings = await _settingsService.GetSettingsAsync();

        // Resolve provider ID from the selected ComboBox index
        var resolvedProviderId = ProviderIndexToId(ActiveProviderIndex);

        // Provider
        settings.ActiveProviderId = resolvedProviderId;

        // Ollama
        settings.OllamaEndpoint = OllamaEndpoint;
        settings.DefaultModel = DefaultModel;
        settings.EmbeddingModel = EmbeddingModel;

        // OpenAI
        settings.OpenAiApiKey = string.IsNullOrWhiteSpace(OpenAiApiKey) ? null : OpenAiApiKey;
        settings.OpenAiEndpoint = OpenAiEndpoint;
        settings.OpenAiDefaultModel = OpenAiDefaultModel;

        // Anthropic
        settings.AnthropicApiKey = string.IsNullOrWhiteSpace(AnthropicApiKey) ? null : AnthropicApiKey;
        settings.AnthropicEndpoint = AnthropicEndpoint;
        settings.AnthropicDefaultModel = AnthropicDefaultModel;

        // Inference
        settings.Temperature = Temperature;
        settings.MaxTokens = MaxTokens;
        settings.ContextWindow = ContextWindow;

        // Indexing
        settings.ChunkSize = ChunkSize;
        settings.ChunkOverlap = ChunkOverlap;
        settings.TopKResults = TopKResults;
        settings.AutoIndexWatchFolders = AutoIndexWatchFolders;

        // Multi-Model Routing
        settings.EnableModelRouting = EnableModelRouting;
        settings.ActiveRoutingProfileId = RoutingProfileIndexToId(RoutingProfileIndex);
        ActiveRoutingProfileId = settings.ActiveRoutingProfileId;

        await _settingsService.SaveSettingsAsync(settings);

        // Re-initialize AI service so provider changes take effect
        try
        {
            await _aiService.InitializeAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AI service re-initialization failed after settings save");
        }

        Log.Information("Settings saved with active provider: {Provider}", resolvedProviderId);
    }

    [RelayCommand]
    private async Task TestOllamaConnectionAsync()
    {
        OllamaConnectionStatus = "Testing...";
        try
        {
            // Always create a temporary provider with the current endpoint value
            // (the user may have edited the endpoint but not saved yet)
            using var tempProvider = new AgentX.Core.AI.Providers.OllamaProvider(
                new Uri(OllamaEndpoint), Log.Logger);
            var connected = await tempProvider.CheckConnectionAsync();
            OllamaConnectionStatus = connected ? "Connected" : "Not reachable";
        }
        catch (Exception ex)
        {
            OllamaConnectionStatus = $"Error: {ex.Message}";
            Log.Warning(ex, "Ollama connection test failed");
        }
    }

    [RelayCommand]
    private async Task TestOpenAiConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(OpenAiApiKey))
        {
            OpenAiConnectionStatus = "API key required";
            return;
        }

        OpenAiConnectionStatus = "Testing...";
        try
        {
            using var tempProvider = new AgentX.Core.AI.Providers.OpenAiProvider(
                OpenAiApiKey, OpenAiEndpoint, Log.Logger);
            var connected = await tempProvider.CheckConnectionAsync();
            OpenAiConnectionStatus = connected ? "Connected" : "Authentication failed";
        }
        catch (Exception ex)
        {
            OpenAiConnectionStatus = $"Error: {ex.Message}";
            Log.Warning(ex, "OpenAI connection test failed");
        }
    }

    [RelayCommand]
    private async Task TestAnthropicConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(AnthropicApiKey))
        {
            AnthropicConnectionStatus = "API key required";
            return;
        }

        AnthropicConnectionStatus = "Testing...";
        try
        {
            using var tempProvider = new AgentX.Core.AI.Providers.AnthropicProvider(
                AnthropicApiKey, AnthropicEndpoint, Log.Logger);
            var connected = await tempProvider.CheckConnectionAsync();
            AnthropicConnectionStatus = connected ? "Connected" : "Authentication failed";
        }
        catch (Exception ex)
        {
            AnthropicConnectionStatus = $"Error: {ex.Message}";
            Log.Warning(ex, "Anthropic connection test failed");
        }
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
        // Provider defaults
        ActiveProviderIndex = 0; // Ollama
        ActiveProviderId = "ollama";

        // Ollama
        OllamaEndpoint = "http://localhost:11434";
        DefaultModel = "llama3.2";
        EmbeddingModel = "all-minilm";

        // OpenAI — clear key, keep default endpoint/model
        OpenAiApiKey = string.Empty;
        OpenAiEndpoint = "https://api.openai.com/v1/";
        OpenAiDefaultModel = "gpt-4o-mini";

        // Anthropic — clear key, keep default endpoint/model
        AnthropicApiKey = string.Empty;
        AnthropicEndpoint = "https://api.anthropic.com/v1/";
        AnthropicDefaultModel = "claude-sonnet-4-20250514";

        // Inference
        Temperature = 0.7;
        MaxTokens = 4096;
        ContextWindow = 8192;

        // Indexing
        ChunkSize = 512;
        ChunkOverlap = 50;
        TopKResults = 5;
        AutoIndexWatchFolders = true;

        // Multi-Model Routing
        EnableModelRouting = false;
        RoutingProfileIndex = 2; // Balanced
        ActiveRoutingProfileId = "balanced";

        // Clear connection statuses
        OllamaConnectionStatus = string.Empty;
        OpenAiConnectionStatus = string.Empty;
        AnthropicConnectionStatus = string.Empty;

        await SaveSettingsAsync();
        Log.Information("Settings reset to defaults");
    }

    // ── Private Helpers ─────────────────────────────────────

    /// <summary>
    /// Refreshes the cost display properties from the cost tracker.
    /// </summary>
    private void RefreshCostDisplay()
    {
        try
        {
            TotalCostDisplay = $"${_costTracker.GetTotalCostUsd():F4}";
            var todayStart = DateTime.UtcNow.Date;
            TodayCostDisplay = $"${_costTracker.GetCostForPeriod(todayStart, DateTime.UtcNow):F4}";
            var totalTokens = _costTracker.GetTotalInputTokens() + _costTracker.GetTotalOutputTokens();
            TotalTokensDisplay = totalTokens.ToString("N0");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to refresh cost display");
        }
    }

    /// <summary>
    /// Maps a provider ComboBox index to the provider ID string.
    /// </summary>
    private static string ProviderIndexToId(int index) => index switch
    {
        0 => "ollama",
        1 => "openai",
        2 => "anthropic",
        _ => "ollama"
    };

    /// <summary>
    /// Maps a provider ID string to the ComboBox index.
    /// </summary>
    private static int ProviderIdToIndex(string providerId) => providerId?.ToLowerInvariant() switch
    {
        "ollama" => 0,
        "openai" => 1,
        "anthropic" => 2,
        _ => 0
    };

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

    /// <summary>
    /// Reacts to theme ComboBox selection changes.
    /// Maps the index to an <see cref="Microsoft.UI.Xaml.ElementTheme"/> and
    /// applies it immediately via <see cref="IThemeService"/>.
    /// </summary>
    partial void OnThemeIndexChanged(int value)
    {
        var theme = value switch
        {
            0 => Microsoft.UI.Xaml.ElementTheme.Dark,
            1 => Microsoft.UI.Xaml.ElementTheme.Light,
            _ => Microsoft.UI.Xaml.ElementTheme.Default
        };
        _ = _themeService.SetThemeAsync(theme);
    }

    /// <summary>
    /// Reacts to routing profile ComboBox selection changes.
    /// Updates the active routing profile immediately if the router is available.
    /// </summary>
    partial void OnRoutingProfileIndexChanged(int value)
    {
        var profileId = RoutingProfileIndexToId(value);
        ActiveRoutingProfileId = profileId;
        _modelRouterService?.SetActiveProfile(profileId);
    }

    /// <summary>
    /// Maps a routing profile ComboBox index to the profile ID string.
    /// </summary>
    private static string RoutingProfileIndexToId(int index) => index switch
    {
        0 => "cost-optimized",
        1 => "quality-optimized",
        2 => "balanced",
        _ => "balanced"
    };

    /// <summary>
    /// Maps a routing profile ID string to the ComboBox index.
    /// </summary>
    private static int RoutingProfileIdToIndex(string profileId) => profileId?.ToLowerInvariant() switch
    {
        "cost-optimized" => 0,
        "quality-optimized" => 1,
        "balanced" => 2,
        _ => 2
    };
}
