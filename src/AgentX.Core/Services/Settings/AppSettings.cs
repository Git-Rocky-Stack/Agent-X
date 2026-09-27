using AgentX.Core.AI.Providers;
using AgentX.Core.Services.Backup.Models;
using AgentX.Core.Services.Search;

namespace AgentX.Core.Services.Settings;

public class AppSettings
{
    // Onboarding
    public bool OnboardingCompleted { get; set; } = false;

    // Appearance - ElementTheme name ("Dark" Night Ops / "Light" Day Shift /
    // "Default" follows Windows). SettingsService.Get/SetValueAsync resolve
    // keys by AppSettings property name, so ThemeService must use "Theme".
    public string Theme { get; set; } = "Dark";

    // UI language picked in Settings ("en-US", "de", "es", "fr", "ja" or "zh-CN"). Null follows
    // the Windows display language. Applied at startup, before the shell is built.
    public string? LanguageOverride { get; set; }

    // AI Provider — Active selection ("local", "ollama", "openai", "anthropic")
    public string ActiveProviderId { get; set; } = "local";

    // Built-in Local LLM (LLamaSharp)
    public string LocalModelFileName { get; set; } = "llama-3.2-3b-instruct-q4_k_m.gguf";
    public int LocalContextSize { get; set; } = 8192;
    // Layers on the GPU: 0 = automatic (NVIDIA GPU detected by video memory), a positive count =
    // that many, negative = none (CPU only). See LocalLlmProvider.ResolveGpuLayers.
    public int LocalGpuLayers { get; set; } = 0;

    // Ollama Provider
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string DefaultModel { get; set; } = "llama3.2";
    public string EmbeddingModel { get; set; } = "all-minilm";

    // OpenAI Provider
    public string? OpenAiApiKey { get; set; }
    public string OpenAiEndpoint { get; set; } = "https://api.openai.com/v1/";
    public string? OpenAiDefaultModel { get; set; } = "gpt-4o-mini";

    // Anthropic Provider
    public string? AnthropicApiKey { get; set; }
    public string AnthropicEndpoint { get; set; } = "https://api.anthropic.com/v1/";
    public string? AnthropicDefaultModel { get; set; } = AnthropicProvider.DefaultModelId;

    // Inference
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 4096;
    public int ContextWindow { get; set; } = 8192;

    // Knowledge Vault
    public int ChunkSize { get; set; } = 512;
    public int ChunkOverlap { get; set; } = 50;
    public int TopKResults { get; set; } = 5;
    public bool AutoIndexWatchFolders { get; set; } = true;

    // Multi-Model Routing
    public bool EnableModelRouting { get; set; } = false;
    public string ActiveRoutingProfileId { get; set; } = "balanced";

    // Deep Research Mode
    public bool EnableResearchMode { get; set; } = false;
    public WebSearchProvider WebSearchProvider { get; set; } = WebSearchProvider.Brave;
    public string? WebSearchApiKey { get; set; }
    public int MaxSearchResults { get; set; } = 10;
    public int SearchCacheTtlMinutes { get; set; } = 60;

    // Screen Awareness
    public bool EnableScreenAwareness { get; set; } = false;

    // Local REST API (browser extension / companion integrations)
    // When enabled, an authenticated HTTP listener runs on localhost:9846. Every data route
    // requires the bearer token below; clients pair by pasting it. The token is generated on
    // first start and stored DPAPI-encrypted at rest, like the provider API keys.
    public bool LocalApiEnabled { get; set; } = true;
    public string? LocalApiToken { get; set; }

    // HNSW Vector Search
    public bool EnableHnswIndex { get; set; } = true;
    public int HnswM { get; set; } = 16;
    public int HnswEfConstruction { get; set; } = 200;
    // Minimum search breadth (ef). Queries already search at least max(EfConstruction, 2 x
    // candidates), so only a value above that widens the search (better recall, slower).
    public int HnswEfSearch { get; set; } = 50;
    public int HnswFallbackThreshold { get; set; } = 10000;

    // OAuth2 Configuration
    public OAuthSettings OAuth { get; set; } = new();

    // Calendar Connector
    public CalendarSettings CalendarConnector { get; set; } = new();

    // Email Connector
    public EmailSettings EmailConnector { get; set; } = new();

    // Scheduled backups (read by IBackupService.StartScheduledBackupsAsync). Off by default; the
    // optional archive password is stored DPAPI-encrypted like the other secrets.
    public BackupScheduleConfig BackupSchedule { get; set; } = new();

    // Storage
    public string StoragePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AgentX");
}

/// <summary>
/// OAuth2 provider configuration for third-party authentication.
/// Contains settings for Google and Microsoft providers, plus
/// global parameters like token refresh buffer and auth timeout.
/// </summary>
public class OAuthSettings
{
    public GoogleOAuthSettings Google { get; set; } = new();
    public MicrosoftOAuthSettings Microsoft { get; set; } = new();

