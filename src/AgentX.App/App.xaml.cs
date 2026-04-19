using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Serilog;
using SQLitePCL;
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
using AgentX.Core.Services.Shortcuts;
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
        Batteries_V2.Init();

        // 0. Unlock encrypted database (if encryption has been enabled)
        // We check an out-of-DB marker file FIRST — reading UserSettings requires an unlocked
        // DB, which we cannot do until the key is applied. The marker tells us which path to
        // take without any DB access.
        try
        {
            var stateFile = GetService<AgentX.Core.Services.Security.IEncryptionStateFile>();
            if (stateFile.Exists())
            {
                var info = stateFile.Read();
                var keySvc = GetService<AgentX.Core.Services.Security.IDatabaseKeyService>();
                var keyProviderRaw = GetService<AgentX.Core.Services.Security.IDatabaseKeyProvider>();
                var keyProvider = keyProviderRaw as AgentX.Core.Services.Security.DatabaseKeyProvider
                                  ?? throw new InvalidOperationException("Expected DatabaseKeyProvider concrete type from DI.");

                AgentX.Core.Services.Security.DatabaseKeyMaterial key;

                if (info is null)
                {
                    Log.Warning("Encryption state file exists but is unreadable. Assuming plaintext DB.");
                    key = null!; // fall-through — will NOT be used below
                }
                else if (info.StorageMode == AgentX.Core.Services.Security.KeyStorageMode.DpapiWrapped)
                {
                    key = await keySvc.GetOrCreateKeyAsync(AgentX.Core.Services.Security.KeyStorageMode.DpapiWrapped);
                    Log.Information("Database unlocked via DPAPI-wrapped key.");
                }
                else
                {
                    // UserPassphrase — prompt loop with probe.
                    key = await UnlockWithPassphraseLoopAsync(keySvc);
                    Log.Information("Database unlocked via user passphrase.");
                }

                if (key is not null)
                    keyProvider.Set(key);
            }
            else
            {
                Log.Debug("Encryption marker not present — opening plaintext database.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Database unlock flow failed before migration runner");
            // Swallow here so the migration runner can still try (it will fail clearly
            // if the DB is encrypted and we have no key — user can re-enter passphrase).
        }

        // 1. Ensure the database schema is at the latest migration
        try
        {
            var db = GetService<AgentXDbContext>();
            db.EnsureKeyApplied();
            var runner = GetService<AgentX.Core.Data.MigrationRunner.IMigrationRunner>();
            var result = await runner.RunAsync();
            Log.Information(
                "Migration runner: db={DbPath} created={Created} applied={Applied} alreadyApplied={AlreadyApplied}",
                result.DatabasePath,
                result.DatabaseCreated,
                string.Join(",", result.AppliedMigrations),
                string.Join(",", result.AlreadyApplied));
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

        // 3b. Initialize localization (reads persisted language override and
        //     constructs the ResourceLoader). Must be awaited BEFORE any UI
        //     renders — otherwise GetString/FormatPlural can race against a
        //     null loader and return fallback keys instead of localized text.
        try
        {
            var localization = GetService<ILocalizationService>();
            await localization.InitializeAsync();
            Log.Information("Localization initialized: {Language}", localization.CurrentLanguage);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Localization initialization failed — UI will use resource keys as fallback");
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
        services.AddSingleton<AgentXDbContext>(sp =>
        {
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AgentXDbContext>().Options;
            var factory = sp.GetRequiredService<AgentX.Core.Data.IEncryptedConnectionFactory>();
            return new AgentXDbContext(options, factory);
        });
        services.AddSingleton<AgentX.Core.Data.MigrationRunner.IMigrationRunner,
                             AgentX.Core.Data.MigrationRunner.MigrationRunner>();

        // ── Security ──────────────────────────────────────────
        services.AddSingleton<IDpapiEncryptionService, DpapiEncryptionService>();
        services.AddSingleton<AgentX.Core.Services.Security.IDatabaseKeyProvider,
                             AgentX.Core.Services.Security.DatabaseKeyProvider>();
        services.AddSingleton<AgentX.Core.Data.IEncryptedConnectionFactory,
                             AgentX.Core.Data.EncryptedConnectionFactory>();
        services.AddSingleton<AgentX.Core.Services.Security.IDatabaseKeyService,
                             AgentX.Core.Services.Security.DatabaseKeyService>();
        services.AddSingleton<AgentX.Core.Services.Security.IEncryptionStateFile,
                             AgentX.Core.Services.Security.EncryptionStateFile>();
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
        services.AddSingleton<IShortcutRegistry, ShortcutRegistry>();
        services.AddSingleton(_ => new ChordStateMachine(1000, () => DateTime.UtcNow));
        services.AddSingleton<ShortcutCatalog>();
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
            // IEncryptedConnectionFactory is registered in Task 9; this resolve is
            // wired here as the minimal call-site adjustment for Task 8's signature
            // change so the project builds.
            var connectionFactory = sp.GetRequiredService<AgentX.Core.Data.IEncryptedConnectionFactory>();
            return VectorStoreFactory.Create(settingsService, logger, connectionFactory);
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
        services.AddSingleton<IPluralRuleProvider, CldrPluralRuleProvider>();
        services.AddSingleton<IResourceLoaderAdapter, WinUIResourceLoaderAdapter>();
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

    private static async System.Threading.Tasks.Task<AgentX.Core.Services.Security.DatabaseKeyMaterial> UnlockWithPassphraseLoopAsync(
        AgentX.Core.Services.Security.IDatabaseKeyService keySvc)
    {
        while (true)
        {
            var passphrase = await PromptForPassphraseAsync();
            if (passphrase is null)
            {
                Microsoft.UI.Xaml.Application.Current.Exit();
                throw new OperationCanceledException("User cancelled passphrase entry.");
            }

            var candidate = await keySvc.UnlockWithPassphraseAsync(passphrase);
            if (await TryProbeKeyAsync(candidate))
                return candidate;

            await ShowInvalidPassphraseDialogAsync();
        }
    }

    private static async System.Threading.Tasks.Task<bool> TryProbeKeyAsync(AgentX.Core.Services.Security.DatabaseKeyMaterial candidate)
    {
        var dbPath = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "AgentX",
            "agentx.db");
        try
        {
            await using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var keyCmd = conn.CreateCommand();
            keyCmd.CommandText = $@"PRAGMA key = ""x'{candidate.HexKey}'"";";
            await keyCmd.ExecuteNonQueryAsync();
            using var probeCmd = conn.CreateCommand();
            probeCmd.CommandText = "SELECT count(*) FROM sqlite_master";
            await probeCmd.ExecuteScalarAsync();
            return true;
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 26)
        {
            return false;
        }
    }

    private static async System.Threading.Tasks.Task<string?> PromptForPassphraseAsync()
    {
        var box = new Microsoft.UI.Xaml.Controls.PasswordBox
        {
            PlaceholderText = "Enter your database passphrase"
        };
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = "Unlock your Agent-X database",
            Content = box,
            PrimaryButtonText = "Unlock",
            CloseButtonText = "Exit app",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            XamlRoot = MainWindow.Content.XamlRoot,
        };
        var result = await dialog.ShowAsync();
        return result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary ? box.Password : null;
    }

    private static async System.Threading.Tasks.Task ShowInvalidPassphraseDialogAsync()
    {
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = "Incorrect passphrase",
            Content = "That passphrase did not unlock the database. Please try again, or exit to restore from a backup.",
            CloseButtonText = "OK",
            XamlRoot = MainWindow.Content.XamlRoot,
        };
        await dialog.ShowAsync();
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
