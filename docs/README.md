# Agent-X — Intelligence Hub

**Local-First AI-Powered Document Intelligence for Windows**

Agent-X is a native Windows desktop application that transforms your personal document collection into a queryable, AI-augmented knowledge base. Import documents, ask questions in natural language, search semantically across your entire vault, and interact with large language models — local by default, with nothing leaving your machine unless you explicitly opt in to a cloud model provider or web search. No cloud subscription, no telemetry, no internet dependency for the core experience.

**What makes Agent-X different:**

- **Bundled Local Model**: Ships with Llama 3.2 3B — fully functional offline AI out of the box (~2 GB included in installer)
- **Enterprise-Grade RAG**: 6-stage retrieval pipeline with multi-query expansion, HyDE embeddings, LLM reranking, and citation chaining
- **Production Data Layer**: SQLCipher AES-256-CBC encryption, EF Core migrations, 37-table relational schema, HNSW ANN vector index
- **Comprehensive Feature Set**: 29 navigation pages, 85+ services, 2,773 unit tests, workflow automation, analytics dashboard, REST API
- **GPU-Accelerated**: CUDA 12 support with automatic VRAM-based layer offloading for 2-50x inference speedup

Built on .NET 8.0 and WinUI 3 (Windows App SDK 1.6), Agent-X delivers an enterprise-grade document intelligence pipeline — chunking, embedding, vector search, retrieval-augmented generation, knowledge graph visualization, and AI memory — as a self-contained, privacy-first Windows application.

> **Version:** 2.1.2 ("Bedrock" security & supply-chain hardening — the full Codex audit and the 2026-06-19 QA audit, AX-QA-001…016, are closed; see [CHANGELOG](../CHANGELOG.md))
> **Build:** 2,773 unit tests | 85+ services | 29 navigation pages | 37 database tables | 6 supported locales
> **Publisher:** Rocky Elsalaymeh / Strategia-X
> **Platform:** Windows 10 19041+ (x64)
> **License:** MIT — see [LICENSE](../LICENSE)

---

## Table of Contents