    /// <summary>
    /// Number of minutes before token expiry at which a refresh is triggered.
    /// Prevents API calls from failing due to tokens that expire mid-request.
    /// </summary>
    public int TokenRefreshBufferMinutes { get; set; } = 5;

    /// <summary>
    /// Maximum number of seconds to wait for the user to complete the
    /// OAuth consent flow in the browser before timing out.
    /// </summary>
    public int AuthTimeoutSeconds { get; set; } = 300;
}

/// <summary>
/// Google OAuth2 settings, entered under OAuth App Credentials on the Calendar and Email
/// connector pages. The client is a Desktop app OAuth client from the Google Cloud Console
/// (APIs &amp; Services &gt; Credentials); its secret is stored DPAPI-encrypted.
/// </summary>
public class GoogleOAuthSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = "http://localhost:8400/oauth/callback";
}

/// <summary>
/// Microsoft (Azure AD / Entra ID) OAuth2 settings, entered under OAuth App Credentials on the
/// Calendar and Email connector pages. The app registration is a public client (mobile and
/// desktop applications), so <see cref="ClientSecret"/> stays empty; a secret entered here by
/// hand for a confidential registration is sent, and is dropped when the page changes the
/// client ID.
/// </summary>
public class MicrosoftOAuthSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string TenantId { get; set; } = "common";
    public string RedirectUri { get; set; } = "http://localhost:8401/oauth/callback";
}

/// <summary>
/// Calendar sync configuration. Controls how the application connects to
/// and synchronizes with external calendar providers (Google Calendar,
/// Microsoft Outlook).
/// </summary>
public class CalendarSettings
{
    /// <summary>
    /// Whether calendar synchronization is enabled.
    /// When disabled, no calendar data is fetched or stored locally.
    /// </summary>
    public bool EnableCalendarSync { get; set; } = false;

    /// <summary>
    /// How often (in minutes) to poll the calendar provider for changes.
    /// </summary>
    public int SyncIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Number of days in the past to include when syncing calendar events.
    /// </summary>
    public int DaysPastToSync { get; set; } = 90;

    /// <summary>
    /// Number of days in the future to include when syncing calendar events.
    /// </summary>
    public int DaysFutureToSync { get; set; } = 30;

    /// <summary>
    /// Not applied. Meant as the strategy for conflicting calendar events ("LocalWins",
    /// "RemoteWins" or "Merge"), but calendar sync only imports events and never reads it, and
    /// the Settings control for it was removed. Kept so existing settings files still load.
    /// </summary>
    public string ConflictResolution { get; set; } = "RemoteWins";

    /// <summary>
    /// Whether to include attendee details (names, emails, response status)
    /// when syncing calendar events.
    /// </summary>
    public bool IncludeAttendeeDetails { get; set; } = true;

    /// <summary>
    /// Whether to include the full event description/body when syncing
    /// calendar events.
    /// </summary>
    public bool IncludeDescriptions { get; set; } = true;
}

/// <summary>
/// Email sync configuration. Controls how the application connects to
/// and synchronizes with external email providers (Gmail, Outlook).
/// </summary>
public class EmailSettings
{
    /// <summary>
    /// Whether email synchronization is enabled.
    /// When disabled, no email data is fetched or stored locally.
    /// </summary>
    public bool EnableEmailSync { get; set; } = false;

    /// <summary>
    /// How often (in minutes) to poll the email provider for new messages.
    /// </summary>
    public int SyncIntervalMinutes { get; set; } = 10;

    /// <summary>
    /// Maximum number of messages to fetch per sync cycle.
    /// </summary>
    public int MessagesPerSync { get; set; } = 50;

    /// <summary>
    /// Number of days back to include when syncing email messages.
    /// </summary>
    public int DaysBackToSync { get; set; } = 30;

    /// <summary>
    /// Not applied. Synced messages are categorized by rules, not by AI (see
    /// <c>EmailSyncSettings.EnableAiCategorization</c>); the property is kept so existing
    /// settings files still load.
    /// </summary>
    public bool EnableAiCategorization { get; set; } = true;

    /// <summary>
    /// Not applied. Whether a message body is kept is the email connector's own
    /// <c>EmailSyncSettings.IncludeHtmlBody</c> setting; the property is kept so existing
    /// settings files still load.
    /// </summary>
    public bool IncludeBodyContent { get; set; } = true;

    /// <summary>
    /// Whether to include attachment metadata (filename, size, content type)
    /// when syncing email messages. Does not download the actual attachments.
    /// </summary>
    public bool IncludeAttachmentMetadata { get; set; } = false;
}
