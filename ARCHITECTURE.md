# Agent-X Architecture Documentation

## Overview

Agent-X is a sophisticated **AI-native knowledge management and RAG (Retrieval Augmented Generation) platform** built as a Windows desktop application using WinUI 3, .NET 8, and Entity Framework Core. It combines advanced AI capabilities with local-first data storage to provide an intelligent workspace for document analysis, semantic search, and multi-agent orchestration.

## Technology Stack

| Layer | Technology |
|-------|-----------|
| **UI Framework** | WinUI 3 (Windows App SDK 1.6) |
| **Runtime** | .NET 8.0 (targeting Windows 10 1809+) |
| **Language** | C# 12 with nullable reference types enabled |
| **ORM** | Entity Framework Core 8.0 with SQLite |
| **MVVM Framework** | CommunityToolkit.Mvvm 8.2 |
| **Dependency Injection** | Microsoft.Extensions.DependencyInjection |
| **Logging** | Serilog |
| **Vector Database** | HnswLite (HNSW approximate nearest neighbor index) |
| **Local LLM** | LLamaSharp (llama.cpp bindings) |

## Architectural Patterns

### 1. Layered Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Presentation Layer                      │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ Views (XAML)         │ ViewModels (MVVM)            │   │
│  │ - DashboardPage      │ - DashboardViewModel         │   │
│  │ - ChatPage           │ - ChatViewModel              │   │
│  │ - SearchPage         │ - SearchViewModel            │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    Application Services Layer                │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ Coordinators │ UI Services │ Navigation             │   │
│  │ - ConversationCoordinator  │ - AppNavigationService │   │
│  │ - MessagingCoordinator    │ - StatusBarService      │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                      Domain Services Layer                   │
│  ┌───────────────────┬─────────────────┬─────────────────┐  │
│  │ AI & Agents      │ Search & RAG    │ Document        │  │
│  │ - AiService      │ - SemanticSearch│ - DocumentService│  │
│  │ - ReActAgent     │ - HybridSearch  │ - ChunkingService│  │
│  │ - MultiAgent     │ - CitationService│ - IndexingService││
│  └───────────────────┴─────────────────┴─────────────────┘  │
│  ┌───────────────────┬─────────────────┬─────────────────┐  │
│  │ Intelligence      │ Collaboration   │ Integration     │  │
│  │ - KnowledgeGraph  │ - SyncService   │ - PluginService │  │
│  │ - DigestService   │ - Collaboration │ - WebImport     │  │
│  │ - ComparisonService│ - FeedbackService│ - WorkflowEngine││
│  └───────────────────┴─────────────────┴─────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                      Data Access Layer                       │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ AgentXDbContext (EF Core) │ Vector Store (HnswLite) │   │
│  │ - Entity Mappings         │ - Embedding Index       │   │
│  │ - Migrations              │ - ANN Search            │   │
│  │ - Encrypted Connection    │ - Persistent Storage    │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                      Storage Layer                            │
│  ┌─────────────────┬─────────────────┬─────────────────────┐│
│  │ SQLite (SQLCipher)│ HnswLite Files│ File System         ││
│  │ - Relational Data│ - Vector Index │ - Documents         ││
│  │ - Encrypted at Rest│ - Embeddings  │ - Backups           ││
│  └─────────────────┴─────────────────┴─────────────────────┘│
└─────────────────────────────────────────────────────────────┘
```

### 2. MVVM Pattern with Coordinators

Agent-X uses the **Model-View-ViewModel (MVVM)** pattern enhanced with **Coordinator Pattern** for complex features:

```csharp
// Traditional MVVM binding in View
<TextBox Text="{x:Bind ViewModel.Query, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
         KeyDown="OnQueryKeyDown" />

// ViewModel with CommunityToolkit.Mvvm source generators
public partial class SearchViewModel : ObservableObject
{
    [ObservableProperty]
    private string _query;

    [RelayCommand]
    private async Task SearchAsync() => await _coordinator.ExecuteSearchAsync(Query);
}

