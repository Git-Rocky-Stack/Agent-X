using AgentX.App.Services;
using AgentX.Core.AI;
using AgentX.Core.AI.Agents;
using AgentX.Core.AI.Context;
using AgentX.Core.AI.Models;
using AgentX.Core.AI.Routing;
using AgentX.Core.Configuration;
using AgentX.Core.Data;
using AgentX.Core.Data.VectorDb;
using AgentX.Core.Documents;
using AgentX.Core.Documents.Processors;
using AgentX.Core.Observability;
using AgentX.Core.Search;
using AgentX.Core.Services.Analytics;
using AgentX.Core.Services.Annotations;
using AgentX.Core.Services.Api;
using AgentX.Core.Services.Audio;
using AgentX.Core.Services.Backup;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Collections;
using AgentX.Core.Services.Export;
using AgentX.Core.Services.FeatureFlags;
using AgentX.Core.Services.Feedback;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Indexing;
using AgentX.Core.Services.Intelligence;
using AgentX.Core.Services.Localization;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins;
using AgentX.Core.Services.Plugins.Calendar;
using AgentX.Core.Services.Plugins.Email;
using AgentX.Core.Services.Screen;
using AgentX.Core.Services.Search;
using AgentX.Core.Services.Security;
using AgentX.Core.Services.Settings;
using AgentX.Core.Services.Shortcuts;
using AgentX.Core.Services.Sync;
using AgentX.Core.Services.Sync.Codec;
using AgentX.Core.Services.Sync.ConflictResolution;
using AgentX.Core.Services.Sync.Models;
using AgentX.Core.Services.Sync.Transport;
using AgentX.Core.Services.Tagging;
using AgentX.Core.Services.TemporalIdentity;
using AgentX.Core.Services.Web;
using AgentX.Core.Services.Workflows;
using AgentX.Core.Services.Workspace;
using AgentX.Core.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using Serilog;
using SQLitePCL;

namespace AgentX.App;

public partial class App : Application
{
    private static IHost? _host;
    private static Window? _mainWindow;
    private static int _shutdownStarted;

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

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((ctx, config) =>
            {
                // P2-4: load RagPrompts.json from the application base directory.
                // Optional + reloadOnChange so operators can edit prompts at
                // runtime without restarting the process; IRagPromptCatalog
                // reads via IOptionsMonitor and picks up the change on next
                // prompt site invocation.
                var baseDir = AppContext.BaseDirectory;
                config.SetBasePath(baseDir);
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                config.AddJsonFile("RagPrompts.json", optional: true, reloadOnChange: true);
            })
            .UseSerilog()
            .ConfigureServices(ConfigureServices)
            .Build();

        // Apply the persisted UI language before the shell is built: a language override only
        // reaches resources loaded after it is set, so the shell's x:Uid strings would otherwise
        // stay in the default language for the whole session. Localization reads settings.json
        // only (no database access), so it does not have to wait for unlock and migration.
        await InitializeLocalizationAsync();

        // Create and show the window shell FIRST. The critical async init below opens UI surfaces
        // before any data work — the passphrase-unlock prompt and the migration-recovery dialog both
        // need MainWindow.Content.XamlRoot — so _mainWindow must be assigned before init runs.
        // (Pages are loaded lazily on navigation, so the shell carries no data-backed state yet; the
        // migration gate inside InitializeCoreServicesAsync still precedes every data-backed feature.)
        _mainWindow = new MainWindow();
        if (_mainWindow is MainWindow mainWindow)
        {
            mainWindow.Closed += OnMainWindowClosed;
            mainWindow.ConfigureWindowLifecycleServices();
        }

        GetService<SystemTrayService>().ShowMainWindow("startup");

        // Critical services: migration is AWAITED and gates the API/connectors/FTS/data features.
        // Runs after the shell exists so its UI prompts have a XamlRoot; fails closed on migration
        // error (see InitializeCoreServicesAsync / EnterMigrationRecoveryStateAsync).
        InitializeCoreServicesAsync();

