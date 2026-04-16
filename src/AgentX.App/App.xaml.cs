using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Serilog;
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using AgentX.Core.AI.Routing;
using AgentX.Core.Data;
using AgentX.Core.Data.VectorDb;
using AgentX.Core.Services.Screen;
using AgentX.Core.Documents;
using AgentX.Core.Documents.Processors;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Indexing;
using AgentX.Core.Services.License;
using AgentX.Core.Services.Settings;
using AgentX.Core.Services.Tagging;
using AgentX.Core.Search;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Export;
using AgentX.Core.Services.Workflows;
using AgentX.Core.Services.Web;
using AgentX.Core.Services.Backup;
using AgentX.Core.Services.Annotations;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Workspace;
using AgentX.Core.Services.Plugins;
using AgentX.Core.Services.Audio;
using AgentX.Core.Services.Sync;
using AgentX.Core.Services.Sync.Models;
using AgentX.Core.Services.Search;
using AgentX.Core.Services.FeatureFlags;
using AgentX.Core.Services.Analytics;
using AgentX.Core.Services.Feedback;
using AgentX.Core.Services.Collaboration;
using AgentX.Core.Services.Api;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Security;
using AgentX.Core.Validation;
using AgentX.App.Services;

namespace AgentX.App;

public partial class App : Application
{
    private static IHost? _host;
    private static Window? _mainWindow;

    /// <summary>
    /// Gets the main application window. Used by pages that need the HWND
    /// for file/folder pickers (WinUI 3 requirement).
    /// </summary>
    public static Window MainWindow => _mainWindow ?? throw new InvalidOperationException("MainWindow not initialized.");

    public App()
    {
        InitializeComponent();
        ConfigureLogging();
        ConfigureExceptionHandling();
    }

    public static IHost Host => _host ?? throw new InvalidOperationException("Host not initialized.");
    public static T GetService<T>() where T : class => Host.Services.GetRequiredService<T>();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(ConfigureServices)
            .Build();

        // Initialize critical services before showing the window
        InitializeCoreServicesAsync();

        _mainWindow = new MainWindow();
        _mainWindow.Activate();

