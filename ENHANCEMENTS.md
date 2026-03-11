# Agent-X Enhancement Roadmap

## Codebase Audit Summary (2026-03-10)
- **24 ViewModels**, **26 entities**, **55+ services**, **26+ navigation pages**
- **224 unit tests** passing (was 0% - test project was scaffolded but empty)
- Tiers 1-3 enhancements completed, Tier 4 (Plugin Manager + Sync Settings) completed
- High Priority #1-5 completed (Unit Tests, Validation, Error Handling, Search Caching, Hybrid Search UI)
- Medium Priority #6-12 ALL completed (Localization, Plugin Docs, Knowledge Graph, CSV Export, Workflow Templates, Batch Ops, Saved Filters)
- Built-in Local LLM (LLamaSharp) + 6 Advanced RAG Enhancements completed
- GPU Acceleration (CUDA 12), JSON Mode / Structured Output, Installer-Bundled Model completed
- Tech Debt A-E completed (Magic Numbers, Formatting, DTOs, Logging, Feature Flags)
- Lower Priority #13-17 ALL completed (Collaboration, Feedback/Few-Shot, Analytics Dashboard, REST API, Mobile Companion)
- UX Polish #18-21 completed (Per-Message Actions, Message Editing, Code Syntax Highlighting, Notification Toasts)

---

## HIGH PRIORITY — Immediate Impact

| # | Enhancement | Description | Status |
|---|-------------|-------------|--------|
| 1 | **Unit Test Suite** | 222 tests across 8 files covering Settings, Collections, License, Search Cache, and all 3 validators. | DONE |
| 2 | **Input Validation Layer** | IValidator<T> with AppSettingsValidator, SyncConfigurationValidator, PluginManifestValidator. Registered in DI. | DONE |
| 3 | **Structured Error Handling** | 7 typed exceptions: AgentXException, EntityNotFoundException, ValidationException, PluginException, SyncException, ExportException, LicenseException. | DONE |
| 4 | **Search Result Caching** | Thread-safe LRU cache (100 entries, 5min TTL) integrated into HybridSearchOrchestrator with auto-invalidation on re-index. | DONE |
| 5 | **Hybrid Search Prominence** | Collection filter dropdown, advanced filters panel (min relevance, max results, date range), sort options, saved filters sidebar. Full UI + backend. | DONE |

## MEDIUM PRIORITY — Feature Richness

| # | Enhancement | Description | Status |
|---|-------------|-------------|--------|
| 6 | **Localization / i18n** | x:Uid wiring on PluginManagerPage (23 resource entries), full .resw translations for 5 locales (en-US, de, fr, ja, zh-CN) with x:Uid property format. | DONE |
| 7 | **Plugin Documentation Viewer** | README extraction during plugin install, markdown rendering in Plugin Manager detail panel using MarkdownParser + MarkdownMessageControl. ReadmeContent persisted in PluginEntity. | DONE |
| 8 | **Knowledge Graph Visualization** | Node search/highlight, zoom/pan controls (mouse wheel + buttons), hover tooltips, cluster highlighting for Collection/Tag nodes. | DONE |
| 9 | **Additional Export Formats** | CSV export for conversations, search results, and collections. Manual CSV escaping (RFC 4180). ExportFormat.Csv enum + 4 builder methods in ExportService. | DONE |
| 10 | **Workflow Templates** | Pre-built agent workflow templates users can import and customize. | DONE |
| 11 | **Batch Operations** | Plugin multi-select with bulk enable/disable/uninstall. Collection multi-select with recursive select-all and bulk delete. IsMultiSelectMode toggle in both ViewModels. | DONE |
| 12 | **Saved Filters & Views** | 5 new SearchHistoryEntity fields (MinScore, MaxResults, DateAfter, DateBefore, SortOrder). Full save/load/apply cycle with schema migration for existing databases. | DONE |

