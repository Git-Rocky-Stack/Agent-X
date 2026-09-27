# Agent-X Developer Guide

Version 2.2.0 | Last updated: September 2026

---

## Table of Contents

1. [Overview](#1-overview)
2. [Prerequisites and Setup](#2-prerequisites-and-setup)
3. [Project Structure](#3-project-structure)
4. [Architecture Patterns and Conventions](#4-architecture-patterns-and-conventions)
5. [Adding New Features](#5-adding-new-features)
6. [Database and Data Access](#6-database-and-data-access)
7. [AI Integration](#7-ai-integration)
8. [Search and RAG](#8-search-and-rag)
9. [Data Connectors (Calendar and Email)](#9-data-connectors-calendar-and-email)
10. [Testing](#10-testing)
11. [Build, Publish, and Packaging](#11-build-publish-and-packaging)
12. [Troubleshooting](#12-troubleshooting)
13. [Code Style Guidelines](#13-code-style-guidelines)

---

## 1. Overview

Agent-X is a local-first AI document intelligence app for Windows. Users import their documents,
the app extracts, chunks and embeds them, and they can then chat with answers grounded in those
documents, search them by meaning or by keyword, and ask questions of the whole library. The
detailed design is in [ARCHITECTURE.md](ARCHITECTURE.md) and the database in
[DATABASE_SCHEMA.md](../DATABASE_SCHEMA.md); this guide covers how to build, change and test the
code.

**What local first means in the code:**

- The default AI provider is a built-in model: Llama 3.2 3B (a GGUF file) run in-process by
  LLamaSharp. The OFFLINE installer bundles the file; in a SLIM install the first-run wizard offers
  to download it. Ollama (on this computer or another), OpenAI and Anthropic are optional
  providers.
- Documents, chunks, embeddings, conversations and settings stay under `%LocalAppData%\AgentX`: one
  SQLite database (SQLCipher, optionally encrypted), `settings.json` and a few side files.
- When prompts can leave the computer (a cloud provider, an Ollama server on another machine, model
  routing to a cloud provider, or Research Mode web search), `PrivacyStatusService` says so. The
  `LOCAL`/`NET` lamp, the Dashboard and the privacy note in an empty chat all use its evaluation.
- The app is an unpackaged, self-contained Windows executable: the .NET runtime and the Windows App
  SDK ship with it.

**Tech stack:**

| Layer | Technology |
|---|---|
| UI | WinUI 3 (Windows App SDK 1.6.250108002), unpackaged, self-contained |
| Language and runtime | C# 12, .NET 8 (`net8.0-windows10.0.22621.0`, minimum Windows 10.0.19041) |
| MVVM | CommunityToolkit.Mvvm 8.2.2 |
| Host and DI | Microsoft.Extensions.Hosting 8.0.1 |
| Database | EF Core 8.0.11 (`Sqlite.Core`) on SQLCipher (`SQLitePCLRaw.bundle_e_sqlcipher` 2.1.7) |
| Vector search | `vec_embeddings` table with an HNSW index (HnswLite 1.0.6) or a linear scan |
| Built-in model | LLamaSharp 0.19.0 (CPU and CUDA 12 backends) |
| Ollama | OllamaSharp 4.0.6 |
| Cloud providers | `HttpClient` with server-sent events (OpenAI, Anthropic) |
| Document processing | PDFsharp 6.1.1, DocumentFormat.OpenXml 3.2.0, Markdig 0.37.0, Windows OCR |
| Audio | Whisper.Net 1.5.0, NAudio 2.2.1 |
| Web | HtmlAgilityPack 1.11.67, Microsoft.Playwright 1.59.0 |
| Export | QuestPDF 2024.12.2 (PDF), DocumentFormat.OpenXml (DOCX, PPTX) |
| Tray | H.NotifyIcon.WinUI 2.1.3 |
| Logging | Serilog 4.0.2 (file and debug sinks) |
| Tests | xUnit 2.9.2, Moq 4.20.72, FluentAssertions 6.12.2 |
| Installer | Inno Setup 6 |

---

## 2. Prerequisites and Setup

### 2.1 Required Tooling

| Tool | Version | Notes |
|---|---|---|
| Windows | 10 version 2004 (build 19041) or later | The app's minimum platform and the installer's `MinVersion` |
| .NET SDK | 8.0.421 or a later 8.0 feature band | Pinned in `global.json` (`rollForward: latestFeature`) |
| Visual Studio 2022 | With the .NET desktop and WinUI (Windows application development) workloads | Optional: the `dotnet` CLI is enough |
| Windows App SDK | 1.6 | Restored from NuGet; nothing to install |
| Ollama | Any current release | Optional, only to develop against the Ollama provider |
| Node 20 | | Only for `browser-extension/` |
| MAUI Android workload | `dotnet workload install maui-android` | Only for `src/AgentX.Mobile` |
| Inno Setup 6 | 6.x | Only to build installers |

**Entity Framework Core CLI.** Migrations use a repo-pinned local tool: `dotnet-ef` 8.0.11 in
`.config/dotnet-tools.json`. Restore it once after cloning; no global install is needed:

```powershell
dotnet tool restore
```

See [6.3](#63-schema-migrations) for the migration workflow.

### 2.2 Getting Started

```powershell
git clone <repository-url>
cd Agent-X
dotnet tool restore
dotnet build -p:Platform=x64
```

**The platform argument is required.** A bare `dotnet build` of the solution fails (the runtime
identifier resolves to `win-anycpu`). The app project itself defaults to `x64` when no platform is
given, so it can also be run on its own:

```powershell
dotnet run --project src/AgentX.App/AgentX.App.csproj -p:Platform=x64
```

In Visual Studio, open `AgentX.sln`, select the `x64` platform and press F5.

Nothing else is required to run the app. To work with Ollama as well, install it from
[ollama.com](https://ollama.com), pull a chat model (the Ollama default model setting is
`llama3.2`), and for Ollama embeddings `all-minilm`:

```powershell
ollama pull llama3.2
ollama pull all-minilm
```

### 2.3 First Run Configuration

On first run (`OnboardingCompleted` is `false`) the onboarding wizard hides the navigation rail and
walks through five steps: a welcome, the Ollama connection (Test, or Skip), model selection, the
built-in model and cloud API keys, and a summary. The built-in model step checks for the model file
and offers to download it (`OnboardingViewModel.DownloadLocalModelAsync`, which calls
`IBuiltInModelBootstrap.EnsureInstalledAsync`). Finishing sets the active provider to the first
that is usable: the built-in model if its file is installed, otherwise Ollama if the connection test
passed, otherwise OpenAI or Anthropic if a key was entered.

To skip the wizard during development, edit the settings file (camelCase JSON):

```
%LocalAppData%\AgentX\settings.json
```

and set `"onboardingCompleted": true`. Delete the file, or set the value to `false`, to see the
wizard again.

### 2.4 Application Data Locations

| Artifact | Path |
|---|---|
| Settings | `%LocalAppData%\AgentX\settings.json` |
| Database | `%LocalAppData%\AgentX\agentx.db` (fixed path; see [6.1](#61-schema-overview)) |
| Encryption state | `%LocalAppData%\AgentX\encryption.info.json` (only when encryption is on) |
| Cost history | `%LocalAppData%\AgentX\usage-history.json` |
| Built-in model | `%LocalAppData%\AgentX\Models\llama-3.2-3b-instruct-q4_k_m.gguf` |
| Speech-to-text model | `%LocalAppData%\AgentX\Models\Whisper\ggml-base.bin` |
| Plugins | `%LocalAppData%\AgentX\Plugins\<PluginId>\` |
| Logs | `%LocalAppData%\AgentX\Logs\agentx-yyyyMMdd.log` |

The full list is in [DATABASE_SCHEMA.md, Data Outside the Database](../DATABASE_SCHEMA.md#data-outside-the-database).

---

## 3. Project Structure

```
Agent-X/
  AgentX.sln                  AgentX.App, AgentX.Core, AgentX.Tests, LocaleAudit.Tool,
                              LocaleAudit.Tests
  Directory.Build.props       C# 12, nullable, implicit usings, version 2.2.0
  global.json                 .NET SDK pin
  src/
    AgentX.App/               WinUI 3 application (presentation layer)
    AgentX.Core/              Class library: services, data, AI, search
    AgentX.Mobile/            .NET MAUI Android companion (not in AgentX.sln)
  tests/
    AgentX.Tests/             xUnit tests for Core, plus App sources linked into the project
    LocaleAudit.Tests/        Tests for the locale audit tool and the six locales
  tools/LocaleAudit/          Localization coverage tool
  plugins/sample-plugin/      Sample document processor plugin
  browser-extension/          Browser extension that clips pages into the Smart Inbox
  installer/AgentX-Setup.iss  Inno Setup script (SLIM and OFFLINE profiles)
  scripts/                    Release, model download, coverage and localization scripts
  models/                     The GGUF file the OFFLINE installer bundles (downloaded, not committed)
  docs/                       Documentation
```

`AgentX.App` references `AgentX.Core`; `AgentX.Core` never references the app.

### 3.1 AgentX.App: Presentation Layer

```
src/AgentX.App/
  App.xaml, App.xaml.cs         Entry point: Serilog, DI host, startup and shutdown
  MainWindow.xaml(.cs)          Shell: navigation rail, PageMap, command palette, shortcuts,
                                title bar, backdrop
  MainWindow.JumpTo.cs          Jump-To dialog
  MainWindow.StatusTrayOnboarding.cs   Instrument strip lamps, tray, onboarding
  appsettings.json              "Rag" options
  RagPrompts.json               RAG prompt texts (reloaded when the file changes)
  Views/                        30 pages; Dialogs/ (Jump-To, Cheatsheet), ExportDialog,
                                QuickChatWindow, BranchCompareWindow
  ViewModels/                   Page and support view models; ChatMessageItem,
                                ConversationListItem, SystemPromptItem; Coordinators/ (chat);
                                Sync/ (sync history rows)
  Controls/                     CommandPalette, Faceplate, LampTile, MarkdownMessageControl,
                                NotificationOverlay, OAuthAppCredentialsPanel, SegmentMeter
  Converters/                   11 IValueConverter implementations
  Helpers/                      PageViewModelFactory, MarkdownParser, SyntaxHighlighter,
                                ShortcutRegistrationExtensions, ThemeResources, ContentColumn,
                                WindowPlacement, FlowDirectionHelper, ...
  Services/                     Shell services: AppNavigationService, OnboardingService,
                                StatusBarService, AnnunciatorService, SystemTrayService,
                                ChromeService, ThemeService, LocalizationService,
                                NotificationService, StartupOrchestrator, StartupGate,
                                ApiHostLifecycleService, BuiltinConnectorLifecycleService,
                                Operations page services, ShortcutCatalog, ShortcutInputRouter,
                                ProviderStatusText, WorkflowLaunchService
  Models/, Selectors/           User Guide section model and template selector
  Styles/                       Resource dictionaries: Colors, Hardware, Typography, Controls,
                                Navigation, Chat, Documents, UserGuideSections*
  Themes/Generic.xaml           Faceplate control template
  Strings/<locale>/Resources.resw   UI strings: en-US, de, es, fr, ja, zh-CN
```

### 3.2 AgentX.Core: Business Logic

`AgentX.Core` targets `net8.0-windows10.0.22621.0` and has no WinUI dependency. Services expose
interfaces, which keeps them testable in isolation.

```
src/AgentX.Core/
  AI/                   IAiService/AiService, IAiProvider, EmbeddingService,
                        EmbeddingTargetResolver, CachedEmbeddingService, ModelManager,
                        BuiltInModelBootstrap, BuiltInModelCatalog, HardwareDetector,
                        TokenCounter, TokenEstimator, ContextWindowManager, ProviderChoices
    Providers/          LocalLlmProvider, OllamaProvider, OpenAiProvider, AnthropicProvider
    Context/            ContextAssemblyService, SemanticContextSelector,
                        ConversationCompressionService
    Routing/            TaskTypeDetector, ModelRouterService, routing profiles
    Agents/             MultiAgentOrchestrator
    Models/             AiModel, ChatMessage, ChatOptions, CostTracker, ...
  Configuration/        RagConfiguration ("Rag" options), RAG prompt catalog
  Data/                 AgentXDbContext, AgentXDbContextFactory (design time),
                        SerializingConcurrencyDetector, SerializingQueryCompiler,
                        EncryptedConnectionFactory
    Entities/           The 37 EF entities
    Migrations/         11 migrations and the model snapshot
    MigrationRunner/    MigrationRunner, MigrationResult, BaselineSchemaIncompleteException
    VectorDb/           IVectorStore, VectorStoreFactory, HnswVectorStore, SqliteVecStore
  Documents/            DocumentService, ChunkingService, AdaptiveChunkingService,
                        DocumentExtractionException
    Models/             ProcessedDocument, DocumentChunk, DocumentMetadata, SupportedFileTypes
    Processors/         Pdf, Docx, Text, Markdown, CodeFile, Image, Audio, Web processors
  Search/               SemanticSearchService, KeywordSearchService, HybridSearchOrchestrator,
                        RagPipeline and its stages (MultiQueryGenerator, HydeService,
                        RagReranker, LlmReranker, ParentDocumentRetriever, ContextualCompressor,
                        CitationService, RagEvaluator)
  Services/             Analytics, Annotations, Api (local REST API), Audio, Backup, Chat,
                        Collections, Export, FeatureFlags, Feedback, Inbox, Indexing,
                        Intelligence, Localization, OAuth, Plugins (with Calendar and Email),
                        Privacy, Screen, Search (web search), Security, Settings, Shortcuts,
                        Sync, Tagging, TemporalIdentity, Web, Workflows, Workspace
  Observability/        RagMetrics, PiiDetector
  Validation/           Settings, sync configuration and plugin manifest validators
  Helpers/              PathHelper, HashHelper, FormatHelper, FileTypeHelper
```

[ARCHITECTURE.md](ARCHITECTURE.md) describes each area in detail.

### 3.3 AgentX.Tests

```
tests/AgentX.Tests/
  AgentX.Tests.csproj   References AgentX.Core; compiles 83 AgentX.App source files as links
  AI/ Data/ Documents/ Search/ Services/ ViewModels/ Views/ ...   Tests by area
  CodeQuality/          Source and XAML guard tests (see 10.3)
  Helpers/              TestDbContextFactory (in-memory SQLite context) and other helpers
  TestFixtures/         SqlCipherFixture
  TestDoubles/, Stubs/  Stand-ins for WinUI-only types
```

---

## 4. Architecture Patterns and Conventions

### 4.1 Dependency Injection

The container is configured in `App.xaml.cs`, `ConfigureServices()`, with
`Microsoft.Extensions.Hosting`.

**Lifetime rules:**

| Type | Lifetime | Reason |
|---|---|---|
| Services (Core and shell) | Singleton | They hold state, connections, caches or timers, or are expensive to create |
| View models | Transient (32) | A new instance each time a page is built |
| Pages | Transient (30) | Registered for consistency; navigation creates pages with `Frame.Navigate(pageType, parameter)` |
| `AgentXDbContext` | Singleton | One context shared by the UI and all background work (see below) |

**The shared `AgentXDbContext`:**

EF Core does not support two operations at once on one `DbContext`, and SQLite's locking (WAL
included) does not change that: WAL lets separate connections read while one writes; it does not
make one context safe to use from several threads. Agent-X still registers a single context, and
reaches it from the UI thread and from background work at the same time (the indexing loop, the
local REST API, status polling, scheduled backup and sync, connector timers). So the context
serializes itself behind one gate:

- A custom `IConcurrencyDetector` (`SerializingConcurrencyDetector`) makes an overlapping operation
  wait for the one in flight instead of throwing "A second operation was started on this context
  instance". `SerializingQueryCompiler` holds the same gate across whole query executions,
  including enumerator disposal.
- `SaveChanges` and `SaveChangesAsync` run under the gate. When a save fails, the pending changes
  are discarded (added entities are detached, modified and deleted ones reverted), so one rejected
  change is not replayed, and failed again, by every later unrelated save. The change tracker is
  shared, so this also drops changes another caller staged and had not saved yet: stage and save
  in one step.
- Raw ADO.NET work on `Database.GetDbConnection()` is invisible to EF and must join the gate for its
  whole duration, including any transaction it opens: `using (db.EnterDatabaseGate()) { ... }`. The
  gate is re-entrant within one async flow, so do not fan out parallel database work while holding
  it.

Every caller waits on the same gate, the UI thread included, so a long database section stalls
everything else. Keep database work in background services short: read what you need, leave the
gate, and do the slow part (embedding, model calls, file I/O) outside it.

**Service resolution:**

Classes receive services through constructor injection. Code-behind that cannot, uses the static
accessor:

```csharp
var navigation = App.GetService<IAppNavigationService>();
```

`App.GetService<T>()` resolves from the root provider (`App.Host.Services`), which keeps every
transient `IDisposable` it creates until shutdown. The `Frame` caches at most ten pages, so a page
that resolved an `IDisposable` view model that way would leak the old view model (and, through its
event subscriptions, the old page) each time it is rebuilt. Pages whose view model is
`IDisposable` therefore use `PageViewModelFactory.Create<T>()` (`Helpers/PageViewModelFactory.cs`),
which builds the same object with `ActivatorUtilities` without the container tracking it; 13 pages
do. `PagesCreateDisposableViewModelsUntrackedTests` fails on a page that resolves an `IDisposable`
view model through the root provider.

**Registration pattern:**

```csharp
// In App.xaml.cs ConfigureServices():

// Singleton service
services.AddSingleton<IMyService, MyService>();

// Singleton built by a factory (when constructor selection is not enough)
services.AddSingleton<IChunkingService>(sp => new ChunkingService(
    sp.GetRequiredService<ITokenCounter>(),
    sp.GetService<IAdaptiveChunkingService>(),
    sp.GetRequiredService<Serilog.ILogger>().ForContext<ChunkingService>()));

// Transient view model and page
services.AddTransient<ViewModels.MyViewModel>();
services.AddTransient<Views.MyPage>();

// Several implementations of one interface, consumed as IEnumerable<IDocumentProcessor>
services.AddSingleton<IDocumentProcessor, PdfProcessor>();
services.AddSingleton<IDocumentProcessor, DocxProcessor>();
```

Services take the Serilog `ILogger` (registered as `Log.Logger`) and usually call
`ForContext<T>()` on it.

### 4.2 MVVM Pattern

View models use the `CommunityToolkit.Mvvm` source generators:

**`[ObservableProperty]`** on a private field generates the public property with change
notification, plus optional `partial void On<Name>Changed(T value)` and `On<Name>Changing` hooks:

```csharp
[ObservableProperty]
private string _userInput = string.Empty;

// Optional hook for side effects:
partial void OnUserInputChanged(string value)
{
    SendMessageCommand.NotifyCanExecuteChanged();
}
```

**`[RelayCommand]`** on a method generates an `IRelayCommand` (or `IAsyncRelayCommand` for an async
method) named after it:

```csharp
[RelayCommand]
private void CopyMessage(string? content) { ... }

[RelayCommand]
private async Task NewConversationAsync() { ... }       // NewConversationCommand

[RelayCommand(CanExecute = nameof(CanSend))]
private async Task SendMessageAsync() { ... }
```

Call `SendMessageCommand.NotifyCanExecuteChanged()` when the `CanExecute` condition changes.

**Page and view model wiring.** A page exposes its view model as a `ViewModel` property, creates it
before `InitializeComponent()` so compiled bindings see it, and starts loading when the page loads:

```csharp
public sealed partial class DigestPage : Page
{
    public DigestViewModel ViewModel { get; }

    public DigestPage()
    {
        ViewModel = App.GetService<DigestViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }
}
```

A page with an `IDisposable` view model uses the factory instead:

```csharp
ViewModel = PageViewModelFactory.Create<OperationsViewModel>();
```

XAML binds with `x:Bind` against that property, for example
`Text="{x:Bind ViewModel.Title, Mode=OneWay}"`; data templates may use `{Binding}`. The load method
(`InitializeAsync`, `LoadAsync` or similar) catches its own errors: a page must render even when a
service is unavailable. The window and the rail are usable before the startup migration has
finished (see [4.5](#45-startup-sequence)), so a view model that reads the database as soon as its
page appears should await `IStartupGate.WaitForDataReadyAsync()` first, as `DashboardViewModel`
does.

### 4.3 Navigation System

`MainWindow` owns two maps and hands them to `IAppNavigationService` (`AppNavigationService`):

```csharp
// Page tag -> page type (30 entries, including Onboarding, which has no rail item)
private static readonly Dictionary<string, Type> PageMap = new()
{
    ["Dashboard"] = typeof(Views.DashboardPage),
    ["Operations"] = typeof(Views.OperationsPage),
    // ...
};

// Page tag -> rail item (29 entries), used to move the rail's selection indicator
private Dictionary<string, NavigationViewItem> BuildNavItemMap() => new()
{
    ["Dashboard"] = NavDashboard,
    ["Operations"] = NavOperations,
    // ...
};
```

Every route goes through `IAppNavigationService.NavigateToPage(string pageKey, object? parameter = null)`:
a rail click (`NavigationView.SelectionChanged` reads the item's `Tag`), a keyboard shortcut, the
command palette, Jump-To, the tray, a status lamp or a link inside a page. The optional parameter
carries what the user picked (a conversation from Jump-To, a query for Search, an intent such as
`NavigationIntents.NewConversation`) to the page's `OnNavigatedTo`. `ExecuteAction(actionId)` runs
non-navigation actions (`NewConversation`, `ImportFiles`, `ToggleTheme`).

While the onboarding wizard owns the shell, `OnboardingService` sets
`INavigationGate.SuppressNavigation`, so rail selections are ignored; it clears the flag when
onboarding ends. Leaving the wizard any other way (a shortcut, the palette, Jump-To, the tray, a
lamp) ends onboarding as skipped.

### 4.4 Keyboard Shortcuts

Global shortcuts are seeded by `ShortcutCatalog.SeedDefaults()` (`Services/ShortcutCatalog.cs`)
into the singleton `IShortcutRegistry`, with labels and categories from `ILocalizationService`.
`ShortcutInputRouter` hooks `PreviewKeyDown` on the window's root, maps the key and modifiers to a
`KeyChord`, handles the palette (`Ctrl+K`, `Ctrl+Shift+P`), Jump-To (`Ctrl+P`) and the cheatsheet
(`F1`, `Ctrl+Shift+?`) itself, and otherwise asks the registry for a descriptor, preferring one
scoped to the current page (the page's class name) over a global one. Multi-key chords go through
`ChordStateMachine` (1,000 ms between keys).

Each descriptor is a `ShortcutDescriptor` record:

```csharp
public sealed record ShortcutDescriptor(
    string Id,                              // stable id, e.g. "nav.search"
    string Label,                           // localized label
    ShortcutScope Scope,                    // ShortcutScope.Global or new ShortcutScope(pageName)
    IReadOnlyList<KeyChord> Chord,          // one KeyChord, or several for a chord sequence
    Func<CancellationToken, Task> Handler,
    string? Category = null);               // cheatsheet group
```

**Default global shortcuts:**

| Shortcut | Action |
|---|---|
| `Ctrl+K`, `Ctrl+Shift+P` | Command Palette |
| `Ctrl+P` | Jump To |
| `F1`, `Ctrl+Shift+?` | Keyboard shortcuts (cheatsheet) |
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

Page-scoped shortcuts: AI Chat `Ctrl+Shift+N` (new conversation) and `Ctrl+B` (toggle the
conversation pane); Knowledge Vault `F5` (refresh); Settings `Ctrl+S` (save). `Escape` closes the
command palette. `Win+Shift+A` is a system-wide hotkey registered by `SystemTrayService`; it opens
Quick Chat. To add a shortcut, see [5.5](#55-adding-a-keyboard-shortcut).

### 4.5 Startup Sequence

1. `App()`: `InitializeComponent`, `ConfigureLogging()` (Serilog), `ConfigureExceptionHandling()`.
2. `OnLaunched()`:
   - builds the host (`appsettings.json`, `RagPrompts.json`, `UseSerilog`, `ConfigureServices`);
   - `InitializeLocalizationAsync()` applies the saved UI language before any shell resource loads;
   - creates `MainWindow` (maps, shortcuts, palette, status strip pollers, the queued navigation to
     the Dashboard and the onboarding check), configures the tray and shows the window;
   - calls `InitializeCoreServicesAsync()` without awaiting it.
3. `InitializeCoreServicesAsync()` awaits each step in order:
   1. `SQLitePCL.Batteries_V2.Init()`.
   2. `IDatabaseEncryptionMigrator.RecoverIfNeeded` finishes or undoes an interrupted encryption
      change.
   3. If `encryption.info.json` exists, the key is unlocked (DPAPI-wrapped, or a passphrase dialog
      loop for a legacy passphrase keystore) and cached in `IDatabaseKeyProvider`;
      `AgentXDbContext.EnsureKeyApplied()` applies it.
   4. `StartupOrchestrator.RunCriticalStartupAsync()`: `MigrationRunner.RunAsync()`, then
      `StartupGate.SignalDataReady()`, the local REST API (when enabled) and the built-in calendar
      and email connectors. A migration failure stops here (see [4.7](#47-error-handling-strategy)).
   5. FTS5 table (`KeywordSearchService.InitializeFtsAsync`), auto-sync resume, interrupted workflow
      runs, `IAiService.InitializeAsync()`, feature flags, theme, enabled plugins, scheduled
      backups, then the indexing pipeline and the watch folders on the thread pool. Each of these
      logs its own failure and startup continues.

The full sequence, including shutdown, is in [ARCHITECTURE.md, section 12](ARCHITECTURE.md#12-startup-sequence).

### 4.6 Instrument Strip (Status Bar)

Two typed pollers feed the strip at the bottom of `MainWindow`:

- **`StatusBarService`** (every 30 seconds, first after 5): the active provider's connection
  (`CheckConnectionAsync`) and model for the `MDL` lamp and LCD, the indexing queue for `IDX`, the
  document count for `VAULT`. Each cycle also re-evaluates the `LOCAL`/`NET` privacy lamp through
  `IPrivacyStatusService`.
- **`AnnunciatorService`** (every 30 seconds, first after 6): inbox pending count and sync state
  every cycle; backup age and workflow-run health every fourth cycle. They drive `INBOX`, `SYNC`,
  `JOBS` and `BAK`.

Each source fails soft (a failed query keeps the previous state), and lamps map typed states, never
display strings. A lit lamp navigates to its source page when clicked; an unlit one ignores clicks.
The lamp table is in [ARCHITECTURE.md, section 5.2](ARCHITECTURE.md#52-mainwindow-and-navigation-shell).

### 4.7 Error Handling Strategy

| Situation | Handling |
|---|---|
| `AppDomain.UnhandledException` | `Log.Fatal`, then `Log.CloseAndFlush()` |
| `Application.UnhandledException` (UI thread) | `Log.Fatal`, and `e.Handled = true` so the app keeps running |
| `TaskScheduler.UnobservedTaskException` | `Log.Error`, then `SetObserved()` |
| Startup migration failure | Recovery state: a dialog ("Agent-X could not start", naming missing tables when known), no data-backed feature starts, the app exits |
| Other startup steps | Logged (`Warning`, or `Error` for the indexing pipeline); startup continues without that feature |
| Background fire-and-forget work | `try`/`catch` inside the task, logged as a warning |
| Command failures the user should see | Logged, then shown through a view model status or error property, or `INotificationService.ShowError(title, message)` |

`INotificationService` (`Services/NotificationService.cs`) also offers `ShowSuccess`,
`ShowWarning` and `ShowInfo`; the `NotificationOverlay` control displays them.

### 4.8 Async Patterns

**Cancellation.** Public service methods take a `CancellationToken` and pass it down the call
chain. `AgentX.Core` uses `ConfigureAwait(false)` (it also references
`Microsoft.VisualStudio.Threading.Analyzers`, which reports threading problems as warnings); view
models do not, because they must return to the UI thread.

**Linked tokens for Stop.** `ChatService` starts every reply through `BeginGenerationAsync`, which
cancels any reply in flight and links the caller's token with its own:

```csharp
_generationCts = new CancellationTokenSource();
return CancellationTokenSource.CreateLinkedTokenSource(ct, _generationCts.Token);
```

`StopGenerationAsync()` cancels `_generationCts`, which ends the stream without cancelling the
caller's token. A lock serializes starts and stops.

**Fire and forget.** Non-critical follow-up work runs without being awaited and always catches its
own exceptions, as memory extraction after a chat reply does:

```csharp
_ = Task.Run(async () =>
{
    try
    {
        if (_semanticMemoryService is not null)
            await _semanticMemoryService.ExtractMemoriesAsync(conversationId);
        else
            await _memoryService.ExtractMemoriesAsync(conversationId);
    }
    catch (Exception ex)
    {
        _log.Warning(ex, "Background memory extraction failed for conversation {ConversationId}", conversationId);
    }
});
```

### 4.9 WinUI 3 File and Folder Pickers

An unpackaged WinUI 3 app must initialize pickers with the window handle. Most pickers live in page
code-behind and pass the result to the view model:

```csharp
var picker = new FileOpenPicker();
picker.FileTypeFilter.Add(".pdf");

var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
InitializeWithWindow.Initialize(picker, hwnd);

var files = await picker.PickMultipleFilesAsync();
if (files is not null && files.Count > 0)
{
    await ViewModel.ImportWithDedupCommand.ExecuteAsync(files.Select(f => f.Path).ToList());
}
```

`App.MainWindow` is the static reference to the main window. `ChatViewModel.PickAudioFileAsync` is
the one view model that opens a picker itself, through the same call.

### 4.10 Markdown Rendering

Chat replies are rendered in two passes:

1. **`MarkdownParser.Parse(string content)`** (`Helpers/MarkdownParser.cs`) splits the text into
   `MarkdownSegment` objects of type `Text`, `CodeBlock`, `InlineCode`, `Bold`, `Heading` or
   `ListItem`. `ChatMessageItem.ContentSegments` calls it whenever `Content` changes.
2. **`MarkdownMessageControl`** renders the segments with WinUI controls. Code blocks get syntax
   highlighting (`SyntaxHighlighter`) for supported languages and a localized Copy button.

The parser is a small regular-expression pass written for model output; it does not use Markdig
(Markdig is used by the Core `MarkdownProcessor` for imported documents).

---

## 5. Adding New Features

### 5.1 Adding a New Page

A page is done when it is reachable, not when it compiles. Follow every step; the guard tests in
`tests/AgentX.Tests/CodeQuality/` fail on the steps that are easy to miss.

**Step 1: The view model** (`src/AgentX.App/ViewModels/ReadingListViewModel.cs`):

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class ReadingListViewModel : ObservableObject
{
    private readonly IMyService _myService;

    [ObservableProperty]
    private bool _isLoading;

    public ReadingListViewModel(IMyService myService)
    {
        _myService = myService;
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            // Load data here.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load the reading list");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await InitializeAsync();
}
```

**Step 2: The page** (`src/AgentX.App/Views/ReadingListPage.xaml` and `.xaml.cs`):

```xml
<Page
    x:Class="AgentX.App.Views.ReadingListPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Grid>
        <TextBlock x:Uid="ReadingList_Title" Text="Reading List" />
        <ProgressRing IsActive="{x:Bind ViewModel.IsLoading, Mode=OneWay}" />
    </Grid>
</Page>
```

```csharp
using AgentX.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace AgentX.App.Views;

public sealed partial class ReadingListPage : Page
{
    public ReadingListViewModel ViewModel { get; }

    public ReadingListPage()
    {
        // Use PageViewModelFactory.Create<ReadingListViewModel>() instead if the view model
        // implements IDisposable.
        ViewModel = App.GetService<ReadingListViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }
}
```

Visual decisions (colors, type, spacing, depth, lamps) come from [DESIGN.md](../DESIGN.md); read it
before laying out the page.

**Step 3: Register both** in `App.xaml.cs`, `ConfigureServices()`:

```csharp
services.AddTransient<ViewModels.ReadingListViewModel>();
services.AddTransient<Views.ReadingListPage>();
```

**Step 4: Add the tag to `PageMap`** in `MainWindow.xaml.cs`:

```csharp
["ReadingList"] = typeof(Views.ReadingListPage),
```

**Step 5: Add the rail item** in `MainWindow.xaml`, under the group header it belongs to:

```xml
<NavigationViewItem x:Name="NavReadingList"
                    x:Uid="Main_ReadingList" Content="Reading List"
                    Tag="ReadingList"
                    Style="{StaticResource AgentXNavItemStyle}">
    <NavigationViewItem.Icon>
        <FontIcon Glyph="&#xE7C3;" FontSize="16" />
    </NavigationViewItem.Icon>
</NavigationViewItem>
```

Pick a glyph no other rail item uses.

**Step 6: Add the tag to `BuildNavItemMap()`** in `MainWindow.xaml.cs`:

```csharp
["ReadingList"] = NavReadingList,
```

**Step 7: Add the strings** (`Main_ReadingList.Content`, `ReadingList_Title.Text` and any others) to
all six `Strings/<locale>/Resources.resw` files (see [13.9](#139-localized-strings)).

**The command palette needs nothing.** `MainWindow.ConfigureCommandPalette()` walks
`NavView.MenuItems` and `FooterMenuItems` and registers every rail item with the palette under the
rail's localized label, glyph and group. `NavRailParityTests` checks that every rail item has an
`x:Uid`, a `Tag` with `PageMap` and `BuildNavItemMap` entries, a unique glyph and a group header,
and that the palette still derives its pages from the rail. To give the page a keyboard shortcut,
see [5.5](#55-adding-a-keyboard-shortcut).

### 5.2 Adding a New Service

**Step 1: The interface** in the matching folder of `AgentX.Core/Services/`:

```csharp
// AgentX.Core/Services/MyFeature/IMyService.cs
namespace AgentX.Core.Services.MyFeature;

public interface IMyService
{
    Task<string> DoWorkAsync(string input, CancellationToken ct = default);
}
```

**Step 2: The implementation:**

```csharp
// AgentX.Core/Services/MyFeature/MyService.cs
using AgentX.Core.AI;
using Serilog;

namespace AgentX.Core.Services.MyFeature;

public sealed class MyService : IMyService
{
    private readonly IAiService _aiService;
    private readonly ILogger _logger;

    public MyService(IAiService aiService, ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _logger = (logger ?? throw new ArgumentNullException(nameof(logger))).ForContext<MyService>();
    }

    public async Task<string> DoWorkAsync(string input, CancellationToken ct = default)
    {
        _logger.Information("Doing work for input length {Length}", input.Length);
        return await Task.FromResult("result").ConfigureAwait(false);
    }
}
```

**Step 3: Register it** in `App.xaml.cs`, in the group it belongs to, and add the `using`:

```csharp
services.AddSingleton<IMyService, MyService>();
```

**Step 4: Make it reachable.** A service nothing resolves is dead code. The container resolves
services lazily, so a missing registration only shows when the first consumer is built.

### 5.3 Adding a New Document Processor

A processor implements `IDocumentProcessor`:

```csharp
public interface IDocumentProcessor
{
    IReadOnlySet<string> SupportedExtensions { get; }
    bool CanProcess(string filePath);
    Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default);
}
```

`DocumentService` (imports) and `IndexingService` (re-extraction) receive
`IEnumerable<IDocumentProcessor>` and use the first registered processor whose `CanProcess`
returns `true`, so check that no built-in processor already claims the extension (the table is in
[ARCHITECTURE.md, section 6.3](ARCHITECTURE.md#63-document-processing-pipeline)).

**Step 1: Implement the interface.** This example reads SubRip subtitle files:

```csharp
// AgentX.Core/Documents/Processors/SubtitleProcessor.cs
using System.Text.RegularExpressions;
using AgentX.Core.Documents.Models;
using AgentX.Core.Helpers;

namespace AgentX.Core.Documents.Processors;

public sealed class SubtitleProcessor : IDocumentProcessor
{
    private static readonly HashSet<string> Extensions =
        new(StringComparer.OrdinalIgnoreCase) { ".srt" };

    private static readonly Regex CueNumberOrTiming = new(
        @"^\s*(\d+|\d{2}:\d{2}:\d{2},\d{3}\s*-->\s*\d{2}:\d{2}:\d{2},\d{3})\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public IReadOnlySet<string> SupportedExtensions => Extensions;

    public bool CanProcess(string filePath) => Extensions.Contains(Path.GetExtension(filePath));

    public async Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("Subtitle file not found.", filePath);

        try
        {
            var raw = await File.ReadAllTextAsync(filePath, ct);
            var text = CueNumberOrTiming.Replace(raw, string.Empty).Trim();

            return new ProcessedDocument
            {
                FilePath = filePath,
                FileName = fileInfo.Name,
                FileType = "srt",
                FileSizeBytes = fileInfo.Length,
                ContentHash = await HashHelper.ComputeFileHashAsync(filePath, ct),
                ExtractedText = text,
                ExtractedTitle = Path.GetFileNameWithoutExtension(filePath),
                PageCount = 1,
                WordCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A reason the user can read: the document is recorded as failed with this message.
            throw new DocumentExtractionException(
                $"Could not read the subtitle file '{fileInfo.Name}': {ex.Message}", ex);
        }
    }
}
```

`ProcessedDocument` also carries `Language`, `Metadata` (author, subject, dates, custom values) and
`Chunks`; the chunks are produced later by `ChunkingService`. A processor that separates pages with
form feeds (`\f`) gets page numbers on its chunks.

**Step 2: Register it** with the other processors in `App.xaml.cs`:

```csharp
services.AddSingleton<IDocumentProcessor, SubtitleProcessor>();
```

`EveryCollectionServiceIsRegisteredTests` fails when an `IDocumentProcessor` implementation in Core
has no registration line; two processors once shipped unregistered and their formats were silently
rejected.

**Step 3: Offer the extension in the import picker** (`KnowledgeVaultPage.xaml.cs`, the
`FileTypeFilter` list), if users should be able to pick it there.
`ImportPickerOffersOnlyProcessableTypesTests` fails when the picker offers an extension that no
processor claims. Watch folders need no change: `FileWatcherService` asks
`DocumentService.CanProcess`.

**From a plugin.** A plugin can contribute a processor by implementing `IDocumentProcessorPlugin`
(`IPlugin` plus `IDocumentProcessor`). `DocumentService` tries plugin processors after the built-in
ones, so a plugin only handles formats no built-in processor accepts. `IndexingService` does the
same when it has to extract a document again (a re-index, or a document still queued when the app
restarted), so a plugin-format document is re-read while its plugin is active. See
[PLUGIN-DEVELOPMENT-GUIDE.md](PLUGIN-DEVELOPMENT-GUIDE.md) and `plugins/sample-plugin/`.

### 5.4 Adding a New AI Provider

**Step 1: Implement `IAiProvider`** in `AgentX.Core/AI/Providers/`:

```csharp
public interface IAiProvider : IDisposable
{
    string ProviderId { get; }
    string DisplayName { get; }
    bool IsAvailable { get; }
    Task<bool> CheckConnectionAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default);
    Task PullModelAsync(string modelName, IProgress<ModelDownloadProgress>? progress = null, CancellationToken ct = default);
    Task DeleteModelAsync(string modelName, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamChatAsync(IReadOnlyList<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default);
    Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default);
    Task<float[]> GenerateEmbeddingAsync(string text, string modelName, CancellationToken ct = default);
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, string modelName, CancellationToken ct = default);
}
```

Follow `OpenAiProvider` for a hosted API: take the key, endpoint, logger and an optional
`ICostTracker`, stream tokens from server-sent events, and report usage with
`_costTracker.RecordUsage(modelId, ProviderId, inputTokens, outputTokens)`. A provider without
embeddings throws `NotSupportedException` from the two embedding methods, as `AnthropicProvider`
does.

**Step 2: Add its settings** to `AppSettings` (`AgentX.Core/Services/Settings/AppSettings.cs`), for
example `MyCustomApiKey`, `MyCustomEndpoint` and `MyCustomDefaultModel`. Add the key to
`SettingsService.SecretFields`, so it is stored DPAPI-encrypted like the other API keys.

**Step 3: Register it in `AiService.InitializeAsync()`.** Providers are registered through a local
`Register` function with a fingerprint of their configuration; an unchanged fingerprint keeps the
existing instance, so saving settings does not cut off a reply that is streaming:

```csharp
if (!string.IsNullOrWhiteSpace(settings.MyCustomApiKey))
{
    Register("mycustom", Fingerprint(settings.MyCustomApiKey, settings.MyCustomEndpoint),
        () => new MyCustomProvider(settings.MyCustomApiKey, settings.MyCustomEndpoint, _logger, _costTracker));
}
```

Add its default model to `ResolveDefaultModel()`:

```csharp
return providerId.ToLowerInvariant() switch
{
    "local" => Or(settings?.LocalModelFileName, BuiltInModelBootstrap.DefaultModelFileName),
    "openai" => Or(settings?.OpenAiDefaultModel, OpenAiProvider.DefaultModelId),
    "anthropic" => Or(settings?.AnthropicDefaultModel, AnthropicProvider.DefaultModelId),
    "mycustom" => Or(settings?.MyCustomDefaultModel, MyCustomProvider.DefaultModelId),   // add
    _ => Or(settings?.DefaultModel, "llama3.2")
};
```

and a case to `ApplyModelSetting()`, which stores a model picked at run time in the provider's own
setting. `SettingsViewModel` calls `IAiService.InitializeAsync()` again after every save.

**Step 4: Tell the privacy check.** If prompts leave the computer, add the provider to
`CloudAiProviderName()` and its key to `HasCloudAiKey()` in `PrivacyStatusService`. Otherwise the
`LOCAL` lamp, the Dashboard and the chat privacy note would claim that nothing leaves the machine.

**Step 5: Make it selectable.** Add it to `ProviderChoices.All` (the Active Provider list), give it
a section under Settings, AI Providers, with its strings in all six locales, and add its models to
`CostTracker.KnownCosts` if they are priced. `EmbeddingTargetResolver` chooses embedding providers
separately and will not pick a new provider unless you extend it.

### 5.5 Adding a Keyboard Shortcut

**A global shortcut** is added in `ShortcutCatalog.SeedDefaults()` with its `Global(...)` helper.
The label and category come from `ILocalizationService`, so add the label to the six resw files:

```csharp
Global("nav.readinglist",
    _localization.GetString("Shortcut_ReadingList"),
    KeyModifiers.Ctrl | KeyModifiers.Shift,
    VirtualKeyCode.L,
    Navigate(actions, "ReadingList"),
    pageActions);
```

If the shortcut opens a page, also add the page tag and the shortcut id to `PageShortcutIds`; the
command palette then prints the chord next to the page, read from the live registry.

**A page-scoped shortcut** is registered when the page is shown and removed when it is left. The
scope name must be the page's class name, because `ShortcutInputRouter` uses
`ContentFrame.CurrentSourcePageType?.Name` as the active scope:

```csharp
// _shortcutRegistry (IShortcutRegistry) and _localization (ILocalizationService) are
// resolved with App.GetService<T>() in the page constructor.
private IDisposable? _shortcutScope;

protected override void OnNavigatedTo(NavigationEventArgs e)
{
    base.OnNavigatedTo(e);
    _shortcutScope = _shortcutRegistry.RegisterShortcuts(      // ShortcutRegistrationExtensions
        new ShortcutDescriptor(
            "readinglist.refresh",
            _localization.GetString("Shortcut_ReadingListRefresh"),
            new ShortcutScope(nameof(ReadingListPage)),
            new[] { new KeyChord(KeyModifiers.None, VirtualKeyCode.F5) },
            _ => ViewModel.RefreshCommand.ExecuteAsync(null),
            _localization.GetString("Shortcut_CategoryActions")));
}

protected override void OnNavigatedFrom(NavigationEventArgs e)
{
    base.OnNavigatedFrom(e);
    _shortcutScope?.Dispose();
    _shortcutScope = null;
}
```

The three existing page-scoped registrations (AI Chat, Knowledge Vault, Settings) still pass English
literals as labels and categories; new ones should use `ILocalizationService` as shown.

**Choosing a chord.** The registry does not reject duplicates: for a key, a descriptor scoped to the
current page wins, then the first global one registered. `Ctrl+K`, `Ctrl+Shift+P`, `Ctrl+P`, `F1`
and `Ctrl+Shift+?` are handled by the router before the registry is consulted, so they cannot be
reused. Check the tables in [4.4](#44-keyboard-shortcuts) for a free chord. A sequence of several
chords is a `Chord` list with more than one `KeyChord`.

---

## 6. Database and Data Access

### 6.1 Schema Overview

The database is one SQLite file at `%LocalAppData%\AgentX\agentx.db`. The native SQLite library
is SQLCipher (`SQLitePCLRaw.bundle_e_sqlcipher`) whether or not encryption is on; when it is on,
`IEncryptedConnectionFactory` applies the key (`PRAGMA key`) to every connection. The EF model maps
37 entities to snake_case tables; two more tables are created with raw SQL:

| Table | Created by | Purpose |
|---|---|---|
| 37 EF tables | Migrations and `MigrationRunner` | Conversations, messages, documents, chunks, collections, tags, memories, inbox, workflows, sync, plugins, temporal identity and more |
| `fts_chunks` | `KeywordSearchService.InitializeFtsAsync()` | FTS5 keyword index over chunk text |
| `vec_embeddings` | The vector store's `InitializeAsync()` | Embedding vectors (float32 BLOBs) with their precomputed norms |

Every table and column is listed in [DATABASE_SCHEMA.md](../DATABASE_SCHEMA.md).

The EF context always uses the fixed path above. The vector store opens the file named by the
`StoragePath` setting, which defaults to the same folder, so leave `StoragePath` at its default.

### 6.2 Entity Framework Core Configuration

The context is registered through a factory that passes the encrypted connection factory:

```csharp
services.AddSingleton<AgentXDbContext>(sp =>
{
    var options = new DbContextOptionsBuilder<AgentXDbContext>().Options;
    var factory = sp.GetRequiredService<AgentX.Core.Data.IEncryptedConnectionFactory>();
    return new AgentXDbContext(options, factory);
});
```

When the options carry no provider, the context configures SQLite on the fixed path itself, and it
always installs the serializing concurrency detector and query compiler (see
[4.1](#41-dependency-injection)).

The model is configured with the fluent API in `OnModelCreating()`, one private static
`ConfigureXxx(ModelBuilder)` method per entity or area. The only data annotations are the
`[MaxLength]` attributes on `InboxItemEntity`. Relationship examples:

```csharp
// Cascade: deleting a conversation deletes its messages
entity.HasOne(e => e.Conversation)
    .WithMany(c => c.Messages)
    .HasForeignKey(e => e.ConversationId)
    .OnDelete(DeleteBehavior.Cascade);

// Restrict: a collection with child collections cannot be deleted
entity.HasOne(e => e.ParentCollection)
    .WithMany(e => e.ChildCollections)
    .HasForeignKey(e => e.ParentCollectionId)
    .OnDelete(DeleteBehavior.Restrict)
    .IsRequired(false);

// SetNull: deleting a collection clears WatchFolder.TargetCollectionId
entity.HasOne(e => e.TargetCollection)
    .WithMany()
    .HasForeignKey(e => e.TargetCollectionId)
    .OnDelete(DeleteBehavior.SetNull)
    .IsRequired(false);
```

**Raw SQL.** Code that uses `Database.GetDbConnection()` directly holds the gate for the whole
section:

```csharp
using (db.EnterDatabaseGate())
{
    var connection = db.Database.GetDbConnection();
    // commands and transactions here
}
```

### 6.3 Schema Migrations

**Runtime.** `MigrationRunner.RunAsync()` runs on every launch, awaited, before anything else reads
data:

1. It decides from the schema (not from the file) whether a database already existed.
2. **Baseline adoption:** application tables without `__EFMigrationsHistory` (builds that used
   `EnsureCreated`) get the history table; missing baseline tables are created from
   `InitialBaseline`'s own operations, `InitialBaseline` is stamped, and so is every later migration
   whose schema is already present. If a baseline table is still missing it throws
   `BaselineSchemaIncompleteException` instead.
3. **Stamped-baseline repair:** missing baseline tables in a database that stamps `InitialBaseline`
   are recreated and brought forward.
4. **Reconciliation** of an old placeholder id for `AddTemporalIdentity`, and of
   `AddSemanticMemoryColumns` when its columns already exist.
5. `MigrateAsync()` applies pending migrations.
6. **Idempotent repairs** on every run: the operations tables, the Temporal Identity columns the
   `AddTemporalIdentity` migration left out, a compatibility schema for `inbox_items` and
   `belief_conflicts`, the `conversations` branching columns, and four indexes the model declares
   but no migration created.

It returns a `MigrationResult` (created, applied, already applied, database path), logged as
`Migration runner: db=... created=... applied=... alreadyApplied=...`. If it throws, startup enters
the recovery state described in [4.7](#47-error-handling-strategy).

**The migrations** (in `src/AgentX.Core/Data/Migrations/`):

| Migration | Purpose |
|---|---|
| `20260417011607_InitialBaseline` | The 28 baseline tables |
| `20260418013814_AddEncryptionColumns` | Encryption columns on `user_settings` |
| `20260418041030_RemoveEncryptionColumns` | Removes them again: encryption state lives in `encryption.info.json` |
| `20260422120000_AddSemanticMemoryColumns` | Embedding, links, decay, confidence and tags on `memories` |
| `20260422153000_AddConversationSummaryPersistence` | Summary snapshot and state tables |
| `20260423093000_AddMessageRecallEmbeddings` | Message embeddings for recall |
| `20260423153000_AddConversationThemeClustering` | Theme clusters and memberships |
| `20260423170000_AddConversationThemeDailyMetrics` | Daily theme metrics |
| `20260430000000_AddTemporalIdentity` | The five Temporal Identity tables |
| `20260503000000_AddEmbeddingModelVersioning` | Embedding model version, dimensions and time on chunks and memories |
| `20260528120000_DropLicensesTable` | Drops `licenses` (there are no license tiers) |

**Adding a migration.** Restore the pinned tool first (`dotnet tool restore`), then from the
repository root:

```powershell
dotnet ef migrations add <MigrationName> `
  --project src/AgentX.Core `
  --startup-project src/AgentX.Core `
  --output-dir Data/Migrations
```

`AgentX.Core` is its own startup project for tooling. `--startup-project src/AgentX.App` fails: the
EF host does not pass a platform and the WinUI build then errors. The design-time
`AgentXDbContextFactory` points EF at a throwaway, never-encrypted `agentx.design.db` (git-ignored),
and the `CopyWindowsSdkProjectionForEfTooling` target in `AgentX.Core.csproj` copies the Windows SDK
projection assemblies into the build output so the EF host can load the assembly. Review the
generated migration, commit it with the entity change, and the next launch applies it.

**Rollback during development:**

```powershell
# Remove the most recent migration that has not shipped
dotnet ef migrations remove --project src/AgentX.Core --startup-project src/AgentX.Core
```

`dotnet ef database update` only touches the design-time database. The SQLite provider cannot
generate idempotent scripts (`dotnet ef migrations script --idempotent`); the runtime runner is what
applies migrations to user databases.

**Migrations and encryption.** At run time the migration runs on the shared connection after the key
has been applied, so it works the same on encrypted and plaintext databases. Never toggle encryption
inside a migration; `IDatabaseEncryptionManager` and `IDatabaseEncryptionMigrator` own that.

When an entity, its configuration or a migration changes, update
[DATABASE_SCHEMA.md](../DATABASE_SCHEMA.md) in the same change.

### 6.4 Indexing Status Lifecycle

`documents.IndexingStatus`:

```
pending -> processing -> completed
                      -> failed      (IndexingError holds the reason)
```

An import whose file cannot be read is saved as `failed` straight away, with the reason, rather than
as an empty success. `indexing_jobs.Status` moves through `queued`, `processing`, `completed` and
`failed`. At startup `IndexingService.InitializeAsync()` sets documents left in `processing` back to
`pending` and jobs back to `queued`, then queues every pending document.

### 6.5 Vector Storage

`vec_embeddings` stores each vector as a BLOB of float32 values, 4 bytes each:

```csharp
// Serialization
var bytes = new byte[embedding.Length * sizeof(float)];
Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);

// Deserialization
var floats = new float[blob.Length / sizeof(float)];
Buffer.BlockCopy(blob, 0, floats, 0, blob.Length);
```

The `magnitude` column holds the precomputed L2 norm for cosine similarity. `VectorStoreFactory`
chooses `HnswVectorStore` when `EnableHnswIndex` is on (the default): the table stays the source of
truth, and an HNSW index answers searches above `HnswFallbackThreshold` (10,000) embeddings, with a
linear scan below that. `SqliteVecStore` always scans linearly. Details, including when the index is
persisted and how `HnswEfSearch` works, are in
[ARCHITECTURE.md, section 7.3](ARCHITECTURE.md#73-vector-store-implementation).

### 6.6 FTS5 Full-Text Search

`KeywordSearchService.InitializeFtsAsync()` creates the table with raw SQL, because EF Core cannot
create FTS5 virtual tables:

```sql
CREATE VIRTUAL TABLE IF NOT EXISTS fts_chunks USING fts5(
    content,
    document_id UNINDEXED,
    chunk_id UNINDEXED,
    file_name UNINDEXED,
    file_path UNINDEXED,
    file_type UNINDEXED,
    page_number UNINDEXED,
    chunk_index UNINDEXED,
    tokenize='porter unicode61'
);
```

Only `content` is indexed. A search selects from it with the filters inside the query:

```sql
SELECT content, document_id, chunk_id, file_name, file_path, file_type,
       page_number, chunk_index, rank
FROM fts_chunks
WHERE fts_chunks MATCH @query
  AND CAST(document_id AS INTEGER) IN
      (SELECT DocumentId FROM document_collections WHERE CollectionId = @collectionId)
ORDER BY rank
LIMIT @topK;
```

(the collection, file type and date conditions are added only when the query asks for them). An FTS5
initialization failure is logged as a warning and startup continues; keyword search then returns no
results.

---

## 7. AI Integration

### 7.1 Provider Architecture

**`IAiProvider`** is the low-level contract: connection check, model listing, pull and delete,
streaming and complete chat, and single and batch embeddings. **`IAiService`** (`AiService`) builds
the provider set from settings, keeps the active provider and model, and adds application
operations (`SummarizeAsync`, `GenerateTagsAsync`, `SwitchProviderAsync`, `SetActiveModelAsync`,
`ResolveEmbeddingTarget` and others). Services and view models use `IAiService`; the status strip,
Model Manager and the Dashboard read the active provider through it.

`AiService.InitializeAsync()` registers:

| Id | Provider | Registered when |
|---|---|---|
| `local` | `LocalLlmProvider` (LLamaSharp, GGUF file in `StoragePath\Models`) | Always |
| `ollama` | `OllamaProvider` (OllamaSharp) | `OllamaEndpoint` is an absolute http(s) URL |
| `openai` | `OpenAiProvider` | An OpenAI key is set |
| `anthropic` | `AnthropicProvider` | An Anthropic key is set |

The active provider is `ActiveProviderId` (`"local"` by default). If it is not registered, the
fallback is the built-in model when its file is installed, then Ollama, then whatever is registered.

### 7.2 Provider Implementations

- **`LocalLlmProvider`** loads the configured GGUF file (`LocalModelFileName`, default
  `llama-3.2-3b-instruct-q4_k_m.gguf`) with LLamaSharp on first use. `LocalGpuLayers` controls GPU
  offload: `0` is automatic, a positive number is used as given, a negative number keeps the model
  on the CPU. `PullModelAsync` downloads GGUF files listed in `BuiltInModelCatalog`.
- **`OllamaProvider`** uses OllamaSharp. The connection check times out after 3 seconds, so a
  stopped Ollama never hangs the caller. Models can be pulled and deleted.
- **`OpenAiProvider`** uses `HttpClient` with `Authorization: Bearer` and parses server-sent events.
  The endpoint is configurable for OpenAI-compatible servers.
- **`AnthropicProvider`** uses `HttpClient` with `x-api-key` and `anthropic-version: 2023-06-01`. The
  system prompt goes in the top-level `system` field, not in a message. Models come from
  `GET /v1/models`, with a small fallback list. It has no embeddings.

### 7.3 Embedding Service

`EmbeddingService` does not use the chat provider. `AiService.ResolveEmbeddingTarget()`
(`EmbeddingTargetResolver`) chooses the embedding provider from the Embedding Model setting, so
switching the chat model never changes the embedding space:

1. An OpenAI embedding model id (`text-embedding-*`) embeds with OpenAI.
2. A `.gguf` file name selects the built-in provider.
3. Any other non-default name is used as an Ollama model.
4. The default (`all-minilm`) uses the built-in model when its file is installed, and Ollama's
   `all-minilm` otherwise.

Batches use `Rag:EmbeddingBatchSize` (32 in `appsettings.json`). The vector size is learned from the
provider's output, and every document chunk is stamped with an `EmbeddingModelVersion` of the form
`provider:model:dimensions` (memories and messages have version columns too, but nothing writes
them). `CachedEmbeddingService` wraps the service with a bounded LRU cache.

### 7.4 Context Assembly

`ContextAssemblyService` builds the prompt for a chat reply within the context window minus a
1,024-token reserve for the answer: the system prompt and the current message, selected history
(`SemanticContextSelector`), a summary of older overflow (`ConversationCompressionService`), memory
context, and, when budget remains, up to three passages recalled from other conversations
(similarity 0.72 or more). If assembly fails, `ContextWindowManager` trims the oldest messages
instead.

Token counts are estimates from `TokenEstimator` (behind `ITokenCounter`): about four characters
per token for Latin script, and one or more tokens per character for Chinese, Japanese and Korean.

### 7.5 Conversation Memory

After each reply, `ChatService` extracts memories in the background (see
[4.8](#48-async-patterns)): through `SemanticMemoryService` when it is registered, otherwise
`ConversationMemoryService`. The basic service asks the model for `category|content` lines with the
categories `preference`, `fact`, `topic` and `instruction`. Memories are stored in `memories` with an
importance, a decay rate and an embedding, and relevant ones are added to later prompts. The chat's
context inspector lists them and can delete one or all.

### 7.6 Cost Tracking

Providers report token usage to `CostTracker`, which prices it from `KnownCosts` (per 1,000 tokens,
matched by the longest model-id prefix); the built-in and Ollama models cost nothing. The history is
saved to `usage-history.json` (90 days, at most 20,000 records, older totals carried forward), so the
Cost Tracking totals in Settings survive restarts. The figures are estimates for display only.

### 7.7 Routing and Multi-Agent Modes

With Enable Auto-Routing on (Settings, Multi-Model Routing), `ModelRouterService` classifies each
message (`TaskTypeDetector`) and picks a provider and model from the active profile (`balanced`,
`cost-optimized` or `quality-optimized`) for that reply only.

`MultiAgentOrchestrator` runs the chat's multi-agent modes: Parallel (researcher, critic and
synthesizer) and Debate (researcher, critic and creative, two rounds). The final synthesis is
assembled as text, without another model call. There is no tool calling.

---

## 8. Search and RAG

### 8.1 Search Architecture

```
SearchQuery (Mode: Semantic | Keyword | Hybrid)
    |
    v
HybridSearchOrchestrator
    |-- Semantic -> SemanticSearchService -> IVectorStore (HNSW index or linear scan)
    |-- Keyword  -> KeywordSearchService  -> fts_chunks (FTS5, BM25 rank)
    |-- Hybrid   -> both in parallel -> Reciprocal Rank Fusion -> merged results
```

The Semantic Search page starts in Semantic mode; the RAG pipeline uses Hybrid. Results are cached
by `SearchCacheService`, which indexing and deletion invalidate. The page has type filter chips,
including `CalendarEvent` and `EmailMessage` for connector items.

### 8.2 Semantic Search

`SemanticSearchService.SearchAsync()`:

1. Embeds the query (`IEmbeddingService.EmbedAsync`).
2. Searches the vector store for `TopK` results above the minimum similarity; collection, file type
   and date scopes restrict the candidates first.
3. Loads the chunks, documents and collection names with EF Core, skipping chunks whose
   `EmbeddingModelVersion` differs from the current one.
4. Returns `SearchResult` objects with the text, an excerpt around the query terms and the score.

### 8.3 Keyword Search

`KeywordSearchService.SearchAsync()`:

1. Turns the input into a MATCH expression: every term is double-quoted, so FTS5 operators and
   punctuation are matched as text; stop words are dropped and the remaining terms are joined with
   `OR` (a query of stop words only requires all of them).
2. Runs the query shown in [6.6](#66-fts5-full-text-search), with the file type, collection and date
   filters inside the SQL.
3. Builds each excerpt around the query words in C# (`BuildExcerpt`) and reports scores relative to
   the best hit.

A malformed MATCH expression is logged and returns no results.

### 8.4 Hybrid Search and Reciprocal Rank Fusion

`HybridSearchOrchestrator` runs both searches in parallel, each asked for
`TopK x Rag:RetrievalMultiplier` candidates (3, capped at `Rag:RetrievalCap`, 500). For each result
at rank `r` (1-based) in a list:

```
RRF_score += 1 / (k + r)
```

with `k = 60` (Cormack, Clarke and Buettcher, 2009). A result in both lists gets both contributions.
Scores are divided by the maximum, `2 / (k + 1)`, to fall between 0 and 1. If one backend fails, the
other backend's results are returned.

### 8.5 RAG Pipeline

`RagPipeline.AskAsync(question, collectionId, onToken, enableResearchMode)` serves Ask Your Files:

1. Multi-query expansion (`MultiQueryGenerator`, 3 variations) and HyDE for questions of 80
   characters or more.
2. Hybrid search for every query; results merged by chunk, keeping the best score.
3. No results: a fixed answer, without a model call.
4. PII redaction of the context chunks before any stage sends them to a model.
5. `RagReranker` (near-duplicate removal, query-term boost, document diversity), then `LlmReranker`
   when enabled and more than two chunks remain.
6. Parent-document expansion (redacted again) and contextual compression.
7. Research Mode web results, when requested and a web search provider is configured.
8. The prompt, with numbered sources `[1]`, `[2]` and prompt texts from `RagPrompts.json`.
9. The answer streams through `onToken` (temperature 0.3, at most 2,048 tokens, top-p 0.9).
10. `CitationService.ExtractCitations` maps `[N]` to documents and pages; `RagEvaluator` scores a
    sample of answers in the background.

Each optional stage that fails is logged and skipped. The full list, with the `Rag` options that
control each stage, is in [ARCHITECTURE.md, section 6.5](ARCHITECTURE.md#65-search-and-rag-pipeline).

### 8.6 Indexing Pipeline

`DocumentService` saves an imported document as `pending` and raises `DocumentPendingIndexing` with
the extracted text. `IndexingService` enqueues the id in its `Channel<long>` (unbounded, single
reader) and processes documents one at a time:

1. Checks that the vector store is ready; otherwise the document fails with the store's error.
2. Takes the text extracted at import, or extracts again when the file changed or the handoff is
   gone (for example after a restart).
3. Chunks it (`ChunkingService.ChunkDocument` with the `ChunkSize` and `ChunkOverlap` settings).
4. Removes the document's old keyword rows, vectors and chunks.
5. Saves the new `DocumentChunkEntity` rows.
6. Embeds them in batches of `Rag:EmbeddingBatchSize` and stores each vector
   (`IVectorStore.InsertEmbeddingAsync`), stamping the chunk's embedding version and dimensions.
7. Writes the keyword rows (`KeywordSearchService.IndexDocumentChunksAsync`, non-fatal).
8. Marks the document and job `completed`.
9. Invalidates the search cache and raises `DocumentIndexed`.
10. Applies automatic tags (`AutoTagService.ApplyAutoTagsAsync`, non-fatal).

On an error the document and job are marked `failed` with the message and `DocumentIndexingFailed`
is raised. When the queue has been empty for 30 seconds the loop picks up documents left `pending`
by other paths, and re-embeds documents embedded before model versions were recorded.

### 8.7 Chunking Algorithm

`ChunkingService` splits recursively:

1. On paragraph boundaries (`\n\n`).
2. A paragraph above `chunkSize` tokens is split at sentence boundaries (`. `, `! `, `? `, `.\n`).
3. A sentence still above `chunkSize` is split at word boundaries.

Tokens are counted with `ITokenCounter` (see [7.4](#74-context-assembly)); the constructors without
one, used in tests, count whitespace-separated words. The overlap repeats the last `chunkOverlap`
tokens of a chunk at the start of the next. Text with form-feed page breaks (`\f`) is chunked page
by page, so chunks keep their page number for citations. `AdaptiveChunkingService` classifies the
content, and for code and tables its recommended size replaces the configured one.

Defaults: `ChunkSize` 512 tokens and `ChunkOverlap` 50 (Settings: Chunk Size (tokens), Chunk
Overlap). The overlap must be smaller than the size. Re-index documents after changing either, or
the embedding model.

### 8.8 Connector Content in Search

Calendar events and email messages reach search through the Smart Inbox:

```
CalendarSyncService / EmailSyncService
    |
    v
IInboxService.UpsertExternalAsync()      content file under the app data folder, an
    |                                     "accepted" inbox row keyed by plugin and external id
    v
IDocumentService.ImportExternalContentAsync(fileTypeOverride: "CalendarEvent" or "EmailMessage")
    |
    v
IndexingService                           chunks, embeddings, keyword rows
```

- An unchanged item writes nothing; a changed one rewrites its content and re-indexes the linked
  document.
- An item that disappears at the source is retired with `RemoveExternalAsync`: a row with no vault
  document is deleted, and a vault document is kept and marked as removed (a connector never deletes
  a vault document).
- The vault import is best effort; if it fails the inbox row still exists.
- `InboxItemEntity.DocumentId` links the row to its document.

---

## 9. Data Connectors (Calendar and Email)

### 9.1 Plugin Architecture

Connectors implement `IPlugin` with `Type = PluginType.DataConnector`:

```
InitializeAsync(IPluginContext)   read settings, resolve services
ActivateAsync()                   start the sync timer
[periodic sync cycles]
DeactivateAsync()                 stop the timer
Dispose()
```

`IPluginContext` provides `Services` (an `IServiceProvider`), `PluginDataPath` and `Logger`. The
built-in calendar and email connectors are run by `BuiltinConnectorLifecycleService`, which gives
them `IOAuthService` and `IInboxService`, keeps their data in
`%LocalAppData%\AgentX\Plugins\com.agentx.calendar\data\` and `...\com.agentx.email\data\`, and
activates each only when Calendar sync or Email sync is on. It starts after the migration and
stops at shutdown (15-second cap). Third-party plugins loaded by `PluginService` get only
`IInboxService`: `IOAuthService` would hand them the user's refresh tokens.

### 9.2 OAuth2 Service

`IOAuthService` (`OAuthService`):

| Method | Purpose |
|---|---|
| `AuthorizeAsync(provider, scopes = null, redirectUri = null, cancellationToken)` | Browser sign-in with PKCE and a one-time state value, answered on a local loopback callback |
| `GetAccessTokenAsync(provider)` | A valid access token, refreshed when it is within `TokenRefreshBufferMinutes` (5) of expiry |
| `RefreshTokenAsync(provider)` | Forces a refresh |
| `RevokeAsync(provider)` | Revokes and deletes the credential |
| `GetCredentialAsync(provider)` | The stored credential, or null when not connected |
| `ApplyProviderSettings(OAuthSettings)` | Applies the client ids, secrets and redirect URIs from settings |

Credentials are stored in `oauth_credentials` with the tokens DPAPI-encrypted.
`OAuthProviderRegistry` defines the `google` and `microsoft` endpoints and scopes (read-only
calendar and mail; Microsoft adds `offline_access` so it issues a refresh token). The client
credentials are entered under OAuth App Credentials on the Calendar and Email pages; the default
redirect URIs are `http://localhost:8400/oauth/callback` (Google) and
`http://localhost:8401/oauth/callback` (Microsoft). `ApplyProviderSettings` unregisters a provider
whose client id is empty, and connecting it then fails with `OAuthProviderNotConfiguredException`.

### 9.3 Calendar Connector

| File | Purpose |
|---|---|
| `CalendarPlugin.cs` | Plugin lifecycle, sync timer, `calendar-sync-settings.json` |
| `ICalendarProvider.cs` | `ListCalendarsAsync`, `GetEventsAsync` with a delta token |
| `GoogleCalendarProvider.cs` | Google Calendar API v3 |
| `OutlookCalendarProvider.cs` | Microsoft Graph v1.0 |
| `CalendarSyncService.cs` | Providers to `CalendarEventProcessor` to `IInboxService` |
| `CalendarEventProcessor.cs` | Event to inbox item (`fileType` `CalendarEvent`, `sourceType` `calendar-connector`, plugin id `com.agentx.calendar`) |

Each sync fetches events per enabled calendar with the stored delta token, upserts them, retires
deleted events (including the stored occurrences of a deleted recurring series), and saves the
tokens per `provider:calendarId` in `calendar-delta-tokens.json`. The external id is
`provider:calendarId:eventId`.

The Calendar page offers Calendar sync, Sync interval (minutes), the past and future day range,
Conflict resolution, Include attendee details, Include event descriptions, Save Settings and Sync
Now.

### 9.4 Email Connector

| File | Purpose |
|---|---|
| `EmailPlugin.cs` | Plugin lifecycle, sync timer, `email-sync-settings.json` |
| `IEmailProvider.cs` | `ListFoldersAsync`, `GetMessagesAsync` with a delta token |
| `GmailProvider.cs` | Gmail API v1 (history-based delta) |
| `OutlookEmailProvider.cs` | Microsoft Graph v1.0 (message delta) |
| `EmailSyncService.cs` | Providers to `EmailTriageProcessor` to `IInboxService` |
| `EmailTriageProcessor.cs` | Message to inbox item (`fileType` `EmailMessage`, `sourceType` `email-connector`, plugin id `com.agentx.email`) and its category |

Each sync reads only the folders selected under Folders to sync (the inbox by default), upserts the
messages and saves the tokens per `provider:folderId` in `email-delta-tokens.json`. The external id
is `provider:folderId:messageId`.

**Searchable text.** `EmailTriageProcessor.ExtractSearchableContent()` writes the subject, sender,
To, Cc, date (ISO, UTC), folder, flags (starred, attachments, read), attachment names (unless
Include attachment names in search index is off), source provider and body: the plain-text part, or
the HTML part converted to text when there is no plain-text part (`IncludeHtmlBody`, on by default).

**Category.** `EmailTriageProcessor.Classify()` assigns one `EmailCategory` from ordered keyword
and sender rules (first match wins): `ActionRequired`, `Meeting`, `Financial`, `Social`,
`Promotion`, `Newsletter`, `Notification`, otherwise `Other`. It travels as the inbox item's
`sourceCategory`. No model is called; `EmailSyncSettings.EnableAiCategorization` and
`CategorizationPrompt` are read from older settings files but not applied.

### 9.5 External ID Format

| Source | Example external id |
|---|---|
| Google Calendar | `google:primary:abc123` |
| Outlook Calendar | `microsoft:AAMkAGI2AAA=:xyz789` |
| Gmail | `google:INBOX:msg-1` |
| Outlook Email | `microsoft:AAMkAGI2AAA=:msg-2` |

Inbox rows are keyed by source plugin id and external id, so a repeated item updates its row instead
of adding a second one. Content file names are hashes of those ids, so provider ids never reach the
file system.

---

## 10. Testing

### 10.1 Test Framework

| Package | Version | Purpose |
|---|---|---|
| xunit | 2.9.2 | Test framework |
| xunit.runner.visualstudio | 2.8.2 | Runner |
| Xunit.SkippableFact | 1.5.23 | Tests that skip when a prerequisite (the Playwright browser) is missing |
| FluentAssertions | 6.12.2 | Assertions |
| Moq | 4.20.72 | Interface mocks |
| coverlet.collector | 6.0.2 | Coverage collection |
| Microsoft.NET.Test.Sdk | 17.12.0 | Test host |
| Microsoft.CodeAnalysis.CSharp | 4.5.0 | Compiles throwaway plugin assemblies in `PluginServiceTests` |

The test project references `AgentX.Core` and compiles 83 `AgentX.App` source files as links (view
models, coordinators and shell services without WinUI dependencies), because the self-contained
WinUI project cannot be referenced directly.

### 10.2 Running Tests

```powershell
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj -p:Platform=x64

# One class or area
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~ChunkingServiceTests"

# Locale tool and locale snapshot tests
dotnet test tests/LocaleAudit.Tests/LocaleAudit.Tests.csproj
```

CI (`build-test.yml`) restores, builds the tests and the app in Release x64, installs Playwright's
Chromium, runs the suite with coverage, and applies the coverage gate
(`scripts/check-coverage.ps1 -CoverageFile TestResults`). The tests that drive a headless browser
need Chromium once: `pwsh tests/AgentX.Tests/bin/Release/net8.0-windows*/playwright.ps1 install chromium`
(or the Debug folder).

**Do not pass `--no-build` after editing anything in `src/AgentX.App`.** The linked App sources are
compiled into the test assembly, so `--no-build` runs a stale copy and can report a false pass.

The other CI workflows are listed in [CI.md](CI.md).

### 10.3 What to Test

- **Services against a real schema.** `TestDbContextFactory` (`tests/AgentX.Tests/Helpers/`) opens an
  in-memory SQLite database, creates the schema from the model and hands out contexts that share it;
  most data-backed service tests use it. Tests that need SQLCipher join the `SqlCipher` collection
  (`SqlCipherFixture` calls `Batteries_V2.Init()`).
- **Pure logic:** chunking, reciprocal rank fusion, the FTS query builder, markdown parsing, token
  estimation, the email classifier.
- **Vector math and storage:** cosine similarity, BLOB round trips, the HNSW index and its fallback.
- **Processors:** extraction from small sample files.
- **Linked view models:** command palette, Jump-To, cheatsheet, Quick Chat and others compiled into
  the test project.
- **Guard tests (`CodeQuality/`).** They read the sources and XAML and fail on defect classes that
  compiled fine before: an unregistered processor or export formatter, an import picker offering an
  unhandled type, rail and palette drift, interactive controls without an accessible name or without
  a handler, unreachable view model commands, undefined or orphaned XAML resource keys, theme-blind
  brushes in code-behind, spacing off the 4-pixel grid, radii off the DESIGN.md scale, banned hues,
  `NotImplementedException` stubs, disposable view models resolved through the root provider, the
  release scripts drifting from the app, and more.

Before relying on a new test, break the code it covers and confirm that it fails.

### 10.4 Writing a Unit Test

From `tests/AgentX.Tests/Documents/ChunkingServiceTests.cs`:

```csharp
using AgentX.Core.Documents;
using FluentAssertions;
using Serilog;
using Xunit;

namespace AgentX.Tests.Documents;

public sealed class ChunkingServiceTests
{
    private static readonly ILogger Silent = new LoggerConfiguration().CreateLogger();

    private static ChunkingService Service() => new(Silent);

    [Theory]
    [InlineData(10, 10)]
    [InlineData(10, 11)]
    public void ChunkText_OverlapAtOrAboveChunkSize_Throws(int chunkSize, int overlap)
    {
        var act = () => Service().ChunkText("some text here", chunkSize, overlap);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("chunkOverlap");
    }

    [Fact]
    public void ChunkText_ShortText_ProducesASingleChunk()
    {
        var chunks = Service().ChunkText("A short paragraph of text.", 512, 50);

        chunks.Should().ContainSingle();
        chunks[0].Content.Should().Be("A short paragraph of text.");
        chunks[0].TokenCount.Should().Be(5);   // word count: no ITokenCounter was given
    }
}
```

The overlap check throws with the message "Chunk overlap must be less than chunk size to ensure
forward progress."

### 10.5 Mocking Services

```csharp
using AgentX.Core.AI;
using AgentX.Core.AI.Context;
using AgentX.Core.AI.Models;
using AgentX.Core.Services.Chat;
using AgentX.Core.Services.Settings;
using FluentAssertions;
using Moq;
using Serilog;
using Xunit;

public sealed class ChatServiceEmptyMessageTests
{
    private readonly Mock<IAiService> _ai = new();

    [Fact]
    public async Task SendMessageAsync_WithEmptyMessage_YieldsNoTokens()
    {
        var chat = new ChatService(
            _ai.Object,
            Mock.Of<IConversationService>(),
            Mock.Of<ISettingsService>(),
            Mock.Of<IContextAssemblyService>(),
            Mock.Of<IConversationMemoryService>(),
            new LoggerConfiguration().CreateLogger());

        var tokens = new List<string>();
        await foreach (var token in chat.SendMessageAsync(1, ""))
            tokens.Add(token);

        tokens.Should().BeEmpty();
        _ai.Verify(s => s.StreamChatAsync(
            It.IsAny<IReadOnlyList<ChatMessage>>(),
            It.IsAny<string?>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

The last three `ChatService` constructor parameters (`IModelRouterService`,
`ISemanticMemoryService`, `IConversationSummaryService`) are optional. `ChatServiceRoutingTests` and
`ChatServiceContextAssemblyTests` in `tests/AgentX.Tests/Services/Chat/` show fuller setups.

---

## 11. Build, Publish, and Packaging

### 11.1 Development Builds

```powershell
# Whole solution
dotnet build -p:Platform=x64

# Release configuration
dotnet build -c Release -p:Platform=x64

# Run the app
dotnet run --project src/AgentX.App/AgentX.App.csproj -p:Platform=x64
```

Formatting is checked in CI with `dotnet format AgentX.sln --verify-no-changes` (whitespace, LF line
endings, using order); run `dotnet format AgentX.sln` before committing.

### 11.2 Self-Contained Publish

`scripts/build-installers.ps1` publishes with:

```powershell
dotnet publish src/AgentX.App/AgentX.App.csproj -c Release -r win-x64 --self-contained true `
    -p:Platform=x64 -p:WindowsPackageType=None -o publish/win-x64
```

Project settings behind the publish (`AgentX.App.csproj`):

| Property | Value | Effect |
|---|---|---|
| `WindowsPackageType` | `None` | Unpackaged; no MSIX |
| `WindowsAppSDKSelfContained` | `true` | Bundles the Windows App SDK runtime |
| `PublishReadyToRun` | `true` | Precompiled code for faster startup |
| `TargetFramework` | `net8.0-windows10.0.22621.0` | Windows SDK targeting |
| `TargetPlatformMinVersion` | `10.0.19041.0` | Windows 10 version 2004 minimum |
| `Platforms` | `x86;x64;ARM64` | The app builds for all three; the installer ships x64 |

### 11.3 Building the Installer

`installer/AgentX-Setup.iss` (Inno Setup 6) packages `publish\win-x64\*` in two profiles:

- **SLIM** (default): no model; the first-run wizard offers the download.
- **OFFLINE** (`ISCC /DAgentXOffline=1`): bundles `models\llama-3.2-3b-instruct-q4_k_m.gguf` into
  `%LocalAppData%\AgentX\Models` (never removed by uninstall). Run `scripts/download-model.ps1` first
  to fetch the file the app itself downloads.

The script sets:

- **AppId** `{B3F8A2D1-7E4C-4A9B-8F6D-1C5E3A2B9D7F}` (stable across upgrades);
- **install folder** `{autopf}\Agent-X`, with `PrivilegesRequired=lowest` (a per-user install
  without elevation unless the user chooses otherwise);
- **architecture** x64 only, **minimum Windows** `10.0.19041`, **compression** `lzma2/max` (solid);
- after install, `%LocalAppData%\AgentX\Logs` and `Models` are created; a running `AgentX.App.exe`
  is closed before files are replaced;
- uninstall removes the log files and leaves the database, settings and models.

Output goes to `installer-output\AgentX-Setup-<version>-x64.exe` (OFFLINE adds `-offline`).
`scripts/build-installers.ps1 -Profiles slim|offline|both` runs the publish (unless `-SkipPublish`)
and the compiles, and Authenticode-signs the binaries and installers when given a certificate
(`-RequireSign` makes an unsigned build an error); see [RELEASE-SIGNING.md](RELEASE-SIGNING.md).
`ReleaseScriptsMatchTheAppTests` checks that the scripts sign the executable the installer ships
and fetch the model file the app loads.

### 11.4 Version Numbering

The version is set in two places:

- `Directory.Build.props`: `Version`, `AssemblyVersion`, `FileVersion` and `InformationalVersion`
  (2.2.0).
- `installer/AgentX-Setup.iss`: `#define MyAppVersion "2.2.0"`.

For a release, update both, record the changes in `CHANGELOG.md`, then build.

### 11.5 Release Build

```powershell
dotnet build -c Release -p:Platform=x64
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64
./scripts/build-installers.ps1 -Profiles both -CertificateThumbprint <thumbprint> -RequireSign
# Output: installer-output\AgentX-Setup-2.2.0-x64.exe and ...-x64-offline.exe
```

---

## 12. Troubleshooting

### 12.1 Application Fails to Start

**Diagnostic:** the log at `%LocalAppData%\AgentX\Logs\agentx-yyyyMMdd.log`, which is created before
the window appears.

- **"Agent-X could not start" dialog.** The migration failed and the app stopped before loading any
  feature. The log has a fatal entry beginning "Startup halted in migration recovery state", with
  the missing tables when the baseline schema was incomplete. Keep a copy of
  `%LocalAppData%\AgentX` before trying anything else.
- **Unreadable `settings.json`.** The app does not overwrite it: it copies the file to
  `settings.json.corrupt-<UTC timestamp>` and runs on defaults for that session. A secret that cannot
  be decrypted (settings copied from another Windows account) is cleared and the original file is
  kept as `settings.json.undecryptable-<UTC timestamp>`.
- **Encrypted database.** With `encryption.info.json` present, the key is unlocked before the
  migration; a legacy passphrase keystore asks for the passphrase first.
- **Windows App SDK.** It is bundled with the app (self-contained); nothing needs to be installed.

### 12.2 AI Provider Not Available

**Symptom:** the `MDL` readout shows "<provider> not available" in amber (for example "Ollama not
available" or "Built-in LLM not available") and the `MDL` lamp holds amber.

1. The Dashboard shows the hint for the active provider: "Check that the built-in model is installed
   and that there is enough free memory to load it." or "Check that Ollama is running with a model
   downloaded, and that its address in Settings is correct."
2. In the log, `AI service initialized with {Provider} provider, model: {Model}` means it connected;
   `{Provider} is not reachable. AI service initialized in offline mode.` means it did not.
   `Preferred provider ... is not registered` means a missing API key or an invalid Ollama endpoint;
   the app fell back to another provider.
3. **Built-in model:** install it from the wizard, or from Model Manager while the built-in provider
   is active; check `%LocalAppData%\AgentX\Models`.
4. **Ollama:** run `ollama list`, and check the Endpoint under Settings, AI Providers, Ollama (Local)
   (default `http://localhost:11434`); use Test Connection. The check gives up after 3 seconds.

### 12.3 Indexing Fails

**Symptom:** a document shows `failed` in the Knowledge Vault; the reason is shown with it (from
`IndexingError`). The log has `Failed to index document {DocumentId} ({FileName})`.

- **"Source file no longer exists"**: the file moved after import. Re-import it from its new
  location.
- **"No processor found for file type"**: nothing claims the extension (see
  [5.3](#53-adding-a-new-document-processor)).
- **"The vector store is not available (...)"**: the store failed to initialize (the log has "Vector
  store failed to initialize"). Fix the cause, then use Re-index.
- **"The speech-to-text model is not installed..."**: install it on the Model Manager page; audio
  documents that failed for this reason are queued again automatically.
- **Embedding errors**: the embedding provider (see [7.3](#73-embedding-service)) is not reachable or
  the model is missing.
- **Unreadable PDF**: encrypted or malformed files PDFsharp cannot open fail with its message.

Re-index (on a row, in the preview panel, or for several selected documents) indexes the document
again from its original file; the Operations page can also queue a failed import again.

### 12.4 Search Returns No Results

**Semantic search:**

1. Only `completed` documents have searchable chunks.
2. Chunks embedded with another embedding model (a different `EmbeddingModelVersion`) are skipped.
   After changing the Embedding Model setting, re-index the documents.
3. On an unencrypted copy of the database, `SELECT COUNT(*) FROM vec_embeddings;` shows whether any
   vectors were stored.

**Keyword search:**

1. The log should contain `FTS5 keyword search initialized`; a warning beginning "FTS5
   initialization failed" means keyword search is off for the session.
2. `SELECT COUNT(*) FROM fts_chunks;` shows whether keyword rows exist.
3. Every term is matched literally (quoted); operators and wildcards are not interpreted, and a
   query of punctuation only returns nothing.

### 12.5 Onboarding Stuck or Not Showing

- **Force onboarding:** set `"onboardingCompleted": false` in `settings.json`, or delete the file.
- **Skip onboarding:** set `"onboardingCompleted": true`.
- **Navigation pane hidden after onboarding:** at the next status poll (every 30 seconds) the
  status strip handler (`OnStatusBarStateChanged` in `MainWindow.StatusTrayOnboarding.cs`) restores
  a pane that is hidden outside onboarding and logs "Nav pane was hidden outside of onboarding -
  restored".

### 12.6 Build Errors

- **A bare `dotnet build` fails** with a `win-anycpu` runtime identifier: pass `-p:Platform=x64`.
- **`dotnet ef` fails with the app as startup project:** use `--startup-project src/AgentX.Core`
  (see [6.3](#63-schema-migrations)).
- **Tests pass but the change is not in them:** the test run used `--no-build` after an App edit.
  Build again.
- **Playwright tests skip or fail to start:** install Chromium (see [10.2](#102-running-tests)).
- **Inno Setup "Source file not found":** publish to `publish\win-x64` first, or run
  `scripts/build-installers.ps1`; an OFFLINE build also needs the model in `models\`.
- **`dotnet format` fails in CI:** run `dotnet format AgentX.sln` and commit the result.

### 12.7 Log Files

Logs are written to `%LocalAppData%\AgentX\Logs\agentx-yyyyMMdd.log`, one file per day, the last 7
kept, at `Debug` level and above, in the format
`{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}`. Levels appear as `[DBG]`, `[INF]`,
`[WRN]`, `[ERR]` and `[FTL]`.

Startup lines to look for:

```
Agent-X logging initialized at {LogPath}
Localization initialized: {Language}
Migration runner: db={DbPath} created={Created} applied={Applied} alreadyApplied={AlreadyApplied}
FTS5 keyword search initialized
AI service initialized with {Provider} provider, model: {Model}
Theme initialized: {Theme}
Plugins activated: {Activated}; failed: {Failed}
Indexing pipeline started
Agent-X started successfully
```

The lines from the migration on come from `InitializeCoreServicesAsync` in this order. "Agent-X
started successfully" is written when `OnLaunched` returns, which does not wait for that
initialization, so it can appear anywhere among those lines. If an expected line is missing, read
the `[WRN]`, `[ERR]` or `[FTL]` lines around it.

---

## 13. Code Style Guidelines

### 13.1 General Principles

- Give public types and members an XML doc comment (`/// <summary>`), and explain non-obvious
  behavior, and why, in private helpers.
- Prefer named constants to inline literals, with a comment on where a value comes from (the RRF
  `k = 60` cites its paper).
- Plain ASCII in documentation and code comments: no em dashes, no decorative glyphs.
- A feature is done when it is reachable from an entry point, not when it compiles.

### 13.2 C# Language Conventions

`.editorconfig` and the `dotnet format` gate set the basics: four-space indentation, LF line endings,
file-scoped namespaces, `using` directives outside the namespace with `System` first, and `var` when
the type is apparent.

**Constructor validation** in services:

```csharp
_service = service ?? throw new ArgumentNullException(nameof(service));
```

**Pattern matching** over casts:

```csharp
if (args.SelectedItemContainer is NavigationViewItem selectedItem)
{
    var tag = selectedItem.Tag?.ToString();
}
```

**Switch expressions** for multi-branch values:

```csharp
var defaultModel = providerId.ToLowerInvariant() switch
{
    "openai"    => Or(settings?.OpenAiDefaultModel, OpenAiProvider.DefaultModelId),
    "anthropic" => Or(settings?.AnthropicDefaultModel, AnthropicProvider.DefaultModelId),
    _           => Or(settings?.DefaultModel, "llama3.2")
};
```

Use the null-forgiving operator only when the value cannot be null, with a comment saying why.

### 13.3 Async Conventions

- Async methods end in `Async` and accept `CancellationToken ct = default` last.
- `ConfigureAwait(false)` in `AgentX.Core`; not in view models, which must return to the UI thread.
- `async void` only for event handlers (and `App.InitializeCoreServicesAsync`, which nothing can
  await).
- No `.Result` or `.Wait()` on the UI thread; await, or run the work in the background with its own
  error handling.

### 13.4 Logging Conventions

Use message templates with named properties:

```csharp
// Structured: the properties are searchable
_logger.Information("Indexed document {DocumentId} ({FileName}): {ChunkCount} chunks in {ElapsedMs:F0}ms",
    documentId, document.FileName, chunkEntities.Count, elapsed);

// Avoid: interpolation produces plain text
_logger.Information($"Indexed document {documentId}");
```

| Level | When |
|---|---|
| `Debug` | Per-request detail, counts, paths |
| `Information` | Lifecycle events, completed operations |
| `Warning` | Degraded but working, recoverable errors |
| `Error` | An operation failed |
| `Fatal` | The app cannot continue |

Logs name files and show shortened queries; do not log document text, prompts in full, API keys or
tokens. Shorten user text before logging it:

```csharp
_logger.Debug("Streaming chat: {MessagePreview}",
    userContent.Length > 50 ? userContent[..50] + "..." : userContent);
```

The logger is flushed and closed only at shutdown; `LoggerIsFlushedOnlyAtShutdownTests` guards this.

### 13.5 File Organization

One primary type per file; small types used only by it (an enum, a result record) may share the
file. Chat's list items (`ChatMessageItem`, `ConversationListItem`, `SystemPromptItem`) have their
own files next to `ChatViewModel`. Interfaces sit next to their implementations.

### 13.6 XAML Conventions

- **Read [DESIGN.md](../DESIGN.md) before any UI work.** It defines the Command Console design:
  colors, type, spacing, depth recipes and lamp semantics. Guard tests enforce parts of it (4-pixel
  spacing grid, the radius scale, banned hues).
- `x:Name` in PascalCase; binding paths match view model property names; resource keys in `Styles/`
  in PascalCase (`TextPrimaryBrush`).
- Brushes come from `ThemeResource`, so the Default, Light and HighContrast theme dictionaries all
  resolve; a key used as a `ThemeResource` must exist in each. HighContrast stays bound to
  `SystemColor*` tokens and is exempt from the hardware skin.
- In code-behind, resolve theme-varying brushes with `ThemeResources.Brush(key)`, not
  `Application.Current.Resources[key]`, which ignores the theme applied to the window root.
- Every user-visible text has an `x:Uid`; every interactive control has an accessible name and a
  handler or command.
- Font icons use escapes: `&#xE8BD;` in XAML, `"\uE8BD"` in C#.

### 13.7 Interface Design

- `Task`-returning async members, with `CancellationToken ct = default` last.
- `IReadOnlyList<T>` for returned collections callers must not change.
- Concrete parameter types unless polymorphism is needed.

### 13.8 Disposable Resources

Services that own `HttpClient`, connections, timers or provider instances implement `IDisposable`:

```csharp
public sealed class MyService : IMyService, IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Release resources here.
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
```

The host disposes singletons at shutdown. Page view models created with `PageViewModelFactory` are
not disposed by anyone, by design: their `Dispose` methods only log or cancel work that should keep
running when the page is left.

### 13.9 Localized Strings

All user-visible text goes through the localization pipeline, in all six locales: `en-US` (the
source), `de`, `es`, `fr`, `ja` and `zh-CN` (`src/AgentX.App/Strings/<locale>/Resources.resw`).

**XAML: `x:Uid`.** The resource name is the uid plus the property:

```xml
<Button x:Uid="ReadingList_Refresh" Content="Refresh" />
```

```xml
<data name="ReadingList_Refresh.Content" xml:space="preserve">
  <value>Refresh</value>
</data>
```

**C#: `ILocalizationService.GetString(key)`**, for text produced in code (status text, dialogs,
notifications, shortcut labels). The resource name is the key itself:

```csharp
var title = _localization.GetString("Startup_FailedTitle");
var status = _localization.GetString("Provider_NotAvailable", providerName);   // with format args
```

**Plurals.** Define `<key>_one` and `<key>_other` (and other CLDR categories where a language needs
them) and call `FormatPlural(baseKey, count, args)`; a missing category falls back to `_other`
(`CldrPluralRuleProvider`).

**Checks:**

- `tools/LocaleAudit` computes coverage per locale:

  ```powershell
  dotnet run --project tools/LocaleAudit/LocaleAudit.Tool.csproj -- src/AgentX.App src src/AgentX.App/Strings --fail-below 98
  ```

  It exits 1 when a locale is below the threshold (2 for bad arguments, 3 when the audit itself
  fails) and writes `audit-report.json` (or the `--output` path).
- `LocaleAudit.Tests` fails when any locale has an orphan entry (a key nothing references), a
  blank value, a different key set from `en-US`, or coverage below 98%.
- `scripts/translations/<locale>.json` is the legacy translation record read by
  `inject-translations.py`. When a string is removed, remove it there too:
  `LegacyTranslationMirrorTests` fails when a mirror keeps a key the resw files no longer define.

`.github/workflows/locale-audit.yml` runs the audit and `LocaleAudit.Tests` on pull requests and on
pushes to `main`.

### 13.10 Keyboard Shortcuts

See [5.5](#55-adding-a-keyboard-shortcut).

---

*This guide describes the Agent-X code as of version 2.2.0 (September 2026). For the system design
see [ARCHITECTURE.md](ARCHITECTURE.md); for the public API see [API-REFERENCE.md](API-REFERENCE.md).*
