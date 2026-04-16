# Agent-X Developer Guide

Version 1.0.0 | Last updated: February 2026

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

Agent-X is a local-first AI personal intelligence hub for Windows. It runs entirely on the user's machine — no cloud services required for core functionality. Users import their documents, the application indexes and embeds them, and they can then have grounded AI conversations, run semantic searches, and query their document library directly using natural language.

**What makes Agent-X distinctive:**

- All AI inference runs locally via Ollama (with optional cloud fallback to OpenAI or Anthropic)
- All document data, embeddings, and conversation history are stored locally in SQLite
- The application is a self-contained Windows executable with no runtime dependencies
- Retrieval-Augmented Generation (RAG) grounds AI answers in the user's actual documents

**Tech stack summary:**

| Layer | Technology |
|---|---|
| UI Framework | WinUI 3 (Windows App SDK 1.6) |
| Language | C# 12 / .NET 8.0 |
| MVVM | CommunityToolkit.Mvvm 8.2.2 |
| Database | EF Core 8.0.11 + SQLite |
| Vector Store | Custom SQLite-based store (cosine similarity in C#) |
| AI — Local | OllamaSharp 4.0.12 |
| AI — Cloud | Raw HttpClient (OpenAI, Anthropic) |
| Document Processing | PDFsharp 6.1.1, DocumentFormat.OpenXml 3.2.0, Markdig 0.37.0 |
| Logging | Serilog 4.0.2 |
| Installer | Inno Setup 6 |

---

## 2. Prerequisites and Setup

### 2.1 Required Tooling

| Tool | Version | Notes |
|---|---|---|
| Visual Studio 2022 | 17.8 or later | With "Windows application development" workload |
| .NET SDK | 8.0 | Installed automatically with VS or from dotnet.microsoft.com |
| Windows App SDK | 1.6.x | Installed automatically via NuGet |
| Windows 10/11 | Build 19041+ (20H1+) | Target platform minimum |
| Ollama | Latest | For local AI inference during development |
| Inno Setup 6 | 6.x | Required only for building the installer |

Visual Studio workloads required:

- .NET desktop development
- Windows application development (includes Windows App SDK)

### 2.2 Getting Started

Clone the repository and open the solution:

```
git clone <repository-url>
cd Agent-X
```

Open `AgentX.sln` in Visual Studio 2022. The NuGet packages will restore automatically on first build.

**Install Ollama for local development:**

Download from [ollama.com](https://ollama.com) and install it. Then pull the recommended models:

```
ollama pull llama3.2
ollama pull all-minilm
```

`llama3.2` is the default inference model. `all-minilm` is the embedding model used for document indexing and semantic search. Both must be available for full functionality.

### 2.3 First Run Configuration

When Agent-X is launched for the first time, the onboarding wizard runs. It hides the navigation pane and walks the user through:

1. Selecting an AI provider (Ollama is default)
2. Choosing a default model
3. Configuring basic settings

During development you can skip onboarding by editing the settings file directly. The settings file is located at:

```
%LocalAppData%\AgentX\settings.json
```

Set `"onboardingCompleted": true` to bypass the wizard.

### 2.4 Application Data Locations

| Artifact | Path |
|---|---|
| Settings | `%LocalAppData%\AgentX\settings.json` |
| Database | `%LocalAppData%\AgentX\agentx.db` |
| Log files | `%LocalAppData%\AgentX\Logs\agentx-YYYYMMDD.log` |

---

## 3. Project Structure

The solution contains two main source projects and one test project:

```
Agent-X/
  AgentX.sln
  Directory.Build.props           # Shared build properties
  src/
    AgentX.App/                   # WinUI 3 application (presentation layer)
    AgentX.Core/                  # Business logic class library
  tests/
    AgentX.Tests/                 # xUnit unit tests
  installer/
    AgentX-Setup.iss              # Inno Setup 6 script
  publish/
    win-x64/                      # Self-contained publish output
  docs/                           # Documentation
```

### 3.1 AgentX.App — Presentation Layer

`AgentX.App` is the WinUI 3 executable project. It owns the UI, navigation shell, view models, and dependency injection configuration. It references `AgentX.Core` but `AgentX.Core` has no reference back to `AgentX.App`.

```
AgentX.App/
  App.xaml.cs              # Application entry point, DI container, Serilog setup
  MainWindow.xaml(.cs)     # Navigation shell, status bar, keyboard shortcuts, backdrop
  Views/                   # 16 XAML Page files
    AskFilesPage.xaml(.cs)
    ChatPage.xaml(.cs)
    CollectionManagerPage.xaml(.cs)
    DashboardPage.xaml(.cs)
    DigestPage.xaml(.cs)
    HardwareAdvisorPage.xaml(.cs)
    KnowledgeGraphPage.xaml(.cs)
    KnowledgeVaultPage.xaml(.cs)
    ModelManagerPage.xaml(.cs)
    OnboardingPage.xaml(.cs)
    PrivacyPolicyPage.xaml(.cs)
    QuickActionsPage.xaml(.cs)
    SearchPage.xaml(.cs)
    CalendarSettingsPage.xaml(.cs)
    EmailSettingsPage.xaml(.cs)
    SettingsPage.xaml(.cs)
    TermsOfServicePage.xaml(.cs)
    UserGuidePage.xaml(.cs)
  ViewModels/              # 13 ViewModel files
    AskFilesViewModel.cs
    ChatViewModel.cs
    CollectionManagerViewModel.cs
    DashboardViewModel.cs
    DigestViewModel.cs
    HardwareAdvisorViewModel.cs
    KnowledgeGraphViewModel.cs
    KnowledgeVaultViewModel.cs
    ModelManagerViewModel.cs
    OnboardingViewModel.cs
    QuickActionsViewModel.cs
    SearchViewModel.cs
    CalendarSettingsViewModel.cs
    EmailSettingsViewModel.cs
    SettingsViewModel.cs
  Controls/                # Custom UserControls
    CommandPalette.xaml(.cs)        # Ctrl+K VS Code-style command palette
    MarkdownMessageControl.xaml(.cs) # Rich markdown renderer for chat messages
  Converters/              # IValueConverter implementations
    BoolToOpacityConverter.cs
    BoolToVisibilityConverter.cs
    BytesToStringConverter.cs
    CountToVisibilityConverter.cs
    InverseBoolConverter.cs
    NullToVisibilityConverter.cs
    PercentToWidthConverter.cs
    StatusToColorConverter.cs
    StringToVisibilityConverter.cs
    TimeAgoConverter.cs
    TokensToStringConverter.cs
    ZeroToVisibleConverter.cs
  Helpers/
    DispatcherQueueExtensions.cs    # TryEnqueue helper for cross-thread UI updates
    MarkdownParser.cs               # Lightweight markdown-to-segment parser
  Services/
    KeyboardShortcutService.cs      # Global keyboard shortcut registry
  Styles/                  # XAML resource dictionaries
    Chat.xaml              # Chat bubble and message styles
    Colors.xaml            # Color palette and brush resources
    Controls.xaml          # Button, TextBox, and control overrides
    Documents.xaml         # Document card and vault styles
    Navigation.xaml        # NavigationView and sidebar styles
    Typography.xaml        # Font families, text styles
```

### 3.2 AgentX.Core — Business Logic

`AgentX.Core` is a class library targeting `net8.0-windows`. It contains all domain logic and is fully independent of WinUI. Every service exposes an interface, making it testable in isolation.

```
AgentX.Core/
  AI/
    IAiProvider.cs               # Low-level provider interface
    IAiService.cs                # High-level AI orchestration interface
    AiService.cs                 # Routes requests to active provider
    IContextWindowManager.cs
    ContextWindowManager.cs      # Trims conversation history to fit context window
    IEmbeddingService.cs
    EmbeddingService.cs          # Wraps provider embedding calls with batching
    IHardwareDetector.cs
    HardwareDetector.cs          # GPU/RAM/CPU detection via System.Management
    IModelManager.cs
    ModelManager.cs              # Model listing, install/uninstall coordination
    IRetryPolicy.cs
    ExponentialBackoffRetryPolicy.cs
    Models/
      AiModel.cs                 # Model metadata (name, family, size, quantization)
      ChatMessage.cs             # Role + content DTO
      ChatOptions.cs             # Temperature, MaxTokens, TopP, stop sequences
      CostTracker.cs             # Token usage and estimated cost accumulation
      HardwareCapability.cs      # GPU VRAM, recommended model size
    Providers/
      OllamaProvider.cs          # OllamaSharp 4.0.x implementation
      OpenAiProvider.cs          # Raw HttpClient + SSE streaming
      AnthropicProvider.cs       # Raw HttpClient + SSE streaming (top-level system field)
  Data/
    AgentXDbContext.cs           # EF Core DbContext with all 16 entity DbSets
    Entities/                    # EF Core entity classes
      CollectionEntity.cs
      ConversationEntity.cs
      DigestReportEntity.cs
      DocumentChunkEntity.cs
      DocumentCollectionEntity.cs
      DocumentEntity.cs
      DocumentTagEntity.cs
      IndexingJobEntity.cs
      LicenseEntity.cs
      MemoryEntity.cs
      MessageEntity.cs
      SearchHistoryEntity.cs
      SystemPromptEntity.cs
      TagEntity.cs
      UserSettingsEntity.cs
      WatchFolderEntity.cs
    VectorDb/
      IVectorStore.cs
      SqliteVecStore.cs          # Custom cosine similarity over SQLite BLOBs
      VectorSearchResult.cs
  Documents/
    IDocumentProcessor.cs        # Per-format text extraction interface
    IDocumentService.cs
    DocumentService.cs           # Document import, deduplication, CRUD
    IChunkingService.cs
    ChunkingService.cs           # Recursive character text splitter
    DuplicateCheckResult.cs
    Models/                      # ProcessedDocument, DocumentChunk
    Processors/
      PdfProcessor.cs            # PDFsharp text extraction
      DocxProcessor.cs           # DocumentFormat.OpenXml extraction
      TextProcessor.cs           # Plain text passthrough
      MarkdownProcessor.cs       # Markdig-based markdown stripping
      CodeFileProcessor.cs       # Source code files (cs, py, ts, etc.)
      ImageProcessor.cs          # Image metadata extraction (no OCR)
  Search/
    ISemanticSearchService.cs
    SemanticSearchService.cs     # Embed query -> vector ANN search -> hydrate results
    IKeywordSearchService.cs
    KeywordSearchService.cs      # SQLite FTS5 full-text search
    IHybridSearchOrchestrator.cs
    HybridSearchOrchestrator.cs  # Reciprocal Rank Fusion over semantic + keyword
    IRagPipeline.cs
    RagPipeline.cs               # End-to-end RAG: search -> prompt -> stream -> cite
    IRagReranker.cs
    RagReranker.cs               # Cross-encoder style reranking of retrieved chunks
    ICitationService.cs
    CitationService.cs           # Extract [1], [2] citations from AI responses
    Models/                      # SearchQuery, SearchResult, RagResponse, Citation
  Services/
    Chat/
      IChatService.cs
      ChatService.cs             # Streaming orchestrator: persist + stream via IAiService
      IConversationService.cs
      ConversationService.cs     # CRUD for conversations and messages
      IConversationMemoryService.cs
      ConversationMemoryService.cs # AI-extracted facts from conversations
      ISystemPromptService.cs
      SystemPromptService.cs     # Built-in and user-defined system prompt management
    Collections/
      ICollectionService.cs
      CollectionService.cs       # Hierarchical collection management
    Indexing/
      IIndexingService.cs
      IndexingService.cs         # Background pipeline: extract -> chunk -> embed -> store
      IIndexingQueueService.cs
      IndexingQueueService.cs    # Channel<long>-based queue wrapper
      IFileWatcherService.cs
      FileWatcherService.cs      # FileSystemWatcher for watch folders
    Intelligence/
      ISummaryService.cs
      SummaryService.cs          # AI-generated document summaries
      IDuplicateDetectionService.cs
      DuplicateDetectionService.cs # Content hash + semantic similarity dedup
      IOrganizationSuggestionService.cs
      OrganizationSuggestionService.cs # AI-suggested collection organization
      IKnowledgeGraphService.cs
      KnowledgeGraphService.cs   # Entity/relationship extraction from documents
      IDigestService.cs
      DigestService.cs           # Periodic knowledge digest report generation
    Settings/
      ISettingsService.cs
      SettingsService.cs         # JSON-persisted settings with in-memory cache
      AppSettings.cs             # Settings POCO with defaults
    License/
      ILicenseService.cs
      LicenseService.cs          # Offline HMAC-SHA256 license validation
      LicenseInfo.cs             # Feature gates by tier
      LicenseTier.cs             # Trial, Starter, Professional, Ultimate
    Tagging/
      IAutoTagService.cs
      AutoTagService.cs          # AI-generated tags applied during indexing
    OAuth/
      IOAuthService.cs
      OAuthService.cs            # OAuth2 with DPAPI encryption, PKCE, CSRF state
      OAuthCredential.cs         # Credential DTO
      OAuthProviderConfig.cs     # Per-provider auth endpoint configuration
      OAuthProviderRegistry.cs   # Google/Microsoft endpoint registry
    Inbox/
      IInboxService.cs
      InboxService.cs            # Smart inbox triage with AI preview generation
    Plugins/
      IPlugin.cs                 # Plugin interface (Initialize/Activate/Deactivate/Dispose)
      IPluginContext.cs           # Plugin DI context (Services, PluginDataPath, Logger)
      PluginType.cs               # Enum: Agent, DataConnector
      PluginService.cs            # Plugin lifecycle manager with scoped DI
      Calendar/
        CalendarPlugin.cs        # IPlugin for calendar sync
        ICalendarProvider.cs     # Google/Outlook calendar provider interface
        ICalendarService.cs
        CalendarService.cs        # Calendar sync orchestration
        CalendarSyncService.cs    # Provider → Processor → Inbox pipeline
        CalendarEventProcessor.cs # CalEvent → TriageExternalAsync conversion
        GoogleCalendarProvider.cs # Google Calendar API v3
        OutlookCalendarProvider.cs # Microsoft Graph API v1.0
        Models/                   # CalEvent, CalAttendee, CalendarInfo, SyncResult, etc.
      Email/
        EmailPlugin.cs            # IPlugin for email sync
        IEmailProvider.cs         # Gmail/Outlook email provider interface
        IEmailService.cs
        EmailService.cs           # Email sync orchestration
        EmailSyncService.cs       # Provider → Processor → Inbox pipeline
        EmailTriageProcessor.cs   # EmailMessage → TriageExternalAsync conversion
        GmailProvider.cs          # Gmail API v1 with history delta sync
        OutlookEmailProvider.cs   # Microsoft Graph API v1.0 with OData delta
        Models/                   # EmailMessage, EmailContact, EmailFolderInfo, etc.
```

### 3.3 AgentX.Tests — Unit Tests

```
AgentX.Tests/
  AgentX.Tests.csproj    # xUnit, Moq, FluentAssertions, coverlet
  AI/                    # AI service and provider tests
  Data/                  # Vector store and DbContext tests
  Documents/             # Processor and chunking tests
  Search/                # Search and RAG pipeline tests
  Services/              # Service layer tests
```

---

## 4. Architecture Patterns and Conventions

### 4.1 Dependency Injection

The DI container is configured in `App.xaml.cs` inside `ConfigureServices()`. It uses `Microsoft.Extensions.Hosting` and `Microsoft.Extensions.DependencyInjection`.

**Lifetime rules:**

| Type | Lifetime | Reason |
|---|---|---|
| All services (Core) | Singleton | Services hold state, connections, or are expensive to create |
| All ViewModels | Transient | Each page navigation creates a fresh ViewModel instance |
| All Views (Pages) | Transient | Each navigation creates a fresh Page instance |
| `AgentXDbContext` | Singleton | Shared across all services; SQLite handles concurrency |

**Service resolution:**

Services are resolved via constructor injection in all classes. For the rare case where a service must be resolved imperatively (e.g., in code-behind that cannot use constructor injection), use the static accessor:

```csharp
var myService = App.GetService<IMyService>();
```

This is used in `MainWindow.xaml.cs` for the status bar, in Pages for their ViewModels, and during startup initialization.

**Registration pattern:**

```csharp
// In App.xaml.cs ConfigureServices():

// Singleton service
services.AddSingleton<IMyService, MyService>();

// Transient ViewModel
services.AddTransient<MyViewModel>();

// Transient Page
services.AddTransient<Views.MyPage>();

// Multiple implementations of the same interface (used for IDocumentProcessor)
services.AddSingleton<IDocumentProcessor, PdfProcessor>();
services.AddSingleton<IDocumentProcessor, DocxProcessor>();
// Consuming classes inject IEnumerable<IDocumentProcessor>
```

### 4.2 MVVM Pattern

Agent-X uses `CommunityToolkit.Mvvm` for source-generated MVVM. The key attributes are:

**`[ObservableProperty]`** — Applied to a private field. The source generator creates:
- A public property with `PropertyChanged` notification
- An optional partial method `OnPropertyNameChanged(T value)` you can implement
- An optional partial method `OnPropertyNameChanging(T value)` for pre-change hooks

```csharp
// Declaration
[ObservableProperty]
private string _userInput = string.Empty;

// Generated output (you don't write this):
public string UserInput
{
    get => _userInput;
    set
    {
        if (SetProperty(ref _userInput, value))
            OnPropertyChanged(nameof(UserInput));
    }
}

// Optional hook — implement when you need side effects:
partial void OnUserInputChanged(string value)
{
    SendMessageCommand.NotifyCanExecuteChanged();
    OnPropertyChanged(nameof(CanSend));
}
```

**`[RelayCommand]`** — Applied to a method. The source generator creates an `IRelayCommand` property:

```csharp
// Synchronous command — CanExecute is always true unless canExecute is specified
[RelayCommand]
private void CopyMessage(string? content) { ... }

// Async command — the generated property is AsyncRelayCommand
[RelayCommand]
private async Task NewConversationAsync() { ... }

// Async command with CanExecute guard
[RelayCommand(CanExecute = nameof(CanSend))]
private async Task SendMessageAsync() { ... }
```

When the CanExecute condition changes, notify the command explicitly:

```csharp
SendMessageCommand.NotifyCanExecuteChanged();
```

**ViewModel initialization pattern:**

ViewModels are created by the DI container (transient), so their constructors cannot perform async work. The pattern used throughout Agent-X is an async `InitializeAsync()` method called from the page's code-behind:

```csharp
// In the Page code-behind:
public ChatPage()
{
    InitializeComponent();
    DataContext = App.GetService<ChatViewModel>();
}

protected override async void OnNavigatedTo(NavigationEventArgs e)
{
    base.OnNavigatedTo(e);
    if (DataContext is ChatViewModel vm)
    {
        await vm.InitializeAsync();
    }
}
```

The `InitializeAsync()` method in the ViewModel loads all data needed by the page. It must be guarded with try/catch because navigation can happen before services are fully initialized.

### 4.3 Navigation System

Navigation is handled by `MainWindow`. The key data structures are:

```csharp
// Maps tag strings to Page types
private readonly Dictionary<string, Type> _pageMap = new()
{
    ["Dashboard"] = typeof(Views.DashboardPage),
    ["Chat"] = typeof(Views.ChatPage),
    // ... 14 more entries
};

// Maps tag strings to NavigationViewItem controls
// Used to sync the nav pane selection indicator
private readonly Dictionary<string, NavigationViewItem> _navItemMap = new()
{
    ["Dashboard"] = NavDashboard,
    ["Chat"] = NavChat,
    // ... entries for visible nav items only
};
```

**Navigation is initiated from three places:**

1. User clicks a `NavigationViewItem` — handled by `NavView_SelectionChanged`
2. Keyboard shortcut fires — routes through `KeyboardShortcutService.HandleKeyDown` -> `NavigateToPage()`
3. Command palette selection — routes through `CommandPalette.ExecuteSelected` -> `NavigateToPageRequested` callback -> `NavigateToPage()`

The `NavigateToPage(string pageTag)` method is the single canonical navigation function:

```csharp
internal void NavigateToPage(string pageTag)
{
    if (_pageMap.TryGetValue(pageTag, out var pageType))
    {
        ContentFrame.Navigate(pageType);

        // Sync the NavigationView selection indicator
        if (_navItemMap.TryGetValue(pageTag, out var navItem))
        {
            NavView.SelectedItem = navItem;
        }
    }
}
```

**The `_suppressNavigation` flag** prevents a feedback loop during onboarding. When the onboarding flow hides the nav pane and clears `NavView.SelectedItem`, this flag prevents `NavView_SelectionChanged` from re-triggering navigation to the dashboard.

### 4.4 Keyboard Shortcuts

`KeyboardShortcutService` is a Singleton registered in DI. It maintains a dictionary keyed by `(VirtualKey, Ctrl, Shift, Alt)` tuples. Registration happens in `MainWindow.RegisterDefaultShortcuts()`:

```csharp
_keyboardShortcutService.RegisterShortcut(
    VirtualKey.K, ctrl: true, shift: false, alt: false,
    () => CommandPalette.Toggle());
```

The `RootGrid.PreviewKeyDown` handler in `MainWindow` calls `HandleKeyDown()` for every key press at the window root level, marking the event as handled if a shortcut fires.

**Default shortcuts:**

| Shortcut | Action |
|---|---|
| `Ctrl+K` | Toggle command palette |
| `Ctrl+N` | Navigate to Chat |
| `Ctrl+I` | Navigate to Knowledge Vault |
| `Ctrl+F` / `Ctrl+Shift+F` | Navigate to Search |
| `Ctrl+,` | Navigate to Settings |
| `Escape` | Close command palette |

### 4.5 Startup Sequence

The application startup follows this sequence:

1. `App()` constructor — `ConfigureLogging()` initializes Serilog, `ConfigureExceptionHandling()` hooks unhandled exception handlers
2. `OnLaunched()` — builds the `IHost` (triggers `ConfigureServices()`), then calls `InitializeCoreServicesAsync()` as fire-and-forget
3. `InitializeCoreServicesAsync()` — runs concurrently with window display:
   - Calls `dbContext.Database.EnsureCreatedAsync()` — creates all EF Core tables
   - Calls `keywordSearch.InitializeFtsAsync()` — creates FTS5 virtual table via raw ADO.NET
   - Calls `aiService.InitializeAsync()` — registers providers, checks connection
4. `MainWindow` constructor — sets up the `_pageMap`, `_navItemMap`, keyboard shortcuts, command palette callbacks, window configuration, title bar, backdrop, then navigates to `DashboardPage`
5. `CheckOnboardingAsync()` — if `OnboardingCompleted` is false, hides the nav pane and navigates to `OnboardingPage`
6. `InitializeStatusBar()` — starts a 30-second polling timer that updates connection status, indexing progress, and document count

### 4.6 Status Bar

The status bar at the bottom of `MainWindow` polls three services every 30 seconds (with an initial 5-second delay):

- **Connection dot and text**: `IAiService.ActiveProvider.CheckConnectionAsync()` — 3-second timeout
- **Indexing ring**: `IIndexingService.IsProcessing` and `GetQueueLengthAsync()`
- **Document count**: `IDocumentService.GetTotalDocumentCountAsync()`

Status bar errors are silently swallowed — status display is non-critical.

### 4.7 Error Handling Strategy

Error handling follows a deliberate tiered approach:

| Severity | Approach | Example |
|---|---|---|
| Fatal (unhandled) | `Log.Fatal` + `CloseAndFlush` | AppDomain.UnhandledException |
| Critical path | `Log.Error` + re-throw | Database init failure |
| Non-critical pipeline | `Log.Warning` + continue | FTS5 init, auto-tagging, status bar |
| Background fire-and-forget | Wrap in try/catch | Memory extraction after AI response |
| User-visible async commands | `Log.Error` + ViewModel error state | SendMessageAsync |

The convention in ViewModels: all `[RelayCommand]`-decorated methods have a top-level `try/catch` that logs with `Log.Error` and sets an error message property for display.

### 4.8 Async Patterns

**CancellationToken threading:**

All public service methods accept an optional `CancellationToken`. The token is passed through the entire call chain. Methods use `.ConfigureAwait(false)` in `AgentX.Core` (no UI context needed) but omit it in ViewModels (which must marshal back to the UI thread).

**Linked CancellationTokenSource for user cancellation:**

`ChatService.SendMessageAsync` uses a linked `CancellationTokenSource` to support both external cancellation (caller's CT) and user-initiated stop:

```csharp
_generationCts = new CancellationTokenSource();
var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _generationCts.Token);
```

Calling `_generationCts.CancelAsync()` from `StopGenerationAsync()` cancels the linked token, which propagates through the entire streaming chain.

**Fire-and-forget patterns:**

Non-critical background operations are launched without awaiting. These are always wrapped in try/catch:

```csharp
// Memory extraction after a chat response — non-critical
_ = Task.Run(async () =>
{
    try
    {
        await _memoryService.ExtractMemoriesAsync(conversationId);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Memory extraction failed for conversation {Id}", conversationId);
    }
});
```

### 4.9 WinUI 3 File and Folder Pickers

WinUI 3 requires the window HWND to initialize file pickers. This cannot be done in a ViewModel (which has no reference to the window). The pattern is to handle picker logic in the code-behind and pass the result to the ViewModel, or to use `App.MainWindow`:

```csharp
// In a ViewModel command that needs a file picker:
var picker = new Windows.Storage.Pickers.FileSavePicker();
picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
picker.FileTypeChoices.Add("Markdown", new List<string> { ".md" });
picker.SuggestedFileName = "export";

// Required for WinUI 3 — initialize with the window HWND
var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

var file = await picker.PickSaveFileAsync();
```

Note that `App.MainWindow` is the static property on the `App` class that stores the main window reference. This is intentionally accessible from ViewModels because the alternative (code-behind-only file pickers) creates untestable code.

### 4.10 Markdown Rendering

AI response content is rendered using a custom two-pass pipeline:

1. **`MarkdownParser.Parse(string content)`** — splits raw markdown text into typed `MarkdownSegment` objects: `Text`, `CodeBlock`, `InlineCode`, `Bold`, `Heading`, `ListItem`. This runs synchronously in `ChatMessageItem.ContentSegments` whenever `Content` changes.

2. **`MarkdownMessageControl`** — a `UserControl` that accepts a list of `MarkdownSegment` objects and renders them using appropriate WinUI 3 controls (TextBlock for text, custom code block with copy button for code, etc.).

The `MarkdownParser` deliberately does not use Markdig for runtime parsing — it is a lightweight pass-through that handles the most common patterns in AI output without the overhead of a full AST.

---

## 5. Adding New Features

### 5.1 Adding a New Page

Follow these steps exactly. Skipping any step will result in a page that is not navigable or not properly wired to the DI container.

**Step 1: Create the ViewModel**

Create `src/AgentX.App/ViewModels/MyNewPageViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class MyNewPageViewModel : ObservableObject
{
    // Observable properties
    [ObservableProperty]
    private string _pageTitle = "My New Page";

    [ObservableProperty]
    private bool _isLoading;

    // Inject required services via constructor
    private readonly IMyService _myService;

    public MyNewPageViewModel(IMyService myService)
    {
        _myService = myService;
    }

    // Async initialization called from code-behind after navigation
    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            // Load data here
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize MyNewPageViewModel");
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Commands
    [RelayCommand]
    private async Task DoSomethingAsync()
    {
        try
        {
            // Command logic
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DoSomething failed");
        }
    }
}
```

**Step 2: Create the XAML Page**

Create `src/AgentX.App/Views/MyNewPage.xaml`:

```xml
<Page
    x:Class="AgentX.App.Views.MyNewPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:AgentX.App.Views">

    <Grid>
        <TextBlock Text="{Binding PageTitle}" />
    </Grid>
</Page>
```

Create `src/AgentX.App/Views/MyNewPage.xaml.cs`:

```csharp
using AgentX.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AgentX.App.Views;

public sealed partial class MyNewPage : Page
{
    public MyNewPage()
    {
        InitializeComponent();
        DataContext = App.GetService<MyNewPageViewModel>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (DataContext is MyNewPageViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}
```

**Step 3: Register in App.xaml.cs**

Add both the ViewModel (transient) and the Page (transient) in `ConfigureServices()`:

```csharp
// ViewModels (Transient)
services.AddTransient<ViewModels.MyNewPageViewModel>();

// Views (Transient)
services.AddTransient<Views.MyNewPage>();
```

**Step 4: Add to the page map in MainWindow.xaml.cs**

Add an entry to `_pageMap` in the `MainWindow` constructor:

```csharp
["MyNewPage"] = typeof(Views.MyNewPage),
```

**Step 5: Add a NavigationViewItem in MainWindow.xaml**

Add a `NavigationViewItem` inside the appropriate section of the `NavigationView`:

```xml
<NavigationViewItem
    x:Name="NavMyNewPage"
    Content="My New Page"
    Tag="MyNewPage">
    <NavigationViewItem.Icon>
        <FontIcon Glyph="&#xE8BD;" />
    </NavigationViewItem.Icon>
</NavigationViewItem>
```

**Step 6: Add to the nav item map in MainWindow.xaml.cs**

Add an entry to `_navItemMap` so the selection indicator syncs correctly:

```csharp
["MyNewPage"] = NavMyNewPage,
```

**Step 7: Add to the Command Palette (optional)**

In `CommandPalette.xaml.cs`, add an entry to `_allItems` in `BuildCommandItems()`:

```csharp
new("My New Page", "Description of the page", "Pages", "\uE8BD", "MyNewPage", CommandItemKind.Page, ""),
```

### 5.2 Adding a New Service

**Step 1: Create the interface in AgentX.Core**

Place the interface in the appropriate subdirectory of `AgentX.Core/Services/`:

```csharp
// AgentX.Core/Services/MyFeature/IMyService.cs
namespace AgentX.Core.Services.MyFeature;

public interface IMyService
{
    Task<string> DoWorkAsync(string input, CancellationToken ct = default);
}
```

**Step 2: Create the implementation**

```csharp
// AgentX.Core/Services/MyFeature/MyService.cs
using Serilog;

namespace AgentX.Core.Services.MyFeature;

public sealed class MyService : IMyService
{
    private readonly IAiService _aiService;
    private readonly ILogger _logger;

    public MyService(IAiService aiService, ILogger logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _logger = logger?.ForContext<MyService>() ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> DoWorkAsync(string input, CancellationToken ct = default)
    {
        _logger.Information("Doing work for input length {Length}", input.Length);
        // Implementation
        return await Task.FromResult("result");
    }
}
```

**Step 3: Register in App.xaml.cs**

Add the registration in `ConfigureServices()`:

```csharp
services.AddSingleton<IMyService, MyService>();
```

**Step 4: Add the using statement**

Add the namespace import at the top of `App.xaml.cs`:

```csharp
using AgentX.Core.Services.MyFeature;
```

### 5.3 Adding a New Document Processor

Document processors implement `IDocumentProcessor` and are auto-discovered by `DocumentService` and `IndexingService` through `IEnumerable<IDocumentProcessor>` injection.

**Step 1: Implement the interface**

```csharp
// AgentX.Core/Documents/Processors/XmlProcessor.cs
using AgentX.Core.Documents.Models;
using Serilog;

namespace AgentX.Core.Documents.Processors;

public sealed class XmlProcessor : IDocumentProcessor
{
    private static readonly IReadOnlySet<string> _supportedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xml", ".xsd", ".xslt" };

    public IReadOnlySet<string> SupportedExtensions => _supportedExtensions;

    public bool CanProcess(string filePath)
        => _supportedExtensions.Contains(Path.GetExtension(filePath));

    public async Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default)
    {
        var content = await File.ReadAllTextAsync(filePath, ct);

        // Strip XML tags, extract text content
        var text = System.Text.RegularExpressions.Regex.Replace(content, "<[^>]+>", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

        return new ProcessedDocument
        {
            FileName = Path.GetFileName(filePath),
            FilePath = filePath,
            ExtractedText = text,
            PageCount = 1,
            WordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            CharacterCount = text.Length
        };
    }
}
```

**Step 2: Register in App.xaml.cs**

Add alongside the other processor registrations. The order determines which processor is tried first when multiple processors claim the same extension:

```csharp
services.AddSingleton<IDocumentProcessor, XmlProcessor>();
```

No other changes are needed. `DocumentService` and `IndexingService` both inject `IEnumerable<IDocumentProcessor>` and use the first processor that returns `true` from `CanProcess()`.

### 5.4 Adding a New AI Provider

**Step 1: Implement IAiProvider**

```csharp
// AgentX.Core/AI/Providers/MyCustomProvider.cs
using AgentX.Core.AI.Models;
using System.Runtime.CompilerServices;
using Serilog;

namespace AgentX.Core.AI.Providers;

public sealed class MyCustomProvider : IAiProvider
{
    public string ProviderId => "mycustom";
    public string DisplayName => "My Custom Provider";
    public bool IsAvailable => _isAvailable;

    private bool _isAvailable;
    private bool _disposed;
    private readonly ILogger _logger;

    public MyCustomProvider(string apiKey, string endpoint, ILogger logger)
    {
        _logger = logger?.ForContext<MyCustomProvider>()
            ?? throw new ArgumentNullException(nameof(logger));
        // Initialize HTTP client or SDK client
    }

    public async Task<bool> CheckConnectionAsync(CancellationToken ct = default)
    {
        // Ping health endpoint or list models
        _isAvailable = true; // Set based on actual check
        return _isAvailable;
    }

    public async Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
    {
        // Return available models — static list if no discovery endpoint
        return Array.Empty<AiModel>();
    }

    public Task PullModelAsync(string modelName, IProgress<ModelDownloadProgress>? progress = null, CancellationToken ct = default)
        => Task.CompletedTask; // Not applicable for most cloud providers

    public Task DeleteModelAsync(string modelName, CancellationToken ct = default)
        => Task.CompletedTask; // Not applicable for most cloud providers

    public async IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Implement SSE streaming — yield each token
        yield break;
    }

    public async Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default)
    {
        // Collect all tokens from streaming and return complete response
        var sb = new System.Text.StringBuilder();
        await foreach (var token in StreamChatAsync(messages, options, ct))
            sb.Append(token);
        return sb.ToString();
    }

    public Task<float[]> GenerateEmbeddingAsync(string text, string modelName, CancellationToken ct = default)
        => throw new NotSupportedException("Embeddings not supported by this provider.");

    public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, string modelName, CancellationToken ct = default)
        => throw new NotSupportedException("Embeddings not supported by this provider.");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
```

**Step 2: Add settings properties to AppSettings**

In `AgentX.Core/Services/Settings/AppSettings.cs`, add the configuration properties:

```csharp
// My Custom Provider
public string? MyCustomApiKey { get; set; }
public string MyCustomEndpoint { get; set; } = "https://api.mycustom.com/v1/";
public string? MyCustomDefaultModel { get; set; } = "mycustom-default";
```

**Step 3: Register in AiService.InitializeAsync()**

In `AgentX.Core/AI/AiService.cs`, add registration in the `InitializeAsync()` method after the existing providers:

```csharp
// Register MyCustom if API key is configured
if (!string.IsNullOrWhiteSpace(settings.MyCustomApiKey))
{
    try
    {
        var myCustomProvider = new MyCustomProvider(
            settings.MyCustomApiKey,
            settings.MyCustomEndpoint,
            _logger);
        _providers["mycustom"] = myCustomProvider;
    }
    catch (Exception ex)
    {
        _logger.Warning(ex, "Failed to create MyCustom provider");
    }
}
```

Also add a model resolution case in `ResolveDefaultModel()`:

```csharp
private static string ResolveDefaultModel(AppSettings settings, string providerId)
{
    return providerId.ToLowerInvariant() switch
    {
        "openai" => settings.OpenAiDefaultModel ?? "gpt-4o-mini",
        "anthropic" => settings.AnthropicDefaultModel ?? "claude-sonnet-4-20250514",
        "mycustom" => settings.MyCustomDefaultModel ?? "mycustom-default",  // Add this
        _ => settings.DefaultModel
    };
}
```

**Step 4: Add UI in SettingsPage.xaml**

Add a configuration section for the new provider in the Settings page, similar to the existing OpenAI and Anthropic sections. Include fields for API key, endpoint, and default model.

---

## 6. Database and Data Access

### 6.1 Schema Overview

The database is a single SQLite file at `%LocalAppData%\AgentX\agentx.db`. It contains 16 EF Core-managed tables and 2 additional tables managed via raw ADO.NET:

**EF Core tables:**

| Table | Purpose |
|---|---|
| `conversations` | AI chat conversation records |
| `messages` | Individual messages within conversations |
| `documents` | Imported document metadata |
| `document_chunks` | Text chunks extracted from documents |
| `collections` | Hierarchical document collections |
| `document_collections` | Many-to-many: documents in collections |
| `tags` | Tag catalog |
| `document_tags` | Many-to-many: tags on documents (with confidence score) |
| `search_history` | User search query history |
| `system_prompts` | Built-in and user-defined system prompt library |
| `user_settings` | Key-value store for fine-grained settings |
| `watch_folders` | File system watch folder configuration |
| `indexing_jobs` | Background indexing job tracking |
| `licenses` | License key storage |
| `memories` | AI-extracted conversation memories |
| `digest_reports` | Periodic knowledge digest report content |

**ADO.NET-managed tables (created by raw SQL in `InitializeFtsAsync`):**

| Table | Purpose |
|---|---|
| `document_chunks_fts` | FTS5 virtual table for full-text keyword search |
| `vec_embeddings` | Vector embedding storage (float[] BLOBs + magnitude) |

### 6.2 Entity Framework Core Configuration

`AgentXDbContext` uses the parameterless constructor pattern. The database path is computed at construction time and is not injected, which keeps the `AgentXDbContext` registration simple (no `DbContextOptions` builder required):

```csharp
// Registration in App.xaml.cs — no options needed
services.AddSingleton<AgentXDbContext>();
```

All entity configuration uses the fluent API inside `OnModelCreating()`. There are no data annotations on entity classes. Each entity has a dedicated private static `ConfigureXxx(ModelBuilder)` method for clarity.

**Example entity relationship configuration:**

```csharp
// Cascade delete: deleting a Conversation deletes all its Messages
entity.HasOne(e => e.Conversation)
    .WithMany(c => c.Messages)
    .HasForeignKey(e => e.ConversationId)
    .OnDelete(DeleteBehavior.Cascade);

// Restrict delete: deleting a parent Collection is blocked if it has children
entity.HasOne(e => e.ParentCollection)
    .WithMany(e => e.ChildCollections)
    .HasForeignKey(e => e.ParentCollectionId)
    .OnDelete(DeleteBehavior.Restrict)
    .IsRequired(false);

// SetNull: deleting a Collection sets WatchFolder.TargetCollectionId to NULL
entity.HasOne(e => e.TargetCollection)
    .WithMany()
    .HasForeignKey(e => e.TargetCollectionId)
    .OnDelete(DeleteBehavior.SetNull)
    .IsRequired(false);
```

### 6.3 Schema Creation — No Formal Migrations

Agent-X uses `EnsureCreatedAsync()` rather than EF Core migrations. This is a deliberate design decision:

- The app is deployed as a single-user desktop application
- Schema upgrades between versions are handled by `EnsureCreatedAsync()` for new databases and by defensive queries for existing ones
- Migration complexity is not justified for the distribution model

This means: if you add a new column to an entity, existing installations will not automatically get that column. Strategies for handling this:

1. Make the column nullable with a default value (EF Core will not fail to map it)
2. Add a startup check that runs `ALTER TABLE ... ADD COLUMN ...` if the column doesn't exist
3. For significant schema changes, increment a schema version stored in `user_settings` and run upgrade SQL at startup

### 6.4 Indexing Status Lifecycle

Documents move through a defined set of indexing statuses stored in `documents.indexing_status`:

```
pending -> processing -> completed
                      -> failed
```

`IndexingJobEntity` in `indexing_jobs` mirrors this with `Status` values of `queued`, `processing`, `completed`, `failed`. On startup, `IndexingService.InitializeAsync()` resets any stale `processing` jobs back to `queued` to recover from crashes.

### 6.5 Vector Storage

The `vec_embeddings` table stores embeddings as raw BLOBs using `Buffer.BlockCopy` (4 bytes per float):

```csharp
// Serialization
var bytes = new byte[embedding.Length * sizeof(float)];
Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);

// Deserialization
var floats = new float[blob.Length / sizeof(float)];
Buffer.BlockCopy(blob, 0, floats, 0, blob.Length);
```

The `magnitude` column stores the pre-computed L2 norm so cosine similarity can be computed without re-computing the query embedding's magnitude on every comparison. Similarity search is a full table scan in C# — suitable for collections up to approximately 100,000 embeddings on modern hardware. Beyond that scale, a proper ANN index (HNSW, IVF) would be needed.

### 6.6 FTS5 Full-Text Search

The FTS5 virtual table is created by `KeywordSearchService.InitializeFtsAsync()` using raw ADO.NET because EF Core does not support FTS5 virtual table creation syntax:

```sql
CREATE VIRTUAL TABLE IF NOT EXISTS document_chunks_fts
USING fts5(content, chunk_id UNINDEXED, document_id UNINDEXED);
```

The `content` column is indexed for full-text search. `chunk_id` and `document_id` are stored but not indexed (UNINDEXED). Searching uses FTS5 query syntax:

```sql
SELECT chunk_id, document_id, snippet(document_chunks_fts, 0, '[', ']', '...', 32)
FROM document_chunks_fts
WHERE document_chunks_fts MATCH @query
ORDER BY rank
LIMIT @limit;
```

FTS5 initialization failure is logged as a Warning and execution continues — keyword search will simply return no results.

---

## 7. AI Integration

### 7.1 Provider Architecture

The provider abstraction has two levels:

**`IAiProvider`** — the low-level interface. Each provider wraps a specific backend (Ollama, OpenAI API, Anthropic API). Responsibilities:
- Connection health check
- Model listing and management (pull, delete)
- Chat inference: streaming (`IAsyncEnumerable<string>`) and batch
- Embedding generation: single and batch

**`IAiService`** — the high-level orchestrator. Responsibilities:
- Provider lifecycle (initialization, switching)
- Prepending system prompts to message lists
- Ensuring the active model ID is set in `ChatOptions`
- Application-level operations: `SummarizeAsync()`, `GenerateTagsAsync()`

All ViewModel and service code uses `IAiService`, never `IAiProvider` directly (except `MainWindow.UpdateStatusBarAsync()` which checks the active provider's connection).

### 7.2 Provider Implementations

**OllamaProvider** uses OllamaSharp 4.0.x. Key implementation notes:

- Connection check uses a 3-second timeout via a linked `CancellationTokenSource` to avoid hanging when Ollama is not running
- Streaming uses `OllamaApiClient.ChatAsync()` which returns `IAsyncEnumerable<ChatResponseStream?>`
- Batch embeddings use `EmbedRequest` with a `List<string>` input
- The `SelectedModel` property on the client is set before embed calls

**OpenAiProvider** uses raw `HttpClient` with `Authorization: Bearer {apiKey}`. Streaming parses Server-Sent Events (SSE) lines manually. The endpoint is configurable to support OpenAI-compatible APIs (e.g., LM Studio, Groq).

**AnthropicProvider** uses raw `HttpClient` with `x-api-key: {apiKey}` and `anthropic-version: 2023-06-01` headers. Critical difference from OpenAI: **Anthropic requires the system prompt as a top-level `system` field in the request body**, not as a message with `role: "system"`. The `AiService.PrepareMessages()` method includes system messages as the first message in the list — the `AnthropicProvider` must extract this and move it to the top-level field when building its request payload.

Additionally, Anthropic does not expose a model listing endpoint. `AnthropicProvider.ListModelsAsync()` returns a hardcoded static list of known Claude models.

### 7.3 Embedding Service

`EmbeddingService` wraps the active provider's embedding calls with:

- Settings lookup for the configured embedding model name (default: `all-minilm`)
- Batching: splits large lists into groups of 32 before calling the provider
- Dimension constant: 384 (for all-MiniLM-L6-v2)

The embedding model must be separately configured from the chat model. In Ollama, this typically means pulling `all-minilm` independently of the LLM.

### 7.4 Context Window Management

`ContextWindowManager` trims conversation history to fit within the configured context window. The strategy:

1. Always include the system prompt if present
2. Always include the most recent user message
3. Fill remaining token budget with messages from most-recent to oldest
4. Truncate message content if a single message exceeds the per-message limit

Token count is approximated as `words * 1.3` (a common rough approximation).

### 7.5 Conversation Memory

`ConversationMemoryService` runs after each AI response (fire-and-forget) to extract memorable facts using the AI model itself. The extraction prompt instructs the model to return `category|content` pairs, one per line. Supported categories: `preference`, `fact`, `topic`, `instruction`.

Extracted memories are stored in the `memories` table with an importance score (0.0–1.0) and are injected into the system prompt of future conversations to personalize responses.

### 7.6 Cost Tracking

`CostTracker` accumulates token counts and estimates costs based on per-provider pricing tables. This is display-only and does not affect billing. It is reset on application restart.

---

## 8. Search and RAG

### 8.1 Search Architecture

Three search services compose into the `HybridSearchOrchestrator`:

```
SearchQuery (mode: Semantic | Keyword | Hybrid)
    |
    v
HybridSearchOrchestrator
    |-- Semantic only --> SemanticSearchService --> SqliteVecStore (cosine similarity)
    |-- Keyword only  --> KeywordSearchService  --> FTS5 (BM25 ranking)
    |-- Hybrid        --> Both in parallel --> Reciprocal Rank Fusion --> merged results
```

### 8.2 Semantic Search

`SemanticSearchService.SearchAsync()`:

1. Embeds the query text via `IEmbeddingService.EmbedAsync()`
2. Calls `IVectorStore.SearchAsync()` with the query embedding, `topK`, and `minSimilarity`
3. Loads chunk and document metadata from EF Core for the returned `chunkId` set
4. Applies collection filter (if `CollectionId` is specified in the query)
5. Returns `SearchResult` objects with matched text, excerpts, and scores

### 8.3 Keyword Search

`KeywordSearchService.SearchAsync()`:

1. Escapes special FTS5 query characters in the user query
2. Executes FTS5 `MATCH` query against `document_chunks_fts`
3. Uses FTS5 `snippet()` function to extract highlighted excerpt context
4. Applies collection filter via a JOIN with `document_collections`
5. Returns `SearchResult` objects with BM25-ranked results

### 8.4 Hybrid Search and Reciprocal Rank Fusion

`HybridSearchOrchestrator` runs semantic and keyword search in parallel (`Task.WhenAll()`). Each backend receives an expanded query with `TopK * 3` results to give RRF a larger candidate pool.

RRF scoring formula: for each result ranked at position `r` in list `L`:

```
RRF_score += 1 / (k + r)
```

Where `k = 60` (the standard constant from Cormack et al. 2009). Results appearing in both lists accumulate contributions from both. The final score is normalized to [0, 1] by dividing by the maximum possible RRF score (`2 / (k + 1)`).

If one backend fails during hybrid search, the orchestrator gracefully degrades to single-backend results from whichever succeeded.

### 8.5 RAG Pipeline

`RagPipeline.AskAsync()` orchestrates the complete question-answering flow:

**Step 1: Semantic search** — retrieves top-8 chunks with minimum similarity 0.25

**Step 2: Reranking** — `RagReranker` reorders retrieved chunks using a cross-encoder style scoring that considers both semantic relevance and document freshness

**Step 3: Context construction** — builds a numbered context block:
```
[1] source: document_name.pdf (page 3)
chunk text here...

[2] source: another_doc.docx
more chunk text...
```

**Step 4: System prompt construction** — prepends the RAG system prompt instructing the model to answer from context only and cite sources using `[1]`, `[2]`, etc.

**Step 5: Streaming inference** — streams the AI response token-by-token, calling the `onToken` callback for each token so the UI can display progressive output

**Step 6: Citation extraction** — `CitationService.ExtractCitations()` scans the completed response for `[N]` patterns, resolves them to the corresponding source chunks, and populates the `Citations` list in `RagResponse`

**Step 7: Return** — returns a `RagResponse` containing: answer text, list of citations, search latency, generation latency, and total latency

### 8.6 Indexing Pipeline

When a document is imported, `IndexingService.IndexDocumentAsync()` enqueues its ID into a `Channel<long>`. The background `ProcessQueueAsync()` loop processes documents sequentially:

1. Load document from database
2. Set status to `processing`
3. Find the appropriate `IDocumentProcessor` via `CanProcess()`
4. Extract text via `processor.ProcessAsync()`
5. Chunk via `ChunkingService.ChunkDocument()` using configured `ChunkSize` and `ChunkOverlap`
6. Delete any existing chunks and embeddings (for re-indexing)
7. Save new `DocumentChunkEntity` records to EF Core
8. Generate embeddings in batches of 16 via `EmbeddingService.EmbedBatchAsync()`
9. Store each embedding in `SqliteVecStore.InsertEmbeddingAsync()`
10. Update document status to `completed`
11. Run `AutoTagService.ApplyAutoTagsAsync()` (non-fatal)
12. Run `KeywordSearchService.IndexDocumentChunksAsync()` to populate FTS5 (non-fatal)

The channel is `UnboundedChannelOptions { SingleReader = true, SingleWriter = false }` — multiple threads can enqueue, but only one processes at a time to avoid overwhelming local model inference.

### 8.7 Chunking Algorithm

`ChunkingService` implements a recursive character text splitter:

**Splitting hierarchy:**
1. Split on `\n\n` (paragraph boundaries)
2. If a paragraph exceeds `chunkSize` tokens, split on sentence boundaries (`. `, `! `, `? `, `.\n`)
3. If a sentence exceeds `chunkSize` tokens, split on word boundaries (space-separated)

**Token counting:** approximated as whitespace-delimited word count. This is a safe lower bound since real tokenizers produce approximately 1.3 tokens per word.

**Overlap:** the last `chunkOverlap` tokens from each chunk are prepended to the next chunk. This ensures no context is lost at chunk boundaries, which is important for RAG retrieval quality.

**Multi-page documents:** PDFs extracted with form-feed separators (`\f`) are chunked page-by-page. This preserves page number metadata in `DocumentChunkEntity.PageNumber`, which flows through to `SearchResult.PageNumber` for accurate citations.

Default settings (configurable in `AppSettings`): `ChunkSize = 512` words, `ChunkOverlap = 50` words.

### 8.8 Data Connector Search Integration

Calendar and Email content from DataConnector plugins is integrated into the search pipeline via the **Inbox-to-Document bridge**:

```
CalendarSyncService / EmailSyncService
    |
    v
IInboxService.TriageExternalAsync()     -- auto-accepts, writes .txt temp file
    |
    v
IDocumentService.ImportExternalContentAsync()  -- creates DocumentEntity with
    |                                           semantic FileType preserved
    v
IndexingService                          -- chunks, embeds, FTS5 indexes
    |
    v
Searchable via Semantic / Keyword / Hybrid search
```

**Key design decisions:**

- `InboxItemEntity.FileType` stores the semantic type (`"CalendarEvent"`, `"EmailMessage"`)
- `DocumentEntity.FileType` also preserves the semantic type via `ImportExternalContentAsync(fileTypeOverride: ...)`
- The bridge is best-effort — if `IDocumentService` is unavailable, the inbox item is still created; only search indexing is skipped
- `InboxItemEntity.DocumentId` links back to the `DocumentEntity` for cross-referencing
- Search filter chips on `SearchPage` support `"CalendarEvent"` and `"EmailMessage"` types with appropriate icons

---

## 9. Data Connectors (Calendar and Email)

### 9.1 Plugin Architecture

Data Connectors implement the `IPlugin` interface with `Type = PluginType.DataConnector`. They are loaded by `PluginService` and receive a scoped `IPluginContext` containing:

- `IPluginContext.Services` — DI service provider (includes `IOAuthService`, `IInboxService`)
- `IPluginContext.PluginDataPath` — per-plugin data directory for settings and delta tokens
- `IPluginContext.Logger` — Serilog logger

**Plugin lifecycle:**

```
InitializeAsync(IPluginContext)  -- resolve dependencies, load settings
    |
    v
ActivateAsync()                  -- start sync timer, register providers
    |
    v
[Running: periodic sync cycles]
    |
    v
DeactivateAsync()                -- stop timer, flush state
    |
    v
Dispose()                        -- release resources
```

### 9.2 OAuth2 Service

`IOAuthService` provides provider-agnostic OAuth2 authorization:

| Method | Purpose |
|---|---|
| `AuthorizeAsync(providerId, scopes)` | Launch browser auth flow with CSRF state and PKCE |
| `GetAccessTokenAsync(providerId)` | Get valid access token (auto-refreshes if expired) |
| `RefreshTokenAsync(providerId)` | Force token refresh |
| `RevokeAsync(providerId)` | Revoke and delete credentials |
| `GetCredentialAsync(providerId)` | Check if a provider is connected |

Credentials are stored in SQLite (`oauth_credentials` table) with DPAPI encryption for access/refresh tokens. The `OAuthProviderRegistry` maps provider IDs (`"google"`, `"microsoft"`) to authorization/token endpoints and scopes.

### 9.3 Calendar Connector

**Key files:**

| File | Purpose |
|---|---|
| `CalendarPlugin.cs` | IPlugin lifecycle, sync timer, provider registration |
| `ICalendarProvider.cs` | Provider interface: `ListCalendarsAsync`, `GetEventsAsync` (returns delta token) |
| `GoogleCalendarProvider.cs` | Google Calendar API v3: sync tokens, all-day events, recurring expansion |
| `OutlookCalendarProvider.cs` | Microsoft Graph API v1.0: OData delta queries, iCalUId |
| `CalendarSyncService.cs` | Orchestration: providers → CalendarEventProcessor → IInboxService |
| `CalendarEventProcessor.cs` | Converts `CalEvent` → `TriageExternalAsync` parameters |
| `ICalendarService.cs` | Service interface: `SyncCalendarsAsync`, `IsConnectedAsync` |

**Sync flow:**

1. `CalendarPlugin.ExecuteSyncCycleAsync()` checks `IOAuthService.GetCredentialAsync()` for connected providers
2. For each connected provider, calls `CalendarSyncService.SyncAsync()`
3. `CalendarSyncService` iterates enabled calendars, fetches events via `ICalendarProvider.GetEventsAsync(deltaToken)`
4. Events are converted by `CalendarEventProcessor` into inbox parameters (fileName, fileType="CalendarEvent", sourceType="calendar-connector", externalId=`provider:calendarId:eventId`)
5. `IInboxService.TriageExternalAsync()` auto-accepts and bridges to the document library
6. Delta tokens are persisted per `provider:calendarId` key in `calendar-delta-tokens.json`

**Settings page:** `CalendarSettingsPage.xaml` with `CalendarSettingsViewModel` — connect/disconnect Google/Outlook, sync interval, days back, conflict resolution.

### 9.4 Email Connector

**Key files:**

| File | Purpose |
|---|---|
| `EmailPlugin.cs` | IPlugin lifecycle, sync timer, provider registration |
| `IEmailProvider.cs` | Provider interface: `ListFoldersAsync`, `GetMessagesAsync` (returns delta token) |
| `GmailProvider.cs` | Gmail API v1: labels, messages (list+get), history delta sync |
| `OutlookEmailProvider.cs` | Microsoft Graph API v1.0: mailFolders, messages/delta, OData pagination |
| `EmailSyncService.cs` | Orchestration: providers → EmailTriageProcessor → IInboxService |
| `EmailTriageProcessor.cs` | Converts `EmailMessage` → `TriageExternalAsync` parameters |
| `IEmailService.cs` | Service interface: `SyncMessagesAsync`, `IsConnectedAsync` |

**Sync flow:**

1. `EmailPlugin.ExecuteSyncCycleAsync()` checks OAuth credentials for connected providers
2. For each connected provider, calls `EmailSyncService.SyncAsync()`
3. `EmailSyncService` iterates enabled folders, fetches messages via `IEmailProvider.GetMessagesAsync(deltaToken)`
4. Messages are converted by `EmailTriageProcessor` into inbox parameters (fileName, fileType="EmailMessage", sourceType="email-connector", externalId=`provider:folderId:messageId`)
5. `IInboxService.TriageExternalAsync()` auto-accepts and bridges to the document library
6. Delta tokens are persisted per `provider:folderId` key in `email-delta-tokens.json`

**Email triage content:** `EmailTriageProcessor.ExtractSearchableContent()` builds a full-text representation including Subject, From (formatted), To, Cc, Date, Folder, Flags (Starred, HasAttachments), Attachment names, Source provider, and Body (text preferred, HTML fallback with `StripHtmlTags`).

**Settings page:** `EmailSettingsPage.xaml` with `EmailSettingsViewModel` — connect/disconnect Gmail/Outlook, sync interval, max messages per sync, days back, AI categorization toggle, attachment names toggle.

### 9.5 External ID Format

External IDs follow the pattern `{providerId}:{folderOrCalendarId}:{itemId}`:

| Source | Example External ID |
|---|---|
| Google Calendar | `google:primary:abc123` |
| Outlook Calendar | `microsoft:AAMkAGI2AAA=:xyz789` |
| Gmail | `google:INBOX:msg-1` |
| Outlook Email | `microsoft:AAMkAGI2AAA=:msg-2` |

Deduplication uses `ExternalId + SourcePluginId` — if a matching inbox item already exists, the duplicate is silently skipped.

---

## 10. Testing

### 10.1 Test Framework

| Package | Version | Purpose |
|---|---|---|
| xUnit | 2.9.2 | Test runner and assertions |
| FluentAssertions | 6.12.2 | Readable assertion syntax |
| Moq | 4.20.72 | Interface mocking |
| coverlet.collector | 6.0.2 | Code coverage collection |
| Microsoft.NET.Test.Sdk | 17.12.0 | Test host infrastructure |

### 10.2 Running Tests

```bash
# Run all tests
dotnet test

# Run with coverage collection
dotnet test --collect:"XPlat Code Coverage"

# Run specific test class
dotnet test --filter "FullyQualifiedName~ChunkingServiceTests"

# Run with verbose output
dotnet test --verbosity normal
```

From Visual Studio: open Test Explorer (`Ctrl+E, T`) and run all or selected tests.

### 10.3 What to Test

Focus unit tests on:

- **Pure business logic**: `ChunkingService`, `HybridSearchOrchestrator` (RRF algorithm), `MarkdownParser`, `LicenseService` (key validation), `AiService` (tag parsing, message preparation)
- **Vector math**: `SqliteVecStore.CosineSimilarity()`, `SerializeEmbedding()`/`DeserializeEmbedding()` round-trips
- **Document processors**: text extraction from sample files
- **Search orchestration**: correct delegation based on `SearchMode`, RRF merge correctness

Integration tests (requiring a live Ollama or SQLite database) are currently minimal. Use Moq to mock `IAiService`, `IAiProvider`, `IVectorStore`, `ISettingsService`, and `AgentXDbContext` for unit tests.

### 10.4 Writing a Unit Test

```csharp
using AgentX.Core.Documents;
using FluentAssertions;
using Xunit;

namespace AgentX.Tests.Documents;

public class ChunkingServiceTests
{
    private readonly ChunkingService _sut = new();

    [Fact]
    public void ChunkText_WithShortText_ReturnsSingleChunk()
    {
        // Arrange
        var text = "This is a short text that fits in one chunk.";

        // Act
        var chunks = _sut.ChunkText(text, chunkSize: 512, chunkOverlap: 50);

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Contain("short text");
    }

    [Fact]
    public void ChunkText_WithOverlapLargerThanChunkSize_ThrowsArgumentOutOfRange()
    {
        // Arrange
        var text = "Some text content for testing.";

        // Act
        var act = () => _sut.ChunkText(text, chunkSize: 100, chunkOverlap: 100);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Chunk overlap must be less than chunk size*");
    }

    [Theory]
    [InlineData(512, 50)]
    [InlineData(256, 25)]
    [InlineData(1024, 100)]
    public void ChunkText_ProducesChunksWithinSizeLimit(int chunkSize, int chunkOverlap)
    {
        // Arrange
        var text = string.Join(" ", Enumerable.Repeat("word", 2000));

        // Act
        var chunks = _sut.ChunkText(text, chunkSize, chunkOverlap);

        // Assert
        chunks.Should().AllSatisfy(chunk =>
            chunk.TokenCount.Should().BeLessOrEqualTo(chunkSize + chunkOverlap));
    }
}
```

### 10.5 Mocking Services

```csharp
using AgentX.Core.AI;
using AgentX.Core.AI.Models;
using Moq;

public class ChatServiceTests
{
    private readonly Mock<IAiService> _mockAiService = new();
    private readonly Mock<IConversationService> _mockConversationService = new();

    [Fact]
    public async Task SendMessageAsync_WithEmptyMessage_YieldsNoTokens()
    {
        // Arrange
        var chatService = new ChatService(
            _mockAiService.Object,
            _mockConversationService.Object,
            // ... other mocks
        );

        // Act
        var tokens = new List<string>();
        await foreach (var token in chatService.SendMessageAsync(1, ""))
        {
            tokens.Add(token);
        }

        // Assert
        tokens.Should().BeEmpty();
        _mockAiService.Verify(s => s.StreamChatAsync(
            It.IsAny<IReadOnlyList<ChatMessage>>(),
            It.IsAny<string?>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

---

## 11. Build, Publish, and Packaging

### 11.1 Development Builds

```bash
# Build all projects
dotnet build

# Run the application (development)
dotnet run --project src/AgentX.App

# Build in Release configuration
dotnet build -c Release
```

In Visual Studio: press F5 to build and run with debugger, Ctrl+F5 to run without debugger.

### 11.2 Self-Contained Publish

The release build produces a self-contained, single-directory publish with all .NET runtime and app dependencies bundled:

```bash
dotnet publish src/AgentX.App/AgentX.App.csproj \
    -c Release \
    -r win-x64 \
    --self-contained \
    -o publish/win-x64
```

Key project settings that control the publish behavior (in `AgentX.App.csproj`):

| Property | Value | Effect |
|---|---|---|
| `WindowsPackageType` | `None` | Unpackaged deployment — no MSIX |
| `WindowsAppSDKSelfContained` | `true` | Bundles Windows App SDK runtime |
| `PublishReadyToRun` | `true` | Pre-JIT compilation for faster startup |
| `TargetFramework` | `net8.0-windows10.0.22621.0` | Windows 10 SDK targeting |
| `TargetPlatformMinVersion` | `10.0.19041.0` | Minimum Windows 10 2004 (20H1) |

The publish output in `publish/win-x64/` contains all binaries. The installer packages this entire directory.

### 11.3 Building the Installer

Prerequisites:
- Inno Setup 6 must be installed at its default path (`C:\Program Files (x86)\Inno Setup 6\`)
- The publish output at `publish/win-x64/` must exist (run the publish step first)

```bash
"C:/Program Files (x86)/Inno Setup 6/ISCC.exe" installer/AgentX-Setup.iss
```

The installer script (`installer/AgentX-Setup.iss`) configures:

- **App ID**: `{B3F8A2D1-7E4C-4A9B-8F6D-1C5E3A2B9D7F}` — a stable GUID used for upgrades
- **Installation directory**: `%ProgramFiles%\Agent-X` (user can override)
- **Privileges**: `PrivilegesRequired=lowest` — no UAC elevation required for normal installs
- **Architecture**: x64 only (`ArchitecturesAllowed=x64compatible`)
- **Minimum Windows**: 10.0.18362 (Windows 10 1903)
- **Compression**: LZMA2 ultra64
- **Startup script**: Creates `%LocalAppData%\AgentX\{Logs,Data,Models}` directories on first install
- **Uninstall cleanup**: Removes log files from `%LocalAppData%\AgentX\Logs\` on uninstall; leaves user data (database, settings) intact

The installer output is written to `installer-output/AgentX-Setup-{version}-x64.exe`.

### 11.4 Version Numbering

The application version is defined in `installer/AgentX-Setup.iss`:

```
#define MyAppVersion "1.0.0"
```

For a new release:
1. Update the version in the `.iss` file
2. Update `Directory.Build.props` if it contains a version property
3. Publish the new binaries
4. Build the installer

### 11.5 Complete Release Build Script

```bash
# 1. Build
dotnet build -c Release

# 2. Test
dotnet test

# 3. Publish (self-contained, win-x64)
dotnet publish src/AgentX.App/AgentX.App.csproj \
    -c Release \
    -r win-x64 \
    --self-contained \
    -o publish/win-x64

# 4. Build installer
"C:/Program Files (x86)/Inno Setup 6/ISCC.exe" installer/AgentX-Setup.iss

# Output: installer-output/AgentX-Setup-1.0.0-x64.exe
```

---

## 12. Troubleshooting

### 11.1 Application Fails to Start

**Symptom:** Application crashes immediately on launch with no window appearing.

**Diagnostic:** Check the Serilog log file at `%LocalAppData%\AgentX\Logs\agentx-YYYYMMDD.log`. The log is written before the window appears.

**Common causes:**

1. **Database permission error** — the `%LocalAppData%\AgentX\` directory cannot be created. Verify the user account has write access to `%LocalAppData%`.

2. **Settings file corruption** — `settings.json` contains invalid JSON. Delete `%LocalAppData%\AgentX\settings.json` and restart. The application will recreate it with defaults.

3. **Windows App SDK version mismatch** — ensure Windows App SDK 1.6.x is available. For the self-contained publish, this is bundled. For development builds, it is installed via NuGet automatically.

### 11.2 Ollama Not Detected

**Symptom:** Status bar shows "Ollama not detected" or the connection dot is red.

**Diagnostic steps:**

1. Verify Ollama is running: open a terminal and run `ollama list`. If Ollama is running, this should list installed models.
2. Verify the endpoint in Settings matches the Ollama server address (default: `http://localhost:11434`).
3. Check the log for `Ollama connection check timed out (3s)` — this indicates the Ollama HTTP server is not responding. Restart Ollama.
4. On first-time setup, ensure the `llama3.2` and `all-minilm` models are pulled: `ollama pull llama3.2 && ollama pull all-minilm`.

### 11.3 Indexing Fails

**Symptom:** Documents show "failed" status in the Knowledge Vault.

**Diagnostic:** Check the log for `Failed to index document {DocumentId}`. The `IndexingError` column in the `documents` table also stores the error message.

**Common causes:**

1. **Embedding model not available** — `EmbeddingService` fails if the configured embedding model is not pulled. Pull it: `ollama pull all-minilm`.

2. **Source file moved or deleted** — the indexing pipeline checks `File.Exists(document.FilePath)`. If the original file was moved after import, indexing will fail with `FileNotFoundException`. Re-import the file from its new location.

3. **Unsupported file format** — no `IDocumentProcessor` claims the file extension. Check `SupportedExtensions` on each processor. Add a new processor for the format if needed.

4. **PDF extraction failure** — some PDFs use encryption or exotic formats that PDFsharp cannot parse. The pipeline marks these as failed with the PDFsharp exception message.

### 11.4 Search Returns No Results

**Symptom:** Semantic or keyword search returns 0 results even for obvious queries.

**Semantic search diagnostic:**

1. Check the indexing status — documents must have `indexing_status = 'completed'` before their chunks appear in search results.
2. Verify the embedding model used for indexing matches the one used for querying. If you change `EmbeddingModel` in settings after indexing, you must re-index all documents.
3. Check `vec_embeddings` count: `SELECT COUNT(*) FROM vec_embeddings;` — if 0, embeddings were not generated.

**Keyword search diagnostic:**

1. Check FTS5 initialization in the log — look for `FTS5 keyword search initialized`.
2. Check `document_chunks_fts` count: `SELECT COUNT(*) FROM document_chunks_fts;` — if 0, FTS5 indexing did not run.
3. Try a simpler query — FTS5 supports basic boolean operators. Special characters may need to be escaped.

### 11.5 Onboarding Stuck or Not Showing

**Symptom:** The application always shows onboarding, or never shows it.

**To force onboarding:** Delete `%LocalAppData%\AgentX\settings.json`.

**To skip onboarding:** Edit `%LocalAppData%\AgentX\settings.json` and set `"onboardingCompleted": true`.

**Navigation pane stuck hidden:** If the navigation pane is not visible after onboarding completes, the `MainWindow.UpdateStatusBarAsync()` safety net should restore it on the next 30-second poll. Alternatively, restart the application.

### 11.6 Build Errors

**`The type or namespace 'WinRT' could not be found`**

Ensure the `Microsoft.WindowsAppSDK` NuGet package is restored. Run `dotnet restore` and rebuild.

**`NETSDK1179: One of assets or runtimepack must be specified`**

This occurs when targeting `net8.0-windows` without a `RuntimeIdentifier`. The self-contained publish explicitly sets `-r win-x64`. For development builds, this error should not appear if the project is opened in Visual Studio with the correct workloads.

**Inno Setup: `Source file not found`**

Run the publish step (`dotnet publish`) before building the installer. The `.iss` file sources files from `publish\win-x64\*`.

### 11.7 Log File Locations and Interpretation

Logs are written to `%LocalAppData%\AgentX\Logs\agentx-YYYYMMDD.log`. Log level is `Debug` in development.

| Log prefix | Meaning |
|---|---|
| `[DBG]` | Debug-level diagnostic information |
| `[INF]` | Normal operational events |
| `[WRN]` | Non-fatal issues (degraded functionality) |
| `[ERR]` | Errors that affect a specific operation |
| `[FTL]` | Fatal errors that crash the application |

Key startup log events to look for:

```
Agent-X logging initialized at {LogPath}
Database initialized at {Path}
FTS5 keyword search initialized
AI service initialized with {Provider} provider, model: {Model}
Agent-X started successfully
```

If any of these are missing, look at the preceding `[ERR]` or `[FTL]` lines for the root cause.

---

## 13. Code Style Guidelines

### 12.1 General Principles

- Every public type and member has an XML doc comment (`/// <summary>`)
- Private helper methods have summary comments explaining their purpose and any non-obvious behavior
- Constants have comments explaining their origin (e.g., the RRF `k = 60` constant cites the original paper)
- Magic numbers are always named constants, never inline literals

### 12.2 C# Language Conventions

**Namespace declarations:** File-scoped namespaces (`namespace Foo;`) throughout the codebase.

**Primary constructors:** Used sparingly. Constructor injection with explicit `this.field = param ?? throw new ArgumentNullException(nameof(param))` validation is preferred for service classes.

**Null handling:**
```csharp
// Argument validation in service constructors
_service = service ?? throw new ArgumentNullException(nameof(service));

// Null-conditional and null-coalescing
var name = entity?.Name ?? "Unknown";

// Null-forgiving operator only when logically certain
var result = maybeNull!.Value; // Add a comment explaining why it cannot be null
```

**Pattern matching:** Preferred over type casting and `is` checks:
```csharp
// Preferred
if (args.SelectedItemContainer is NavigationViewItem selectedItem)
{
    var tag = selectedItem.Tag?.ToString();
}

// Avoid
var selectedItem = args.SelectedItemContainer as NavigationViewItem;
if (selectedItem != null)
{
    var tag = selectedItem.Tag?.ToString();
}
```

**Switch expressions:** Used for multi-branch returns:
```csharp
var defaultModel = providerId.ToLowerInvariant() switch
{
    "openai"    => settings.OpenAiDefaultModel ?? "gpt-4o-mini",
    "anthropic" => settings.AnthropicDefaultModel ?? "claude-sonnet-4-20250514",
    _           => settings.DefaultModel
};
```

### 12.3 Async Conventions

- All async methods end in `Async` and accept `CancellationToken ct = default`
- `ConfigureAwait(false)` on every `await` in `AgentX.Core` (library code, no UI context)
- `ConfigureAwait(false)` omitted in `AgentX.App` ViewModels (must marshal to UI thread)
- `async void` is used only in WinUI event handlers and `OnNavigatedTo` overrides
- Never use `Task.Result` or `.Wait()` — use `await` or fire-and-forget with explicit error handling

### 12.4 Logging Conventions

Use structured logging with named parameters throughout:

```csharp
// Correct — named parameters create searchable structured data
_logger.Information("Indexed document {DocumentId} ({FileName}): {ChunkCount} chunks in {ElapsedMs:F0}ms",
    documentId, document.FileName, chunkEntities.Count, elapsed);

// Avoid — string interpolation creates unstructured plain text
_logger.Information($"Indexed document {documentId} ({document.FileName})");
```

Log level guidelines:

| Level | When to use |
|---|---|
| `Debug` | Per-request diagnostics, counts, paths |
| `Information` | Service lifecycle events, successful operations |
| `Warning` | Non-fatal degraded functionality, recoverable errors |
| `Error` | Operation failed, user-visible failure |
| `Fatal` | Application cannot continue |

Truncate user-generated content before logging to avoid log file bloat:

```csharp
_logger.Debug("Streaming chat: {MessagePreview}",
    userContent.Length > 50 ? userContent[..50] + "..." : userContent);
```

### 12.5 File Organization

Each file contains exactly one primary type. Closely related secondary types (item classes, enums used exclusively within a class) may be placed in the same file. The `ChatViewModel.cs` file demonstrates this with `ChatMessageItem`, `ConversationListItem`, and `SystemPromptItem` defined at the bottom of the file.

Interface and implementation files are kept adjacent. For `IMyService.cs` and `MyService.cs`, place both in the same directory.

### 12.6 XAML Conventions

- All `x:Name` attributes use PascalCase: `SearchInput`, `ResultsPanel`
- Binding paths match ViewModel property names exactly
- Resource keys in `Styles/` use PascalCase: `TextPrimaryBrush`, `AccentPrimaryBrush`
- Font icon glyphs use Unicode escape: `&#xE8BD;` in XAML or `"\uE8BD"` in code

### 12.7 Interface Design

All services expose an interface. The interface lives in the same directory as the implementation. Interface methods:

- Are defined with `Task` return types for all async operations
- Accept `CancellationToken ct = default` as the last parameter
- Use `IReadOnlyList<T>` for output collections that callers should not modify
- Use concrete types for input parameters (not interfaces) unless polymorphism is needed

### 12.8 Disposable Resources

Services that hold disposable resources (`HttpClient`, `SqliteConnection`, provider instances) implement `IDisposable`. The pattern:

```csharp
public sealed class MyService : IMyService, IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Release managed resources here

        _logger.Debug("MyService disposed");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MyService));
    }
}
```

Services registered as Singletons are disposed by the `IHost` when the application shuts down.

---

*This developer guide reflects the Agent-X codebase as of version 1.0.0 (February 2026). For architecture decisions and high-level system design, refer to `docs/ARCHITECTURE.md`. For the public API reference, refer to `docs/API-REFERENCE.md`.*