        // The core services above keep starting in the background; their last step logs
        // "Agent-X core services started".
        Log.Information("Agent-X window shown; core services are starting");
    }

    /// <summary>
    /// Loads the persisted language override and builds the resource loader. Runs before the
    /// main window exists; a failure leaves the default language and never blocks startup.
    /// </summary>
    private static async Task InitializeLocalizationAsync()
    {
        try
        {
            var localization = GetService<ILocalizationService>();
            await localization.InitializeAsync();

            // Relative times ("5m ago") and the chat context inspector are worded in Core, which
            // cannot read the app's resources.
            AgentX.Core.Helpers.FormatHelper.LocalizedText = localization.GetString;
            Log.Information("Localization initialized: {Language}", localization.CurrentLanguage);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Localization initialization failed; UI will use resource keys as fallback");
        }
    }

    /// <summary>
    /// Initializes core services on startup in a single defined order. The CRITICAL path —
    /// database unlock, key apply, and the migration → API → connectors sequence — is AWAITED and
    /// fails closed: if migration throws, the app enters a recovery state and starts NO data-backed
    /// feature (AX-QA-003). Only after the migration gate succeeds do the best-effort inits (FTS,
    /// AI/Ollama, feature flags, theme) run, in order; localization already ran in OnLaunched,
    /// before the shell was built. Invoked after the window shell exists so its UI prompts
    /// (passphrase, recovery dialog) have a XamlRoot. This is an <c>async void</c> event-style
    /// entry point (OnLaunched returns void, so nothing awaits it), but it no longer races the
    /// rest of startup for the critical path — that path is internally awaited and gated end to
    /// end.
    /// </summary>
    private static async void InitializeCoreServicesAsync()
    {
        Batteries_V2.Init();

        // 0. Unlock encrypted database (if encryption has been enabled)
        // We check an out-of-DB marker file FIRST — reading UserSettings requires an unlocked
        // DB, which we cannot do until the key is applied. The marker tells us which path to
        // take without any DB access. Before that, finish or undo an encryption change that a
        // crash interrupted, so the marker and the database file agree when the marker is read.
        try
        {
            GetService<IDatabaseEncryptionMigrator>().RecoverIfNeeded(
                AgentX.Core.Helpers.PathHelper.GetDatabasePath());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Encryption crash recovery failed");
        }

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

        // 1. CRITICAL PATH (AX-QA-003): apply the database migration and gate every data-backed
        //    subsystem behind its success. The orchestrator AWAITS the migration, then — only if it
        //    succeeds — starts the REST API and built-in connectors, in order. If migration throws
        //    (including BaselineSchemaIncompleteException from AX-QA-002), it returns a recovery
        //    state and starts NOTHING. We must NOT continue to FTS or any data-backed feature in
        //    that case: fail closed.
        try
        {
            var db = GetService<AgentXDbContext>();
            db.EnsureKeyApplied();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply the database key before migration");
        }

        var startup = GetService<IStartupOrchestrator>();
        var startupResult = await startup.RunCriticalStartupAsync();

        if (startupResult.IsRecoveryState)
        {
            // Fail closed: do not initialize FTS, AI, connectors, data-backed pages, etc. Surface a
            // blocking error so the user can recover (restore a backup / re-enter passphrase) rather
            // than running the app against a broken or partially-migrated schema.
            await EnterMigrationRecoveryStateAsync(startupResult.Failure);
            return;
        }

        // 1b. Initialize FTS5 full-text search — only AFTER the migration gate, since it queries
        //     the now-valid schema.
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

        // 1c. Resume auto-sync when it was on (the first cycle runs about a minute later; the loop
        //     lives for the whole session, so it gets no short-lived token), and mark workflow
        //     runs that a previous session left running as interrupted, so Operations does not
        //     show them as running forever.
        try
        {
            await GetService<ISyncService>().ResumeAutoSyncAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not resume auto-sync at startup");
        }

        try
        {
            await GetService<IWorkflowService>().ReconcileInterruptedRunsAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not reconcile interrupted workflow runs at startup");
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
            // Apply theme on the UI thread. Use the backing field (not the App.MainWindow getter,
            // which THROWS when the window is not yet assigned) so a not-yet-ready window degrades to
            // a no-op instead of an InvalidOperationException swallowed by the catch below.
            _mainWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                themeService.ApplyTheme(themeService.CurrentTheme);
            });
            Log.Information("Theme initialized: {Theme}", themeService.CurrentTheme);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to initialize theme");
        }

        // 4b. Activate the plugins the operator enabled. After the migration gate, because plugin
        //     state lives in the database, and before indexing, so document processors that
        //     plugins contribute are available to imports. A plugin that fails to activate is
        //     marked disabled and logged by the plugin service.
        try
        {
            var plugins = await GetService<IPluginService>().ActivateEnabledPluginsAsync();
            Log.Information(
                "Plugins activated: {Activated}; failed: {Failed}",
                plugins.Activated.Count,
                plugins.Failed.Count);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Plugin activation failed; enabled plugins stay inactive this session");
        }

        // 4c. Scheduled backups: runs only when a backup schedule is enabled in settings.json; an
        //     overdue backup starts a few minutes after launch.
        try
        {
            await GetService<IBackupService>().StartScheduledBackupsAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Scheduled backups could not be started");
        }

        // 5. Start the indexing pipeline: initialize the vector store, re-queue documents left
        //    pending or interrupted, and start the background loop that chunks, embeds and
        //    FTS-indexes every import. Nothing else starts it, so without this call no document
        //    ever becomes searchable. Late, because embedding needs the AI service from step 2,
        //    and on the thread pool, because rebuilding the vector index is CPU-heavy.
        try
        {
            var indexing = GetService<IIndexingService>();
            await Task.Run(() => indexing.InitializeAsync());
            Log.Information("Indexing pipeline started");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Indexing pipeline failed to start; imported documents will not be indexed");
        }

        // 6. Watch folders: when AutoIndexWatchFolders is on, start monitoring and run a
        //    catch-up scan for files added or changed while the app was closed. After step 5,
        //    so the files it imports are picked up by the running indexing pipeline.
        try
        {
            var watcher = GetService<IFileWatcherService>();
            await Task.Run(() => watcher.InitializeAsync());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Watch folder monitoring failed to start");
        }

        Log.Information("Agent-X core services started");
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
        // AX-QA-003: the data-ready gate data-backed UI awaits before its first DB read. The
        // orchestrator opens it the instant migration succeeds (or fails it on error). Singleton so
        // the orchestrator and every consumer (e.g. DashboardViewModel) share one instance.
        services.AddSingleton<IStartupGate, StartupGate>();
        // AX-QA-003: the critical, ordered startup sequence (migration gate → API → connectors).
        // Awaited in OnLaunched; fails closed when migration throws so nothing runs on a broken schema.
        services.AddSingleton<IStartupOrchestrator, StartupOrchestrator>();

        // ── Security ──────────────────────────────────────────
        services.AddSingleton<IDpapiEncryptionService, DpapiEncryptionService>();
        services.AddSingleton<AgentX.Core.Services.Security.IDatabaseKeyProvider,
                             AgentX.Core.Services.Security.DatabaseKeyProvider>();
        services.AddSingleton<AgentX.Core.Data.IEncryptedConnectionFactory,
                             AgentX.Core.Data.EncryptedConnectionFactory>();
        services.AddSingleton<AgentX.Core.Services.Security.IDatabaseKeyService,
                             AgentX.Core.Services.Security.DatabaseKeyService>();
        services.AddSingleton<IDatabaseEncryptionMigrator, DatabaseEncryptionMigrator>();
        // Turns database encryption on from Settings (without it the toggle is refused).
        services.AddSingleton<IDatabaseEncryptionManager, DatabaseEncryptionManager>();
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

            // Token refresh buffer and consent timeout come from settings.json.
            oauthService.ApplySettings(settings.OAuth);

            // Google and Microsoft come from the client credentials saved under OAuth App
            // Credentials (Calendar and Email connector pages); saving there re-applies them.
            oauthService.ApplyProviderSettings(settings.OAuth);

            return oauthService;
        });

        // ── Core Services ──────────────────────────────────────
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IFeatureFlagService, FeatureFlagService>();
        // AX-QA-008: derives the dashboard's state-aware privacy disclosure from current settings.
        services.AddSingleton<AgentX.Core.Services.Privacy.IPrivacyStatusService,
                             AgentX.Core.Services.Privacy.PrivacyStatusService>();
        // AX-QA-011: injectable app-data/temp path seam (delegates to PathHelper in production) so
        // tests can redirect artifact writes away from the real user profile.
        services.AddSingleton<AgentX.Core.Helpers.IAppPathService, AgentX.Core.Helpers.AppPathService>();

        // ── RAG Configuration (Phase 1) ─────────────────────────
        services.Configure<RagConfigurationOptions>(context.Configuration.GetSection("Rag"));
        services.AddSingleton<IRagConfiguration, RagConfiguration>();

        // P2-4: bind RagPrompts.json (loaded above in ConfigureAppConfiguration)
        // and register the catalog. IOptionsMonitor gives us hot-reload — every
        // prompt-site read resolves the current value, so editing the JSON at
        // runtime takes effect on the next call.
        services.Configure<RagPromptOptions>(context.Configuration.GetSection("RagPrompts"));
        services.AddSingleton<IRagPromptCatalog, RagPromptCatalog>();

        // ── App Services (UI layer) ──────────────────────────────
        services.AddSingleton<IShortcutRegistry, ShortcutRegistry>();
        services.AddSingleton(_ => new ChordStateMachine(1000, () => DateTime.UtcNow));
        services.AddSingleton<ShortcutCatalog>();
        // ShortcutInputRouter is constructed manually in MainWindow because it requires
        // UI-callback delegates (show palette, show jump-to, show cheatsheet) that are
        // instance methods on the window. Registering in DI would force an unnecessary
        // abstraction layer over callbacks that belong to the view.
        services.AddSingleton<IThemeService, ThemeService>();

        // ── AI Services ────────────────────────────────────────
        services.AddSingleton<IAiService, AiService>();
        services.AddSingleton<ICostTracker, CostTracker>();
        services.AddSingleton<IModelManager, ModelManager>();
        // Built-in model bootstrap — the SLIM installer ships without the ~1.9 GB GGUF, so the
        // app fetches it on first run. Resolves to the same Models dir / file name the local
        // provider reads from. Cloud providers never need this; OFFLINE installs find it present.
        services.AddSingleton<IBuiltInModelBootstrap>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>().GetSettingsAsync().GetAwaiter().GetResult();
            var modelsDir = System.IO.Path.Combine(settings.StoragePath, "Models");
            return new BuiltInModelBootstrap(
                new System.Net.Http.HttpClient { Timeout = AgentX.Core.Constants.AppConstants.ModelDownloadTimeout },
                modelsDir,
                sp.GetRequiredService<Serilog.ILogger>(),
                modelFileName: settings.LocalModelFileName);
        });
        services.AddSingleton<IHardwareDetector, HardwareDetector>();

        // Token counter for accurate context window budgeting
        services.AddSingleton<ITokenCounter, TokenCounter>();

        // Inner embedding service (wrapped by cache)
        services.AddSingleton<EmbeddingService>();

        // Cached embedding service (decorator pattern - wraps the inner service)
        services.AddSingleton<IEmbeddingService>(sp =>
        {
            var inner = sp.GetRequiredService<EmbeddingService>();
            var config = sp.GetRequiredService<IRagConfiguration>();
            var logger = sp.GetRequiredService<Serilog.ILogger>();
            return new CachedEmbeddingService(inner, config, logger.ForContext<CachedEmbeddingService>());
        });

        services.AddSingleton<IContextWindowManager, ContextWindowManager>();
        services.AddSingleton<ISemanticContextSelector, SemanticContextSelector>();
        services.AddSingleton<IConversationCompressionService, ConversationCompressionService>();
        services.AddSingleton<IContextAssemblyService, ContextAssemblyService>();

        // ── AI Routing ────────────────────────────────────────
        services.AddSingleton<ITaskTypeDetector, TaskTypeDetector>();
        services.AddSingleton<IModelRouterService, ModelRouterService>();

        // ── Vector Store ─────────────────────────────────────────
        services.AddSingleton<IVectorStore>(sp =>
        {
            var settingsService = sp.GetRequiredService<ISettingsService>();
            var embeddingService = sp.GetRequiredService<IEmbeddingService>();
            var logger = sp.GetRequiredService<Serilog.ILogger>();
            // IEncryptedConnectionFactory is registered in Task 9; this resolve is
            // wired here as the minimal call-site adjustment for Task 8's signature
            // change so the project builds.
            var connectionFactory = sp.GetRequiredService<AgentX.Core.Data.IEncryptedConnectionFactory>();
            return VectorStoreFactory.Create(settingsService, embeddingService, logger, connectionFactory);
        });

        // ── Chat Services ──────────────────────────────────────
        services.AddSingleton<IConversationService, ConversationService>();
        services.AddSingleton<IConversationRecallService, ConversationRecallService>();
        services.AddSingleton<IConversationSummaryService, ConversationSummaryService>();
        services.AddSingleton<ISystemPromptService, SystemPromptService>();
        services.AddSingleton<IConversationMemoryService, ConversationMemoryService>();
        services.AddSingleton<ISemanticMemoryService, SemanticMemoryService>();
        services.AddSingleton<IChatService, ChatService>();

        // ── Agent Orchestration (Phase 3) ───────────────────────
        services.AddSingleton<IMultiAgentOrchestrator, MultiAgentOrchestrator>();

        // ── Chat Coordinators (orchestrate chat operations for ChatViewModel) ──
        services.AddSingleton<ViewModels.Coordinators.IConversationCoordinator,
                             ViewModels.Coordinators.ConversationCoordinator>();
        services.AddSingleton<ViewModels.Coordinators.IMessagingCoordinator,
                             ViewModels.Coordinators.MessagingCoordinator>();
        services.AddSingleton<ViewModels.Coordinators.IVoiceCoordinator,
                             ViewModels.Coordinators.VoiceCoordinator>();
        services.AddSingleton<ViewModels.Coordinators.IBranchingCoordinator,
                             ViewModels.Coordinators.BranchingCoordinator>();

        // ── Screen Awareness ─────────────────────────────────────
        services.AddSingleton<IScreenCaptureService, ScreenCaptureService>();

        // ── Document Processors ──────────────────────────────────
        services.AddSingleton<IDocumentProcessor, PdfProcessor>();
        services.AddSingleton<IDocumentProcessor, DocxProcessor>();
        services.AddSingleton<IDocumentProcessor, TextProcessor>();
        services.AddSingleton<IDocumentProcessor, MarkdownProcessor>();
        services.AddSingleton<IDocumentProcessor, CodeFileProcessor>();
        services.AddSingleton<IDocumentProcessor, ImageProcessor>();
        // AudioProcessor depends on ITranscriptionService (line 665) and WebProcessor on
        // IWebScraperService (line 621); both resolve lazily, so registration order here
        // does not matter.
        services.AddSingleton<IDocumentProcessor, AudioProcessor>();
        services.AddSingleton<IDocumentProcessor, WebProcessor>();

        // ── Document Services ────────────────────────────────────
        services.AddSingleton<IDocumentService, DocumentService>();
        services.AddSingleton<IChunkingService>(sp =>
        {
            var tokenCounter = sp.GetRequiredService<ITokenCounter>();
            var adaptive = sp.GetService<IAdaptiveChunkingService>(); // optional — may be null
            var logger = sp.GetRequiredService<Serilog.ILogger>();
            return new ChunkingService(tokenCounter, adaptive, logger.ForContext<ChunkingService>());
        });

        // ── Indexing Pipeline ────────────────────────────────────
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

        // ── Phase 3: Advanced Observability & Enhancements ────────
        services.AddSingleton<IAdaptiveChunkingService>(sp =>
        {
            var config = sp.GetRequiredService<IRagConfiguration>();
            var logger = sp.GetRequiredService<Serilog.ILogger>();
            return new AdaptiveChunkingService(config, logger.ForContext<AdaptiveChunkingService>());
        });
        services.AddSingleton<IRagMetrics>(sp =>
        {
            var logger = sp.GetRequiredService<Serilog.ILogger>();
            var metrics = new RagMetrics(logger.ForContext<RagMetrics>());

            // FU-4: pull-based embedding-cache stats. Resolving IEmbeddingService
            // here would create a singleton-init cycle; defer to a closure that
            // resolves at first GetSnapshot() call (lazy by construction).
            metrics.RegisterEmbeddingCacheProvider(() =>
            {
                if (sp.GetService<IEmbeddingService>() is CachedEmbeddingService cache)
                {
                    var (hits, misses, total, size, hitRate) = cache.GetStatistics();
                    return new EmbeddingCacheStats
                    {
                        Hits = hits,
                        Misses = misses,
                        TotalRequests = total,
                        CurrentCacheSize = size,
                        HitRate = hitRate
                    };
                }
                return null;
            });

            return metrics;
        });
        services.AddSingleton<IPiiDetector>(sp =>
        {
            var logger = sp.GetRequiredService<Serilog.ILogger>();
            return new PiiDetector(logger.ForContext<PiiDetector>());
        });

        services.AddSingleton<IRagPipeline, RagPipeline>();

        // ── Deep Research (Web Search) ────────────────────────────
        // Reads the provider, key or SearXNG URL, and cache duration from settings on every
        // search, so a changed key applies without a restart.
        services.AddSingleton<WebSearchCache>();
        services.AddSingleton<IWebSearchService>(sp => new SettingsAwareWebSearchService(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<WebSearchCache>(),
            sp.GetRequiredService<Serilog.ILogger>()));

        // ── Validation ──────────────────────────────────────────
        services.AddSingleton<IValidator<AppSettings>, AppSettingsValidator>();
        services.AddSingleton<IValidator<SyncConfiguration>, SyncConfigurationValidator>();
        services.AddSingleton<IValidator<PluginManifest>, PluginManifestValidator>();

        // ── Intelligence Services ──────────────────────────────
        services.AddSingleton<IHierarchicalSummaryService, HierarchicalSummaryService>();
        services.AddSingleton<IDuplicateEvidenceService, DuplicateEvidenceService>();
        services.AddSingleton<IDocumentSynthesisService, DocumentSynthesisService>();
        services.AddSingleton<IDigestInsightService, DigestInsightService>();
        services.AddSingleton<ISummaryService, SummaryService>();
        services.AddSingleton<IDuplicateDetectionService, DuplicateDetectionService>();
        services.AddSingleton<IOrganizationSuggestionService, OrganizationSuggestionService>();
        services.AddSingleton<IKnowledgeGraphService, KnowledgeGraphService>();
        services.AddSingleton<IDigestService, DigestService>();
        services.AddSingleton<IConversationThemeTrendService, ConversationThemeTrendService>();
        services.AddSingleton<IConversationThemeClusterService, ConversationThemeClusterService>();

        // ── Export Services ──────────────────────────────────
        // Formatters registered first so ExportService can resolve them via IEnumerable<IExportFormatter>
        services.AddSingleton<AgentX.Core.Services.Export.Formatters.IExportFormatter,
                             AgentX.Core.Services.Export.Formatters.MarkdownFormatter>();
        services.AddSingleton<AgentX.Core.Services.Export.Formatters.IExportFormatter,
                             AgentX.Core.Services.Export.Formatters.PlainTextFormatter>();
        services.AddSingleton<AgentX.Core.Services.Export.Formatters.IExportFormatter,
                             AgentX.Core.Services.Export.Formatters.CsvFormatter>();
        services.AddSingleton<AgentX.Core.Services.Export.Formatters.IExportFormatter,
                             AgentX.Core.Services.Export.Formatters.HtmlFormatter>();
        services.AddSingleton<AgentX.Core.Services.Export.Formatters.IExportFormatter,
                             AgentX.Core.Services.Export.Formatters.JsonFormatter>();
        services.AddSingleton<AgentX.Core.Services.Export.Formatters.IExportFormatter,
                             AgentX.Core.Services.Export.Formatters.PdfFormatter>();
        services.AddSingleton<AgentX.Core.Services.Export.Formatters.IExportFormatter,
                             AgentX.Core.Services.Export.Formatters.DocxFormatter>();
        services.AddSingleton<AgentX.Core.Services.Export.Formatters.IExportFormatter,
                             AgentX.Core.Services.Export.Formatters.PptxFormatter>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<IExportTemplateService, ExportTemplateService>();

        // ── Workflow Services ────────────────────────────────
        services.AddSingleton<IWorkflowService, WorkflowService>();
        services.AddSingleton<IWorkflowEngine, WorkflowEngine>();

        // ── Web Services ─────────────────────────────────────
        services.AddSingleton<IWebContentFetcher, WebContentFetcher>();
        services.AddSingleton<IHtmlParser, HtmlParser>();
        services.AddSingleton<IStructuredDataExtractor, StructuredDataExtractor>();
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
        // The same instance offers active plugins' document processors to DocumentService.
        services.AddSingleton<IPluginDocumentProcessorSource>(sp =>
            (PluginService)sp.GetRequiredService<IPluginService>());
        services.AddSingleton<CalendarPlugin>();
        services.AddSingleton<ICalendarService>(sp =>
            new CalendarService(
                sp.GetRequiredService<CalendarPlugin>(),
                sp.GetRequiredService<Serilog.ILogger>()));
        services.AddSingleton<EmailPlugin>();
        services.AddSingleton<IEmailService>(sp =>
            new EmailService(
                sp.GetRequiredService<EmailPlugin>(),
                sp.GetRequiredService<Serilog.ILogger>()));
        services.AddSingleton<IBuiltinConnectorLifecycleService, BuiltinConnectorLifecycleService>();

        // ── Voice / Audio ──────────────────────────────────────
        services.AddSingleton<ITranscriptionService, TranscriptionService>();

        // ── Collaborative Sync (sub-services registered before orchestrator) ──
        services.AddSingleton<ISyncTransport, SyncTransport>();
        services.AddSingleton<ISyncPackageCodec, SyncPackageCodec>();
        services.AddSingleton<ISyncConflictResolver, SyncConflictResolver>();
        services.AddSingleton<ISyncService>(sp =>
            new SyncService(
                sp.GetRequiredService<AgentXDbContext>(),
                sp.GetRequiredService<Serilog.ILogger>(),
                sp.GetRequiredService<ISyncTransport>(),
                sp.GetRequiredService<ISyncPackageCodec>(),
                sp.GetRequiredService<ISyncConflictResolver>()));

        // ── Analytics ────────────────────────────────────────────
        services.AddSingleton<IAnalyticsService, AnalyticsService>();

        // ── User Feedback ────────────────────────────────────────
        services.AddSingleton<IFeedbackService, FeedbackService>();

        // ── REST API ─────────────────────────────────────────────
        services.AddSingleton<IApiHostService, ApiHostService>();
        services.AddSingleton<IApiHostLifecycleService, ApiHostLifecycleService>();

        // ── Temporal Identity ─────────────────────────────────────
        services.AddSingleton<ITemporalIdentityService, TemporalIdentityService>();
        services.AddSingleton<IVoiceDraftService, VoiceDraftService>();

        // ── Notifications ────────────────────────────────────────
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IWorkflowLaunchService, WorkflowLaunchService>();
        services.AddSingleton<IOperationsDrillInService, OperationsDrillInService>();
        services.AddSingleton<IOperationsActionService, OperationsActionService>();
        services.AddSingleton<IOperationsOverviewService, OperationsOverviewService>();

        // ── System Tray ───────────────────────────────────────
        services.AddSingleton<SystemTrayService>();

        // ── Window Services (extracted from MainWindow) ──────────
        services.AddSingleton<IAppNavigationService, AppNavigationService>();
        services.AddSingleton<IStatusBarService, StatusBarService>();
        services.AddSingleton<IAnnunciatorService, AnnunciatorService>();
        services.AddSingleton<IOnboardingService, OnboardingService>();
        services.AddSingleton<IChromeService, ChromeService>();

        // ── ViewModels (Transient) ─────────────────────────────
        services.AddTransient<ViewModels.DashboardViewModel>();
        services.AddTransient<ViewModels.OperationsViewModel>();
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
        services.AddTransient<ViewModels.PastSelfViewModel>();
        // Keyboard Power Mode ViewModels — registered for testability.
        // MainWindow constructs them directly with runtime scope/callback values;
        // these factory registrations allow DI resolution with global-scope defaults.
        // Note: CommandPalette (UserControl), JumpToDialog, and CheatsheetDialog (ContentDialogs)
        // are NOT registered here — WinUI dialogs/controls are constructed on demand by the view,
        // not resolved from DI. Adding them would create an unused registration path.
        services.AddTransient<ViewModels.CommandPaletteViewModel>(sp =>
            new ViewModels.CommandPaletteViewModel(
                sp.GetRequiredService<IShortcutRegistry>(),
                activeScopeName: null));
        services.AddTransient<ViewModels.JumpToViewModel>(_ =>
            new ViewModels.JumpToViewModel(
                // CAUTION: factory returns empty candidates — for DI testability only.
                // MainWindow constructs JumpToViewModel with real document/conversation/page loaders.
                // Do NOT resolve from DI at runtime expecting populated results.
                loadCandidates: _ => System.Threading.Tasks.Task.FromResult<
                    System.Collections.Generic.IReadOnlyList<ViewModels.JumpToItem>>(
                    Array.Empty<ViewModels.JumpToItem>())));
        services.AddTransient<ViewModels.CheatsheetViewModel>(sp =>
            new ViewModels.CheatsheetViewModel(
                sp.GetRequiredService<IShortcutRegistry>(),
                activeScopeName: null,
                sp.GetRequiredService<ILocalizationService>()));

        // ── Views (Transient) ──────────────────────────────────
        services.AddTransient<Views.DashboardPage>();
        services.AddTransient<Views.OperationsPage>();
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
        services.AddTransient<Views.PastSelfPage>();
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
        var dbPath = AgentX.Core.Helpers.PathHelper.GetDatabasePath();
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
        var localization = GetService<ILocalizationService>();
        var box = new Microsoft.UI.Xaml.Controls.PasswordBox
        {
            PlaceholderText = localization.GetString("Startup_PassphrasePlaceholder")
        };
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = localization.GetString("Startup_UnlockTitle"),
            Content = box,
            PrimaryButtonText = localization.GetString("Startup_UnlockButton"),
            CloseButtonText = localization.GetString("Startup_ExitAppButton"),
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            XamlRoot = MainWindow.Content.XamlRoot,
        };
        var result = await dialog.ShowAsync();
        return result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary ? box.Password : null;
    }

    private static async System.Threading.Tasks.Task ShowInvalidPassphraseDialogAsync()
    {
        var localization = GetService<ILocalizationService>();
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = localization.GetString("Startup_WrongPassphraseTitle"),
            Content = localization.GetString("Startup_WrongPassphraseMessage"),
            CloseButtonText = localization.GetString("Startup_OkButton"),
            XamlRoot = MainWindow.Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    /// <summary>
    /// AX-QA-003 recovery state: the database migration failed, so the app is fail-closed — the REST
    /// API, connectors, FTS, and every data-backed feature were NOT started. Surface a blocking
    /// error explaining the situation and exit, so the user can restore from a backup rather than
    /// operate against a broken or partially-migrated schema. Runs on the UI thread via the window's
    /// dispatcher; the window shell is already created in <see cref="OnLaunched"/>.
    /// </summary>
    private static async System.Threading.Tasks.Task EnterMigrationRecoveryStateAsync(Exception? failure)
    {
        var missingTables = failure is AgentX.Core.Data.MigrationRunner.BaselineSchemaIncompleteException baselineEx
            ? string.Join(", ", baselineEx.MissingTables)
            : null;

        Log.Fatal(
            failure,
            "Startup halted in migration recovery state; data-backed features were not started. Missing tables: {MissingTables}",
            missingTables ?? "(none reported)");

        // The dialog text is localized; the log above stays in English for bug reports.
        var localization = GetService<ILocalizationService>();
        var details = missingTables is not null
            ? localization.GetString("Startup_SchemaIncomplete", missingTables)
            : localization.GetString("Startup_UpgradeFailed");

        var window = _mainWindow;
        if (window?.Content?.XamlRoot is null)
        {
            // No UI surface to host a dialog (should not happen — the shell is created first). Exit
            // immediately rather than continue running against an unmigrated database.
            Microsoft.UI.Xaml.Application.Current.Exit();
            return;
        }

        var tcs = new TaskCompletionSource();
        window.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                {
                    Title = localization.GetString("Startup_FailedTitle"),
                    Content =
                        details
                        + "\n\n" + localization.GetString("Startup_FailedHelp")
                        + "\n\n" + localization.GetString("Startup_FailedManualRestore"),
                    CloseButtonText = localization.GetString("Startup_ExitButton"),
                    XamlRoot = window.Content.XamlRoot,
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to display the migration recovery dialog");
            }
            finally
            {
                Microsoft.UI.Xaml.Application.Current.Exit();
                tcs.TrySetResult();
            }
        });

        await tcs.Task;
    }

    private static void ConfigureLogging()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentX", "Logs");
        var logPath = Path.Combine(logDirectory, "agentx-.log");
        var currentLogPath = Path.Combine(logDirectory, $"agentx-{DateTime.Now:yyyyMMdd}.log");

        Directory.CreateDirectory(logDirectory);

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

        Log.Information("Agent-X logging initialized at {LogPath}", currentLogPath);
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

        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    private static async void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        await ShutdownCoreServicesAsync();
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        // Bounded, so a shutdown step that never completes cannot hang process exit.
        ShutdownCoreServicesAsync().Wait(TimeSpan.FromSeconds(20));
    }

    private static async System.Threading.Tasks.Task ShutdownCoreServicesAsync()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) == 1)
            return;

        try
        {
            if (_host is not null)
            {
                // Bounded: a connector stuck mid-sync must not hold the app open.
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await _host.Services
                    .GetRequiredService<IBuiltinConnectorLifecycleService>()
                    .StopAsync(stopTimeout.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to stop built-in connectors during shutdown");
        }

        try
        {
            if (_host is not null)
            {
                await _host.Services
                    .GetRequiredService<IApiHostLifecycleService>()
                    .StopAsync()
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to stop REST API during shutdown");
        }

        try
        {
            _host?.Services.GetRequiredService<IBackupService>().StopScheduledBackups();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to stop scheduled backups during shutdown");
        }

        try
        {
            if (_host is not null)
            {
                // Each plugin's OnDeactivateAsync is capped by the plugin service.
                await _host.Services
                    .GetRequiredService<IPluginService>()
                    .DeactivateAllPluginsAsync()
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to deactivate plugins during shutdown");
        }

        try
        {
            switch (_host)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }

            _host = null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to dispose application host during shutdown");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
