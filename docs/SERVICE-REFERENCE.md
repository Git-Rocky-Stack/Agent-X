# Agent-X Service Reference

The services of the Agent-X desktop app: the interfaces and classes in `AgentX.Core` and the app-level services registered in `src/AgentX.App/App.xaml.cs`, with their public members and what they do, as the code has them. Plugin authors should start with the [Plugin Development Guide](PLUGIN-DEVELOPMENT-GUIDE.md), because a plugin reaches only `IInboxService`. The HTTP routes of the local REST API are documented in [API_ENDPOINTS.md](../API_ENDPOINTS.md).

## Table of Contents

1. [How to Read This Reference](#how-to-read-this-reference)
2. [Startup and Shutdown](#startup-and-shutdown)
3. [AI Services](#ai-services)
    - [IAiService](#iaiservice)
    - [IAiProvider](#iaiprovider)
    - [IModelManager](#imodelmanager)
    - [IBuiltInModelBootstrap](#ibuiltinmodelbootstrap)
    - [IHardwareDetector](#ihardwaredetector)
    - [ITokenCounter](#itokencounter)
    - [IContextWindowManager](#icontextwindowmanager)
    - [IEmbeddingService](#iembeddingservice)
    - [ICostTracker](#icosttracker)
    - [IContextAssemblyService, ISemanticContextSelector, IConversationCompressionService](#icontextassemblyservice-isemanticcontextselector-iconversationcompressionservice)
    - [IModelRouterService and ITaskTypeDetector](#imodelrouterservice-and-itasktypedetector)
    - [IMultiAgentOrchestrator](#imultiagentorchestrator)
4. [Chat Services](#chat-services)
    - [IChatService](#ichatservice)
    - [IConversationService](#iconversationservice)
    - [IConversationBranchService](#iconversationbranchservice)
    - [IConversationMemoryService](#iconversationmemoryservice)
    - [ISemanticMemoryService](#isemanticmemoryservice)
    - [IConversationRecallService](#iconversationrecallservice)
    - [IConversationSummaryService](#iconversationsummaryservice)
    - [ISystemPromptService](#isystempromptservice)
    - [IFeedbackService](#ifeedbackservice)
5. [Document Services](#document-services)
    - [IDocumentService](#idocumentservice)
    - [IDocumentProcessor](#idocumentprocessor)
    - [IChunkingService](#ichunkingservice)
    - [IAdaptiveChunkingService](#iadaptivechunkingservice)
6. [Indexing Services](#indexing-services)
    - [IIndexingService](#iindexingservice)
    - [IFileWatcherService](#ifilewatcherservice)
7. [Search and RAG Services](#search-and-rag-services)
    - [ISemanticSearchService](#isemanticsearchservice)
    - [IKeywordSearchService](#ikeywordsearchservice)
    - [IHybridSearchOrchestrator](#ihybridsearchorchestrator)
    - [ISearchCacheService](#isearchcacheservice)
    - [IRagPipeline](#iragpipeline)
    - [RAG Pipeline Stages](#rag-pipeline-stages)
    - [IWebSearchService](#iwebsearchservice)
    - [IRagConfiguration and IRagPromptCatalog](#iragconfiguration-and-iragpromptcatalog)
    - [IRagMetrics and IPiiDetector](#iragmetrics-and-ipiidetector)
8. [Vector Database](#vector-database)
    - [IVectorStore](#ivectorstore)
9. [Collections, Tags and Annotations](#collections-tags-and-annotations)
    - [ICollectionService](#icollectionservice)
    - [IAutoTagService](#iautotagservice)
    - [IAnnotationService](#iannotationservice)
10. [Intelligence Services](#intelligence-services)
    - [ISummaryService](#isummaryservice)
    - [IHierarchicalSummaryService](#ihierarchicalsummaryservice)
    - [IDuplicateDetectionService](#iduplicatedetectionservice)
    - [IOrganizationSuggestionService](#iorganizationsuggestionservice)
    - [IKnowledgeGraphService](#iknowledgegraphservice)
    - [IDigestService and IDigestInsightService](#idigestservice-and-idigestinsightservice)
    - [IComparisonService and IDocumentSynthesisService](#icomparisonservice-and-idocumentsynthesisservice)
    - [IConversationThemeClusterService and IConversationThemeTrendService](#iconversationthemeclusterservice-and-iconversationthemetrendservice)
    - [IAnalyticsService](#ianalyticsservice)
11. [Temporal Identity Services](#temporal-identity-services)
    - [ITemporalIdentityService](#itemporalidentityservice)
    - [IVoiceDraftService](#ivoicedraftservice)
    - [EngagementTracker](#engagementtracker)
12. [Audio and Screen Services](#audio-and-screen-services)
    - [ITranscriptionService](#itranscriptionservice)
    - [IScreenCaptureService](#iscreencaptureservice)
13. [Web Import Services](#web-import-services)
    - [IWebImportService](#iwebimportservice)
    - [IWebScraperService](#iwebscraperservice)
    - [IWebContentFetcher and IJsRenderingService](#iwebcontentfetcher-and-ijsrenderingservice)
    - [IHtmlParser and IStructuredDataExtractor](#ihtmlparser-and-istructureddataextractor)
    - [IFeedService and ISitemapParser](#ifeedservice-and-isitemapparser)
14. [Export Services](#export-services)
    - [IExportService](#iexportservice)
    - [IExportFormatter](#iexportformatter)
    - [IExportTemplateService](#iexporttemplateservice)
15. [Workflow Services](#workflow-services)
    - [IWorkflowService](#iworkflowservice)
    - [IWorkflowEngine](#iworkflowengine)
16. [Smart Inbox and Connectors](#smart-inbox-and-connectors)
    - [IInboxService](#iinboxservice)
    - [IOAuthService](#ioauthservice)
    - [Calendar Connector](#calendar-connector)
    - [Email Connector](#email-connector)
17. [Plugin Infrastructure](#plugin-infrastructure)
    - [IPluginService](#ipluginservice)
    - [IPlugin](#iplugin)
    - [IPluginContext](#iplugincontext)
    - [IPluginDocumentProcessorSource](#iplugindocumentprocessorsource)
    - [PluginManifest](#pluginmanifest)
18. [Backup, Sync and Security Services](#backup-sync-and-security-services)
    - [IBackupService](#ibackupservice)
    - [ISyncService](#isyncservice)
    - [Database and migrations](#database-and-migrations)
    - [Database encryption](#database-encryption)
    - [IDpapiEncryptionService and ISecurityStatusService](#idpapiencryptionservice-and-isecuritystatusservice)
19. [Settings and Platform Services](#settings-and-platform-services)
    - [ISettingsService](#isettingsservice)
    - [IPrivacyStatusService](#iprivacystatusservice)
    - [ILocalizationService](#ilocalizationservice)
    - [IFeatureFlagService](#ifeatureflagservice)
    - [IWorkspaceProfileService](#iworkspaceprofileservice)
    - [IShortcutRegistry](#ishortcutregistry)
    - [IValidator and IAppPathService](#ivalidator-and-iapppathservice)
20. [Local REST API Services](#local-rest-api-services)
    - [IApiHostService](#iapihostservice)
    - [IApiHostLifecycleService](#iapihostlifecycleservice)
21. [App Services](#app-services)
    - [IStartupOrchestrator and IStartupGate](#istartuporchestrator-and-istartupgate)
    - [App Chat Coordinators](#app-chat-coordinators)
    - [Operations services](#operations-services)
    - [Shell services](#shell-services)
22. [Models and Data Structures](#models-and-data-structures)
    - [AI models](#ai-models)
    - [Search models](#search-models)
    - [Document models](#document-models)
    - [Indexing events](#indexing-events)
    - [Intelligence models](#intelligence-models)
23. [Quick Reference](#quick-reference)
24. [Common Usage Patterns](#common-usage-patterns)
    - [Import documents and follow indexing](#import-documents-and-follow-indexing)
    - [Ask a question of the vault](#ask-a-question-of-the-vault)
    - [Chat with streaming](#chat-with-streaming)
    - [Search documents](#search-documents)
    - [Draft in the user's voice](#draft-in-the-users-voice)
    - [From a plugin](#from-a-plugin)

---

## How to Read This Reference

- **Registration.** Every service here is registered as a singleton in `src/AgentX.App/App.xaml.cs` (view models and pages are transient) and reaches its consumers through constructor injection. Code in the app that has no constructor to inject into uses `App.GetService<T>()`.
- **Signatures** are copied from the code, with their default values. Each section names the interface's namespace and the class registered for it.
- **"No caller in the app"** marks a member that exists but that nothing in `AgentX.App` or `AgentX.Core` calls yet. It works as described, but no page uses it.
- **Names in quotes** are the labels of the English UI (`src/AgentX.App/Strings/en-US/Resources.resw`).
- **Plugins** cannot resolve these services. A plugin's `IPluginContext.Services` offers only `IInboxService`; see [Plugin Infrastructure](#plugin-infrastructure).
- **Data.** Most services share one `AgentXDbContext` over SQLite (`%LOCALAPPDATA%\AgentX\agentx.db`, SQLCipher-encrypted when [Database encryption](#database-encryption) is on). Work on it is serialized by a database gate, and services that run in the background avoid leaving tracked entities in the shared context.

---

## Startup and Shutdown

`App.OnLaunched` applies the saved UI language (it reads `settings.json` only), creates the main window and then runs `InitializeCoreServicesAsync`. The steps carry the numbers used in the code comments; other sections refer to them.

| Step | What runs |
|------|-----------|
| 0 | `IDatabaseEncryptionMigrator.RecoverIfNeeded` finishes or undoes an interrupted encryption change. When the encryption marker exists, the database key is loaded (DPAPI) or, for a passphrase database from an older build, the passphrase is asked for until it opens the database; the key is then applied to the shared connection. |
| 1 | [IStartupOrchestrator.RunCriticalStartupAsync](#istartuporchestrator-and-istartupgate): the database migration. Only on success does it open the startup gate, start the Local REST API and then initialize the built-in connectors. On failure the app shows a blocking recovery screen and none of the following steps run. |
| 1b | `IKeywordSearchService.InitializeFtsAsync` prepares the FTS5 keyword index. |
| 1c | `ISyncService.ResumeAutoSyncAsync` restores the sync status and resumes auto-sync when it is on; `IWorkflowService.ReconcileInterruptedRunsAsync` marks runs left "running" by the previous session as failed. |
| 2 | `IAiService.InitializeAsync` registers the AI providers and checks the connection. |
| 3 | `IFeatureFlagService.InitializeAsync` loads flag overrides. |
| 4 | `IThemeService.InitializeAsync` loads the saved theme, which is then applied on the UI thread. |
| 4b | `IPluginService.ActivateEnabledPluginsAsync` activates enabled plugins, dependencies first, before indexing so that plugin document processors are available to imports. |
| 4c | `IBackupService.StartScheduledBackupsAsync` starts the backup schedule when one is enabled. |
| 5 | `IIndexingService.InitializeAsync` (on a pool thread) starts the indexing pipeline. |
| 6 | `IFileWatcherService.InitializeAsync` (on a pool thread) starts the watch folders when "Auto-index watch folders" is on. |

Every step after the migration is best effort: a failure is logged and startup continues.

At shutdown, `ShutdownCoreServicesAsync` stops the built-in connectors (waiting at most 15 seconds), stops the Local REST API, stops scheduled backups, deactivates every plugin (dependents first, each within 10 seconds), disposes the host and flushes the log.

---

## AI Services

### IAiService

**Namespace**: `AgentX.Core.AI` | **Implementation**: `AiService`

Owns the AI providers and the active provider and model, and adds summarization and tagging on top of them. `InitializeAsync` (step 2 of startup) reads the settings and registers the providers it can build: `local` (the built-in model, always), `ollama` (only when `OllamaEndpoint` is an absolute http or https URL), `openai` and `anthropic` (only when their API key is set). The active provider is `ActiveProviderId` (default `local`); when that provider is not registered the service falls back to the built-in model if its file is installed, then Ollama, then any registered provider. Calling `InitializeAsync` again (the Settings page does this after a save) builds the new provider set first and swaps it in atomically; providers whose configuration did not change are kept, so a save does not reload the built-in model or cut off a streaming reply. Connection checks are cached for 60 seconds after a success and 15 seconds after a failure.

| Member | Description |
|--------|-------------|
| `IAiProvider ActiveProvider { get; }` | The active provider. Throws `InvalidOperationException` before `InitializeAsync`. |
| `bool IsConnected { get; }` | Whether the active provider answered its last connection check. |
| `string ActiveModelId { get; }` | The model sent with requests that do not name one. |
| `IReadOnlyCollection<string> RegisteredProviderIds { get; }` | Ids of the registered providers. |
| `Task InitializeAsync(CancellationToken ct = default)` | Builds or rebuilds the providers from the current settings and checks the active one. |
| `Task<bool> SwitchProviderAsync(string providerId, CancellationToken ct = default)` | Makes a registered, reachable provider active and sets the active model to that provider's default. Returns false, and keeps the previous provider, when the provider is not registered or not reachable. |
| `Task SetActiveModelAsync(string modelId, CancellationToken ct = default)` | Sets the active model and saves it in the active provider's own setting (`DefaultModel` for Ollama, `OpenAiDefaultModel`, `AnthropicDefaultModel`). For the built-in provider the choice lasts for the session only, because its configured file is also the embedding model. |
| `IAiProvider? GetProvider(string providerId)` | The registered provider with that id, or null (for example a cloud provider without an API key). |
| `Task<bool> IsProviderAvailableAsync(string providerId, CancellationToken ct = default)` | Whether a registered provider is reachable, reusing recent checks. |
| `string GetDefaultModelId(string providerId)` | The model a provider starts with: `LocalModelFileName` (default `llama-3.2-3b-instruct-q4_k_m.gguf`), `OpenAiDefaultModel` (default `gpt-4o-mini`), `AnthropicDefaultModel` (default `claude-sonnet-5`), or `DefaultModel` for Ollama (default `llama3.2`). |
| `EmbeddingTarget ResolveEmbeddingTarget()` | The provider and model that produce embeddings, chosen independently of the chat provider (see `EmbeddingTargetResolver` under [IEmbeddingService](#iembeddingservice)). |
| `IAsyncEnumerable<string> StreamChatAsync(IReadOnlyList<ChatMessage> messages, string? systemPrompt = null, ChatOptions? options = null, CancellationToken ct = default)` | Streams a reply from the active provider. A system prompt is prepended as a `system` message; options without a model id get the active model (the caller's options object is copied, not changed). |
| `Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, string? systemPrompt = null, ChatOptions? options = null, CancellationToken ct = default)` | The same as a single, complete reply. |
| `Task<string> SummarizeAsync(string content, CancellationToken ct = default)` | Asks the active model for a summary of at most 2-3 paragraphs. Throws `ArgumentException` for empty content. |
| `Task<IReadOnlyList<string>> GenerateTagsAsync(string content, int maxTags = 5, CancellationToken ct = default)` | Asks for a JSON array of lower-case tags (JSON response format) and parses it, falling back to comma or line splitting. Returns an empty list when the request fails. |

---

### IAiProvider

**Namespace**: `AgentX.Core.AI` | **Implementations**: `LocalLlmProvider`, `OllamaProvider`, `OpenAiProvider`, `AnthropicProvider` (namespace `AgentX.Core.AI.Providers`, created by `AiService`, not registered in DI)

One inference backend. Each provider counts its in-flight calls (`ProviderLifetime`), so disposing a replaced provider waits until the calls already running on it have finished.

| Provider | `ProviderId` | `DisplayName` | Notes |
|----------|--------------|---------------|-------|
| `LocalLlmProvider` | `local` | Built-in LLM | LLamaSharp (llama.cpp) running a GGUF file from `{StoragePath}\Models`. Embeddings always come from the configured model file, mean-pooled over the input; chat can use another installed GGUF named in `ChatOptions.ModelId`. |
| `OllamaProvider` | `ollama` | Ollama | OllamaSharp client for a local or remote Ollama server; pull, delete and list models. |
| `OpenAiProvider` | `openai` | OpenAI | Chat Completions API over `HttpClient` with server-sent events for streaming; default model `gpt-4o-mini`. |
| `AnthropicProvider` | `anthropic` | Anthropic Claude | Messages API over `HttpClient` with server-sent events; supports prompt caching through `ChatOptions.SystemPromptBlocks`; default model `claude-sonnet-5`. Has no embedding API. |

The Ollama, OpenAI and Anthropic providers record the token usage each response reports in [ICostTracker](#icosttracker).

| Member | Description |
|--------|-------------|
| `string ProviderId { get; }` | Stable id, as in the table above. |
| `string DisplayName { get; }` | Name shown in the UI. |
| `bool IsAvailable { get; }` | Result of the last `CheckConnectionAsync`. |
| `Task<bool> CheckConnectionAsync(CancellationToken ct = default)` | Tests whether the backend answers. |
| `Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)` | Models the provider can run. |
| `Task PullModelAsync(string modelName, IProgress<ModelDownloadProgress>? progress = null, CancellationToken ct = default)` | Downloads a model (Ollama registry, or a known GGUF for the built-in provider). |
| `Task DeleteModelAsync(string modelName, CancellationToken ct = default)` | Deletes an installed model. |
| `IAsyncEnumerable<string> StreamChatAsync(IReadOnlyList<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)` | Streams a reply token by token. |
| `Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)` | Returns a complete reply. |
| `Task<float[]> GenerateEmbeddingAsync(string text, string modelName, CancellationToken ct = default)` | Embeds one text with the named model. |
| `Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, string modelName, CancellationToken ct = default)` | Embeds several texts. |

---

### IModelManager

**Namespace**: `AgentX.Core.AI` | **Implementation**: `ModelManager`

Model list, pull and delete for the active provider (used by the Model Manager page). The installed-model list is cached for 30 seconds per provider instance, so a provider switch never shows the previous provider's list.

| Member | Description |
|--------|-------------|
| `Task<IReadOnlyList<AiModel>> GetAvailableModelsAsync(CancellationToken ct = default)` | Same as `GetInstalledModelsAsync`; no remote catalog is queried. |
| `Task<IReadOnlyList<AiModel>> GetInstalledModelsAsync(CancellationToken ct = default)` | The active provider's models (cached for 30 seconds). |
| `Task PullModelAsync(string modelName, IProgress<ModelDownloadProgress>? progress = null, CancellationToken ct = default)` | Pulls through the active provider, clears the cache and raises `ModelListChanged`. |
| `Task DeleteModelAsync(string modelName, CancellationToken ct = default)` | Deletes through the active provider, clears the cache and raises `ModelListChanged`. |
| `Task<AiModel?> GetModelInfoAsync(string modelName, CancellationToken ct = default)` | The installed model whose name or id matches, ignoring case, or null. |
| `Task<bool> IsModelAvailableAsync(string modelName, CancellationToken ct = default)` | Whether `GetModelInfoAsync` finds the model. |
| `event EventHandler<AiModel>? ModelListChanged` | Raised after a pull or delete. |

---

### IBuiltInModelBootstrap

**Namespace**: `AgentX.Core.AI` | **Implementation**: `BuiltInModelBootstrap` (built in `App.xaml.cs` with the models folder `{StoragePath}\Models` and `LocalModelFileName`)

Ensures the built-in model file (Llama 3.2 3B Instruct, Q4_K_M GGUF, about 1.9 GB) is on disk. The download source is listed in `BuiltInModelCatalog` (the Hugging Face `hugging-quants` repository); no SHA-256 is pinned yet, so only the size checks apply. The onboarding flow uses it.

| Member | Description |
|--------|-------------|
| `string ModelFileName { get; }` | GGUF file name the built-in provider loads. |
| `string ModelDisplayName { get; }` | Name for the UI. |
| `long ExpectedSizeBytes { get; }` | Approximate size, for display and as a progress fallback. |
| `string ModelPath { get; }` | Full path of the model file. |
| `bool IsInstalled()` | True when a complete file is present (at or above the size floor); a truncated leftover counts as not installed. |
| `Task EnsureInstalledAsync(IProgress<ModelDownloadProgress>? progress = null, CancellationToken ct = default)` | Downloads only when the model is not installed. |
| `Task DownloadAsync(IProgress<ModelDownloadProgress>? progress = null, CancellationToken ct = default)` | Downloads to a `.part` file, verifies it, then moves it into place; a cancelled or failed download leaves nothing behind. |

---

### IHardwareDetector

**Namespace**: `AgentX.Core.AI` | **Implementation**: `HardwareDetector`

Reads the GPU, CPU, memory and NPU through WMI on a background thread. The dedicated video memory comes from the display driver's 64-bit registry value when it publishes one (WMI's `AdapterRAM` saturates at 4 GB). The result is cached for the life of the process. Used by the Hardware Advisor, the Dashboard and onboarding.

| Member | Description |
|--------|-------------|
| `Task<HardwareCapability> DetectAsync(CancellationToken ct = default)` | Detects once, then returns the cached [HardwareCapability](#ai-models). |

---

### ITokenCounter

**Namespace**: `AgentX.Core.AI` | **Implementation**: `TokenCounter`

Estimates tokens for chunking. Latin-script text counts about 4 characters per token; CJK characters count separately at about 1.7 tokens each. Context window sizes come from a table of known model families, matched by exact id or longest prefix.

| Member | Description |
|--------|-------------|
| `int CountTokens(string text, string? modelId = null)` | Estimated tokens; `modelId` defaults to the configured embedding model. |
| `IReadOnlyList<int> CountTokensBatch(IReadOnlyList<string> texts, string? modelId = null)` | Estimates for several texts, in order. |
| `int GetRemainingCapacity(int usedTokens, string? modelId = null)` | The model's context window minus `usedTokens`, never below 0. |

---

### IContextWindowManager

**Namespace**: `AgentX.Core.AI` | **Implementation**: `ContextWindowManager`

Token estimation and history trimming for chat, with the same script-aware estimate as `ITokenCounter` plus about 4 tokens of overhead per message.

| Member | Description |
|--------|-------------|
| `Task<List<ChatMessage>> FitToContextWindowAsync(List<ChatMessage> messages, int maxTokens, int reserveForResponse = 1024, CancellationToken ct = default)` | Keeps the system prompt and the most recent messages and drops older ones until the history fits `maxTokens - reserveForResponse`. |
| `int EstimateTokenCount(string text)` | Estimated tokens of one text. |
| `int EstimateTokenCount(IEnumerable<ChatMessage> messages)` | Estimated tokens of a message list, including per-message overhead. |
| `int GetEffectiveContextWindow(int reportedContextLength)` | The reported context length, capped at 131,072 tokens; 4,096 when the model reports none (0 or less). |

---

### IEmbeddingService

**Namespace**: `AgentX.Core.AI` | **Implementation**: `CachedEmbeddingService` wrapping `EmbeddingService` (both registered; resolve the interface)

Produces the vectors the index and semantic search use. The embedding provider and model come from `IAiService.ResolveEmbeddingTarget()`, which applies `EmbeddingTargetResolver` to the Embedding Model setting:

1. an OpenAI model id (`text-embedding-*`) embeds with OpenAI; this is the only way document text is embedded in the cloud;
2. a `.gguf` file name uses the built-in provider;
3. any other name other than the default `all-minilm` is an Ollama model;
4. otherwise the built-in model is used when its file is installed, and Ollama with `all-minilm` when it is not.

Anthropic is never used for embeddings. Changing the chat provider or model never changes the embedding space. `CachedEmbeddingService` keeps up to 2,048 vectors (least recently used first out; each expires after `EmbeddingCacheExpirationMinutes`, 7 days by default), keyed by `ModelVersion` and a hash of the normalized text, and drops the entries of a previous model version as soon as the version changes.

| Member | Description |
|--------|-------------|
| `int Dimensions { get; }` | Vector size: the size the model last returned, else the known size of well-known models, else `IRagConfiguration.DefaultEmbeddingDimensions`. |
| `string ModelName { get; }` | Embedding model name. |
| `string ModelVersion { get; }` | `{provider}:{model}:{dimensions}`, for example `ollama:all-minilm:384`. Chunks are stamped with it, and retrieval ignores chunks embedded under another value. |
| `Task<float[]> EmbedAsync(string text, CancellationToken ct = default)` | Embeds one text. Throws `ArgumentException` for empty text. |
| `Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default)` | Embeds texts in batches of `IRagConfiguration.EmbeddingBatchSize`. |

---

### ICostTracker

**Namespace**: `AgentX.Core.AI.Models` | **Implementation**: `CostTracker`

Records the token usage the cloud providers report and prices it with a built-in table of OpenAI and Anthropic models (per 1,000 tokens, matched by exact id or longest prefix of the same provider). Local models (built-in and Ollama) cost nothing. Prompt-cache writes are billed at 1.25 times the input price and cache reads at the model's cache-read price (10% of input when none is listed).

Usage survives restarts. The records are kept in `%LOCALAPPDATA%\AgentX\usage-history.json`, loaded when the tracker is created and written atomically about 2 seconds after new usage and when the app shuts down. A record is kept for 90 days and at most 20,000 records are kept; what a dropped record cost and used is carried into the totals, so `GetTotalCostUsd` and the token totals cover all tracked usage, while `GetCostForPeriod` and `GetUsageHistory` see the kept records only. A corrupt file is renamed to `usage-history.json.corrupt` and a new history starts. The Settings page reads the totals.

| Member | Description |
|--------|-------------|
| `void RecordUsage(string modelId, string providerId, int inputTokens, int outputTokens)` | Records one response. |
| `void RecordUsage(string modelId, string providerId, int inputTokens, int outputTokens, int cacheCreationInputTokens, int cacheReadInputTokens)` | Records one response with prompt-cache activity; `inputTokens` are the uncached prompt tokens. |
| `double GetTotalCostUsd()` | Estimated cost of all tracked usage. |
| `double GetCostForPeriod(DateTime start, DateTime end)` | Estimated cost of the kept records in the period (inclusive, UTC timestamps). |
| `IReadOnlyList<UsageRecord> GetUsageHistory(int limit = 50)` | The newest kept records first. |
| `int GetTotalInputTokens()` | All tracked prompt tokens, capped at `int.MaxValue`. |
| `int GetTotalOutputTokens()` | All tracked output tokens, capped at `int.MaxValue`. |

---

### IContextAssemblyService, ISemanticContextSelector, IConversationCompressionService

**Namespace**: `AgentX.Core.AI.Context` | **Implementations**: `ContextAssemblyService`, `SemanticContextSelector`, `ConversationCompressionService`

Build the prompt for each chat reply (called by [IChatService](#ichatservice)).

| Member | Description |
|--------|-------------|
| `IContextAssemblyService.AssembleAsync(ContextAssemblyRequest request, CancellationToken ct = default)` | Returns the messages and system prompt for one reply, plus the durable recall results and diagnostics. When the whole history fits the context window it is sent as it is. Otherwise the last 4 messages are always kept, older messages are chosen by relevance to the question within the remaining budget, the overflow is condensed into a summary when there is room, and up to 3 messages from other conversations (similarity 0.72 or more, from [IConversationRecallService](#iconversationrecallservice)) are added as recalled context. Any failure falls back to plain trimming (`IContextWindowManager.FitToContextWindowAsync`). |
| `ISemanticContextSelector.SelectRelevantContextAsync(ContextSelectionRequest request, CancellationToken ct = default)` | Scores the older messages by a weighted mix of embedding similarity to the question, word overlap and recency (`SemanticWeight`, `LexicalWeight`, `RecencyWeight` in [IRagConfiguration](#iragconfiguration-and-iragpromptcatalog); without embeddings only overlap and recency count), picks the best within the token budget, and adds the other half of a picked question and answer pair when it fits. |
| `IConversationCompressionService.CompressAsync(ConversationCompressionRequest request, CancellationToken ct = default)` | Summarizes the overflow messages: the most recent ones that fit the transcript window, older ones dropped first. The summary does not depend on the question, so the same overflow is summarized once and then served from a small cache. |

---

### IModelRouterService and ITaskTypeDetector

**Namespace**: `AgentX.Core.AI.Routing` | **Implementations**: `ModelRouterService`, `TaskTypeDetector`

Smart model routing. When `EnableModelRouting` is on, [IChatService](#ichatservice) asks the router for each reply's provider and model and uses them for that reply only; routing never switches the app-wide provider or changes settings. A routed provider that is not registered or not reachable falls back to the active provider. A local preference never falls back to a cloud provider.

The profiles are `RoutingProfile.CostOptimized` (`cost-optimized`), `QualityOptimized` (`quality-optimized`) and `Balanced` (`balanced`); the saved `ActiveRoutingProfileId` applies until `SetActiveProfile` picks another for the session. Task types are `extraction`, `summarization`, `analysis`, `generation`, `code`, `creative`, `chat` and `embedding`.

| Member | Description |
|--------|-------------|
| `IModelRouterService.ActiveProfile` | The session's selected profile, else the saved one, else Balanced. |
| `IModelRouterService.SetActiveProfile(RoutingProfile profile)` / `SetActiveProfile(string profileId)` | Selects a profile for the session; an unknown id means Balanced. |
| `IModelRouterService.RouteAsync(string prompt, CancellationToken ct = default)` | Detects the task type and returns a `RoutingDecision` (provider, model, task type, profile, reason). |
| `IModelRouterService.RouteAsync(string prompt, TaskType taskTypeOverride, CancellationToken ct = default)` | The same with a fixed task type. |
| `IModelRouterService.DecisionMade` | Raised for every decision. |
| `ITaskTypeDetector.Detect(string prompt)` | An explicit tag such as `[analysis]` at the start of the prompt wins; otherwise the first matching group of whole-word keywords (extraction, summarization, analysis, embedding, creative, code, generation, in that order; everyday uses such as "zip code" are ignored); otherwise `chat`. |

---

### IMultiAgentOrchestrator

**Namespace**: `AgentX.Core.AI.Agents` | **Implementation**: `MultiAgentOrchestrator`

Runs several agent roles (`AgentRole`: name, expertise, system prompt, temperature) over one task with the active provider. Used by the chat's orchestration modes (`ChatOrchestrationMode` in [IMessagingCoordinator](#app-chat-coordinators)). Predefined roles: `AgentRole.Researcher()`, `Critic()`, `Synthesizer()`, `Creative()` and `TechnicalExpert(domain)`. Honors cancellation and reports failed agents in the result's `Errors`.

| Member | Description |
|--------|-------------|
| `Task<OrchestrationResult> RunAsync(string task, IReadOnlyList<AgentRole> agents, OrchestratorStrategy strategy = OrchestratorStrategy.Sequential, CancellationToken ct = default)` | Runs the agents with one of the `OrchestratorStrategy` values: `Sequential`, `Parallel`, `Debate`, `DivideAndConquer`, `GenerateCritiqueRefine`. |
| `Task<DebateResult> RunDebateAsync(string task, IReadOnlyList<AgentRole> agents, int rounds = 2, CancellationToken ct = default)` | Rounds of positions and counterpoints, then a synthesis. |
| `Task<ParallelResult> RunParallelAsync(string task, IReadOnlyList<AgentRole> agents, CancellationToken ct = default)` | All agents at once, with the combined output. |

---

## Chat Services

### IChatService

**Namespace**: `AgentX.Core.Services.Chat` | **Implementation**: `ChatService`

Sends a message, streams the reply and saves both. For every reply it:

1. saves the user message (not for a regeneration, which reuses the saved one);
2. asks [IModelRouterService](#imodelrouterservice-and-itasktypedetector) for this reply's provider and model when `EnableModelRouting` is on (the decision applies to this reply only);
3. collects memory context: up to 8 memories with similarity 0.65 or more from [ISemanticMemoryService](#isemanticmemoryservice), plus the durable summary of the conversation, plus the supplemental context of this reply (Research Mode's web results);
4. assembles the prompt with [IContextAssemblyService](#icontextassemblyservice-isemanticcontextselector-iconversationcompressionservice) and keeps the result as the latest context inspection;
5. streams the reply and saves it with the id of the model that wrote it, its token count, its generation time and the sources it cites (`MessageCitations` JSON);
6. extracts memories from the conversation in the background.

Starting a new generation cancels one that is still running. An empty reply is not saved. Belief and voice learning ([ITemporalIdentityService](#itemporalidentityservice)) are run by the chat view model after the reply, not by this service. `ChatService` also exposes a public `RoutingDecisionMade` event (not on the interface).

| Member | Description |
|--------|-------------|
| `IAsyncEnumerable<string> SendMessageAsync(long conversationId, string userMessage, CancellationToken ct = default)` | Sends a message and streams the reply. An empty message yields nothing. |
| `IAsyncEnumerable<string> SendMessageAsync(long conversationId, string userMessage, SupplementalContext? supplementalContext, CancellationToken ct)` | The same, adding `supplementalContext` for this reply only: its prompt text is not saved, its citations are saved with the answer. |
| `IAsyncEnumerable<string> RegenerateResponseAsync(long conversationId, long userMessageId, CancellationToken ct = default)` | Streams a new answer to a saved user message without saving the message again. The message must close the conversation (optionally followed by its answer); the old answer is removed only after the new one is saved, so stopping or failing keeps it. Throws `InvalidOperationException` otherwise. |
| `Task<string> SendMessageAndWaitAsync(long conversationId, string userMessage, CancellationToken ct = default)` | Sends a message and returns the complete reply. |
| `ChatContextInspectionSnapshot? GetLatestContextInspection(long conversationId)` | The in-memory record of what the last assembled context contained (not persisted); shown by the chat's context inspector. |
| `Task<ConversationSummaryRefreshResult> RefreshConversationSummaryInspectionAsync(long conversationId, CancellationToken ct = default)` | Refreshes the durable summary of one conversation and updates the cached inspection. |
| `Task StopGenerationAsync()` | Cancels the running generation. |
| `bool IsGenerating { get; }` | Whether a reply is being generated. |
| `event EventHandler<bool>? GenerationStateChanged` | Raised when `IsGenerating` changes. |

---

### IConversationService

**Namespace**: `AgentX.Core.Services.Chat` | **Implementation**: `ConversationService`

Conversation and message persistence. Saving a message also refreshes its recall embedding ([IConversationRecallService](#iconversationrecallservice)) and marks the durable summary stale ([IConversationSummaryService](#iconversationsummaryservice)); failures of those steps are logged, not thrown.

| Member | Description |
|--------|-------------|
| `Task<ConversationEntity> CreateConversationAsync(string? title = null, string? systemPrompt = null, string? modelId = null)` | Creates a conversation. |
| `Task<ConversationEntity?> GetConversationAsync(long conversationId)` | A conversation with its messages, or null. |
| `Task<IReadOnlyList<ConversationEntity>> GetAllConversationsAsync(bool includeArchived = false)` | All conversations, newest update first. |
| `Task<IReadOnlyList<ConversationEntity>> GetRecentConversationsAsync(int limit = 5, bool includeArchived = false, CancellationToken ct = default)` | The most recently updated conversations. |
| `Task<IReadOnlyList<ConversationEntity>> SearchConversationsAsync(string query)` | Non-archived conversations whose title or any message contains the text (SQL `LIKE`); an empty query returns all. |
| `Task UpdateConversationTitleAsync(long conversationId, string title)` | Renames a conversation. |
| `Task TogglePinAsync(long conversationId)` | Pins or unpins a conversation. |
| `Task ArchiveConversationAsync(long conversationId)` | Archives a conversation (hidden from the default list). |
| `Task DeleteConversationAsync(long conversationId)` | Deletes a conversation and its messages. Its branches are kept: they are promoted into its place in the branch tree. |
| `Task<IReadOnlyList<MessageEntity>> GetMessagesAsync(long conversationId)` | Messages in `SortOrder`. |
| `Task AddMessageAsync(long conversationId, string role, string content, int? tokenCount = null, double? generationTimeMs = null, string? modelId = null, string? citationsJson = null)` | Adds a message (`user`, `assistant` or `system`) and updates the conversation. `modelId` is the model that wrote an assistant message; `citationsJson` its sources in the `MessageCitations` format (web pages or documents). |
| `Task DeleteMessageAsync(long messageId)` | Deletes one message. |
| `Task UpdateMessageContentAsync(long messageId, string newContent)` | Edits a message and refreshes its embedding. |
| `Task<int> DeleteMessageAndFollowingAsync(long conversationId, long messageId)` | Deletes a message and every later message in one save (used when an edited prompt is resent); returns the count, 0 when the message is not in the conversation. |
| `Task<int> GetConversationCountAsync()` | Number of non-archived conversations. |
| `Task<long> GetTotalTokensUsedAsync()` | Sum of `TokensUsed` over all conversations. |
| `Task SetConversationFolderAsync(long conversationId, string? folderName)` | Puts a conversation in a folder, or takes it out with null. |
| `Task<IReadOnlyList<string>> GetAllFolderNamesAsync()` | Folder names in use. |
| `Task AddTagToConversationAsync(long conversationId, long tagId)` / `Task RemoveTagFromConversationAsync(long conversationId, long tagId)` | Tags a conversation, or removes the tag. |
| `Task<IReadOnlyList<ConversationEntity>> GetConversationsByFolderAsync(string folderName)` | Conversations in a folder. |

---

### IConversationBranchService

**Namespace**: `AgentX.Core.Services.Chat` | **Implementation**: `ConversationBranchService`

Conversation branches. A branch is a conversation whose `ParentConversationId` and `BranchPointMessageId` point at the message it was forked from.

| Member | Description |
|--------|-------------|
| `Task<ConversationEntity> BranchAtMessageAsync(long conversationId, long messageId, string? branchLabel = null, CancellationToken ct = default)` | Creates a branch holding copies of all messages up to and including `messageId`. |
| `Task<IReadOnlyList<ConversationEntity>> GetBranchesAsync(long conversationId, CancellationToken ct = default)` | Direct child branches. |
| `Task<ConversationBranchTree> GetBranchTreeAsync(long rootConversationId, CancellationToken ct = default)` | The whole tree, rooted at the ultimate root even when a branch id is passed. |
| `Task<ConversationEntity> GetRootConversationAsync(long conversationId, CancellationToken ct = default)` | The conversation at the top of the tree. |
| `Task MergeMessagesAsync(long sourceConversationId, IReadOnlyList<long> messageIds, long targetConversationId, CancellationToken ct = default)` | Appends copies of the chosen messages to another conversation ("merge insights"). |
| `Task<int> GetBranchCountAsync(long conversationId, CancellationToken ct = default)` | Number of direct branches. |
| `Task<bool> HasBranchesAtMessageAsync(long messageId, CancellationToken ct = default)` | Whether any branch forks at the message. |
| `Task DeleteBranchAsync(long branchConversationId, bool recursive = true, CancellationToken ct = default)` | Deletes a branch (never a root), with its sub-branches by default. |

---

### IConversationMemoryService

**Namespace**: `AgentX.Core.Services.Chat` | **Implementation**: `ConversationMemoryService`

The memories chat keeps about the user, as the chat UI manages them: the context inspector's Memories card lists them, deletes one, or clears all; the suggested follow-up questions also come from here. Extraction for new replies runs through [ISemanticMemoryService](#isemanticmemoryservice), which writes the same `MemoryEntity` rows.

| Member | Description |
|--------|-------------|
| `Task ExtractMemoriesAsync(long conversationId, CancellationToken ct = default)` | Asks the active model for facts and preferences in the newest messages and stores them. |
| `Task<string> GetMemoryContextAsync(int maxMemories = 10, CancellationToken ct = default)` | Active memories as a context block for a system prompt. |
| `Task<IReadOnlyList<string>> GetSuggestedQuestionsAsync(long conversationId, CancellationToken ct = default)` | Follow-up questions from the conversation and the memories, generated by the active model. |
| `Task<IReadOnlyList<MemoryEntity>> GetAllMemoriesAsync(CancellationToken ct = default)` | Active (not dismissed) memories. |
| `Task DismissMemoryAsync(long memoryId, CancellationToken ct = default)` | Hides a memory; its text stays in the database. |
| `Task<bool> DeleteMemoryAsync(long memoryId, CancellationToken ct = default)` | Deletes a memory permanently and clears links from other memories to it; false when there was none. |
| `Task<int> DeleteAllMemoriesAsync(CancellationToken ct = default)` | Deletes every memory, dismissed ones included; returns the count. Conversations are kept, so facts can be noted again from new messages. |
| `Task<int> GetMemoryCountAsync(CancellationToken ct = default)` | Number of active memories. |

---

### ISemanticMemoryService

**Namespace**: `AgentX.Core.Services.Chat` | **Implementation**: `SemanticMemoryService`

Embedding-based memory used by [IChatService](#ichatservice). Extraction reads the last 10 messages of a conversation, asks the active model for `category|content|confidence` lines (preferences, facts, topics, instructions, project context and similar categories), embeds each memory and links similar ones. Retrieval ranks by similarity and by importance with temporal decay.

| Member | Description |
|--------|-------------|
| `Task<IReadOnlyList<MemoryEntity>> RetrieveRelevantMemoriesAsync(string query, int maxMemories = 10, float minSimilarity = 0.7f, CancellationToken ct = default)` | Memories similar to the query, ranked by effective importance. |
| `Task<IReadOnlyList<MemoryEntity>> RetrieveAssociativeMemoriesAsync(long seedMemoryId, int maxDepth = 2, CancellationToken ct = default)` | Memories reachable through associative links. |
| `Task ExtractMemoriesAsync(long conversationId, CancellationToken ct = default)` | Extracts, embeds and links new memories. |
| `Task LinkMemoriesAsync(long memoryId1, long memoryId2, CancellationToken ct = default)` | Links two memories. |
| `Task ApplyFeedbackAsync(long memoryId, bool isPositive, CancellationToken ct = default)` | Raises or lowers a memory's importance. |
| `double GetEffectiveImportance(MemoryEntity memory)` | Base importance times temporal decay. |
| `Task<IReadOnlyList<MemoryEntity>> GetAllMemoriesAsync(CancellationToken ct = default)` | Active memories, by effective importance. |
| `Task DismissMemoryAsync(long memoryId, CancellationToken ct = default)` | Hides a memory. |
| `Task<int> GetMemoryCountAsync(CancellationToken ct = default)` | Number of active memories. |

---

### IConversationRecallService

**Namespace**: `AgentX.Core.Services.Chat` | **Implementation**: `ConversationRecallService`

Stores an embedding per message so replies can recall relevant messages from other conversations (see `IContextAssemblyService`). The Analytics page shows its coverage.

| Member | Description |
|--------|-------------|
| `Task<bool> RefreshMessageEmbeddingAsync(long messageId, bool forceRefresh = false, CancellationToken ct = default)` | Embeds one eligible message; true when an embedding was created or replaced. |
| `Task<int> RefreshConversationEmbeddingsAsync(long conversationId, bool forceRefresh = false, CancellationToken ct = default)` | Embeds the missing or stale messages of one conversation. |
| `Task<int> RefreshRecentConversationEmbeddingsAsync(int maxConversations = 4, CancellationToken ct = default)` | Backfills a few recent conversations. |
| `Task<IReadOnlyList<ConversationRecallResult>> SearchRelevantMessagesAsync(string query, int maxResults = 6, float minSimilarity = 0.65f, long? excludeConversationId = null, CancellationToken ct = default)` | Past messages similar to the query, most similar first. |

---

### IConversationSummaryService

**Namespace**: `AgentX.Core.Services.Chat` | **Implementation**: `ConversationSummaryService`

Durable conversation summaries: immutable snapshots plus a per-conversation state row that tracks freshness. Summaries are written by the active model (only while it is connected) and never block chat writes; a new snapshot also updates the conversation's theme cluster ([IConversationThemeClusterService](#iconversationthemeclusterservice-and-iconversationthemetrendservice)). The Operations page and Analytics can refresh them.

| Member | Description |
|--------|-------------|
| `Task<string> GetConversationSummaryContextAsync(long conversationId, CancellationToken ct = default)` | The current summary as a context block, or an empty string. |
| `Task<ConversationSummaryInspection?> GetConversationSummaryInspectionAsync(long conversationId, CancellationToken ct = default)` | The summary state for the chat's context inspector, or null. |
| `Task MarkConversationStaleAsync(long conversationId, bool forceFullRefresh = false, CancellationToken ct = default)` | Marks the summary stale after a change; `forceFullRefresh` rebuilds from the whole transcript next time. |
| `Task<bool> RefreshConversationSummaryAsync(long conversationId, CancellationToken ct = default)` | Refreshes one summary if needed; true when a snapshot was created. |
| `Task<int> RefreshStaleSummariesAsync(int maxConversations = 4, CancellationToken ct = default)` | Refreshes a few stale or unsummarized conversations. |

---

### ISystemPromptService

**Namespace**: `AgentX.Core.Services.Chat` | **Implementation**: `SystemPromptService`

System prompt templates for chat. The chat view model seeds the built-in prompts when it loads.

| Member | Description |
|--------|-------------|
| `Task<IReadOnlyList<SystemPromptEntity>> GetAllPromptsAsync(string? category = null)` | Prompts, favorites first, then by usage. |
| `Task<SystemPromptEntity?> GetPromptAsync(long id)` | One prompt, or null. |
| `Task<SystemPromptEntity> CreatePromptAsync(string name, string content, string category)` | Creates a user prompt. |
| `Task UpdatePromptAsync(long id, string name, string content, string category)` | Edits a prompt. |
| `Task DeletePromptAsync(long id)` | Deletes a prompt; built-in prompts cannot be deleted. |
| `Task ToggleFavoriteAsync(long id)` | Marks or unmarks a favorite. |
| `Task IncrementUsageAsync(long id)` | Counts a use. |
| `Task SeedBuiltInPromptsAsync()` | Adds the built-in prompts that are missing. |

---

### IFeedbackService

**Namespace**: `AgentX.Core.Services.Feedback` | **Implementation**: `FeedbackService`

Ratings of assistant messages, one row per message (a new rating replaces the old one). The chat uses `SubmitFeedbackAsync` for its rating buttons and `GetFeedbackForMessageAsync` to show a saved rating; nothing in the app calls the other members yet, so feedback is not fed back into prompts.

| Member | Description |
|--------|-------------|
| `Task SubmitFeedbackAsync(long messageId, long conversationId, string rating, string? preferredResponse = null, string? note = null, string? category = null, CancellationToken ct = default)` | Saves or updates the rating (`positive`, `negative` or `none`) with an optional corrected answer, note and category. |
| `Task<FeedbackEntity?> GetFeedbackForMessageAsync(long messageId, CancellationToken ct = default)` | The rating of a message, or null. |
| `Task<IReadOnlyList<FeedbackEntity>> GetPositiveFeedbackAsync(int limit = 50, CancellationToken ct = default)` / `GetNegativeFeedbackAsync(...)` | Newest positive or negative ratings. |
| `Task<FeedbackSummary> GetFeedbackSummaryAsync(CancellationToken ct = default)` | Aggregate counts. |
| `Task<string> BuildFewShotExamplesAsync(int maxExamples = 5, CancellationToken ct = default)` | Positive ratings with a corrected answer, formatted as `### Example N` blocks for a system prompt; empty when there are none. |
| `Task DeleteFeedbackAsync(long feedbackId, CancellationToken ct = default)` | Deletes a rating. |

---

## Document Services

### IDocumentService

**Namespace**: `AgentX.Core.Documents` | **Implementation**: `DocumentService`

The import pipeline of the Knowledge Vault. An import checks that the file exists, hashes it (SHA-256), refuses content that is already in the vault (`DuplicateDocumentException`) unless duplicates are allowed, picks a processor, extracts the text and saves a `DocumentEntity` with status `pending`. A file the processor cannot read is still saved, with status `failed` and the reason in `IndexingError`, so the problem shows in the vault. After a successful extraction the service raises `DocumentPendingIndexing` with the extracted text, and [IIndexingService](#iindexingservice) queues the document at once and reuses that text.

Processor selection: the built-in [IDocumentProcessor](#idocumentprocessor) implementations first, then the processors of active plugins ([IPluginDocumentProcessorSource](#iplugindocumentprocessorsource)) for files no built-in processor claims.

| Member | Description |
|--------|-------------|
| `event EventHandler<DocumentPendingIndexingEventArgs>? DocumentPendingIndexing` | Raised after a document was imported or reset for re-indexing and waits in `pending`; carries the document id and, when available, the extracted text. Handlers run on the caller's thread. |
| `Task<DocumentEntity> ImportFileAsync(string filePath, long? collectionId = null, CancellationToken ct = default)` | Imports one file (optionally into a collection). Throws `FileNotFoundException`, `NotSupportedException` (no processor) or `DuplicateDocumentException`. |
| `Task<DocumentEntity> ImportExternalContentAsync(string filePath, string fileTypeOverride, string displayName, string? sourceUrl = null, long? collectionId = null, CancellationToken ct = default)` | Imports a text file written for a connector item (calendar event, email), keeping the semantic file type (`CalendarEvent`, `EmailMessage`) and display name; the source URL is stored in the metadata. |
| `Task<DocumentEntity> ImportPreparedDocumentAsync(DocumentEntity document, long? collectionId = null, CancellationToken ct = default)` | Records a document for a file Agent-X wrote itself (a page saved by Web Import). The document and its collection link are saved together; throws `DuplicateDocumentException`, or `InvalidOperationException` when the collection does not exist. |
| `Task<IReadOnlyList<DocumentEntity>> ImportFilesAsync(IReadOnlyList<string> filePaths, long? collectionId = null, IProgress<int>? progress = null, CancellationToken ct = default)` | Imports several files and returns the documents that were created; duplicates and files that could not be imported are skipped without an exception (use `ImportFilesWithReportAsync` to learn which). |
| `Task<DocumentImportReport> ImportFilesWithReportAsync(IReadOnlyList<string> filePaths, long? collectionId = null, bool allowDuplicates = false, IProgress<int>? progress = null, CancellationToken ct = default)` | Imports several files and reports each outcome: imported (possibly as failed), skipped as a duplicate, or not imported with a reason. One file's failure does not stop the batch. |
| `Task<DocumentEntity?> GetDocumentAsync(long documentId)` | One document, or null. |
| `Task<string?> GetDocumentPreviewTextAsync(long documentId, int maxChars = 1800, CancellationToken ct = default)` | The stored summary, else the first indexed chunk, trimmed to 200 to 4,000 characters; used to launch a workflow from a document. |
| `Task<IReadOnlyList<DocumentEntity>> GetAllDocumentsAsync(string? fileTypeFilter = null, string? statusFilter = null, string? tagFilter = null, long? collectionId = null, DateTime? importedAfter = null, DateTime? importedBefore = null, string? sortBy = null, CancellationToken ct = default)` | Filtered document list, newest import first. `fileTypeFilter` is an extension without the dot, or `code` / `image` for every extension of the code or image processor; `sortBy` is `name`, `date` (default), `size` or `type`. |
| `Task<IReadOnlyList<DocumentEntity>> GetRecentDocumentsAsync(int limit = 5, CancellationToken ct = default)` | The newest imports. |
| `Task DeleteDocumentAsync(long documentId)` | Deletes the document with its vectors, keyword rows, chunks, collection links (keeping collection counts right), tags and annotations, and drops cached search results. The file on disk is not touched. |
| `Task ReindexDocumentAsync(long documentId, CancellationToken ct = default)` | Extracts the text again first; only then removes the old chunks, vectors and keyword rows, resets the document to `pending` and raises `DocumentPendingIndexing`. A missing source or a failed extraction marks the document `failed` and leaves its current index data alone. |
| `Task<DocumentEntity?> GetDocumentByHashAsync(string contentHash)` | The document with that SHA-256 hash, or null. |
| `Task<long> GetTotalDocumentCountAsync()` | Number of documents. |
| `Task<long> GetTotalStorageBytesAsync()` | Sum of the documents' file sizes. |
| `Task<Dictionary<string, int>> GetFileTypeDistributionAsync()` | Document count per file type. |
| `bool CanProcess(string filePath)` | Whether a built-in or active plugin processor reads the file. |
| `IReadOnlySet<string> GetSupportedExtensions()` | Extensions of the built-in processors plus those of active plugin processors (computed per call). Folder imports and dropped folders use it. |
| `Task<DuplicateCheckResult> CheckForDuplicateAsync(string filePath, CancellationToken ct = default)` | Whether a document with the same SHA-256 hash exists (exact match only). |
| `Task BulkDeleteAsync(IReadOnlyList<long> documentIds, CancellationToken ct = default)` | Deletes several documents; failures are logged and skipped. |
| `Task BulkReindexAsync(IReadOnlyList<long> documentIds, CancellationToken ct = default)` | Calls `ReindexDocumentAsync` for each; failures are logged and skipped. |
| `Task<int> RequeueAudioAwaitingSpeechModelAsync(CancellationToken ct = default)` | Queues the audio documents that have no transcript because the speech-to-text model was missing (failed with `AudioProcessor.SpeechModelMissingError`, or imported by earlier versions with a placeholder transcript): each is reset to `pending` and handed to the indexer, which transcribes it. The Model Manager calls it after the model is downloaded; returns how many were queued. |
| `Task BulkAssignToCollectionAsync(IReadOnlyList<long> documentIds, long collectionId, CancellationToken ct = default)` | Adds several documents to a collection. |

---

### IDocumentProcessor

**Namespace**: `AgentX.Core.Documents` | **Implementations**: namespace `AgentX.Core.Documents.Processors`, registered in this order

Turns one file into text (`ProcessedDocument`). A processor that cannot read a file throws `DocumentExtractionException` with a message for the user, which becomes the document's failure reason.

| Processor | Extensions | How |
|-----------|------------|-----|
| `PdfProcessor` | `.pdf` | PDFsharp content-stream parser (text-show operators). A PDF without a text layer (scanned) or with font encodings it cannot decode fails with a reason; there is no OCR for PDF files. |
| `DocxProcessor` | `.docx` | OpenXML paragraphs, with heading styles as sections. The legacy `.doc` format is not claimed. |
| `TextProcessor` | `.txt`, `.csv`, `.log`, `.json`, `.xml`, `.yaml`, `.yml`, `.toml`, `.ini`, `.cfg` | Raw text; UTF-8 first, then the system encoding. |
| `MarkdownProcessor` | `.md`, `.mdx`, `.markdown` | Markdig plain text; the first H1 becomes the title. |
| `CodeFileProcessor` | `.cs`, `.js`, `.ts`, `.py`, `.java`, `.cpp`, `.c`, `.h`, `.go`, `.rs`, `.swift`, `.kt`, `.rb`, `.php`, `.html`, `.htm`, `.css`, `.scss`, `.sql`, `.sh`, `.yaml`, `.yml`, `.toml`, `.ini`, `.cfg`, `.xaml` | Source text with the detected language; the first class, function or module name becomes the title. (`TextProcessor` is registered first, so it reads the YAML, TOML, INI and CFG extensions both claim.) |
| `ImageProcessor` | `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tiff` | Windows OCR (`OcrEngine`) with the installed recognizers; an image without text yields empty text. |
| `AudioProcessor` | `.mp3`, `.wav`, `.m4a`, `.flac`, `.ogg`, `.webm` | Transcribes through [ITranscriptionService](#itranscriptionservice) into a timestamped transcript. Fails with a reason when the speech-to-text model is not installed, the Whisper runtime cannot load, or the audio cannot be decoded. |
| `WebProcessor` | `.url`, `.webloc` | Reads the shortcut's URL and imports the page through [IWebScraperService](#iwebscraperservice). Only public internet hosts are fetched. |

| Member | Description |
|--------|-------------|
| `IReadOnlySet<string> SupportedExtensions { get; }` | Extensions with the leading dot. |
| `bool CanProcess(string filePath)` | Whether the processor reads the file (by extension). |
| `Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default)` | Extracts the text and metadata. |

Plugins can add processors; see [IDocumentProcessorPlugin](#iplugin) and the [Plugin Development Guide](PLUGIN-DEVELOPMENT-GUIDE.md).

---

### IChunkingService

**Namespace**: `AgentX.Core.Documents` | **Implementation**: `ChunkingService`

Splits text into overlapping chunks for embedding: paragraphs first, then sentences, then words, with the last tokens of a chunk repeated at the start of the next. Sizes are estimated tokens (`ITokenCounter`). The indexer passes the Chunk Size and Chunk Overlap settings. For content that [IAdaptiveChunkingService](#iadaptivechunkingservice) classifies as code or a table, its recommended size replaces the requested one (and the overlap is kept below it). When a document has more than one page and its text separates pages with form feed characters, chunks are cut per page and keep their page numbers.

| Member | Description |
|--------|-------------|
| `IReadOnlyList<DocumentChunk> ChunkText(string text, int chunkSize = 512, int chunkOverlap = 50, string? sectionTitle = null, int? pageNumber = null)` | Chunks raw text. Throws `ArgumentOutOfRangeException` unless `chunkSize` is positive and `0 <= chunkOverlap < chunkSize`. |
| `IReadOnlyList<DocumentChunk> ChunkDocument(ProcessedDocument document, int chunkSize = 512, int chunkOverlap = 50)` | Chunks a processed document; empty text gives no chunks. |

---

### IAdaptiveChunkingService

**Namespace**: `AgentX.Core.Documents` | **Implementation**: `AdaptiveChunkingService`

| Member | Description |
|--------|-------------|
| `AdaptiveChunkInfo AnalyzeContent(string text, string? fileName = null)` | Classifies the content (`Prose`, `Code`, `Table`, `List`, `Mixed`), measures line length and structure, and recommends a chunk size within the configured bounds (smaller for code, larger for tables, adjusted for very long or short lines). |

---

## Indexing Services

### IIndexingService

**Namespace**: `AgentX.Core.Services.Indexing` | **Implementation**: `IndexingService`

The background pipeline that makes documents searchable. Startup step 5 calls `InitializeAsync`, which initializes the vector store, returns documents a previous session left in `processing` to the queue, queues every `pending` document and starts one background loop; nothing else starts it. Work arrives from `IDocumentService.DocumentPendingIndexing` (imports and re-indexes), from `IndexDocumentAsync`, and from a sweep every 30 seconds of idle time for `pending` documents written by paths that do not signal (sync, the local API). When the sweep finds nothing, chunks embedded before embedding model versions were recorded are embedded again with the current model.

For each document, one at a time: take the text extracted at import when it still matches the file (same size and write time; at most 8,000,000 characters of such text are held), otherwise extract it again (built-in processors first, then those of active plugins); chunk it with the Chunk Size and Chunk Overlap settings; remove the previous chunks, vectors and keyword rows; embed the chunks in batches of `EmbeddingBatchSize` and store the vectors, stamping each chunk with `IEmbeddingService.ModelVersion`; add the keyword (FTS5) rows; mark the document `completed`; clear the search cache; then auto-tag it ([IAutoTagService](#iautotagservice), when the `ai.auto_tagging` flag is on). A failure marks the document `failed` with the reason. A shutdown in the middle of a document returns it to the queue for the next start.

| Member | Description |
|--------|-------------|
| `Task InitializeAsync(CancellationToken ct = default)` | Starts the pipeline (see above). A vector store that fails to initialize is remembered: every document the loop takes is then marked `failed` with that reason. |
| `Task IndexDocumentAsync(long documentId, CancellationToken ct = default)` | Queues one document (it is not indexed before the call returns). Throws `InvalidOperationException` for an unknown id. |
| `Task ReindexAllAsync(IProgress<(int Processed, int Total)>? progress = null, CancellationToken ct = default)` | Clears the search cache, resets every `completed` or `failed` document to `pending` (removing its chunks, vectors and keyword rows) and queues it. Nothing in the app calls it. |
| `Task<int> GetQueueLengthAsync()` | Documents waiting plus the one being processed. |
| `Task<int> GetProcessedCountAsync()` | Number of completed indexing jobs. |
| `bool IsProcessing { get; }` | Whether a document is being processed. |
| `event EventHandler<IndexingProgressEventArgs>? ProgressChanged` | Raised when the queue changes; see [IndexingProgressEventArgs](#indexing-events). |
| `event EventHandler<long>? DocumentIndexed` | Raised on the indexing thread after a document is indexed (document id). |
| `event EventHandler<DocumentIndexingFailedEventArgs>? DocumentIndexingFailed` | Raised on the indexing thread after a document was saved as `failed` (`DocumentId`, `Error`). The Knowledge Vault updates its rows from these two events. |

---

### IFileWatcherService

**Namespace**: `AgentX.Core.Services.Indexing` | **Implementation**: `FileWatcherService`

Watch folders. New or changed files in an enabled watch folder are debounced for 500 ms and imported through [IDocumentService](#idocumentservice) (into the folder's collection, if it has one); a file whose content is already in the vault is skipped. A file counts when it has an extension, passes the folder's extension filter and a processor (built-in or plugin) reads it. Watch folders are managed under Watch Folders in Settings; the **Auto-index watch folders** switch (`AutoIndexWatchFolders`) turns monitoring on or off when the settings are saved.

| Member | Description |
|--------|-------------|
| `Task InitializeAsync(CancellationToken ct = default)` | Startup step 6. When `AutoIndexWatchFolders` is on, starts watching every enabled folder, then imports files added while the app was closed and re-indexes files changed since their import. Does nothing when the setting is off. |
| `Task StartWatchingAsync(CancellationToken ct = default)` | Starts a `FileSystemWatcher` for every enabled folder. |
| `Task StopWatchingAsync()` | Stops all watchers; the folders stay registered. |
| `Task AddWatchFolderAsync(string path, bool includeSubfolders = true, string? fileTypeFilter = null, long? collectionId = null)` | Registers a folder and starts watching it. `fileTypeFilter` is a comma-separated extension list (null for every supported type). Throws when the folder does not exist or is already registered (paths compared ignoring case). |
| `Task RemoveWatchFolderAsync(long watchFolderId)` | Stops watching and deletes the registration. |
| `Task<IReadOnlyList<WatchFolderEntity>> GetWatchFoldersAsync()` | Registered folders. |
| `bool IsWatching { get; }` | Whether any folder is being watched. |
| `event EventHandler<string>? FileDetected` | Raised with the full path of a detected file. |

---

## Search and RAG Services

### ISemanticSearchService

**Namespace**: `AgentX.Core.Search` | **Implementation**: `SemanticSearchService`

Vector search over indexed chunks, plus the search history of the Semantic Search page. The query is embedded, the vector store is asked for `TopK x RetrievalMultiplier` candidates (at most `RetrievalCap`), and the candidates are joined with their documents and filtered by collection, file type and import date. When a scoped search keeps too few candidates, the candidate pool is widened (up to 8 times `RetrievalCap`). Chunks stamped with a different `IEmbeddingService.ModelVersion` are left out (chunks without a version are kept). Excerpts are up to 200 characters around the best query-word match.

| Member | Description |
|--------|-------------|
| `Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default)` | Results, most similar first; `Score` is the cosine similarity and `MinScore` is applied to it. `CreatedAfter` and `CreatedBefore` filter on the document's import time (UTC). |
| `Task SaveSearchHistoryAsync(string queryText, int resultCount, double? minScore = null, int? maxResults = null, DateTime? dateAfter = null, DateTime? dateBefore = null, string? sortOrder = null, string? searchType = null)` | Records a search with its filter settings; `searchType` is `semantic` (default), `keyword` or `hybrid`, so a saved filter restores the same mode. |
| `Task<IReadOnlyList<SearchHistoryEntry>> GetSearchHistoryAsync(int limit = 20)` | Recent searches. |
| `Task ClearSearchHistoryAsync()` | Deletes the history. |
| `Task SaveSearchFilterAsync(long historyId)` / `Task UnsaveSearchFilterAsync(long historyId)` | Marks or unmarks a history entry as a saved filter. |
| `Task<IReadOnlyList<SearchHistoryEntry>> GetSavedFiltersAsync()` | Saved filters. |

---

### IKeywordSearchService

**Namespace**: `AgentX.Core.Search` | **Implementation**: `KeywordSearchService`

Full-text search with SQLite FTS5 (Porter stemmer, Unicode61 tokenizer) and BM25 ranking. Raw SQL on the shared connection runs under the database gate.

| Member | Description |
|--------|-------------|
| `Task InitializeFtsAsync(CancellationToken ct = default)` | Creates the FTS5 table if needed. Startup step 1b, after the migration. |
| `Task IndexDocumentChunksAsync(long documentId, CancellationToken ct = default)` | Adds a document's chunks to the index (called by the indexer). |
| `Task RemoveDocumentFromFtsAsync(long documentId, CancellationToken ct = default)` | Removes a document's rows (on delete and before re-indexing). |
| `Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default)` | Stop words are ignored and any remaining term may match; collection, file type and date filters run inside the query. Scores are 0 to 1 relative to the best hit of the search, and `MinScore` applies on that scale. |

---

### IHybridSearchOrchestrator

**Namespace**: `AgentX.Core.Search` | **Implementation**: `HybridSearchOrchestrator`

Runs a search in the query's `SearchMode` and caches the results ([ISearchCacheService](#isearchcacheservice)). Used by the Semantic Search page and the RAG pipeline.

| Member | Description |
|--------|-------------|
| `Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default)` | `Semantic` and `Keyword` delegate to one backend. `Hybrid` runs both in parallel with `TopK x RetrievalMultiplier` candidates each (each backend applies `MinScore` on its own scale) and merges them with Reciprocal Rank Fusion (k = 60); the fused score is normalized to 0 to 1. When one backend fails, the other's results are returned. |

---

### ISearchCacheService

**Namespace**: `AgentX.Core.Services.Search` | **Implementation**: `SearchCacheService`

An in-memory LRU cache of search results: 100 entries, 5 minutes each. Turned off by the `search.caching` feature flag. The indexer clears it after every indexed document; deleting or re-indexing a document drops the entries that reference it.

| Member | Description |
|--------|-------------|
| `IReadOnlyList<SearchResult>? TryGetCached(SearchQuery query)` | Cached results, or null when missing or expired. |
| `void Cache(SearchQuery query, IReadOnlyList<SearchResult> results)` | Stores results, evicting the least recently used entry when full. |
| `void InvalidateAll()` | Clears the cache. |
| `void InvalidateForDocument(long documentId)` | Drops the entries whose results contain the document. |
| `CacheStatistics GetStatistics()` | Entry, hit and miss counts and the hit rate. |

---

### IRagPipeline

**Namespace**: `AgentX.Core.Search` | **Implementation**: `RagPipeline`

Answers a question from the Knowledge Vault (Ask Your Files, and `DocumentLookup` workflow steps). The steps, each optional service skipped when it fails:

1. `IMultiQueryGenerator` adds 3 alternative phrasings;
2. `IHydeService` adds a hypothetical answer as another query when `EnableHyde` is on and the question has at least `HydeMinQueryLength` (80) characters;
3. every query is searched through [IHybridSearchOrchestrator](#ihybridsearchorchestrator) in `DefaultSearchMode` (Hybrid) with the Top-K Results setting as Top-K (capped at `MaxTopK`) and `DefaultMinScore`; results are merged by chunk;
4. with no results, the answer is a fixed "I couldn't find any relevant information in your documents..." message and no model is called;
5. personal data (emails, phone numbers, SSNs, credit cards, API keys, IP addresses) is redacted from the chunks when `EnablePiiRedaction` is on, before any model sees them;
6. `IRagReranker` removes near-duplicates and balances documents; `ILlmReranker` reorders with the model when `EnableLlmReranking` is on and more than 2 chunks remain;
7. `IParentDocumentRetriever` adds neighbouring chunks (redacted again); `IContextualCompressor` keeps the relevant sentences;
8. with Research Mode requested and web search configured, web results are added as `WebCitations`;
9. the answer is streamed from the active provider (temperature 0.3, up to 2,048 tokens) with numbered context sections, `[N]` citations are resolved by `ICitationService`, and `IRagEvaluator` scores the answer in the background (sampled by `EvalSampleRate`).

| Member | Description |
|--------|-------------|
| `Task<RagResponse> AskAsync(string question, long? collectionId = null, Action<string>? onToken = null, bool enableResearchMode = false, CancellationToken ct = default)` | Runs the pipeline, calling `onToken` for each streamed token, and returns the answer with citations and timings ([RagResponse](#search-models)). `collectionId` limits the search to one collection. No caller in the app passes `enableResearchMode: true`; chat's Research Mode adds web results through [IChatService](#ichatservice) instead. |
| `Task<long> GetIndexedChunkCountAsync(CancellationToken ct = default)` | Number of indexed chunks (shown on the Dashboard). |

---

### RAG Pipeline Stages

**Namespace**: `AgentX.Core.Search` | All registered as singletons and used only by [IRagPipeline](#iragpipeline).

| Service | Implementation | Member and behavior |
|---------|----------------|---------------------|
| `IMultiQueryGenerator` | `MultiQueryGenerator` | `Task<IReadOnlyList<string>> GenerateQueryVariationsAsync(string query, int count = 3, CancellationToken ct = default)`: the original query first, then alternative phrasings from the model. |
| `IHydeService` | `HydeService` | `Task<string> GenerateHypotheticalDocumentAsync(string query, CancellationToken ct = default)`: a 1-2 paragraph hypothetical answer, used as an extra search query. |
| `IRagReranker` | `RagReranker` | `List<RagContextChunk> Rerank(List<RagContextChunk> chunks, string query, int maxChunks = 8)`: drops chunks more than 85% similar (word sets) to one already kept, boosts chunks containing query terms (at most 1.5 times), demotes by 20% the extra chunks of a document that contributes more than 60%, and returns the top `maxChunks`. No model call. |
| `ILlmReranker` | `LlmReranker` | `Task<List<RagContextChunk>> RerankAsync(List<RagContextChunk> chunks, string query, int maxChunks = 8, CancellationToken ct = default)`: the model scores all chunks in one prompt. |
| `IParentDocumentRetriever` | `ParentDocumentRetriever` | `Task<List<RagContextChunk>> RetrieveParentChunksAsync(List<RagContextChunk> childChunks, CancellationToken ct = default)`: expands each chunk with the chunks before and after it in the same document. |
| `IContextualCompressor` | `ContextualCompressor` | `Task<List<RagContextChunk>> CompressAsync(List<RagContextChunk> chunks, string query, CancellationToken ct = default)`: the model keeps only the parts of each chunk that answer the question (`CompressionConcurrency` chunks at a time). |
| `ICitationService` | `CitationService` | `List<Citation> ExtractCitations(string responseText, IReadOnlyList<RagContextChunk> contextChunks)`: resolves `[N]` references (1-based) to the context chunks. |
| `IRagEvaluator` | `RagEvaluator` | `Task<RagEvalMetrics> EvaluateAsync(string question, string answer, IReadOnlyList<RagContextChunk> contextChunks, CancellationToken ct = default)`: the model rates context relevance, faithfulness and answer relevance (0 to 1; overall = 0.3, 0.4, 0.3 weights). When the call or its JSON fails, the result is 0.5 each with `IsDefault = true` and a `DefaultReason`. |

---

### IWebSearchService

**Namespace**: `AgentX.Core.Services.Search` | **Implementation**: `SettingsAwareWebSearchService` (uses `BraveSearchService`, `SerperSearchService` or `SearXngSearchService` with the shared `WebSearchCache`)

Web search for Research Mode. Every call reads the current settings (`WebSearchConfiguration.FromSettings`), so a new provider, key, SearXNG URL, result limit or cache time applies to the next search without a restart. The one credential field, `WebSearchApiKey`, belongs to the selected provider only: the API key for Brave or Serper, or the instance URL (http or https) for SearXNG; there is no fallback to another provider. Results are cached per query and provider for `SearchCacheTtlMinutes` (1 to 1,440, default 60); failed searches are not cached.

| Member | Description |
|--------|-------------|
| `Task<WebSearchResponse> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)` | Searches with the selected provider, returning at most `min(maxResults, MaxSearchResults)` results (the setting is capped at 20). An unconfigured provider returns an empty response. |
| `bool IsConfigured { get; }` | Whether the selected provider has its key or a valid URL. |
| `WebSearchProvider ActiveProvider { get; }` | `Brave`, `Serper` or `SearXng`. |

---

### IRagConfiguration and IRagPromptCatalog

**Namespace**: `AgentX.Core.Configuration` | **Implementations**: `RagConfiguration`, `RagPromptCatalog`

Retrieval tuning and the RAG prompts, read from the app folder's `appsettings.json` (section `Rag`) and `RagPrompts.json` through `IOptionsMonitor`, so edits apply on the next use without a restart.

`IRagConfiguration` exposes read-only values (defaults in parentheses): retrieval `DefaultTopK` (8), `DefaultMinScore` (0.25), `MaxTopK` (50), `RetrievalMultiplier` (3), `RetrievalCap` (500); chunking `DefaultChunkSize` (512), `DefaultChunkOverlap` (50), `MaxChunkSize` (768), `MinChunkSize` (128); embeddings `DefaultEmbeddingModel` (`all-minilm`), `DefaultEmbeddingDimensions` (384), `EmbeddingCacheExpirationMinutes` (10,080), `EmbeddingBatchSize` (32); context scoring `SemanticWeight` (0.68), `LexicalWeight` (0.22), `RecencyWeight` (0.10), `MinRecallBudgetTokens` (48); memory `MemoryDecayRate` (0.01), `MemoryDaysBeforeFullDecay` (90), `AssociativeLinkThreshold` (0.85), `MaxMemoriesPerQuery` (10); vector store `VectorStoreFallbackThreshold` (10,000), `StaleRebuildFraction` (0.05), `HnswM` (16), `HnswEfConstruction` (200); pipeline `EnableLlmReranking` (true), `RerankerMaxTokens` (800), `HydeMaxTokens` (256), `CompressionConcurrency` (4), `EvalSampleRate` (1.0), `EvalContextCharLimit` (800), `EnableHyde` (true), `HydeMinQueryLength` (80), `DefaultSearchMode` (`Hybrid`), `EnablePiiRedaction` (true), `PiiRedactionMask` (`***`), `EnableResearchMode` (false), `ResearchMaxWebResults` (10); and `void Validate()`.

`IRagPromptCatalog` exposes `RagSystemPrefix`, `EvalSystem`, `RerankerSystem`, `CompressorSystem`, `MultiQuerySystem` and `HydeSystem`. A missing or empty entry in `RagPrompts.json` falls back to the built-in default (`RagPromptDefaults`).

---

### IRagMetrics and IPiiDetector

**Namespace**: `AgentX.Core.Observability` | **Implementations**: `RagMetrics`, `PiiDetector`

| Member | Description |
|--------|-------------|
| `IRagMetrics.RecordSearch(SearchMetrics metrics)`, `RecordEvaluation(EvaluationMetrics metrics)`, `RecordTokensProcessed(int tokenCount)` | Record search latency and result counts, evaluation scores (not placeholder defaults) and token counts. The search orchestrator and the RAG pipeline record; nothing in the app reads the snapshot yet. |
| `IRagMetrics.GetSnapshot()`, `Reset()`, `RegisterEmbeddingCacheProvider(Func<EmbeddingCacheStats?> provider)` | Aggregate snapshot (including embedding cache statistics pulled from `CachedEmbeddingService`), reset, and the cache statistics source. |
| `IPiiDetector.ContainsPii(string text)`, `DetectPii(string text)`, `RedactPii(string text, string mask = "***")` | Find or mask emails, phone numbers, SSNs, credit card numbers, API keys (including current OpenAI, Anthropic, GitHub and AWS key formats) and IP addresses. |
| `IPiiDetector.AddCustomPattern(string regex, PiiType type, string? name = null)`, `GetStatistics(string text)` | Add a pattern; count matches per type. |

---

## Vector Database

### IVectorStore

**Namespace**: `AgentX.Core.Data.VectorDb` | **Implementations**: `HnswVectorStore` or `SqliteVecStore`, chosen once by `VectorStoreFactory` from the `EnableHnswIndex` setting (default on)

Stores one embedding per chunk in the `vec_embeddings` table of `agentx.db` in the storage folder (`StoragePath`, the app data folder by default, so the app database itself), the source of truth, opened through `IEncryptedConnectionFactory` so an encrypted database works. `SqliteVecStore` scans every vector and computes cosine similarity in C#. `HnswVectorStore` adds an in-memory HNSW index (settings `HnswM`, default 16, and `HnswEfConstruction`, default 200) and falls back to a linear scan below `HnswFallbackThreshold` (10,000) embeddings. `HnswEfSearch` (default 50) is a minimum search breadth: a query already searches at least the larger of `HnswEfConstruction` and twice the candidates it asks for, so only a larger value widens the search (up to 10,000) and the default changes nothing. Its index files are written next to the database only while the database is not encrypted, otherwise the index is rebuilt from the database at each start. [IIndexingService](#iindexingservice) initializes it.

| Member | Description |
|--------|-------------|
| `Task InitializeAsync(CancellationToken ct = default)` | Creates the table and loads or builds the index. |
| `Task<long> InsertEmbeddingAsync(long chunkId, float[] embedding, CancellationToken ct = default)` | Stores a chunk's vector; returns its row id. |
| `Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryEmbedding, int topK = 5, double minSimilarity = 0.3, CancellationToken ct = default)` | Nearest neighbours by cosine similarity, most similar first. |
| `Task DeleteEmbeddingAsync(long chunkId, CancellationToken ct = default)` | Removes one vector. |
| `Task DeleteEmbeddingsForDocumentAsync(long documentId, IReadOnlyList<long> chunkIds, CancellationToken ct = default)` | Removes the vectors of the given chunks. |
| `Task<long> GetEmbeddingCountAsync(CancellationToken ct = default)` | Number of stored vectors. |
| `Task OptimizeAsync(CancellationToken ct = default)` | Rebuilds or compacts the index where the implementation supports it. |
| `Task SuspendAsync(CancellationToken ct = default)` | Waits for running operations, then closes the store's connection so the database file can be replaced (restore) or re-encrypted; later operations wait. Suspensions nest. |
| `Task ResumeAsync(bool reloadFromDatabase, CancellationToken ct = default)` | Ends one suspension; the last one reopens the connection with the current key, and `reloadFromDatabase` drops the index derived from the old file and loads again (after a restore). |

---

## Collections, Tags and Annotations

### ICollectionService

**Namespace**: `AgentX.Core.Services.Collections` | **Implementation**: `CollectionService`

Collections and their documents (Collection Manager, Knowledge Vault, imports). Each collection keeps a `DocumentCount` that the service keeps in step with its links. The service allows any depth of nesting and refuses cycles; the Collection Manager itself offers one level ("Move into...").

| Member | Description |
|--------|-------------|
| `Task<CollectionEntity> CreateCollectionAsync(string name, string? description = null, long? parentId = null)` | Creates a collection, optionally inside another. |
| `Task<IReadOnlyList<CollectionEntity>> GetAllCollectionsAsync()` | All collections by sort order, then name, with their children. |
| `Task<IReadOnlyList<CollectionEntity>> GetRootCollectionsAsync()` | Top-level collections with their children. |
| `Task<IReadOnlyList<CollectionEntity>> GetChildCollectionsAsync(long parentId)` | Direct children. |
| `Task<CollectionEntity?> GetCollectionAsync(long collectionId)` | One collection with its document links and children, or null. |
| `Task UpdateCollectionAsync(long collectionId, string name, string? description = null)` | Renames and describes a collection. |
| `Task DeleteCollectionAsync(long collectionId, bool deleteDocuments = false)` | Deletes a collection; its sub-collections move up to its parent. With `deleteDocuments` its documents are deleted through `IDocumentService.DeleteDocumentAsync`; otherwise they stay in the vault. |
| `Task<bool> AddDocumentToCollectionAsync(long documentId, long collectionId)` | Adds a document; false when it was already in the collection. Throws `InvalidOperationException` when the document or collection does not exist. |
| `Task RemoveDocumentFromCollectionAsync(long documentId, long collectionId)` | Removes a document from a collection (the document stays in the vault). |
| `Task MoveCollectionAsync(long collectionId, long? newParentId)` | Moves a collection under another, or to the top level with null. Throws when a collection would move into itself or one of its descendants. |
| `Task<int> GetCollectionCountAsync()` | Number of collections. |
| `Task<IReadOnlyList<DocumentEntity>> GetDocumentsInCollectionAsync(long collectionId)` | The collection's documents by file name, untracked. |

---

### IAutoTagService

**Namespace**: `AgentX.Core.Services.Tagging` | **Implementation**: `AutoTagService`

Document tags. The indexer calls `ApplyAutoTagsAsync` after each indexed document; the `ai.auto_tagging` feature flag (on by default) turns automatic tagging off. Generated names are normalized (non-Latin names are kept) and each normalized tag is applied once.

| Member | Description |
|--------|-------------|
| `Task<IReadOnlyList<(string TagName, double Confidence)>> GenerateTagsAsync(string documentContent, int maxTags = 5, CancellationToken ct = default)` | Asks the active model for tags (through `IAiService.GenerateTagsAsync`, then a plain chat prompt as fallback) for the start of the text. Empty when the flag is off or the text is empty. |
| `Task ApplyAutoTagsAsync(long documentId, CancellationToken ct = default)` | Generates up to 5 tags from the document's text (its chunks, else the file) and saves them: existing tags are matched by name ignoring case, new ones are created as auto-generated, and links the document already has are skipped. |
| `Task<IReadOnlyList<TagEntity>> GetAllTagsAsync()` | All tags by name. |
| `Task<TagEntity> CreateTagAsync(string name, string? colorHex = null)` | Creates a tag; the name must be unique. |
| `Task DeleteTagAsync(long tagId)` | Deletes a tag and its document links. |
| `Task AssignTagAsync(long documentId, long tagId)` | Tags a document manually (confidence 1.0). |
| `Task RemoveTagAsync(long documentId, long tagId)` | Removes a tag from a document. |
| `Task<IReadOnlyList<TagEntity>> GetTagsForDocumentAsync(long documentId)` | A document's tags. |
| `Task<IReadOnlyDictionary<long, IReadOnlyList<TagEntity>>> GetTagsForDocumentsAsync(IReadOnlyList<long> documentIds)` | Tags of several documents in one query. |

---

### IAnnotationService

**Namespace**: `AgentX.Core.Services.Annotations` | **Implementation**: `AnnotationService`

Highlights and notes on documents. They are created from the Document Preview in the Knowledge Vault (on one passage, that is one indexed chunk, at a time) and managed on the Annotations page. Each new annotation is also handed to [ITemporalIdentityService](#itemporalidentityservice) as an insight. Deleting a document deletes its annotations.

| Member | Description |
|--------|-------------|
| `Task<AnnotationEntity> CreateAnnotationAsync(long documentId, long? chunkId, int startOffset, int endOffset, string highlightedText, string color, string? noteText = null)` | Creates a highlight. `color` is `yellow`, `green`, `blue`, `red` or `purple`; a blank note is stored as no note. |
| `Task<AnnotationPassage?> GetPassageAsync(long documentId, int position, CancellationToken ct = default)` | The chunk at `position` in reading order (clamped to the first or last) as `AnnotationPassage(ChunkId, Position, Count, PageNumber, Text)`; null when the document has no indexed text. Offsets of a highlight made on it point into `Text`. |
| `Task<AnnotationEntity?> GetAnnotationAsync(long annotationId)` | One annotation with its document, or null. |
| `Task<IReadOnlyList<AnnotationEntity>> GetAnnotationsForDocumentAsync(long documentId)` | A document's annotations by start offset. |
| `Task<IReadOnlyList<AnnotationEntity>> GetAnnotationsByColorAsync(string color)` | Annotations of one color, newest first. |
| `Task<IReadOnlyList<AnnotationEntity>> SearchAnnotationsAsync(string query)` | Annotations whose highlighted text or note contains the query (case-insensitive `LIKE`). |
| `Task<IReadOnlyList<AnnotationEntity>> GetAllAnnotationsAsync(int skip = 0, int take = 50)` | A page of annotations, newest first. |
| `Task<int> GetAnnotationCountAsync(long? documentId = null)` | Count, for one document or all. |
| `Task<AnnotationEntity> UpdateAnnotationAsync(long annotationId, string? noteText = null, string? color = null)` | Changes the note and/or color; null arguments leave the field as it is. |
| `Task DeleteAnnotationAsync(long annotationId)` | Deletes one annotation (no error when missing). |
| `Task DeleteAnnotationsForDocumentAsync(long documentId)` | Deletes a document's annotations. |
| `Task<IReadOnlyList<AnnotationEntity>> GetRecentAnnotationsAsync(int count = 20)` | Newest annotations. |
| `Task<Dictionary<string, int>> GetColorDistributionAsync()` | Count per color in use. |
| `Task<string> ExportAnnotationsAsMarkdownAsync(long? documentId = null)` | Markdown of one document's annotations, or of all grouped by document (timestamps in the invariant calendar). |

---

## Intelligence Services

### ISummaryService

**Namespace**: `AgentX.Core.Services.Intelligence` | **Implementation**: `SummaryService`

Document summaries, key points and translation with the active model (temperature 0.3, up to 2,048 tokens per request). Used by Quick Actions and the Smart Inbox. For a document, the indexed chunks are taken in order up to 8,000 characters and passed to [IHierarchicalSummaryService](#ihierarchicalsummaryservice).

| Member | Description |
|--------|-------------|
| `Task<string> SummarizeDocumentAsync(long documentId, CancellationToken ct = default)` | Summary of an indexed document, also saved as the document's `Summary` (shown in the Knowledge Vault preview and sent by its Workflow action); a failed save is logged and the summary is still returned. Throws `InvalidOperationException` when the document does not exist or has no chunks. |
| `Task<IReadOnlyList<string>> ExtractKeyPointsAsync(long documentId, CancellationToken ct = default)` | One-sentence key points (numbers in the text are kept). Same exceptions. |
| `Task<string> TranslateTextAsync(string text, string targetLanguage, CancellationToken ct = default)` | Translates into the named language. Text longer than 4,000 characters is translated in parts split at paragraph, line or sentence breaks, and the parts are joined in order, so nothing is cut off. Throws `ArgumentException` for empty text or language. |

---

### IHierarchicalSummaryService

**Namespace**: `AgentX.Core.Services.Intelligence` | **Implementation**: `HierarchicalSummaryService`

Summarizes a document from its sections: each of the first 6 non-empty sections is summarized, the section summaries are combined into one document summary (a single section's summary is used as it is), and key points are extracted from the section summaries.

| Member | Description |
|--------|-------------|
| `Task<HierarchicalSummaryResult> BuildSummaryAsync(string documentTitle, IReadOnlyList<string> sections, CancellationToken ct = default)` | Section summaries, document summary, key points, and how many sections were included out of the total. |
| `Task<string> SummarizeAsync(string documentTitle, IReadOnlyList<string> sections, CancellationToken ct = default)` | Just the document summary. |
| `Task<IReadOnlyList<string>> ExtractKeyPointsAsync(string documentTitle, IReadOnlyList<string> sections, CancellationToken ct = default)` | Just the key points. |

---

### IDuplicateDetectionService

**Namespace**: `AgentX.Core.Services.Intelligence` | **Implementation**: `DuplicateDetectionService` (uses `IDuplicateEvidenceService`)

Finds duplicate documents for Quick Actions. The `intelligence.duplicate_detection` feature flag (on by default) turns both scans off.

| Member | Description |
|--------|-------------|
| `Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(CancellationToken ct = default)` | Groups documents with the same SHA-256 content hash. No model call. |
| `Task<IReadOnlyList<DuplicateGroup>> FindNearDuplicatesAsync(float similarityThreshold = 0.9f, CancellationToken ct = default)` | Groups documents whose chunk embeddings are at least `similarityThreshold` similar (cosine), using the vector store; the scan covers at most 500 documents. |

`IDuplicateEvidenceService.BuildEvidence(IReadOnlyList<VectorSearchResult> searchResults, IReadOnlyDictionary<long, long> chunkToDocument)` aggregates chunk-level vector matches into per-document evidence for the near-duplicate scan.

---

### IOrganizationSuggestionService

**Namespace**: `AgentX.Core.Services.Intelligence` | **Implementation**: `OrganizationSuggestionService`

| Member | Description |
|--------|-------------|
| `Task<IReadOnlyList<OrganizationSuggestion>> SuggestOrganizationAsync(int maxDocuments = 20, CancellationToken ct = default)` | For the newest documents that belong to no collection (up to `maxDocuments`), asks the active model for a collection (an existing one or a new name), tags, a reason and a confidence, as JSON. Empty when every document is in a collection. Used by Quick Actions. |

---

### IKnowledgeGraphService

**Namespace**: `AgentX.Core.Services.Intelligence` | **Implementation**: `KnowledgeGraphService`

| Member | Description |
|--------|-------------|
| `Task<KnowledgeGraphData> BuildGraphAsync(CancellationToken ct = default)` | Loads all documents, collections and tags, makes a node for each, adds "in collection" and "tagged" edges plus document-to-document edges weighted by the collections and tags two documents share, and runs 100 iterations of a force-directed layout. Honors cancellation. Used by the Knowledge Graph page; see [KnowledgeGraphData](#intelligence-models). |

---

### IDigestService and IDigestInsightService

**Namespace**: `AgentX.Core.Services.Intelligence` | **Implementations**: `DigestService`, `DigestInsightService`

Weekly Digest reports, computed from the database (no model call) and saved as `DigestReportEntity` rows: new documents, new conversations, searches, tokens used (from message token counts), storage added, the three most active conversations, and period-over-period trends of searches, collections and file types from `IDigestInsightService`.

| Member | Description |
|--------|-------------|
| `Task<DigestReportEntity> GenerateDigestAsync(DateTime? periodStart = null, DateTime? periodEnd = null, CancellationToken ct = default)` | Builds and saves a report; the default period is the last 7 days. |
| `Task<IReadOnlyList<DigestReportEntity>> GetReportHistoryAsync(int limit = 10, CancellationToken ct = default)` | Newest reports first. |
| `Task<DigestReportEntity?> GetLatestReportAsync(CancellationToken ct = default)` | The newest report, or null. |
| `Task MarkAsReadAsync(long reportId, CancellationToken ct = default)` | Marks a report read. |
| `Task<bool> HasUnreadReportsAsync(CancellationToken ct = default)` | Whether any report is unread. |
| `IDigestInsightService.BuildSearchTrendsAsync(...)`, `BuildCollectionTrendsAsync(...)`, `BuildFileTypeTrendsAsync(...)` | Each takes `(DateTime periodStart, DateTime periodEnd, CancellationToken ct = default)` and compares the period with the one before it. |

---

### IComparisonService and IDocumentSynthesisService

**Namespace**: `AgentX.Core.Services.Intelligence` | **Implementations**: `ComparisonService`, `DocumentSynthesisService`

Compare Documents. For each call: duplicate ids are removed and each document gets a label unique within the comparison; one vault-wide semantic search (on `ComparisonOptions.FocusQuery`, or a broad overview query) supplies each document's best chunks, and a document the search does not surface is filled from its own stored chunks spread across it; `IDocumentSynthesisService.SynthesizeComparisonAsync` builds a prompt asking for a JSON report and runs it on the active model; the JSON is parsed into a `ComparisonReport`, with a plain-text fallback when parsing fails.

| Member | Description |
|--------|-------------|
| `Task<ComparisonReport> CompareDocumentsAsync(IReadOnlyList<long> documentIds, ComparisonOptions? options = null, IProgress<string>? progress = null, CancellationToken ct = default)` | Similarities, differences, contradictions, unique points per document, a summary, tokens and duration. `ComparisonOptions`: `MaxChunksPerDoc` (5), `FocusQuery`, `DetailLevel` (`detailed`). Throws `ArgumentException` for fewer than two ids and `InvalidOperationException` when fewer than two documents can be used. |
| `Task<string> ExportComparisonAsMarkdownAsync(ComparisonReport report)` | The report as Markdown (the page saves it to a file). |

---

### IConversationThemeClusterService and IConversationThemeTrendService

**Namespace**: `AgentX.Core.Services.Intelligence` | **Implementations**: `ConversationThemeClusterService`, `ConversationThemeTrendService`

Durable conversation themes for the Analytics page. A theme cluster is assigned from each conversation's latest summary snapshot with deterministic heuristics (no separate model call); the trend service keeps a trailing window of daily activity rows per cluster.

| Member | Description |
|--------|-------------|
| `Task<bool> MaterializeConversationThemeAsync(long conversationId, bool forceRefresh = false, CancellationToken ct = default)` | Assigns or refreshes one conversation's theme (called after a new summary snapshot). |
| `Task<int> RefreshStaleClustersAsync(int maxConversations = 4, CancellationToken ct = default)` | Refreshes a few conversations whose theme is stale. |
| `Task<int> RefreshClusterTrendWindowAsync(long clusterId, int days = 30, CancellationToken ct = default)` | Rewrites a cluster's daily rows; returns how many were written. |
| `Task<int> RefreshRecentClusterTrendsAsync(int maxClusters = 4, int days = 30, CancellationToken ct = default)` | Refreshes a few recently touched clusters. |

---

### IAnalyticsService

**Namespace**: `AgentX.Core.Services.Analytics` | **Implementation**: `AnalyticsService`

Read-only aggregates for the Analytics page, the annunciator lamps and the Operations overview. Daily series include days without activity as zero.

| Member | Description |
|--------|-------------|
| `Task<AnalyticsSummary> GetSummaryAsync(CancellationToken ct = default)` | Totals across all feature areas. |
| `Task<IReadOnlyList<DailyMetric>> GetDailyConversationMetricsAsync(int days = 30, CancellationToken ct = default)` | Conversations per day. |
| `Task<IReadOnlyList<DailyMetric>> GetDailyDocumentMetricsAsync(int days = 30, CancellationToken ct = default)` | Document imports per day. |
| `Task<IReadOnlyList<DailyMetric>> GetDailySearchMetricsAsync(int days = 30, CancellationToken ct = default)` | Searches per day. |
| `Task<IReadOnlyList<ModelUsageMetric>> GetModelUsageAsync(CancellationToken ct = default)` | Usage per model, most conversations first. |
| `Task<IReadOnlyList<FileTypeMetric>> GetFileTypeDistributionAsync(CancellationToken ct = default)` | Documents per file type. |
| `Task<PerformanceMetrics> GetPerformanceMetricsAsync(CancellationToken ct = default)` | Generation-time statistics of assistant messages (zeros when none are timed). |
| `Task<WorkflowIntelligenceOverview> GetWorkflowIntelligenceOverviewAsync(int maxRecentRuns = 6, int maxTopWorkflows = 5, int recentActivityDays = 30, CancellationToken ct = default)` | Workflow run coverage, reliability and recent runs. |
| `Task<IReadOnlyList<DailyMetric>> GetDailyWorkflowRunMetricsAsync(int days = 30, CancellationToken ct = default)` | Workflow runs per day. |
| `Task<ConversationIntelligenceOverview> GetConversationIntelligenceAsync(int maxRecent = 6, CancellationToken ct = default)` | Summary coverage and recent summaries. |
| `Task<ConversationRecallOverview> GetConversationRecallOverviewAsync(CancellationToken ct = default)` | Message-embedding coverage for recall. |
| `Task<ConversationThemeOverview> GetConversationThemeOverviewAsync(int maxClusters = 6, CancellationToken ct = default)` | Theme cluster metrics and top themes. |
| `Task<ConversationThemeTrendOverview> GetConversationThemeTrendOverviewAsync(int maxThemes = 5, int days = 30, CancellationToken ct = default)` | Daily theme trends. |

---

## Temporal Identity Services

Temporal Identity records what the user has said, saved and spent time on, so the Past Self page can answer "what did I think about X then?" and draft text in the user's voice. The analysis in `TemporalIdentityService` is rule based (phrase patterns and word lists); only [IVoiceDraftService](#ivoicedraftservice) calls an AI model.

### ITemporalIdentityService

**Namespace**: `AgentX.Core.Services.TemporalIdentity` | **Implementation**: `TemporalIdentityService`

Who calls it:

- The chat, after each reply to a new prompt (not after a regeneration), runs `ProcessMessageAsync`, `LearnFromMessageAsync` and `DetectInsightsAsync` off the UI thread; a failing step is logged and the next one still runs.
- `AnnotationService` hands each new annotation to `ProcessAnnotationAsync`.
- [EngagementTracker](#engagementtracker) reports reading time through `RecordEngagementAsync`.
- The Past Self page, the Dashboard's "Your Belief Evolution" panel and [IVoiceDraftService](#ivoicedraftservice) read the results.

Reads do not track entities, updates run as single `ExecuteUpdate` statements, and each new row is saved and detached under the database gate, so the service leaves nothing in the shared change tracker.

| Member | Description |
|--------|-------------|
| `Task ProcessMessageAsync(long messageId, CancellationToken ct = default)` | Belief tracking for a user message (other roles are ignored). A topic is the text after "that" in a sentence of 21 to 99 characters containing "I think", "I believe" or "I feel" (up to 5 per message). A new topic creates a belief with its stance (the sentence), a sentiment from a fixed word list and a confidence. A known topic gets the new stance, a moving-average sentiment and 0.05 more confidence; when the sentiment moves by more than 0.5 the belief is marked evolved and a `BeliefConflictEntity` records the previous and new stance. |
| `Task ProcessAnnotationAsync(long annotationId, CancellationToken ct = default)` | Saves the annotation as an insight (source `DocumentAnnotation`, significance 0.7): its note, or the highlighted text when there is no note; the topic is the first 50 characters of that text. |
| `Task<PastSelfResponse?> GetPastSelfAsync(string topic, DateTime? at = null, CancellationToken ct = default)` | The stance held on a topic at `at` (the earliest recorded stance when null), its confidence, the evidence excerpt recorded with the first mention, and up to 3 conversation titles and 3 document file names that contain the topic and date from within 30 days of that time. When the stance changed after that time, `HasEvolved` is true and `CurrentStance` and `StanceChangedAt` give today's stance and when it changed. Null when the topic is unknown or was first recorded after `at`. The topic matches ignoring surrounding spaces and the case of ASCII letters. |
| `Task<TemporalBeliefEntity?> GetBeliefEvolutionAsync(string topic, CancellationToken ct = default)` | The belief row (current stance, previous stance with its sentiment, when it changed), matched like `GetPastSelfAsync`; null when unknown. |
| `Task<List<BeliefConflictEntity>> GetBeliefConflictsAsync(CancellationToken ct = default)` | Conflicts not yet acknowledged, largest sentiment shift first, with their belief. |
| `Task<bool> AcknowledgeConflictAsync(long conflictId, CancellationToken ct = default)` | Marks a conflict acknowledged in the database, so it stays dismissed after a restart; the first acknowledgement's time is kept. False when no such conflict exists. |
| `Task CaptureInsightAsync(string topic, string insight, InsightSource source, long? sourceId, double? significance = null, CancellationToken ct = default)` | Saves an insight. The significance defaults to 0.7. |
| `Task DetectInsightsAsync(long conversationId, CancellationToken ct = default)` | Looks through the conversation's assistant messages and saves, once per message, each one that contains a breakthrough word (breakthrough, key insight, important, realize, discover, aha, eureka) or an excitement marker (an exclamation mark, amazing, incredible, fascinating, interesting). Significance is 0.6, plus 0.2 for a breakthrough word and 0.1 for an excitement marker; the text is cut at 500 characters. |
| `Task<List<ResurfacedInsight>> GetRelevantInsightsAsync(string[] currentTopics, CancellationToken ct = default)` | Up to 5 insights with significance above 0.5 whose topics, or whose text, match one of `currentTopics` (ignoring case), most significant first. |
| `Task<List<InsightMomentEntity>> GetTopInsightsAsync(int count = 10, CancellationToken ct = default)` | The most significant insights, newer first among equals. No caller in the app. |
| `Task RecordEngagementAsync(EngagementTargetType targetType, long targetId, int secondsSpent, CancellationToken ct = default)` | Adds time to the target's single engagement row (created on the first call with depth `Read`) and counts a revisit on each later call. Depth becomes `Engaged` above 60 seconds in total and `Deep` above 300 seconds with more than 2 revisits. Concurrent calls are all counted. |
| `Task<List<EngagementMetricsEntity>> GetMostEngagedContentAsync(DateTime start, DateTime end, int count = 10, CancellationToken ct = default)` | Rows last engaged between `start` and `end`, ordered by time spent weighted by depth. No caller in the app. |
| `Task<List<EngagementMetricsEntity>> GetEngagedContentForTopicAsync(string topic, CancellationToken ct = default)` | Rows whose recorded topics contain `topic`. Engagement rows are not given topics yet (`TopicsJson` stays `[]`), so this returns an empty list. No caller in the app. |
| `Task LearnFromMessageAsync(long messageId, CancellationToken ct = default)` | Voice learning for a user message that has words. The first such message creates the single voice profile; later ones update it: the average words per sentence is a plain mean over the first 10 messages and then a moving average in which each message counts for 1/10, and the formality score (0 casual to 1 formal: 0.8 when the text contains "therefore" or "however", otherwise 0.5 minus 0.05 per apostrophe) works the same way over 20 messages. Characteristic phrases are not learned yet. |
| `Task<VoiceProfileEntity?> GetVoiceProfileAsync(CancellationToken ct = default)` | The voice profile, or null before the first measured message. |
| `Task<List<ProblemSolvingPattern>> FindSimilarProblemsAsync(string currentProblem, CancellationToken ct = default)` | Up to 5 newest conversations whose title contains one of the problem's words (longer than 4 characters, lower-cased), with a problem type guessed from the title and a fixed success rate (0.8 when the conversation used more than 1,000 tokens, else 0.5). No caller in the app. |
| `Task<double> GetExpertiseLevelAsync(string topic, CancellationToken ct = default)` | 0.1 per conversation whose title contains the topic plus the hours of engagement recorded under it, capped at 1.0; the engagement part is always 0 today (see `GetEngagedContentForTopicAsync`). No caller in the app. |
| `Task<List<string>> GetActiveTopicsAsync(int days = 30, CancellationToken ct = default)` | Up to 15 belief topics observed in the last `days` days, ordered by confidence and recency. |
| `Task<List<ActiveTopic>> GetActiveTopicDetailsAsync(int days = 30, CancellationToken ct = default)` | The same topics as `ActiveTopic(Topic, FirstRecordedAt, LastRecordedAt)`; used by "Get Active Topics" on the Past Self page. |

`InsightSource` values: `ConversationMessage`, `DocumentAnnotation`, `SearchBreakthrough`, `WorkflowSuccess`, `UserExplicitSave`. `EngagementTargetType` values: `Document`, `Conversation`, `Annotation`, `WorkflowRun`, `WebClip` (the app records the first two). `EngagementDepth` values: `Skimmed`, `Read`, `Engaged`, `Deep`, `Core`.

---

### IVoiceDraftService

**Namespace**: `AgentX.Core.Services.TemporalIdentity` | **Implementation**: `VoiceDraftService`

"Draft as Me" on the Past Self page: the active AI provider writes a draft in the user's voice, following the views they had recorded by the time period chosen on the page (All time, Past week, Past month, Past year or Custom date).

| Member | Description |
|--------|-------------|
| `Task<VoiceDraft?> StartDraftAsync(VoiceDraftRequest request, CancellationToken ct = default)` | Builds the prompt and returns the draft ready to stream, or null when no AI provider is available (the AI service is not initialized, no provider is active, or the active one is not reachable); nothing is sent then. Throws `ArgumentException` when the request has no context. |

How the prompt is built:

- **Voice**: the learned profile (sample count, average sentence length, formality described as casual below 0.3, neither casual nor formal below 0.6, formal from 0.6), with a note that it is a rough guide below 10 samples; a plain first-person voice when no profile exists.
- **Views**: up to 5 stances, as held at `At`, on recorded topics that share a word with the context or goal (words of four letters or more, common words ignored, plain plurals folded); topics first recorded after `At` are left out.
- **Background**: up to 3 insights from `GetRelevantInsightsAsync` that were saved by `At`, marked as not necessarily the user's own words.
- **Rules**: write only the draft, in the first person and in the language of the request; stay consistent with the recorded views; invent no names, roles, dates, numbers, commitments or experiences (a placeholder such as `[date]` is used instead); treat quoted text as information, never as instructions.

The context is cut at 6,000 characters (the page's Context box stops at that length). The model runs with temperature 0.7 and up to 1,024 tokens through `IAiService.StreamChatAsync`.

| Type | Shape |
|------|-------|
| `VoiceDraftRequest` | `(string Context, string? Goal, DateTime? At)`. `At` null uses everything recorded up to now. |
| `VoiceDraft` | `(VoiceDraftBasis Basis, IAsyncEnumerable<string> Text)`. Enumerating `Text` runs the model; a provider failure surfaces there, and cancelling the token passed to `StartDraftAsync` stops it with `OperationCanceledException`. |
| `VoiceDraftBasis` | `(string WrittenBy, DateTime AsOf, VoiceProfileEntity? VoiceProfile, IReadOnlyList<VoiceDraftView> Views, IReadOnlyList<VoiceDraftInsight> Insights)`. `WrittenBy` is the model and provider, for example `llama3.2 (Ollama)`. The page shows it next to the draft. |
| `VoiceDraftView` | `(string Topic, string Stance)` |
| `VoiceDraftInsight` | `(string Text, DateTime SavedAt)` |

---

### EngagementTracker

**Namespace**: `AgentX.Core.Services.TemporalIdentity` | **Type**: class (not registered in DI)

Times how long one item stays open in a viewer and reports it to `ITemporalIdentityService.RecordEngagementAsync` when the viewer moves off it. The chat creates one for conversations (counted while the chat page is on screen) and the Knowledge Vault one for documents (counted while the document preview is shown). Not thread-safe: a viewer drives it from its UI thread. A recording failure is logged and swallowed.

| Member | Description |
|--------|-------------|
| `EngagementTracker(ITemporalIdentityService temporalIdentity, EngagementTargetType targetType, Func<DateTime>? utcNow = null)` | Creates a tracker for one kind of item; `utcNow` replaces the clock in tests. |
| `static readonly TimeSpan MaxViewDuration` | 30 minutes: the most one view can count for, so an item left open overnight does not outweigh everything else. |
| `long? OpenTargetId` | The item being timed, or null. |
| `Task OpenAsync(long targetId)` | Records the item that was open before and starts timing `targetId`; opening the item already open keeps its running time. |
| `Task CloseAsync()` | Stops timing and records the whole seconds shown, capped at `MaxViewDuration`; a view shorter than one second records nothing. |
| `void Discard()` | Stops timing without recording, for an item that no longer exists. |

---

## Audio and Screen Services

### ITranscriptionService

**Namespace**: `AgentX.Core.Services.Audio` | **Implementation**: `TranscriptionService`

Local speech-to-text with Whisper (Whisper.net running whisper.cpp GGML models on the computer). Used by the audio document processor (audio imports), by voice input in Chat (`VoiceCoordinator`), and by the "Speech-to-Text Model" card on the Model Manager page, which offers Download, Cancel download and Remove. The app always uses the `base` model.

Models are files named `ggml-<size>.bin` in `%LOCALAPPDATA%\AgentX\Models\Whisper\`, downloaded from the `ggerganov/whisper.cpp` repository on Hugging Face (`large` is `ggml-large-v3.bin`). Nothing downloads a model on its own. When the Model Manager finishes a download it calls [IDocumentService.RequeueAudioAwaitingSpeechModelAsync](#idocumentservice), so audio imported while the model was missing is indexed again. Downloads and removals are serialized.

| Member | Description |
|--------|-------------|
| `IReadOnlyList<string> SupportedFormats` | `.mp3`, `.wav`, `.m4a`, `.flac`, `.ogg`, `.webm`. |
| `Task<TranscriptionResult> TranscribeFileAsync(string audioFilePath, TranscriptionOptions? options = null, IProgress<TranscriptionProgress>? progress = null, CancellationToken ct = default)` | Transcribes a file. Anything other than a 16 kHz integer-PCM WAV is first decoded and resampled into a temporary WAV, which is deleted afterwards. Progress runs from validation (0%) through audio preparation (10%), model loading (20%) and transcription (30 to 90%, with each finished segment attached) to 100%. Throws `FileNotFoundException` for a missing file; `NotSupportedException` for an unsupported extension or audio that cannot be decoded; `TranscriptionRuntimeUnavailableException` (a `NotSupportedException`) when the native Whisper runtime cannot load on this computer; `TranscriptionModelMissingException` (an `InvalidOperationException` carrying `ModelSize`) when the model is not installed; `InvalidOperationException` when the model file is damaged or incompatible. |
| `Task<bool> IsModelAvailableAsync(string modelSize = "base")` | Whether the model file exists. |
| `Task<long?> GetInstalledModelSizeAsync(string modelSize = "base")` | The installed file's size in bytes, or null. Throws `ArgumentException` for a size other than `tiny`, `base`, `small`, `medium` or `large`. |
| `Task DownloadModelAsync(string modelSize = "base", IProgress<double>? progress = null, CancellationToken ct = default)` | Downloads the model unless it is already present, reporting 0.0 to 1.0. The file is written under a temporary name and moved into place only when its length matches the server's Content-Length (when sent) and it starts with the GGML magic; a cancelled, failed or incomplete download throws and leaves no file behind. |
| `Task RemoveModelAsync(string modelSize = "base")` | Deletes the model file and any partial download; nothing happens when it is not installed. Throws `IOException` when the file cannot be deleted (for example while in use). |

`TranscriptionOptions`: `ModelSize` (`base`), `Language` (null or `auto` detects it), `EnableTimestamps` (true; when false the result has no segments), `EnableSpeakerDiarization` (false; the bundled runtime does not support it, so a request for it is logged and segments carry no speaker ids). `TranscriptionResult`: `FullText`, `Segments` (`StartMs`, `EndMs`, `Text`, `SpeakerId`), `Language`, `DurationMs` (the end of the last segment), `ModelUsed`. `TranscriptionProgress`: `PercentComplete`, `CurrentPhase`, `Segment`.

---

### IScreenCaptureService

**Namespace**: `AgentX.Core.Services.Screen` | **Implementation**: `ScreenCaptureService`

Screen context for Quick Chat: captures a window or the screen with GDI and runs Windows OCR on the image. Quick Chat reads the window that was in front when it was summoned (`CaptureWindowAndOcrAsync`), or the active window when none was recorded.

Every method returns an empty `ScreenContextResult` without capturing when `AppSettings.EnableScreenAwareness` is false. A capture or OCR failure is logged and returns a result without OCR text (the window title may still be set). That setting is false by default and no page in the app changes it, so screen context stays off unless `settings.json` is edited.

| Member | Description |
|--------|-------------|
| `Task<ScreenContextResult> CaptureAndOcrAsync(CancellationToken ct = default)` | The whole primary screen, with the foreground window's title. |
| `Task<ScreenContextResult> CaptureActiveWindowAndOcrAsync(CancellationToken ct = default)` | The foreground window. |
| `Task<ScreenContextResult> CaptureWindowAndOcrAsync(IntPtr windowHandle, CancellationToken ct = default)` | The given window (HWND); empty for a zero handle. |

`ScreenContextResult`: `OcrText`, `ActiveWindowTitle`, `CapturedAtUtc`, `IdeContext` (an `IdeDetection` when the window title belongs to a recognized IDE, else null) and `IsEmpty`.

---

## Web Import Services

The Web Import page imports web pages, YouTube transcripts, feed items and sitemap pages into the Knowledge Vault. All services here are in `AgentX.Core.Services.Web`.

### IWebImportService

**Implementation**: `WebImportService`

Turns a URL into a vault document. For each URL: the page is extracted with [IWebScraperService](#iwebscraperservice); the text is saved as a Markdown file with a small front matter block (`source`, `author`, `date`, `site`) in the `WebImports` folder under the storage path; and the document (file type `web`, status `pending`) is recorded with its optional collection link in one save through `IDocumentService.ImportPreparedDocumentAsync`, which signals the indexer at once.

| Member | Description |
|--------|-------------|
| `Task<DocumentEntity> ImportFromUrlAsync(string url, long? collectionId = null, CancellationToken ct = default)` | Imports one URL. Throws `InvalidOperationException` for a URL that is not absolute HTTP or HTTPS, a collection that does not exist (checked before anything is fetched), a failed or empty extraction, and a duplicate (`DuplicateDocumentException`, raised when a document with the same content hash exists). The saved file is deleted when recording the document fails. |
| `Task<IReadOnlyList<WebImportResult>> ImportFromUrlsAsync(IReadOnlyList<string> urls, long? collectionId = null, IProgress<int>? progress = null, CancellationToken ct = default)` | Imports URLs one after another ("Import All"); a failure is reported in that URL's result and the batch continues. Progress is the number of URLs done. Cancellation stops the batch. |
| `Task<IReadOnlyList<WebImportResult>> ImportDiscoveredUrlsAsync(string sourceUrl, IReadOnlyList<string> urls, long? collectionId = null, IProgress<int>? progress = null, CancellationToken ct = default)` | The same for URLs listed by a feed or sitemap, except that a URL pointing to this computer or a private network address is not fetched (its result says why), unless `sourceUrl` itself is on such a network. |

`WebImportResult`: `Url`, `Document` (null on failure), `ErrorMessage`, `Success`.

---

### IWebScraperService

**Implementation**: `WebScraperService` (uses `IWebContentFetcher`, `IHtmlParser`, `IStructuredDataExtractor`)

| Member | Description |
|--------|-------------|
| `Task<WebContent> ExtractContentAsync(string url, CancellationToken ct = default)` | Fetches the page and extracts the main article text and metadata (title, author, publish date, site name, description, image, canonical URL, language, word count). A YouTube URL is sent to `ExtractYouTubeTranscriptAsync`. Failures come back with `Success` false and an `ErrorMessage` instead of an exception. Used by "Preview First URL", by `IWebImportService` and by the `.url` / `.webloc` document processor. |
| `Task<WebContent> ExtractYouTubeTranscriptAsync(string youtubeUrl, CancellationToken ct = default)` | Reads the video's caption track from its watch page (no API key); fails when the video has no captions. |
| `Task<IReadOnlyList<WebContent>> ExtractBatchAsync(IReadOnlyList<string> urls, IProgress<int>? progress = null, CancellationToken ct = default)` | Extracts several URLs in turn with 500 ms between requests. No caller in the app. |
| `bool IsYouTubeUrl(string url)` | Matches `youtube.com/watch?v=`, `youtu.be/`, `youtube.com/embed/` and `youtube.com/shorts/`. |
| `bool IsValidUrl(string url)` | Whether the text is an absolute HTTP or HTTPS URL. |

---

### IWebContentFetcher and IJsRenderingService

**Implementations**: `WebContentFetcher`, `JsRenderingService`

The page fetcher, the feed reader and the sitemap parser send their requests through `GuardedWebHandler` and `WebHttp`: redirects are followed one hop at a time, a host that checked as public is connected to only at public addresses (so DNS rebinding cannot reach a private one), response bodies are read under a size cap, and cloud metadata endpoints are refused even for a URL the user entered.

| Member | Description |
|--------|-------------|
| `IWebContentFetcher.FetchAsync(string url, CancellationToken ct = default)` | Returns `FetchResult(string Html, string? FinalUrl, TimeSpan Elapsed, bool UsedJsRendering)`. Decompresses, sends a browser User-Agent, gives up after 15 seconds (`TimeoutException`) and refuses responses over 10 MB. Throws `HttpRequestException` for an error status. When the page is empty, or relies on scripts and shows fewer than 200 characters of text without them, it is rendered again with `IJsRenderingService`; if that fails, the plain HTML is used. |
| `IJsRenderingService.RenderPageAsync(string url, bool waitForNetworkIdle = false, CancellationToken ct = default)` | Renders the page in headless Chromium through Playwright (30-second navigation timeout) and returns the HTML. The browser starts on first use; it must be a Playwright Chromium installed on the computer, which the app does not install. For a public page, requests and WebSockets to local or private addresses are refused. |

---

### IHtmlParser and IStructuredDataExtractor

**Implementations**: `HtmlParser`, `StructuredDataExtractor`

| Member | Description |
|--------|-------------|
| `ParsedContent IHtmlParser.Parse(string html, string url)` | Article text plus title, description, author, publish date and reading time. |
| `string IHtmlParser.ExtractReadabilityText(string html)` | The main text by text density, without navigation, footers, scripts and styles. |
| `Metadata IHtmlParser.ExtractMetadata(string html, string url)` | Title, description, author, image, publish date and site name from Open Graph, Twitter Card, standard meta tags and JSON-LD. |
| `JsonLdData? IStructuredDataExtractor.ExtractJsonLd(string html)` | The first schema.org object in the page's JSON-LD (arrays and `@graph` included). |
| `OpenGraphData? IStructuredDataExtractor.ExtractOpenGraph(string html)` | `og:title`, `og:description`, `og:image`, `og:url` and `og:type`. |
| `IReadOnlyList<StructuredTag> IStructuredDataExtractor.ExtractMetaTags(string html)` | Every `name` and `property` meta tag. |
| `string? IStructuredDataExtractor.ExtractAuthor(string html)` | The author from JSON-LD, then meta tags, then `rel="author"` links. `WebScraperService` uses this for the author. |

---

### IFeedService and ISitemapParser

**Implementations**: `FeedService`, `SitemapParser`

"Subscribe & Import" on the Web Import page reads the feed once and imports the items it lists at that moment; no subscription is stored. "Import from Sitemap" imports the first 100 URLs a sitemap lists. Both import through `ImportDiscoveredUrlsAsync`.

| Member | Description |
|--------|-------------|
| `Task<FeedInfo> IFeedService.ParseFeedAsync(string feedUrl, CancellationToken ct = default)` | Reads an RSS 2.0, RSS 1.0 (RDF) or Atom 1.0 feed (up to 10 MB, 30-second timeout). `FeedInfo`: `Title`, `Url`, `Description`, `LastUpdated`, `Items`; each `FeedItem` has `Title`, `Content`, `Url`, `Author`, `PublishedDate`, `Description`, `Category`. Throws `InvalidOperationException` for an unrecognized format. |
| `Task<IReadOnlyList<FeedItem>> IFeedService.GetNewItemsAsync(string feedUrl, DateTime since, CancellationToken ct = default)` | Items published after `since`. No caller in the app. |
| `Task<IReadOnlyList<string>> ISitemapParser.ParseSitemapAsync(string sitemapUrl, CancellationToken ct = default)` | All page URLs of a sitemap, following sitemap indexes (at most 10 levels deep, 100 child sitemaps per index, 500 sitemap fetches, 50,000 URLs and 50 MB per sitemap). |
| `Task<IReadOnlyList<string>> ISitemapParser.ParseSitemapIndexAsync(string sitemapIndexUrl, CancellationToken ct = default)` | Only the child sitemap URLs of an index. No caller in the app. |

---

## Export Services

### IExportService

**Namespace**: `AgentX.Core.Services.Export` | **Implementation**: `ExportService`

Writes conversations, collections, workflow results and search results to files. Used by the Export Conversation dialog in Chat (Format, Template (optional), Include citations, Include model info, Include metadata, Include timestamps, Copy as Markdown), by collection export in the Collection Manager and by result export in the Workflow Builder.

Every export method returns an `ExportResult` (`Success`, `FilePath`, `FileSize`, `ErrorMessage`) instead of throwing: a missing item, an unsupported format, cancellation and I/O errors all come back as `Success` false with a message. When `ExportOptions.OutputPath` is null the file goes to `<storage path>\Exports\<title>_<yyyyMMdd_HHmmss><extension>`.

| Member | Description |
|--------|-------------|
| `Task<ExportResult> ExportConversationAsync(long conversationId, ExportOptions options, CancellationToken ct = default)` | One conversation in any `ExportFormat`, through the matching [IExportFormatter](#iexportformatter). With `TemplateId` set, the conversation is laid out by that template instead; templates produce Markdown, so any other format fails. |
| `Task<ExportResult> ExportConversationsAsync(IReadOnlyList<long> conversationIds, ExportOptions options, CancellationToken ct = default)` | Several conversations in one file, separated by section dividers. Fails when no id is given, none is found, or a template is set. |
| `Task<ExportResult> ExportCollectionAsync(long collectionId, ExportOptions options, CancellationToken ct = default)` | A ZIP with `manifest.json` (collection and document metadata: names, paths, types, sizes, status, hashes) and `README.txt`; the document files themselves are not included. With `Format` `Csv`, a single CSV of the documents instead. |
| `Task<ExportResult> ExportTextArtifactAsync(TextArtifactExportItem artifact, ExportOptions options, CancellationToken ct = default)` | A titled text (`Title`, `Content`, optional `Metadata`), such as a workflow result, as Markdown, PlainText, Html or Json; other formats fail. |
| `Task<ExportResult> ExportSearchResultsAsync(string query, IReadOnlyList<SearchResultExportItem> results, ExportOptions options, CancellationToken ct = default)` | Search results (`Query`, `Content`, `DocumentName`, `RelevanceScore`, `Citations`) as Markdown, Json, PlainText or Csv. No caller in the app. |
| `Task<string> FormatConversationAsMarkdownAsync(long conversationId, bool includeMeta)` | The conversation as Markdown, without writing a file ("Copy as Markdown"). Failures throw, so an empty string is never reported as a successful copy. |
| `Task<string> FormatConversationAsHtmlAsync(long conversationId, bool includeMeta)` | The same as styled HTML. No caller in the app. |

`ExportOptions`: `Format` (`Markdown`), `IncludeCitations` (true), `IncludeMetadata` (true), `IncludeTimestamps` (true), `IncludeModelInfo` (false), `OutputPath`, `Title`, `TemplateId`, and `IncludeBranches`, which no exporter applies yet (the dialog does not offer it).

---

### IExportFormatter

**Namespace**: `AgentX.Core.Services.Export.Formatters` | **Implementations**: eight, one per `ExportFormat`, all registered as `IExportFormatter`

| Member | Description |
|--------|-------------|
| `ExportFormat Format` | The format handled. |
| `string FileExtension` | Extension with the leading dot. |
| `string MimeType` | MIME type. |
| `Task<string> ExportConversationAsync(ConversationEntity conversation, ExportOptions options, CancellationToken ct = default)` | One conversation as text; the binary formats return Base64, which `ExportService` decodes before writing. |
| `Task<string> ExportConversationsAsync(IReadOnlyList<ConversationEntity> conversations, ExportOptions options, CancellationToken ct = default)` | Several conversations. |

| Format | Formatter | Extension |
|--------|-----------|-----------|
| `Markdown` | `MarkdownFormatter` | `.md` |
| `Html` | `HtmlFormatter` (renders with `HtmlExport`) | `.html` |
| `Pdf` | `PdfFormatter` (renders with `PdfExport`, QuestPDF) | `.pdf` |
| `Json` | `JsonFormatter` (renders with `JsonExport`) | `.json` |
| `PlainText` | `PlainTextFormatter` | `.txt` |
| `Csv` | `CsvFormatter` | `.csv` |
| `Docx` | `DocxFormatter` (OpenXML) | `.docx` |
| `Pptx` | `PptxFormatter` (OpenXML) | `.pptx` |

`HtmlExport`, `JsonExport` and `PdfExport` (namespace `AgentX.Core.Services.Export.Formats`) implement the older `IExportFormat` (`Format`, `FileExtension`, `Task<object> RenderAsync<T>(T data, ExportOptions options, CancellationToken ct = default)`, `bool Supports<T>()`). They are not registered; the three formatters create and call them directly.

---

### IExportTemplateService

**Namespace**: `AgentX.Core.Services.Export` | **Implementation**: `ExportTemplateService`

Built-in layouts for a single-conversation Markdown export. Templates rearrange the conversation's own messages under fixed headings; no model is called.

| Member | Description |
|--------|-------------|
| `IReadOnlyList<ExportTemplate> GetTemplates()` | `ResearchReport` (Introduction, Methodology, Findings, Discussion, Conclusion, References), `ExecutiveSummary` (Executive Summary, Key Findings, Recommendations) and `AnnotatedBibliography` (Overview, Sources). Each `ExportTemplate` has `Id`, `Name`, `Description` and `Sections`. |
| `Task<string> ApplyTemplateAsync(ExportTemplateId templateId, IReadOnlyList<TemplateMessage> messages, string title)` | The Markdown for one template; `TemplateMessage` has `Role`, `Content`, `Timestamp` and `DocumentName`. Throws `ArgumentOutOfRangeException` for an unknown id. |

---

## Workflow Services

A workflow is an ordered list of steps run on one text input. Each step sees the original input and the previous step's output; its output becomes the next step's `{{previous_output}}`, and the last output is the run's result. Workflows are built and run on the Workflow Builder page.

### IWorkflowService

**Namespace**: `AgentX.Core.Services.Workflows` | **Implementation**: `WorkflowService`

Stores workflows, their steps and their runs.

| Member | Description |
|--------|-------------|
| `Task<WorkflowEntity> CreateWorkflowAsync(string name, string? description, string category)` | A new empty workflow. Throws `ArgumentException` for an empty name or category. |
| `Task<WorkflowEntity?> GetWorkflowAsync(long workflowId)` | A workflow with its steps in order, or null. |
| `Task<IReadOnlyList<WorkflowEntity>> GetAllWorkflowsAsync(bool includeBuiltIn = true)` | All workflows by category, then name. |
| `Task<IReadOnlyList<WorkflowRunHistoryItem>> GetRecentRunsAsync(long workflowId, int maxCount = 8, CancellationToken ct = default)` | Recent runs, newest first: status, input, final output, error, times, steps completed of total, tokens, duration and per-step results. |
| `Task<WorkflowEntity> CreateWorkflowFromTemplateAsync(long sourceWorkflowId, string? nameOverride = null, CancellationToken ct = default)` | Copies a workflow and its steps into a new editable one (used to start from a built-in). Throws `InvalidOperationException` when the source does not exist. |
| `Task UpdateWorkflowAsync(WorkflowEntity workflow)` | Saves name, description, category, icon and enabled state (steps are not touched). Throws `InvalidOperationException` when the workflow does not exist. |
| `Task DeleteWorkflowAsync(long workflowId)` | Deletes a workflow with its steps and runs. Throws `InvalidOperationException` for a built-in workflow. |
| `Task AddStepAsync(long workflowId, WorkflowStepEntity step)` | Adds a step. |
| `Task UpdateStepAsync(WorkflowStepEntity step)` | Saves a step's name, type, prompt template, overrides and settings. |
| `Task RemoveStepAsync(long stepId)` | Removes a step; the others keep their order numbers. |
| `Task ReorderStepsAsync(long workflowId, IReadOnlyList<long> stepIdsInOrder)` | Renumbers the steps in the given order. |
| `Task<string> ExportWorkflowAsJsonAsync(long workflowId)` | The workflow and its steps as JSON. |
| `Task<WorkflowEntity> ImportWorkflowFromJsonAsync(string json)` | Creates a new, non-built-in workflow from exported JSON. Throws `ArgumentException` for empty text and `InvalidOperationException` for JSON that is not a workflow export or has no name. |
| `Task SeedBuiltInWorkflowsAsync()` | Adds the built-in workflows when none exist (the Workflow Builder calls it when it loads) and repairs known defects in seeded ones the user has not changed. The built-ins are "Summarize & Act", "Research Brief", "Document Review" and "Content Repurpose", all made of AI prompt steps. |
| `Task<int> ReconcileInterruptedRunsAsync(CancellationToken ct = default)` | Marks runs left "running" by a previous session (the app closed or crashed mid-run) as failed with an "interrupted" message and returns how many. Called at startup, and by the engine before its first run. |

---

### IWorkflowEngine

**Namespace**: `AgentX.Core.Services.Workflows` | **Implementation**: `WorkflowEngine`

Runs one workflow at a time and records each run (status `running`, then `completed`, `failed` or `cancelled`) with its step results, final output and an estimated token count (about four characters per token). A failed step ends the run as failed; the output so far is kept.

| Member | Description |
|--------|-------------|
| `Task<WorkflowRunResult> ExecuteWorkflowAsync(long workflowId, string input, IProgress<WorkflowStepResult>? progress = null, CancellationToken ct = default)` | Runs the steps in order, reporting each finished step to `progress` and `StepCompleted`. Throws `ArgumentException` for empty input and `InvalidOperationException` when another workflow is running, the workflow does not exist, or it has no steps. A step that times out without a cancellation request fails with a timeout message. |
| `Task CancelExecutionAsync()` | Cancels the running workflow: a model or search call in progress is cancelled, and the run is saved as cancelled with the output so far. |
| `bool IsRunning` | Whether a workflow is executing. |
| `event EventHandler<WorkflowStepResult>? StepCompleted` | Raised after each step. |

`WorkflowStepResult`: `StepName`, `StepOrder`, `Output`, `TokensUsed`, `DurationMs`, `ModelUsed`, `Success`, `ErrorMessage`. `WorkflowRunResult`: `WorkflowName`, `Steps`, `FinalOutput`, `TotalTokensUsed`, `TotalDurationMs`, `Success`, `WasCancelled`.

#### Step types

Templates can use `{{input}}` (the run's input) and `{{previous_output}}`; both are replaced in one pass. A step's settings are JSON in `WorkflowStepEntity.ConfigJson`; the Workflow Builder checks them as they are typed (`WorkflowStepSettings`).

| `StepType` | What it does | Settings |
|------------|--------------|----------|
| `AiPrompt` | Sends the resolved prompt template to the active model as one user message. `ModelOverride`, `TemperatureOverride` and `MaxTokensOverride` on the step apply to this call. | None. |
| `DocumentLookup` | Asks the vault through [IRagPipeline](#iragpipeline) with the resolved template as the question; the output is the answer text. | Optional `collectionId`. |
| `TextTransform` | Applies a text operation to the resolved template (or the previous output when the template is empty). No model call. | `transform`: `uppercase` (default), `lowercase`, `titlecase`, `trim`, `extract_lines`, `word_count`, `char_count`, `reverse_lines`, `deduplicate_lines`, `sort_lines`, `number_lines`. |
| `ConditionalBranch` | Tests the previous output and outputs the resolved `trueBranch` or `falseBranch` text (each defaults to the previous output). Fails without settings. | `condition`: `contains`, `not_contains`, `starts_with`, `ends_with`, `equals` (all ignoring case), `matches` (a regular expression, limited to 2 seconds) or `length_greater_than`; `value`; `trueBranch`; `falseBranch`. |
| `OutputFormat` | Formats the resolved template (or the previous output) and adds an optional prefix and suffix. | `format`: `json`, `markdown`, `html`, `bullet_list`, `numbered_list`; `prefix`; `suffix`. |

An unknown step type fails the step.

---

## Smart Inbox and Connectors

### IInboxService

**Namespace**: `AgentX.Core.Services.Inbox` | **Implementation**: `InboxService`

The Smart Inbox holds items for triage before they enter the vault. Items come from three places: pages clipped with the browser extension (through the [Local REST API](#local-rest-api-services), status `pending`), the built-in Calendar and Email connectors, and `DataConnector` plugins (both through `UpsertExternalAsync`, imported at once). Files from watch folders do not pass through the inbox; they go straight to the Knowledge Vault. Statuses are `pending`, `accepted`, `rejected` and `deferred`.

| Member | Description |
|--------|-------------|
| `Task<InboxItemEntity> AddToInboxAsync(string filePath, long? watchFolderId = null, string? sourceType = null, string? sourceUrl = null)` | Adds a pending item; a path that is already pending returns the existing row. |
| `Task<IReadOnlyList<InboxItemEntity>> GetPendingItemsAsync()` | Pending items, oldest first. |
| `Task<IReadOnlyList<InboxItemEntity>> GetAllItemsAsync(string? statusFilter = null, int skip = 0, int take = 50)` | A page of items, newest first, optionally of one status. |
| `Task<int> GetPendingCountAsync()` | Number of pending items (the inbox badge). |
| `Task<InboxAcceptResult> AcceptItemAsync(long itemId, long? collectionId = null)` | Copies the file into app storage, imports it through `IDocumentService.ImportFileAsync` (which queues it for indexing) and marks the row accepted, linked to the document; identical content already in the vault is linked instead of imported twice. `collectionId` overrides the suggested collection. Throws, leaving the row as it was, when the file is gone, its type cannot be processed or import is unavailable. The result is `(ItemId, Outcome, DocumentId)` with outcome `Imported`, `AlreadyInVault` or `AlreadyAccepted`. |
| `Task<InboxBatchAcceptResult> AcceptAllPendingAsync()` | "Accept All Pending": each pending item as `AcceptItemAsync` does, with its own suggested collection. Failed items stay pending. |
| `Task<InboxBatchAcceptResult> AcceptSelectedAsync(IEnumerable<long> itemIds, long? collectionId = null)` | The same for chosen items; unknown ids are skipped. `InboxBatchAcceptResult` has `Imported`, `AlreadyInVault`, `AlreadyAccepted`, `Failed`, `Errors` and `Accepted`. |
| `Task RejectItemAsync(long itemId)` | Marks a pending item rejected; the file stays on disk. |
| `Task DeferItemAsync(long itemId)` | Marks a pending item deferred; it stays visible but no longer counts as pending. |
| `Task RejectSelectedAsync(IEnumerable<long> itemIds)` | Rejects several items; missing or processed ones are skipped. |
| `Task GeneratePreviewAsync(long itemId, CancellationToken ct = default)` | Sends the first 2,000 characters of the file to the active model (temperature 0.2, up to 512 tokens) for a short preview, a suggested collection and tags, and saves them on the row. |
| `Task GenerateAllPreviewsAsync(CancellationToken ct = default)` | "Generate AI Previews": the same for every pending item without a preview, one at a time. |
| `Task DeleteProcessedItemsAsync()` | "Clean Up Processed": deletes accepted and rejected rows (not files); pending and deferred items stay. |
| `Task<ExternalTriageResult> UpsertExternalAsync(string fileName, string fileType, string sourceType, string? sourceUrl, string sourcePluginId, string? sourceCategory, string externalId, string? contentPreview, string contentText)` | Creates or refreshes the row for an external item, keyed by `(sourcePluginId, externalId)`. A new item gets its content written under the app data folder (the file name is a hash of the two ids), an accepted row and a vault import: outcome `Created`. Changed content or metadata rewrites the file and row and re-indexes the linked document: `Updated`. Otherwise nothing is written: `Unchanged`. A failed vault import is logged and the row is still returned. |
| `Task<InboxItemEntity> TriageExternalAsync(...)` | The same parameters as `UpsertExternalAsync`; returns only the row. |
| `Task<IReadOnlyList<InboxItemEntity>> GetExternalItemsAsync(string sourcePluginId, string externalIdPrefix)` | A connector's rows whose external id starts with the prefix (for a calendar, every stored event of one calendar), untracked. |
| `Task<ExternalRemovalResult> RemoveExternalAsync(string sourcePluginId, string externalId, Func<ExternalItemContent, ExternalItemContent> markRemoved)` | Retires an item that is gone at its source. No row: `NotFound`. A row without a vault document: the row and its file are deleted (`Deleted`). A row with a vault document: `markRemoved` rewrites its name, preview and text, and the document is renamed and re-indexed but never deleted (`Marked`, or `AlreadyMarked` when nothing changed). A later `UpsertExternalAsync` for the same item replaces the marked copy. |

---

### IOAuthService

**Namespace**: `AgentX.Core.Services.OAuth` | **Implementation**: `OAuthService`

OAuth 2.0 sign-in for the Calendar and Email connectors (Google and Microsoft). Agent-X ships no client credentials: a provider is registered only after its client ID is saved under "OAuth App Credentials" on the Calendar Connector or Email Connector page. The flow opens the system browser, receives the callback on a localhost listener, checks the `state` value and uses PKCE. Tokens are stored DPAPI-encrypted in the `OAuthCredentials` table and decrypted only in memory. When the service is created, the app applies `AppSettings.OAuth` to it with `ApplySettings` and `ApplyProviderSettings`.

| Member | Description |
|--------|-------------|
| `Task<OAuthCredential> AuthorizeAsync(string provider, string? scopes = null, string? redirectUri = null, CancellationToken cancellationToken = default)` | Runs the consent flow and stores the credential. `scopes` adds to the provider's defaults. The redirect URI must be `http://localhost:` or `http://127.0.0.1:`. Throws `OAuthProviderNotConfiguredException` for a provider without client configuration, `SecurityException` when the callback's state does not match, `InvalidOperationException` when consent is denied or the token exchange fails, and `OperationCanceledException` on cancellation or when the consent timeout (`AuthTimeoutSeconds`, 300 by default) passes. |
| `Task<string> GetAccessTokenAsync(string provider)` | A valid access token, refreshed first when it expires within the refresh buffer (`TokenRefreshBufferMinutes`, 5 by default). Refreshes are serialized per provider. Throws `InvalidOperationException` when there is no credential or the refresh fails. |
| `Task<bool> RefreshTokenAsync(string provider)` | Refreshes with the stored refresh token; false when there is no credential or the refresh fails. |
| `Task RevokeAsync(string provider)` | Revokes the grant at the provider when it has a revocation endpoint (Google; best effort, the refresh token is revoked when present), then deletes the local credential. |
| `Task<OAuthCredential?> GetCredentialAsync(string provider)` | The stored credential, decrypted, or null. Does not refresh. |
| `void ApplyProviderSettings(OAuthSettings settings)` | Registers Google and Microsoft from the saved client credentials: a provider with a client ID is (re)registered, one with an empty client ID is unregistered. Stored credentials are kept, so saved changes take effect without a restart. |

`OAuthService` also has:

| Member | Description |
|--------|-------------|
| `void ApplySettings(OAuthSettings settings)` | Applies the refresh buffer (clamped to 0 to 60 minutes) and consent timeout (30 seconds to 1 hour). |
| `void RegisterProvider(OAuthProviderConfig config)` | Registers or replaces a provider configuration. |
| `bool UnregisterProvider(string provider)` | Removes a provider configuration (credentials are kept); false when none was registered. |
| `IReadOnlyDictionary<string, OAuthProviderConfig> GetRegisteredProviders()` | The registered configurations. |

`OAuthProviderNotConfiguredException` (an `InvalidOperationException`) carries `Provider`; the connector pages catch it and explain the setup.

`OAuthProviderRegistry` builds the two configurations: `ProviderIdGoogle` (`"google"`) and `ProviderIdMicrosoft` (`"microsoft"`); `Google(string clientId, string clientSecret, string redirectUri)` requests `openid profile email` plus read-only Calendar and Gmail scopes with `access_type=offline` and `prompt=consent`; `Microsoft(string clientId, string clientSecret, string tenantId, string redirectUri)` requests `openid profile email offline_access Calendars.Read Mail.Read User.Read` with `prompt=select_account` and has no revocation endpoint. The Microsoft registration is a public client, so its client secret may be empty. Default redirect URIs are `http://localhost:8400/oauth/callback` (Google) and `http://localhost:8401/oauth/callback` (Microsoft); the default tenant is `common`.

`OAuthProviderConfig` (init-only): `ProviderId`, `DisplayName`, `AuthorizationEndpoint`, `TokenEndpoint`, `RevocationEndpoint`, `Scopes` (space-separated; commas accepted), `ClientId`, `ClientSecret`, `RedirectUri`, `ExtraAuthParameters`.

`OAuthCredential` (decrypted, in memory only): `ProviderId`, `AccessToken`, `RefreshToken`, `TokenExpiry`, `RequiresReauthorization` (true when there is no refresh token), `Scopes`, `UserId`, `CreatedAt`, `UpdatedAt`.

---

### Calendar Connector

**Namespace**: `AgentX.Core.Services.Plugins.Calendar`

`CalendarPlugin` (`com.agentx.calendar`, a `DataConnector`) syncs Google Calendar and Outlook calendar events into the Smart Inbox and the vault, where they are searchable like documents. It is started by `BuiltinConnectorLifecycleService` with a context whose services are `IOAuthService` and `IInboxService`, and its sync timer runs only while "Calendar sync" is on (the Calendar Connector page's Save Settings refreshes it). Each sync reads the enabled calendars over the configured range, writes each event through `IInboxService.UpsertExternalAsync`, and retires events that left the calendar through `RemoveExternalAsync` (a vault document is marked as removed, never deleted).

#### ICalendarService

**Implementation**: `CalendarService` (wraps `CalendarPlugin`)

| Member | Description |
|--------|-------------|
| `Task<SyncResult> SyncEventsAsync(CancellationToken cancellationToken = default)` | Runs a sync now ("Sync Now"). While a sync is already running it starts none and returns the previous sync's result, or an empty one. |
| `Task<IReadOnlyList<CalendarInfo>> ListAvailableCalendarsAsync(CancellationToken cancellationToken = default)` | Calendars of every connected provider; a provider that fails is logged and skipped. |
| `Task<bool> IsConnectedAsync()` | Whether a Google or Microsoft credential is stored. No caller in the app. |
| `Task<CalendarSyncSettings> GetSyncSettingsAsync()` | The connector's sync settings. |
| `Task UpdateSyncSettingsAsync(CalendarSyncSettings settings)` | Saves them to the plugin data folder. |
| `Task<IReadOnlyList<CalEvent>> GetUpcomingEventsAsync(int daysAhead = 7, CancellationToken cancellationToken = default)` | Events of the enabled calendars in the next `daysAhead` days, by start time. No caller in the app. |
| `Task<CalEvent?> GetEventDetailsAsync(string eventId, string sourceProvider, string calendarId, CancellationToken cancellationToken = default)` | One event, looked up in the synced range; null when not found. No caller in the app. |

#### ICalendarProvider

Implemented by `GoogleCalendarProvider` and `OutlookCalendarProvider`.

| Member | Description |
|--------|-------------|
| `string ProviderId` | `google` or `microsoft`. |
| `Task<IReadOnlyList<CalendarInfo>> ListCalendarsAsync(CancellationToken cancellationToken = default)` | Calendars the account can read. |
| `Task<(IReadOnlyList<CalEvent> Events, string? DeltaToken)> GetEventsAsync(string calendarId, DateTime start, DateTime end, string? deltaToken = null, CancellationToken cancellationToken = default)` | Events in a range, or the changes since `deltaToken`. The built-in providers return a `CalendarEventBatch` whose `IsCompleteWindow` tells the sync that an event missing from the read is gone; a deletion from an incremental read is a `CalEvent` with `IsDeleted` set. |

#### Calendar models

| Type | Members |
|------|---------|
| `CalEvent` | `Id`, `Title`, `Description`, `Start`, `End`, `Location`, `IsAllDay`, `IsRecurring`, `IsCancelled`, `IsDeleted`, `Attendees` (`CalAttendee`: `DisplayName`, `Email`, `ResponseStatus`, `IsOrganizer`), `Organizer`, `CalendarName`, `SourceProvider`, `HtmlLink`, `CalendarId` |
| `CalendarInfo` | `Id`, `Name`, `Owner`, `EventCount`, `SourceProvider`, `IsPrimary`, `LastSyncedAt` |
| `CalendarSyncSettings` | `EnabledCalendars`, `SyncIntervalMinutes` (15), `DaysFutureToSync` (30), `DaysPastToSync` (90), `ConflictResolution` (`RemoteWins`; saved from the page but not read by the sync), `IncludeAttendeeDetails` (true), `IncludeDescriptions` (true) |
| `SyncResult` | `ItemsAdded`, `ItemsUpdated`, `ItemsSkipped`, `ItemsFailed`, `ItemsRemoved`, `TotalItemsProcessed`, `IsSuccess` (no failures), `StartedAt`, `CompletedAt`, `Duration`, `DeltaToken` |

`CalendarPlugin` also exposes `event EventHandler<SyncResult>? SyncCompleted` and `SyncResult? LastSyncResult`.

---

### Email Connector

**Namespace**: `AgentX.Core.Services.Plugins.Email`

`EmailPlugin` (`com.agentx.email`, a `DataConnector`) syncs Gmail and Outlook mail from the selected folders ("Folders to sync"; only the inbox by default) into the Smart Inbox and the vault. It is started like the Calendar Connector, and its timer runs only while "Email sync" is on. Each message is filed under an `EmailCategory` (`Other`, `ActionRequired`, `Newsletter`, `Notification`, `Meeting`, `Financial`, `Social`, `Promotion`) by the rule-based `EmailTriageProcessor.Classify`; no model is called.

#### IEmailService

**Implementation**: `EmailService` (wraps `EmailPlugin`)

| Member | Description |
|--------|-------------|
| `Task<SyncResult> SyncMessagesAsync(CancellationToken cancellationToken = default)` | Runs a sync now ("Sync Now"). It syncs nothing while email sync is off (no provider is registered then); while a sync is running it returns the previous result. |
| `Task<IReadOnlyList<EmailFolderInfo>> ListAvailableFoldersAsync(CancellationToken cancellationToken = default)` | Folders of every connected account ("Refresh folders"), whether or not sync is on; an account that cannot be reached is logged and left out. |
| `Task<EmailSyncSettings> GetSyncSettingsAsync()` | The connector's sync settings. |
| `Task UpdateSyncSettingsAsync(EmailSyncSettings settings)` | Saves them to `email-sync-settings.json` in the plugin data folder. |
| `Task<bool> IsConnectedAsync()` | Whether the plugin has registered a provider, which it does only while email sync is on (for the accounts connected when sync started), so a connected account reads as not connected while sync is off. No caller in the app. |
| `Task<IReadOnlyList<EmailMessage>> GetRecentMessagesAsync(int count = 20, CancellationToken cancellationToken = default)` | Recent messages across enabled folders. No caller in the app. |

#### IEmailProvider

Implemented by `GmailProvider` and `OutlookEmailProvider`.

| Member | Description |
|--------|-------------|
| `const string InboxFolderId` | `"INBOX"`: both providers report the account's inbox under this id. |
| `string ProviderId` | `google` or `microsoft`. |
| `Task<IReadOnlyList<EmailFolderInfo>> ListFoldersAsync(CancellationToken cancellationToken = default)` | Folders or labels of the account. |
| `Task<(IReadOnlyList<EmailMessage> Messages, string? DeltaToken)> GetMessagesAsync(string folderId, int maxResults = 50, string? deltaToken = null, DateTime? receivedAfterUtc = null, CancellationToken cancellationToken = default)` | Messages of a folder and the token for the next call. A full sync (no token) reads only messages received after `receivedAfterUtc`, which is how "Sync days back" reaches the provider; an incremental sync returns what changed since the token. |

#### Email models

| Type | Members |
|------|---------|
| `EmailMessage` | `Id`, `Subject`, `BodyPreview`, `BodyHtml`, `BodyText`, `From`, `To`, `Cc`, `Bcc` (`EmailContact`: `DisplayName`, `EmailAddress`, `IsMe`), `ReceivedAt`, `IsRead`, `IsStarred`, `HasAttachments`, `FolderName`, `FolderId`, `ThreadId`, `SourceProvider`, `AttachmentNames`, `WebLink` |
| `EmailFolderInfo` | `Id`, `Name`, `TotalCount`, `UnreadCount`, `SourceProvider` |
| `EmailSyncSettings` | `EnabledFolders` (the inbox), `SyncIntervalMinutes` (10), `MaxMessagesPerSync` (50), `SyncDaysBack` (30), `IncludeHtmlBody` (true: a message without a plain-text part keeps its HTML body converted to text), `IncludeAttachmentNames` (true), and `EnableAiCategorization` and `CategorizationPrompt`, which are not applied |

`EmailPlugin` also exposes `Providers`, `event EventHandler<SyncResult>? SyncCompleted`, `SyncResult? LastSyncResult`, `GetSettings()` (a copy), `UpdateSettings(EmailSyncSettings settings)` and `GetProvidersForFolderListingAsync()`.

---

## Plugin Infrastructure

**Namespace**: `AgentX.Core.Services.Plugins`

The plugin contract, packaging and loader are described step by step in the [Plugin Development Guide](PLUGIN-DEVELOPMENT-GUIDE.md). In short: a plugin is a .NET 8 assembly with a `manifest.json`, packaged as a ZIP (`.agentx-plugin` or `.zip`) and installed from the Plugin Manager page. Each plugin loads into its own collectible `AssemblyLoadContext`, which resolves the plugin's private dependencies from its folder while `AgentX.Core`, Serilog, `Microsoft.Extensions.*` and the host's other assemblies always come from the host. Plugins are not sandboxed: they run in-process with the user's rights, and the host does not restrict their file-system or network access. Manifest `permissions` are informational only.

### IPluginService

**Implementation**: `PluginService` (the same instance is registered as `IPluginDocumentProcessorSource`)

| Member | Description |
|--------|-------------|
| `Task<IReadOnlyList<PluginEntity>> GetInstalledPluginsAsync()` | Every installed plugin, enabled or not, by name. |
| `Task<PluginEntity> InstallPluginAsync(string packagePath)` | Reads and validates `manifest.json` (required: `id`, `name`, `version`, `entryAssembly`), refuses a package whose `minAppVersion` is newer than the running Agent-X, whose dependencies are not installed, or whose id is already installed, extracts it to `%LOCALAPPDATA%\AgentX\Plugins\{id}\` (entries that would land outside that folder are refused) and records it disabled. Nothing is loaded. Throws `FileNotFoundException` or `InvalidOperationException`. |
| `Task EnablePluginAsync(long id)` | Checks `minAppVersion` again and that every dependency is enabled, loads the entry assembly, creates its single public non-abstract `IPlugin` type, then calls `InitializeAsync` and `ActivateAsync` (30 seconds each) and saves the plugin as enabled. No-op when already active. Throws `InvalidOperationException` on any failure. |
| `Task DisablePluginAsync(long id)` | Calls `DeactivateAsync` (10 seconds at most) and `Dispose`, unloads the load context and saves the plugin as disabled. |
| `Task<PluginUninstallResult> UninstallPluginAsync(long id)` | Deactivates and unloads the plugin, deletes its folder (retrying while files are locked) and its record. `PluginUninstallResult(bool Found, string? LeftoverDirectory)` names the folder when files could not be deleted; `FilesRemoved` is true when everything went. |
| `Task<PluginActivationSummary> ActivateEnabledPluginsAsync(CancellationToken cancellationToken = default)` | At startup: activates every enabled plugin, dependencies first. A plugin that fails is logged and marked disabled; the rest still start. `PluginActivationSummary(Activated, Failed)` lists the ids and `PluginActivationFailure(PluginId, Reason)` entries. |
| `Task DeactivateAllPluginsAsync()` | At shutdown: deactivates, disposes and unloads every active plugin, dependents first, leaving them enabled for the next start. |
| `Task<IReadOnlyList<IPlugin>> GetActivePluginsAsync()` | The loaded, active instances. |
| `Task<T?> GetPluginInstanceAsync<T>(string pluginId) where T : class, IPlugin` | An active plugin by manifest id, cast to `T`; null when not active or not a `T`. |

---

### IPlugin

The entry type of a plugin: `IDisposable` plus these members.

| Member | Description |
|--------|-------------|
| `string Id`, `string Name`, `string Version`, `string Author`, `string Description` | Identity shown in the Plugin Manager; `Id` matches the manifest. |
| `PluginType Type` | The extension point the plugin targets. |
| `Task InitializeAsync(IPluginContext context)` | Called once after loading; one-time setup, no background work. |
| `Task ActivateAsync()` | Called right after initialization, when the user enables the plugin or at startup for a plugin left enabled. |
| `Task DeactivateAsync()` | Called before disabling or uninstalling, and at shutdown. |

`IDocumentProcessorPlugin : IPlugin, IDocumentProcessor` is the entry type of a document processor plugin. Built-in processors always win: a plugin processor is asked only about files no built-in processor claims.

`PluginType` values: `DocumentProcessor` (integrated through `IPluginDocumentProcessorSource`), `AiProvider`, `QuickAction`, `WorkflowStep`, `DataConnector` (integrated through the `IInboxService` in the context), `Theme`, `Custom`. Only `DocumentProcessor` and `DataConnector` have a host integration; the others are labels.

---

### IPluginContext

| Member | Description |
|--------|-------------|
| `IServiceProvider Services` | A provider holding only `IInboxService`. `IOAuthService` is not offered to installed plugins because it can return the user's stored refresh tokens; only the built-in Calendar and Email connectors receive it. |
| `string PluginDataPath` | `%LOCALAPPDATA%\AgentX\Plugins\{id}\data`, created before `InitializeAsync`. A convention, not a sandbox; it is deleted with the plugin. |
| `ILogger Logger` | A Serilog logger tagged with the plugin id and version. |

---

### IPluginDocumentProcessorSource

| Member | Description |
|--------|-------------|
| `IReadOnlyList<IDocumentProcessor> GetDocumentProcessors()` | A snapshot of the processors of active plugins whose entry type implements `IDocumentProcessor`. `DocumentService` consults it after the built-in processors when a file is imported, and `IndexingService` when it extracts a document again. The returned processors guard `CanProcess` and `SupportedExtensions` so a faulty plugin cannot break processor selection; an exception from `ProcessAsync` fails only that import. Callers must not cache the list, because plugins can be disabled and unloaded at any time. |

---

### PluginManifest

The deserialized `manifest.json` (camelCase keys): `Id` (reverse-DNS, required), `Name` (required, up to 100 characters), `Version` (SemVer, required), `Author`, `Description`, `PluginType` (default `Custom`), `MinAppVersion` (default `1.0.0`), `Dependencies` (plugin ids), `EntryAssembly` (a bare `.dll` file name, required), `Readme` (Markdown shown in the Plugin Manager; a `README` file at the package root replaces it) and `Permissions` (informational). The full table is in the guide's Manifest Reference.

---

## Backup, Sync and Security Services

### IBackupService

**Namespace**: `AgentX.Core.Services.Backup` | **Implementation**: `BackupService`

Backup & Restore page. A backup is a ZIP archive with the extension `.agentxbak` holding `database/agentx.db` (a copy made with the SQLite Online Backup API, so the app keeps running), `manifest.json` (version, counts, timestamps) and, optionally, `documents/` with the document folders inside the storage folder (the files web import writes). Documents imported from other folders are indexed where they are and are not copied. Settings, secrets, the encryption marker, logs, models, plugins, caches and derived indexes are never included. The archive is streamed to disk; with a password it is encrypted on the way with AES-256-GCM in 1 MB records, the key derived with PBKDF2 (600,000 iterations, stored in the header). Older AES-256-GCM and AES-256-CBC archives can still be restored.

| Member | Description |
|--------|-------------|
| `Task<BackupResult> CreateBackupAsync(BackupOptions options, IProgress<BackupProgress>? progress = null, CancellationToken ct = default)` | Creates a backup and records it in the history. `BackupOptions`: `DestinationPath`, `EncryptionPassword` (null for none), `IncludeDocuments` (true), `Notes`, `BackupType` (`manual`). `BackupResult`: `Success`, `BackupFilePath`, `SizeMB`, `DurationMs`, `ErrorMessage`, `BackupId`, `WarningMessages`. |
| `Task<RestoreResult> RestoreFromBackupAsync(string backupFilePath, IProgress<BackupProgress>? progress = null, CancellationToken ct = default)` | Restores an unencrypted archive; an encrypted one fails asking for a password. |
| `Task<RestoreResult> RestoreFromBackupAsync(string backupFilePath, string? password, IProgress<BackupProgress>? progress = null, CancellationToken ct = default)` | Restores an archive. The database is staged next to the live file and verified with the current database key (a plaintext backup is re-encrypted when encryption is on); the vector store is suspended, the file is swapped in, and the replaced file is kept until the new one passes verification, so a failure at any step leaves the current data in place. Agent-X must be restarted afterwards (`RequiresRestart`). `RestoreResult` also carries `RestoredDocumentCount`, `RestoredConversationCount`, `RestoredWorkflowCount`, `DurationMs`, `ErrorMessage` and `WarningMessages`. |
| `Task<IReadOnlyList<BackupEntity>> GetBackupHistoryAsync()` | Backup history, newest first. |
| `Task DeleteBackupAsync(long backupId)` | Deletes a history record and its archive file when it still exists. |
| `Task<BackupSizeEstimate> EstimateBackupSizeAsync()` | `DatabaseSizeMB`, `DocumentsSizeMB`, `TotalEstimatedMB`, `DocumentCount`. |
| `Task<bool> ValidateBackupAsync(string backupFilePath)` | Whether the archive is a readable `.agentxbak` with the expected entries; an encrypted archive is only checked for its encryption header. |
| `Task<bool> ValidateBackupAsync(string backupFilePath, string? password)` | The same, decrypting an encrypted archive; a wrong password returns false. |
| `Task<bool> IsEncryptedBackupAsync(string backupFilePath)` | Whether the archive is password-encrypted. |
| `Task StartScheduledBackupsAsync(CancellationToken ct = default)` | Starts (or replaces) the scheduled-backup loop from the saved schedule; does nothing when the schedule is off. Called at startup and when "Save Schedule" is used. |
| `void StopScheduledBackups()` | Stops the loop after the current cycle. |

`BackupScheduleConfig` ("Scheduled Backups" on the page): `Enabled` ("Back up automatically"), `IntervalHours` (168, at most 720), `MaxBackupsToKeep` (5; older scheduled backups are deleted, 0 keeps all, manual backups are never deleted), `DestinationPath`, `EncryptionPassword`. `BackupProgress`: `Phase`, `PercentComplete`, `CurrentItem`.

---

### ISyncService

**Namespace**: `AgentX.Core.Services.Sync` | **Implementation**: `SyncService` (uses `ISyncTransport`, `ISyncPackageCodec`, `ISyncConflictResolver`)

Collaborative Sync between Agent-X installations through a shared folder (a network share, OneDrive, a USB drive and so on). Each pass exports local changes to an encrypted `agentx-sync-{deviceId}-{timestamp}.axs` file and imports the other installations' files. What is exchanged: document records (the files themselves are not copied; a record whose file path does not exist on this computer is saved as failed), collections, tags, conversations (title and other descriptive fields; messages are not synced), annotations and system prompts. With the scope `SelectedCollections` only the chosen collections, their documents and those documents' annotations are exported. Each change is matched to a local row by a natural key, never by id, and settled by last writer wins on the modification time, with a device-id tie-break.

| Member | Description |
|--------|-------------|
| `SyncStatus Status` | `LastSyncAt`, `SyncState`, `ErrorMessage`, `PendingChanges`, `LastSyncDurationMs`. |
| `event Action<SyncStatus>? StatusChanged` | Raised on a pool thread when the status changes. |
| `Task ConfigureAsync(SyncConfiguration config)` | Saves the configuration: `SyncFolderPath`, `EncryptionKey` (the passphrase, identical on every installation), `AutoSyncEnabled`, `SyncIntervalMinutes` (30), `SyncScope` (`All` or `SelectedCollections`), `SelectedCollectionIds`. |
| `Task<SyncConfiguration?> GetConfigurationAsync()` | The saved configuration, or null before setup. |
| `Task<SyncChangeSet> ExportChangesAsync(DateTime? since = null, CancellationToken ct = default)` | Collects changes since `since` (all when null), encrypts them and writes the file. |
| `Task<int> ImportChangesAsync(SyncChangeSet changeSet, CancellationToken ct = default)` | Applies a peer's change set; each change is saved on its own, so one bad change does not affect the others. Returns the number applied. |
| `Task<SyncRunResult> SyncNowAsync(CancellationToken ct = default)` | "Sync Now": exports changes since the saved export watermark, then imports every peer file. Waits for a pass already running. |
| `Task<SyncRunResult> ImportNowAsync(CancellationToken ct = default)` | Imports peer files without exporting. A file is renamed to `.imported` only when all its changes were applied or deliberately discarded; otherwise it is retried next time. |
| `Task<IReadOnlyList<SyncConflict>> DetectConflictsAsync(SyncChangeSet incoming)` | The incoming changes an import would discard because the local copy is newer (each resolved `KeepLocal`). |
| `Task ResolveConflictAsync(SyncConflict conflict, SyncResolution resolution)` | `KeepRemote` overwrites the newer local copy. |
| `Task<IReadOnlyList<SyncLogEntity>> GetSyncHistoryAsync(int limit = 20)` | Sync log, newest first. |
| `Task StartAutoSyncAsync(CancellationToken ct = default)` | Starts (or replaces) the auto-sync loop; does nothing when sync is not configured or auto-sync is off. |
| `Task ResumeAutoSyncAsync(CancellationToken ct = default)` | Startup: restores the last sync time and starts auto-sync (soon after launch) when it is on. |
| `Task StopAutoSyncAsync()` | Stops the loop and waits for a running cycle. |
| `bool IsAutoSyncRunning` | Whether the loop runs. |

`SyncRunResult`: `ExportedChanges`, `PeerFilesFound`, `PeerFilesImported`, `PeerFilesPendingRetry`, `PeerFilesUnreadable`, `ChangesApplied`, `ConflictsResolved`, `ChangesRejected`, `ChangesFailed`, `Errors`, `HasProblems`.

Supporting interfaces (namespaces `AgentX.Core.Services.Sync.Transport`, `.Codec`, `.ConflictResolution`):

| Interface | Members |
|-----------|---------|
| `ISyncTransport` (`SyncTransport`) | `WriteSyncFileAsync(string syncFolderPath, string deviceId, DateTime exportedAt, byte[] data, CancellationToken ct)`, `ReadPeerFilesAsync(string syncFolderPath, string localDeviceId, CancellationToken ct)` (the `.axs` files of other devices as `SyncFilePayload`: `FilePath`, `Data`, `FileName`), `MarkFileImportedAsync(string filePath)`, `EnsureFolderExists(string syncFolderPath)`. |
| `ISyncPackageCodec` (`SyncPackageCodec`) | `Serialise(SyncChangeSet)` and `Deserialise(byte[])` (UTF-8 JSON), `Encrypt(byte[] plaintext, string passphrase)` and `Decrypt(byte[] cipherData, string passphrase)` (AES-256-GCM, key from PBKDF2 with 100,000 iterations; `Decrypt` throws `CryptographicException` for a wrong passphrase or damaged data), `IsValidHeader(byte[] data)`. |
| `ISyncConflictResolver` (`SyncConflictResolver`) | `Decide(SyncChange remoteChange, string remoteDeviceId, DateTime? localModifiedAt, string localDeviceId)`, `DetectConflictsAsync(SyncChangeSet incoming, string localDeviceId, Func<SyncChange, Task<SyncLocalVersion?>> getLocalVersion)`, `ResolveConflict(SyncConflict conflict, SyncResolution resolution)`. |

---

### Database and migrations

**Namespace**: `AgentX.Core.Data` | **Registered**: `AgentXDbContext`, `IMigrationRunner` (`MigrationRunner`), `IEncryptedConnectionFactory` (`EncryptedConnectionFactory`)

| Member | Description |
|--------|-------------|
| `AgentXDbContext` | The one EF Core context the services share, over `%LOCALAPPDATA%\AgentX\agentx.db`. `EnterDatabaseGate()` enters the gate that serializes every operation on it (raw ADO.NET work on its connection must hold the gate); `SaveChanges` runs under the gate and discards the pending changes of a save that fails, so a rejected change cannot fail every later save; `EnsureKeyApplied()` opens the connection with the current database key (startup step 0). |
| `Task<MigrationResult> IMigrationRunner.RunAsync(CancellationToken cancellationToken = default)` | Creates the database when it is missing and applies pending migrations; `MigrationResult(bool DatabaseCreated, IReadOnlyList<string> AppliedMigrations, IReadOnlyList<string> AlreadyApplied, string DatabasePath)`. Startup step 1; a failure puts the app in its recovery state. |
| `Task<IReadOnlyList<string>> IMigrationRunner.GetPendingMigrationsAsync(CancellationToken cancellationToken = default)` | Names of the migrations not applied yet. |
| `SqliteConnection IEncryptedConnectionFactory.OpenKeyed(string dbPath)`, `void ApplyKey(SqliteConnection openConnection)` | Open a connection with the current key, or apply the key to an open one; nothing is applied while the database is not encrypted. Used by code that opens its own connection: the vector stores and the backup service. |

---

### Database encryption

**Namespace**: `AgentX.Core.Services.Security`

The database can be encrypted at rest with SQLCipher (AES-256) from "Database Encryption" in Settings. The key is a random 32-byte key wrapped with DPAPI for the current Windows user (`KeyStorageMode.DpapiWrapped`) and kept in the marker file `%LOCALAPPDATA%\AgentX\encryption.info.json`, outside the database it unlocks. `UserPassphrase` mode (a passphrase asked at every start, key derived with PBKDF2-HMAC-SHA256 at 600,000 iterations) remains only for databases set up by older builds. Encryption cannot be turned off in the app.

| Interface (implementation) | Members |
|----------------------------|---------|
| `IDatabaseEncryptionManager` (`DatabaseEncryptionManager`) | `bool IsEncryptionEnabled`; `KeyStorageMode? ProvisionedMode`; `Task<bool> EnableEncryptionAsync(CancellationToken ct = default)`: creates the key, releases the shared connection and suspends the vector store, migrates and verifies the file, writes the marker last and reopens the connection with the key. False when already encrypted; on failure the database stays plaintext without a marker and the exception propagates. |
| `IDatabaseEncryptionMigrator` (`DatabaseEncryptionMigrator`) | `Task MigrateToEncryptedAsync(string dbPath, DatabaseKeyMaterial key)` and an overload with `Func<Task>? commitAsync` (run while the plaintext copy still exists; if it throws, the plaintext database is put back); `void RecoverIfNeeded(string dbPath)`, called at startup before any database access, finishes or undoes an interrupted migration and retires a marker whose database is plaintext. |
| `IDatabaseKeyService` (`DatabaseKeyService`) | `GetOrCreateKeyAsync(KeyStorageMode mode, string? passphrase = null)`, `CreateUncommittedKeyAsync(KeyStorageMode mode, string? passphrase = null)` (a key whose marker the caller writes after the migration; throws when a marker exists), `UnlockWithPassphraseAsync(string passphrase)`, `IsProvisionedAsync()`, `GetProvisionedModeAsync()`. |
| `IDatabaseKeyProvider` (`DatabaseKeyProvider`) | `DatabaseKeyMaterial? Current`: the key in use (`HexKey`, `Mode`), or null for a plaintext database. |
| `IEncryptionStateFile` (`EncryptionStateFile`) | `Exists()`, `Read()`, `WriteAsync(EncryptionStateInfo info)`, `Delete()`, `MoveAside()` (renames the marker to `encryption.info.json.stale-<timestamp>` when it claims encryption for a plaintext database). |

---

### IDpapiEncryptionService and ISecurityStatusService

**Namespace**: `AgentX.Core.Services.Security` | **Implementations**: `DpapiEncryptionService`, `SecurityStatusService`

| Member | Description |
|--------|-------------|
| `string IDpapiEncryptionService.Encrypt(string plaintext)` | DPAPI (current user) encryption, returned as Base64 with the prefix `DPAPI:`. Used for API keys, the Local API token and OAuth client secrets in `settings.json`, and for OAuth tokens in the database. |
| `string IDpapiEncryptionService.Decrypt(string ciphertext)` | Reverses `Encrypt`. Throws `InvalidOperationException` without the prefix and `FormatException` for bad Base64. |
| `bool IDpapiEncryptionService.IsEncrypted(string value)` | Whether the value carries the prefix. |
| `bool ISecurityStatusService.AreKeysEncrypted` | Whether API keys are encrypted at rest. |
| `string ISecurityStatusService.GetEncryptionStatusDescription()` | A one-line description for Settings. |

---

## Settings and Platform Services

### ISettingsService

**Namespace**: `AgentX.Core.Services.Settings` | **Implementation**: `SettingsService`

Application settings live in `%LOCALAPPDATA%\AgentX\settings.json` and are cached after the first read. The OpenAI, Anthropic and web search API keys, the Local API token and the two OAuth client secrets are stored DPAPI-encrypted and decrypted in memory, each on its own, so one value that cannot be decrypted (for example one written by another Windows account) is cleared without losing the others. A save is written to a temporary file and swapped in, so a crash cannot leave a truncated file; an unreadable file is copied to `settings.json.corrupt-<timestamp>`.

| Member | Description |
|--------|-------------|
| `Task<AppSettings> GetSettingsAsync()` | The current settings (the cached instance). |
| `Task SaveSettingsAsync(AppSettings settings)` | Validates with `IValidator<AppSettings>` and saves. Clearly invalid values throw `SettingsValidationException` (the cache is dropped, so the next read returns the last saved settings); rules that describe an incomplete setup, such as a cloud provider chosen before its key is pasted, are only logged. |
| `Task<T?> GetValueAsync<T>(string key)` | One top-level `AppSettings` property by name; default when there is no such property. |
| `Task SetValueAsync<T>(string key, T value)` | Sets one top-level property by name and saves; does nothing for an unknown name. |

`AppSettings` groups (defaults in parentheses):

| Group | Properties |
|-------|------------|
| General | `OnboardingCompleted`, `Theme` (`Dark`), `LanguageOverride`, `StoragePath` (`%LOCALAPPDATA%\AgentX`) |
| AI providers | `ActiveProviderId` (`local`), `LocalModelFileName`, `LocalContextSize` (8,192), `LocalGpuLayers` (0), `OllamaEndpoint` (`http://localhost:11434`), `DefaultModel` (`llama3.2`), `EmbeddingModel` (`all-minilm`), `OpenAiApiKey`, `OpenAiEndpoint`, `OpenAiDefaultModel` (`gpt-4o-mini`), `AnthropicApiKey`, `AnthropicEndpoint`, `AnthropicDefaultModel` |
| Generation | `Temperature` (0.7), `MaxTokens` (4,096), `ContextWindow` (8,192), `EnableModelRouting` (false), `ActiveRoutingProfileId` (`balanced`) |
| Documents and search | `ChunkSize` (512), `ChunkOverlap` (50), `TopKResults` (5), `AutoIndexWatchFolders` (true), `EnableHnswIndex` (true), `HnswM` (16), `HnswEfConstruction` (200), `HnswEfSearch` (50), `HnswFallbackThreshold` (10,000) |
| Research Mode | `EnableResearchMode` (false), `WebSearchProvider` (`Brave`), `WebSearchApiKey`, `MaxSearchResults` (10), `SearchCacheTtlMinutes` (60) |
| Other features | `EnableScreenAwareness` (false; no page changes it), `LocalApiEnabled` (true), `LocalApiToken` |
| `OAuth` | `Google` and `Microsoft` (`ClientId`, `ClientSecret`, `RedirectUri`, and `TenantId` for Microsoft), `TokenRefreshBufferMinutes` (5), `AuthTimeoutSeconds` (300) |
| `CalendarConnector` | `EnableCalendarSync` (false), `SyncIntervalMinutes` (15), `DaysPastToSync` (90), `DaysFutureToSync` (30), `ConflictResolution`, `IncludeAttendeeDetails`, `IncludeDescriptions` |
| `EmailConnector` | `EnableEmailSync` (false), `SyncIntervalMinutes` (10), `MessagesPerSync` (50), `DaysBackToSync` (30), `IncludeAttachmentMetadata` (false), and `EnableAiCategorization` and `IncludeBodyContent`, which nothing applies (the connector's own `EmailSyncSettings.IncludeHtmlBody` decides whether bodies are kept) |
| `BackupSchedule` | See [IBackupService](#ibackupservice). |

---

### IPrivacyStatusService

**Namespace**: `AgentX.Core.Services.Privacy` | **Implementation**: `PrivacyStatusService`

Works out, from the settings, what actually leaves the computer, so the app never claims "fully local" while a cloud feature is on. The Dashboard and the privacy lamp use `GetCurrentAsync`; Chat uses `GetChatMessageRecipientsAsync` to show where the next message goes.

| Member | Description |
|--------|-------------|
| `PrivacyStatus Evaluate(AppSettings settings)` | Every enabled surface that sends data out: a hosted AI provider, an Ollama endpoint on another machine, model routing when a cloud key is configured, the configured web search provider (a SearXNG instance counts, because it forwards queries to public engines), and Calendar or Email sync when on. `PrivacyStatus(bool IsFullyLocal, IReadOnlyList<PrivacyDisclosure> Disclosures)`; `PrivacyDisclosure(string Surface, string Detail)`. |
| `Task<PrivacyStatus> GetCurrentAsync(CancellationToken cancellationToken = default)` | `Evaluate` on the saved settings. |
| `IReadOnlyList<PromptRecipient> EvaluateChatMessage(AppSettings settings, string? activeProviderId, bool researchModeOn)` | Where one chat message goes: the AI provider (the given one, or the saved one when null), model routing, and web search only while Research Mode is on for the message and enabled in Settings. Empty when the message stays on the computer. `PromptRecipient(PromptRecipientKind Kind, string? Name)` with kinds `CloudAiProvider`, `RemoteOllama`, `ModelRouting`, `WebSearch`, `SearXng`. |
| `Task<IReadOnlyList<PromptRecipient>> GetChatMessageRecipientsAsync(string? activeProviderId, bool researchModeOn, CancellationToken cancellationToken = default)` | `EvaluateChatMessage` on the saved settings. |

---

### ILocalizationService

**Namespace**: `AgentX.Core.Services.Localization` | **Implementation**: `LocalizationService` (in `AgentX.App`, over the MRT Core resource loader through `IResourceLoaderAdapter`)

UI languages: English (`en-US`), Spanish (`es`), German (`de`), French (`fr`), Japanese (`ja`) and Chinese Simplified (`zh-CN`). The Language setting is saved immediately; every page shows the new language after a restart.

| Member | Description |
|--------|-------------|
| `Task InitializeAsync()` | Applies the saved language and builds the resource loader; awaited at startup before any string is read. |
| `string CurrentLanguage` | The active language code. |
| `IReadOnlyList<LanguageOption> SupportedLanguages` | `Code`, `DisplayName`, `NativeName`, `IsRtl`. |
| `Task SetLanguageAsync(string? languageCode)` | Saves the language (null follows the Windows display language) and applies it to resources loaded from now on. Throws when the settings cannot be saved. |
| `string GetString(string resourceKey)` | A string from `Resources.resw`, falling back to English. |
| `string GetString(string resourceKey, params object[] args)` | The same, formatted. |
| `string FormatPlural(string baseKey, double count, params object[] args)` | Picks `<baseKey>_<category>` by the CLDR plural category of `count` (from `IPluralRuleProvider`), falling back to `<baseKey>_other`; throws when neither exists. |

---

### IFeatureFlagService

**Namespace**: `AgentX.Core.Services.FeatureFlags` | **Implementation**: `FeatureFlagService`

Three flags, all on by default and each checked by one service: `ai.auto_tagging` (`AutoTagService`), `search.caching` (`SearchCacheService`) and `intelligence.duplicate_detection` (`DuplicateDetectionService`). Overrides are stored in the database (`UserSettings`); no page changes them.

| Member | Description |
|--------|-------------|
| `bool IsEnabled(string featureName)` / `bool IsEnabled(string featureName, bool defaultValue)` | The effective state: the stored override, else the flag's default; an unknown flag is false (or `defaultValue`). |
| `Task SetFlagAsync(string featureName, bool enabled)` | Stores an override. |
| `Task ResetFlagAsync(string featureName)` | Removes an override. |
| `IReadOnlyDictionary<string, bool> GetAllFlags()` | Every known flag with its state. |
| `Task InitializeAsync()` | Loads the overrides; called at startup. |

---

### IWorkspaceProfileService

**Namespace**: `AgentX.Core.Services.Workspace` | **Implementation**: `WorkspaceProfileService`

Workspace Profiles page. A profile is a saved preset (a name, description, model id, collection ids and custom settings). Nothing applies a profile: choosing one or marking it as the default does not change the model, the collections in scope or any setting, and no profile is loaded at startup. At most one profile is the default.

| Member | Description |
|--------|-------------|
| `Task<IReadOnlyList<WorkspaceProfileEntity>> GetAllProfilesAsync()` | All profiles, oldest first. |
| `Task<WorkspaceProfileEntity?> GetProfileAsync(long id)` | One profile, or null. |
| `Task<WorkspaceProfileEntity?> GetDefaultProfileAsync()` | The profile marked default, or null. |
| `Task<WorkspaceProfileEntity> CreateProfileAsync(string name, string? description = null)` | Creates a profile. Throws `ArgumentException` for an empty name. |
| `Task UpdateProfileAsync(WorkspaceProfileEntity profile)` | Saves name, description, model, collections and custom settings (not the default mark). Throws `InvalidOperationException` when the profile does not exist. |
| `Task DeleteProfileAsync(long id)` | Deletes a profile. |
| `Task SetDefaultProfileAsync(long id)` / `Task ClearDefaultProfileAsync(long id)` | Moves or removes the default mark. |
| `Task<WorkspaceProfileEntity> DuplicateProfileAsync(long sourceId, string newName)` | Copies a profile (not marked default). |

---

### IShortcutRegistry

**Namespace**: `AgentX.Core.Services.Shortcuts` | **Implementation**: `ShortcutRegistry`

Keyboard shortcuts. `ShortcutCatalog` (in `AgentX.App`) registers the global shortcuts that the Keyboard Shortcuts dialog lists; pages register their own while they are shown, and the input router asks the registry on every key press. A `ShortcutDescriptor` has `Id`, `Label`, `Scope` (global or a page), `Chord` (a list of `KeyChord`), `Handler` and `Category`. `ChordStateMachine` supports multi-step chords such as `Ctrl+K, D` with a one-second window, although no shortcut uses one yet.

| Member | Description |
|--------|-------------|
| `IDisposable Register(ShortcutDescriptor descriptor)` | Registers a shortcut; disposing the token removes it. |
| `IReadOnlyList<ShortcutDescriptor> All()` | Every registered shortcut. |
| `IReadOnlyList<ShortcutDescriptor> ForScope(string scopeName)` | Global shortcuts plus those of one page. |
| `ShortcutDescriptor? FindByPrimaryKey(KeyChord key, string? activeScopeName)` | The shortcut whose first key matches; a page's own shortcut wins over a global one. |
| `event EventHandler? Changed` | Raised when registrations change. |

---

### IValidator and IAppPathService

| Interface | Description |
|-----------|-------------|
| `IValidator<in T>` (`AgentX.Core.Validation`) | `ValidationResult Validate(T instance)`; the result has `IsValid` and `ValidationError(FieldName, Message)` entries. Registered for `AppSettings` (`AppSettingsValidator`), `SyncConfiguration` (`SyncConfigurationValidator`) and `PluginManifest` (`PluginManifestValidator`). |
| `IAppPathService` (`AgentX.Core.Helpers`, `AppPathService`) | `string GetAppDataPath()` (`%LOCALAPPDATA%\AgentX`) and `string GetTempPath()` (a temporary folder under it); both are created when missing. |

---

## Local REST API Services

The routes, request and response shapes of the local REST API are documented in [API_ENDPOINTS.md](../API_ENDPOINTS.md). The browser extension and the Android companion app use it.

### IApiHostService

**Namespace**: `AgentX.Core.Services.Api` | **Implementation**: `ApiHostService`

An `HttpListener` on `http://localhost:9846/`. Every route except `GET /api/extension/health` requires the bearer token (compared in constant time); without a token the host fails closed. The routes are `GET /api/health`, `GET /api/documents`, `GET /api/documents/{id}`, `GET /api/conversations`, `GET /api/conversations/{id}`, `GET /api/collections`, `POST /api/search`, `POST /api/inbox/clip` (a page clipped by the browser extension, saved under the app data folder and added to the [Smart Inbox](#iinboxservice)), `GET /api/auth/check` and `GET /api/extension/health`. A request body over 10 MB (search or clip) is answered with 413.

| Member | Description |
|--------|-------------|
| `bool IsRunning` | Whether the listener runs. |
| `int Port` | The bound port. |
| `string BaseUrl` | For example `http://localhost:9846/`. |
| `Task StartAsync(int port = 9846, string? authToken = null, CancellationToken ct = default)` | Starts listening; no-op when running. When the port cannot be bound, the listener is released, the host stays stopped and the `HttpListenerException` is rethrown. |
| `void SetAuthToken(string? authToken)` | Replaces the token from the next request on, so a regenerated token revokes the old one at once. Null or empty locks every data route. |
| `Task StopAsync(CancellationToken ct = default)` | Stops, letting requests in progress finish; no-op when stopped. |

---

### IApiHostLifecycleService

**Namespace**: `AgentX.App.Services` | **Implementation**: `ApiHostLifecycleService`

Starts the API at startup when "Enable Local API" (`LocalApiEnabled`, on by default) is set, creating the per-install token (`LocalApiToken`, stored DPAPI-encrypted) on first start. The token is shown under Connections in Settings (Show, Copy, Regenerate).

| Member | Description |
|--------|-------------|
| `Task StartAsync(CancellationToken ct = default)` | Starts the listener on port 9846 unless it runs or the API is disabled. |
| `Task StopAsync(CancellationToken ct = default)` | Stops it (at shutdown). |
| `Task ApplySettingsAsync(CancellationToken ct = default)` | Applies saved settings without a restart: stops the listener when the API was turned off, starts it when turned on, and hands a running listener the current token. |

---

## App Services

These live in `AgentX.App` (namespace `AgentX.App.Services` unless noted) and are registered as singletons in `App.xaml.cs`. They depend on WinUI or on the app's pages and are not available to `AgentX.Core` or to plugins.

### IStartupOrchestrator and IStartupGate

**Implementations**: `StartupOrchestrator`, `StartupGate`

| Member | Description |
|--------|-------------|
| `Task<StartupResult> IStartupOrchestrator.RunCriticalStartupAsync(CancellationToken cancellationToken = default)` | Awaits the database migration (`IMigrationRunner.RunAsync`). Only when it succeeds does it open the startup gate and start the Local REST API and then the built-in connectors; a failure of either is logged and does not stop the other. When the migration throws, the gate is failed, nothing data-backed starts, and the result has `IsRecoveryState` set, so the app shows a blocking recovery screen. `StartupResult(bool MigrationSucceeded, bool IsRecoveryState, MigrationResult? MigrationResult, Exception? Failure)`. |
| `bool IStartupGate.IsDataReady` | Whether data-backed reads are allowed. |
| `Task IStartupGate.WaitForDataReadyAsync(CancellationToken cancellationToken = default)` | Completes when the migration has succeeded; throws `OperationCanceledException` when startup failed. The Dashboard awaits it before its first query, because the window shows before the migration ends. |
| `void IStartupGate.SignalDataReady()` / `void IStartupGate.SignalStartupFailed()` | Open or fail the gate (once). |

---

### App Chat Coordinators

**Namespace**: `AgentX.App.ViewModels.Coordinators` | **Implementations**: `ConversationCoordinator`, `MessagingCoordinator`, `VoiceCoordinator`, `BranchingCoordinator`

The Chat page's view model delegates its work to four coordinators and follows their events.

| Interface | Members |
|-----------|---------|
| `IMessagingCoordinator` | `SendMessageAsync(string userContent, long? conversationId, string? systemPrompt, string? modelId, bool isResearchMode)` and an overload with `ChatOrchestrationMode orchestrationMode` (`Standard`, `MultiAgentParallel`, `MultiAgentDebate`); `RegenerateResponseAsync(long conversationId, long userMessageId, string userContent, string? systemPrompt, ChatOrchestrationMode orchestrationMode)`; `StopGenerationAsync()`; `SubmitFeedbackAsync(long messageId, long conversationId, string rating)`; `DeleteMessageAsync(long messageId)`; `bool IsGenerating`; events `TokenReceived`, `StreamingCompleted`, `GenerationError`, `NotificationRequested`. A send creates the conversation when needed (titled with the first 60 characters of the prompt). In `Standard` mode with a connected provider it streams through [IChatService](#ichatservice), which saves both messages; the multi-agent modes run through [IMultiAgentOrchestrator](#imultiagentorchestrator); without a connected provider it streams without saving. With Research Mode on (and enabled in Settings with a search provider configured) web results are added as cited context, and the user is told when none could be added. Results come back as `SendMessageResult` (conversation id and title, response, token count, time, cancelled or error state, persisted message ids, context inspection, web citations). |
| `IConversationCoordinator` | `CreateConversationAsync`, `DeleteConversationAsync` (branches are promoted), `LoadConversationSummaryAsync`, `LoadConversationsAsync`, `TogglePinAsync`, `SetConversationFolderAsync`, `LoadConversationsByFolderAsync`, `LoadFolderNamesAsync`, `SearchConversationsAsync`, `UpdateMessageContentAsync`, `DeleteMessageAndFollowingAsync` (used before an edited prompt is sent again), `LoadMessagesAsync` (messages with feedback ratings and saved web citations); events `ConversationsChanged`, `FolderNamesChanged`. |
| `IBranchingCoordinator` | `BranchFromMessageAsync(long conversationId, long messageId, string? label)`, `LoadBranchTreeAsync(long conversationId)`, `MergeToMainAsync(MergeBranchRequest request)`, `DeleteBranchAsync(long branchConversationId)`; events `BranchTreeChanged`, `NotificationRequested`. |
| `IVoiceCoordinator` | `ToggleRecordingAsync()` (starts recording from the microphone, or stops and returns the transcript), `TranscribeFileAsync(string filePath)`, `IsRecording`, `IsTranscribing`, `StatusMessage`, `SupportedFormats`; events for each state. Transcription uses the `base` model of [ITranscriptionService](#itranscriptionservice); when it is missing, the user is pointed to the Model Manager page. |

---

### Operations services

**Implementations**: `OperationsOverviewService`, `OperationsActionService`, `OperationsDrillInService`

The Operations page ("Unified status for conversation intelligence, sync posture, ingestion backlog, workflow activity, and connectors") and the Dashboard read one snapshot; the Operations page can also run a few repairs and open the record behind a row on its own page.

| Member | Description |
|--------|-------------|
| `Task<OperationsOverviewSnapshot> IOperationsOverviewService.GetSnapshotAsync(CancellationToken ct = default)` | Five cards (`ConversationIntelligence`, `SyncHealth`, `IngestionBacklog`, `WorkflowActivity`, `Connectors`) and preview lists (recent conversation summaries, sync passes, pending inbox items, imported documents, workflow runs, connectors). |
| `IOperationsActionService.EnableConnectorAsync(long pluginId, CancellationToken ct = default)` | "Enable Connector": enables an installed plugin. |
| `IOperationsActionService.GenerateInboxPreviewsAsync(CancellationToken ct = default)` | AI previews for pending inbox items. |
| `IOperationsActionService.ReindexImportedDocumentAsync(long documentId, CancellationToken ct = default)` | "Retry Index" for an imported document. |
| `IOperationsActionService.RefreshConversationSummariesAsync(int maxConversations = 4, CancellationToken ct = default)` | Refreshes stale conversation summaries. |
| `IOperationsActionService.RunManualSyncAsync(CancellationToken ct = default)` | Runs a Collaborative Sync pass. |
| `IOperationsDrillInService.Stage...Request` / `Consume...Request` | One pending request each for a conversation, inbox item, document, workflow run, sync log entry or plugin; the destination page consumes it after navigation and focuses that record. |

Each action returns `OperationsActionResult(bool IsSuccess, string Message)` with the message in the user's language.

**Typed status.** Card and preview texts are written in the user's language, so nothing reads them for logic or color. Each card and preview carries an `OperationsStatusKind` (for example `SyncNotConfigured`, `SyncConflict`, `BacklogWaiting`, `RunFailed`, `ConnectorDisabled`; `Other` for a status taken from the data), and each imported document an `OperationsDocumentHealth` (`None`, `Searchable`, `Processing`, `NeedsAttention`). `OperationsStatusTones.TokenFor` maps them to the tone tokens (`success`, `pending`, `failed`, `running`, `idle`) that color the badges (`StatusToneToken`, `HealthToneToken`), and the Dashboard recommends setup steps from the kinds, so colors and recommendations are the same in every language.

---

### Shell services

| Service | Description |
|---------|-------------|
| `IAppNavigationService` (`AppNavigationService`, extends `INavigationGate`) | `NavigateToPage(string pageKey, object? parameter = null)` (the parameter reaches the page, for example a query for Search or a document for the vault), `ExecuteAction(string actionId)` for commands such as a new conversation or a theme toggle, `CurrentPage`, event `PageChanged`; `SuppressNavigation` and `EnsureNavPaneVisible()` while the first-run wizard owns the window. Used by shortcuts, the command palette, Jump-To and the tray menu. |
| `IOnboardingService` (`OnboardingService`) | The first-run wizard: `ShouldShowOnboardingAsync()`, `BeginOnboarding()`, `OnNavigatedAsync(bool destinationIsOnboarding)` (leaving the wizard any way other than Finish ends it like Skip, and it does not return on the next start), `SkipOnboardingAsync()`, `CompleteOnboardingAsync()` (saves `OnboardingCompleted`), `IsOnboardingActive`. |
| `IThemeService` (`ThemeService`) | `CurrentTheme`, `InitializeAsync()` (reads the saved theme at startup), `SetThemeAsync(ElementTheme theme)` (applies and saves), `ApplyTheme(ElementTheme theme)`. |
| `IChromeService` (`ChromeService`) | `ConfigureWindow` (title, and a first-launch size fitted to the screen), `ConfigureTitleBar` (content extended into the title bar, caption buttons painted from the theme and repainted when the theme or high contrast changes), `ConfigureBackdrop` (Mica Alt, then Acrylic, then a plain fallback). |
| `IStatusBarService` (`StatusBarService`) | Polls the AI connection, active model, indexing queue and document count (every 30 seconds, first after 5) and raises `StateChanged` with `StatusBarState(IsConnected, ConnectionStatus, ActiveModelName, IsIndexing, IndexingQueueLength, DocumentCount)`. |
| `IAnnunciatorService` (`AnnunciatorService`) | Polls the annunciator lamps (every 30 seconds, first after 6; history queries every fourth cycle) and raises `StateChanged` with `AnnunciatorState(InboxPendingCount, SyncConfigured, SyncState, JobsRunning, JobsLastRunFailed, LastBackupUtc)`. Values come from typed queries, never display text; a failing source keeps its last reading. |
| `INotificationService` (`NotificationService`) | In-app toasts: `Show(title, message, severity, durationMs)`, `ShowSuccess`, `ShowError`, `ShowWarning`, `ShowInfo`, `Dismiss(id)`, and the `Notifications` collection (at most 5 visible; each dismisses itself after its duration). |
| `IWorkflowLaunchService` (`WorkflowLaunchService`) | `StageRequest(WorkflowLaunchRequest request)` / `ConsumePendingRequest()`: the Knowledge Vault and Search hand text (`InputText`, `SourceLabel`, `RecommendedWorkflowName`) to the Workflow Builder, which picks it up after navigation. |
| `SystemTrayService` (class) | The tray icon (Open Agent-X, Quick Chat, Settings, Exit), `MinimizeToTray` (closing the window hides it to the tray), the tooltip, and the global hotkey Win+Shift+A, which opens Quick Chat. |
| `IBuiltinConnectorLifecycleService` (`BuiltinConnectorLifecycleService`) | `InitializeAsync`, `RefreshAsync` and `StopAsync` for the built-in [Calendar](#calendar-connector) and [Email](#email-connector) connectors: each refresh stops them, initializes both with a context whose services are `IOAuthService` and `IInboxService` (data folder `%LOCALAPPDATA%\AgentX\Plugins\{id}\data`), and activates each only when its sync switch is on. Started by the startup orchestrator, refreshed when a connector page saves, stopped at shutdown (at most 15 seconds). |
| `IApiHostLifecycleService` (`ApiHostLifecycleService`) | See [Local REST API Services](#local-rest-api-services). |
| `ShortcutCatalog`, `ChordStateMachine` | See [IShortcutRegistry](#ishortcutregistry). |
| `LocalizationService`, `WinUIResourceLoaderAdapter` | See [ILocalizationService](#ilocalizationservice). |

---

## Models and Data Structures

The shared types the services above take and return. Models of a single service are described with that service.

### AI models

**Namespace**: `AgentX.Core.AI.Models`

| Type | Members |
|------|---------|
| `ChatMessage` | `Role` (`system`, `user`, `assistant` or `tool`), `Content`, `Timestamp` (UTC), `ToolCalls`, `ToolCallId`; factories `User(content)`, `Assistant(content)`, `System(content)`, `AssistantWithTools(toolCalls)`, `ToolResult(toolCallId, content)`. |
| `ChatOptions` | `ModelId` (null uses the active model), `Temperature` (0.7), `MaxTokens` (2,048), `ContextWindow` (4,096), `TopP` (0.9), `FrequencyPenalty`, `PresencePenalty`, `StopSequences`, `ResponseFormat` (`Text` or `JsonObject`, which uses the provider's JSON mode), `CacheSystemPrompt` and `SystemPromptBlocks` (prompt caching, used by Anthropic; `SystemPromptBlock(string Text, bool Cacheable)`), `JsonSchema` and `JsonSchemaName` (strict structured output, used by OpenAI), and `Tools`, `ForceToolCall`, `ForceToolName`. |
| `AiModel` | `Id`, `Name`, `ProviderId`, `Family`, `IsAvailable`, `SizeBytes`, `SizeFormatted`, `QuantizationLevel`, `ParameterCount`, `ContextLength`, `ModifiedAt`, `Digest`. |
| `ModelDownloadProgress` | `ModelId`, `Status`, `CompletedBytes`, `TotalBytes`, `PercentComplete`. |
| `HardwareCapability` | `GpuName`, `GpuVramBytes`, `HasNpu`, `NpuName`, `CpuCores`, `CpuName`, `TotalRamBytes`, `AvailableRamBytes`, formatted sizes; `RecommendedMaxModelParameters` from available RAM (`3B` below 4 GB, `7B` below 8 GB, `13B` below 16 GB, `34B` below 32 GB, else `70B+`); `IsNvidiaGpu`; `RecommendedGpuLayers` from VRAM on an NVIDIA GPU (0 below 2 GB, 16 below 4 GB, 28 below 6 GB, else 33; 0 without an NVIDIA GPU). |
| `ToolCall`, `ToolDefinition` | Tool-calling shapes (`Id`, `Name`, `Arguments`; `Name`, `Description`, `Parameters`, `Handler`). |

Tool calling is not implemented: no provider sends `Tools` to a model, so `Tools`, `ForceToolCall`, `ForceToolName`, the tool factories and `ToolCalls` have no effect today.

---

### Search models

**Namespace**: `AgentX.Core.Search.Models`

| Type | Members |
|------|---------|
| `SearchQuery` | `QueryText` (required), `TopK` (10), `MinScore` (0.3), `CollectionId`, `FileTypeFilter` (for example `pdf`), `CreatedAfter` and `CreatedBefore` (the document's import time, UTC), `Mode` (`SearchMode.Semantic` by default; `Keyword`, `Hybrid`). |
| `SearchResult` | `ChunkId`, `DocumentId`, `FileName`, `FilePath`, `FileType`, `PageNumber`, `ChunkIndex`, `MatchedText`, `Excerpt` (a shorter display excerpt), `Score` (0 to 1; its scale depends on the mode, see [Search Services](#search-and-rag-services)), `RelevancePercent`, `CollectionNames`. |
| `RagResponse` | `AnswerText`, `Question`, `Citations`, `ContextChunksUsed`, `IsStreaming`, `TotalLatencyMs`, `SearchLatencyMs`, `CollectionScope`, `EvalMetrics` (`RagEvalMetrics`: `ContextRelevance`, `Faithfulness`, `AnswerRelevance`, `OverallScore` weighted 0.3, 0.4 and 0.3, `IsDefault`, `DefaultReason`; null until the evaluator has run), `WebCitations` (Research Mode). |
| `Citation` | `Number`, `DocumentId`, `ChunkId`, `FileName`, `FilePath`, `PageNumber`, `ChunkIndex`, `Excerpt`, `RelevanceScore`. |
| `WebCitation` | `Title`, `Url`, `Snippet`, `Source` (`Vault` or `Web`), `DocumentName`. |

---

### Document models

**Namespace**: `AgentX.Core.Documents.Models` (`DuplicateCheckResult` in `AgentX.Core.Documents`)

| Type | Members |
|------|---------|
| `ProcessedDocument` | What a processor returns: `FilePath`, `FileName`, `FileType`, `FileSizeBytes`, `ContentHash`, `ExtractedText`, `ExtractedTitle`, `PageCount`, `WordCount`, `Language`, `Metadata`, `Chunks`. |
| `DocumentMetadata` | `Author`, `Subject`, `CreatedDate`, `ModifiedDate`, `Custom` (string pairs). |
| `DocumentChunk` | `Index`, `Content`, `StartCharOffset`, `EndCharOffset`, `PageNumber`, `SectionTitle`, `TokenCount`, `Embedding`. |
| `DuplicateCheckResult` | From `IDocumentService.CheckForDuplicateAsync`: `IsDuplicate`, `IsExactMatch`, `ExistingDocumentId`, `ExistingFileName`, `MatchScore`. |

---

### Indexing events

**Namespace**: `AgentX.Core.Services.Indexing`

| Type | Members |
|------|---------|
| `IndexingProgressEventArgs` | `QueueLength`, `Processed`, `CurrentDocument`, `PercentComplete`. |
| `DocumentIndexingFailedEventArgs` | `DocumentId`, `Error`. |

---

### Intelligence models

**Namespace**: `AgentX.Core.Services.Intelligence.Models`

| Type | Members |
|------|---------|
| `KnowledgeGraphData` | `Nodes`, `Edges`, `DocumentCount`, `CollectionCount`, `TagCount`. |
| `GraphNode` | `Id`, `Label`, `NodeType` (`Document`, `Collection`, `Tag`), `ColorHex`, `Size`, `X`, `Y`, `Vx`, `Vy` (layout position and velocity), `EntityId`, `Subtitle`, `ConnectionCount`. |
| `GraphEdge` | `SourceId`, `TargetId`, `Label`, `Weight`, `ColorHex`. |
| `DuplicateGroup` | `ContentHash`, `MatchKind` (`Exact` or `Semantic`), `Documents`, `WastedStorageBytes` (the size of every copy after the first). |
| `DuplicateDocument` | `DocumentId`, `FileName`, `FilePath`, `FileSizeBytes`, `ImportedAt`, `Evidence` (`DuplicateEvidence` for a near duplicate: `SupportingChunkCount`, `MaxSimilarity`, `AverageSimilarity`, `Confidence`). |

---

## Quick Reference

Every service registered in `App.xaml.cs`, with the class registered for it and where it is described.

| Service | Implementation | Section |
|---------|----------------|---------|
| `IAiService` | `AiService` | [IAiService](#iaiservice) |
| `IModelManager` | `ModelManager` | [IModelManager](#imodelmanager) |
| `IBuiltInModelBootstrap` | `BuiltInModelBootstrap` | [IBuiltInModelBootstrap](#ibuiltinmodelbootstrap) |
| `IHardwareDetector` | `HardwareDetector` | [IHardwareDetector](#ihardwaredetector) |
| `ITokenCounter` | `TokenCounter` | [ITokenCounter](#itokencounter) |
| `IContextWindowManager` | `ContextWindowManager` | [IContextWindowManager](#icontextwindowmanager) |
| `IEmbeddingService` | `CachedEmbeddingService` over `EmbeddingService` | [IEmbeddingService](#iembeddingservice) |
| `ICostTracker` | `CostTracker` | [ICostTracker](#icosttracker) |
| `IContextAssemblyService` | `ContextAssemblyService` | [IContextAssemblyService, ISemanticContextSelector, IConversationCompressionService](#icontextassemblyservice-isemanticcontextselector-iconversationcompressionservice) |
| `ISemanticContextSelector` | `SemanticContextSelector` | [IContextAssemblyService, ISemanticContextSelector, IConversationCompressionService](#icontextassemblyservice-isemanticcontextselector-iconversationcompressionservice) |
| `IConversationCompressionService` | `ConversationCompressionService` | [IContextAssemblyService, ISemanticContextSelector, IConversationCompressionService](#icontextassemblyservice-isemanticcontextselector-iconversationcompressionservice) |
| `IModelRouterService` | `ModelRouterService` | [IModelRouterService and ITaskTypeDetector](#imodelrouterservice-and-itasktypedetector) |
| `ITaskTypeDetector` | `TaskTypeDetector` | [IModelRouterService and ITaskTypeDetector](#imodelrouterservice-and-itasktypedetector) |
| `IMultiAgentOrchestrator` | `MultiAgentOrchestrator` | [IMultiAgentOrchestrator](#imultiagentorchestrator) |
| `IChatService` | `ChatService` | [IChatService](#ichatservice) |
| `IConversationService` | `ConversationService` | [IConversationService](#iconversationservice) |
| `IConversationBranchService` | `ConversationBranchService` | [IConversationBranchService](#iconversationbranchservice) |
| `IConversationMemoryService` | `ConversationMemoryService` | [IConversationMemoryService](#iconversationmemoryservice) |
| `ISemanticMemoryService` | `SemanticMemoryService` | [ISemanticMemoryService](#isemanticmemoryservice) |
| `IConversationRecallService` | `ConversationRecallService` | [IConversationRecallService](#iconversationrecallservice) |
| `IConversationSummaryService` | `ConversationSummaryService` | [IConversationSummaryService](#iconversationsummaryservice) |
| `ISystemPromptService` | `SystemPromptService` | [ISystemPromptService](#isystempromptservice) |
| `IFeedbackService` | `FeedbackService` | [IFeedbackService](#ifeedbackservice) |
| `IDocumentService` | `DocumentService` | [IDocumentService](#idocumentservice) |
| `IDocumentProcessor` | Eight built-in processors | [IDocumentProcessor](#idocumentprocessor) |
| `IChunkingService` | `ChunkingService` | [IChunkingService](#ichunkingservice) |
| `IAdaptiveChunkingService` | `AdaptiveChunkingService` | [IAdaptiveChunkingService](#iadaptivechunkingservice) |
| `IIndexingService` | `IndexingService` | [IIndexingService](#iindexingservice) |
| `IFileWatcherService` | `FileWatcherService` | [IFileWatcherService](#ifilewatcherservice) |
| `ISemanticSearchService` | `SemanticSearchService` | [ISemanticSearchService](#isemanticsearchservice) |
| `IKeywordSearchService` | `KeywordSearchService` | [IKeywordSearchService](#ikeywordsearchservice) |
| `IHybridSearchOrchestrator` | `HybridSearchOrchestrator` | [IHybridSearchOrchestrator](#ihybridsearchorchestrator) |
| `ISearchCacheService` | `SearchCacheService` | [ISearchCacheService](#isearchcacheservice) |
| `IRagPipeline` | `RagPipeline` | [IRagPipeline](#iragpipeline) |
| `ICitationService`, `IRagReranker`, `IMultiQueryGenerator`, `IHydeService`, `ILlmReranker`, `IParentDocumentRetriever`, `IContextualCompressor`, `IRagEvaluator` | One class each | [RAG Pipeline Stages](#rag-pipeline-stages) |
| `IWebSearchService` | `SettingsAwareWebSearchService` | [IWebSearchService](#iwebsearchservice) |
| `IRagConfiguration`, `IRagPromptCatalog` | `RagConfiguration`, `RagPromptCatalog` | [IRagConfiguration and IRagPromptCatalog](#iragconfiguration-and-iragpromptcatalog) |
| `IRagMetrics`, `IPiiDetector` | `RagMetrics`, `PiiDetector` | [IRagMetrics and IPiiDetector](#iragmetrics-and-ipiidetector) |
| `IVectorStore` | `HnswVectorStore` or `SqliteVecStore` | [IVectorStore](#ivectorstore) |
| `ICollectionService` | `CollectionService` | [ICollectionService](#icollectionservice) |
| `IAutoTagService` | `AutoTagService` | [IAutoTagService](#iautotagservice) |
| `IAnnotationService` | `AnnotationService` | [IAnnotationService](#iannotationservice) |
| `ISummaryService` | `SummaryService` | [ISummaryService](#isummaryservice) |
| `IHierarchicalSummaryService` | `HierarchicalSummaryService` | [IHierarchicalSummaryService](#ihierarchicalsummaryservice) |
| `IDuplicateDetectionService`, `IDuplicateEvidenceService` | `DuplicateDetectionService`, `DuplicateEvidenceService` | [IDuplicateDetectionService](#iduplicatedetectionservice) |
| `IOrganizationSuggestionService` | `OrganizationSuggestionService` | [IOrganizationSuggestionService](#iorganizationsuggestionservice) |
| `IKnowledgeGraphService` | `KnowledgeGraphService` | [IKnowledgeGraphService](#iknowledgegraphservice) |
| `IDigestService`, `IDigestInsightService` | `DigestService`, `DigestInsightService` | [IDigestService and IDigestInsightService](#idigestservice-and-idigestinsightservice) |
| `IComparisonService`, `IDocumentSynthesisService` | `ComparisonService`, `DocumentSynthesisService` | [IComparisonService and IDocumentSynthesisService](#icomparisonservice-and-idocumentsynthesisservice) |
| `IConversationThemeClusterService`, `IConversationThemeTrendService` | `ConversationThemeClusterService`, `ConversationThemeTrendService` | [IConversationThemeClusterService and IConversationThemeTrendService](#iconversationthemeclusterservice-and-iconversationthemetrendservice) |
| `IAnalyticsService` | `AnalyticsService` | [IAnalyticsService](#ianalyticsservice) |
| `ITemporalIdentityService` | `TemporalIdentityService` | [ITemporalIdentityService](#itemporalidentityservice) |
| `IVoiceDraftService` | `VoiceDraftService` | [IVoiceDraftService](#ivoicedraftservice) |
| `ITranscriptionService` | `TranscriptionService` | [ITranscriptionService](#itranscriptionservice) |
| `IScreenCaptureService` | `ScreenCaptureService` | [IScreenCaptureService](#iscreencaptureservice) |
| `IWebImportService` | `WebImportService` | [IWebImportService](#iwebimportservice) |
| `IWebScraperService` | `WebScraperService` | [IWebScraperService](#iwebscraperservice) |
| `IWebContentFetcher`, `IJsRenderingService` | `WebContentFetcher`, `JsRenderingService` | [IWebContentFetcher and IJsRenderingService](#iwebcontentfetcher-and-ijsrenderingservice) |
| `IHtmlParser`, `IStructuredDataExtractor` | `HtmlParser`, `StructuredDataExtractor` | [IHtmlParser and IStructuredDataExtractor](#ihtmlparser-and-istructureddataextractor) |
| `IFeedService`, `ISitemapParser` | `FeedService`, `SitemapParser` | [IFeedService and ISitemapParser](#ifeedservice-and-isitemapparser) |
| `IExportService` | `ExportService` | [IExportService](#iexportservice) |
| `IExportFormatter` | Eight formatters | [IExportFormatter](#iexportformatter) |
| `IExportTemplateService` | `ExportTemplateService` | [IExportTemplateService](#iexporttemplateservice) |
| `IWorkflowService` | `WorkflowService` | [IWorkflowService](#iworkflowservice) |
| `IWorkflowEngine` | `WorkflowEngine` | [IWorkflowEngine](#iworkflowengine) |
| `IInboxService` | `InboxService` | [IInboxService](#iinboxservice) |
| `IOAuthService` | `OAuthService` | [IOAuthService](#ioauthservice) |
| `ICalendarService`, `CalendarPlugin` | `CalendarService`, `CalendarPlugin` | [Calendar Connector](#calendar-connector) |
| `IEmailService`, `EmailPlugin` | `EmailService`, `EmailPlugin` | [Email Connector](#email-connector) |
| `IPluginService`, `IPluginDocumentProcessorSource` | `PluginService` | [IPluginService](#ipluginservice) |
| `IBackupService` | `BackupService` | [IBackupService](#ibackupservice) |
| `ISyncService`, `ISyncTransport`, `ISyncPackageCodec`, `ISyncConflictResolver` | `SyncService`, `SyncTransport`, `SyncPackageCodec`, `SyncConflictResolver` | [ISyncService](#isyncservice) |
| `AgentXDbContext`, `IMigrationRunner`, `IEncryptedConnectionFactory` | `AgentXDbContext`, `MigrationRunner`, `EncryptedConnectionFactory` | [Database and migrations](#database-and-migrations) |
| `IDatabaseEncryptionManager`, `IDatabaseEncryptionMigrator`, `IDatabaseKeyService`, `IDatabaseKeyProvider`, `IEncryptionStateFile` | One class each | [Database encryption](#database-encryption) |
| `IDpapiEncryptionService`, `ISecurityStatusService` | `DpapiEncryptionService`, `SecurityStatusService` | [IDpapiEncryptionService and ISecurityStatusService](#idpapiencryptionservice-and-isecuritystatusservice) |
| `ISettingsService` | `SettingsService` | [ISettingsService](#isettingsservice) |
| `IPrivacyStatusService` | `PrivacyStatusService` | [IPrivacyStatusService](#iprivacystatusservice) |
| `ILocalizationService`, `IPluralRuleProvider`, `IResourceLoaderAdapter` | `LocalizationService`, `CldrPluralRuleProvider`, `WinUIResourceLoaderAdapter` | [ILocalizationService](#ilocalizationservice) |
| `IFeatureFlagService` | `FeatureFlagService` | [IFeatureFlagService](#ifeatureflagservice) |
| `IWorkspaceProfileService` | `WorkspaceProfileService` | [IWorkspaceProfileService](#iworkspaceprofileservice) |
| `IShortcutRegistry`, `ShortcutCatalog`, `ChordStateMachine` | `ShortcutRegistry` | [IShortcutRegistry](#ishortcutregistry) |
| `IValidator<T>`, `IAppPathService` | `AppSettingsValidator`, `SyncConfigurationValidator`, `PluginManifestValidator`, `AppPathService` | [IValidator and IAppPathService](#ivalidator-and-iapppathservice) |
| `IApiHostService` | `ApiHostService` | [IApiHostService](#iapihostservice) |
| `IApiHostLifecycleService` | `ApiHostLifecycleService` | [IApiHostLifecycleService](#iapihostlifecycleservice) |
| `IStartupOrchestrator`, `IStartupGate` | `StartupOrchestrator`, `StartupGate` | [IStartupOrchestrator and IStartupGate](#istartuporchestrator-and-istartupgate) |
| `IMessagingCoordinator`, `IConversationCoordinator`, `IBranchingCoordinator`, `IVoiceCoordinator` | One class each | [App Chat Coordinators](#app-chat-coordinators) |
| `IOperationsOverviewService`, `IOperationsActionService`, `IOperationsDrillInService` | One class each | [Operations services](#operations-services) |
| `IAppNavigationService`, `IOnboardingService`, `IThemeService`, `IChromeService`, `IStatusBarService`, `IAnnunciatorService`, `INotificationService`, `IWorkflowLaunchService`, `SystemTrayService`, `IBuiltinConnectorLifecycleService` | One class each | [Shell services](#shell-services) |

---

## Common Usage Patterns

The snippets use the signatures above; in the app, resolve each service with `App.GetService<T>()` or take it in a constructor. `Debug` is `System.Diagnostics.Debug`.

### Import documents and follow indexing

```csharp
var documents = App.GetService<IDocumentService>();
var indexing = App.GetService<IIndexingService>();

indexing.DocumentIndexed += (_, documentId) =>
    Debug.WriteLine($"Document {documentId} is searchable");
indexing.DocumentIndexingFailed += (_, e) =>
    Debug.WriteLine($"Document {e.DocumentId} failed: {e.Error}");

var report = await documents.ImportFilesWithReportAsync(
    new[] { @"C:\Docs\q3-report.pdf", @"C:\Docs\notes.md" },
    collectionId: null);

foreach (var duplicate in report.Duplicates)
    Debug.WriteLine($"{duplicate.FilePath} is already in the vault");
foreach (var failure in report.Failed)
    Debug.WriteLine($"{failure.FilePath}: {failure.Reason}");
```

Import returns once each document is recorded; chunking and embedding run in the background, and the two events report the outcome on the indexing thread.

### Ask a question of the vault

```csharp
var rag = App.GetService<IRagPipeline>();

var answer = await rag.AskAsync(
    "What did the Q3 report say about churn?",
    collectionId: null,
    onToken: token => Debug.Write(token));

foreach (var citation in answer.Citations)
    Debug.WriteLine($"[{citation.Number}] {citation.FileName}, page {citation.PageNumber}");
```

### Chat with streaming

```csharp
var conversations = App.GetService<IConversationService>();
var chat = App.GetService<IChatService>();

var conversation = await conversations.CreateConversationAsync(title: "Launch planning");

await foreach (var token in chat.SendMessageAsync(conversation.Id, "Summarize my launch notes."))
    Debug.Write(token);
```

`IChatService` saves the prompt and the reply; see [IChatService](#ichatservice) for the context it assembles.

### Search documents

```csharp
var search = App.GetService<IHybridSearchOrchestrator>();

var results = await search.SearchAsync(new SearchQuery
{
    QueryText = "vendor contract renewal",
    Mode = SearchMode.Hybrid,
    TopK = 10,
});

foreach (var result in results)
    Debug.WriteLine($"{result.RelevancePercent}% {result.FileName}: {result.Excerpt}");
```

### Draft in the user's voice

```csharp
var drafts = App.GetService<IVoiceDraftService>();

var draft = await drafts.StartDraftAsync(
    new VoiceDraftRequest("A reply to the team about moving the release", Goal: null, At: null), ct);
if (draft is null)
    return; // no AI provider is available

await foreach (var piece in draft.Text.WithCancellation(ct))
    Debug.Write(piece);
```

### From a plugin

A plugin has no `App.GetService`; it resolves `IInboxService` from `IPluginContext.Services`. The [Plugin Development Guide](PLUGIN-DEVELOPMENT-GUIDE.md) has a complete data connector example.

---

**Last Updated**: 2026-09-27
**Version**: 2.2.0
