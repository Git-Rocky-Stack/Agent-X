# Agent-X Architecture Documentation

## Overview

Agent-X is a **local-first AI document intelligence app for Windows**, built with WinUI 3, .NET 8 and
Entity Framework Core. It imports documents into a local Knowledge Vault, indexes them for semantic
and keyword search, answers questions over them with cited sources (Ask Your Files), and chats with
a built-in model, Ollama, OpenAI or Anthropic. Everything it stores stays in a SQLite database and
files under `%LocalAppData%\AgentX\`.

This file is the overview. The detailed reference is [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md),
the table reference is [DATABASE_SCHEMA.md](DATABASE_SCHEMA.md), and the local REST API is in
[API_ENDPOINTS.md](API_ENDPOINTS.md).

## Technology Stack

| Layer | Technology |
|-------|-----------|
| **UI framework** | WinUI 3 (Windows App SDK 1.6), unpackaged and self-contained |
| **Runtime** | .NET 8 (`net8.0-windows10.0.22621.0`), Windows 10 version 2004 (build 19041) or later |
| **Language** | C# 12 with nullable reference types |
| **ORM** | Entity Framework Core 8.0.11 on SQLite, with SQLCipher through `SQLitePCLRaw.bundle_e_sqlcipher` |
| **MVVM** | CommunityToolkit.Mvvm 8.2.2 |
| **Hosting and DI** | Microsoft.Extensions.Hosting and Microsoft.Extensions.DependencyInjection 8.0 |
| **Logging** | Serilog 4.0 (rolling file and debug sinks) |
| **Vector search** | Embeddings in SQLite, searched by an HNSW index (HnswLite 1.0.6) or a linear scan |
| **Built-in model** | LLamaSharp 0.19 (llama.cpp), CPU backend plus an optional CUDA 12 backend |
| **Ollama** | OllamaSharp 4.0.6 |
| **Speech to text** | Whisper.Net 1.5 |
| **Documents** | PDFsharp 6.1.1, DocumentFormat.OpenXml 3.2.0, Markdig 0.37, Windows OCR for images |
| **Mobile companion** | .NET MAUI (`net8.0-android`), a separate project outside `AgentX.sln` |

## Solution Layout

```
Agent-X/
  AgentX.sln
  src/
    AgentX.App/        WinUI 3 desktop app: views, view models, shell services, composition root
    AgentX.Core/       Class library: AI, documents, search, services, EF Core data layer
    AgentX.Mobile/     MAUI Android companion (not in AgentX.sln)
  tests/
    AgentX.Tests/      xUnit tests for Core, plus App view models and services linked as source
    LocaleAudit.Tests/ Tests for the locale audit tool
  tools/
    LocaleAudit/       Localization coverage tool used by CI
  plugins/
    sample-plugin/     Example document processor plugin
  browser-extension/   Browser extension that clips pages into the Smart Inbox
  installer/           Inno Setup script (AgentX-Setup.iss)
```

`AgentX.App` references `AgentX.Core`; `AgentX.Core` has no reference to the app.

## Architectural Patterns

### 1. Layered Architecture

```
+------------------------------------------------------------------+
| Presentation (AgentX.App)                                        |
|   Views (XAML pages) and ViewModels (CommunityToolkit.Mvvm)      |
|   Chat coordinators: Conversation, Messaging, Voice, Branching   |
|   Shell services: navigation, status strip, annunciators,        |
|   onboarding, window chrome, tray, notifications, localization   |
+------------------------------------------------------------------+
                               |
                               v
+------------------------------------------------------------------+
| Services (AgentX.Core)                                           |
|   AI: providers, embeddings, routing, context assembly, agents   |
|   Documents and indexing: processors, chunking, indexing queue   |
|   Search and RAG: semantic, keyword (FTS5), hybrid, RAG pipeline |
|   Chat, memory, recall, summaries, themes, Temporal Identity     |
|   Intelligence, export, workflows, web import, web search        |
|   Inbox, connectors (calendar, email), OAuth, plugins, sync      |
|   Backup, security, settings, local REST API, localization       |
+------------------------------------------------------------------+
                               |
                               v