1. [Feature Overview](#feature-overview)
2. [Screenshots](#screenshots)
3. [Prerequisites](#prerequisites)
4. [Installation](#installation)
5. [Build from Source](#build-from-source)
6. [Configuration](#configuration)
7. [Application Architecture](#application-architecture)
8. [Pages and Navigation](#pages-and-navigation)
9. [Core Service Layer](#core-service-layer)
10. [Data Storage](#data-storage)
11. [Keyboard Shortcuts](#keyboard-shortcuts)
12. [Pricing](#pricing)
13. [Contributing](#contributing)
14. [License](#license)

---

## What's New in the v2.1 "Bedrock" Line

The current release is **v2.1.2** — a security & supply-chain hardening patch that closes the full Codex security audit and the 2026-06-19 QA audit (findings AX-QA-001…016): an authenticated local API with contained file boundaries and encrypted secrets, hardened mobile transport, the dormant vulnerable SQLite binary removed, single-source version display, a green and CI-gated mobile Android build, and a signing-ready release pipeline with a provenance gate. See the [CHANGELOG](../CHANGELOG.md) for the complete v2.1.1 → v2.1.2 history. The v2.1 "Bedrock" data layer it builds on:

### Data-Layer Hardening

| Feature | Impact |
|---|---|
| **SQLCipher Encryption** | AES-256-CBC at-rest database encryption with automatic DPAPI-wrapped key management tied to your Windows account |
| **EF Core Migrations** | Production-ready migration runner with pending-migration API and baseline adoption for pre-existing installs |
| **Out-of-DB Key Storage** | Encryption state separated from the encrypted vault via `encryption.info.json` — prevents unlock ↔ migration chicken-and-egg |
| **Startup Unlock Flow** | Clean authentication experience before database access |

### Feature Highlights

| Category | New Since v1.0 |
|---|---|
| **Local AI** | Bundled Llama 3.2 3B model — fully offline AI out of the box |
| **GPU Acceleration** | CUDA 12 support with automatic VRAM-based layer offloading |
| **Advanced RAG** | Multi-query retrieval, HyDE embeddings, LLM reranking, parent document expansion, contextual compression |
| **Enterprise Features** | REST API, Analytics Dashboard, Collaborative Sync, Calendar/Email Connectors |
| **UX Polish** | Per-message actions, inline editing, code syntax highlighting (18 languages), notification system |
| **Developer Quality** | 2,773 unit tests, validation layer, typed exceptions, structured logging, feature flags |

---

## Feature Overview

Agent-X now spans a broader product surface than a simple feature checklist. The tables below highlight representative capabilities across core productivity, intelligence, and advanced local-first workflows. Every capability is free and unconditionally available to every user. All features run offline first; cloud AI providers (OpenAI, Anthropic) are optional and user-configured.

### Core Productivity

| Feature | Description |
|---|---|
| **Built-in Local LLM** | Llama 3.2 3B Instruct bundled in the installer (~2 GB) — fully functional offline AI out of the box |
| Conversation Export | Copy any conversation to clipboard or save as a Markdown file with a single action |
| Conversation Search | Filter the conversation sidebar by title or content in real time |
| Document Preview Panel | 360 px right-side panel inside Knowledge Vault renders file metadata, content preview, tags, and collection membership without leaving the page |
| Auto-Generate Titles | AI generates a concise, descriptive title for each imported document based on its extracted content |
| Persistent Search History | Every search query is persisted to SQLite and displayed as clickable history chips on the Search page |

### Differentiated Intelligence

| Feature | Description |
|---|---|
| Hybrid Search | Runs semantic (vector cosine similarity) and keyword (SQLite FTS5) searches in parallel, then merges results using Reciprocal Rank Fusion (RRF, k=60) for a unified relevance ranking |
| Auto-Tagging System | On import, the AI analyzes document content (up to 2,000 characters) and assigns confidence-scored tags stored in the relational tag graph |
| Batch Document Operations | Multi-select documents in Knowledge Vault for bulk delete, re-index, or collection assignment without per-document navigation |
| Advanced Filtering | Filter the document vault by file type, collection, date range, indexing status, and sort order through a composable filter panel |
| Chat Code Rendering | Markdown parsing via Markdig renders AI responses with syntax-highlighted code blocks and one-click copy buttons |

### Advanced Intelligence

| Feature | Description |
|---|---|
| **GPU Acceleration** | CUDA 12 support with automatic VRAM detection and layer offloading (2-8+ GB tiers) for 2-50x inference speedup |
| **Advanced RAG Pipeline** | Multi-query retrieval, HyDE embeddings, LLM-based reranking, parent document expansion, and contextual compression for superior citation quality |
| Knowledge Graph Visualization | Interactive force-directed graph (spring-electric algorithm, 100 iterations) renders documents, collections, and tags as typed nodes with weighted edges showing shared membership; rendered on a WinUI 3 Canvas |
| Multi-Provider LLM Support | Unified AI service abstraction over Bundled Local, Ollama (local), OpenAI (GPT-4o and family), and Anthropic (Claude family) with per-provider cost tracking |
| Conversation Memory | AI extracts facts, preferences, instructions, and topics of interest from conversations; stores them as importance-weighted memory entities; injects the top memories into system prompts for personalized future interactions; generates suggested follow-up questions; and now persists durable conversation-summary snapshots for longer-lived context |
| Semantic Deduplication | SHA-256 hash check on every import detects exact duplicates before incurring any AI cost; near-duplicate detection uses vector embedding similarity for semantic overlap identification |
| Scheduled Digest Reports | Weekly activity summaries aggregate new document counts, conversation activity, top searches, file type distribution, storage delta, and token consumption into persisted digest reports |
| Analytics & Conversation Intelligence | Analytics aggregates usage, performance, file-type, and durable conversation-intelligence metrics, including summary freshness and recent summary previews |
| **REST API** | Embedded HTTP listener (port 9846) with endpoints for documents, conversations, collections, and search |
| **Database Encryption** | SQLCipher AES-256-CBC at-rest encryption with automatic DPAPI-wrapped key management |

### Developer Quality Metrics

| Metric | Value |
|---|---|
| **Unit Tests** | Comprehensive test suite across Settings, Collections, Export, Search Cache, and all validators |
| **Code Coverage** | Critical paths in AI, search, indexing, and data layers fully tested |
| **Validation Layer** | `IValidator<T>` with typed validators for AppSettings, SyncConfiguration, PluginManifest |
| **Error Handling** | 7 typed exception classes with structured error propagation |
| **Logging** | Serilog with 7-day rolling retention |
| **Feature Flags** | 15 feature gates for experimental capabilities and phased rollouts |

### UX Polish Features

| Feature | Details |
|---|---|
| Per-Message Actions | Copy, delete, regenerate, thumbs up/down feedback on every chat bubble |
| Message Editing | Inline edit with "Save & Resend" that truncates subsequent messages |
| Code Syntax Highlighting | 18 languages with One Dark Pro color palette |
| Notification System | Toast overlay with severity icons and auto-dismiss |
| Keyboard Shortcuts | 18+ global shortcuts with command palette (`Ctrl+K`) |
| Theme Toggle | Dark/Light/System Default with instant switching |
| Conversation Folders | Organize conversations into Work, Research, Personal, Archive |

---

## Screenshots

Screenshots are located in the `/screenshots/` directory at the repository root. The directory is populated during development builds; the release installer does not include screenshots.

Suggested screenshots to capture before release:

- Dashboard with populated statistics cards
- AI Chat page with streaming response and rendered code block
- Knowledge Vault with document preview panel open
- Semantic Search results with citation excerpts highlighted
- Knowledge Graph visualization with multiple node types
- Model Manager showing Ollama model list with download progress

---

## Prerequisites

### Required

| Requirement | Minimum Version | Notes |
|---|---|---|
| Windows | 10, build 19041 (version 2004) | Windows 11 is fully supported and recommended |
| Architecture | x64 | The installer and published binary target win-x64 |

### Strongly Recommended

| Requirement | Notes |
|---|---|
| **Bundled Model** | Included with installer — Llama 3.2 3B provides ~3 tokens/sec on CPU, ~15-25 tokens/sec on mid-range GPUs |
| Ollama (Optional) | Download from [https://ollama.com](https://ollama.com). Agent-X auto-detects Ollama at `http://localhost:11434`. Pull at minimum one chat model (e.g., `llama3.2`, `phi4`, `mistral`) and one embedding model (e.g., `nomic-embed-text`, `mxbai-embed-large`) |
| GPU (NVIDIA or AMD) | **CUDA 12 support** for NVIDIA GPUs enables automatic layer offloading based on VRAM (2-8+ GB tiers). CPU inference works but is significantly slower (5-10x) |
| RAM | 8 GB minimum (bundled model); 16 GB recommended for 7B models; 32 GB+ for 30B+ models |

**Performance expectations by hardware tier:**

| Configuration | Chat Model | Expected Speed |
|---|---|---|
| CPU-only, 8 GB RAM | Llama 3.2 3B (bundled) | ~2-4 tokens/sec |
| NVIDIA RTX 3060 (8 GB VRAM) | Llama 3.1 8B, Phi 4 | ~8-15 tokens/sec |
| NVIDIA RTX 4090 (24 GB VRAM) | Llama 3.1 70B, Mixtral 8x7B | ~30-50 tokens/sec |

### Optional (Cloud AI Providers)

| Provider | Requirement |
|---|---|
| OpenAI | API key configured in Settings; billed per token at standard OpenAI rates |
| Anthropic | API key configured in Settings; billed per token at standard Anthropic rates |

### Build Prerequisites

| Requirement | Minimum Version |
|---|---|
| .NET SDK | 8.0 |
| Windows App SDK Workload | Installed via `dotnet workload install windowsdesktop` |
| Visual Studio (optional) | 2022 17.8+ with "Windows application development" workload |
| Inno Setup (installer only) | 6.x — available at [https://jrsoftware.org/isinfo.php](https://jrsoftware.org/isinfo.php) |

---

## Installation

### Installer (Recommended)

1. Download `AgentX-Setup-2.1.2-x64.exe` (SLIM) — or `AgentX-Setup-2.1.2-x64-offline.exe` (OFFLINE, model bundled) — from the releases page or the `installer-output/` directory.
2. Run the installer. It does not require administrator privileges by default (installs to `%LocalAppData%\Programs\Agent-X` unless elevated).
3. The installer automatically creates the application data directories at `%LocalAppData%\AgentX\`.
4. **The bundled Llama 3.2 3B model (~2 GB) is installed automatically**, giving you fully functional offline AI out of the box — zero additional downloads required.
5. Launch Agent-X from the Start Menu or desktop shortcut.
6. On first launch, the onboarding wizard runs and guides you through built-in model verification, optional Ollama connection, GPU detection, and cloud provider configuration.

**What you get immediately after installation:**
- ✓ Fully functional local AI (Llama 3.2 3B) — no internet required
- ✓ GPU acceleration auto-detection (CUDA 12 for NVIDIA GPUs)
- ✓ Complete document intelligence pipeline (indexing, search, RAG)
- ✓ 2,773 unit tests across 29 navigation pages
- ✓ Database encryption ready (SQLCipher AES-256-CBC)

### Uninstall

Use the "Add or Remove Programs" entry in Windows Settings, or run the uninstaller from the Start Menu group. Log files at `%LocalAppData%\AgentX\Logs\` are removed on uninstall; the database (`agentx.db`) and settings (`settings.json`) are preserved to protect your data.

---

## Build from Source

### Clone the Repository

```bash
git clone <repository-url>
cd Agent-X
```

### Restore and Build

```bash
# Restore NuGet packages and build all projects
dotnet build

# Build in Release configuration
dotnet build -c Release
```

### Run the Application

```bash
# Run with debug output to the debug console
dotnet run --project src/AgentX.App
```

### Run Tests

```bash
dotnet test
```

### Publish Self-Contained Binary

The published output is what the Inno Setup installer packages. Publishing produces a self-contained, ReadyToRun-compiled binary that does not require the .NET runtime to be pre-installed on the target machine.

```bash
dotnet publish src/AgentX.App/AgentX.App.csproj \
  -c Release \
  -r win-x64 \
  --self-contained \
  -o publish/win-x64
```

### Build the Installer

Requires Inno Setup 6 to be installed and `ISCC.exe` to be on the system PATH, or specify the full path.

```bash
ISCC.exe installer/AgentX-Setup.iss
```

The installer output is written to `installer-output/AgentX-Setup-1.0.0-x64.exe`.

### Platform Targets

The project supports three runtime identifiers. Only win-x64 is currently packaged by the installer.

| Runtime ID | Architecture |
|---|---|
| `win-x64` | 64-bit Intel/AMD (recommended) |
| `win-x86` | 32-bit (not installer-packaged) |
| `win-arm64` | ARM64 (not installer-packaged) |

---

## Configuration

### Application Settings File

Agent-X stores user preferences in `%LocalAppData%\AgentX\settings.json`. This file is created automatically on first launch with defaults. It is a plain JSON document and can be edited manually while the application is closed.

Key settings fields include:

| Field | Default | Description |
|---|---|---|
| `ollamaEndpoint` | `http://localhost:11434` | Ollama server base URL |
| `selectedModelId` | (empty) | Active Ollama model for chat |
| `embeddingModelId` | `nomic-embed-text` | Model used to generate embeddings |
| `aiProvider` | `ollama` | Active AI provider: `ollama`, `openai`, or `anthropic` |
| `openAiApiKey` | (empty) | OpenAI API key (stored locally, never transmitted by Agent-X) |
| `anthropicApiKey` | (empty) | Anthropic API key (stored locally, never transmitted by Agent-X) |
| `storagePath` | `%LocalAppData%\AgentX` | Root path for the SQLite database and vector store |
| `chunkSize` | `512` | Target token count per document chunk |
| `chunkOverlap` | `64` | Token overlap between consecutive chunks |
| `maxSearchResults` | `10` | Default number of results returned by search |
| `onboardingCompleted` | `false` | Set to `true` after the onboarding wizard is dismissed |
| `theme` | `dark` | UI theme: `dark` or `light` |

### Environment Variables

Agent-X does not read API keys from environment variables by design — all credentials are stored in `settings.json` and managed through the Settings page UI. This is an explicit privacy decision: secrets are kept in the user's own profile directory, under the user's control, and are never embedded in the application binary or transmitted anywhere.

### AI Provider Configuration

Navigate to **Settings** in the application to configure AI providers:

- **Ollama:** No key required. Set the endpoint URL if Ollama runs on a non-default port or a remote host (e.g., a local network GPU server).
- **OpenAI:** Paste your API key. The model selector populates from the OpenAI models list endpoint.
- **Anthropic:** Paste your API key. Claude models are listed from the Anthropic messages API.

Provider switching is live — the active provider is changed immediately and persisted to settings.

### Logging

Structured logs are written to `%LocalAppData%\AgentX\Logs\agentx-YYYYMMDD.log` using a daily rolling strategy. The last 7 days of logs are retained; older files are automatically deleted. Log entries use the format:

```
2026-02-27 14:32:01.123 [INF] Database initialized at Data Source=C:\Users\...\AgentX\agentx.db
```

Log files are useful for diagnosing indexing failures, AI provider connection issues, and database errors. The log level is set to `Debug` in the current build, which produces verbose output during development.

---

## Application Architecture

Agent-X is structured as a two-project .NET solution with a strict dependency direction: the UI project depends on Core; Core has no dependency on the UI project.

```
AgentX.sln
├── src/
│   ├── AgentX.App/          -- WinUI 3 presentation layer
│   └── AgentX.Core/         -- Platform-independent business logic and data layer
└── tests/                   -- Unit and integration tests
```

### AgentX.App (Presentation Layer)

The application host project. Responsibilities:

- **Dependency Injection Composition Root:** `App.xaml.cs` builds the `IHost` using `Microsoft.Extensions.Hosting`, registers every service and view model as singleton or transient, and resolves the dependency graph before the main window is shown.
- **Main Window Shell:** `MainWindow.xaml.cs` manages the `NavigationView`, `Frame`, command palette overlay, keyboard shortcut dispatch, live status bar (Ollama connection state, indexing progress, document count), and system backdrop (Mica Alt with Acrylic fallback).
- **Views:** WinUI 3 pages span intelligence, knowledge, triage, system, onboarding, help, and legal surfaces, each resolved through the DI container.
- **ViewModels:** Page-specific, dialog, and support ViewModels use `CommunityToolkit.Mvvm.ComponentModel.ObservableObject`, `[ObservableProperty]`, and `[RelayCommand]` source generation.
- **Onboarding Flow:** First-run detection hides the navigation pane and presents a focused onboarding wizard; navigation is restored and Dashboard is loaded on completion.
- **Controls:** Reusable XAML controls including the Command Palette overlay.

### AgentX.Core (Business Logic and Data Layer)

The portable class library. Responsibilities:

- **AI Subsystem** (`AI/`): Provider abstraction (`IAiProvider`), multi-provider service (`IAiService`), embedding generation (`IEmbeddingService`), context window management (`IContextWindowManager`), model enumeration (`IModelManager`), hardware detection (`IHardwareDetector`), cost tracking (`ICostTracker`), and retry policy (`IRetryPolicy`).
- **Chat Subsystem** (`Services/Chat/`): Conversation persistence (`IConversationService`), message streaming orchestration (`IChatService`), system prompt management (`ISystemPromptService`), and AI memory extraction and injection (`IConversationMemoryService`).
- **Document Processing** (`Documents/`): Document ingestion and metadata extraction (`IDocumentService`), pluggable processor pipeline (`IDocumentProcessor`), and text chunking with configurable size and overlap (`IChunkingService`).
- **Indexing Pipeline** (`Services/Indexing/`): Asynchronous queue-based indexing (`IIndexingQueueService`, `IIndexingService`), file system watcher for watch folder auto-import (`IFileWatcherService`).
- **Search and RAG** (`Search/`): Vector cosine similarity search (`ISemanticSearchService`), SQLite FTS5 keyword search (`IKeywordSearchService`), hybrid orchestration with RRF fusion (`IHybridSearchOrchestrator`), source citation extraction (`ICitationService`), LLM-based reranking (`IRagReranker`), and the full RAG pipeline (`IRagPipeline`).
- **Intelligence Services** (`Services/Intelligence/`): Document summarization (`ISummaryService`), duplicate detection via SHA-256 and semantic similarity (`IDuplicateDetectionService`), organization suggestions (`IOrganizationSuggestionService`), knowledge graph construction with force-directed layout (`IKnowledgeGraphService`), and digest report generation (`IDigestService`).
- **Collections and Tagging** (`Services/Collections/`, `Services/Tagging/`): Hierarchical collection management (`ICollectionService`) and AI-powered tag generation with confidence scoring (`IAutoTagService`).
- **Data Layer** (`Data/`): Entity Framework Core DbContext with 16 entity types mapped to SQLite, vector embedding storage via `IVectorStore` (`HnswVectorStore` when enabled, `SqliteVecStore` fallback for small/disabled indexes), `IMigrationRunner` applying EF Core migrations on startup with baseline-adoption for pre-B9 installs (v2.1 Bedrock B9), `IEncryptedConnectionFactory` + `IDatabaseKeyService` routing every `SqliteConnection` through SQLCipher `PRAGMA key` (v2.1 Bedrock C13), and `IDatabaseEncryptionMigrator` for atomic plaintext→encrypted conversion via `sqlcipher_export`.
- **Settings** (`Services/Settings/`): JSON-file settings persistence (`ISettingsService`) with at-rest encryption of any secrets.

### Dependency Injection Pattern

All services are registered as singletons at application startup in `App.xaml.cs`. ViewModels and Views are registered as transients — a new instance is created for each navigation to a page, which simplifies lifecycle management in the absence of a navigation cache. The DI container is accessed via `App.GetService<T>()` throughout the application.

```csharp
// Example: resolving a service from outside the constructor
var aiService = App.GetService<IAiService>();
```

### Startup Sequence

1. `App.OnLaunched` builds the `IHost` and calls `InitializeCoreServicesAsync`.
2. `InitializeCoreServicesAsync` (fire-and-forget): reads the `%LocalAppData%\AgentX\encryption.info.json` keystore (when present) and unlocks the database key via `IDatabaseKeyService` (DPAPI-wrap, or PBKDF2-HMAC-SHA256 passphrase for legacy keystores); applies pending EF Core migrations via `IMigrationRunner.RunAsync()` with baseline-adoption for pre-B9 installs; initializes FTS5 virtual tables; and calls `IAiService.InitializeAsync` to connect to Ollama.
3. `MainWindow` is instantiated and shown immediately — initialization continues in the background.
4. The main window checks onboarding status; if not completed, it hides the navigation pane and navigates to `OnboardingPage`.
5. The status bar polling timer fires after a 5-second delay and every 30 seconds thereafter, updating the Ollama connection dot, active model name, indexing progress ring, and document count.

### System Backdrop

Agent-X uses Mica Alt (`MicaKind.BaseAlt`) on supported systems for a deep, material-aware title bar and window background. It falls back to Desktop Acrylic and finally to a solid dark background on systems that do not support composited backdrops (e.g., older GPU drivers or RDP sessions).

---

## Pages and Navigation

Navigation is managed by a `NavigationView` in `MainWindow.xaml`. The `ContentFrame` loads pages as transient instances. The nav rail carries the primary product map, while the command palette and keyboard shortcuts expose a curated set of high-frequency destinations and actions.

| Page | Nav Tag | Description |
|---|---|---|
| Dashboard | `Dashboard` | Activity summary with statistics cards: total documents, conversations, searches, and storage. Quick-access cards link to frequently used pages. |
| Weekly Digest | `Digest` | Reads and displays the most recent `DigestReportEntity`. Shows document import counts, conversation highlights, top search queries, file type distribution, storage delta, and token consumption for the selected 7-day period. |
| Analytics | `Analytics` | Aggregates usage, performance, indexing, model, file-type, and conversation-intelligence metrics, including durable summary freshness and recent summary previews. |
| AI Chat | `Chat` | Full-featured chat interface. Streams responses token-by-token from the active provider. Supports multiple conversations, pinning, system prompt selection, model switching, conversation export (clipboard and `.md` file), and conversation search sidebar filter. Renders markdown with syntax-highlighted code blocks. |
| Ask Your Files | `AskFiles` | RAG-powered question answering against the document vault. Retrieves relevant document chunks via hybrid search, reranks them, injects them into a context-aware prompt, and streams a grounded answer with source citations. |
| Quick Actions | `QuickActions` | One-click document intelligence surface for layered summaries, exact duplicate scans, semantic near-duplicate evidence, and organization actions. |
| Workflows | `Workflows` | Multi-step prompt workflow builder and runner for repeatable AI-powered document or text-processing sequences. |
| Knowledge Vault | `KnowledgeVault` | Document library with list/grid view toggle, multi-select for batch operations (delete, re-index, assign to collection), advanced filter panel (file type, collection, date range, status, sort), and 360 px document preview panel. |
| Web Import | `WebImport` | Imports articles, feeds, and scraped web content into the vault for later search, RAG, and analysis. |
| Collections | `Collections` | Hierarchical collection manager. Create, rename, delete, and nest collections. Drag documents between collections. |
| Semantic Search | `Search` | Unified search page with mode toggle (Semantic / Keyword / Hybrid). Displays results with relevance scores, source excerpts, and citation links. Persistent search history displayed as chips. |
| Knowledge Graph | `KnowledgeGraph` | Interactive Canvas-rendered force-directed graph. Nodes are color-coded by type (blue = document, purple = collection, amber = tag). Edges indicate collection membership, tag assignment, and shared-connection relationships. Supports pan and zoom. |
| Compare Documents | `Comparison` | Multi-document comparison surface for shared themes, unique points, and AI-generated synthesis reports. |
| Smart Inbox | `Inbox` | Review queue for externally sourced or watch-folder content before it enters the vault. Supports accept, reject, defer, and batch operations. |
| Model Manager | `ModelManager` | Lists all locally installed Ollama models. Pull new models with a download progress bar. Delete models. Set active chat and embedding models. |
| Hardware Advisor | `HardwareAdvisor` | Reads CPU, RAM, and GPU specifications via `System.Management` and provides model size recommendations (e.g., "Your hardware supports up to 13B parameter models at 4-bit quantization"). |
| Backup & Restore | `BackupRestore` | Creates encrypted or plaintext backup packages and restores application state for migration and recovery workflows. |
| Workspace Profiles | `WorkspaceProfiles` | Creates isolated workspaces for different contexts with separate vault and conversation settings. |
| Plugin Manager | `PluginManager` | Installs, enables, disables, and removes plugin packages that extend ingestion, provider, workflow, or UI capabilities. |
| Collaborative Sync | `SyncSettings` | Configures encrypted sync packages, auto-sync scheduling, sync history, and conflict-aware synchronization settings. |
| Calendar | `CalendarSettings` | Configures calendar connectors and related ingestion behavior for event-driven inbox flows. |
| Email | `EmailSettings` | Configures email connectors and related ingestion behavior for inbox-driven knowledge capture. |
| Annotations | `Annotations` | Displays and manages user annotations attached to documents and intelligence outputs. |
| Settings | `Settings` | Full settings editor: AI provider selection and API keys, Ollama endpoint, model selection, chunking parameters, UI theme, storage path, and watch folders. |
| Onboarding | `Onboarding` | First-run wizard shown on initial launch with navigation pane hidden. Steps through Ollama connection check, model selection, and a brief feature tour. |
| User Guide | `UserGuide` | In-app reference documentation rendered as styled rich text. |
| Privacy Policy | `PrivacyPolicy` | Full privacy policy text confirming the local-only data model. |
| Terms of Service | `TermsOfService` | Terms of service for the application. |

---

## Core Service Layer

This section documents the interface contracts and implementation behavior of each major service group.

### AI Providers

The `IAiProvider` interface abstracts the three supported LLM backends behind a uniform API:

```csharp
public interface IAiProvider : IDisposable
{
    string ProviderId { get; }
    string DisplayName { get; }
    bool IsAvailable { get; }

    Task<bool> CheckConnectionAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default);
    Task PullModelAsync(string modelName, IProgress<ModelDownloadProgress>? progress, CancellationToken ct);
    Task DeleteModelAsync(string modelName, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamChatAsync(IReadOnlyList<ChatMessage> messages, ChatOptions? options, CancellationToken ct);
    Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, ChatOptions? options, CancellationToken ct);
    Task<float[]> GenerateEmbeddingAsync(string text, string modelName, CancellationToken ct);
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, string modelName, CancellationToken ct);
}
```

| Provider | Implementation | Transport |
|---|---|---|
| Ollama | `OllamaProvider` | OllamaSharp 4.0.12 over HTTP to `localhost:11434` |
| OpenAI | `OpenAiProvider` | Raw `HttpClient` to `api.openai.com/v1` |
| Anthropic | `AnthropicProvider` | Raw `HttpClient` to `api.anthropic.com/v1` |

The Ollama connection check uses a 3-second timeout so the status bar never blocks the UI when Ollama is not running.

### Hybrid Search and RRF Fusion

The `HybridSearchOrchestrator` accepts a `SearchQuery` with a `SearchMode` discriminant:

- **Semantic:** Delegates directly to `SemanticSearchService`, which queries `IVectorStore`. When HNSW indexing is enabled this uses `HnswVectorStore`; otherwise it falls back to `SqliteVecStore` linear cosine search.
- **Keyword:** Delegates directly to `KeywordSearchService`, which queries the SQLite FTS5 virtual table.
- **Hybrid:** Executes both searches in parallel with a 3x-expanded `TopK` to give RRF a larger candidate pool, then merges results using Reciprocal Rank Fusion with k=60.

RRF scoring formula applied to each result:

```
RRF_score = sum(1 / (k + rank_i))  for each list where the chunk appears
```

Normalized scores are clamped to [0, 1] using the theoretical maximum (result ranked first in both lists). If either backend fails, the orchestrator gracefully degrades to the surviving backend's results rather than propagating the exception.

### Vector Store

`IVectorStore` stores embeddings as BLOBs in a dedicated `vec_embeddings` table within the main SQLite database file. `VectorStoreFactory` chooses `HnswVectorStore` when `EnableHnswIndex` is enabled, with built-in linear-scan fallback below the configured threshold; otherwise it uses `SqliteVecStore`. Embeddings are serialized as little-endian IEEE 754 float arrays using `Buffer.BlockCopy`. The pre-computed L2 magnitude is stored alongside each embedding to avoid recomputing it during fallback similarity comparison.

Search is a full table scan with cosine similarity computed in managed C#:

```
cosine_similarity(a, b) = dot(a, b) / (|a| * |b|)
```

This approach is portable (no native SQLite extensions required) and suitable for collections up to approximately 100,000 embeddings. The database uses WAL journal mode for concurrent read/write access during indexing and search.

### Document Processing Pipeline

When a file is imported, the following pipeline executes:

1. `IDocumentService.ImportDocumentAsync` computes the SHA-256 hash and checks for an exact duplicate.
2. The appropriate `IDocumentProcessor` is selected by file extension and extracts raw text and metadata.
3. `IChunkingService` splits the text into overlapping chunks of the configured size.
4. `IIndexingQueueService` enqueues the document for background indexing.
5. `IIndexingService` (background consumer) generates embeddings for each chunk via `IEmbeddingService` and writes them through `IVectorStore`.
6. `IAutoTagService` calls the AI to generate confidence-scored tags and persists them to the tag graph.

Supported file types and their processors:

| Extension | Processor | Notes |
|---|---|---|
| `.pdf` | `PdfProcessor` | PDFsharp content stream parser; handles standard text-based PDFs |
| `.docx` | `DocxProcessor` | DocumentFormat.OpenXml paragraph extraction |
| `.txt` | `TextProcessor` | UTF-8 plain text |
| `.md` | `MarkdownProcessor` | Markdig strips markup; extracts plain text |
| `.cs`, `.py`, `.js`, `.ts`, `.go`, etc. | `CodeFileProcessor` | Treated as UTF-8 text with file type metadata |
| `.png`, `.jpg`, `.jpeg`, `.bmp`, `.gif` | `ImageProcessor` | Extracts EXIF metadata; content extraction requires a vision-capable model |

### Conversation Memory

`ConversationMemoryService` extracts four categories of memory from conversation turns:

| Category | Description | Default Importance |
|---|---|---|
| `instruction` | Explicit user directives ("Always respond in bullet points") | 0.9 |
| `preference` | User preferences ("I prefer concise answers") | 0.8 |
| `fact` | Factual statements made by the user | 0.5 |
| `topic` | Topics of interest surfaced in conversation | 0.5 |

Duplicate detection uses a substring match on the first 30 characters of the memory content. When a near-duplicate is found, the existing record's importance is incremented by 0.1 (capped at 1.0) rather than creating a new record. The top 10 memories ranked by importance and recency are injected into every system prompt as a `[User Memory Context]` block.

### Knowledge Graph Construction

`KnowledgeGraphService.BuildGraphAsync` executes the following pipeline:

1. Loads all documents (with collections and tags eager-loaded), all collections, and all tags from EF Core.
2. Creates typed nodes: document nodes (size proportional to chunk count, capped at 40 px), collection nodes (fixed 32 px), tag nodes (fixed 16 px).
3. Builds edges: document-to-collection, document-to-tag, and document-to-document (for pairs sharing at least one collection or tag, weighted by shared connection count).
4. Assigns initial random positions using a deterministic seed (42) for reproducible starting layouts.
5. Runs 100 iterations of a spring-electric force-directed layout: Coulomb repulsion between all node pairs (strength 5000), Hooke attraction along edges (strength 0.01, ideal length 100 px), centering gravity (0.01), and velocity damping (0.85 per iteration).

The resulting node positions are returned to `KnowledgeGraphViewModel` and rendered on a WinUI 3 `Canvas` using `Ellipse` and `Line` primitives.


## Data Storage

All persistent data lives under `%LocalAppData%\AgentX\`.

```
%LocalAppData%\AgentX\
├── agentx.db               -- SQLite database (EF Core + raw vector BLOB store;
│                              SQLCipher-encrypted at rest when encryption is enabled)
├── settings.json           -- User preferences (JSON)
├── encryption.info.json    -- Out-of-DB encryption keystore (DPAPI-wrapped database
│                              key or PBKDF2 passphrase material; present only when
│                              encryption is enabled — C13)
└── Logs\
    ├── agentx-20260227.log
    └── agentx-20260226.log   (7-day rolling retention)
```

### Database Schema

The `AgentXDbContext` maps 16 entity types to SQLite tables:

| Table | Entity | Description |
|---|---|---|
| `conversations` | `ConversationEntity` | Chat sessions with title, model ID, pin state, message count, and token usage |
| `messages` | `MessageEntity` | Individual chat messages with role, content, timestamp, sort order, and token count |
| `documents` | `DocumentEntity` | Imported file metadata with SHA-256 hash, indexing status, file type, and timestamps |
| `document_chunks` | `DocumentChunkEntity` | Text chunks with content, position index, and vector store row ID |
| `collections` | `CollectionEntity` | Hierarchical collections with self-referencing parent/child relationship |
| `document_collections` | `DocumentCollectionEntity` | Many-to-many join between documents and collections |
| `tags` | `TagEntity` | Tag names with auto-generated flag; unique name constraint |
| `document_tags` | `DocumentTagEntity` | Many-to-many join with confidence score and assignment timestamp |
| `search_history` | `SearchHistoryEntity` | Persisted search queries with type and timestamp |
| `system_prompts` | `SystemPromptEntity` | Named, categorized system prompt templates |
| `user_settings` | `UserSettingsEntity` | Key-value settings with type annotation (supplement to settings.json) |
| `watch_folders` | `WatchFolderEntity` | File system paths monitored for auto-import, optionally linked to a collection |
| `indexing_jobs` | `IndexingJobEntity` | Asynchronous indexing job queue with status lifecycle |
| `memories` | `MemoryEntity` | AI-extracted memory entries with category, importance, and usage tracking |
| `digest_reports` | `DigestReportEntity` | Weekly activity summaries with JSON-serialized sub-reports |
| `vec_embeddings` | (raw SQL) | Float array BLOBs with pre-computed magnitude; managed outside EF Core |

Key indexes are defined on all foreign keys, date columns used in range queries, and the `content_hash` column for O(1) duplicate detection.

### At-Rest Encryption (v2.1 Bedrock C13)

Starting with v2.1.0-preview.1, the entire `agentx.db` file can be encrypted at rest using **SQLCipher** (AES-256-CBC) via the `SQLitePCLRaw.bundle_e_sqlcipher` provider. Encryption is opt-in from **Settings → Database Encryption** and is applied atomically via `sqlcipher_export` — a `.plain.bak` backup of the plaintext database is retained only through the atomic-swap critical section and removed on success.

**Key management (available to every user):**

| Key Derivation | User Experience |
|---|---|
| DPAPI-wrapped random 256-bit key, tied to your Windows account | Transparent — enable the toggle and the vault is encrypted; Windows handles the unlock |

**Key storage is OUT of the database.** The database key (or the salt + verifier material for passphrase mode) lives in `%LocalAppData%\AgentX\encryption.info.json`, a sibling file that is read before the `SqliteConnection` opens. This breaks the startup unlock ↔ migration chicken-and-egg and means the encryption state never depends on the encrypted vault. Losing `encryption.info.json` while the vault is encrypted means the database cannot be unlocked — back it up alongside `agentx.db` if you enable encryption.

Every production `SqliteConnection` creation site is routed through `IEncryptedConnectionFactory`, which applies `PRAGMA key` uniformly before any schema or query operation.

---

## Keyboard Shortcuts

Current shipped shortcuts are seeded by `ShortcutCatalog` into `IShortcutRegistry`, then routed by `ShortcutInputRouter` from the root `Grid` `PreviewKeyDown` event. The command palette, jump-to dialog, cheatsheet, and page-scoped shortcut help all read from the same descriptor registry.

| Shortcut | Action |
|---|---|
| `Ctrl+K` | Toggle the Command Palette overlay |
| `Ctrl+Shift+P` | Open the Command Palette overlay |
| `Ctrl+N` | Navigate to AI Chat (new conversation) |
| `Ctrl+I` | Navigate to Knowledge Vault (import documents) |
| `Ctrl+F` | Navigate to Semantic Search |
| `Ctrl+Shift+F` | Navigate to Semantic Search (alternate) |
| `Ctrl+Shift+A` | Navigate to Analytics |
| `Ctrl+D` | Navigate to Dashboard |
| `Ctrl+Shift+W` | Navigate to Workflows |
| `Ctrl+Shift+E` | Navigate to Web Import |
| `Ctrl+G` | Navigate to Knowledge Graph |
| `Ctrl+P` | Open Jump To |
| `F1` | Open Keyboard Shortcuts / Cheatsheet |
| `Ctrl+,` | Navigate to Settings |
| `Esc` | Close the Command Palette (when open) |

### Command Palette

The Command Palette (`Ctrl+K` or `Ctrl+Shift+P`) provides keyboard-first access to the app's registered navigation shortcuts and actions. Type to filter the command list. Press `Enter` to execute the selected command or `Esc` to dismiss. Registered destinations include Dashboard, Analytics, AI Chat, Ask Your Files, Knowledge Vault, Collections, Search, Workflows, Web Import, Knowledge Graph, Model Manager, and Settings, alongside actions such as Jump To and Keyboard Shortcuts.

---

## Pricing

Agent-X is **100% free and open-source**. There are no paid tiers, no subscriptions, no activation, no document limits, and no feature gates of any kind. Every capability — including unlimited documents, the full intelligence stack, plugins, integrations, and encryption — is unconditionally available to every user, forever, at no cost.

---

## Project Structure

```
Agent-X/
├── src/
│   ├── AgentX.App/                    -- WinUI 3 application host
│   │   ├── App.xaml / App.xaml.cs     -- DI composition root, logging, exception handling
│   │   ├── MainWindow.xaml.cs         -- Shell, navigation, keyboard shortcuts, status bar
│   │   ├── Assets/                    -- Application icons and brand assets
│   │   ├── Controls/                  -- Reusable XAML controls (CommandPalette, etc.)
│   │   ├── Converters/                -- IValueConverter implementations
│   │   ├── Helpers/                   -- UI helper utilities
│   │   ├── Services/                  -- UI-layer services (ShortcutCatalog, ShortcutInputRouter, theme/localization)
│   │   ├── Styles/                    -- Global XAML resource dictionaries and theme overrides
│   │   ├── ViewModels/                -- Page, dialog, coordinator, and helper ViewModels
│   │   └── Views/                     -- Page XAML files, dialogs, and supporting views
│   │
│   └── AgentX.Core/                   -- Platform-independent business logic
│       ├── AI/                        -- AI provider abstraction, embedding, context management
│       │   ├── Providers/             -- OllamaProvider, OpenAiProvider, AnthropicProvider
│       │   └── Models/                -- AiModel, ChatMessage, ChatOptions, CostTracker
│       ├── Data/                      -- EF Core DbContext, entities, migrations, vector store
│       │   ├── Entities/              -- 16 EF Core entity classes
│       │   ├── Migrations/            -- EF Core database migrations
│       │   └── VectorDb/              -- IVectorStore, HnswVectorStore, SqliteVecStore fallback
│       ├── Documents/                 -- Document processing pipeline
│       │   └── Processors/            -- PdfProcessor, DocxProcessor, TextProcessor, etc.
│       ├── Helpers/                   -- HashHelper, shared utilities
│       ├── Search/                    -- Semantic, keyword, and hybrid search
│       │   └── Models/                -- SearchQuery, SearchResult
│       └── Services/
│           ├── Chat/                  -- ChatService, ConversationService, MemoryService
│           ├── Collections/           -- CollectionService
│           ├── Indexing/              -- IndexingService, IndexingQueueService, FileWatcherService
│           ├── Intelligence/          -- DigestService, KnowledgeGraphService, SummaryService, etc.
│           ├── Settings/              -- SettingsService, AppSettings model
│           └── Tagging/               -- AutoTagService
│
├── tests/                             -- Test projects
├── installer/
│   └── AgentX-Setup.iss               -- Inno Setup 6 installer script
├── installer-output/                  -- Built installer executables
├── publish/
│   └── win-x64/                       -- Published self-contained binary
├── docs/                              -- Project documentation
├── screenshots/                       -- Application screenshots
├── Directory.Build.props              -- Solution-wide MSBuild properties (version, copyright)
└── AgentX.sln                         -- Visual Studio solution file
```

---

## Technology Stack

| Category | Package | Version |
|---|---|---|
| UI Framework | Microsoft.WindowsAppSDK | 1.6.250108002 |
| MVVM | CommunityToolkit.Mvvm | 8.2.2 |
| WinUI Animations | CommunityToolkit.WinUI.Animations | 8.1.240916 |
| Dependency Injection | Microsoft.Extensions.Hosting | 8.0.1 |
| ORM | Microsoft.EntityFrameworkCore.Sqlite | 8.0.11 |
| SQLite Driver | Microsoft.Data.Sqlite | 8.0.11 |
| AI Abstractions | Microsoft.Extensions.AI | 9.5.0 |
| Ollama Client | OllamaSharp | 4.0.12 |
| PDF Processing | PDFsharp | 6.1.1 |
| DOCX Processing | DocumentFormat.OpenXml | 3.2.0 |
| Markdown | Markdig | 0.37.0 |
| Logging | Serilog | 4.0.2 |
| System Info | System.Management | 8.0.0 |
| Installer | Inno Setup | 6.x |
| Language | C# 12 (.NET 8.0) | — |
| Target Framework | net8.0-windows10.0.22621.0 | — |

---

## Contributing

Agent-X is released under the MIT License — you are free to use, modify, fork, and redistribute it under the license terms. The project is provided as-is and is not actively soliciting external contributions, but you are welcome to fork it.

For bug reports, feature requests, or general inquiries, contact Rocky Elsalaymeh through the channels listed at strategia-x.com.

### Internal Development Guidelines

The following conventions apply to all code in this repository:

**Architecture constraints:**
- `AgentX.Core` must remain free of any WinUI 3 or Windows App SDK references. It targets `net8.0-windows10.0.22621.0` for `System.Management` access but must not take on any UI framework dependency.
- All public service methods must accept a `CancellationToken` parameter. Background tasks are cancellable.
- Services must not call other services' internal implementation details — only program to the interface.

**Error handling:**
- Catch and log exceptions at service boundaries. Do not let exceptions from AI providers or file I/O propagate unhandled to the ViewModel layer.
- For user-facing operations, return result objects (e.g., `ExportResult`) rather than throwing exceptions.
- For background operations, log at `Warning` level and continue where possible (graceful degradation).

**Logging:**
- Use Serilog's structured logging: `_logger.Information("Processed {Count} chunks for document {DocumentId}", count, id)`.
- Log at `Information` for significant state changes, `Debug` for fine-grained diagnostics, `Warning` for recoverable failures, and `Error`/`Fatal` for unrecoverable conditions.
- Do not log sensitive data: API keys, full file paths containing usernames, or document content.

**Data access:**
- Use `AsNoTracking()` on all read-only EF Core queries.
- Prefer async EF Core methods (`ToListAsync`, `FirstOrDefaultAsync`, `SaveChangesAsync`).
- Raw ADO.NET is acceptable only in vector-store implementations (`HnswVectorStore`, `SqliteVecStore`) and `KeywordSearchService` where FTS5 or vector operations require it.

**UI and ViewModel:**
- ViewModels must extend `ObservableObject` from CommunityToolkit.Mvvm.
- Use `[ObservableProperty]` and `[RelayCommand]` source generators.
- All UI updates from async operations must be dispatched to the UI thread using `DispatcherQueue.TryEnqueue`.
- Do not put business logic in code-behind files. Code-behind is for event wiring and DI resolution only.

---

## License

Copyright (c) 2026 Rocky Elsalaymeh.

Agent-X is released under the **MIT License** — see [LICENSE](../LICENSE) at the repository root. You may use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the software, subject to inclusion of the copyright and permission notice.

Agent-X is 100% free and open-source. Every capability is unconditionally available to every user — there are no paid tiers, no activation, no quotas, and no feature gates of any kind. Use it for anything, forever, at no cost.