        Log.Information("Agent-X started successfully");
    }

    /// <summary>
    /// Initializes the database and AI service on startup.
    /// Runs as fire-and-forget so the window appears immediately while
    /// initialization continues in the background.
    /// </summary>
    private static async void InitializeCoreServicesAsync()
    {
        // 1. Ensure the database schema exists
        try
        {
            var dbContext = GetService<AgentXDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            Log.Information("Database initialized at {Path}",
                dbContext.Database.GetConnectionString());

            // 1a. Apply schema upgrades for existing databases
            // EnsureCreated does not alter existing tables, so new columns must
            // be added manually. Each ALTER TABLE is wrapped individually so that
            // columns already present (on fresh installs) are silently skipped.
            string[] alterStatements =
            [
                "ALTER TABLE search_history ADD COLUMN MinScore REAL NULL",
                "ALTER TABLE search_history ADD COLUMN MaxResults INTEGER NULL",
                "ALTER TABLE search_history ADD COLUMN DateAfter TEXT NULL",
                "ALTER TABLE search_history ADD COLUMN DateBefore TEXT NULL",
                "ALTER TABLE search_history ADD COLUMN SortOrder TEXT NULL",
                "ALTER TABLE conversations ADD COLUMN FolderName TEXT NULL",
                """
                CREATE TABLE IF NOT EXISTS oauth_credentials (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProviderId TEXT NOT NULL DEFAULT '',
                    AccessToken TEXT NOT NULL DEFAULT '',
                    RefreshToken TEXT NOT NULL DEFAULT '',
                    TokenExpiry TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                    Scopes TEXT NOT NULL DEFAULT '',
                    UserId TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                    UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
                )
                """,
                "CREATE UNIQUE INDEX IF NOT EXISTS ix_oauth_credentials_providerid ON oauth_credentials (ProviderId)",

                // inbox_items: new columns for DataConnector plugin support (Calendar/Email)
                "ALTER TABLE inbox_items ADD COLUMN SourceType TEXT NULL",
                "ALTER TABLE inbox_items ADD COLUMN SourceUrl TEXT NULL",
                "ALTER TABLE inbox_items ADD COLUMN SourcePluginId TEXT NULL",
                "ALTER TABLE inbox_items ADD COLUMN SourceCategory TEXT NULL",
                "ALTER TABLE inbox_items ADD COLUMN ExternalId TEXT NULL",
                "ALTER TABLE inbox_items ADD COLUMN DocumentId INTEGER NULL",
            ];

            foreach (var sql in alterStatements)
            {
                try
                {
                    await dbContext.Database.ExecuteSqlRawAsync(sql);
                }
                catch
                {
                    // Column already exists — safe to ignore on fresh databases
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize database");
        }

        // 1b. Initialize FTS5 full-text search
        try
        {
            var keywordSearch = GetService<IKeywordSearchService>();
            await keywordSearch.InitializeFtsAsync();
            Log.Information("FTS5 keyword search initialized");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "FTS5 initialization failed — keyword search unavailable");
        }

        // 2. Initialize the AI service (creates provider, tests connection)
        try
        {
            var aiService = GetService<IAiService>();
            await aiService.InitializeAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AI service initialization failed — Ollama may not be running");
        }

        // 3. Initialize feature flags
        try
        {
            var featureFlags = GetService<IFeatureFlagService>();
            await featureFlags.InitializeAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Feature flag initialization failed — using defaults");
        }

        // 4. Initialize theme from user preferences
        try
        {
            var themeService = GetService<IThemeService>();
            await themeService.InitializeAsync();
            // Apply theme on UI thread
            App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                themeService.ApplyTheme(themeService.CurrentTheme);
            });
            Log.Information("Theme initialized: {Theme}", themeService.CurrentTheme);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to initialize theme");
        }
    }

    private void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        // ── Logging (Serilog ILogger for DI) ─────────────────────
        services.AddSingleton<Serilog.ILogger>(_ => Log.Logger);

        // ── Data Layer ─────────────────────────────────────────
        services.AddSingleton<AgentXDbContext>();

        // ── Security ──────────────────────────────────────────
        services.AddSingleton<IDpapiEncryptionService, DpapiEncryptionService>();
        services.AddSingleton<ISecurityStatusService, SecurityStatusService>();

        // ── OAuth ──────────────────────────────────────────────
        services.AddSingleton<IOAuthService>(sp =>
        {
            var oauthService = new OAuthService(
                sp.GetRequiredService<AgentXDbContext>(),
                sp.GetRequiredService<IDpapiEncryptionService>(),
                sp.GetRequiredService<Serilog.ILogger>());

            var settings = sp.GetRequiredService<ISettingsService>().GetSettingsAsync().GetAwaiter().GetResult();

            // Only register Google if credentials are configured
            if (!string.IsNullOrWhiteSpace(settings.OAuth.Google.ClientId))
            {
                oauthService.RegisterProvider(OAuthProviderRegistry.Google(
                    settings.OAuth.Google.ClientId,
                    settings.OAuth.Google.ClientSecret,
                    settings.OAuth.Google.RedirectUri));
            }

            // Only register Microsoft if credentials are configured
            if (!string.IsNullOrWhiteSpace(settings.OAuth.Microsoft.ClientId))
            {
                oauthService.RegisterProvider(OAuthProviderRegistry.Microsoft(
                    settings.OAuth.Microsoft.ClientId,
                    settings.OAuth.Microsoft.ClientSecret,
                    settings.OAuth.Microsoft.TenantId,
                    settings.OAuth.Microsoft.RedirectUri));
            }

            return oauthService;
        });

        // ── Core Services ──────────────────────────────────────
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILicenseService, LicenseService>();
        services.AddSingleton<IFeatureFlagService, FeatureFlagService>();

        // ── App Services (UI layer) ──────────────────────────────
        services.AddSingleton<KeyboardShortcutService>();
        services.AddSingleton<IThemeService, ThemeService>();

        // ── AI Services ────────────────────────────────────────
        services.AddSingleton<IAiService, AiService>();
        services.AddSingleton<ICostTracker, CostTracker>();
        services.AddSingleton<IModelManager, ModelManager>();
        services.AddSingleton<IHardwareDetector, HardwareDetector>();
        services.AddSingleton<IEmbeddingService, EmbeddingService>();
        services.AddSingleton<IContextWindowManager, ContextWindowManager>();
        services.AddSingleton<IRetryPolicy, ExponentialBackoffRetryPolicy>();

        // ── AI Routing ────────────────────────────────────────
        services.AddSingleton<ITaskTypeDetector, TaskTypeDetector>();
        services.AddSingleton<IModelRouterService, ModelRouterService>();

        // ── Vector Store ─────────────────────────────────────────
        services.AddSingleton<IVectorStore>(sp =>
        {
            var settingsService = sp.GetRequiredService<ISettingsService>();
            var logger = sp.GetRequiredService<Serilog.ILogger>();
            return VectorStoreFactory.Create(settingsService, logger);
        });

        // ── Chat Services ──────────────────────────────────────
        services.AddSingleton<IConversationService, ConversationService>();
        services.AddSingleton<ISystemPromptService, SystemPromptService>();
        services.AddSingleton<IConversationMemoryService, ConversationMemoryService>();
        services.AddSingleton<IChatService, ChatService>();

        // ── Screen Awareness ─────────────────────────────────────
        services.AddSingleton<IScreenCaptureService, ScreenCaptureService>();

        // ── Document Processors ──────────────────────────────────
        services.AddSingleton<IDocumentProcessor, PdfProcessor>();
        services.AddSingleton<IDocumentProcessor, DocxProcessor>();
        services.AddSingleton<IDocumentProcessor, TextProcessor>();
        services.AddSingleton<IDocumentProcessor, MarkdownProcessor>();
        services.AddSingleton<IDocumentProcessor, CodeFileProcessor>();
        services.AddSingleton<IDocumentProcessor, ImageProcessor>();

        // ── Document Services ────────────────────────────────────
        services.AddSingleton<IDocumentService, DocumentService>();
        services.AddSingleton<IChunkingService, ChunkingService>();

        // ── Indexing Pipeline ────────────────────────────────────
        services.AddSingleton<IIndexingQueueService, IndexingQueueService>();
        services.AddSingleton<IIndexingService, IndexingService>();
        services.AddSingleton<IFileWatcherService, FileWatcherService>();

        // ── Collections & Tagging ────────────────────────────────
        services.AddSingleton<ICollectionService, CollectionService>();
        services.AddSingleton<IAutoTagService, AutoTagService>();

        // ── Search & RAG ──────────────────────────────────────────
        services.AddSingleton<ISemanticSearchService, SemanticSearchService>();
        services.AddSingleton<IKeywordSearchService, KeywordSearchService>();
        services.AddSingleton<ISearchCacheService, SearchCacheService>();
        services.AddSingleton<IHybridSearchOrchestrator, HybridSearchOrchestrator>();
        services.AddSingleton<ICitationService, CitationService>();
        services.AddSingleton<IRagReranker, RagReranker>();

        // ── RAG Enhancements (optional pipeline stages) ─────────
        services.AddSingleton<IMultiQueryGenerator, MultiQueryGenerator>();
        services.AddSingleton<IHydeService, HydeService>();
        services.AddSingleton<ILlmReranker, LlmReranker>();
        services.AddSingleton<IParentDocumentRetriever, ParentDocumentRetriever>();
        services.AddSingleton<IContextualCompressor, ContextualCompressor>();
        services.AddSingleton<IRagEvaluator, RagEvaluator>();

        services.AddSingleton<IRagPipeline, RagPipeline>();

        // ── Deep Research (Web Search) ────────────────────────────
        services.AddSingleton<WebSearchCache>();
        services.AddSingleton<WebSearchServiceFactory>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>().GetSettingsAsync().GetAwaiter().GetResult();
            return new WebSearchServiceFactory(settings.WebSearchApiKey, settings.WebSearchApiKey, null);
        });
        services.AddSingleton<IWebSearchService>(sp =>
        {
            var factory = sp.GetRequiredService<WebSearchServiceFactory>();
            var settings = sp.GetRequiredService<ISettingsService>().GetSettingsAsync().GetAwaiter().GetResult();
            return factory.GetConfiguredService(settings);
        });

        // ── Validation ──────────────────────────────────────────
        services.AddSingleton<IValidator<AppSettings>, AppSettingsValidator>();
        services.AddSingleton<IValidator<SyncConfiguration>, SyncConfigurationValidator>();
        services.AddSingleton<IValidator<PluginManifest>, PluginManifestValidator>();

        // ── Intelligence Services ──────────────────────────────
        services.AddSingleton<ISummaryService, SummaryService>();
        services.AddSingleton<IDuplicateDetectionService, DuplicateDetectionService>();
        services.AddSingleton<IOrganizationSuggestionService, OrganizationSuggestionService>();
        services.AddSingleton<IKnowledgeGraphService, KnowledgeGraphService>();
        services.AddSingleton<IDigestService, DigestService>();

        // ── Export Services ──────────────────────────────────
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<IExportTemplateService, ExportTemplateService>();

        // ── Workflow Services ────────────────────────────────
        services.AddSingleton<IWorkflowService, WorkflowService>();
        services.AddSingleton<IWorkflowEngine, WorkflowEngine>();

        // ── Web Services ─────────────────────────────────────
        services.AddSingleton<IWebScraperService, WebScraperService>();
        services.AddSingleton<IWebImportService, WebImportService>();
        services.AddSingleton<IFeedService, FeedService>();
        services.AddSingleton<ISitemapParser, SitemapParser>();
        services.AddSingleton<IJsRenderingService, JsRenderingService>();

        // ── Conversation Branching ───────────────────────────
        services.AddSingleton<IConversationBranchService, ConversationBranchService>();

        // ── Backup & Restore ────────────────────────────────
        services.AddSingleton<IBackupService, BackupService>();

        // ── Annotations ─────────────────────────────────────
        services.AddSingleton<IAnnotationService, AnnotationService>();

        // ── Localization ────────────────────────────────────
        services.AddSingleton<ILocalizationService, LocalizationService>();

        // ── Inbox (Smart Triage) ──────────────────────────────
        services.AddSingleton<IInboxService, InboxService>();

        // ── Comparison (Comparative Analysis) ─────────────────
        services.AddSingleton<IComparisonService, ComparisonService>();

        // ── Workspace Profiles ────────────────────────────────
        services.AddSingleton<IWorkspaceProfileService, WorkspaceProfileService>();

        // ── Plugin API ──────────────────────────────────────────
        services.AddSingleton<IPluginService, PluginService>();

        // ── Voice / Audio ──────────────────────────────────────
        services.AddSingleton<ITranscriptionService, TranscriptionService>();

        // ── Collaborative Sync ─────────────────────────────────
        services.AddSingleton<ISyncService, SyncService>();

        // ── Analytics ────────────────────────────────────────────
        services.AddSingleton<IAnalyticsService, AnalyticsService>();

        // ── User Feedback ────────────────────────────────────────
        services.AddSingleton<IFeedbackService, FeedbackService>();

        // ── Collaboration ────────────────────────────────────────
        services.AddSingleton<ICollaborationService, CollaborationService>();

        // ── REST API ─────────────────────────────────────────────
        services.AddSingleton<IApiHostService, ApiHostService>();

        // ── Notifications ────────────────────────────────────────
        services.AddSingleton<INotificationService, NotificationService>();

        // ── System Tray ───────────────────────────────────────
        services.AddSingleton<SystemTrayService>();

        // ── ViewModels (Transient) ─────────────────────────────
        services.AddTransient<ViewModels.DashboardViewModel>();
        services.AddTransient<ViewModels.SettingsViewModel>();
        services.AddTransient<ViewModels.ChatViewModel>();
        services.AddTransient<ViewModels.AskFilesViewModel>();
        services.AddTransient<ViewModels.KnowledgeVaultViewModel>();
        services.AddTransient<ViewModels.CollectionManagerViewModel>();
        services.AddTransient<ViewModels.SearchViewModel>();
        services.AddTransient<ViewModels.ModelManagerViewModel>();
        services.AddTransient<ViewModels.HardwareAdvisorViewModel>();
        services.AddTransient<ViewModels.QuickActionsViewModel>();
        services.AddTransient<ViewModels.OnboardingViewModel>();
        services.AddTransient<ViewModels.KnowledgeGraphViewModel>();
        services.AddTransient<ViewModels.DigestViewModel>();
        services.AddTransient<ViewModels.WorkflowBuilderViewModel>();
        services.AddTransient<ViewModels.WebImportViewModel>();
        services.AddTransient<ViewModels.ExportViewModel>();
        services.AddTransient<ViewModels.BackupRestoreViewModel>();
        services.AddTransient<ViewModels.AnnotationsViewModel>();
        services.AddTransient<ViewModels.InboxViewModel>();
        services.AddTransient<ViewModels.ComparisonViewModel>();
        services.AddTransient<ViewModels.WorkspaceProfileViewModel>();
        services.AddTransient<ViewModels.PluginManagerViewModel>();
        services.AddTransient<ViewModels.SyncSettingsViewModel>();
        services.AddTransient<ViewModels.CalendarSettingsViewModel>();
        services.AddTransient<ViewModels.EmailSettingsViewModel>();
        services.AddTransient<ViewModels.AnalyticsViewModel>();
        services.AddTransient<ViewModels.QuickChatViewModel>();

        // ── Views (Transient) ──────────────────────────────────
        services.AddTransient<Views.DashboardPage>();
        services.AddTransient<Views.SettingsPage>();
        services.AddTransient<Views.ChatPage>();
        services.AddTransient<Views.AskFilesPage>();
        services.AddTransient<Views.KnowledgeVaultPage>();
        services.AddTransient<Views.CollectionManagerPage>();
        services.AddTransient<Views.SearchPage>();
        services.AddTransient<Views.ModelManagerPage>();
        services.AddTransient<Views.HardwareAdvisorPage>();
        services.AddTransient<Views.QuickActionsPage>();
        services.AddTransient<Views.OnboardingPage>();
        services.AddTransient<Views.KnowledgeGraphPage>();
        services.AddTransient<Views.DigestPage>();
        services.AddTransient<Views.WorkflowBuilderPage>();
        services.AddTransient<Views.WebImportPage>();
        services.AddTransient<Views.BackupRestorePage>();
        services.AddTransient<Views.AnnotationsPage>();
        services.AddTransient<Views.InboxPage>();
        services.AddTransient<Views.ComparisonPage>();
        services.AddTransient<Views.WorkspaceProfilePage>();
        services.AddTransient<Views.PluginManagerPage>();
        services.AddTransient<Views.SyncSettingsPage>();
        services.AddTransient<Views.CalendarSettingsPage>();
        services.AddTransient<Views.EmailSettingsPage>();
        services.AddTransient<Views.AnalyticsPage>();
        services.AddTransient<Views.UserGuidePage>();
        services.AddTransient<Views.PrivacyPolicyPage>();
        services.AddTransient<Views.TermsOfServicePage>();
    }

    private static void ConfigureLogging()
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentX", "Logs", "agentx-.log");

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .Enrich.WithProperty("Application", "AgentX")
            .CreateLogger();

        Log.Information("Agent-X logging initialized at {LogPath}", logPath);
    }

    private void ConfigureExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            Log.Fatal(e.ExceptionObject as Exception, "AppDomain unhandled exception");
            Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        UnhandledException += (sender, e) =>
        {
            Log.Fatal(e.Exception, "Application unhandled exception");
            e.Handled = true;
        };
    }
}