+------------------------------------------------------------------+
| Data (AgentX.Core/Data)                                          |
|   AgentXDbContext (EF Core, one shared serialized instance)      |
|   MigrationRunner and migrations                                 |
|   Vector store (HnswVectorStore or SqliteVecStore)               |
+------------------------------------------------------------------+
                               |
                               v
+------------------------------------------------------------------+
| Storage (%LocalAppData%\AgentX)                                  |
|   agentx.db: EF tables, fts_chunks (FTS5), vec_embeddings        |
|   settings.json, encryption.info.json, usage-history.json, logs  |
+------------------------------------------------------------------+
```

### 2. MVVM, with Coordinators for Chat

Each page has a view model built with CommunityToolkit.Mvvm (`[ObservableProperty]`,
`[RelayCommand]`). Page view models call Core services directly, except the chat page:
`ChatViewModel` delegates to four singleton coordinators (`IConversationCoordinator`,
`IMessagingCoordinator`, `IVoiceCoordinator`, `IBranchingCoordinator`) that hold the chat
workflows, so they can be tested without WinUI.

```csharp
// A page view model: observable state plus commands that call Core services.
public partial class SearchViewModel : ObservableObject
{
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [RelayCommand]
    private async Task SearchAsync() { /* IHybridSearchOrchestrator.SearchAsync(...) */ }
}
```

Pages whose view model is `IDisposable` create it with `PageViewModelFactory.Create<T>()`, which
builds it with `ActivatorUtilities` so the root DI provider does not keep every instance alive
until shutdown. A code-quality test fails when such a page resolves its view model with
`App.GetService<T>()` instead.

### 3. Dependency Injection

`App.xaml.cs` builds a generic host (`Host.CreateDefaultBuilder()`), which loads
`appsettings.json` (the `Rag` options) and `RagPrompts.json` (prompt texts, reloaded when the file
changes), wires Serilog, and registers every service in `ConfigureServices`.

| Category | Examples | Lifetime |
|----------|----------|----------|
| **Data and startup** | `AgentXDbContext`, `MigrationRunner`, `StartupGate`, `StartupOrchestrator` | Singleton |
| **Security** | `DpapiEncryptionService`, `DatabaseKeyService`, `EncryptedConnectionFactory`, `DatabaseEncryptionManager` | Singleton |
| **AI** | `AiService`, `CostTracker`, `EmbeddingService` (wrapped by `CachedEmbeddingService`), `ModelRouterService`, `MultiAgentOrchestrator` | Singleton |
| **Documents, search, RAG** | 8 document processors, `DocumentService`, `IndexingService`, `HybridSearchOrchestrator`, `RagPipeline` | Singleton |
| **Features** | `InboxService`, `WorkflowService`, `BackupService`, `SyncService`, `PluginService`, `TemporalIdentityService`, `VoiceDraftService` | Singleton |
| **Shell** | `AppNavigationService`, `StatusBarService`, `AnnunciatorService`, `ThemeService`, `SystemTrayService` | Singleton |
| **ViewModels and pages** | `ChatViewModel`, `SearchViewModel`, `ChatPage`, `SearchPage` | Transient |

`AgentXDbContext` is registered with a factory that passes the `IEncryptedConnectionFactory`, so the
database key can be applied to its connection.

### 4. One Shared, Serialized Database Context

One `AgentXDbContext` serves the UI thread and all background work (the indexing loop, the local
REST API, status polling, scheduled backups and sync, connector timers). A `DbContext` is not
thread-safe, so this one serializes itself on one gate:

- `SerializingConcurrencyDetector` replaces EF Core's concurrency detector, so an overlapping
  operation waits for the one in flight instead of throwing.
- `SerializingQueryCompiler` holds the gate for whole query executions and enumerator disposal.
- `SaveChanges` and `SaveChangesAsync` run under the gate and discard the pending changes of a
  failed save, so a rejected change is not replayed by every later save.
- Raw ADO.NET work on the shared connection joins the gate with
  `using (db.EnterDatabaseGate()) { ... }`.

Every caller waits on the same gate, so services keep database sections short and do slow work
(embedding, model calls, file I/O) outside them.

## Startup Sequence

`App.OnLaunched` does the following, in order (details in docs/ARCHITECTURE.md):

1. Builds the host and registers services.
2. Applies the saved UI language (`ILocalizationService.InitializeAsync`) **before** the shell is
   built, so the window's `x:Uid` strings load in that language.
3. Creates `MainWindow` and shows it (the Dashboard shell appears at once).
4. Runs the core initialization, which is awaited internally:
   - finishes or undoes an interrupted encryption change (`RecoverIfNeeded`), unlocks an encrypted
     database, and applies the key to the shared connection;
   - `StartupOrchestrator` runs the migration; on failure the app shows a recovery dialog and
     exits without starting any data-backed feature. On success it opens the data-ready gate,
     starts the local REST API, and initializes the built-in calendar and email connectors;
   - initializes FTS5, resumes auto-sync, marks interrupted workflow runs, initializes the AI
     service, feature flags and theme, activates enabled plugins, starts scheduled backups,
     starts the indexing pipeline, and starts watch-folder monitoring.

On shutdown the connectors are stopped (capped at 15 seconds), then the REST API, scheduled backups
and plugins, and the host is disposed.

## Key Architectural Components

### 1. Chat

A message goes through `MessagingCoordinator` to `ChatService`:

1. The user message is saved.
2. **Model routing** (when turned on) picks the provider and model for this one reply; the
   app-wide provider does not change.
3. **Context assembly** (`ContextAssemblyService`) fits the history into the context window,
   summarizes older overflow, adds memories and, when budget remains, recalled passages from other
   conversations. Research Mode can add web results for the reply.
4. The reply streams from `IAiService` and is saved with the model that wrote it and the sources
   it cited. Memories are then extracted in the background, and Temporal Identity learns from the
   prompt.

Chat also has two multi-agent modes. Both run through `MultiAgentOrchestrator`, which gives each
agent role its own prompt and calls the active model through `IAiService.ChatAsync`:

```
User message (multi-agent mode)
    |
    v