// Coordinator orchestration (separates complex workflows from VM)
public class SearchCoordinator : ISearchCoordinator
{
    public async Task<SearchResult> ExecuteSearchAsync(string query)
    {
        // Orchestrates hybrid search, citations, reranking, etc.
        var semantic = await _semanticSearch.SearchAsync(query);
        var keyword = await _keywordSearch.SearchAsync(query);
        var hybrid = await _orchestrator.CombineAsync(semantic, keyword);
        var reranked = await _reranker.RerankAsync(hybrid, query);
        return await _citationService.EnrichAsync(reranked);
    }
}
```

### 3. Dependency Injection Architecture

The application uses **Microsoft.Extensions.DependencyInjection** with a **service lifetime hierarchy**:

```csharp
// Singleton services - application-wide lifetime
services.AddSingleton<IAiService, AiService>();
services.AddSingleton<AgentXDbContext>();
services.AddSingleton<IVectorStore>(sp => VectorStoreFactory.Create(...));

// Scoped services - per-operation lifetime
// (Not commonly used in Agent-X; most services are singletons)

// Transient services - per-request lifetime
services.AddTransient<ChatViewModel>();
services.AddTransient<DashboardPage>();
```

#### Service Registration Groups

| Category | Services (Examples) | Lifetime |
|----------|---------------------|----------|
| **Core Infrastructure** | AgentXDbContext, MigrationRunner, SettingsService | Singleton |
| **AI & Orchestration** | AiService, ReActAgent, MultiAgentOrchestrator | Singleton |
| **Vector & Search** | VectorStore, SemanticSearchService, HybridSearchOrchestrator | Singleton |
| **Document Processing** | DocumentService, ChunkingService, IndexingService | Singleton |
| **UI Services** | ThemeService, NavigationService, StatusBarService | Singleton |
| **ViewModels** | ChatViewModel, SearchViewModel, DashboardViewModel | Transient |
| **Views** | ChatPage, SearchPage, DashboardPage | Transient |

## Key Architectural Components

### 1. Multi-Agent Chat Orchestration

Agent-X implements a **multi-agent system** for complex reasoning:

```
User Query
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│                   MultiAgentOrchestrator                    │
│  - Analyzes task complexity and type                        │
│  - Dispatches to appropriate agent(s)                      │
│  - Synthesizes results                                      │
└─────────────────────────────────────────────────────────────┘
    │
    ├──► ReActAgent (Reasoning + Acting)
    │   - Tool use: screen capture, web search, file access
    │   - Iterative reasoning loop
    │   - Self-correction and reflection
    │
    ├──► ReflectionService (Meta-cognition)
    │   - Evaluates response quality
    │   - Suggests improvements
    │   - Tracks reasoning patterns
    │
    └──► ReasoningService (Chain-of-thought)
        - Decomposes complex tasks
        - Maintains reasoning trace
        - Validates logical consistency
```

### 2. RAG Pipeline Architecture

The Retrieval Augmented Generation pipeline follows an **extensible stage-based architecture**:

```
Query Input
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│                  Query Expansion (Optional)                 │
│  - MultiQueryGenerator: Generates 3-4 paraphrased queries  │
│  - HydeService: Hypothetical document embeddings            │
└─────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│                   Retrieval Stage                           │
│  ┌───────────────────┬───────────────────┬─────────────────┐│
│  │ Semantic Search  │  Keyword Search   │  Parent Document││
│  │ (Vector + HNSW)  │  (FTS5)           │  Retriever      ││
│  └───────────────────┴───────────────────┴─────────────────┘│
└─────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│                   Fusion & Reranking                        │
│  - HybridSearchOrchestrator: Combines semantic + keyword   │
│  - RagReranker: Reorders by relevance                       │
│  - LlmReranker: AI-powered reranking (optional)             │
└─────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│                   Context Assembly                          │
│  - SemanticContextSelector: Intelligently selects chunks   │
│  - ContextualCompressor: Summarizes long contexts          │
│  - ConversationCompressionService: Compresses chat history │
└─────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│                   Citation & Enrichment                     │
│  - CitationService: Attaches source references              │
│  - ParentDocumentRetriever: Fetches full document context  │
└─────────────────────────────────────────────────────────────┘
    │
    ▼
