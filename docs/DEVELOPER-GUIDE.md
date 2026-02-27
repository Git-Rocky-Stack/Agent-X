# Agent-X Developer Guide

Version 1.0.0 | Last updated: February 2026

---

## Table of Contents

1. [Overview](#overview)
2. [Prerequisites](#prerequisites)
3. [Solution Architecture](#solution-architecture)
4. [Getting Started](#getting-started)
5. [Project Structure](#project-structure)
6. [Coding Standards](#coding-standards)
7. [MVVM Pattern](#mvvm-pattern)
8. [Dependency Injection](#dependency-injection)
9. [Data Layer](#data-layer)
10. [AI Integration](#ai-integration)
11. [Document Processing Pipeline](#document-processing-pipeline)
12. [Search and RAG Pipeline](#search-and-rag-pipeline)
13. [XAML and UI Conventions](#xaml-and-ui-conventions)
14. [Error Handling and Logging](#error-handling-and-logging)
15. [Testing](#testing)
16. [Build Pipeline](#build-pipeline)
17. [Adding a New Feature](#adding-a-new-feature)
18. [Adding a New Document Processor](#adding-a-new-document-processor)
19. [Adding a New Page](#adding-a-new-page)
20. [Adding a New Converter](#adding-a-new-converter)
21. [Database Migrations](#database-migrations)
22. [Keyboard Shortcuts](#keyboard-shortcuts)
23. [Installer and Distribution](#installer-and-distribution)
24. [Code Review Checklist](#code-review-checklist)
25. [Troubleshooting](#troubleshooting)

---

## Overview

Agent-X is a local-first AI intelligence hub built as a Windows desktop application. It enables users to import documents, build a personal knowledge base, and interact with locally-running AI models via Ollama. The application supports semantic search, retrieval-augmented generation (RAG), and intelligent document organization -- all without sending data to external cloud services.

**Key capabilities:**

- Chat with local AI models (Ollama integration)
- Import and process documents (PDF, DOCX, TXT, Markdown, code files, images)
- Semantic search across an indexed knowledge vault
- RAG-powered question answering with source citations
- Automatic document tagging, summarization, and duplicate detection
- Folder watching for automatic ingestion
- Hardware detection and model recommendations

**Tech stack:**

| Layer | Technology |
|-------|------------|
| Framework | .NET 8 (net8.0-windows10.0.22621.0) |
| UI | WinUI 3 (Windows App SDK 1.6) |
| Architecture | MVVM (CommunityToolkit.Mvvm 8.2.2) |
| Database | SQLite via Entity Framework Core 8.0.11 |
| Vector Store | sqlite-vec extension |
| AI Runtime | Ollama via OllamaSharp 4.0.12 |
| AI Abstractions | Microsoft.Extensions.AI 9.5.0 |
| Document Processing | PDFsharp 6.1.1, DocumentFormat.OpenXml 3.2.0, Markdig 0.37.0 |
| Logging | Serilog 4.0.2 (file sink with daily rolling) |
| Testing | xUnit 2.9.2, Moq 4.20.72, FluentAssertions 6.12.2 |
| Installer | Inno Setup 6 |
| Language Version | C# 12 |

---

## Prerequisites

Before contributing to Agent-X, ensure you have the following installed:

1. **Visual Studio 2022** (17.0+) with the following workloads:
   - .NET Desktop Development
   - Windows Application Development (WinUI 3 / Windows App SDK)

2. **.NET 8 SDK** (8.0.x)

3. **Windows App SDK 1.6** runtime (installed automatically with the VS workload)

4. **Ollama** (for running local AI models during development and testing):
   - Download from [https://ollama.ai](https://ollama.ai)
   - Pull a model: `ollama pull llama3.2` (or any supported model)
   - Ollama must be running on `http://localhost:11434` during development

5. **Inno Setup 6** (optional, only needed for building the installer):
   - Install to `C:\Program Files (x86)\Inno Setup 6\`

6. **Git** for version control

---

## Solution Architecture

Agent-X follows a clean two-layer architecture with strict dependency flow:

```
AgentX.App  --->  AgentX.Core
   (UI)              (Business Logic)
```

### Dependency Rules

- `AgentX.Core` has **zero UI dependencies**. It must never reference WinUI, Windows App SDK, or any UI framework.
- `AgentX.App` references `AgentX.Core` and contains all UI-specific code: views, view models, converters, styles, and app-level services.
- `AgentX.Tests` references only `AgentX.Core`. Tests target the business logic layer exclusively.

### Solution File

```
AgentX.sln
  src/
    AgentX.App       (WinUI 3 executable)
    AgentX.Core      (Class library)
  tests/
    AgentX.Tests     (xUnit test project)
```

### Platform Targets

The solution supports three platform configurations: `x86`, `x64`, and `ARM64`. The `Any CPU` configuration maps to `x86` by default in the solution configuration. For development, target `x64` unless testing ARM64 compatibility.

### Global Build Properties

The `Directory.Build.props` file at the solution root applies the following settings to all projects:

| Property | Value | Purpose |
|----------|-------|---------|
| `LangVersion` | 12.0 | C# 12 features (primary constructors, collection expressions, etc.) |
| `Nullable` | enable | Nullable reference types enforced across all projects |
| `ImplicitUsings` | enable | Common `using` directives auto-imported |
| `EnforceCodeStyleInBuild` | true | Code style violations produce build warnings |
| `Company` | Rocky Stack | Assembly metadata |
| `Product` | Agent-X | Assembly metadata |
| `Version` | 1.0.0 | Assembly version (update on release) |

---

## Getting Started

### Clone and Build

```bash
git clone <repository-url>
cd Agent-X

# Restore NuGet packages
dotnet restore AgentX.sln

# Build the entire solution
dotnet build AgentX.sln -c Debug

# Run the application
dotnet run --project src/AgentX.App/AgentX.App.csproj

# Run tests
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj
```

### Visual Studio Workflow

1. Open `AgentX.sln` in Visual Studio 2022.
2. Set `AgentX.App` as the startup project.
3. Select `x64` as the solution platform (recommended for development).
4. Press `F5` to build and run with the debugger.

### First Run

On first launch, the application navigates to the **Onboarding** wizard. This guides the user through initial setup (Ollama connection check, model selection, first document import). The onboarding state is persisted in `AppSettings.OnboardingCompleted`.

---

## Project Structure

### AgentX.App (UI Layer)

```
src/AgentX.App/
  App.xaml.cs                   # Application entry point, DI container, logging, exception handling
  MainWindow.xaml/.cs           # Navigation shell, keyboard shortcuts, status bar, command palette
  Views/                        # 11 XAML pages with code-behind
    DashboardPage.xaml/.cs
    ChatPage.xaml/.cs
    AskFilesPage.xaml/.cs
    QuickActionsPage.xaml/.cs
    KnowledgeVaultPage.xaml/.cs
    CollectionManagerPage.xaml/.cs
    SearchPage.xaml/.cs
    ModelManagerPage.xaml/.cs
    HardwareAdvisorPage.xaml/.cs
    SettingsPage.xaml/.cs
    OnboardingPage.xaml/.cs
  ViewModels/                   # 11 ViewModels (one per page)
    DashboardViewModel.cs
    ChatViewModel.cs
    AskFilesViewModel.cs
    QuickActionsViewModel.cs
    KnowledgeVaultViewModel.cs
    CollectionManagerViewModel.cs
    SearchViewModel.cs
    ModelManagerViewModel.cs
    HardwareAdvisorViewModel.cs
    SettingsViewModel.cs
    OnboardingViewModel.cs
  Converters/                   # 11 IValueConverter implementations
    BoolToVisibilityConverter.cs
    BoolToOpacityConverter.cs
    BytesToStringConverter.cs
    InverseBoolConverter.cs
    NullToVisibilityConverter.cs
    PercentToWidthConverter.cs
    StatusToColorConverter.cs
    StringToVisibilityConverter.cs
    TimeAgoConverter.cs
    TokensToStringConverter.cs
    ZeroToVisibleConverter.cs
  Controls/                     # Custom reusable controls
    CommandPalette              # Global command palette (Ctrl+K)
  Styles/                       # 6 XAML resource dictionaries
    Chat.xaml                   # Chat-specific styles
    Colors.xaml                 # Color palette and brush resources
    Controls.xaml               # Control template overrides
    Documents.xaml              # Document display styles
    Navigation.xaml             # NavigationView styles
    Typography.xaml             # Font sizes, weights, text styles
  Services/                     # App-level services (UI-aware)
    KeyboardShortcutService.cs  # Global keyboard shortcut registry
  Helpers/                      # UI helper utilities
  Assets/                       # Images, icons, brand assets
  Properties/                   # Launch settings
```

### AgentX.Core (Business Logic Layer)

```
src/AgentX.Core/
  AI/                           # AI provider abstraction layer
    IAiService.cs               # High-level AI orchestration interface
    AiService.cs                # AI service implementation
    IAiProvider.cs              # Provider abstraction (Ollama, future providers)
    IModelManager.cs            # Model listing, pulling, deletion
    ModelManager.cs
    IHardwareDetector.cs        # GPU/RAM/NPU detection
    HardwareDetector.cs
    IEmbeddingService.cs        # Text-to-vector embedding
    EmbeddingService.cs
    Models/
      AiModel.cs                # Model metadata (name, size, capabilities)
      ChatOptions.cs            # Inference parameters (temperature, top_p, etc.)
    Providers/
      OllamaProvider.cs         # Ollama HTTP client via OllamaSharp
  Data/
    AgentXDbContext.cs           # EF Core DbContext with 14 DbSets
    Entities/                    # 14 entity classes
      ConversationEntity.cs
      MessageEntity.cs
      DocumentEntity.cs
      DocumentChunkEntity.cs
      CollectionEntity.cs
      DocumentCollectionEntity.cs
      TagEntity.cs
      DocumentTagEntity.cs
      SearchHistoryEntity.cs
      SystemPromptEntity.cs
      UserSettingsEntity.cs
      WatchFolderEntity.cs
      IndexingJobEntity.cs
      LicenseEntity.cs
    Migrations/                  # EF Core migrations (if used)
    VectorDb/
      IVectorStore.cs            # Vector storage abstraction
      SqliteVecStore.cs          # sqlite-vec implementation
      VectorSearchResult.cs      # Search result with distance score
  Documents/
    IDocumentService.cs          # Document CRUD and metadata
    DocumentService.cs
    IDocumentProcessor.cs        # File type extraction interface
    IChunkingService.cs          # Text chunking for embedding
    ChunkingService.cs
    Models/
      ProcessedDocument.cs       # Extracted text + metadata
    Processors/                  # 6 file type processors
      PdfProcessor.cs
      DocxProcessor.cs
      TextProcessor.cs
      MarkdownProcessor.cs
      CodeFileProcessor.cs
      ImageProcessor.cs
  Search/
    ISemanticSearchService.cs    # Vector similarity search
    SemanticSearchService.cs
    IRagPipeline.cs              # Full RAG orchestration
    RagPipeline.cs
    ICitationService.cs          # Source citation generation
    CitationService.cs
    Models/
      SearchQuery.cs             # Search parameters
      SearchResult.cs            # Ranked result with relevance score
      RagResponse.cs             # AI response with citations
      Citation.cs                # Source reference
  Services/
    Chat/
      IChatService.cs            # Chat message sending/streaming
      ChatService.cs
      IConversationService.cs    # Conversation CRUD
      ConversationService.cs
      ISystemPromptService.cs    # System prompt management
      SystemPromptService.cs
    Collections/
      ICollectionService.cs      # Collection CRUD, document assignment
      CollectionService.cs
    Indexing/
      IIndexingService.cs        # Document indexing orchestration
      IndexingService.cs
      IIndexingQueueService.cs   # Background indexing queue
      IndexingQueueService.cs
      IFileWatcherService.cs     # Folder monitoring for auto-import
      FileWatcherService.cs
    Intelligence/
      ISummaryService.cs         # AI-powered document summarization
      SummaryService.cs
      IDuplicateDetectionService.cs  # Content hash deduplication
      DuplicateDetectionService.cs
      IOrganizationSuggestionService.cs  # AI-suggested organization
      OrganizationSuggestionService.cs
      Models/
        IntelligenceModels.cs    # Shared models for intelligence services
    License/
      ILicenseService.cs         # License key validation
      LicenseService.cs
      LicenseInfo.cs             # License metadata
      LicenseTier.cs             # Tier enum (Starter, Pro, etc.)
    Settings/
      ISettingsService.cs        # Application settings CRUD
      SettingsService.cs
      AppSettings.cs             # Settings model
    Tagging/
      IAutoTagService.cs         # AI-powered auto-tagging
      AutoTagService.cs
  Helpers/
    FileTypeHelper.cs            # File extension/MIME type utilities
    FormatHelper.cs              # Display formatting (bytes, time, etc.)
    HashHelper.cs                # SHA256 content hashing
    PathHelper.cs                # Path normalization and validation
```

### AgentX.Tests

```
tests/AgentX.Tests/
  AI/                            # Tests for AI services
  Data/                          # Tests for DbContext and entities
  Documents/                     # Tests for document processing pipeline
  Helpers/                       # Tests for helper utilities
  Search/                        # Tests for search and RAG pipeline
  Services/                      # Tests for all business services
```

---

## Coding Standards

### Language and Formatting

- **C# 12** features are encouraged: primary constructors, collection expressions, pattern matching, file-scoped namespaces.
- **Nullable reference types** are enabled globally. Every reference type must be annotated correctly. Use `string?` for nullable strings, never leave `null` warnings unresolved.
- **File-scoped namespaces** are the standard: `namespace AgentX.Core.AI;` (not the block-scoped form).
- **One type per file** as a general rule. The exception is small display item classes that are tightly coupled to a specific ViewModel (e.g., `DashboardRecentDocumentItem` inside `DashboardViewModel.cs`).
- **Implicit usings** are enabled. Do not add explicit `using System;` or other auto-imported namespaces.
- **XML documentation comments** are required on all public interfaces, interface methods, and non-trivial public classes. Use `<summary>`, `<param>`, and `<returns>` tags.

### Naming Conventions

| Element | Convention | Example |
|---------|-----------|---------|
| Interfaces | `I` + PascalCase + `Service`/`Store`/etc. | `IAiService`, `IVectorStore` |
| Service implementations | PascalCase matching interface | `AiService`, `SqliteVecStore` |
| Entities | PascalCase + `Entity` suffix | `DocumentEntity`, `ConversationEntity` |
| ViewModels | PascalCase + `ViewModel` suffix | `DashboardViewModel`, `ChatViewModel` |
| Pages | PascalCase + `Page` suffix | `DashboardPage`, `ChatPage` |
| Converters | Descriptive + `Converter` suffix | `BoolToVisibilityConverter` |
| Display items | Context + `Item` suffix | `DashboardRecentDocumentItem` |
| Private fields | `_camelCase` with underscore prefix | `_aiService`, `_isLoading` |
| Observable properties | `_camelCase` (source generator creates `PascalCase` public property) | `[ObservableProperty] private bool _isLoading;` |
| Async methods | PascalCase + `Async` suffix | `InitializeAsync`, `LoadDataAsync` |
| Constants | PascalCase | `MaxRetryCount`, `DefaultTimeout` |
| Namespaces | `AgentX.{Layer}.{Feature}` | `AgentX.Core.Services.Chat` |

### Async Patterns

All service methods that perform I/O (database queries, HTTP calls, file operations) must be asynchronous:

```csharp
// CORRECT: Async method with CancellationToken
public async Task<DocumentEntity?> GetDocumentAsync(long id, CancellationToken ct = default)
{
    return await _dbContext.Documents
        .FirstOrDefaultAsync(d => d.Id == id, ct)
        .ConfigureAwait(false);
}

// CORRECT: ConfigureAwait(false) in Core library code
// This is required in AgentX.Core to avoid deadlocks and improve performance.
// AgentX.App code does NOT use ConfigureAwait(false) because it needs the UI synchronization context.

// CORRECT: async void ONLY for event handlers
private async void OnLoaded(object sender, RoutedEventArgs e)
{
    await ViewModel.InitializeAsync();
}

// CORRECT: Parallel initialization with Task.WhenAll
public async Task InitializeAsync()
{
    await Task.WhenAll(
        LoadAiStatusAsync(),
        LoadVaultStatsAsync(),
        LoadSystemInfoAsync());
}

// WRONG: Blocking on async code
public void LoadData()
{
    var result = GetDataAsync().Result; // NEVER DO THIS -- causes deadlocks
}
```

### Method Organization

Within a class, organize members in this order:

1. Constants and static fields
2. Private readonly fields (injected dependencies)
3. Observable properties (`[ObservableProperty]`)
4. Public properties
5. Constructor
6. Public methods / Commands (`[RelayCommand]`)
7. Private methods
8. Nested types (display items, enums)
9. `IDisposable` implementation

Use section comment headers to delineate logical groups within large classes:

```csharp
// == Services =====================================================
private readonly IAiService _aiService;
private readonly IDocumentService _documentService;

// == Observable Properties ========================================
[ObservableProperty] private bool _isLoading;
[ObservableProperty] private string _statusMessage = string.Empty;

// == Commands =====================================================
[RelayCommand]
private async Task RefreshAsync() { ... }
```

---

## MVVM Pattern

Agent-X uses the **CommunityToolkit.Mvvm** (version 8.2.2) source generator-based MVVM pattern.

### ViewModel Structure

Every ViewModel inherits from `ObservableObject` and follows this structure:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AgentX.App.ViewModels;

public partial class ExampleViewModel : ObservableObject
{
    // Dependencies injected via constructor
    private readonly IExampleService _exampleService;
    private readonly IAiService _aiService;

    // Observable properties -- the source generator creates public
    // PascalCase properties with change notification.
    // Field: _isLoading  -->  Generated property: IsLoading
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private ObservableCollection<ExampleItem> _items = new();

    // Constructor injection (all dependencies provided by DI container)
    public ExampleViewModel(IExampleService exampleService, IAiService aiService)
    {
        _exampleService = exampleService;
        _aiService = aiService;
        Log.Debug("ExampleViewModel created");
    }

    // Async initialization -- called from the Page's Loaded event
    public async Task InitializeAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var data = await _exampleService.GetAllAsync();
            Items = new ObservableCollection<ExampleItem>(data);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize ExampleViewModel");
            ErrorMessage = "Failed to load data. Please try again.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Commands -- the source generator creates an ICommand property.
    // Method: RefreshAsync  -->  Generated command: RefreshCommand
    [RelayCommand]
    private async Task RefreshAsync()
    {
        Log.Debug("Refresh requested");
        await InitializeAsync();
    }

    // Synchronous commands also work
    [RelayCommand]
    private void NavigateToSettings()
    {
        NavigateRequested?.Invoke("Settings");
    }

    // Navigation callback -- set by the Page code-behind
    public Action<string>? NavigateRequested { get; set; }
}
```

### Source Generator Rules

The `[ObservableProperty]` and `[RelayCommand]` attributes leverage source generators. Important rules:

1. **The ViewModel class must be `partial`**. The source generator creates a companion partial class with the generated properties and commands.

2. **Observable property naming**: The private field `_fieldName` generates a public property `FieldName` (drops the underscore, capitalizes the first letter). Do not create conflicting public properties manually.

3. **RelayCommand naming**: The method `DoSomethingAsync()` generates a command property `DoSomethingCommand`. The method `DoSomething()` (without Async suffix) also generates `DoSomethingCommand`.

4. **Async commands**: When the decorated method returns `Task`, the generated command automatically manages `CanExecute` state to prevent concurrent execution. No manual `IsLoading` guard is needed for the command itself, though you may still want `IsLoading` for UI binding.

### View-ViewModel Connection

Each Page resolves its ViewModel from the DI container in the constructor:

```csharp
namespace AgentX.App.Views;

public sealed partial class ExamplePage : Page
{
    public ExampleViewModel ViewModel { get; }

    public ExamplePage()
    {
        ViewModel = App.GetService<ExampleViewModel>();
        ViewModel.NavigateRequested = NavigateToPage;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private void NavigateToPage(string pageTag)
    {
        if (App.MainWindow is MainWindow mainWindow)
        {
            mainWindow.NavigateToPage(pageTag);
        }
    }
}
```

Key points:

- The ViewModel is a **public property** named `ViewModel` so XAML `x:Bind` expressions can reference it.
- `InitializeAsync()` is called from the `Loaded` event, not the constructor. This ensures the visual tree is ready before async operations begin.
- Navigation is delegated through the `NavigateRequested` callback to `MainWindow.NavigateToPage()`, which handles both frame navigation and NavigationView selection sync.

### Display Item Classes

For `DataTemplate` bindings in WinUI 3, display item classes must be accessible at the namespace level (not nested within the ViewModel). The convention is to place them at the bottom of the ViewModel file as top-level classes:

```csharp
// At the bottom of ExampleViewModel.cs, outside the ViewModel class:

/// <summary>
/// Display model for a single item in the example list.
/// </summary>
public class ExampleDisplayItem
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string FormattedDate { get; init; } = string.Empty;
}
```

Use `init` properties for immutable display items. Use a context-specific prefix to avoid naming collisions (e.g., `DashboardRecentDocumentItem`, not `DocumentItem`).

---

## Dependency Injection

All dependency injection is configured in `App.xaml.cs` within the `ConfigureServices` method. The application uses `Microsoft.Extensions.DependencyInjection` via `Microsoft.Extensions.Hosting`.

### Service Lifetimes

| Lifetime | Used For | Rationale |
|----------|----------|-----------|
| **Singleton** | All services, DbContext, loggers | Services maintain state (caches, connections). DbContext uses SQLite which benefits from a single connection. |
| **Transient** | ViewModels, Pages | Each page navigation gets a fresh ViewModel instance to avoid stale state. |

### Registration Order

Services are registered in dependency order within `ConfigureServices`:

```csharp
private void ConfigureServices(HostBuilderContext context, IServiceCollection services)
{
    // 1. Infrastructure (logging, database)
    services.AddSingleton<Serilog.ILogger>(_ => Log.Logger);
    services.AddSingleton<AgentXDbContext>();

    // 2. Core services (settings, license)
    services.AddSingleton<ISettingsService, SettingsService>();
    services.AddSingleton<ILicenseService, LicenseService>();

    // 3. App-level services (UI-aware)
    services.AddSingleton<KeyboardShortcutService>();

    // 4. AI services
    services.AddSingleton<IAiService, AiService>();
    services.AddSingleton<IModelManager, ModelManager>();
    services.AddSingleton<IHardwareDetector, HardwareDetector>();
    services.AddSingleton<IEmbeddingService, EmbeddingService>();

    // 5. Vector store
    services.AddSingleton<IVectorStore, SqliteVecStore>();

    // 6. Chat services
    services.AddSingleton<IConversationService, ConversationService>();
    services.AddSingleton<ISystemPromptService, SystemPromptService>();
    services.AddSingleton<IChatService, ChatService>();

    // 7. Document processors (multiple implementations of IDocumentProcessor)
    services.AddSingleton<IDocumentProcessor, PdfProcessor>();
    services.AddSingleton<IDocumentProcessor, DocxProcessor>();
    services.AddSingleton<IDocumentProcessor, TextProcessor>();
    services.AddSingleton<IDocumentProcessor, MarkdownProcessor>();
    services.AddSingleton<IDocumentProcessor, CodeFileProcessor>();
    services.AddSingleton<IDocumentProcessor, ImageProcessor>();

    // 8. Document services
    services.AddSingleton<IDocumentService, DocumentService>();
    services.AddSingleton<IChunkingService, ChunkingService>();

    // 9. Indexing pipeline
    services.AddSingleton<IIndexingQueueService, IndexingQueueService>();
    services.AddSingleton<IIndexingService, IndexingService>();
    services.AddSingleton<IFileWatcherService, FileWatcherService>();

    // 10. Collections and tagging
    services.AddSingleton<ICollectionService, CollectionService>();
    services.AddSingleton<IAutoTagService, AutoTagService>();

    // 11. Search and RAG
    services.AddSingleton<ISemanticSearchService, SemanticSearchService>();
    services.AddSingleton<ICitationService, CitationService>();
    services.AddSingleton<IRagPipeline, RagPipeline>();

    // 12. Intelligence services
    services.AddSingleton<ISummaryService, SummaryService>();
    services.AddSingleton<IDuplicateDetectionService, DuplicateDetectionService>();
    services.AddSingleton<IOrganizationSuggestionService, OrganizationSuggestionService>();

    // 13. ViewModels (Transient)
    services.AddTransient<ViewModels.DashboardViewModel>();
    // ... all other ViewModels

    // 14. Views (Transient)
    services.AddTransient<Views.DashboardPage>();
    // ... all other Pages
}
```

### Service Resolution

Services are resolved using the static helper on `App`:

```csharp
// In Page code-behind or MainWindow
var viewModel = App.GetService<DashboardViewModel>();
var aiService = App.GetService<IAiService>();

// In services (prefer constructor injection)
public class ChatService : IChatService
{
    private readonly IAiService _aiService;
    private readonly IConversationService _conversationService;

    public ChatService(IAiService aiService, IConversationService conversationService)
    {
        _aiService = aiService;
        _conversationService = conversationService;
    }
}
```

**Rule**: Always prefer constructor injection over service locator (`App.GetService<T>`). The service locator pattern is acceptable only in Page code-behind constructors and `MainWindow`, where constructor injection is not practical due to WinUI 3 initialization constraints.

---

## Data Layer

### Entity Framework Core

The application uses EF Core 8.0.11 with the SQLite provider. The database file is stored at:

```
%LOCALAPPDATA%\AgentX\agentx.db
```

### DbContext

`AgentXDbContext` exposes 14 `DbSet` properties. All entity configurations are defined in the fluent API within `OnModelCreating` via private `Configure*` methods:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    ConfigureConversation(modelBuilder);
    ConfigureMessage(modelBuilder);
    ConfigureDocument(modelBuilder);
    // ... etc.
}
```

### Entity Design Rules

1. **Primary keys**: Use `long Id` for all entities. EF Core auto-generates incrementing IDs for SQLite.

2. **Table names**: Always specify explicit table names in lowercase with underscores (e.g., `entity.ToTable("document_chunks")`).

3. **Required properties**: Mark all non-nullable columns with `.IsRequired()` in the fluent configuration.

4. **Indexes**: Create indexes on columns frequently used in `WHERE` clauses, `ORDER BY`, and foreign keys.

5. **Relationships**: Configure relationships with explicit foreign keys and `OnDelete` behavior:
   - `Cascade` for parent-child (e.g., Document -> Chunks)
   - `Restrict` for self-referencing (e.g., Collection -> ParentCollection)
   - `SetNull` for optional associations (e.g., WatchFolder -> TargetCollection)

6. **Composite keys**: Use `entity.HasKey(e => new { e.DocumentId, e.CollectionId })` for join tables.

7. **Default values**: Specify defaults for status columns (e.g., `.HasDefaultValue("pending")`).

8. **Navigation properties**: Always include both sides of a relationship. Use `ICollection<T>` for collection navigation properties:

```csharp
public class DocumentEntity
{
    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    // ... other properties

    // Navigation properties
    public ICollection<DocumentChunkEntity> Chunks { get; set; } = new List<DocumentChunkEntity>();
    public ICollection<DocumentCollectionEntity> DocumentCollections { get; set; } = new List<DocumentCollectionEntity>();
    public ICollection<DocumentTagEntity> DocumentTags { get; set; } = new List<DocumentTagEntity>();
}
```

### Database Initialization

The database schema is created on application startup via `EnsureCreatedAsync()` in `App.xaml.cs`:

```csharp
var dbContext = GetService<AgentXDbContext>();
await dbContext.Database.EnsureCreatedAsync();
```

This runs as part of `InitializeCoreServicesAsync()`, which is fire-and-forget to avoid blocking the window from appearing.

---

## AI Integration

### Provider Abstraction

AI functionality is abstracted behind two interfaces:

- **`IAiProvider`**: Low-level provider interface (connection checking, model listing, chat completion, embedding). Currently implemented by `OllamaProvider`.
- **`IAiService`**: High-level orchestration service that wraps the active provider and adds application-specific methods (summarization, tag generation).

### Adding a New AI Provider

1. Implement `IAiProvider` in `AgentX.Core/AI/Providers/`:

```csharp
public class NewProvider : IAiProvider
{
    // Implement all interface methods
}
```

2. Register the provider in `App.xaml.cs` and update `AiService` to support provider switching.

### Ollama Integration

The `OllamaProvider` uses the `OllamaSharp` library (v4.0.12) to communicate with the local Ollama HTTP API at `http://localhost:11434`. Key operations:

- **Connection check**: `GET /api/tags`
- **Chat completion**: `POST /api/chat` (streaming)
- **Embedding**: `POST /api/embed`
- **Model management**: `POST /api/pull`, `DELETE /api/delete`

### Hardware Detection

`HardwareDetector` uses `System.Management` (WMI) to detect:

- GPU name and VRAM
- Total and available RAM
- NPU presence

This information is used by the `HardwareAdvisorViewModel` to recommend appropriate models.

---

## Document Processing Pipeline

The document processing pipeline consists of three stages:

### Stage 1: Extraction (IDocumentProcessor)

Each file format has a dedicated processor that implements `IDocumentProcessor`:

```csharp
public interface IDocumentProcessor
{
    IReadOnlySet<string> SupportedExtensions { get; }
    bool CanProcess(string filePath);
    Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default);
}
```

| Processor | Supported Extensions | Library |
|-----------|---------------------|---------|
| `PdfProcessor` | .pdf | PDFsharp 6.1.1 |
| `DocxProcessor` | .docx, .doc | DocumentFormat.OpenXml 3.2.0 |
| `TextProcessor` | .txt, .csv, .tsv, .log | Built-in |
| `MarkdownProcessor` | .md, .markdown | Markdig 0.37.0 |
| `CodeFileProcessor` | .cs, .py, .js, .ts, .java, .cpp, .go, etc. | Built-in |
| `ImageProcessor` | .png, .jpg, .jpeg, .gif, .bmp | Built-in (metadata only) |

`DocumentService` auto-discovers all registered `IDocumentProcessor` implementations from the DI container and routes files to the appropriate processor based on extension matching.

### Stage 2: Chunking (IChunkingService)

After extraction, the text content is split into overlapping chunks suitable for embedding:

```csharp
public interface IChunkingService
{
    Task<IReadOnlyList<DocumentChunk>> ChunkAsync(
        ProcessedDocument document,
        CancellationToken ct = default);
}
```

### Stage 3: Embedding and Indexing (IIndexingService)

Each chunk is embedded into a vector via `IEmbeddingService` and stored in the `IVectorStore` (sqlite-vec). The indexing pipeline is managed by `IIndexingQueueService` for background processing.

---

## Search and RAG Pipeline

### Semantic Search

`SemanticSearchService` performs vector similarity search:

1. Embed the user's query via `IEmbeddingService`.
2. Search the `IVectorStore` for the nearest vectors.
3. Retrieve the corresponding `DocumentChunkEntity` records.
4. Return ranked `SearchResult` objects with relevance scores.

### RAG Pipeline

`RagPipeline` orchestrates retrieval-augmented generation:

1. Perform semantic search to find relevant chunks.
2. Build a context prompt from the top-K chunks.
3. Send the context + user question to the AI model via `IAiService`.
4. Generate citations via `ICitationService`.
5. Return a `RagResponse` with the AI answer and source citations.

---

## XAML and UI Conventions

### Data Binding

- **Always use `x:Bind`** (compile-time checked) instead of `{Binding}` (runtime-resolved). This catches binding errors at build time and improves performance.

```xml
<!-- CORRECT: Compile-time binding -->
<TextBlock Text="{x:Bind ViewModel.ActiveModelName, Mode=OneWay}" />

<!-- WRONG: Runtime binding (avoid in this project) -->
<TextBlock Text="{Binding ActiveModelName}" />
```

- Use `Mode=OneWay` for properties that change at runtime. `Mode=OneTime` (the default for `x:Bind`) is only appropriate for truly static values.

### Resource References

- Use `StaticResource` for styles, brushes, and other resources defined in resource dictionaries:

```xml
<TextBlock Style="{StaticResource SubtitleTextBlockStyle}"
           Foreground="{StaticResource TextSecondaryBrush}" />
```

### Converters

Declare converters in the Page's `<Page.Resources>` section:

```xml
<Page.Resources>
    <converters:BoolToVisibilityConverter x:Key="BoolToVis" />
    <converters:BoolToVisibilityConverter x:Key="InverseBoolToVis" IsInverted="True" />
</Page.Resources>
```

When creating a new converter, implement `IValueConverter` and include XML documentation. All converters reside in `AgentX.App/Converters/`. The naming convention is `{InputType}To{OutputType}Converter`.

### Style Resource Dictionaries

The six style files in `Styles/` are merged into `App.xaml` and available globally:

| File | Purpose |
|------|---------|
| `Colors.xaml` | Brand colors, semantic brushes (OnlineBrush, OfflineBrush, TextSecondaryBrush, etc.) |
| `Typography.xaml` | Font sizes, weights, and text block styles |
| `Navigation.xaml` | NavigationView and navigation item styles |
| `Controls.xaml` | Button, TextBox, and other control template overrides |
| `Chat.xaml` | Chat bubble, message list, and input area styles |
| `Documents.xaml` | Document card, file type icon, and list item styles |

### Page Navigation

Navigation is handled by `MainWindow` via two dictionaries:

- `_pageMap`: Maps string tags to Page types for frame navigation.
- `_navItemMap`: Maps string tags to `NavigationViewItem` controls for selection sync.

To navigate programmatically from a ViewModel, invoke the `NavigateRequested` callback:

```csharp
NavigateRequested?.Invoke("Chat");
```

### Window Configuration

- **Default size**: 1440 x 900 pixels, centered on the primary display.
- **Backdrop**: Mica Alt (preferred), Desktop Acrylic (fallback), solid color (last resort).
- **Title bar**: Custom dark theme with transparent button backgrounds and extended content.
- **Title**: "Agent-X -- Intelligence Hub"

---

## Error Handling and Logging

### Three-Tier Exception Handling

`App.xaml.cs` configures three levels of exception capture:

1. **`AppDomain.CurrentDomain.UnhandledException`**: Catches unhandled exceptions on any thread. Logs at `Fatal` level and flushes the log.

2. **`TaskScheduler.UnobservedTaskException`**: Catches unobserved exceptions from fire-and-forget tasks. Logs at `Error` level and marks the exception as observed to prevent app termination.

3. **`Application.UnhandledException`**: Catches unhandled exceptions on the UI thread. Logs at `Fatal` level and sets `e.Handled = true` to prevent immediate crash.

### Logging with Serilog

**Configuration:**

- Log files are written to `%LOCALAPPDATA%\AgentX\Logs\agentx-{date}.log`
- Rolling interval: daily (one file per day)
- Retention: 7 days (older files are automatically deleted)
- Debug sink: also writes to the Visual Studio Output window
- Output template: `{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}`

**Usage guidelines:**

```csharp
using Serilog;

// Use Log.ForContext<T>() for structured context in services
private readonly ILogger _log = Log.ForContext<DocumentService>();

// Log levels:
Log.Debug("Detailed diagnostic info for development only");
Log.Information("Significant operational events: {Event}", "DocumentImported");
Log.Warning(ex, "Recoverable issue: AI service unavailable, will retry");
Log.Error(ex, "Operation failed: {Operation} for document {DocId}", "IndexDocument", docId);
Log.Fatal(ex, "Unrecoverable crash in {Component}", "AppDomain");
```

**Log level selection:**

| Level | When to Use | Example |
|-------|-------------|---------|
| `Debug` | Detailed diagnostic data, method entry/exit, variable values | "ViewModel created", "Query returned {Count} results" |
| `Information` | Significant operational milestones | "Application started", "Document imported", "Indexing completed" |
| `Warning` | Recoverable issues, degraded functionality | "Ollama not detected", "Onboarding page failed to load" |
| `Error` | Failed operations that should have succeeded | "Failed to save document", "Database query threw exception" |
| `Fatal` | Unrecoverable crashes, application shutdown | "AppDomain unhandled exception", "Critical service failed" |

### Service-Level Error Handling

Every service method should catch exceptions, log them, and either return a sensible default or re-throw with additional context:

```csharp
public async Task<int> GetDocumentCountAsync(CancellationToken ct = default)
{
    try
    {
        return await _dbContext.Documents.CountAsync(ct).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to get document count");
        return 0; // Graceful degradation
    }
}
```

### ViewModel-Level Error Handling

ViewModels catch exceptions from service calls and set error properties for UI display:

```csharp
[RelayCommand]
private async Task LoadDataAsync()
{
    IsLoading = true;
    ErrorMessage = string.Empty;

    try
    {
        var data = await _service.GetDataAsync();
        Items = new ObservableCollection<ItemModel>(data);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to load data in {ViewModel}", nameof(ExampleViewModel));
        ErrorMessage = "Unable to load data. Please check your connection and try again.";
    }
    finally
    {
        IsLoading = false;
    }
}
```

---

## Testing

### Framework and Libraries

| Package | Version | Purpose |
|---------|---------|---------|
| xUnit | 2.9.2 | Test framework |
| xunit.runner.visualstudio | 2.8.2 | VS Test Explorer integration |
| Microsoft.NET.Test.Sdk | 17.12.0 | Test host |
| Moq | 4.20.72 | Mocking framework |
| FluentAssertions | 6.12.2 | Assertion library |
| coverlet.collector | 6.0.2 | Code coverage collection |

### Test Project Structure

Tests mirror the `AgentX.Core` structure:

```
tests/AgentX.Tests/
  AI/              # AiService, ModelManager, EmbeddingService tests
  Data/            # DbContext, entity configuration tests
  Documents/       # DocumentService, ChunkingService, processor tests
  Helpers/         # FileTypeHelper, HashHelper, PathHelper tests
  Search/          # SemanticSearchService, RagPipeline, CitationService tests
  Services/        # Chat, Collections, Indexing, Intelligence, etc.
```

### Test Naming Convention

Use the pattern: `MethodName_Scenario_ExpectedResult`

```csharp
[Fact]
public async Task GetDocumentAsync_ExistingId_ReturnsDocument()
{
    // Arrange
    var mockRepo = new Mock<IDocumentService>();
    mockRepo.Setup(r => r.GetDocumentAsync(1, default))
        .ReturnsAsync(new DocumentEntity { Id = 1, FileName = "test.pdf" });

    // Act
    var result = await mockRepo.Object.GetDocumentAsync(1);

    // Assert
    result.Should().NotBeNull();
    result!.FileName.Should().Be("test.pdf");
}

[Fact]
public async Task GetDocumentAsync_NonExistentId_ReturnsNull()
{
    // Arrange
    var mockRepo = new Mock<IDocumentService>();
    mockRepo.Setup(r => r.GetDocumentAsync(999, default))
        .ReturnsAsync((DocumentEntity?)null);

    // Act
    var result = await mockRepo.Object.GetDocumentAsync(999);

    // Assert
    result.Should().BeNull();
}
```

### Test Patterns

**Arrange/Act/Assert**: Every test follows this three-phase structure with clear separation.

**Mocking with Moq**: Mock all service dependencies. Never use real database connections or HTTP clients in unit tests.

```csharp
[Fact]
public async Task IndexDocumentAsync_ValidDocument_UpdatesStatus()
{
    // Arrange
    var mockDocService = new Mock<IDocumentService>();
    var mockChunking = new Mock<IChunkingService>();
    var mockEmbedding = new Mock<IEmbeddingService>();
    var mockVectorStore = new Mock<IVectorStore>();

    var service = new IndexingService(
        mockDocService.Object,
        mockChunking.Object,
        mockEmbedding.Object,
        mockVectorStore.Object);

    var document = new DocumentEntity { Id = 1, IndexingStatus = "pending" };

    // Act
    await service.IndexDocumentAsync(document);

    // Assert
    mockChunking.Verify(c => c.ChunkAsync(It.IsAny<ProcessedDocument>(), default), Times.Once);
    mockEmbedding.Verify(e => e.EmbedAsync(It.IsAny<string>(), default), Times.AtLeastOnce);
}
```

**FluentAssertions**: Use FluentAssertions for all assertions. It provides better error messages and a more readable syntax:

```csharp
// Collections
results.Should().HaveCount(5);
results.Should().Contain(r => r.FileName == "test.pdf");
results.Should().BeInDescendingOrder(r => r.ImportedAt);

// Strings
name.Should().NotBeNullOrEmpty();
name.Should().StartWith("doc_");

// Numbers
score.Should().BeInRange(0.0, 1.0);
count.Should().BeGreaterThan(0);

// Exceptions
var act = async () => await service.ProcessAsync(null!);
await act.Should().ThrowAsync<ArgumentNullException>();
```

### Coverage Target

The target is **80% code coverage on `AgentX.Core`**. Measure coverage with:

```bash
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj --collect:"XPlat Code Coverage"
```

The coverage report is generated by `coverlet.collector` and can be viewed with tools like ReportGenerator.

### Running Tests

```bash
# Run all tests
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj

# Run tests with detailed output
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj --verbosity normal

# Run tests in Release configuration
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj -c Release

# Run a specific test class
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj --filter "FullyQualifiedName~DocumentServiceTests"

# Run with code coverage
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj --collect:"XPlat Code Coverage"
```

---

## Build Pipeline

### Full Build Sequence

```bash
# 1. Clean all build artifacts
dotnet clean AgentX.sln

# 2. Restore NuGet packages
dotnet restore AgentX.sln

# 3. Build in Release configuration
dotnet build AgentX.sln -c Release

# 4. Run tests
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj -c Release

# 5. Publish self-contained executable
dotnet publish src/AgentX.App/AgentX.App.csproj -c Release -r win-x64 --self-contained -o publish/win-x64

# 6. Build installer (requires Inno Setup 6)
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer/AgentX-Setup.iss
```

### Build Configurations

| Configuration | Use Case | Optimizations |
|---------------|----------|---------------|
| `Debug` | Development, debugging | No optimization, full debug symbols |
| `Release` | Production builds, testing | Full optimization, ReadyToRun (R2R) AOT |

### Publish Options

The publish command creates a self-contained deployment that includes the .NET 8 runtime:

- `--self-contained`: Bundles the .NET runtime so users do not need to install it separately.
- `-r win-x64`: Targets 64-bit Windows.
- `PublishReadyToRun` is enabled in the project file for ahead-of-time compilation.
- `WindowsAppSDKSelfContained` is `true`, bundling the Windows App SDK runtime.
- `WindowsPackageType` is `None`, producing an unpackaged (non-MSIX) application.

---

## Adding a New Feature

Follow this checklist to add a new feature end-to-end:

### Step 1: Define the Interface

Create the interface in the appropriate `AgentX.Core` subdirectory:

```csharp
// src/AgentX.Core/Services/NewFeature/INewFeatureService.cs
namespace AgentX.Core.Services.NewFeature;

public interface INewFeatureService
{
    Task<FeatureResult> ExecuteAsync(FeatureRequest request, CancellationToken ct = default);
}
```

### Step 2: Implement the Service

```csharp
// src/AgentX.Core/Services/NewFeature/NewFeatureService.cs
namespace AgentX.Core.Services.NewFeature;

public class NewFeatureService : INewFeatureService
{
    private readonly AgentXDbContext _dbContext;
    private readonly Serilog.ILogger _log = Log.ForContext<NewFeatureService>();

    public NewFeatureService(AgentXDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<FeatureResult> ExecuteAsync(FeatureRequest request, CancellationToken ct = default)
    {
        try
        {
            // Implementation
            _log.Information("Feature executed for {Input}", request.Input);
            return new FeatureResult { Success = true };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Feature execution failed for {Input}", request.Input);
            throw;
        }
    }
}
```

### Step 3: Register in DI Container

Add the registration in `App.xaml.cs` `ConfigureServices`, in the appropriate section:

```csharp
services.AddSingleton<INewFeatureService, NewFeatureService>();
```

### Step 4: Create the ViewModel

```csharp
// src/AgentX.App/ViewModels/NewFeatureViewModel.cs
namespace AgentX.App.ViewModels;

public partial class NewFeatureViewModel : ObservableObject
{
    private readonly INewFeatureService _featureService;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public NewFeatureViewModel(INewFeatureService featureService)
    {
        _featureService = featureService;
    }

    public async Task InitializeAsync() { /* ... */ }

    [RelayCommand]
    private async Task ExecuteFeatureAsync() { /* ... */ }
}
```

Register the ViewModel in `App.xaml.cs`:

```csharp
services.AddTransient<ViewModels.NewFeatureViewModel>();
```

### Step 5: Create the XAML Page

Create both `NewFeaturePage.xaml` and `NewFeaturePage.xaml.cs` in `src/AgentX.App/Views/`.

Register the page in `App.xaml.cs`:

```csharp
services.AddTransient<Views.NewFeaturePage>();
```

### Step 6: Add Navigation

In `MainWindow.xaml.cs`, add entries to both dictionaries:

```csharp
_pageMap["NewFeature"] = typeof(Views.NewFeaturePage);
_navItemMap["NewFeature"] = NavNewFeature;
```

In `MainWindow.xaml`, add a `NavigationViewItem`:

```xml
<NavigationViewItem x:Name="NavNewFeature" Tag="NewFeature" Content="New Feature">
    <NavigationViewItem.Icon>
        <FontIcon Glyph="&#xE8A5;" />
    </NavigationViewItem.Icon>
</NavigationViewItem>
```

### Step 7: Write Tests

Create test files in the corresponding subdirectory under `tests/AgentX.Tests/`.

### Step 8: Build and Verify

```bash
dotnet build AgentX.sln
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj
```

---

## Adding a New Document Processor

To support a new file format, follow these steps:

### Step 1: Implement IDocumentProcessor

```csharp
// src/AgentX.Core/Documents/Processors/NewFormatProcessor.cs
using AgentX.Core.Documents.Models;

namespace AgentX.Core.Documents.Processors;

public class NewFormatProcessor : IDocumentProcessor
{
    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xyz", ".abc" };

    public bool CanProcess(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(ext);
    }

    public async Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Extract text content from the file
        var content = await ExtractContentAsync(filePath, ct);

        return new ProcessedDocument
        {
            FileName = Path.GetFileName(filePath),
            FilePath = filePath,
            Content = content,
            FileType = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant(),
            // ... other metadata
        };
    }

    private async Task<string> ExtractContentAsync(string filePath, CancellationToken ct)
    {
        // Format-specific extraction logic
        throw new NotImplementedException();
    }
}
```

### Step 2: Register in DI Container

Add to `App.xaml.cs` in the Document Processors section:

```csharp
services.AddSingleton<IDocumentProcessor, NewFormatProcessor>();
```

The `DocumentService` automatically discovers all registered `IDocumentProcessor` implementations via `IEnumerable<IDocumentProcessor>` constructor injection. No additional wiring is needed.

### Step 3: Update FileTypeHelper (if needed)

If the new format requires MIME type mapping or icon association, update `AgentX.Core/Helpers/FileTypeHelper.cs`.

### Step 4: Write Tests

Test both the processor in isolation and the end-to-end flow through `DocumentService`.

---

## Adding a New Page

This is a condensed version of the navigation setup required when adding a new page to the application.

### 1. Create the ViewModel

File: `src/AgentX.App/ViewModels/NewPageViewModel.cs`

Must inherit `ObservableObject`, use `[ObservableProperty]` and `[RelayCommand]`, accept dependencies via constructor injection, and expose an `InitializeAsync()` method.

### 2. Create the XAML Page

File: `src/AgentX.App/Views/NewPage.xaml`

```xml
<Page
    x:Class="AgentX.App.Views.NewPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:converters="using:AgentX.App.Converters"
    NavigationCacheMode="Enabled">

    <Page.Resources>
        <converters:BoolToVisibilityConverter x:Key="BoolToVis" />
    </Page.Resources>

    <Grid Padding="32">
        <TextBlock Text="{x:Bind ViewModel.Title, Mode=OneWay}"
                   Style="{StaticResource TitleTextBlockStyle}" />
    </Grid>
</Page>
```

File: `src/AgentX.App/Views/NewPage.xaml.cs`

```csharp
using Microsoft.UI.Xaml.Controls;
using AgentX.App.ViewModels;

namespace AgentX.App.Views;

public sealed partial class NewPage : Page
{
    public NewPageViewModel ViewModel { get; }

    public NewPage()
    {
        ViewModel = App.GetService<NewPageViewModel>();
        ViewModel.NavigateRequested = NavigateToPage;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private void NavigateToPage(string pageTag)
    {
        if (App.MainWindow is MainWindow mainWindow)
        {
            mainWindow.NavigateToPage(pageTag);
        }
    }
}
```

### 3. Register in DI

In `App.xaml.cs`:

```csharp
services.AddTransient<ViewModels.NewPageViewModel>();
services.AddTransient<Views.NewPage>();
```

### 4. Wire Navigation

In `MainWindow.xaml.cs`, add to both `_pageMap` and `_navItemMap`:

```csharp
_pageMap["NewPage"] = typeof(Views.NewPage);
_navItemMap["NewPage"] = NavNewPage;
```

In `MainWindow.xaml`, add the nav item:

```xml
<NavigationViewItem x:Name="NavNewPage" Tag="NewPage" Content="New Page">
    <NavigationViewItem.Icon>
        <FontIcon Glyph="&#xE8A5;" />
    </NavigationViewItem.Icon>
</NavigationViewItem>
```

---

## Adding a New Converter

Converters translate between data types for XAML binding. All converters implement `IValueConverter`.

### Template

```csharp
// src/AgentX.App/Converters/InputToOutputConverter.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AgentX.App.Converters;

/// <summary>
/// Converts <see cref="InputType"/> values to <see cref="OutputType"/> values.
/// [Describe the conversion logic and any configurable properties.]
/// </summary>
public sealed class InputToOutputConverter : IValueConverter
{
    /// <summary>
    /// [Document any configurable properties, like IsInverted.]
    /// </summary>
    public bool SomeOption { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // Conversion logic
        throw new NotImplementedException();
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        // Reverse conversion (or throw NotImplementedException if one-way only)
        throw new NotImplementedException();
    }
}
```

### Existing Converters Reference

| Converter | Input | Output | Notes |
|-----------|-------|--------|-------|
| `BoolToVisibilityConverter` | `bool` | `Visibility` | Supports `IsInverted` property |
| `BoolToOpacityConverter` | `bool` | `double` | True = 1.0, False = custom opacity |
| `BytesToStringConverter` | `long` | `string` | Formats bytes to KB/MB/GB |
| `InverseBoolConverter` | `bool` | `bool` | Simple negation |
| `NullToVisibilityConverter` | `object?` | `Visibility` | Null = Collapsed |
| `PercentToWidthConverter` | `double` | `double` | Maps percentage to pixel width via parameter |
| `StatusToColorConverter` | `string` | `SolidColorBrush` | Maps status strings to semantic colors |
| `StringToVisibilityConverter` | `string` | `Visibility` | Empty/null = Collapsed |
| `TimeAgoConverter` | `DateTime` | `string` | Formats as "5m ago", "2h ago", etc. |
| `TokensToStringConverter` | `long` | `string` | Formats token counts with K/M suffixes |
| `ZeroToVisibleConverter` | `int` | `Visibility` | Zero = Visible, non-zero = Collapsed |

---

## Database Migrations

Currently, the application uses `EnsureCreatedAsync()` for schema creation, which is suitable for greenfield development. As the schema stabilizes, consider transitioning to EF Core migrations for production upgrades.

### To Add EF Core Migrations (When Ready)

```bash
# Install the EF Core tools globally (one time)
dotnet tool install --global dotnet-ef

# Create a migration
dotnet ef migrations add MigrationName --project src/AgentX.Core --startup-project src/AgentX.App

# Apply migrations
dotnet ef database update --project src/AgentX.Core --startup-project src/AgentX.App
```

### Schema Change Guidelines

When modifying the database schema:

1. Add new properties to the entity class.
2. Update the `Configure*` method in `AgentXDbContext` with any new constraints, indexes, or relationships.
3. If the change is additive (new column with a default), it is generally safe with `EnsureCreatedAsync`.
4. If the change modifies or removes existing columns, a migration or manual `ALTER TABLE` is required.
5. Always test schema changes against an existing database to verify upgrade compatibility.

---

## Keyboard Shortcuts

### Registered Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+K` | Toggle command palette |
| `Ctrl+N` | Navigate to Chat (new conversation) |
| `Ctrl+I` | Navigate to Knowledge Vault (import) |
| `Ctrl+F` | Navigate to Search |
| `Ctrl+Shift+F` | Navigate to Search (alternate) |
| `Ctrl+,` | Navigate to Settings |
| `Escape` | Close command palette (when open) |

### Adding a New Shortcut

Register in `MainWindow.RegisterDefaultShortcuts()`:

```csharp
_keyboardShortcutService.RegisterShortcut(
    VirtualKey.X, ctrl: true, shift: false, alt: false,
    () => NavigateToPage("NewPage"));
```

The `KeyboardShortcutService` handles deduplication, logging, and error isolation for each shortcut handler.

---

## Installer and Distribution

### Inno Setup

The installer is built using Inno Setup 6. The script is located at `installer/AgentX-Setup.iss`.

Build the installer:

```bash
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer/AgentX-Setup.iss
```

The output installer (`AgentX-Setup-1.0.0-x64.exe`) is placed in `installer-output/`.

### Distribution Checklist

Before creating a release:

1. Update the version in `Directory.Build.props` (`<Version>` property).
2. Update the version in the Inno Setup script if applicable.
3. Run the full build pipeline (clean, restore, build, test, publish).
4. Verify the installer on a clean Windows 11 machine.
5. Test Ollama integration on the target machine.
6. Verify first-run onboarding flow.

---

## Code Review Checklist

Use this checklist when reviewing pull requests:

### Architecture

- [ ] New code respects the dependency flow: `App -> Core`, never `Core -> App`.
- [ ] No UI framework references in `AgentX.Core`.
- [ ] Interfaces are defined in `AgentX.Core`; UI-specific interfaces in `AgentX.App/Services`.
- [ ] New services are registered in `App.xaml.cs` with the correct lifetime.

### Coding Standards

- [ ] File-scoped namespaces used throughout.
- [ ] Nullable reference types handled correctly (no suppressions without justification).
- [ ] XML documentation on all public interfaces and non-trivial public methods.
- [ ] `ConfigureAwait(false)` used in all `AgentX.Core` async methods.
- [ ] `CancellationToken` parameter on all async service methods.
- [ ] No `async void` except in event handlers.
- [ ] No blocking calls (`.Result`, `.Wait()`, `.GetAwaiter().GetResult()`).

### MVVM

- [ ] ViewModel is `partial` and inherits `ObservableObject`.
- [ ] `[ObservableProperty]` used for bindable fields.
- [ ] `[RelayCommand]` used for commands.
- [ ] `InitializeAsync()` called from `Loaded` event, not constructor.
- [ ] Dependencies injected via constructor, not resolved via `App.GetService<T>()`.

### Error Handling

- [ ] Service methods wrap operations in try/catch with Serilog logging.
- [ ] ViewModels catch exceptions and set user-facing error properties.
- [ ] No swallowed exceptions (empty catch blocks) without explicit justification.

### XAML

- [ ] `x:Bind` used instead of `{Binding}`.
- [ ] `Mode=OneWay` specified for changing properties.
- [ ] Converters declared in `Page.Resources`.
- [ ] Styles reference `StaticResource` from resource dictionaries.

### Testing

- [ ] Tests follow the `MethodName_Scenario_ExpectedResult` naming convention.
- [ ] Arrange/Act/Assert structure with clear separation.
- [ ] All service dependencies are mocked.
- [ ] FluentAssertions used for all assertions.
- [ ] Edge cases and error conditions tested.
- [ ] No tests depend on external services (Ollama, file system, network).

### Security

- [ ] No hardcoded secrets, API keys, or credentials.
- [ ] User input is validated before processing.
- [ ] File paths are sanitized to prevent path traversal.
- [ ] License keys are not logged at `Information` level or above.

---

## Troubleshooting

### Common Build Issues

**Problem**: Build fails with "WindowsAppSDK not found."
**Solution**: Install the Windows App SDK 1.6 via Visual Studio Installer or NuGet. Ensure the `Microsoft.WindowsAppSDK` package version matches `1.6.250108002` in `AgentX.App.csproj`.

**Problem**: `x:Bind` errors at compile time.
**Solution**: Ensure the `ViewModel` property is `public` on the Page code-behind and that the property types match the expected binding target types.

**Problem**: "Type or namespace 'AgentX' could not be found."
**Solution**: Restore NuGet packages (`dotnet restore`) and rebuild. If the issue persists, close Visual Studio, delete `bin/` and `obj/` directories, and rebuild.

### Common Runtime Issues

**Problem**: "Ollama not detected" on the status bar.
**Solution**: Ensure Ollama is installed and running. Verify with `curl http://localhost:11434/api/tags`. Check Serilog logs at `%LOCALAPPDATA%\AgentX\Logs\` for detailed error messages.

**Problem**: Database errors on startup.
**Solution**: Check `%LOCALAPPDATA%\AgentX\agentx.db` exists and is not corrupted. If corrupted, delete the file and restart (schema will be recreated, but data is lost).

**Problem**: ViewModel properties not updating in the UI.
**Solution**: Verify the property uses `[ObservableProperty]` (not a manual property without `OnPropertyChanged`). Ensure `x:Bind` specifies `Mode=OneWay`. Check that the ViewModel class is `partial`.

**Problem**: Navigation does not update the sidebar selection.
**Solution**: Ensure the page tag is registered in both `_pageMap` and `_navItemMap` in `MainWindow.xaml.cs`.

### Log File Location

All application logs are written to:

```
%LOCALAPPDATA%\AgentX\Logs\agentx-YYYY-MM-DD.log
```

Open the most recent file to diagnose runtime issues. Search for `[ERR]` or `[FTL]` entries.

---

## Appendix: Key File Paths

| Resource | Path |
|----------|------|
| Solution file | `AgentX.sln` |
| Global build properties | `Directory.Build.props` |
| DI container setup | `src/AgentX.App/App.xaml.cs` |
| Navigation shell | `src/AgentX.App/MainWindow.xaml.cs` |
| Database context | `src/AgentX.Core/Data/AgentXDbContext.cs` |
| AI service interface | `src/AgentX.Core/AI/IAiService.cs` |
| Document processor interface | `src/AgentX.Core/Documents/IDocumentProcessor.cs` |
| RAG pipeline interface | `src/AgentX.Core/Search/IRagPipeline.cs` |
| Style resources | `src/AgentX.App/Styles/` |
| Installer script | `installer/AgentX-Setup.iss` |
| Log files | `%LOCALAPPDATA%\AgentX\Logs\` |
| Database file | `%LOCALAPPDATA%\AgentX\agentx.db` |
| Test project | `tests/AgentX.Tests/AgentX.Tests.csproj` |