MultiAgentOrchestrator
    |-- Parallel: Researcher, Critic and Synthesizer answer independently
    |-- Debate:   Researcher, Critic and Creative argue for 2 rounds, each
    |             seeing the positions of the rounds before
    v
A synthesis document assembled from the agents' answers (consensus,
disagreements, each agent's contribution), without another model call,
saved to the conversation like a standard reply
```

The agents see the prompt, the active system prompt and, in Research Mode, the web results. There
is no tool calling: no agent can run a search, open a file or capture the screen on its own.

The empty chat states where messages go: `PrivacyStatusService.EvaluateChatMessage` classifies the
active provider, a remote Ollama host, model routing and Research Mode web search, and the chat
shows "100% Private" only when nothing leaves the computer.

### 2. RAG Pipeline (Ask Your Files)

`RagPipeline.AskAsync` runs these stages. A stage that fails is logged and skipped:

```
Question
  1. Multi-query expansion (MultiQueryGenerator, 3 variations)
  2. HyDE: a hypothetical answer used as one more query (questions of 80+ characters)
  3. Hybrid search for every query (semantic + FTS5 keyword, Reciprocal Rank Fusion),
     de-duplicated by chunk; no results -> a fixed "nothing found" answer, no model call
  4. PII redaction of the retrieved text (before any stage sends text to a model)
  5. Heuristic reranking (RagReranker: near-duplicate removal, query-term boost,
     document diversity)
  6. LLM reranking (LlmReranker, when more than 2 chunks)
  7. Parent document retrieval (neighboring chunks), redacted again
  8. Contextual compression (ContextualCompressor)
  9. Research Mode web search, when requested and configured
 10. Numbered-context system prompt; the answer streams at temperature 0.3
 11. Citation extraction ([1], [2] mapped to documents and pages)
 12. Sampled answer evaluation (RagEvaluator)