## INFRASTRUCTURE — Offline AI & RAG Pipeline

| # | Enhancement | Description | Status |
|---|-------------|-------------|--------|
| I1 | **Built-in Local LLM** | LLamaSharp-based local AI provider (Llama 3.2 3B Instruct Q4_K_M GGUF). Fully offline inference with StatelessExecutor, lazy model loading, HuggingFace download support. Default provider — no internet required. | DONE |
| I2 | **Multi-Query Retrieval** | Generates 3 alternative query phrasings via LLM for improved recall across diverse document sets. | DONE |
| I3 | **HyDE (Hypothetical Document Embeddings)** | Generates a hypothetical answer passage and embeds it for improved semantic matching against actual documents. | DONE |
| I4 | **LLM-based Reranking** | Cross-encoder style reranking using the LLM to score passage relevance (0-10), combined 40/60 with original score. | DONE |
| I5 | **Parent Document Retrieval** | Expands matched chunks by loading ±1 adjacent chunks from the same document for richer context. | DONE |
| I6 | **Contextual Compression** | LLM extracts only question-relevant sentences from each chunk, filtering irrelevant passages entirely. | DONE |
| I7 | **RAG Evaluation Pipeline** | LLM-as-judge scoring context relevance, faithfulness, and answer relevance (0-1 normalized). Runs async post-generation. | DONE |
| I8 | **GPU Acceleration (CUDA 12)** | LLamaSharp.Backend.Cuda12 for NVIDIA GPUs. Auto-detects GPU via WMI, configures layer offloading based on VRAM (2-8+ GB tiers). Falls back to CPU gracefully. | DONE |
| I9 | **Structured Output / JSON Mode** | ResponseFormat enum (Text/JsonObject) in ChatOptions. OpenAI: response_format API param. Anthropic: system prompt reinforcement. Ollama: Format="json". Local LLM: prompt engineering + output priming. Used by tag generation, RAG evaluation, and LLM reranking. | DONE |
| I10 | **Installer-Bundled AI Model** | Llama 3.2 3B GGUF (~2 GB) bundled directly in the Inno Setup installer. Users get fully working offline AI out of the box — zero downloads required. Model installed to %LOCALAPPDATA%\AgentX\Models. | DONE |
| I11 | **Onboarding Cloud API Keys** | Step 3 redesigned: shows built-in model status + GPU info, with optional OpenAI/Anthropic API key entry. Keys saved securely to local settings. Summary step updated with model & provider info. | DONE |

## UX POLISH — Chat & Developer Experience

