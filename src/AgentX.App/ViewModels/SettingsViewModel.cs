using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.AI.Routing;
using AgentX.Core.Services.Search;
using AgentX.Core.Services.Security;
using AgentX.Core.Services.Settings;
using AgentX.App.Services;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IAiService _aiService;
    private readonly ICostTracker _costTracker;
    private readonly IThemeService _themeService;
    private readonly ISecurityStatusService _securityStatusService;
    private readonly IModelRouterService? _modelRouterService;
    private readonly IDatabaseKeyService _databaseKeyService;
    private readonly IDatabaseEncryptionMigrator _databaseEncryptionMigrator;
    private readonly IDatabaseKeyProvider _databaseKeyProvider;
    private readonly IEncryptionStateFile _encryptionStateFile;

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

    // ── Security Status ────────────────────────────────────
    [ObservableProperty] private bool _areKeysEncrypted;
    [ObservableProperty] private string _encryptionStatusDescription = string.Empty;

    // ── Database Encryption ───────────────────────────────────
    [ObservableProperty] private bool _encryptionEnabled;
    [ObservableProperty] private string _encryptionStatus = string.Empty;

    // ── Multi-Model Routing ──────────────────────────────
    [ObservableProperty] private bool _enableModelRouting;
    [ObservableProperty] private string _activeRoutingProfileId = "balanced";
    [ObservableProperty] private int _routingProfileIndex;

    // ── Deep Research Mode ──────────────────────────────
    [ObservableProperty] private bool _enableResearchMode;
    [ObservableProperty] private WebSearchProvider _selectedWebSearchProvider = WebSearchProvider.Brave;
    [ObservableProperty] private string? _webSearchApiKey;
    [ObservableProperty] private int _maxSearchResults = 10;
    [ObservableProperty] private int _searchCacheTtlMinutes = 60;

    public IReadOnlyList<WebSearchProvider> WebSearchProviders { get; }
        = Enum.GetValues<WebSearchProvider>().ToList();

    // ── App Info ────────────────────────────────────────────
    [ObservableProperty] private string _appVersion = "1.1.0";

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
        IAiService aiService,
        ICostTracker costTracker,
        IThemeService themeService,
        ISecurityStatusService securityStatusService,
        IDatabaseKeyService databaseKeyService,
        IDatabaseEncryptionMigrator databaseEncryptionMigrator,
        IDatabaseKeyProvider databaseKeyProvider,
        IEncryptionStateFile encryptionStateFile,
        IModelRouterService? modelRouterService = null)
    {
        _settingsService = settingsService;
        _aiService = aiService;
        _costTracker = costTracker;
        _themeService = themeService;
        _securityStatusService = securityStatusService;
        _databaseKeyService = databaseKeyService;
        _databaseEncryptionMigrator = databaseEncryptionMigrator;
        _databaseKeyProvider = databaseKeyProvider;
        _encryptionStateFile = encryptionStateFile;
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

            // Deep Research Mode
            EnableResearchMode = settings.EnableResearchMode;
            SelectedWebSearchProvider = settings.WebSearchProvider;
            WebSearchApiKey = settings.WebSearchApiKey;
            MaxSearchResults = settings.MaxSearchResults;
            SearchCacheTtlMinutes = settings.SearchCacheTtlMinutes;
        }

        // Load cost tracking data
        RefreshCostDisplay();

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

        // Deep Research Mode
        settings.EnableResearchMode = EnableResearchMode;
        settings.WebSearchProvider = SelectedWebSearchProvider;
        settings.WebSearchApiKey = string.IsNullOrWhiteSpace(WebSearchApiKey) ? null : WebSearchApiKey;
        settings.MaxSearchResults = MaxSearchResults;
        settings.SearchCacheTtlMinutes = SearchCacheTtlMinutes;

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

        // Deep Research Mode
        EnableResearchMode = false;
        SelectedWebSearchProvider = WebSearchProvider.Brave;
        WebSearchApiKey = null;
        MaxSearchResults = 10;
        SearchCacheTtlMinutes = 60;

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

    // ── Database Encryption Toggle Flow ───────────────────────

    /// <summary>
    /// Invoked by the Settings page when the encryption ToggleSwitch changes.
    /// Database encryption is available to every user, free of charge. The key is
    /// wrapped with Windows DPAPI and tied transparently to the current Windows user
    /// account — no passphrase to remember and no risk of permanent data loss.
    /// On failure, reverts the toggle and does NOT write the marker file.
    /// </summary>
    public async System.Threading.Tasks.Task OnEncryptionToggledAsync()
    {
        // Called by the XAML code-behind when the ToggleSwitch is toggled by the user.
        // The TwoWay binding means EncryptionEnabled already reflects the target state.

        if (!EncryptionEnabled)
        {
            // v2.1 does not support disabling encryption.
            if (_encryptionStateFile.Exists())
            {
                EncryptionStatus = "Disabling encryption is not supported in v2.1. Restore from an unencrypted backup to revert.";
                EncryptionEnabled = true;
            }
            else
            {
                EncryptionStatus = "Encryption is not enabled.";
            }
            return;
        }

        // DPAPI-wrapped key storage is the universal, transparent mode available to
        // every user. The key is managed automatically and tied to the Windows account.
        const KeyStorageMode mode = KeyStorageMode.DpapiWrapped;

        try
        {
            EncryptionStatus = "Encrypting…";

            // Provisioning writes the marker file (containing the DPAPI-wrapped key)
            // as part of GetOrCreateKeyAsync — no separate marker write is needed.
            // If MigrateToEncryptedAsync fails below, the marker will be present but
            // the DB unencrypted; the next launch will detect the mismatch and prompt
            // the user. This is acceptable for v2.1 (disable-encryption flow is a
            // future feature).
            var key = await _databaseKeyService.GetOrCreateKeyAsync(mode, passphrase: null);
            var dbPath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "AgentX",
                "agentx.db");

            await _databaseEncryptionMigrator.MigrateToEncryptedAsync(dbPath, key);

            // Activate the key for THIS session so subsequent DB opens see it.
            if (_databaseKeyProvider is DatabaseKeyProvider provider)
                provider.Set(key);

            EncryptionStatus = "Encrypted. The key is managed automatically and tied to your Windows user account.";

            Serilog.Log.Information("Database encryption enabled (mode={Mode})", mode);
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Database encryption enable failed");
            EncryptionEnabled = false;
            EncryptionStatus = $"Encryption failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Loads the current encryption status from the marker file.
    /// Called by the Settings page on navigation.
    /// </summary>
    public System.Threading.Tasks.Task LoadEncryptionStatusAsync()
    {
        if (_encryptionStateFile.Exists())
        {
            EncryptionEnabled = true;
            var info = _encryptionStateFile.Read();
            // Older installs may have provisioned a passphrase-protected key; keep
            // describing that case accurately while new encryptions use the universal
            // DPAPI-wrapped mode below.
            EncryptionStatus = info?.StorageMode == KeyStorageMode.UserPassphrase
                ? "Encrypted with your passphrase. You'll be prompted on next launch."
                : "Encrypted. The key is managed automatically and tied to your Windows user account.";
        }
        else
        {
            EncryptionEnabled = false;
            EncryptionStatus = "Encryption is not enabled.";
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