```

Top-K comes from the Top-K setting (capped by `Rag:MaxTopK`); the other parameters come from the
`Rag` section of `appsettings.json`.

### 3. Search

`HybridSearchOrchestrator` routes a query by mode:

- **Semantic**: `SemanticSearchService` embeds the query and searches the vector store, then loads
  the chunks and documents and applies collection, file type and date filters.
- **Keyword**: `KeywordSearchService` runs an FTS5 `MATCH` over `fts_chunks` (Porter stemmer,
  Unicode tokenizer), with the filters inside the SQL, ranked by BM25.
- **Hybrid**: both run in parallel with a larger candidate pool, and results merge by Reciprocal
  Rank Fusion (`k = 60`). If one backend fails, the other's results are returned.

The Semantic Search page starts in semantic mode and offers all three; the RAG pipeline uses the
mode in `Rag:DefaultSearchMode` (hybrid). Results are cached by `SearchCacheService`; indexing and
deletes invalidate the cache.

### 4. Document Processing and Indexing

```
File (Knowledge Vault import, watch folder, inbox accept, web import, connector)
    |
    v
DocumentService
    - SHA-256 content hash; a duplicate is reported instead of imported
    - processor: first built-in IDocumentProcessor that accepts the file,
      then processors contributed by active plugins
    - text extracted once; a file that cannot be read is recorded as failed with the reason
    - DocumentEntity saved as "pending"; DocumentPendingIndexing raised
    |
    v
IndexingService (one background loop over a Channel<long>)
    - chunk (ChunkingService: paragraphs, then sentences, then words, with overlap)
    - replace old chunks and vectors
    - embed in batches (EmbeddingService) and store vectors (IVectorStore)
    - index chunk text in FTS5
    - mark "completed" (or "failed" with the reason), then auto-tag
```

Built-in processors: PDF (PDFsharp), DOCX (OpenXML), text and data files, Markdown (Markdig), code
files, images (Windows OCR), audio (Whisper speech-to-text) and web shortcuts (`.url`, `.webloc`).

### 5. Conversation Memory

```
conversations
    - messages (with embeddings for recall across conversations)
    - conversation_summary_snapshots (versioned summaries, embedded)
    - conversation_summary_states (refresh progress, staleness)
    - conversation_theme_memberships -> conversation_theme_clusters
                                          - conversation_theme_daily_metrics
memories (facts extracted from chat, with importance, decay and embeddings)
```

### 6. Vector Store

`VectorStoreFactory` picks the implementation from settings:

- `HnswVectorStore` (default, `EnableHnswIndex = true`): vectors stay in the `vec_embeddings` table
  as the source of truth, and an in-memory HNSW index (HnswLite) answers searches once there are
  more embeddings than `HnswFallbackThreshold` (10,000); below that it scans linearly. The index
  follows the current embedding size and is saved to `hnsw-index.*` files, except when the
  database is encrypted: then it stays in memory and is rebuilt at start.
- `SqliteVecStore`: linear cosine-similarity scan over the same table.

```csharp
public interface IVectorStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<long> InsertEmbeddingAsync(long chunkId, float[] embedding, CancellationToken ct = default);
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryEmbedding, int topK = 5, double minSimilarity = 0.3, CancellationToken ct = default);
    // ... delete, count, optimize, and SuspendAsync / ResumeAsync, which close and reopen its own
    // connection around a restore or an encryption change
}
```

### 7. Temporal Identity

`TemporalIdentityService` records beliefs, belief changes, insights, engagement time and a voice
profile from chat messages, annotations and time spent in viewers (`EngagementTracker`).
`VoiceDraftService` has the active AI provider write Draft As Me text from that record. The Past
Self page and the Dashboard's belief card read it.

## Security Architecture

### 1. Database Encryption

```
Startup
  1. IDatabaseEncryptionMigrator.RecoverIfNeeded: finish or undo an interrupted change
  2. encryption.info.json present? (outside the database)
       DpapiWrapped   -> unwrap the 32-byte key with DPAPI (current Windows user)
       UserPassphrase -> ask for the passphrase (older vaults; PBKDF2-HMAC-SHA256)
  3. IDatabaseKeyProvider holds the key; every connection opened through
     IEncryptedConnectionFactory issues PRAGMA key = "x'<hex>'"
  4. AgentXDbContext.EnsureKeyApplied(), then the migration runner
