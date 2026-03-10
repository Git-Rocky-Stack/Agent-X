# Agent-X Enhancement Roadmap

## Codebase Audit Summary (2026-03-07)
- **23 ViewModels**, **25 entities**, **50+ services**, **25+ navigation pages**
- **224 unit tests** passing (was 0% - test project was scaffolded but empty)
- Tiers 1-3 enhancements completed, Tier 4 (Plugin Manager + Sync Settings) completed
- High Priority #1-5 completed (Unit Tests, Validation, Error Handling, Search Caching, Hybrid Search UI)
- Medium Priority #8 completed (Knowledge Graph enhanced with search, zoom, tooltips, cluster highlight)
- Built-in Local LLM (LLamaSharp) + 6 Advanced RAG Enhancements completed

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
| 6 | **Localization / i18n** | All strings are hardcoded English. Add IStringLocalizer with .resw resource files for multi-language support. | Pending |
| 7 | **Plugin Documentation Viewer** | Show plugin README/docs inline in the Plugin Manager detail panel. | Pending |
| 8 | **Knowledge Graph Visualization** | Node search/highlight, zoom/pan controls (mouse wheel + buttons), hover tooltips, cluster highlighting for Collection/Tag nodes. | DONE |
| 9 | **Additional Export Formats** | Currently limited — add PDF, Markdown, and CSV export for reports, search results, and sync history. | Pending |
| 10 | **Workflow Templates** | Pre-built agent workflow templates users can import and customize. | Pending |
| 11 | **Batch Operations** | Multi-select in list views (plugins, documents, history) with bulk enable/disable/delete. | Pending |
| 12 | **Saved Filters & Views** | Let users save and recall search filters and list configurations. | Pending |

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

## LOWER PRIORITY — Advanced Features

| # | Enhancement | Description | Status |
|---|-------------|-------------|--------|
| 13 | **Real-time Collaboration** | Multi-user sync with SignalR for live editing indicators. | Pending |
| 14 | **Custom Model Training** | Fine-tune agent behavior with user feedback loops. | Pending |
| 15 | **Analytics Dashboard** | Usage stats, performance metrics, and trend charts for agent activity. | Pending |
| 16 | **REST API Layer** | Expose core functionality via a local REST API for external tool integration. | Pending |
| 17 | **Mobile Companion** | MAUI-based mobile app sharing the AgentX.Core library. | Pending |

## TECH DEBT — Code Health

| # | Item | Description | Status |
|---|------|-------------|--------|
| A | **Magic Numbers** | Replace hardcoded values (timeouts, retry counts, buffer sizes) with configuration constants. | Pending |
| B | **Duplicate Formatting** | Consolidate repeated date/time/size formatting logic into shared helpers. | Pending |
| C | **DTO Layer** | Add Data Transfer Objects between services and ViewModels to decouple layers. | Pending |
| D | **Logging Levels** | Audit all Log.Debug/Log.Information usage — some should be Warning or Error. | Pending |
| E | **Feature Flags** | Add a feature flag system for staged rollout of new capabilities. | Pending |