Augmented Context → LLM Generation
```

### 3. Conversation Memory System

Agent-X implements a **hierarchical conversation memory** with summarization:

```
ConversationEntity
    │
    ├──► Messages (Chronological)
    │   └──► User/Assistant exchanges
    │
    ├──► SummarySnapshots (Incremental)
    │   └──► Generated every N messages
    │   └──► Embedded for semantic search
    │
    ├──► SummaryState (Metadata)
    │   └──► Tracks summarization progress
    │   └──► Detects stale summaries
    │
    └──► ThemeMembership (Clustering)
        └──► Groups conversations by topic
        └──► Enables thematic analysis
```

### 4. Vector Database Integration

**HnswLite** provides in-memory HNSW (Hierarchical Navigable Small World) indexing:

```csharp
// Vector store factory pattern
public static class VectorStoreFactory
{
    public static IVectorStore Create(
        ISettingsService settings,
        ILogger logger,
        IEncryptedConnectionFactory connectionFactory)
    {
        // Supports both in-memory and SQLite-backed storage
        // Automatically persists embeddings to encrypted database
        return new SqliteVectorStore(connectionFactory, logger);
    }
}

// Approximate Nearest Neighbor search
var results = await _vectorStore.SearchAsync(
    queryEmbedding,
    k: 10,              // Top-k results
    efSearch: 100        // HNSW search parameter (accuracy vs speed)
);
```

### 5. Document Processing Pipeline

```
File Input
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│                   Document Detection                         │
│  - FileTypeHelper: Determines file type                     │
│  - HashHelper: Computes content hash (deduplication)        │
└─────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│                   Processor Selection                        │
│  ┌───────────────────┬───────────────────┬─────────────────┐│
│  │ PDFProcessor      │ DocxProcessor    │ TextProcessor   ││
│  │ (PDFsharp)        │ (OpenXML)        │ (Plain text)    ││
│  └───────────────────┴───────────────────┴─────────────────┘│
│  ┌───────────────────┬───────────────────┬─────────────────┐│
│  │ MarkdownProcessor │ CodeFileProcessor│ ImageProcessor  ││
│  │ (Markdig)         │ (Syntax-aware)    │ (OCR)           ││
│  └───────────────────┴───────────────────┴─────────────────┘│
└─────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│                   Chunking Strategy                         │
│  - SemanticChunking: Respects paragraph boundaries          │
│  - FixedSizeChunking: Configurable token/chunk count        │
│  - OverlapChunking: Sliding window for context continuity   │
└─────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│                   Embedding Generation                       │
│  - EmbeddingService: Calls configured embedding model       │
│  - VectorStore: Persists embeddings with HNSW indexing      │
└─────────────────────────────────────────────────────────────┘
    │
    ▼
Indexed Document (Searchable)
```

## Security Architecture

### 1. Database Encryption

Agent-X supports **SQLCipher** for at-rest encryption:

```
┌─────────────────────────────────────────────────────────────┐
│                   Encryption Flow                           │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ 1. EncryptionStateFile: Detects encryption mode     │   │
│  │    - DpapiWrapped: DPAPI-encrypted key stored in DB│   │
│  │    - UserPassphrase: User-provided passphrase       │   │
│  └─────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ 2. Unlock Flow: PRAGMA key applied on startup       │   │
│  │    - AgentXDbContext.EnsureKeyApplied()             │   │
│  │    - Passes through IEncryptedConnectionFactory     │   │
│  └─────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ 3. Key Management: Secure key storage              │   │
│  │    - IDatabaseKeyService: Creates/rotates keys      │   │
│  │    - DatabaseEncryptionMigrator: Migrates plaintext │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### 2. API Key Protection

```csharp
// API keys stored in UserSettings (encrypted column)
public class UserSettingsEntity
{
    public string Key { get; set; }           // Setting name
    public string Value { get; set; }         // Encrypted value
    public string ValueType { get; set; }     // "string", "int", "encrypted"
}

// DPAPI encryption for sensitive data
services.AddSingleton<IDpapiEncryptionService, DpapiEncryptionService>();
```

## Plugin Architecture

Agent-X supports **extensible plugins** via a manifest-based system:

```
plugins/
├── sample-plugin/
│   ├── plugin.json              (PluginManifest)
│   ├── SamplePlugin.dll         (Compiled plugin)
│   └── README.md
└── user-plugins/
    └── calendar-connector/
        ├── plugin.json
        └── CalendarPlugin.dll
```

**Plugin Manifest:**
```json
{
  "id": "agentx-plugin-calendar",
  "name": "Calendar Connector",
  "version": "1.0.0",
  "description": "Integrates with Google Calendar",
  "author": "AgentX Team",
  "type": "DataConnector",
  "entryPoint": "CalendarPlugin.CalendarPlugin, CalendarPlugin",
  "permissions": ["read:calendar", "write:events"],
  "settings": {
    "apiKey": { "type": "string", "required": true },
    "syncInterval": { "type": "number", "default": 3600 }
  }
}
```

## Workflow System

The **WorkflowBuilderPage** enables no-code automation:

```
WorkflowEntity
    │
    └──► WorkflowStep[] (Ordered sequence)
        ├──► Step 1: AiPrompt (Generate summary)
        ├──► Step 2: WebSearch (Verify facts)
        ├──► Step 3: DataTransform (Format output)
        └──► Step 4: Export (Save to file)
```

**Execution:**
```csharp
public interface IWorkflowEngine
{
    Task<WorkflowRunResult> ExecuteAsync(
        WorkflowEntity workflow,
        IDictionary<string, object> inputs);
}
```

## REST API Layer

Agent-X exposes a **local REST API** for browser extension and mobile companion:

```
http://localhost:5324/
├── GET  /api/v1/health
├── POST /api/v1/chat/completions
├── POST /api/v1/search
├── POST /api/v1/documents/index
└── GET  /api/v1/conversations
```

**Implementation:**
```csharp
public interface IApiHostService
{
    Task StartAsync();
    Task StopAsync();
    bool IsRunning { get; }
}
```

## Localization Architecture

Agent-X supports **multi-language localization**:

```
Resources/
├── Strings/en-US/resources.resjson
├── Strings/es-ES/resources.resjson
└── Strings/ja-JP/resources.resjson

// Runtime localization
services.AddSingleton<ILocalizationService, LocalizationService>();

// Usage in ViewModel
var localizedText = _localizationService.GetString("SearchPlaceholder");
```

## Performance Optimizations

### 1. Async/Await Best Practices
- All I/O operations are async
- `ConfigureAwait(false)` used in library code
- Avoids deadlocks with proper `DispatcherQueue` usage

### 2. Vector Index Optimization
- HNSW parameters tuned for accuracy/speed tradeoff
- Incremental index updates (no full rebuild)
- Parallel embedding generation

### 3. Database Query Optimization
- Indexed columns on frequently queried fields
- Query projection (select only needed columns)
- Batch operations for bulk inserts

## Build Configuration

### Project Structure
```
Agent-X/
├── src/
│   ├── AgentX.App/          # WinUI 3 application (Main project)
│   ├── AgentX.Core/         # Core library (Business logic, entities)
│   └── AgentX.Mobile/       # MAUI companion app
├── tests/
│   ├── AgentX.Tests/        # Unit tests
│   └── LocaleAudit.Tests/   # Localization audit tests
├── plugins/
│   └── sample-plugin/       # Plugin example
└── tools/
    └── LocaleAudit/         # Localization tool
```

### Build Commands
```bash
# Build all projects
dotnet build AgentX.sln

# Run tests
dotnet test

# Build release
dotnet build -c Release

# Publish self-contained
dotnet publish -c Release -r win-x64 --self-contained
```

## Deployment Architecture

```
Development Build          →  Local AppData
├── Database: agentx.db     →  %LocalAppData%/AgentX/
├── Logs: agentx-.log       →  %LocalAppData%/AgentX/Logs/
└── Backups: *.zip          →  User-configurable

MSIX Package                →  C:\Program Files\WindowsApps\
├── Self-contained          →  Windows App SDK runtime bundled
├── Update: MSIX updates    →  Windows Store or sideload
└── Data migration          →  Automatic on version upgrade
```

## Monitoring & Observability