```

Encryption is turned on from Settings by `DatabaseEncryptionManager`, which exports the database
with `sqlcipher_export`, swaps the file under the database gate with the vector store suspended,
verifies it, and writes the marker file last.

### 2. Secrets

Settings live in `settings.json`. The OpenAI and Anthropic API keys, the web search key, the local
API token, the OAuth client secrets and the backup password are DPAPI-encrypted there
(`SettingsService`). OAuth access and refresh tokens are DPAPI-encrypted in the
`oauth_credentials` table.

### 3. Local REST API

The API listens on loopback only and requires a bearer token (see below). CORS grants go to browser
extension origins only.

### 4. Privacy Disclosure

`PrivacyStatusService` derives the privacy posture from the settings. It discloses the active
provider when it is a cloud service or an Ollama server on another machine, model routing when a
cloud key is set, a configured web search provider, and calendar or email sync when they are on.
The status strip's `LOCAL`/`NET` lamp and the Dashboard show it.

## Plugin Architecture

A plugin is a `.agentx-plugin` package with a `manifest.json` at its root:

```json
{
  "id": "com.agentx.sample-plugin",
  "name": "Sample Document Processor",
  "version": "1.0.0",
  "author": "AgentX Team",
  "description": "...",
  "pluginType": "DocumentProcessor",
  "minAppVersion": "1.3.0",
  "entryAssembly": "SamplePlugin.dll",
  "permissions": ["Documents"],
  "dependencies": []
}
```

`PluginService` installs a package into `%LocalAppData%\AgentX\Plugins\{id}\` (disabled), loads
enabled plugins into a collectible `AssemblyLoadContext`, and runs `InitializeAsync` and
`ActivateAsync` (30 seconds allowed) at startup; `DeactivateAsync` (10 seconds) runs when a plugin
is disabled or uninstalled and at shutdown. Two plugin types have a host integration:
`DocumentProcessor` (an `IDocumentProcessorPlugin` is offered to imports for formats no built-in
processor reads) and `DataConnector` (through `IInboxService`). Plugins run in-process with the
user's rights: permissions are informational and nothing is sandboxed. The calendar and email
connectors are built-in plugins started by `BuiltinConnectorLifecycleService`.

## Workflow System

A workflow is an ordered list of steps (`workflows`, `workflow_steps`). `WorkflowEngine` runs one
workflow at a time; each step receives the original input and the previous step's output:

```
WorkflowEntity
    - WorkflowStep 1: AiPrompt          (template with {{input}} and {{previous_output}})
    - WorkflowStep 2: DocumentLookup    (asks the vault through the RAG pipeline, optionally one collection)
    - WorkflowStep 3: TextTransform     (uppercase, sort_lines, word_count, ...)
    - WorkflowStep 4: ConditionalBranch (contains, matches, length_greater_than, ...)
    - WorkflowStep 5: OutputFormat      (json, markdown, html, bullet_list, numbered_list)
```

```csharp
public interface IWorkflowEngine
{
    Task<WorkflowRunResult> ExecuteWorkflowAsync(
        long workflowId,
        string input,
        IProgress<WorkflowStepResult>? progress = null,
        CancellationToken ct = default);
    Task CancelExecutionAsync();
    bool IsRunning { get; }
    event EventHandler<WorkflowStepResult>? StepCompleted;
}
```

Each run is recorded in `workflow_runs`.

## REST API Layer

Agent-X exposes a **local REST API** for the browser extension and the Android companion. It is an
`HttpListener` host inside the desktop process (`src/AgentX.Core/Services/Api/ApiHostService.cs`),
bound to loopback only:

```
http://localhost:9846/
  GET  /api/extension/health     public liveness probe (no token)
  GET  /api/auth/check           confirms the bearer token (pairing)
  GET  /api/health               status, version, uptime, document and conversation counts
  GET  /api/documents            all documents
  GET  /api/documents/{id}
  GET  /api/conversations        non-archived conversations
  GET  /api/conversations/{id}
  GET  /api/collections
  POST /api/search               semantic search {query, topK, minScore}
  POST /api/inbox/clip           clip web content into the Smart Inbox