| # | Enhancement | Description | Status |
|---|-------------|-------------|--------|
| 18 | **Per-Message Actions** | Copy, delete, and regenerate buttons on each chat message bubble using the existing MessageActionButtonStyle. Inline thumbs up/down wired to IFeedbackService with toggle cycling (positive→negative→none). MessageId, ConversationId, SortOrder, FeedbackRating added to ChatMessageItem. Feedback state loaded from database on conversation select. | DONE |
| 19 | **Message Editing** | Edit sent user messages inline with a TextBox overlay and "Save & Resend" action. Editing truncates all subsequent messages (both UI and DB via DeleteMessagesAfterAsync) then re-sends. New IConversationService methods: DeleteMessageAsync, UpdateMessageContentAsync, DeleteMessagesAfterAsync. IsEditing/EditContent observable properties on ChatMessageItem. | DONE |
| 20 | **Code Syntax Highlighting** | Language-aware token coloring in code blocks. SyntaxHighlighter helper with keyword-based regex tokenizer supporting 18 languages (C#, Python, JS/TS, SQL, JSON, HTML/XML, Rust, Go, Java, Bash, YAML, CSS, C/C++). One Dark Pro inspired color palette (purple keywords, green strings, grey comments, blue functions, orange numbers, cyan operators). MarkdownMessageControl upgraded to use RichTextBlock with colored Runs for supported languages. | DONE |
| 21 | **Error Toasts / Notification System** | App-wide toast notification overlay in MainWindow. INotificationService with Show/ShowSuccess/ShowError/ShowWarning/ShowInfo methods. NotificationOverlay UserControl with auto-dismissing cards (configurable duration), severity icons, dismiss button. Max 5 visible notifications. Integrated into ChatViewModel for generation failures. Available via DI for all pages. | DONE |

## LOWER PRIORITY — Advanced Features

| # | Enhancement | Description | Status |
|---|-------------|-------------|--------|
| 13 | **Real-time Collaboration** | Lightweight HTTP-based collaboration service with HttpListener. 4 REST endpoints (/join, /heartbeat, /events, /leave). ConcurrentDictionary session management with 10s heartbeat + 30s stale-peer pruning. CollaborationSession, CollaborationEvent, CollaborationEventType models. ICollaborationService interface with start/stop hosting, presence updates, event broadcasting. | DONE |
| 14 | **User Feedback & Few-Shot Learning** | Thumbs up/down feedback on assistant messages with FeedbackEntity (rating, preferred response, category, notes). 7-method IFeedbackService with upsert, category filtering, and BuildFewShotExamplesAsync that formats positive-rated messages into few-shot prompt examples. EF Core mapping with unique MessageId index + cascading delete. | DONE |
| 15 | **Analytics Dashboard** | Full-page analytics with 6 summary stat cards (conversations, messages, tokens, documents, searches, response time). Indexing progress bar with tokens/conversation insight. 30-day activity bar charts for conversations, documents, and searches. Model usage horizontal bars. File type distribution. 7 performance metric tiles (avg, median, P95, throughput, fastest, slowest, total inference). AnalyticsService with parallel EF Core queries, gap-filling, and percentile calculations. | DONE |
| 16 | **REST API Layer** | Embedded HttpListener-based local REST API on port 9846. SemaphoreSlim(16) concurrency gate with CORS headers. 5 route groups: /api/health, /api/documents (GET list + GET by ID), /api/conversations, /api/collections, /api/search (POST). ApiResponse<T> envelope with typed DTOs. IApiHostService with start/stop lifecycle. | DONE |
| 17 | **Mobile Companion** | MAUI project scaffold (Android + iOS targets) connecting to desktop app via REST API. AgentXApiClient HTTP service with configurable base URL. 4 pages: Documents, Conversations, Search, Settings. MVVM ViewModels with async data loading. Shell navigation with tab bar. SettingsService for persistent API URL configuration. | DONE |

## TECH DEBT — Code Health

| # | Item | Description | Status |
|---|------|-------------|--------|
| A | **Magic Numbers** | 60+ named constants in AppConstants.cs replacing hardcoded timeouts, retry values, buffer sizes, crypto params, validation limits, AI inference values. 15 source files updated to reference centralized constants. | DONE |
| B | **Duplicate Formatting** | FormatHelper enhanced with FormatPercent, FormatLatency, TimeAgoWithMonths. Removed 13 duplicate private FormatBytes/FormatTimeAgo/FormatDuration methods across 9 ViewModels, all now use shared helpers. | DONE |
| C | **DTO Layer** | 5 DTO files: DocumentDisplayDto, SearchResultDto, ConversationDto, MessageDto, DashboardItemDto. Pre-computed display properties (formatted sizes, time-ago, file icons, token speeds). Ready for ViewModel adoption. | DONE |
| D | **Logging Levels** | Fixed 5 logging issues: added logging to 2 empty catch blocks, upgraded 2 onboarding failures from Warning→Error, downgraded unknown page nav from Warning→Debug. | DONE |
| E | **Feature Flags** | FeatureFlagService registered in DI and initialized at startup. 15 flags (AI, Search, Intelligence, Sync, Plugins, Experimental). Feature guards added to SearchCacheService, AutoTagService, DuplicateDetectionService. | DONE |