### Serilog Configuration
```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Debug()                          // Visual Studio Output
    .WriteTo.File(
        logPath,
        rollingInterval: RollingInterval.Day, // Daily rollover
        retainedFileCountLimit: 7)            // 7-day retention
    .CreateLogger();
```

### Analytics Service
```csharp
public interface IAnalyticsService
{
    Task TrackEventAsync(string eventName, IDictionary<string, string>? properties = null);
    Task TrackSearchAsync(SearchMetrics metrics);
    Task TrackIndexingAsync(IndexingMetrics metrics);
}
```

## Extension Points

### Adding a New AI Provider
```csharp
public sealed class CustomProvider : IAiProvider
{
    public string ProviderId => "custom";
    public string DisplayName => "Custom AI";

    public Task<bool> CheckConnectionAsync(CancellationToken ct = default) { }
    public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default) { }
    public IAsyncEnumerable<string> StreamChatAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default) { }
}
```

### Adding a New Document Processor
```csharp
public sealed class CustomProcessor : IDocumentProcessor
{
    public string SupportedExtension => ".custom";

    public Task<ProcessedDocument> ProcessAsync(
        string filePath,
        CancellationToken ct = default) { }
}
```

### Adding a New RAG Pipeline Stage
```csharp
public interface IRagPipelineStage
{
    string Name { get; }
    Task<SearchResult> ProcessAsync(
        SearchResult input,
        SearchQuery query,
        CancellationToken ct = default);
}

// Register in DI
services.AddSingleton<IRagPipelineStage, CustomReranker>();
```

## Architecture Decision Records (ADRs)

### ADR-001: SQLite + SQLCipher for Database
**Decision:** Use SQLite with SQLCipher encryption for local data storage.

**Rationale:**
- Zero-configuration embedded database
- Excellent query performance for our data model
- Built-in encryption support
- Cross-platform compatibility (future Linux/Mac ports)

**Consequences:**
- Trade-off: No concurrent writes (single-writer limitation)
- Mitigation: Async queue for write operations
- Benefit: Simple deployment, no external database dependency

### ADR-002: HnswLite for Vector Index
**Decision:** Use HnswLite for in-memory HNSW vector index with SQLite persistence.

**Rationale:**
- HNSW algorithm provides O(log n) search performance
- Pure C# implementation (no native dependencies)
- Persistent storage via SQLite integration
- Lower memory overhead than loading all vectors

**Consequences:**
- Trade-off: Index rebuild required on batch imports
- Mitigation: Incremental index updates
- Benefit: Fast retrieval for RAG queries

### ADR-003: WinUI 3 over WPF
**Decision:** Use WinUI 3 (Windows App SDK) instead of WPF.

**Rationale:**
- Modern Fluent Design controls out-of-the-box
- Better performance with composition-based rendering
- Future-proof for Windows 11 features
- Cleaner API with nullable reference types

**Consequences:**
- Trade-off: Windows 10 1809+ only (no Windows 7/8 support)
- Benefit: Native Windows 11 look-and-feel
- Benefit: Mica, Acrylic, and Roundness visual effects

### ADR-004: Coordinator Pattern for Complex Features
**Decision:** Introduce Coordinator pattern between ViewModels and Services.

**Rationale:**
- Separates orchestration logic from ViewModel state management
- Improves testability (coordinators can be unit tested)
- Reduces ViewModel complexity
- Enables reuse across different UI contexts

**Consequences:**
- Trade-off: Additional layer of indirection
- Benefit: Cleaner, more maintainable ViewModels
- Benefit: Easier to test complex workflows

## Future Architectural Considerations

### Scalability
- Current design optimized for single-user desktop usage
- Potential multi-user server variant would require:
  - Migration from SQLite to PostgreSQL
  - Distributed vector store (Weaviate/Qdrant)
  - Authentication and authorization layer

### Cloud Integration
- Plugin architecture allows cloud service connectors
- Potential for cloud sync (currently in development via `SyncService`)
- Hybrid mode: Local-first with cloud backup

### AI Model Evolution
- Modular AI provider interface supports model changes
- Cost tracking enables budget-aware routing
- Model router can optimize for cost vs. quality

---

**Document Version:** 1.0  
**Last Updated:** 2025-01-03  
**Maintained By:** Agent-X Development Team