```

Every route except the extension health probe requires `Authorization: Bearer <token>`
(`LocalApiSecurity`, constant-time comparison); the per-install token is stored DPAPI-encrypted
in settings. CORS grants go to browser-extension origins only. Every response uses the
`{success, data, error, timestamp}` envelope (`ApiResponse<T>`).

**Implementation:**
```csharp
public interface IApiHostService
{
    bool IsRunning { get; }
    int Port { get; }
    string BaseUrl { get; }
    Task StartAsync(int port = 9846, string? authToken = null, CancellationToken ct = default);
    void SetAuthToken(string? authToken); // takes effect on the next request, no restart
    Task StopAsync(CancellationToken ct = default);
}
```

`ApiHostLifecycleService` (App) starts the host at launch when the Local API is enabled and
provisions the token on first start; its `ApplySettingsAsync` re-applies the enable toggle and the
token when Settings are saved or the token is regenerated. The full route reference is in
[API_ENDPOINTS.md](API_ENDPOINTS.md#local-rest-api). How the phone reaches the loopback listener
(USB `adb reverse`, or the emulator's `10.0.2.2`) is in
[docs/MOBILE-TRANSPORT.md](docs/MOBILE-TRANSPORT.md).

## Localization Architecture

The UI ships in six languages: English (en-US), German (de), Spanish (es), French (fr), Japanese
(ja) and Simplified Chinese (zh-CN).

```
src/AgentX.App/Strings/
  en-US/Resources.resw    (canonical)
  de/Resources.resw
  es/Resources.resw
  fr/Resources.resw
  ja/Resources.resw
  zh-CN/Resources.resw
```

- XAML uses `x:Uid`; code uses `ILocalizationService.GetString(key)` and `FormatPlural`, which
  read the same resources through MRT Core (`WinUIResourceLoaderAdapter`).
- The language chosen in Settings (`LanguageOverride`) is applied at startup before the shell is
  built; changing it takes full effect after a restart.
- Relative times ("5m ago") are worded in Core through `FormatHelper.LocalizedText`, which startup
  points at the localization service.
- CI (`.github/workflows/locale-audit.yml`) runs `tools/LocaleAudit` with `--fail-below 98` and the
  `LocaleAudit.Tests` suite.

## Performance Notes

- Database work is serialized on one gate, so background services keep each section short and do
  embedding, model calls and file I/O outside it.
- Indexing runs one document at a time on a background loop; embeddings are generated in batches
  (`Rag:EmbeddingBatchSize`, 32) and cached per text and embedding model in a bounded
  least-recently-used cache (`CachedEmbeddingService`).
- The HNSW index answers searches above 10,000 embeddings; smaller vaults use a linear scan.
- Keyword filters run inside the FTS5 query, so matches inside the chosen collection or date range
  are not lost behind the top-K cut.
- Rebuilding the vector index at start runs on the thread pool.

## Build Configuration

### Build Commands

```bash
# Build the solution (the platform must be given; a bare build fails on win-anycpu)
dotnet build AgentX.sln -p:Platform=x64

# Run the tests
dotnet test tests/AgentX.Tests/AgentX.Tests.csproj -p:Platform=x64

# Publish the self-contained app the installer packages
dotnet publish src/AgentX.App/AgentX.App.csproj -c Release -r win-x64 --self-contained -o publish/win-x64
```

The app targets x86, x64 and ARM64; the installer ships x64.

## Deployment Architecture

```
Installer (Inno Setup, installer/AgentX-Setup.iss)
    SLIM (default)     no bundled model; the first-run wizard offers to download it
    OFFLINE            ISCC /DAgentXOffline=1, bundles the ~1.9 GB Llama 3.2 3B GGUF
    Installs to        {autopf}\Agent-X (per-user by default, no elevation needed)
    Executable         AgentX.App.exe (unpackaged WinUI app, Windows App SDK bundled)

