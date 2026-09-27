# Agent-X Architecture Documentation

**Version:** 2.2.0
**Last Updated:** 2026-09-27
**Platform:** Windows 10 version 2004 (build 19041) or later; the app builds for x86, x64 and ARM64, the installer ships x64
**Runtime:** .NET 8 / WinUI 3 (Windows App SDK 1.6)

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Solution Structure](#2-solution-structure)
3. [High-Level System Architecture](#3-high-level-system-architecture)
4. [Architectural Patterns and Design Principles](#4-architectural-patterns-and-design-principles)
5. [Presentation Layer (AgentX.App)](#5-presentation-layer-agentxapp)
   - 5.1 [Application Bootstrap and DI Host](#51-application-bootstrap-and-di-host)
   - 5.2 [MainWindow and Navigation Shell](#52-mainwindow-and-navigation-shell)
   - 5.3 [MVVM Implementation](#53-mvvm-implementation)
   - 5.4 [Custom Controls](#54-custom-controls)
   - 5.5 [Value Converters](#55-value-converters)
   - 5.6 [XAML Resource Dictionaries and Theming](#56-xaml-resource-dictionaries-and-theming)
   - 5.7 [Keyboard Shortcut System](#57-keyboard-shortcut-system)
6. [Service Layer (AgentX.Core)](#6-service-layer-agentxcore)
   - 6.1 [AI Provider Architecture](#61-ai-provider-architecture)
   - 6.2 [Chat Services](#62-chat-services)
   - 6.3 [Document Processing Pipeline](#63-document-processing-pipeline)
   - 6.4 [Indexing Pipeline](#64-indexing-pipeline)
   - 6.5 [Search and RAG Pipeline](#65-search-and-rag-pipeline)
   - 6.6 [Intelligence Services](#66-intelligence-services)
   - 6.7 [Collections and Tagging](#67-collections-and-tagging)
   - 6.8 [Settings Service](#68-settings-service)
   - 6.9 [Feature Services](#69-feature-services)
7. [Data Layer (AgentX.Core/Data)](#7-data-layer-agentxcoredata)
   - 7.1 [Entity Framework Core Database Context](#71-entity-framework-core-database-context)
   - 7.2 [Entity Relationship Model](#72-entity-relationship-model)
   - 7.3 [Vector Store Implementation](#73-vector-store-implementation)
8. [Key Data Flows](#8-key-data-flows)
   - 8.1 [Document Import Flow](#81-document-import-flow)
   - 8.2 [Chat and Streaming Flow](#82-chat-and-streaming-flow)
   - 8.3 [RAG (Ask Your Files) Flow](#83-rag-ask-your-files-flow)
   - 8.4 [Search Mode Routing and Hybrid Search](#84-search-mode-routing-and-hybrid-search)
   - 8.5 [Knowledge Graph Construction Flow](#85-knowledge-graph-construction-flow)
9. [Navigation Architecture](#9-navigation-architecture)
10. [Dependency Injection Configuration](#10-dependency-injection-configuration)
11. [Storage Architecture](#11-storage-architecture)
12. [Startup Sequence](#12-startup-sequence)
13. [Error Handling and Resilience](#13-error-handling-and-resilience)
14. [Free and Open Source: No Feature Gating](#14-free-and-open-source-no-feature-gating)
15. [Testing Architecture](#15-testing-architecture)
16. [Deployment and Distribution](#16-deployment-and-distribution)
17. [Performance Characteristics](#17-performance-characteristics)
18. [Security Model](#18-security-model)
19. [Glossary](#19-glossary)
20. [Localization and Keyboard Power Mode](#20-localization-and-keyboard-power-mode)

---

## 1. Executive Summary

Agent-X is a Windows desktop application built with WinUI 3 and .NET 8. It imports documents into a
local Knowledge Vault, indexes them, answers questions over them with cited sources, and chats with
AI models. Retrieval-Augmented Generation (RAG) grounds answers in the user's own documents.

The application has three layers:

- **Presentation layer** (`AgentX.App`): a WinUI 3 app with a NavigationView shell of 29 rail
  entries (plus the first-run onboarding wizard), page view models built with
  `CommunityToolkit.Mvvm`, four chat coordinators, and shell services (navigation, instrument
  strip, onboarding, window chrome, tray, notifications, localization). `App.xaml.cs` is the
  composition root.
- **Service layer** (`AgentX.Core`): all business logic. AI providers and embeddings, document
  processing and indexing, semantic, keyword and hybrid search, RAG, chat and conversation memory,
  intelligence features, workflows, web import and web search, the Smart Inbox and its calendar and
  email connectors, plugins, backup, Collaborative Sync, Temporal Identity, security, settings and
  the local REST API.
- **Data layer** (`AgentX.Core/Data`): SQLite through Entity Framework Core with 37 entity types, EF
  migrations plus startup repairs (`MigrationRunner`), and a vector store that keeps embeddings as
  BLOBs in the same database file.

`AgentX.App` depends on `AgentX.Core`; `AgentX.Core` has no reference to the presentation layer.
Services are singletons; views and view models are transients, registered in a
`Microsoft.Extensions.Hosting` container.

The system is local first. The default provider is a built-in model (Llama 3.2 3B through
LLamaSharp; the OFFLINE installer bundles it and the first-run wizard of the SLIM installer offers
to download it), and all documents, embeddings and conversations stay on the machine. Ollama (local or
on another computer), OpenAI and Anthropic are optional providers; the app says where messages go
whenever one of them, model routing or Research Mode sends data off the computer.

---

## 2. Solution Structure

```
Agent-X/
  AgentX.sln                        Solution: AgentX.App, AgentX.Core, AgentX.Tests,
                                    LocaleAudit.Tool, LocaleAudit.Tests
  Directory.Build.props             Shared properties (C# 12, nullable, version 2.2.0)
  global.json                       Pins the .NET SDK (8.0.421, roll forward to latest feature)
  src/
    AgentX.App/                     WinUI 3 presentation layer
      App.xaml / App.xaml.cs        Entry point, DI host, startup and shutdown
      MainWindow.xaml/.cs           Shell (plus MainWindow.JumpTo.cs, MainWindow.StatusTrayOnboarding.cs)
      Views/                        30 pages, Dialogs/ (Jump-To, Cheatsheet), ExportDialog,
                                    QuickChatWindow, BranchCompareWindow
      ViewModels/                   Page and support view models; Coordinators/ for chat
      Controls/                     CommandPalette, Faceplate, LampTile, MarkdownMessageControl,
                                    NotificationOverlay, OAuthAppCredentialsPanel, SegmentMeter
      Converters/                   11 IValueConverter implementations
      Helpers/                      PageViewModelFactory, MarkdownParser, SyntaxHighlighter,
                                    FlowDirectionHelper, WindowPlacement, ...
      Services/                     Shell and lifecycle services (navigation, status strip,
                                    annunciators, onboarding, chrome, tray, startup orchestrator,
                                    API and connector lifecycles, operations, localization)
      Styles/                       Resource dictionaries (see 5.6)
      Themes/Generic.xaml           Faceplate control template
      Strings/<locale>/Resources.resw   UI strings in six languages
      appsettings.json              "Rag" options
      RagPrompts.json               RAG prompt texts (reloaded when the file changes)
    AgentX.Core/                    .NET 8 class library (service and data layers)
      AI/                           AiService, providers, embeddings, context, routing, agents
        Providers/                  LocalLlmProvider, OllamaProvider, OpenAiProvider, AnthropicProvider
        Context/                    ContextAssemblyService, SemanticContextSelector,
                                    ConversationCompressionService
        Routing/                    TaskTypeDetector, ModelRouterService, routing profiles
        Agents/                     MultiAgentOrchestrator
        Models/                     AiModel, ChatOptions, CostTracker, ToolDefinition
      Configuration/                RAG configuration and prompt catalog
      Data/                         AgentXDbContext, entities, migrations, MigrationRunner,
                                    serializing detector and query compiler, VectorDb/
      Documents/                    DocumentService, ChunkingService, AdaptiveChunkingService,
                                    Processors/ (8 built-in processors)
      Search/                       Semantic, keyword, hybrid search, RAG pipeline and its stages
      Services/                     Analytics, Annotations, Api, Audio, Backup, Chat, Collections,
                                    Export, FeatureFlags, Feedback, Inbox, Indexing, Intelligence,
                                    Localization, OAuth, Plugins (with Calendar and Email),
                                    Privacy, Screen, Search (web search), Security, Settings,
                                    Shortcuts, Sync, Tagging, TemporalIdentity, Web, Workflows,
                                    Workspace
      Observability/                RagMetrics, PiiDetector
      Validation/                   Settings, sync configuration and plugin manifest validators
      Helpers/                      PathHelper, HashHelper, FormatHelper, FileTypeHelper
    AgentX.Mobile/                  .NET MAUI Android companion (not in AgentX.sln)
  tests/
    AgentX.Tests/                   xUnit tests (Core, plus App sources linked into the project)
    LocaleAudit.Tests/              Tests for the locale audit tool
  tools/
    LocaleAudit/                    Localization coverage tool
  plugins/sample-plugin/            Sample document processor plugin
  browser-extension/                Browser extension (clips pages into the Smart Inbox)
  installer/AgentX-Setup.iss        Inno Setup script (SLIM and OFFLINE profiles)
  docs/                             This documentation
```

**Dependency direction (enforced by project references):**

```
AgentX.App    -> AgentX.Core -> NuGet packages only
AgentX.Tests  -> AgentX.Core (and compiles selected AgentX.App sources as linked files)
```

`AgentX.Core` never references `AgentX.App`, which keeps business logic testable without WinUI.

---

## 3. High-Level System Architecture

```
+----------------------------------------------------------------------------------+
| AgentX.App (WinUI 3)                                                             |
|  MainWindow shell: NavigationView rail, ContentFrame, instrument strip,          |
|                    CommandPalette, Jump-To, Cheatsheet, tray, Quick Chat         |
|  Views (30 pages) --> ViewModels --> Core service interfaces                     |
|  ChatViewModel --> Conversation / Messaging / Voice / Branching coordinators     |
|  Shell services: AppNavigationService, StatusBarService, AnnunciatorService,     |
|                  OnboardingService, ChromeService, SystemTrayService,            |
|                  StartupOrchestrator, ApiHostLifecycleService,                   |
|                  BuiltinConnectorLifecycleService, LocalizationService           |
+----------------------------------------------------------------------------------+
        |                                                     ^
        v                                                     | events (indexing, notifications)
+----------------------------------------------------------------------------------+
| AgentX.Core                                                                      |
|  AI:        AiService -> LocalLlmProvider (LLamaSharp) | OllamaProvider          |
|                         | OpenAiProvider | AnthropicProvider                     |
|             EmbeddingService (+ cache), ContextAssemblyService, ModelRouterService,|
|             MultiAgentOrchestrator, CostTracker                                  |
|  Documents: DocumentService -> IDocumentProcessor x 8 (+ plugin processors)      |
|  Indexing:  IndexingService (Channel<long> loop) -> ChunkingService,             |
|             EmbeddingService, IVectorStore, KeywordSearchService, AutoTagService |
|  Search:    HybridSearchOrchestrator -> SemanticSearchService + KeywordSearch    |
|             RagPipeline -> query expansion, HyDE, rerankers, compression,        |
|                            citations, evaluation, optional web search           |
|  Features:  Chat, memory and recall, intelligence, inbox and connectors, OAuth,  |
|             plugins, workflows, web import, export, backup, sync, Temporal       |
|             Identity, security, settings, local REST API                         |
+----------------------------------------------------------------------------------+
        |
        v
+----------------------------------------------------------------------------------+
| Data: AgentXDbContext (one shared, serialized instance) + vector store           |
+----------------------------------------------------------------------------------+
        |
        v
+----------------------------------------------------------------------------------+
| %LocalAppData%\AgentX\                                                           |
|   agentx.db (EF tables, fts_chunks, vec_embeddings; optionally SQLCipher)        |
|   settings.json, encryption.info.json, usage-history.json, Logs\, Models\,       |
|   Plugins\, Clips\, Inbox\                                                       |
+----------------------------------------------------------------------------------+

External (optional): Ollama (http://localhost:11434 by default), api.openai.com,
api.anthropic.com, Brave / Serper / SearXNG web search, Google and Microsoft APIs for the
connectors, Hugging Face for model downloads.
```

---

## 4. Architectural Patterns and Design Principles

### 4.1 MVVM (Model-View-ViewModel)

Each page has a view model built with the `CommunityToolkit.Mvvm` source generators:

- `[ObservableProperty]` generates `INotifyPropertyChanged` properties.
- `[RelayCommand]` generates `ICommand` implementations, with async support.
- `ObservableCollection<T>` backs lists that update live (messages, documents, results).

Pages obtain their view model in the constructor. A view model that implements `IDisposable` is
created with `PageViewModelFactory.Create<T>()` (`Helpers/PageViewModelFactory.cs`), which builds it
with `ActivatorUtilities` so the root DI provider does not keep it until shutdown (the `Frame`
caches at most ten pages, so rebuilt pages would otherwise leak their old view models). Other pages
use `App.GetService<T>()`. The `PagesCreateDisposableViewModelsUntrackedTests` code-quality test
fails when a page resolves an `IDisposable` view model through the root provider.

Operations that need the window (file and folder pickers, drag and drop, dialogs with a `XamlRoot`)
stay in code-behind or receive the window explicitly, so view models stay free of WinUI calls where
possible. Many view models are compiled into the test project and tested there.

### 4.2 Interface-Based Service Contracts

Core services expose interfaces (`IAiService`, `IDocumentService`, `IChatService` and so on), and
view models and other services depend on those interfaces. This allows substitution in tests (Moq
or hand-written fakes) and keeps the presentation layer independent of implementations.

### 4.3 Singleton Services, Transient Views and ViewModels

| Registration | Lifetime | Reason |
|---|---|---|
| Core services | Singleton | They hold state (providers, the indexing queue, caches, timers) and are expensive to build. |
| `AgentXDbContext` | Singleton | One long-lived EF Core context, shared by the UI and all background work. A `DbContext` is not thread-safe, and SQLite's WAL mode does not change that (it lets separate connections read while one writes), so the context serializes its own operations; see the note below. |
| `IVectorStore` | Singleton | Created by `VectorStoreFactory`: `HnswVectorStore` when `EnableHnswIndex` is on (the default), otherwise `SqliteVecStore`. It keeps its own connection to the database file. |
| Views and view models | Transient | A new instance per navigation that builds a page. The `Frame` caches pages, so going back does not always rebuild one. |

**The shared context is serialized, not concurrent.** The indexing loop, the local REST API,
status-bar polling, scheduled backup and sync, and connector timers all use the one
`AgentXDbContext` alongside the UI thread. The context replaces EF Core's concurrency detector with
`SerializingConcurrencyDetector`, so an overlapping operation waits for the one in flight instead of
throwing, and `SerializingQueryCompiler` holds the same gate across whole query executions.
`SaveChanges`/`SaveChangesAsync` run under the gate and discard the pending changes of a save that
fails, so a rejected change cannot poison every later save. Raw ADO.NET sections join the gate
through `EnterDatabaseGate()`. Because all of this work queues on one gate, background services
should keep each database section short and do slow work (embedding, model calls, file I/O) outside
it.

### 4.4 Awaited, Fail-Closed Startup

`App.OnLaunched` builds the host, applies the saved UI language, creates the window, and then calls
`InitializeCoreServicesAsync()`. That method is `async void` (nothing awaits `OnLaunched`), but
everything inside it runs in a fixed, awaited order. The critical part is the database: the key is
applied and the migration runs through `StartupOrchestrator` before any data-backed feature starts.
If the migration fails, the app enters a recovery state, shows a dialog and exits (fail closed).
Best-effort steps after the gate (FTS, AI, plugins, backups, indexing, watch folders) each log a
failure and let startup continue. See [section 12](#12-startup-sequence) for the full order.

The window appears before initialization finishes. Data-backed UI that could run before the
migration (the Dashboard, the first page shown) waits on `IStartupGate.WaitForDataReadyAsync()`,
which `StartupOrchestrator` opens the moment the migration succeeds.

#### 4.4a Encryption Unlock Preamble (C13)

Before the migration runs, `InitializeCoreServicesAsync` first calls
`IDatabaseEncryptionMigrator.RecoverIfNeeded` to finish or undo an encryption change a crash
interrupted. It then checks for `%LocalAppData%\AgentX\encryption.info.json` via
`IEncryptionStateFile`. If the marker is present, the database key is unlocked via
`IDatabaseKeyService` (a DPAPI unwrap for the `DpapiWrapped` mode, or a passphrase prompt with a
PBKDF2-HMAC-SHA256 derivation for a legacy `UserPassphrase` keystore) and stored in
`IDatabaseKeyProvider`, so every `SqliteConnection` opened through `IEncryptedConnectionFactory`
applies the same `PRAGMA key`. `AgentXDbContext.EnsureKeyApplied()` then applies it to the shared
connection. The keystore lives outside the encrypted database on purpose: startup needs the key
before it can read the database.

### 4.5 Channel-Based Background Queue

`IndexingService` uses a `System.Threading.Channels.Channel<long>` (unbounded, single reader,
multiple writers). Imports and re-indexes enqueue document IDs from any thread (through the
`DocumentPendingIndexing` event of `IDocumentService`). One background task reads the channel and
processes documents one at a time, so embedding generation never runs several documents at once.
When the channel stays empty for 30 seconds, the loop sweeps the database for documents left
`pending` by other paths and re-embeds chunks stamped before embedding model versions were recorded.

### 4.6 Coordinators for Chat

`ChatViewModel` keeps UI state and delegates the chat workflows to four singleton coordinators in
`ViewModels/Coordinators/`:

- `IConversationCoordinator`: loading, creating, deleting, pinning, filing and searching
  conversations, and editing or truncating their messages.
- `IMessagingCoordinator`: sending, streaming, stopping and regenerating replies, the multi-agent
  modes, Research Mode web results, feedback, and failure text.
- `IVoiceCoordinator`: voice input (recording and Whisper transcription).
- `IBranchingCoordinator`: creating, listing, merging and deleting conversation branches.

Other pages call Core services from their view models directly.

---

## 5. Presentation Layer (AgentX.App)

### 5.1 Application Bootstrap and DI Host

`App.xaml.cs` is the application entry point. `OnLaunched` builds a generic host:

```csharp
_host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
    .ConfigureAppConfiguration((ctx, config) =>
    {
        config.SetBasePath(AppContext.BaseDirectory);
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        config.AddJsonFile("RagPrompts.json", optional: true, reloadOnChange: true);
    })
    .UseSerilog()
    .ConfigureServices(ConfigureServices)
    .Build();
```

**Relevant file:** `src/AgentX.App/App.xaml.cs`

The static `App.GetService<T>()` resolves from the root provider. It is the service locator the
shell and pages use, because WinUI's `Frame` does not create pages through DI.

Logging is configured in the `App` constructor with Serilog:
- a `Debug` sink for the Visual Studio Output window;
- a `File` sink, rolling daily with 7 files kept, writing to
  `%LocalAppData%\AgentX\Logs\agentx-yyyyMMdd.log` with the template
  `{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}`.

Global exception handling is wired to:
- `AppDomain.CurrentDomain.UnhandledException`: logs a fatal error and flushes the log.
- `TaskScheduler.UnobservedTaskException`: logs and marks the exception observed.
- `Application.UnhandledException`: logs a fatal error and sets `Handled = true`.
- `AppDomain.CurrentDomain.ProcessExit`: runs the shutdown sequence, waiting at most 20 seconds.

### 5.2 MainWindow and Navigation Shell

`MainWindow.xaml.cs` (with `MainWindow.JumpTo.cs` and `MainWindow.StatusTrayOnboarding.cs`) owns the
window chrome and delegates navigation to `IAppNavigationService`.

**Window configuration** (`ChromeService`):
- Preferred size 1440 x 900, centered in the work area of the display the window opens on (clamped
  to it, so the title bar never ends up off screen).
- Backdrop: Mica Alt (`MicaKind.BaseAlt`) where Mica is supported, otherwise Desktop Acrylic,
  otherwise a solid background.
- The title bar is extended into the content, and `AppTitleBar` is the drag region.

**Navigation model:**

```csharp
private static readonly Dictionary<string, Type> PageMap = new()
{
    ["Dashboard"] = typeof(Views.DashboardPage),
    ["Operations"] = typeof(Views.OperationsPage),
    ["Digest"] = typeof(Views.DigestPage),
    ["Settings"] = typeof(Views.SettingsPage),
    ["Chat"] = typeof(Views.ChatPage),
    ["AskFiles"] = typeof(Views.AskFilesPage),
    ["QuickActions"] = typeof(Views.QuickActionsPage),
    ["Workflows"] = typeof(Views.WorkflowBuilderPage),
    ["KnowledgeVault"] = typeof(Views.KnowledgeVaultPage),
    ["WebImport"] = typeof(Views.WebImportPage),
    ["Collections"] = typeof(Views.CollectionManagerPage),
    ["Search"] = typeof(Views.SearchPage),
    ["KnowledgeGraph"] = typeof(Views.KnowledgeGraphPage),
    ["ModelManager"] = typeof(Views.ModelManagerPage),
    ["HardwareAdvisor"] = typeof(Views.HardwareAdvisorPage),
    ["BackupRestore"] = typeof(Views.BackupRestorePage),
    ["Annotations"] = typeof(Views.AnnotationsPage),
    ["Inbox"] = typeof(Views.InboxPage),
    ["Comparison"] = typeof(Views.ComparisonPage),
    ["WorkspaceProfiles"] = typeof(Views.WorkspaceProfilePage),
    ["PluginManager"] = typeof(Views.PluginManagerPage),
    ["SyncSettings"] = typeof(Views.SyncSettingsPage),
    ["CalendarSettings"] = typeof(Views.CalendarSettingsPage),
    ["EmailSettings"] = typeof(Views.EmailSettingsPage),
    ["Analytics"] = typeof(Views.AnalyticsPage),
    ["PastSelf"] = typeof(Views.PastSelfPage),
    ["Onboarding"] = typeof(Views.OnboardingPage),
    ["UserGuide"] = typeof(Views.UserGuidePage),
    ["PrivacyPolicy"] = typeof(Views.PrivacyPolicyPage),
    ["TermsOfService"] = typeof(Views.TermsOfServicePage),
};
```

`NavigationView.SelectionChanged` reads the selected item's `Tag` and navigates to the mapped page.
`_navItemMap` holds the 29 rail items so that navigation from a shortcut, the command palette,
Jump-To, the tray or a status lamp also moves the rail's selection indicator. All paths go through
`IAppNavigationService.NavigateToPage(tag, parameter)`; the optional parameter carries what the user
picked (for example a conversation from Jump-To).

**Instrument strip (status bar):** two typed pollers feed the bottom strip.

- `StatusBarService` polls every 30 seconds after a 5-second delay: the active provider's
  connection (`CheckConnectionAsync`) and model for the `MDL` lamp and LCD readout, the indexing
  queue for `IDX`, and the document count for `VAULT`. The text names the connected model, or the
  active provider when it is not available, in the user's language. Each cycle also re-evaluates
  the `LOCAL`/`NET` privacy lamp through `IPrivacyStatusService`.
- `AnnunciatorService` polls every 30 seconds after a 6-second delay. The inbox pending count and
  sync state are read every cycle; backup age and workflow-run health every fourth cycle. They drive
  the `INBOX`, `SYNC`, `JOBS` and `BAK` lamps.

Each source fails soft (a failed query keeps the previous state). Lamps map typed states, never
display strings:

| Lamp | States | Click (lit only) |
|---|---|---|
| `MDL` | Go when the active provider is connected, Hold when it is not | Model Manager |
| `LOCAL` / `NET` | `LOCAL` and Go when nothing leaves the computer, `NET` and Hold otherwise | Settings |
| `INBOX` | Hold while items wait for triage, otherwise Off | Smart Inbox |
| `SYNC` | Off when sync is not configured; NoGo on error, Scope while syncing, otherwise Go | Collaborative Sync |
| `JOBS` | Scope while a workflow runs, NoGo after a failed latest run, otherwise Off | Operations |
| `BAK` | Go when the last backup is at most 7 days old, Hold when older, Off with no backup | Backup & Restore |

An unlit (Off) lamp ignores clicks. The status-strip handler also restores the navigation pane if
it finds it hidden outside onboarding (logged as "Nav pane was hidden outside of onboarding -
restored").

**Onboarding:** on first run (`OnboardingCompleted == false`) `IOnboardingService.BeginOnboarding()`
suppresses rail navigation, the frame shows `OnboardingPage`, and the pane is hidden. Finish calls
`MainWindow.CompleteOnboarding()`, which marks onboarding complete and navigates to the Dashboard.
Leaving the wizard any other way (a shortcut, the palette, Jump-To, the tray, a lamp) ends it as
skipped: the rail comes back and the wizard does not return at the next launch.

**Tray and Quick Chat:** `SystemTrayService` (H.NotifyIcon) keeps a tray icon with Open, Quick
Chat, Settings and Exit, supports minimizing to the tray, and registers the global hotkey
**Win+Shift+A**, which opens the Quick Chat window.

**Relevant files:**
- `src/AgentX.App/MainWindow.xaml`
- `src/AgentX.App/MainWindow.xaml.cs`
- `src/AgentX.App/MainWindow.StatusTrayOnboarding.cs`
- `src/AgentX.App/Services/AppNavigationService.cs`

### 5.3 MVVM Implementation

Page view models, with the page each one serves:

| ViewModel | Page | Primary responsibilities |
|---|---|---|
| `DashboardViewModel` | Dashboard | Counts and recent activity, provider status and hints, privacy disclosures, belief card, operations snapshot |
| `OperationsViewModel` | Operations | Operations overview (connectors, inbox, indexing, summaries, sync, workflows) and guided actions |
| `DigestViewModel` | Weekly Digest | Generate and show digest reports |
| `AnalyticsViewModel` | Analytics | Usage, file type, indexing, workflow and conversation-intelligence metrics |
| `PastSelfViewModel` | Past Self | Past stances and insights by time period, active topics, Draft As Me |
| `ChatViewModel` | AI Chat | Conversation list, streaming messages, branches, memories, context inspector (with the coordinators) |
| `AskFilesViewModel` | Ask Your Files | RAG questions with streamed answers and citation badges |
| `QuickActionsViewModel` | Quick Actions | Summarize, extract key points, translate, find duplicates and near-duplicates, suggest organization, recommended actions |
| `WorkflowBuilderViewModel` | Workflows | Build, edit, run and inspect workflows |
| `KnowledgeVaultViewModel` | Knowledge Vault | Document list, import, filters, preview, delete with confirmation, engagement timing |
| `DocumentNotesViewModel` | (Vault preview) | Passage-by-passage text and annotation creation in the preview |
| `WebImportViewModel` | Web Import | URL, feed and sitemap import with one result per URL |
| `CollectionManagerViewModel` | Collections | Collection CRUD, nesting ("Move into..."), document assignment, export |
| `SearchViewModel` | Semantic Search | Semantic, keyword and hybrid search, filters, saved searches, history |
| `KnowledgeGraphViewModel` | Knowledge Graph | Graph data loading, filters, selection and statistics |
| `ComparisonViewModel` | Compare Documents | Select documents, compare and export the report |
| `AnnotationsViewModel` | Annotations | Search, filter, edit, delete and export annotations |
| `InboxViewModel` | Smart Inbox | Review, accept, reject and defer inbox items |
| `ModelManagerViewModel` | Model Manager | Ollama models, and the speech-to-text model through `SpeechModelViewModel` |
| `HardwareAdvisorViewModel` | Hardware Advisor | Hardware detection and model recommendations |
| `BackupRestoreViewModel` | Backup & Restore | Create, validate, restore and delete backups; backup schedule |
| `WorkspaceProfileViewModel` | Workspace Profiles | Create, edit, delete and mark workspace profiles |
| `PluginManagerViewModel` | Plugin Manager | Install, enable, disable and uninstall plugins |
| `SyncSettingsViewModel` | Collaborative Sync | Sync folder and encryption key, Sync Now, auto-sync, history |
| `CalendarSettingsViewModel` | Calendar | Connect accounts, choose calendars, sync settings, Sync Now |
| `EmailSettingsViewModel` | Email | Connect accounts, choose folders, sync settings, Sync Now |
| `SettingsViewModel` | Settings | All settings, with `BuiltInModelSettingsViewModel` and `WatchFolderSettingsViewModel` |
| `OnboardingViewModel` | Onboarding | First-run wizard |
| `UserGuideViewModel` | User Guide | Guide sections |

Support view models: `CommandPaletteViewModel`, `JumpToViewModel`, `CheatsheetViewModel`,
`QuickChatViewModel` (Quick Chat window), `ExportViewModel` (export dialog, used from Chat and
Collections), `OAuthAppCredentialsViewModel` (the credentials form on the Calendar and Email
pages), and item types such as `ChatMessageItem`, `ConversationListItem` and `SystemPromptItem`.

### 5.4 Custom Controls

| Control | Purpose |
|---|---|
| `CommandPalette` | Overlay listing every rail page (same tags, localized labels, glyphs and group placards as the rail, with each page's chord) and three actions: New Conversation, Import Files, Toggle Theme. Opened with Ctrl+K or Ctrl+Shift+P; Escape closes it. |
| `Faceplate` | The raised panel of the DESIGN.md depth system, with a kicker line (template in `Themes/Generic.xaml`). |
| `LampTile` | A status lamp with the DESIGN.md LED semantics; raises `Invoked` when clicked. |
| `SegmentMeter` | A segmented meter (green to 60%, amber to 85%, red above). |
| `MarkdownMessageControl` | Renders chat answers from `MarkdownParser` segments: text with bold and inline code, headings, bulleted and numbered list items, and fenced code blocks with syntax highlighting (`SyntaxHighlighter`) and a Copy button. |
| `NotificationOverlay` | Toast notifications from `INotificationService`, top right. |
| `OAuthAppCredentialsPanel` | The OAuth App Credentials form shared by the Calendar and Email pages. |

WinUI 3 has no Markdown renderer, so `MarkdownParser` is a small line-based parser rather than
Markdig; it covers the patterns AI answers use.

### 5.5 Value Converters

Eleven `IValueConverter` implementations live in `Converters/`. Pages declare the ones they use in
their own resources.

| Converter | Input | Output | Use |
|---|---|---|---|
| `BoolToOpacityConverter` | `bool` | `double` (1.0, or 0.5 by default for false) | Fade disabled content |
| `BoolToVisibilityConverter` | `bool` | `Visibility` (with `IsInverted`) | Show or hide elements |
| `CountToVisibilityConverter` | number | `Visibility` | Show only when a list has items |
| `DoubleToStringConverter` | `double` | `string` | Numbers in templates |
| `InverseBoolConverter` | `bool` | `bool` | Inverse binding |
| `NullToVisibilityConverter` | `object?` | `Visibility` | Null checks |
| `PercentToWidthConverter` | `double` (0 to 1) | `double` | Progress widths (maximum from the parameter) |
| `StatusToColorConverter` | status string or tone token | `Brush` | Status colors |
| `StringEmptyToVisibilityConverter` | `string?` | `Visibility` | Collapse on null or empty |
| `StringToVisibilityConverter` | `string?` | `Visibility` | Collapse on null, empty or whitespace |
| `TimeAgoConverter` | `DateTime` / `DateTimeOffset` | `string` ("5m ago", "just now", or a date) | Relative times, in the user's language |

### 5.6 XAML Resource Dictionaries and Theming

`App.xaml` merges these dictionaries from `Styles/`:

| File | Contents |
|---|---|
| `Colors.xaml` | Command Console token layer: armed red `#AA2024` accent, LED status vocabulary, well and LCD display tokens, spacing and radius scales, and Fluent lightweight overrides, defined per theme in `ThemeDictionaries` (dark, light, high contrast) |
| `Typography.xaml` | The bundled typefaces (Public Sans body, Archivo Expanded stencil placards, Departure Mono telemetry, Iosevka Term streams and code) and the heading, body and metric style scales |
| `Hardware.xaml` | Faceplate, recessed-well, lamp-tile, and machined and armed cap button recipes (the hardware depth system) |
| `Controls.xaml` | Button, card and input styles |
| `Navigation.xaml` | NavigationView overrides, item styles, section header placards |
| `Chat.xaml` | Message styles, role colors, streaming indicator |
| `Documents.xaml` | Document card layouts, status badges, file type icons |
| `UserGuideSections.xaml` | User Guide section templates (it merges the `UserGuideSections.*.xaml` parts) |

The visual system is defined in [`DESIGN.md`](../DESIGN.md) at the repository root, the source of
truth for all tokens, recipes and rules. The application ships three themes: dark (Night Shift, the
default), light (Day Shift, brushed silver), and high contrast (bound to `SystemColor*` tokens and
exempt from the hardware skin). Display surfaces (LCD wells, lamp caps and the instrument strip)
stay dark in both the dark and light themes by design. `IThemeService` applies the theme saved in
settings at startup.

### 5.7 Keyboard Shortcut System

`IShortcutRegistry` (`AgentX.Core.Services.Shortcuts`) holds every shortcut as a
`ShortcutDescriptor` (id, label, scope, chord, handler, category). `ShortcutCatalog` seeds the
global descriptors when `MainWindow` starts, with labels from the resources. Pages register
page-scoped descriptors in `OnNavigatedTo` with `registry.RegisterShortcuts(...)` and dispose the
returned token in `OnNavigatedFrom`. `ShortcutInputRouter` handles `RootGrid.PreviewKeyDown`,
opens the three built-in surfaces itself, and otherwise dispatches the chord through the registry
for the active page scope.

| Shortcut | Action |
|---|---|
| `Ctrl+K`, `Ctrl+Shift+P` | Command Palette |
| `Ctrl+P` | Jump To (documents, conversations, pages) |
| `F1`, `Ctrl+Shift+?` | Keyboard shortcuts (Cheatsheet) |
| `Ctrl+N` | New conversation (opens AI Chat on a new conversation) |
| `Ctrl+I` | Knowledge Vault |
| `Ctrl+F`, `Ctrl+Shift+F` | Semantic Search |
| `Ctrl+,` | Settings |
| `Ctrl+D` | Dashboard |
| `Ctrl+G` | Knowledge Graph |
| `Ctrl+Shift+A` | Analytics |
| `Ctrl+Shift+O` | Operations |
| `Ctrl+Shift+W` | Workflows |
| `Ctrl+Shift+E` | Web Import |
| `Ctrl+1` to `Ctrl+9` | Dashboard, AI Chat, Ask Your Files, Semantic Search, Knowledge Vault, Collections, Workflows, Model Manager, Settings |
| `Escape` | Close the Command Palette (when the search box does not have focus) |

Page-scoped shortcuts: AI Chat `Ctrl+Shift+N` (new conversation) and `Ctrl+B` (toggle the
conversation pane); Knowledge Vault `F5` (refresh); Settings `Ctrl+S` (save).

Modifier states are read with `InputKeyboardSource.GetKeyStateForCurrentThread`.

---

## 6. Service Layer (AgentX.Core)

### 6.1 AI Provider Architecture

```
AiService (IAiService)
    providers (by id):
        "local"      LocalLlmProvider    LLamaSharp, GGUF file in StoragePath\Models
        "ollama"     OllamaProvider      OllamaSharp, OllamaEndpoint (when it is a valid URL)
        "openai"     OpenAiProvider      HttpClient + SSE, when an OpenAI key is set
        "anthropic"  AnthropicProvider   HttpClient + SSE, when an Anthropic key is set
    active provider: ActiveProviderId from settings ("local" by default); an unknown or
                     unregistered id falls back to another registered provider
    operations: StreamChatAsync, ChatAsync, SummarizeAsync, GenerateTagsAsync,
                SwitchProviderAsync, SetActiveModelAsync, GetProvider, IsProviderAvailableAsync,
                GetDefaultModelId, ResolveEmbeddingTarget
```

**`IAiProvider`** is the low-level contract every provider implements: `CheckConnectionAsync`,
`ListModelsAsync`, `PullModelAsync`, `DeleteModelAsync`, `StreamChatAsync` (an
`IAsyncEnumerable<string>` of tokens), `ChatAsync`, `GenerateEmbeddingAsync` and
`GenerateEmbeddingsAsync`.

**`AiService`** builds the provider set in `InitializeAsync` from settings. It builds the new set
completely before publishing it, so a bad setting never leaves the service without a provider, and
it keeps a provider instance whose configuration did not change, so saving settings does not reload
the built-in model or cut off a stream. `SwitchProviderAsync` changes the active provider at run
time.

**Provider details:**

| Provider | Transport | Models | Embeddings |
|---|---|---|---|
| Built-in (`local`) | LLamaSharp 0.19 (CPU backend; CUDA 12 backend when the NVIDIA CUDA 12 toolkit is installed) | The configured GGUF file (`llama-3.2-3b-instruct-q4_k_m.gguf` by default); in SLIM installs the onboarding wizard offers to download it (`BuiltInModelBootstrap`) | Yes |
| Ollama | OllamaSharp 4.0.6; connection check times out after 3 seconds | Installed Ollama models (pull and delete supported) | Yes |
| OpenAI | `HttpClient`, `Authorization: Bearer`, SSE; the endpoint is configurable for compatible servers | Model list from the API | Yes (`text-embedding-*` models) |
| Anthropic | `HttpClient`, `x-api-key` and `anthropic-version: 2023-06-01`, SSE; the system prompt goes in the top-level `system` field | `GET /v1/models`, with a small fallback catalog; default `claude-sonnet-5` | No (throws `NotSupportedException`) |

`LocalGpuLayers` controls GPU offload of the built-in model: `0` means automatic (an NVIDIA GPU is
detected and a layer count chosen by video memory), a positive number is used as given, and a
negative number keeps the model on the CPU. Settings exposes it as "Automatic GPU layers" and
"GPU Layers".

**Embeddings.** `EmbeddingService` does not simply use the chat provider. `AiService.ResolveEmbeddingTarget()`
(`EmbeddingTargetResolver`) chooses the embedding provider independently, so switching the chat
model never changes the embedding space:

1. An OpenAI embedding model id (`text-embedding-*`) in the Embedding Model setting embeds with
   OpenAI (the only way document text is embedded in the cloud).
2. A `.gguf` file name selects the built-in provider.
3. Any other non-default name is used as an Ollama model.
4. The default setting (`all-minilm`) uses the built-in model when its file is installed, and
   Ollama's `all-minilm` otherwise.

Anthropic is never chosen. Batches use `Rag:EmbeddingBatchSize` (32). The vector size is learned
from the provider's output, and each chunk is stamped with `provider:model:dimensions`
(`EmbeddingModelVersion`). `CachedEmbeddingService` wraps the service with a bounded LRU cache keyed
by model version and text.

**Context.** `ContextAssemblyService` builds the prompt for a chat reply within
`ContextWindow` minus a 1,024-token reserve for the answer: it keeps the system prompt and the
current message, selects history (`SemanticContextSelector`), summarizes older overflow
(`ConversationCompressionService`), adds memory context, and, when enough budget remains, adds up
to three recalled passages from other conversations (`IConversationRecallService`, similarity 0.72
or more). On any failure it falls back to `ContextWindowManager`, which trims the oldest messages.
`TokenCounter` estimates tokens by characters, with CJK text counted separately.

**Model routing.** When Enable Auto-Routing is on (Settings, Multi-Model Routing),
`ModelRouterService` classifies the message (`TaskTypeDetector`) and picks a provider and model
from the active routing profile (`balanced`, `cost-optimized` or `quality-optimized`). The
decision applies to that reply only; the app-wide provider and the saved settings do not change.

**`CostTracker`** records token usage per call, priced from a per-model table matched by longest
model-id prefix; a model without a price entry (the built-in and Ollama models) costs nothing. The
history is saved in `usage-history.json` (90 days, at most 20,000 records, older totals carried
forward), so the Cost Tracking totals in Settings survive restarts. Providers report usage to it
directly.

**Relevant files:**
- `src/AgentX.Core/AI/AiService.cs`, `IAiProvider.cs`, `IAiService.cs`
- `src/AgentX.Core/AI/EmbeddingService.cs`, `EmbeddingTargetResolver.cs`, `CachedEmbeddingService.cs`
- `src/AgentX.Core/AI/Providers/*.cs`
- `src/AgentX.Core/AI/Context/ContextAssemblyService.cs`
- `src/AgentX.Core/AI/Routing/ModelRouterService.cs`
- `src/AgentX.Core/AI/Models/CostTracker.cs`

### 6.2 Chat Services

**`ChatService`** handles standard replies. `SendMessageAsync(conversationId, message)` returns an
`IAsyncEnumerable<string>`:

1. Starts a generation (cancels one in flight; a lock serializes starts and stops).
2. Saves the user message through `ConversationService`.
3. Loads the conversation (system prompt and messages).
4. Builds `ChatOptions` from settings (temperature, max tokens, context window).
5. Asks the model router for this reply's provider and model (when routing is on).
6. Loads memory context: relevant memories from `SemanticMemoryService`, or the top 8 from
   `ConversationMemoryService` when that is unavailable. Supplemental context (Research Mode web
   results) is added for this reply only.
7. Assembles the context (`ContextAssemblyService`) and records a context-inspection snapshot.
8. Streams tokens from the routed provider or `IAiService.StreamChatAsync`.
9. Saves the answer with its token count, generation time, the model that wrote it and the sources
   it cited (`CitationsJson`).
10. Starts memory extraction in the background.

`RegenerateResponseAsync` answers the saved prompt again in place and removes the old answer only
after the new one is saved; only the latest exchange can be regenerated. An edited prompt is resent
after `ConversationService.DeleteMessageAndFollowingAsync`. `StopGenerationAsync` cancels the
internal token that is linked to the caller's token.

The multi-agent modes do not go through `ChatService`: `MessagingCoordinator` calls
`MultiAgentOrchestrator` directly and saves the result (see [8.2](#82-chat-and-streaming-flow)).

**`ConversationService`**: CRUD for conversations and messages, and message ordering
(`SortOrder`).

**`SystemPromptService`**: CRUD for system prompts, by category.

**`ConversationMemoryService`** and **`SemanticMemoryService`**: extract facts from recent turns
with the AI model (the basic service asks for `category|content` lines with the categories
preference, fact, topic and instruction), store them in `memories` with importance, decay and an
embedding, and retrieve them for prompts. The context inspector's Memories card lists them and can
delete one or all.

**`ConversationRecallService`**: embeds messages and finds relevant passages in other conversations.

**`ConversationSummaryService`**: maintains versioned summary snapshots per conversation.

**`ConversationBranchService`**: creates branches (a copy of the messages up to a branch point) and
lists them.

**Relevant files:** `src/AgentX.Core/Services/Chat/`

### 6.3 Document Processing Pipeline

**`DocumentService`** is the entry point for imports:

- Validates the file and its extension.
- Computes a SHA-256 content hash (`HashHelper.ComputeFileHashAsync`); a match with an existing
  document throws `DuplicateDocumentException`, unless the caller allows duplicates
  (`ImportFilesWithReportAsync` reports them instead).
- Picks the first registered `IDocumentProcessor` whose `CanProcess` accepts the file, then
  processors contributed by active plugins (`IPluginDocumentProcessorSource`).
- Extracts text and metadata once. A file the processor cannot read is recorded as a `failed`
  document with the reason, not as an empty success.
- Saves a `DocumentEntity` as `pending`, links it to a collection when one is given, and raises
  `DocumentPendingIndexing` with the extracted text, so the indexer does not read the file again.
- Other entry points: `ImportExternalContentAsync` (connector items, keeping a type such as
  `CalendarEvent`), `ImportPreparedDocumentAsync` (Web Import), re-index, delete (removes FTS rows
  and cached search results), and `RequeueAudioAwaitingSpeechModelAsync` (after the speech model is
  installed).

**Eight built-in `IDocumentProcessor` implementations**, in registration order:

| Processor | Extensions | Library | Notes |
|---|---|---|---|
| `PdfProcessor` | `.pdf` | PDFsharp 6.1.1 | Page-by-page text; pages separated for page numbers |
| `DocxProcessor` | `.docx` | DocumentFormat.OpenXml 3.2.0 | Paragraph text with headings |
| `TextProcessor` | `.txt`, `.csv`, `.log`, `.json`, `.xml`, `.yaml`, `.yml`, `.toml`, `.ini`, `.cfg` | (built-in) | UTF-8 text |
| `MarkdownProcessor` | `.md`, `.mdx`, `.markdown` | Markdig 0.37 | Markdown to text |
| `CodeFileProcessor` | `.cs`, `.js`, `.ts`, `.py`, `.java`, `.cpp`, `.c`, `.h`, `.go`, `.rs`, `.swift`, `.kt`, `.rb`, `.php`, `.html`, `.htm`, `.css`, `.scss`, `.sql`, `.sh`, `.xaml` (it also lists `.yaml`, `.yml`, `.toml`, `.ini` and `.cfg`, which `TextProcessor` takes first) | (built-in) | Detects the language and first declaration |
| `ImageProcessor` | `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tiff` | Windows OCR (`Windows.Media.Ocr`) | Text recognized in the image; fails with a reason when no recognizer is installed |
| `AudioProcessor` | `.mp3`, `.wav`, `.m4a`, `.flac`, `.ogg`, `.webm` | Whisper.Net 1.5 (`ITranscriptionService`) | Transcript; fails with a reason when the speech-to-text model is not installed |
| `WebProcessor` | `.url`, `.webloc` | `IWebScraperService` | Fetches and extracts the linked page |

**`ChunkingService`** splits text recursively: paragraphs, then sentences, then words, with the
overlap carried from the end of the previous chunk (token counts from `ITokenCounter`). Multi-page
documents with form-feed page breaks are chunked page by page, so chunks keep their page number.
`AdaptiveChunkingService` classifies the content; for code and tables its recommended size
replaces the configured one (and the overlap is kept below it). Defaults come from settings:
`ChunkSize` 512 and `ChunkOverlap` 50; the overlap must be smaller than the size.

**Relevant files:**
- `src/AgentX.Core/Documents/DocumentService.cs`
- `src/AgentX.Core/Documents/ChunkingService.cs`
- `src/AgentX.Core/Documents/Processors/`

### 6.4 Indexing Pipeline

```
DocumentService                                  IndexingService (background loop)
    save DocumentEntity (pending)
    raise DocumentPendingIndexing  ---------->   enqueue id in Channel<long>
                                                 take next id
                                                 set document "processing", create/claim job
                                                 1. vector store ready? else fail with its error
                                                 2. extracted text (from the import, or extract now)
                                                 3. ChunkingService.ChunkDocument(size, overlap)
                                                 4. remove old FTS rows, vectors and chunks
                                                 5. save DocumentChunkEntity rows
                                                 6. embed in batches of Rag:EmbeddingBatchSize
                                                    -> IVectorStore.InsertEmbeddingAsync per chunk
                                                    -> chunk: VectorRowId, IsEmbedded,
                                                       EmbeddingModelVersion, dimensions
                                                 7. KeywordSearchService.IndexDocumentChunksAsync
                                                    (non-fatal)
                                                 8. document "completed", job "completed"
                                                 9. invalidate the search cache,
                                                    raise DocumentIndexed
                                                10. AutoTagService.ApplyAutoTagsAsync (non-fatal)
                                                 on error: document and job "failed" with the
                                                 message, raise DocumentIndexingFailed
```

At startup (`InitializeAsync`) the service initializes the vector store, sets documents left in
`processing` back to `pending` and jobs back to `queued`, enqueues every pending document, and
starts the loop. A shutdown in the middle of a document hands it back to the queue.

Step 2 reuses the text extracted at import while the file is unchanged. When it has to extract
again (a re-index, a changed file, or a document still queued after a restart), it uses only the
built-in processors, so a document whose format only a plugin processor reads fails there with "No
processor found for file type".

**`FileWatcherService`** monitors the enabled watch folders (managed under Settings, Watch Folders)
with `FileSystemWatcher` when Auto-index watch folders is on. At startup it runs a catch-up scan
for files added or changed while the app was closed. It imports files directly into the vault
through `DocumentService`, into the folder's target collection when one is set.

**Relevant files:**
- `src/AgentX.Core/Services/Indexing/IndexingService.cs`
- `src/AgentX.Core/Services/Indexing/FileWatcherService.cs`

### 6.5 Search and RAG Pipeline

#### Semantic Search

`SemanticSearchService.SearchAsync(query)`:
1. Embeds the query text (`IEmbeddingService.EmbedAsync`).
2. Searches the vector store (`IVectorStore.SearchAsync(embedding, topK, minSimilarity)`): the HNSW
   index when it is in use, otherwise a linear cosine scan. Scoped searches (collection, file type,
   date range) restrict the candidates first.
3. Loads the chunks, documents and collection names from EF Core, skipping chunks whose embedding
   version differs from the current one (chunks without a version, from before versioning, are
   kept).
4. Returns `SearchResult` objects with the matched text, an excerpt centered on the query terms, and
   the score.

It also saves and reads the search history.

#### Keyword Search (FTS5)

`KeywordSearchService` owns the FTS5 table `fts_chunks` (columns `content`, `document_id`,
`chunk_id`, `file_name`, `file_path`, `file_type`, `page_number`, `chunk_index`; only `content` is
indexed; tokenizer `porter unicode61`). `InitializeFtsAsync()` creates it at startup;
`IndexDocumentChunksAsync` replaces a document's rows inside one transaction. Queries quote each
term for `MATCH`, apply the file type, collection and date filters inside the SQL, order by `rank`
(BM25), and report scores relative to the best hit. Every raw SQL section holds the database gate.

#### Hybrid Search (Reciprocal Rank Fusion)

`HybridSearchOrchestrator.SearchAsync(query)` routes by `SearchMode`:

- `Semantic` and `Keyword` delegate to one service.
- `Hybrid` runs both in parallel for `TopK x Rag:RetrievalMultiplier` candidates (3, capped at
  `Rag:RetrievalCap`, 500), each filtered by `MinScore` on its own scale, and merges them by RRF.

Results are cached in `SearchCacheService`.

**Reciprocal Rank Fusion formula:**

For each unique chunk appearing in either list, the score adds a contribution from every list it
appears in:

```
RRF_score(chunk) = sum over lists of 1 / (k + rank_i)
```

where `rank_i` is the 1-based rank in list `i` and `k = 60`, the constant from Cormack, Clarke and
Buettcher (2009). The maximum is `2 / (60 + 1)`, about 0.0328 (first in both lists); scores are
divided by it to fall between 0 and 1.

Graceful degradation: if one backend fails, the orchestrator returns the other backend's results.

#### RAG Pipeline

`RagPipeline.AskAsync(question, collectionId, onToken, enableResearchMode)`:

```
1.  Multi-query expansion: MultiQueryGenerator returns the question plus variations (3 requested)
2.  HyDE: a hypothetical answer becomes one more query (Rag:EnableHyde, questions of
    Rag:HydeMinQueryLength = 80 characters or more)
3.  Search every query with HybridSearchOrchestrator in Rag:DefaultSearchMode (Hybrid),
    TopK = the Top-K setting capped at Rag:MaxTopK, MinScore = Rag:DefaultMinScore (0.25);
    merge by chunk, keeping the best score
4.  No results: return the fixed no-results answer, without a model call
5.  Build context chunks, then redact PII (Rag:EnablePiiRedaction) before any stage sends text
    to a model
6.  RagReranker: near-duplicate removal (Jaccard > 0.85), query-term boost (up to 1.5x),
    document diversity (a document above 60% of the chunks has its extra chunks demoted)
7.  LlmReranker, when Rag:EnableLlmReranking and more than 2 chunks
8.  ParentDocumentRetriever expands chunks with their neighbors; PII redaction again
9.  ContextualCompressor keeps the relevant sentences
10. Research Mode: web search results added as web citations, when requested and a
    provider is configured
11. System prompt with numbered sections [1], [2], ... (prompt texts from RagPrompts.json;
    a cacheable static prefix block for Anthropic)
12. Stream the answer: temperature 0.3, max tokens 2048, top-p 0.9; onToken for each token
13. CitationService.ExtractCitations maps [N] in the answer to documents and pages
14. RagEvaluator scores a sample of answers (Rag:EvalSampleRate) in the background
```

Each optional stage that throws is logged and skipped. The response carries the answer, citations,
web citations, the number of context chunks, and search and total latency.

**Relevant files:**
- `src/AgentX.Core/Search/SemanticSearchService.cs`
- `src/AgentX.Core/Search/KeywordSearchService.cs`
- `src/AgentX.Core/Search/HybridSearchOrchestrator.cs`
- `src/AgentX.Core/Search/RagPipeline.cs` and the stage services in the same folder

### 6.6 Intelligence Services

**`SummaryService`**: document summaries, key points and translation (long text is translated in
parts split at paragraph, line or sentence breaks). Used by Quick Actions.

**`HierarchicalSummaryService`**: builds summaries and key points of long documents in levels;
`SummaryService` uses it. **`DocumentSynthesisService`**: the synthesis step of
`ComparisonService`.

**`DuplicateDetectionService`** with **`DuplicateEvidenceService`**: exact duplicates by content
hash and near-duplicates by embedding similarity, with evidence that explains each match.

**`OrganizationSuggestionService`**: suggestions for organizing the vault (collections and tags).

**`ComparisonService`**: compares several documents, each from its own content, at summary or
detailed level (Compare Documents page).

**`KnowledgeGraphService`**: builds the graph behind the Knowledge Graph page:

- **Nodes:** documents (size `14 + 2 x ChunkCount`, clamped to 14..40), collections (32) and tags
  (16).
- **Edges:** document to collection, document to tag, and document to document for shared
  collections or tags, weighted by the number shared.
- **Layout:** 100 iterations of a force-directed layout (repulsion `5000 / d^2`, spring
  `0.01 x (d - 100)`, center gravity `0.01 x position`, damping 0.85) from positions seeded with
  `Random(42)`. The build can be cancelled.
- **Colors:** the page resolves theme brushes per node type (documents `InfoBrush`, collections
  `GraphTagBrush`, tags `WarningBrush`, edges by the node they link to), so the graph follows the
  theme and high contrast. The service's reference hex values (`#58C4BC`, `#B3B3B3`, `#FFB000`)
  are only for consumers without a theme.

**`DigestService`** with **`DigestInsightService`**: generates the Weekly Digest from the database
(new documents and conversations, searches, tokens, storage change, top searches and collections,
file types, conversation highlights) and saves it as a `DigestReportEntity`.

**`ConversationThemeClusterService`** and **`ConversationThemeTrendService`**: cluster
conversations by their summary embeddings and track the clusters' activity per day.

**Relevant files:** `src/AgentX.Core/Services/Intelligence/`

### 6.7 Collections and Tagging

**`CollectionService`**: CRUD for collections, nesting (a collection can move into another; one
level deep), document membership, and the denormalized `DocumentCount`. Deleting a collection keeps
its documents and moves its sub-collections up one level.

**`AutoTagService`**: after a document is indexed, asks the AI for tags
(`IAiService.GenerateTagsAsync`) on a truncated sample of its text, normalizes them (keeping
non-Latin tags), and stores them in `tags` and `document_tags` with `IsAutoGenerated = true` and a
confidence. Failures are logged and never fail indexing.

**Relevant files:**
- `src/AgentX.Core/Services/Collections/CollectionService.cs`
- `src/AgentX.Core/Services/Tagging/AutoTagService.cs`

### 6.8 Settings Service

**`SettingsService`** reads and writes `AppSettings` as camelCase JSON in
`%LocalAppData%\AgentX\settings.json`:

- Secrets are DPAPI-encrypted on disk and plaintext in memory: the OpenAI and Anthropic API keys,
  the web search key, the local API token, the Google and Microsoft OAuth client secrets and the
  scheduled-backup password. Plaintext keys found on load are encrypted on the next write; a value
  that cannot be decrypted (another account or machine) is cleared and a copy of the file is kept.
- A file that cannot be read is never replaced by defaults: it is copied to
  `settings.json.corrupt-<timestamp>` and the session runs on defaults.
- Writes go to a temporary file that replaces the old one.
- Saves are validated (`AppSettingsValidator`); a blocking error throws
  `SettingsValidationException`, which Settings shows. An incomplete setup (a cloud provider
  chosen before its key is pasted) is only logged.

**`AppSettings`** key properties:

| Property | Default | Description |
|---|---|---|
| `ActiveProviderId` | `"local"` | Active provider: `local`, `ollama`, `openai` or `anthropic` |
| `LocalModelFileName` | `"llama-3.2-3b-instruct-q4_k_m.gguf"` | Built-in model file |
| `LocalContextSize` | `8192` | Built-in model context size |
| `LocalGpuLayers` | `0` | 0 automatic, positive a count, negative CPU only |
| `OllamaEndpoint` | `http://localhost:11434` | Ollama server URL |
| `DefaultModel` | `"llama3.2"` | Default Ollama chat model |
| `EmbeddingModel` | `"all-minilm"` | Embedding Model setting (see 6.1) |
| `OpenAiApiKey` | `null` | OpenAI key (optional, encrypted) |
| `OpenAiEndpoint` | `https://api.openai.com/v1/` | OpenAI or compatible endpoint |
| `OpenAiDefaultModel` | `"gpt-4o-mini"` | OpenAI default model |
| `AnthropicApiKey` | `null` | Anthropic key (optional, encrypted) |
| `AnthropicDefaultModel` | `AnthropicProvider.DefaultModelId` (`"claude-sonnet-5"`) | Anthropic default model |
| `Temperature` | `0.7` | Chat temperature |
| `MaxTokens` | `4096` | Maximum answer tokens |
| `ContextWindow` | `8192` | Context window used for assembly |
| `ChunkSize` / `ChunkOverlap` | `512` / `50` | Chunking |
| `TopKResults` | `5` | Top-K for RAG (capped by `Rag:MaxTopK`) |
| `AutoIndexWatchFolders` | `true` | Monitor watch folders |
| `EnableModelRouting` / `ActiveRoutingProfileId` | `false` / `"balanced"` | Model routing |
| `EnableResearchMode` | `false` | Allow Research Mode web search in chat |
| `WebSearchProvider` / `WebSearchApiKey` | `Brave` / `null` | Web search provider and key (or SearXNG URL) |
| `MaxSearchResults` / `SearchCacheTtlMinutes` | `10` / `60` | Web search results and cache time |
| `EnableScreenAwareness` | `false` | Screen capture for Quick Chat |
| `LocalApiEnabled` / `LocalApiToken` | `true` / generated on first start | Local REST API |
| `EnableHnswIndex`, `HnswM`, `HnswEfConstruction`, `HnswFallbackThreshold` | `true`, 16, 200, 10000 | Vector index |
| `HnswEfSearch` | 50 | Minimum HNSW search breadth (ef). A query already searches at least max(`HnswEfConstruction`, 2 x candidates), so only a larger value widens it (better recall, slower); the default changes nothing |
| `OAuth` | client ids empty | OAuth client credentials, refresh buffer (5 min), consent timeout (300 s) |
| `CalendarConnector`, `EmailConnector` | sync off | Connector settings |
| `BackupSchedule` | off, every 168 hours, keep 5 | Scheduled backups |
| `LanguageOverride` | `null` | UI language (`null` follows Windows) |
| `Theme` | `"Dark"` | Theme |
| `StoragePath` | `%LocalAppData%\AgentX` | Folder for the built-in model, the vector store and its index files |

> Agent-X is free and open source: there is no license service, no tiers and no feature gating.

### 6.9 Feature Services

| Area | Services | Notes |
|---|---|---|
| Smart Inbox | `InboxService` | Items from browser clips (through the local API) and from the connectors; accept imports into the vault (a copy under `Inbox\Accepted\`), reject, defer, AI previews; connector items are upserted by `(SourcePluginId, ExternalId)` and imported into the vault as they arrive |
| Connectors | `CalendarPlugin`, `CalendarSyncService`, `GoogleCalendarProvider`, `OutlookCalendarProvider`, `EmailPlugin`, `EmailSyncService`, `GmailProvider`, `OutlookEmailProvider`, `EmailTriageProcessor` | Built-in data connectors, incremental sync with delta tokens, rule-based email triage; started by `BuiltinConnectorLifecycleService` |
| OAuth | `OAuthService`, `OAuthProviderRegistry` | Browser sign-in with PKCE and state for Google and Microsoft; tokens DPAPI-encrypted in `oauth_credentials`; providers registered from the saved client credentials (`ApplyProviderSettings`), also at run time when OAuth App Credentials are saved |
| Plugins | `PluginService` | Install, enable, disable, uninstall; collectible load contexts; plugins receive `IInboxService` only |
| Workflows | `WorkflowService`, `WorkflowEngine` | Steps `AiPrompt`, `DocumentLookup`, `TextTransform`, `ConditionalBranch`, `OutputFormat`; one run at a time; interrupted runs are marked failed at startup |
| Web import | `WebContentFetcher`, `HtmlParser`, `StructuredDataExtractor`, `WebScraperService`, `WebImportService`, `FeedService`, `SitemapParser`, `JsRenderingService` (Playwright) | Pages, feeds and sitemaps; content discovered in remote pages cannot reach private or local addresses (`PrivateNetworkGuard`, `GuardedWebHandler`) |
| Web search | `SettingsAwareWebSearchService` over Brave, Serper and SearXNG, `WebSearchCache` | Reads the provider, key or SearXNG URL and cache time on every search |
| Export | `ExportService`, `ExportTemplateService`, formatters for Markdown, plain text, CSV, HTML, JSON, PDF (QuestPDF), DOCX and PPTX | Conversations, search results, collections; CSV formulas neutralized, HTML escaped |
| Backup | `BackupService` | `.agentxbak` archives (optionally AES-256 encrypted), restore with validation, scheduled backups |
| Collaborative Sync | `SyncService`, `SyncTransport`, `SyncPackageCodec`, `SyncConflictResolver` | Encrypted `.axs` change files in a shared folder; rows matched by natural keys; auto-sync loop |
| Temporal Identity | `TemporalIdentityService`, `VoiceDraftService`, `EngagementTracker` | Beliefs, belief conflicts, insights, engagement time, voice profile; Draft As Me through the active provider |
| Audio | `TranscriptionService`, `WhisperAudioConverter` | Whisper models under `Models\Whisper`, verified downloads, 16 kHz PCM conversion |
| Screen | `ScreenCaptureService` | Screen text for Quick Chat when Screen Awareness is on |
| Annotations | `AnnotationService` | Highlights and notes; each new annotation is also handed to Temporal Identity |
| Feedback | `FeedbackService` | Ratings of answers |
| Workspace | `WorkspaceProfileService` | Saved profiles (stored only; selecting one does not switch models or collections) |
| Analytics | `AnalyticsService` | Read-only queries over local data for the Analytics page; no telemetry |
| Local API | `ApiHostService`, `ApiHostLifecycleService`, `LocalApiSecurity` | See [API_ENDPOINTS.md](../API_ENDPOINTS.md) |
| Privacy | `PrivacyStatusService` | Settings-wide disclosure and per-message recipients for chat |
| Operations | `OperationsOverviewService`, `OperationsActionService`, `OperationsDrillInService` (App) | Snapshot with typed status kinds and tone tokens; actions report success by a flag, never by text |
| Notifications | `NotificationService` (App) | Toasts shown by `NotificationOverlay` |
| Feature flags | `FeatureFlagService` | Overrides stored in `user_settings` under `feature_flag:<name>` |

---

## 7. Data Layer (AgentX.Core/Data)

### 7.1 Entity Framework Core Database Context

`AgentXDbContext` uses SQLite through `Microsoft.EntityFrameworkCore.Sqlite.Core` over
`SQLitePCLRaw.bundle_e_sqlcipher`. `OnConfiguring` points it at
`%LocalAppData%\AgentX\agentx.db` (a fixed path) and registers the serializing concurrency detector
and query compiler. The context is a singleton (see 4.3), and the DI registration passes
`IEncryptedConnectionFactory` so `EnsureKeyApplied()` can apply the database key.

The vector store sets WAL journal mode when it opens its own connection to the same file, and SQLite
keeps that mode in the file.

The full table reference is in [DATABASE_SCHEMA.md](../DATABASE_SCHEMA.md).

#### 7.1.1 Migrations

Schema changes ship as EF Core migrations in `src/AgentX.Core/Data/Migrations/` (eleven, from
`20260417011607_InitialBaseline` to `20260528120000_DropLicensesTable`). `StartupOrchestrator`
awaits `IMigrationRunner.RunAsync()` before anything data-backed starts.

`MigrationRunner` (`src/AgentX.Core/Data/MigrationRunner/MigrationRunner.cs`):

- `RunAsync()` applies pending migrations and returns a `MigrationResult` (database path, whether
  the database was created, migrations applied or adopted, migrations already applied).
- `GetPendingMigrationsAsync()` returns pending migration names.

Before and after `MigrateAsync()` it repairs databases created by older builds:

- **Baseline adoption:** a database with application tables but no `__EFMigrationsHistory` (from
  builds that used `EnsureCreated`) gets the history table; missing baseline tables are created from
  the baseline migration's own operations; `InitialBaseline` and every later migration whose schema
  is already present are stamped as applied. If a baseline table is still missing,
  `BaselineSchemaIncompleteException` stops startup (recovery state).
- **Stamped-baseline repair:** a history that stamps the baseline while baseline tables are missing
  gets them recreated and brought forward through the applied migrations.
- **Reconciliation:** a legacy `AddTemporalIdentity` id is renamed, and `AddSemanticMemoryColumns`
  is stamped when its columns exist.
- **Idempotent repairs on every run:** the operations tables (`plugins`, `sync_logs`, `workflows`,
  `workflow_runs`, `workflow_steps`); the **Temporal Identity columns** the `AddTemporalIdentity`
  migration omitted (without them every Past Self write failed); a compatibility schema for
  `inbox_items` and `belief_conflicts`; branching columns on `conversations`; and four indexes the
  model declares that no migration created.

`AgentXDbContextFactory` is the design-time factory for `dotnet ef`. It targets a throwaway
`agentx.design.db`. To author a migration:

```bash
dotnet tool restore
dotnet ef migrations add <MigrationName> \
  --project src/AgentX.Core \
  --startup-project src/AgentX.Core \
  --output-dir Data/Migrations
```

#### 7.1.2 Database Encryption (C13)

When enabled, `agentx.db` is encrypted at rest with **SQLCipher 4** through
`SQLitePCLRaw.bundle_e_sqlcipher`. Encryption is off by default; the user enables it in Settings
(Database Encryption).

**Key management**

| Mode | Key source | Unlock |
|---|---|---|
| `DpapiWrapped` | 32 random bytes, DPAPI-wrapped for the Windows user, stored as `dpapiWrappedKey` in `encryption.info.json` | Transparent at launch |
| `UserPassphrase` (legacy) | PBKDF2-HMAC-SHA256, 600,000 iterations, 16-byte salt in `encryption.info.json` | Passphrase prompt at launch |

New encryptions use `DpapiWrapped`. The legacy mode is still unlocked for vaults encrypted by older
builds.

**Key delivery to SQLCipher (important)**

Keys are delivered with `PRAGMA key = "x'<hex>'"` right after `SqliteConnection.Open()`, **never**
through `SqliteConnectionStringBuilder.Password`. The two are not equivalent: `Password=` runs the
value through SQLCipher's key derivation, while `x'...'` uses the raw bytes. Mixing them produces
two different keys. All production connections are opened through
`IEncryptedConnectionFactory.OpenKeyed(path)` or keyed with `IEncryptedConnectionFactory.ApplyKey(connection)`.

The design-time `AgentXDbContextFactory` is exempt; its throwaway database never ships.

**Out-of-DB key state**

All encryption state lives in `%LocalAppData%\AgentX\encryption.info.json`, managed by
`IEncryptionStateFile`: `{ version, storageMode, enabledAt, dpapiWrappedKey, saltBase64 }`, with one
of the last two set. Nothing about the key is stored in the encrypted database, which would make
the database impossible to open. `DatabaseKeyService` depends only on `IEncryptionStateFile` and
`IDpapiEncryptionService`, so unlocking never touches the database.

**Turning encryption on**

`DatabaseEncryptionManager` runs the change with the vector store suspended and under the database
gate. `DatabaseEncryptionMigrator.MigrateToEncryptedAsync` attaches an empty encrypted file
(`agentx.db.enc.tmp`) with `KEY "x'<hex>'"`, copies schema and rows with `sqlcipher_export`, clears
the connection pools, moves the plaintext file to `agentx.db.plain.bak`, deletes its WAL and SHM
sidecars, installs the encrypted file and verifies it. The marker file is written last, at the
commit point, so a failure at any step leaves a plaintext database and no marker. At every start,
`RecoverIfNeeded` finishes or undoes an interrupted change.

**Startup unlock sequence** (in `App.InitializeCoreServicesAsync`):
1. Initialize the SQLitePCL provider (`Batteries_V2.Init`).
2. `IDatabaseEncryptionMigrator.RecoverIfNeeded(databasePath)`.
3. If `IEncryptionStateFile.Exists()`: unwrap or derive the key per `storageMode` and set it in
   `IDatabaseKeyProvider` (a wrong passphrase shows a dialog and asks again; Exit closes the app).
4. `db.EnsureKeyApplied()` opens the shared connection and runs `PRAGMA key`.
5. `StartupOrchestrator.RunCriticalStartupAsync()` runs the migration on the keyed connection.

### 7.2 Entity Relationship Model

The model has 37 entity types. The main relationships (foreign key, delete behavior):

```
conversations 1-N messages (cascade); messages 1-1 feedback (cascade, unique)
conversations 1-N conversation_tags (cascade) N-1 tags
conversations 1-N conversation_summary_snapshots (cascade)
conversations 1-1 conversation_summary_states (cascade)
conversations 1-1 conversation_theme_memberships (cascade) N-1 conversation_theme_clusters
conversation_theme_clusters 1-N conversation_theme_daily_metrics (cascade)
conversations 1-N conversations (branches, ParentConversationId, restrict)

documents 1-N document_chunks (cascade)
documents 1-N document_collections (cascade) N-1 collections (cascade)
documents 1-N document_tags (cascade) N-1 tags (cascade)
documents 1-N annotations (cascade)
documents 1-N indexing_jobs (cascade)
collections 1-N collections (children, ParentCollectionId, restrict)
collections 1-N watch_folders (TargetCollectionId, set null)

memories N-1 memories (LinkedMemoryId, restrict)
workflows 1-N workflow_steps (cascade), 1-N workflow_runs (cascade)
temporal_beliefs 1-N belief_conflicts (cascade)
```

Standalone tables: `search_history`, `system_prompts`, `user_settings`, `digest_reports`,
`backups`, `inbox_items`, `workspace_profiles`, `sync_logs`, `plugins`, `oauth_credentials`,
`insight_moments`, `engagement_metrics`, `voice_profiles`. See
[DATABASE_SCHEMA.md](../DATABASE_SCHEMA.md) for every column.

### 7.3 Vector Store Implementation

`IVectorStore` keeps embeddings in the `vec_embeddings` table of the database file named by
`StoragePath` (by default the same `agentx.db`), on its own connection opened through
`IEncryptedConnectionFactory`:

```sql
CREATE TABLE IF NOT EXISTS vec_embeddings (
    chunk_id  INTEGER PRIMARY KEY,
    embedding BLOB NOT NULL,     -- float32 values, 4 bytes per dimension
    magnitude REAL NOT NULL      -- precomputed L2 norm for cosine similarity
);

CREATE INDEX IF NOT EXISTS idx_vec_chunk ON vec_embeddings(chunk_id);
```

`VectorStoreFactory` picks the implementation:

- **`HnswVectorStore`** (when `EnableHnswIndex` is on, the default): SQLite stays the source of
  truth, and an HNSW index (HnswLite, `M` 16, `efConstruction` 200) answers searches when there are
  more than `HnswFallbackThreshold` (10,000) embeddings; below that it scans linearly. A query
  searches at least max(`efConstruction`, 2 x candidates) wide; `HnswEfSearch` (default 50) is a
  minimum on top of that, so only a larger value widens the search (better recall, slower). The index
  serves the current embedding size: its dimension comes from the embedding service, a vector of a
  new size rebuilds it, rows of other sizes stay searchable by the linear scan, and sizes above
  HnswLite's limit of 4096 always use the scan. The index is saved to `hnsw-index.bin`,
  `hnsw-index.json` and `hnsw-stale-ids.json` next to the database, except when the database is
  encrypted: then it is kept in memory only and rebuilt from the database at start.
- **`SqliteVecStore`**: linear cosine-similarity scan in C# over the same table.

**Search algorithm (linear scan):**

```
cosine_similarity(a, b) = dot(a, b) / (|a| x |b|)
```

Precomputed magnitudes avoid recomputing norms; results below `minSimilarity` are dropped before
the top-K are taken.

**Suspend and resume:** `SuspendAsync` waits for running operations, closes the store's connection
and clears its pool (later calls wait); `ResumeAsync(reloadFromDatabase)` reopens it with the
current key. Restore and encryption use this so the database file can be swapped on Windows.

**Design rationale:** no native SQLite extension (such as sqlite-vec) has to be shipped or loaded;
the index is a managed library and can always be rebuilt from the table.

**Relevant files:**
- `src/AgentX.Core/Data/VectorDb/VectorStoreFactory.cs`
- `src/AgentX.Core/Data/VectorDb/HnswVectorStore.cs`
- `src/AgentX.Core/Data/VectorDb/SqliteVecStore.cs`
- `src/AgentX.Core/Data/VectorDb/IVectorStore.cs`

---

## 8. Key Data Flows

### 8.1 Document Import Flow

```
User drops files or clicks Import (KnowledgeVaultPage code-behind)
    |
    v
KnowledgeVaultViewModel -> DocumentService.ImportFilesWithReportAsync(paths, collectionId)
    for each file:
        file exists? extension known?
        SHA-256 hash -> existing document with this hash?
            yes -> reported as a duplicate (not imported)
        processor for the extension (built-in, then plugin)?
            no  -> reported as not imported, with the supported types
        extract text once
            fails -> DocumentEntity saved as "failed" with the reason
            ok    -> DocumentEntity saved as "pending"
        link to the collection (when given)
        raise DocumentPendingIndexing(documentId, extracted text)
    |
    v
Report: imported, duplicates, failures -> the page says what happened
    |
    v (asynchronously, one document at a time)
IndexingService loop: chunk -> embed -> vec_embeddings -> fts_chunks -> "completed"
    -> DocumentIndexed event -> the vault row updates (or DocumentIndexingFailed -> "failed")
```

### 8.2 Chat and Streaming Flow

```
User types a message and presses Enter (ChatPage)
    |
    v
ChatViewModel -> MessagingCoordinator
    standard mode:
        ChatService.SendMessageAsync(conversationId, message, supplementalContext)
            save user message
            load conversation, build options, route the reply (when routing is on)
            memory context + Research Mode web results (for this reply)
            ContextAssemblyService.AssembleAsync (history, overflow summary, recall)
            stream tokens from the routed provider or IAiService
                -> each token appended to the answer bubble on the UI thread
            save the answer (tokens, time, model, citations)
            background: memory extraction
    multi-agent mode (Parallel or Debate):
        MultiAgentOrchestrator.RunAsync(task, roles, strategy)
            Parallel: Researcher, Critic, Synthesizer answer independently
            Debate:   Researcher, Critic, Creative argue for 2 rounds
            -> a synthesis document assembled from their answers (consensus,
               disagreements, contributions), without another model call
        save the user message and the synthesis like a standard reply
    |
    v
ChatViewModel background pass: Temporal Identity processes the prompt (beliefs), learns
the voice profile, and detects insights
```

Stop cancels the generation; a failed or stopped regeneration keeps the previous answer.

### 8.3 RAG (Ask Your Files) Flow

```
User asks a question (AskFilesPage)
    |
    v
AskFilesViewModel -> RagPipeline.AskAsync(question, collectionId, onToken)
    query variations + HyDE document
    hybrid search per query (semantic + FTS5, RRF), merged by chunk
    no results -> fixed no-results answer, no model call
    PII redaction -> heuristic rerank -> LLM rerank -> parent chunks (redacted again)
        -> contextual compression -> optional web results
    numbered system prompt [1] Source: file.pdf, Page 3 ...
    stream the answer (temperature 0.3, max 2048 tokens)
        -> tokens reach the answer bubble on the UI thread
    CitationService.ExtractCitations -> [N] mapped to documents and pages
    |
    v
RagResponse {answer, citations, web citations, chunks used, latencies}
    -> answer with citation badges
```

### 8.4 Search Mode Routing and Hybrid Search

```
SearchQuery {QueryText, TopK, MinScore, Mode, CollectionId?, FileTypeFilter?,
             CreatedAfter?, CreatedBefore?}
    |
    v
HybridSearchOrchestrator.SearchAsync
    cache hit? -> cached results
    Mode = Semantic -> SemanticSearchService (embed, vector store, EF metadata, filters)
    Mode = Keyword  -> KeywordSearchService (FTS5 MATCH with filters in SQL, BM25,
                                            scores relative to the best hit)
    Mode = Hybrid   -> both in parallel, TopK x 3 (max 500) candidates each
                       one failed -> the other's results
                       RRF merge: sum of 1/(60 + rank), top K, normalized by 2/61
    cache the results
    |
    v
IReadOnlyList<SearchResult> -> SearchViewModel (sorted by the chosen order)
    -> history entry saved (query, mode, result count)
```

### 8.5 Knowledge Graph Construction Flow

```
KnowledgeGraphViewModel (page loaded or refreshed; a newer build cancels the older one)
    |
    v
KnowledgeGraphService.BuildGraphAsync(ct)
    load documents (with collections and tags), collections, tags
    nodes: documents (size clamp(14 + 2 x chunks, 14, 40)), collections (32), tags (16)
    edges: document-collection, document-tag, document-document (shared collections or tags,
           weighted by the count)
    degrees per node
    positions: Random(42) across a 1000 x 1000 area
    layout: 100 iterations (repulsion 5000/d^2, spring 0.01 x (d - 100),
            center gravity 0.01 x position, damping 0.85), checking the token
    |
    v
KnowledgeGraphData {Nodes, Edges, counts}
    -> KnowledgeGraphPage draws edges and nodes on a Canvas with theme brushes
```

---

## 9. Navigation Architecture

The rail (`NavView.MenuItems`) has five placards and a footer:

```
INTELLIGENCE   Dashboard, Operations, Weekly Digest, Analytics, Past Self, AI Chat,
               Ask Your Files, Quick Actions, Workflows
KNOWLEDGE      Knowledge Vault, Web Import, Collections, Semantic Search, Knowledge Graph,
               Compare Documents, Annotations
TRIAGE         Smart Inbox
SYSTEM         Model Manager, Hardware Advisor, Backup & Restore, Workspace Profiles,
               Plugin Manager, Collaborative Sync, Calendar, Email
SUPPORT        User Guide, Privacy Policy, Terms of Service
Footer         Settings
```

`PageMap` has 30 entries: the 29 rail pages and `Onboarding`. Navigation starts from:

- the rail (`SelectionChanged`);
- keyboard shortcuts (`ShortcutCatalog` handlers);
- the Command Palette, whose page list is built from the rail itself (`ConfigureCommandPalette`
  walks `MenuItems` and `FooterMenuItems`; `NavRailParityTests` keeps them in step) and whose
  actions are New Conversation, Import Files and Toggle Theme;
- Jump-To (documents, conversations and pages, with the picked item as the navigation parameter);
- the tray menu, status lamps and in-page links.

All of them call `IAppNavigationService.NavigateToPage(tag, parameter)`, which navigates the
`ContentFrame` and updates the rail selection. While onboarding is active, rail selections are
suppressed; navigating away from the wizard by any other route ends onboarding (see 5.2).
`ContentFrame.NavigationFailed` logs the error and keeps the current page.

---

## 10. Dependency Injection Configuration

All registrations are in `App.xaml.cs` `ConfigureServices()`: the services as singletons, two
options bindings, 32 transient view models and 30 transient pages. Grouped:

| Group | Registrations (interface -> implementation; singleton unless noted) |
|---|---|
| Logging | `Serilog.ILogger` -> `Log.Logger` |
| Data and startup | `AgentXDbContext` (factory with `IEncryptedConnectionFactory`), `IMigrationRunner` -> `MigrationRunner`, `IStartupGate` -> `StartupGate`, `IStartupOrchestrator` -> `StartupOrchestrator` |
| Security | `IDpapiEncryptionService`, `IDatabaseKeyProvider`, `IEncryptedConnectionFactory`, `IDatabaseKeyService`, `IDatabaseEncryptionMigrator`, `IDatabaseEncryptionManager`, `IEncryptionStateFile`, `ISecurityStatusService` |
| OAuth | `IOAuthService` (factory: `OAuthService`, then `ApplySettings(settings.OAuth)` and `ApplyProviderSettings(settings.OAuth)`) |
| Core | `ISettingsService`, `IFeatureFlagService`, `IPrivacyStatusService`, `IAppPathService` |
| RAG configuration | `RagConfigurationOptions` bound to `Rag`, `IRagConfiguration`, `RagPromptOptions` bound to `RagPrompts`, `IRagPromptCatalog` |
| Shell input and theme | `IShortcutRegistry`, `ChordStateMachine` (1000 ms), `ShortcutCatalog`, `IThemeService` |
| AI | `IAiService`, `ICostTracker`, `IModelManager`, `IBuiltInModelBootstrap` (factory: models folder, 2-hour download timeout), `IHardwareDetector`, `ITokenCounter`, `EmbeddingService`, `IEmbeddingService` (factory: `CachedEmbeddingService` wrapping `EmbeddingService`), `IContextWindowManager`, `ISemanticContextSelector`, `IConversationCompressionService`, `IContextAssemblyService` |
| Routing and agents | `ITaskTypeDetector`, `IModelRouterService`, `IMultiAgentOrchestrator` |
| Vector store | `IVectorStore` (factory: `VectorStoreFactory.Create`) |
| Chat | `IConversationService`, `IConversationRecallService`, `IConversationSummaryService`, `ISystemPromptService`, `IConversationMemoryService`, `ISemanticMemoryService`, `IChatService`, `IConversationBranchService` |
| Chat coordinators | `IConversationCoordinator`, `IMessagingCoordinator`, `IVoiceCoordinator`, `IBranchingCoordinator` |
| Documents | `IDocumentProcessor` x 8 (`PdfProcessor`, `DocxProcessor`, `TextProcessor`, `MarkdownProcessor`, `CodeFileProcessor`, `ImageProcessor`, `AudioProcessor`, `WebProcessor`), `IDocumentService`, `IChunkingService` (factory with `ITokenCounter` and `IAdaptiveChunkingService`), `IAdaptiveChunkingService` (factory) |
| Indexing | `IIndexingService` (it keeps its own queue and the `indexing_jobs` rows), `IFileWatcherService` |
| Collections and tags | `ICollectionService`, `IAutoTagService` |
| Search and RAG | `ISemanticSearchService`, `IKeywordSearchService`, `ISearchCacheService`, `IHybridSearchOrchestrator`, `ICitationService`, `IRagReranker`, `IMultiQueryGenerator`, `IHydeService`, `ILlmReranker`, `IParentDocumentRetriever`, `IContextualCompressor`, `IRagEvaluator`, `IRagMetrics` (factory, with embedding cache statistics), `IPiiDetector` (factory), `IRagPipeline` |
| Web search | `WebSearchCache`, `IWebSearchService` (factory: `SettingsAwareWebSearchService`) |
| Validation | `IValidator<AppSettings>`, `IValidator<SyncConfiguration>`, `IValidator<PluginManifest>` |
| Intelligence | `IHierarchicalSummaryService`, `IDuplicateEvidenceService`, `IDocumentSynthesisService`, `IDigestInsightService`, `ISummaryService`, `IDuplicateDetectionService`, `IOrganizationSuggestionService`, `IKnowledgeGraphService`, `IDigestService`, `IConversationThemeTrendService`, `IConversationThemeClusterService`, `IComparisonService` |
| Export | `IExportFormatter` x 8 (Markdown, plain text, CSV, HTML, JSON, PDF, DOCX, PPTX), `IExportService`, `IExportTemplateService` |
| Workflows | `IWorkflowService`, `IWorkflowEngine`, `IWorkflowLaunchService` |
| Web | `IWebContentFetcher`, `IHtmlParser`, `IStructuredDataExtractor`, `IWebScraperService`, `IWebImportService`, `IFeedService`, `ISitemapParser`, `IJsRenderingService` |
| Features | `IScreenCaptureService`, `IBackupService`, `IAnnotationService`, `IInboxService`, `IWorkspaceProfileService`, `ITranscriptionService`, `IAnalyticsService`, `IFeedbackService`, `ITemporalIdentityService`, `IVoiceDraftService` |
| Localization | `IPluralRuleProvider` -> `CldrPluralRuleProvider`, `IResourceLoaderAdapter` -> `WinUIResourceLoaderAdapter`, `ILocalizationService` -> `LocalizationService` |
| Plugins and connectors | `IPluginService` -> `PluginService`, `IPluginDocumentProcessorSource` (the same `PluginService` instance), `CalendarPlugin`, `ICalendarService` (factory), `EmailPlugin`, `IEmailService` (factory), `IBuiltinConnectorLifecycleService` |
| Collaborative Sync | `ISyncTransport`, `ISyncPackageCodec`, `ISyncConflictResolver`, `ISyncService` (factory) |
| Local REST API | `IApiHostService`, `IApiHostLifecycleService` |
| Shell services | `INotificationService`, `IOperationsDrillInService`, `IOperationsActionService`, `IOperationsOverviewService`, `SystemTrayService`, `IAppNavigationService`, `IStatusBarService`, `IAnnunciatorService`, `IOnboardingService`, `IChromeService` |
| View models (transient) | The 29 page and support view models, plus factory registrations of `CommandPaletteViewModel`, `JumpToViewModel` and `CheatsheetViewModel` for tests (`MainWindow` builds the real ones with its callbacks) |
| Views (transient) | The 30 pages |

The services that were once registered but never resolved are gone: the ReAct agent, reflection,
reasoning, retry policy and tool registry services, and the collaboration hub, together with their
registrations. Tool calling is not implemented, and providers are not retried automatically.

`IDocumentProcessor` and `IExportFormatter` are registered once per implementation. Consumers take
`IEnumerable<...>`; for processors the registration order decides which one handles an extension
that two of them claim. `ShortcutInputRouter` is built by `MainWindow` because it needs window
callbacks.

---

## 11. Storage Architecture

```
%LocalAppData%\AgentX\
    agentx.db                  SQLite: EF tables, fts_chunks, vec_embeddings (WAL mode)
    agentx.db-wal, -shm        WAL sidecars while the app runs
    settings.json              AppSettings as camelCase JSON; secrets DPAPI-encrypted
    encryption.info.json       Encryption key state (only when encryption is on)
    usage-history.json         Cost tracking history (90 days, max 20,000 records)
    hnsw-index.bin/.json       HNSW index (not written for an encrypted database)
    hnsw-stale-ids.json
    Logs\agentx-yyyyMMdd.log   Serilog, daily files, 7 kept
    Models\                    Built-in GGUF model; Whisper\ggml-*.bin speech models
    Plugins\{id}\              Installed plugins, each with data\ (built-in connectors keep
                               their sync settings and delta tokens there)
    Clips\                     Pages clipped by the browser extension
    Inbox\External\, Accepted\ Connector content files and accepted inbox copies
```

**SQLite database contents:**

```
agentx.db
    37 EF tables (see DATABASE_SCHEMA.md), including
        conversations, messages, documents, document_chunks, collections, tags,
        memories, workflows, inbox_items, plugins, oauth_credentials,
        temporal_beliefs, insight_moments, engagement_metrics, belief_conflicts,
        voice_profiles
    fts_chunks (+ FTS5 shadow tables)   keyword index
    vec_embeddings                      embedding BLOBs, chunk_id + magnitude
    __EFMigrationsHistory               applied migrations
```

**File format notes:**
- `settings.json` uses `System.Text.Json` with camelCase names.
- Embedding BLOBs hold float32 values; the size per vector depends on the embedding model.
- Log lines use `{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}`.

---

## 12. Startup Sequence

```
App()                          InitializeComponent, ConfigureLogging (Serilog),
                               ConfigureExceptionHandling (incl. ProcessExit)
OnLaunched
  1. Build the host            appsettings.json + RagPrompts.json, UseSerilog, ConfigureServices
  2. InitializeLocalizationAsync   saved UI language applied before any shell resource loads;
                                   FormatHelper.LocalizedText set for relative times
  3. new MainWindow()          services resolved, page and nav maps, ShortcutCatalog.SeedDefaults,
                               ShortcutInputRouter, Command Palette from the rail, window size,
                               title bar, backdrop, status strip pollers, queued navigation to
                               the Dashboard and the onboarding check
     ConfigureWindowLifecycleServices; SystemTrayService.ShowMainWindow("startup")
  4. InitializeCoreServicesAsync (async void; each step awaited)
     a. Batteries_V2.Init
     b. IDatabaseEncryptionMigrator.RecoverIfNeeded(agentx.db)
     c. encryption marker? unlock (DPAPI, or passphrase dialog loop) -> IDatabaseKeyProvider
     d. AgentXDbContext.EnsureKeyApplied()
     e. StartupOrchestrator.RunCriticalStartupAsync()
          MigrationRunner.RunAsync()
            failure -> StartupGate failed -> recovery dialog -> exit (nothing else starts)
          StartupGate.SignalDataReady()         (Dashboard data loads now)
          ApiHostLifecycleService.StartAsync()  (when Local API is enabled; token provisioned)
          BuiltinConnectorLifecycleService.InitializeAsync()  (calendar and email)
     f. KeywordSearchService.InitializeFtsAsync()
     g. ISyncService.ResumeAutoSyncAsync()      (first cycle about a minute later)
     h. IWorkflowService.ReconcileInterruptedRunsAsync()
     i. IAiService.InitializeAsync()            (providers from settings)
     j. IFeatureFlagService.InitializeAsync()
     k. IThemeService.InitializeAsync() and ApplyTheme on the UI thread
     l. IPluginService.ActivateEnabledPluginsAsync()   (before indexing and watch folders, so
                                                        imports can use plugin processors)
     m. IBackupService.StartScheduledBackupsAsync()    (only when a schedule is enabled)
     n. IIndexingService.InitializeAsync() on the thread pool (vector store, recovery,
        pending documents, background loop)
     o. IFileWatcherService.InitializeAsync() on the thread pool (when Auto-index watch folders
        is on; catch-up scan)
```

Steps f to o each catch and log their own failure; the app keeps running without that feature.
The status strip polls start on their own delays (5 and 6 seconds).

**Shutdown** (`MainWindow.Closed` or `ProcessExit`, once): stop the built-in connectors (15-second
cap), stop the REST API, stop scheduled backups, deactivate plugins (each capped by the plugin
service), dispose the host (which disposes the singletons, including the indexing loop and the cost
tracker's final save), and flush the log. `ProcessExit` waits at most 20 seconds for this.

---

## 13. Error Handling and Resilience

**Application level:**
- `AppDomain.CurrentDomain.UnhandledException`: logs a fatal error and flushes Serilog.
- `TaskScheduler.UnobservedTaskException`: logs and marks the exception observed.
- `Application.UnhandledException`: logs and sets `e.Handled = true`.
- Navigation failures are logged and the current page stays; the logger is never closed before
  real shutdown.

**Startup:** the migration is the only fail-closed step (recovery dialog, exit). Every other step
logs and continues (section 12).

**Service level:**
- Provider calls catch their errors; a failed health check marks the provider unavailable without
  throwing. `AiService.InitializeAsync` failures leave the app running without that provider.
- `IndexingService` marks a document `failed` with the error in `IndexingError`, raises
  `DocumentIndexingFailed`, and continues with the next document. FTS indexing and auto-tagging
  failures are warnings and do not fail the document.
- `HybridSearchOrchestrator` returns the surviving backend's results when one fails.
- RAG stages that fail are skipped (section 6.5).
- A failed `SaveChanges` discards its pending changes, so later saves are not poisoned.
- Settings are never reset on a read error (section 6.8).

**Cancellation:**
- `ChatService` links the caller's token with its own generation token; Stop cancels the stream
  without affecting the caller.
- `IndexingService` uses a shutdown token cancelled on `Dispose()`; `Dispose` waits up to 5 seconds
  for the loop, and a document interrupted mid-way returns to the queue.
- Connector syncs observe deactivation and stop within 10 seconds.

**Onboarding recovery:** if the wizard cannot be shown, onboarding is skipped and the rail is
restored (`SkipOnboardingAsync` or `EnsureNavPaneVisible()`).

---

## 14. Free and Open Source: No Feature Gating

Agent-X is free and open-source software (MIT License). There are no license tiers, no activation,
no quotas and no feature gates. Every capability is available to every user.

The application used to carry a `LicenseService` and a `LicenseEntity` that gated features (document
limits, advanced models, intelligence services) behind paid tiers. That subsystem is gone:

- The `Services/License/` module (`ILicenseService`, `LicenseService`, `LicenseInfo`, `LicenseTier`)
  and `LicenseException` are deleted.
- The `licenses` table is dropped by the `DropLicensesTable` EF Core migration.
- Every former gate (export formats, intelligence features, document limits) is gone.
- Database encryption, once tier-specific, is available to everyone with DPAPI-wrapped key storage.

---

## 15. Testing Architecture

`tests/AgentX.Tests` is an xUnit project (`net8.0-windows10.0.22621.0`) that references
`AgentX.Core` and compiles selected `AgentX.App` sources (view models, coordinators, services such
as `LocalizationService`) as linked files, so they are tested without WinUI.

```
tests/AgentX.Tests/
    AI/              AiService, providers, routing, context assembly, agents
    CodeQuality/     Source guards (page view model creation, unreachable commands, rail and
                     palette parity, undefined or orphan XAML resource keys, banned palette
                     hues, accessible names, the logger flushed only at shutdown, ...)
    Configuration/   RAG configuration
    Data/            DbContext serialization, MigrationRunner (baseline adoption, repairs,
                     Temporal Identity schema), vector stores
    Documents/       Chunking and processors
    Search/          Semantic, keyword, hybrid (RRF) and RAG pipeline tests
    Services/        Chat, indexing, inbox, connectors, OAuth, backup, security, sync, web,
                     workflows, Temporal Identity, localization, and more
    ViewModels/      Page view models and chat coordinators
    Views/           Page logic that can run without a window
    Helpers/, Mathematics/, Observability/, Validation/, DTOs/
    Stubs/, TestDoubles/, TestFixtures/   Fakes and fixtures
```

Packages: xUnit 2.9.2, FluentAssertions 6.12.2, Moq 4.20.72, coverlet.collector 6.0.2,
Microsoft.NET.Test.Sdk 17.12.0, Xunit.SkippableFact.

Strategies:
- Database tests use temporary SQLite files; the migration tests build the schema through
  `MigrationRunner`, not `EnsureCreated`, and pin that the model has no pending changes.
- Providers are tested against stub HTTP handlers.
- Concurrency tests overlap indexing, EF queries, raw SQL and searches on one context.
- Tests that need Windows-only APIs (DPAPI, user32, Windows paths) run on the Windows CI.

CI (`.github/workflows/build-test.yml`) builds the test project and the WinUI app
(`-p:Platform=x64`, Release), installs Playwright Chromium, runs the tests with coverage, and applies
a coverage gate (`scripts/check-coverage.ps1`). `tests/LocaleAudit.Tests` runs in the locale audit
workflow.

---

## 16. Deployment and Distribution

The application is distributed as a Windows installer built with Inno Setup
(`installer/AgentX-Setup.iss`). One script produces two profiles via the `AgentXOffline`
preprocessor flag:

- **SLIM** (default): no bundled model, small enough for a GitHub Release. The first-run wizard
  offers to download the built-in Llama 3.2 3B model (`BuiltInModelBootstrap`); Ollama and cloud
  API keys work without it.
- **OFFLINE** (`ISCC /DAgentXOffline=1`): bundles the ~1.9 GB model for a fully offline first run.
  The model is installed to `%LocalAppData%\AgentX\Models` with `uninsneveruninstall`, so an
  uninstall leaves it in place. The installer is too large for a GitHub Release asset and is hosted
  elsewhere (`scripts/publish-offline-installer.ps1`).

**Build pipeline:**
1. `dotnet publish src/AgentX.App/AgentX.App.csproj -c Release -r win-x64 --self-contained -o publish/win-x64`
   produces a self-contained, unpackaged app (`WindowsPackageType=None`,
   `WindowsAppSDKSelfContained=true`, `PublishReadyToRun=true`).
2. Inno Setup packages `publish\win-x64\*` into `installer-output\AgentX-Setup-2.2.0-x64.exe`
   (SLIM) or `installer-output\AgentX-Setup-2.2.0-x64-offline.exe` (OFFLINE).

**Installer behavior:**
- Installs to `{autopf}\Agent-X`; `PrivilegesRequired=lowest`, so a per-user install needs no
  elevation (an all-users install can be chosen in the dialog).
- x64 only (`ArchitecturesAllowed=x64compatible`), Windows 10 build 19041 or later
  (`MinVersion=10.0.19041`).
- Start Menu entry; an optional desktop shortcut.
- Closes a running `AgentX.App.exe` before installing.
- Creates `%LocalAppData%\AgentX\Logs` and `Models`; on uninstall removes the log files and keeps
  the database, settings and models.

**Runtime requirements:**
- Windows 10 version 2004 (build 19041) or later, or Windows 11.
- The Windows App SDK runtime is bundled (self-contained).
- Ollama is optional (installed separately by the user).
- GPU offload of the built-in model needs an NVIDIA GPU and the CUDA 12 toolkit.

---

## 17. Performance Characteristics

Measured latencies depend on the hardware and the model, so this section lists the limits and
batch sizes the code uses:

| Area | Value | Source |
|---|---|---|
| Embedding batch | 32 texts | `Rag:EmbeddingBatchSize` |
| Embedding cache | bounded LRU, keyed by model version | `CachedEmbeddingService` |
| HNSW threshold | above 10,000 embeddings; linear scan below | `HnswFallbackThreshold` |
| HNSW parameters | `M` 16, `efConstruction` 200; search breadth at least max(`efConstruction`, 2 x candidates), raised only by an `HnswEfSearch` above that | settings |
| Hybrid candidates | `TopK x 3`, at most 500 per backend | `Rag:RetrievalMultiplier`, `Rag:RetrievalCap` |
| RAG answer | max 2048 tokens, temperature 0.3 | `RagPipeline` |
| Chat context reserve | 1,024 tokens for the answer | `AppConstants.ContextWindowTokenReserve` |
| Ollama connection check | 3-second timeout | `AppConstants.OllamaCheckTimeout` |
| Model download | 2-hour timeout | `AppConstants.ModelDownloadTimeout` |
| Status strip | every 30 seconds | `StatusBarService`, `AnnunciatorService` |
| Indexing idle sweep | every 30 seconds while idle | `IndexingService` |
| Mobile client timeout | 15 seconds | `AgentXApiClient` |

**Scaling notes:**
- All database work shares one gate, so a long database section delays every other caller.
- Indexing throughput is bounded by embedding speed; the queue itself is unbounded.
- The linear scan grows with the number of embeddings; the HNSW index takes over above the
  threshold.
- Rebuilding the HNSW index at start (always, for an encrypted database) is CPU work on the thread
  pool.

---

## 18. Security Model

**Threat model:** Agent-X is a local desktop application. Its network surfaces are the loopback
REST API, the AI providers and web services the user configures, and the connectors.

**Secrets:**
- API keys, the web search key, the local API token, OAuth client secrets and the backup password
  are DPAPI-encrypted in `settings.json` (current Windows user).
- OAuth tokens are DPAPI-encrypted in the database; Microsoft sign-in requests `offline_access` so
  a refresh token is issued; revoking revokes the refresh token.
- The database can be encrypted with SQLCipher (section 7.1.2).
- Plugins do not receive `IOAuthService`, so third-party code cannot read connector tokens.

**Local REST API:**
- Loopback only (`http://localhost:9846/`), bearer token on every route except the extension health
  probe (constant-time comparison), CORS for browser-extension origins only, token revoked at once
  when regenerated.

**Input handling:**
- EF Core queries are parameterized; raw SQL (`KeywordSearchService`, `MigrationRunner`) uses
  parameters, and FTS5 queries quote every term.
- Clipped pages are written with escaped YAML front matter and unique file names.
- HTML exports escape content; CSV exports neutralize formulas.
- Web import: URLs discovered in remote content may reach private or local addresses only when that
  content came from such an address; host checks are pinned against DNS rebinding and enforced where
  connections are opened; cloud metadata endpoints are always refused.

**Privacy disclosure:**
- `PrivacyStatusService` reports every enabled feature that sends data off the computer, and chat
  shows per message where it goes (section 6.9).
- PII is redacted from RAG context before any model sees it (when `Rag:EnablePiiRedaction` is on).

**File system:**
- Imports store the original path; source files are not copied or modified.
- Logs record operations, file names and (shortened) search queries; they stay in
  `%LocalAppData%\AgentX\Logs`.

**Plugins:**
- Plugins run in-process with the user's rights; manifest permissions are informational. Installing
  a plugin is like running any other program.

**Network:**
- Ollama traffic goes to the configured endpoint, which can be another computer (disclosed).
- OpenAI, Anthropic, web search and connector traffic uses HTTPS.

---

## 19. Glossary

| Term | Definition |
|---|---|
| **AiService** | The `AgentX.Core` singleton that owns the AI providers, the active provider and model, and high-level operations (chat, summarize, tag). |
| **all-minilm** | The default value of the Embedding Model setting: an Ollama embedding model, used when the built-in model is not installed. |
| **BM25** | The ranking function SQLite FTS5 uses for keyword search. |
| **Built-in model** | The GGUF model run in-process by `LocalLlmProvider` (LLamaSharp), Llama 3.2 3B by default. |
| **Chunk** | A piece of a document's text (default 512 tokens, 50-token overlap) stored as a `DocumentChunkEntity` and embedded as a vector. |
| **CommunityToolkit.Mvvm** | Microsoft's MVVM library with source generators for `[ObservableProperty]` and `[RelayCommand]`. |
| **Cosine similarity** | `dot(a,b) / (|a| x |b|)`, between -1 and 1; the semantic similarity measure. |
| **Database gate** | The single lock that serializes all work on the shared `AgentXDbContext` (`EnterDatabaseGate()`). |
| **DPAPI** | Windows Data Protection API: encryption tied to the Windows user, used for settings secrets, OAuth tokens and the wrapped database key. |
| **EF Core** | Entity Framework Core, the ORM, used with the SQLite provider. |
| **FTS5** | SQLite's full-text search engine; the `fts_chunks` table. |
| **HnswVectorStore** | The `IVectorStore` with an HNSW index over the SQLite table, used above the fallback threshold. |
| **IAiProvider** | The provider contract implemented by `LocalLlmProvider`, `OllamaProvider`, `OpenAiProvider` and `AnthropicProvider`. |
| **Indexing pipeline** | The background loop that chunks, embeds, stores vectors, FTS-indexes and auto-tags a `pending` document. |
| **Knowledge Vault** | The user-facing name for the document library. |
| **Mica** | A Windows 11 backdrop material. |
| **MVVM** | Model-View-ViewModel, the presentation pattern. |
| **OllamaSharp** | The .NET client library for the Ollama API. |
| **RAG** | Retrieval-Augmented Generation: retrieving relevant chunks and giving them to the model as numbered context for a cited answer. |
| **RRF** | Reciprocal Rank Fusion: merges ranked lists by summing `1/(k + rank)`. |
| **Serilog** | The structured logging library. |
| **SqliteVecStore** | The linear-scan `IVectorStore` over embedding BLOBs in SQLite. |
| **SSE** | Server-Sent Events, the streaming format of the OpenAI and Anthropic APIs. |
| **Temperature** | Sampling randomness: 0.7 by default for chat, 0.3 for RAG. |
| **VectorRowId** | The row id the vector store returned for a chunk's embedding. |
| **WAL** | Write-Ahead Logging, SQLite's journal mode used by the database. |
| **Watch folder** | A folder monitored by `FileWatcherService`; new and changed files are imported into the vault. |
| **WinUI 3** | The Windows App SDK UI framework. |

---

## 20. Localization and Keyboard Power Mode

### Localization (A1)

Agent-X ships six UI languages: **English (en-US, canonical)**, **German (de)**, **Spanish (es)**,
**French (fr)**, **Japanese (ja)** and **Simplified Chinese (zh-CN)**, in
`src/AgentX.App/Strings/<locale>/Resources.resw`. `scripts/translations/<locale>.json` mirror the
translations.

**Two ways strings are referenced:**

1. **XAML `x:Uid`**: WinUI resolves `<Uid>.<Property>` keys (for example `Settings_Connections.Text`).
2. **C# `ILocalizationService.GetString(key)`** (and `GetString(key, args)`, `FormatPlural`) for text
   built in code: navigation labels, the status strip, dialogs, notifications, view model
   messages. Keys have no dot.

Both go through MRT Core. `LocalizationService` (App) reads resources through
`IResourceLoaderAdapter` (`WinUIResourceLoaderAdapter` in the app, a fake in tests).

**Language choice.** Settings offers the Windows display language or one of the six languages
(`AppSettings.LanguageOverride`). `App.OnLaunched` applies it before the main window exists, so the
shell's `x:Uid` strings load in that language; a change takes full effect after a restart. Core
formats relative times through `FormatHelper.LocalizedText`, which startup points at the localization
service.

**Fallback behavior.** A key missing in the current language returns the key itself, a visible
failure that the audit gate is meant to prevent.

**Pluralization.** `FormatPlural(baseKey, count, args)` asks `CldrPluralRuleProvider` for the CLDR
category and reads `<baseKey>_<category>`, falling back to `<baseKey>_other`; if that is missing too
it throws `KeyNotFoundException`, so the defect shows in tests.

**RTL readiness.** `RtlDetector` (Core) tells whether a culture is right-to-left, and
`FlowDirectionHelper` (App) turns that into a `FlowDirection`; `MainWindow` sets
`RootGrid.FlowDirection` from it. No right-to-left language ships today.

**Coverage enforcement.** `tools/LocaleAudit` scans XAML for `x:Uid`, C# for literal
`GetString("...")` calls and the resw files, and reports covered, missing and orphan keys per
locale. `.github/workflows/locale-audit.yml` runs it with `--fail-below 98` on changes to XAML, C#,
resw files, the translation mirrors or the tool, runs `tests/LocaleAudit.Tests`, and posts a
coverage table on pull requests.

| Layer | Path |
|---|---|
| Core detector | `src/AgentX.Core/Services/Localization/RtlDetector.cs` |
| Core pluralization | `src/AgentX.Core/Services/Localization/CldrPluralRuleProvider.cs` |
| Service interface | `src/AgentX.Core/Services/Localization/ILocalizationService.cs` |
| Service implementation | `src/AgentX.App/Services/LocalizationService.cs` |
| Resource adapter | `src/AgentX.App/Services/WinUIResourceLoaderAdapter.cs` |
| WinUI helper | `src/AgentX.App/Helpers/FlowDirectionHelper.cs` |
| Audit tool | `tools/LocaleAudit/` |
| Tests | `tests/LocaleAudit.Tests/`, `tests/AgentX.Tests/Services/Localization/` |
| CI gate | `.github/workflows/locale-audit.yml` |

### Keyboard Power Mode (A2)

`IShortcutRegistry` owns every shortcut as a `ShortcutDescriptor`, scoped `Global` or to a page.
Three built-in surfaces use it:

- **Command Palette** (`Ctrl+K` or `Ctrl+Shift+P`): the rail's pages and the palette actions, with
  fuzzy matching.
- **Jump-To** (`Ctrl+P`): fuzzy search over documents, conversations and pages.
- **Cheatsheet** (`F1` or `Ctrl+Shift+?`): the shortcuts available in the current scope, grouped by
  category.

`ShortcutInputRouter` handles `RootGrid.PreviewKeyDown`. `ChordStateMachine` (1-second window)
supports multi-key chords, though every shipped shortcut is a single chord. `ShortcutCatalog` seeds
the global shortcuts (section 5.7); pages register theirs with `RegisterShortcuts(...)` in
`OnNavigatedTo` and dispose the token in `OnNavigatedFrom`.

---

*This document reflects the Agent-X codebase at version 2.2.0 (2026-09-27). Paths are relative to
the repository root.*