Runtime data            %LocalAppData%\AgentX\
    agentx.db           SQLite database (optionally SQLCipher-encrypted)
    settings.json       settings (secrets DPAPI-encrypted)
    Logs\               agentx-yyyyMMdd.log, 7 days kept
    Models\             built-in model and the Whisper speech-to-text model
```

There is no MSIX package and no Store distribution. Uninstalling removes the log files and keeps the
database, settings and models.

## Monitoring and Logging

### Serilog Configuration

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Debug()
    .WriteTo.File(
        logPath,                               // %LocalAppData%\AgentX\Logs\agentx-.log
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .Enrich.WithProperty("Application", "AgentX")
    .CreateLogger();
```

Unhandled exceptions are logged from `AppDomain.UnhandledException`,
`TaskScheduler.UnobservedTaskException` and `Application.UnhandledException` (the last is marked
handled). There is no telemetry: `IAnalyticsService` only queries the local database for the
Analytics page, and `RagMetrics` keeps RAG counters in memory.

## Extension Points

### Adding an AI Provider

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

Providers are created by `AiService.InitializeAsync` from settings (`local`, `ollama`, and `openai`
and `anthropic` when their keys are set).

### Adding a Document Processor

```csharp
public interface IDocumentProcessor
{
    IReadOnlySet<string> SupportedExtensions { get; }
    bool CanProcess(string filePath);
    Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default);
}

// Register in App.xaml.cs; registration order decides which processor wins a shared extension.
services.AddSingleton<IDocumentProcessor, CustomProcessor>();
```

A plugin can add one without rebuilding the app by implementing `IDocumentProcessorPlugin`.

## Architecture Decision Records (ADRs)

### ADR-001: SQLite + SQLCipher for Database
**Decision:** Use SQLite, optionally encrypted with SQLCipher, for all local data.

**Rationale:**
- Zero-configuration embedded database, one file per user
- Encryption at rest without an external service
- EF Core migrations for schema changes

**Consequences:**
- Trade-off: one context and one connection serve the whole app
- Mitigation: the context serializes its own operations on one gate (see above), and raw SQL joins
  the gate
- Benefit: simple deployment and backup (one file plus settings)

### ADR-002: HnswLite for the Vector Index
**Decision:** Keep vectors in SQLite and search them with an in-memory HNSW index (HnswLite), with a
linear scan for small vaults.

**Rationale:**
- Approximate nearest-neighbor search for large vaults
- Managed library, no native SQLite extension to deploy
- SQLite stays the source of truth, so the index can always be rebuilt

**Consequences:**
- Trade-off: the index is rebuilt from the database when its files are missing, stale or not
  allowed (encrypted database)
- Benefit: the same `IVectorStore` contract with or without the index

### ADR-003: WinUI 3 over WPF
**Decision:** Use WinUI 3 (Windows App SDK) instead of WPF.

**Rationale:**
- Current Windows UI stack with Fluent controls and Mica/Acrylic backdrops
- Unpackaged, self-contained deployment with an ordinary installer

**Consequences:**
- Trade-off: Windows 10 version 2004 (build 19041) or later only
- Trade-off: file pickers and dialogs need the window handle or `XamlRoot`, so they stay in
  code-behind or take the window explicitly

### ADR-004: Coordinators for Chat
**Decision:** Put the chat workflows in coordinators between `ChatViewModel` and the Core services.

**Rationale:**
- Chat is the most complex page (streaming, regeneration, branching, voice, multi-agent modes)
- Coordinators can be unit tested without WinUI

**Consequences:**
- Trade-off: one more layer for chat only; other pages call services directly
- Benefit: `ChatViewModel` keeps UI state, the coordinators keep behavior

## Current Limits

- Single-user desktop app; the database and settings belong to one Windows account.
- Tool calling is not implemented: models and agents cannot run searches or open files themselves.
- The mobile companion reaches the desktop only over loopback (USB or emulator); there is no LAN
  transport.
- Plugins are not sandboxed.

---

**Last Updated:** 2026-09-27
