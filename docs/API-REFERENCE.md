# Agent-X Service API Reference

**Application:** Agent-X
**Platform:** Windows Desktop (.NET 8 / WinUI 3)
**Core Library:** `AgentX.Core`
**Last Updated:** 2026-09-27

This reference describes the public service interfaces and models of `AgentX.Core` that the app
and plugins build on, with the signatures as they are in the code. Every service is registered as
a singleton in `src/AgentX.App/App.xaml.cs`. The HTTP routes of the local REST API are documented
in [`API_ENDPOINTS.md`](../API_ENDPOINTS.md).

---

## Table of Contents

1. [AI Services](#1-ai-services)
   - [IAiService](#iaiservice)
   - [IAiProvider](#iaiprovider)
   - [IHardwareDetector](#ihardwaredetector)
   - [IModelManager](#imodelmanager)
   - [IEmbeddingService](#iembeddingservice)
   - [ICostTracker](#icosttracker)
2. [AI Models](#2-ai-models)
   - [ChatMessage](#chatmessage)
   - [ChatOptions](#chatoptions)
   - [AiModel](#aimodel)
   - [HardwareCapability](#hardwarecapability)
   - [ModelDownloadProgress](#modeldownloadprogress)
   - [EmbeddingTarget and EmbeddingTargetResolver](#embeddingtarget-and-embeddingtargetresolver)
3. [Document Services](#3-document-services)
   - [IDocumentService](#idocumentservice)
   - [IDocumentProcessor](#idocumentprocessor)
   - [IChunkingService](#ichunkingservice)
   - [Document Models](#document-models)
4. [Search and RAG](#4-search-and-rag)
   - [ISemanticSearchService](#isemanticsearchservice)
   - [IKeywordSearchService](#ikeywordsearchservice)
   - [IHybridSearchOrchestrator](#ihybridsearchorchestrator)
   - [IRagPipeline](#iragpipeline)
   - [ICitationService](#icitationservice)
   - [Search Models](#search-models)
5. [Vector Store](#5-vector-store)
   - [IVectorStore](#ivectorstore)
   - [VectorSearchResult](#vectorsearchresult)
6. [Chat Services](#6-chat-services)
   - [IChatService](#ichatservice)
   - [IConversationService](#iconversationservice)
   - [ISystemPromptService](#isystempromptservice)
7. [Collections and Tags](#7-collections-and-tags)
   - [ICollectionService](#icollectionservice)
   - [IAutoTagService](#iautotagservice)
8. [Indexing Services](#8-indexing-services)
   - [IIndexingService](#iindexingservice)
   - [IFileWatcherService](#ifilewatcherservice)
   - [Indexing Event Data](#indexing-event-data)
9. [Settings](#9-settings)
   - [ISettingsService](#isettingsservice)
   - [AppSettings](#appsettings)
10. [Intelligence Services](#10-intelligence-services)
    - [ISummaryService](#isummaryservice)
    - [IDuplicateDetectionService](#iduplicatedetectionservice)
    - [IOrganizationSuggestionService](#iorganizationsuggestionservice)
    - [Intelligence Models](#intelligence-models)
11. [Database Entities](#11-database-entities)
    - [ConversationEntity](#conversationentity)
    - [MessageEntity](#messageentity)
    - [DocumentEntity](#documententity)
    - [DocumentChunkEntity](#documentchunkentity)
    - [CollectionEntity](#collectionentity)
    - [DocumentCollectionEntity](#documentcollectionentity)
    - [TagEntity](#tagentity)
    - [DocumentTagEntity](#documenttagentity)
    - [SearchHistoryEntity](#searchhistoryentity)
    - [SystemPromptEntity](#systempromptentity)
    - [UserSettingsEntity](#usersettingsentity)
    - [WatchFolderEntity](#watchfolderentity)
    - [IndexingJobEntity](#indexingjobentity)
    - [OAuthCredentialEntity](#oauthcredentialentity)
12. [OAuth Services](#12-oauth-services)
    - [IOAuthService](#ioauthservice)
    - [OAuthService](#oauthservice)
    - [OAuthProviderConfig](#oauthproviderconfig)
    - [OAuthProviderRegistry](#oauthproviderregistry)
    - [OAuthCredential](#oauthcredential)
13. [Calendar Connector](#13-calendar-connector)
    - [ICalendarService](#icalendarservice)
    - [ICalendarProvider](#icalendarprovider)
    - [CalendarPlugin](#calendarplugin)
    - [Calendar Models](#calendar-models)
14. [Email Connector](#14-email-connector)
    - [IEmailService](#iemailservice)
    - [IEmailProvider](#iemailprovider)
    - [EmailPlugin](#emailplugin)
    - [Email Models](#email-models)
15. [Plugin Infrastructure](#15-plugin-infrastructure)
    - [IPlugin](#iplugin)
    - [IPluginContext](#iplugincontext)
    - [PluginType](#plugintype)
    - [IDocumentProcessorPlugin](#idocumentprocessorplugin)
    - [IPluginDocumentProcessorSource](#iplugindocumentprocessorsource)
16. [Local REST API Host](#16-local-rest-api-host)
    - [IApiHostService](#iapihostservice)
    - [LocalApiSecurity](#localapisecurity)
    - [API Models](#api-models)
17. [Database Encryption](#17-database-encryption)
    - [IDatabaseEncryptionManager](#idatabaseencryptionmanager)
18. [Draft As Me](#18-draft-as-me)
    - [IVoiceDraftService](#ivoicedraftservice)

---

## 1. AI Services

### IAiService

```csharp
namespace AgentX.Core.AI;

public interface IAiService : IDisposable
```

High-level AI service that owns the providers, keeps the active provider and model, and adds
application operations such as summarization and tagging.

**Namespace:** `AgentX.Core.AI`
**Assembly:** `AgentX.Core`
**Implementation:** `AiService` (sealed)

The implementation publishes its provider state (the registered providers, the active provider,
the active model and the connection flag) as one immutable snapshot, so a caller always sees a
provider and a model that belong together, also while the service re-initializes. `AiService`
takes an optional `ICostTracker`, which it hands to the Ollama, OpenAI and Anthropic providers so
they record token usage.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ActiveProvider` | `IAiProvider` | The active provider. Throws `InvalidOperationException` when the service is not initialized or no provider could be registered. |
| `IsConnected` | `bool` | Whether the active provider answered its last connection check. |
| `ActiveModelId` | `string` | The model used for requests that do not name one. Set to the active provider's default model at initialization and when the provider changes; `SetActiveModelAsync` changes it. |
| `RegisteredProviderIds` | `IReadOnlyCollection<string>` | The ids of the registered providers (`local`, `ollama`, `openai`, `anthropic`). |

#### Methods

---

##### InitializeAsync

```csharp
Task InitializeAsync(CancellationToken ct = default);
```

Builds the providers from the saved settings and activates the preferred one. It is called at
startup and again after Settings are saved.

**Behavior:**
- Registers the built-in provider (`local`) always; Ollama (`ollama`) when
  `AppSettings.OllamaEndpoint` is an absolute `http` or `https` URL (otherwise a warning is logged
  and Ollama stays unavailable); OpenAI (`openai`) and Anthropic (`anthropic`) only when their API
  keys are set. A provider that fails to construct is skipped with a warning.
- A provider whose configuration did not change keeps its instance, so a settings save does not
  reload the built-in model or cut off a response that is streaming. Replaced providers are
  disposed after the calls running on them finish.
- Activates `AppSettings.ActiveProviderId` (default `local`). When that provider is not
  registered, it falls back to the built-in provider if its model file is installed, then Ollama,
  then any registered provider.
- Checks the active provider's connection. `IsConnected` reflects the result; a provider that does
  not answer is still activated (offline mode).
- Sets `ActiveModelId` to the provider's default model: `LocalModelFileName` for `local`,
  `OpenAiDefaultModel` (`gpt-4o-mini`) for `openai`, `AnthropicDefaultModel` (`claude-sonnet-5`)
  for `anthropic`, and `DefaultModel` (`llama3.2`) for `ollama`.
- The new state is built completely before it replaces the old one, so an invalid setting never
  leaves the service without a provider.

**Exceptions:** Rethrows unexpected errors (for example a settings read failure) after logging
them. `ObjectDisposedException` after `Dispose`.

---

##### SwitchProviderAsync

```csharp
Task<bool> SwitchProviderAsync(string providerId, CancellationToken ct = default);
```

Makes a registered, reachable provider the active one. The active model becomes that provider's
default model, so a model id of the previous provider is never sent to the new one.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `providerId` | `string` | -- | The provider id (for example `"ollama"`). Matched without regard to case. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** `true` when the switch happened. `false` when the provider is not registered or not
reachable; the previous provider then stays active.

**Behavior:** The connection check reuses a recent result: a success for 60 seconds, a failure
for 15 seconds.

**Exceptions:** `ArgumentException` when `providerId` is null or whitespace;
`ObjectDisposedException`.

---

##### SetActiveModelAsync

```csharp
Task SetActiveModelAsync(string modelId, CancellationToken ct = default);
```

Sets `ActiveModelId` at once and saves the choice in the active provider's own setting:
`DefaultModel` for Ollama, `OpenAiDefaultModel` for OpenAI, `AnthropicDefaultModel` for Anthropic.
For the built-in provider the choice lasts for this session only, because its configured model
file is also the embedding model. A failure to save is logged as a warning; the in-memory value is
kept.

**Exceptions:** `ArgumentException` when `modelId` is null or whitespace;
`ObjectDisposedException`.

---

##### GetProvider

```csharp
IAiProvider? GetProvider(string providerId);
```

Returns the registered provider with this id, or `null` when it is not registered (for example a
cloud provider without an API key) or the id is blank.

---

##### IsProviderAvailableAsync

```csharp
Task<bool> IsProviderAvailableAsync(string providerId, CancellationToken ct = default);
```

Checks whether a registered provider answers. Recent results are reused (60 seconds after a
success, 15 seconds after a failure), so model routing does not probe or bill a provider on every
message. Returns `false` for an unregistered provider.

---

##### GetDefaultModelId

```csharp
string GetDefaultModelId(string providerId);
```

Returns the model a provider uses when it becomes active, from the current settings (see
`InitializeAsync`). Throws `ArgumentException` for a blank id.

---

##### ResolveEmbeddingTarget

```csharp
EmbeddingTarget ResolveEmbeddingTarget();
```

Returns the provider and model that produce embeddings, chosen independently of the chat provider
by [`EmbeddingTargetResolver`](#embeddingtarget-and-embeddingtargetresolver) from the Embedding
Model setting and the state of the built-in model file.

---

##### StreamChatAsync

```csharp
IAsyncEnumerable<string> StreamChatAsync(
    IReadOnlyList<ChatMessage> messages,
    string? systemPrompt = null,
    ChatOptions? options = null,
    CancellationToken ct = default);
```

Streams a chat completion from the active provider, token by token.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `messages` | `IReadOnlyList<ChatMessage>` | -- | The conversation history. |
| `systemPrompt` | `string?` | `null` | When set, a `system` message with this text is put in front of the history. |
| `options` | `ChatOptions?` | `null` | Inference options. When `null` or when `ModelId` is empty, a copy carrying `ActiveModelId` is used; the caller's object is never modified. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** The generated text, piece by piece. The provider and model are taken from one
snapshot, so they stay together even if the service re-initializes during the stream.

**Exceptions:** `InvalidOperationException` when there is no active provider;
`ObjectDisposedException`; provider errors (for example `HttpRequestException`) propagate.

---

##### ChatAsync

```csharp
Task<string> ChatAsync(
    IReadOnlyList<ChatMessage> messages,
    string? systemPrompt = null,
    ChatOptions? options = null,
    CancellationToken ct = default);
```

Same as `StreamChatAsync`, returning the complete answer. Provider errors are logged and rethrown.

---

##### SummarizeAsync

```csharp
Task<string> SummarizeAsync(string content, CancellationToken ct = default);
```

Asks the active model for a summary of `content` with a built-in instruction (key points and main
ideas, at most 2-3 paragraphs).

**Exceptions:** `ArgumentException` when `content` is null, empty or whitespace;
`ObjectDisposedException`; provider errors propagate.

---

##### GenerateTagsAsync

```csharp
Task<IReadOnlyList<string>> GenerateTagsAsync(
    string content,
    int maxTags = 5,
    CancellationToken ct = default);
```

Asks the active model for up to `maxTags` tags in JSON mode (`ResponseFormat.JsonObject`).

**Behavior:**
- Reads the JSON array between the first `[` and the last `]` of the answer. When that fails, the
  answer is split at commas and line breaks and quotes, brackets, dashes and asterisks are
  stripped; pieces longer than 50 characters are dropped.
- Tags are trimmed, lowercased, de-duplicated and capped at `maxTags`.
- Any failure after the argument check returns an empty list instead of throwing.

**Exceptions:** `ArgumentException` when `content` is null, empty or whitespace;
`ObjectDisposedException`.

---

`AiService` also has a public static helper used by the settings and onboarding connection tests:
`public static bool TryParseHttpEndpoint(string? value, out Uri endpoint)`, which accepts only
absolute `http` and `https` URLs.

---

### IAiProvider

```csharp
namespace AgentX.Core.AI;

public interface IAiProvider : IDisposable
```

Low-level abstraction over one inference backend.

**Namespace:** `AgentX.Core.AI`
**Assembly:** `AgentX.Core`
**Implementations** (in `AgentX.Core.AI.Providers`):

| Class | `ProviderId` | `DisplayName` | Backend |
|-------|--------------|---------------|---------|
| `LocalLlmProvider` | `local` | `Built-in LLM` | LLamaSharp, GGUF files in `{StoragePath}\Models` |
| `OllamaProvider` | `ollama` | `Ollama` | Ollama server through OllamaSharp |
| `OpenAiProvider` | `openai` | `OpenAI` | OpenAI Chat Completions or a compatible server |
| `AnthropicProvider` | `anthropic` | `Anthropic Claude` | Anthropic Messages API |

The HTTP requests each provider makes are listed in
[`API_ENDPOINTS.md`](../API_ENDPOINTS.md#ai-providers). Disposal is deferred while a call is
running on the provider, so a settings change cannot pull resources out from under a streaming
answer.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ProviderId` | `string` | Stable id (see the table above). |
| `DisplayName` | `string` | Name for the UI. |
| `IsAvailable` | `bool` | Result of the last `CheckConnectionAsync`. |

#### Methods

| Method | Description |
|--------|-------------|
| `Task<bool> CheckConnectionAsync(CancellationToken ct = default)` | Tests the backend and returns `false` instead of throwing on failure. Timeouts: Ollama 3 seconds, OpenAI and Anthropic 10 seconds. The built-in provider checks that its model file exists and loads it. Anthropic uses the Models API, so no tokens are generated. |
| `Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)` | Models this provider offers: the local Ollama models; OpenAI chat models only; Anthropic's Models API list, or a built-in fallback list; for the built-in provider, the configured GGUF file and any other `.gguf` file in the models folder. |
| `Task PullModelAsync(string modelName, IProgress<ModelDownloadProgress>? progress = null, CancellationToken ct = default)` | Downloads a model. Ollama pulls from its registry. The built-in provider downloads only models listed in `BuiltInModelCatalog` and throws `NotSupportedException` for other names. OpenAI and Anthropic do nothing. |
| `Task DeleteModelAsync(string modelName, CancellationToken ct = default)` | Deletes a local model. Ollama deletes it on the server; the built-in provider unloads and deletes the GGUF file; OpenAI and Anthropic do nothing. |
| `IAsyncEnumerable<string> StreamChatAsync(IReadOnlyList<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)` | Streams a completion. An error reported after the HTTP response started (an error event, or an Ollama stream without its final chunk) throws, so a cut-off answer is never returned as complete. |
| `Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)` | Returns the complete answer (the cloud providers collect their stream). |
| `Task<float[]> GenerateEmbeddingAsync(string text, string modelName, CancellationToken ct = default)` | Embeds one text. Anthropic throws `NotSupportedException`. The built-in provider embeds only with its configured model file. |
| `Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, string modelName, CancellationToken ct = default)` | Embeds several texts, one vector per text in the same order. |

---

### IHardwareDetector

```csharp
namespace AgentX.Core.AI;

public interface IHardwareDetector
{
    Task<HardwareCapability> DetectAsync(CancellationToken ct = default);
}
```

Detects the hardware relevant to local inference.

**Implementation:** `HardwareDetector`

**Behavior:** Queries WMI (`Win32_VideoController` through `GpuMemoryReader`, `Win32_Processor`,
`Win32_OperatingSystem` for free memory, and `Win32_PnPEntity` for an NPU); total memory comes from
`GC.GetGCMemoryInfo`. GPU memory is read from the driver's 64-bit `qwMemorySize` registry value,
with WMI `AdapterRAM` (which stops at 4 GB) as the fallback, and the GPU with the most memory is
chosen. The result is cached for the rest of the session. When GPU detection fails, `GpuName` is
`"Detection failed"`.

---

### IModelManager

```csharp
namespace AgentX.Core.AI;

public interface IModelManager
```

Lists, downloads and deletes models through the active provider.

**Implementation:** `ModelManager`

| Member | Description |
|--------|-------------|
| `Task<IReadOnlyList<AiModel>> GetAvailableModelsAsync(CancellationToken ct = default)` | Same as `GetInstalledModelsAsync` (there is no separate registry listing). |
| `Task<IReadOnlyList<AiModel>> GetInstalledModelsAsync(CancellationToken ct = default)` | The active provider's `ListModelsAsync`, cached for 30 seconds. The cache belongs to the provider that produced it and is not used after a provider change. Errors are logged and rethrown. |
| `Task PullModelAsync(string modelName, IProgress<ModelDownloadProgress>? progress = null, CancellationToken ct = default)` | Pulls through the active provider, clears the cache and raises `ModelListChanged`. `ArgumentException` for a blank name. |
| `Task DeleteModelAsync(string modelName, CancellationToken ct = default)` | Deletes through the active provider, clears the cache and raises `ModelListChanged`. `ArgumentException` for a blank name. |
| `Task<AiModel?> GetModelInfoAsync(string modelName, CancellationToken ct = default)` | Finds an installed model by `Name` or `Id`, ignoring case. Returns `null` when it is not found, the name is blank, or the list cannot be read. |
| `Task<bool> IsModelAvailableAsync(string modelName, CancellationToken ct = default)` | `true` when `GetModelInfoAsync` finds the model. |
| `event EventHandler<AiModel>? ModelListChanged` | Raised after a pull or delete. The argument carries only `Id` and `Name` (the model name). |

---

### IEmbeddingService

```csharp
namespace AgentX.Core.AI;

public interface IEmbeddingService
```

Generates embedding vectors with the embedding provider and model that
`IAiService.ResolveEmbeddingTarget` chooses, independently of the chat provider.

**Implementation:** `EmbeddingService`, registered wrapped in `CachedEmbeddingService` (an LRU cache
of 2,048 vectors keyed by `ModelVersion` and the text hash; entries of an earlier version are
dropped when the version changes).

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Dimensions` | `int` | Vector size of the current embedding model: the size observed from its last embedding, otherwise the known size of the model (`EmbeddingTargetResolver.KnownDimensions`), otherwise the configured default. |
| `ModelName` | `string` | The embedding model name (for example `all-minilm`, or the GGUF file name for the built-in model). |
| `ModelVersion` | `string` | The embedding space, `{providerId}:{modelName}:{dimensions}`, for example `ollama:all-minilm:384`. Chunks are stamped with it, and search leaves out chunks embedded with a different version. |

#### Methods

| Method | Description |
|--------|-------------|
| `Task<float[]> EmbedAsync(string text, CancellationToken ct = default)` | Embeds one text. `ArgumentException` for blank text. |
| `Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct = default)` | Embeds texts in batches of `EmbeddingBatchSize` (RAG configuration) and returns the vectors in input order. A provider that returns a different number of vectors than texts fails the batch. |

**Exceptions:** `InvalidOperationException` when the embedding provider is not registered (for
example an OpenAI embedding model without an OpenAI API key, or an invalid Ollama endpoint) or
returns an empty vector. There is no fallback to another provider, because that would mix
embedding spaces in one index.

---

### ICostTracker

```csharp
namespace AgentX.Core.AI.Models;

public interface ICostTracker
```

Records the token usage the providers report and estimates its cost.

**Implementation:** `CostTracker` (also `IDisposable`)

| Member | Description |
|--------|-------------|
| `void RecordUsage(string modelId, string providerId, int inputTokens, int outputTokens)` | Records one response. |
| `void RecordUsage(string modelId, string providerId, int inputTokens, int outputTokens, int cacheCreationInputTokens, int cacheReadInputTokens)` | Records one response with prompt-cache writes and reads (Anthropic). |
| `double GetTotalCostUsd()` | Estimated cost of all tracked usage, including records already dropped from the history. |
| `double GetCostForPeriod(DateTime start, DateTime end)` | Estimated cost of the kept records in the range. |
| `IReadOnlyList<UsageRecord> GetUsageHistory(int limit = 50)` | The most recent usage records. |
| `int GetTotalInputTokens()` / `int GetTotalOutputTokens()` | Token totals of all tracked usage. |

**Behavior:** Records are saved to `%LOCALAPPDATA%\AgentX\usage-history.json` shortly after new
usage and at shutdown, kept for 90 days (at most 20,000), and the totals of dropped records are
carried forward. Costs come from a built-in price table matched by the longest model id prefix;
local models and unknown models cost nothing. The table is listed in
[`API_ENDPOINTS.md`](../API_ENDPOINTS.md#errors-retries-and-usage-costs).

`UsageRecord` has `ModelId`, `ProviderId`, `InputTokens` (all prompt tokens, cache writes and reads
included), `OutputTokens`, `CacheCreationInputTokens`, `CacheReadInputTokens`, `EstimatedCostUsd`
and `Timestamp` (UTC).

---

## 2. AI Models

### ChatMessage

```csharp
namespace AgentX.Core.AI.Models;

public class ChatMessage
```

One message in a chat request.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Role` | `string` | `""` | `"user"`, `"assistant"`, `"system"` or `"tool"`. |
| `Content` | `string` | `""` | The message text. |
| `Timestamp` | `DateTime` | `DateTime.UtcNow` | When the message was created. |
| `ToolCalls` | `List<ToolCall>?` | `null` | Tool calls made by an assistant message. |
| `ToolCallId` | `string?` | `null` | For a `tool` message, the call it answers. |

Factory methods: `ChatMessage.User(string content)`, `ChatMessage.Assistant(string content)`,
`ChatMessage.AssistantWithTools(List<ToolCall> toolCalls)`, `ChatMessage.System(string content)`
and `ChatMessage.ToolResult(string toolCallId, string content)`.

---

### ChatOptions

```csharp
namespace AgentX.Core.AI.Models;

public class ChatOptions
```

Inference options for one request. Providers send only what their API accepts: OpenAI reasoning
models (`o1`, `o3`, `o4`, `gpt-5`) get no sampling or penalty parameters, and Anthropic receives
at most `Temperature` (clamped to 0-1, and only for model families that accept it).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ModelId` | `string?` | `null` | Model for this request. When `null`, `IAiService` fills in `ActiveModelId`. |
| `Temperature` | `double` | `0.7` | Sampling temperature. |
| `MaxTokens` | `int` | `2048` | Maximum tokens to generate. |
| `ContextWindow` | `int` | `4096` | Context window size used for generation. |
| `TopP` | `double` | `0.9` | Nucleus sampling threshold. OpenAI receives it only when it is between 0 and 1 exclusive; Anthropic never receives it. |
| `FrequencyPenalty` | `double` | `0` | Frequency penalty (OpenAI, only when not 0). |
| `PresencePenalty` | `double` | `0` | Presence penalty (OpenAI, only when not 0). |
| `StopSequences` | `string[]?` | `null` | Stop sequences. |
| `ResponseFormat` | `ResponseFormat` | `Text` | `JsonObject` asks for JSON output in the provider's native JSON mode. |
| `JsonSchema` | `string?` | `null` | A JSON Schema document. With `JsonSchemaName` and `ResponseFormat.JsonObject`, OpenAI enforces it (`json_schema`, `strict: true`); other providers fall back to plain JSON mode. |
| `JsonSchemaName` | `string?` | `null` | Name for the schema (required with `JsonSchema` on OpenAI). |
| `CacheSystemPrompt` | `bool` | `false` | Marks the system prompt for prompt caching on providers that support it (Anthropic). |
| `SystemPromptBlocks` | `IReadOnlyList<SystemPromptBlock>?` | `null` | A system prompt in blocks, each `SystemPromptBlock(string Text, bool Cacheable)`, for per-block prompt caching (Anthropic). Other providers ignore it, so callers still pass the joined system prompt. |
| `Tools` | `IReadOnlyList<ToolDefinition>?` | `null` | Not implemented: no provider sends tools to the model yet, so this and the two options below have no effect. |
| `ForceToolCall` | `bool` | `false` | See `Tools`. |
| `ForceToolName` | `string?` | `null` | See `Tools`. |

`ResponseFormat` is an enum: `Text = 0`, `JsonObject = 1`.

---

### AiModel

```csharp
namespace AgentX.Core.AI.Models;

public class AiModel
```

A model a provider offers.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `string` | `""` | Model id (for the built-in provider, the GGUF file name). |
| `Name` | `string` | `""` | Display name. |
| `ProviderId` | `string` | `""` | The provider that lists the model. |
| `Family` | `string` | `""` | Model family (for example `"llama"`; `"gguf"` for other GGUF files). |
| `IsAvailable` | `bool` | `true` | Whether the model can be used. |
| `SizeBytes` | `long` | `0` | Size in bytes. |
| `QuantizationLevel` | `string` | `""` | Quantization (for example `"Q4_K_M"`). |
| `ParameterCount` | `int` | `0` | Parameter count in millions (for example `7000` for a 7B model). |
| `ContextLength` | `int` | `0` | Context length, when the provider reports it. |
| `ModifiedAt` | `DateTime` | `default` | Last modification time. |
| `Digest` | `string` | `""` | Content digest, when the provider reports it. |
| `SizeFormatted` | `string` | *(computed)* | `"{MB:F1} MB"` below 1,000,000,000 bytes, otherwise `"{GB:F1} GB"`. |

---

### HardwareCapability

```csharp
namespace AgentX.Core.AI.Models;

public class HardwareCapability
```

Hardware detected by `IHardwareDetector`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `GpuName` | `string` | `"Unknown"` | GPU with the most memory (`"Detection failed"` when detection failed). |
| `GpuVramBytes` | `long` | `0` | Dedicated GPU memory in bytes. |
| `HasNpu` | `bool` | `false` | Whether an NPU was found. |
| `NpuName` | `string` | `"None"` | NPU name. |
| `CpuCores` | `int` | `0` | Physical cores of the first processor (`Win32_Processor.NumberOfCores`). |
| `CpuName` | `string` | `"Unknown"` | Processor name. |
| `TotalRamBytes` | `long` | `0` | Total RAM in bytes. |
| `AvailableRamBytes` | `long` | `0` | Free RAM in bytes. |

**Computed properties:**

| Property | Type | Description |
|----------|------|-------------|
| `GpuVramFormatted` | `string` | `"No dedicated GPU"` for 0, `"{MB:F0} MB"` below 1,000,000,000 bytes, otherwise `"{GB:F1} GB"`. |
| `TotalRamFormatted` | `string` | `"{GB:F0} GB"`. |
| `AvailableRamFormatted` | `string` | `"{GB:F1} GB"`. |
| `RecommendedMaxModelParameters` | `string` | Largest model size the free RAM holds: `"3B"` below 4 GB, `"7B"` below 8 GB, `"13B"` below 16 GB, `"34B"` below 32 GB, otherwise `"70B+"`. The Hardware Advisor shows it inside a localized sentence. |
| `IsNvidiaGpu` | `bool` | Whether `GpuName` contains `NVIDIA`. |
| `RecommendedGpuLayers` | `int` | 0 for other GPUs; for NVIDIA: 0 below 2 GB, 16 below 4 GB, 28 below 6 GB, otherwise 33. |
| `GpuAccelerationSummary` | `string` | English summary of CUDA acceleration or CPU inference. |

---

### ModelDownloadProgress

```csharp
namespace AgentX.Core.AI.Models;

public class ModelDownloadProgress
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ModelId` | `string` | `""` | The model being downloaded. |
| `Status` | `string` | `""` | Status text from the provider or downloader. |
| `CompletedBytes` | `long` | `0` | Bytes downloaded so far. |
| `TotalBytes` | `long` | `0` | Total bytes. |
| `PercentComplete` | `double` | *(computed)* | 0 to 100; `0` when `TotalBytes` is 0. |

---

### EmbeddingTarget and EmbeddingTargetResolver

```csharp
namespace AgentX.Core.AI;

public sealed record EmbeddingTarget(string ProviderId, string ModelName);

public static class EmbeddingTargetResolver
```

`EmbeddingTarget` names the provider (`"local"`, `"ollama"` or `"openai"`, never `"anthropic"`) and
the model passed to its embedding call.

| Member | Description |
|--------|-------------|
| `const string DefaultEmbeddingModelSetting = "all-minilm"` | The Embedding Model setting of a fresh install. |
| `static EmbeddingTarget Resolve(string? embeddingModelSetting, string? localModelFileName, bool localModelInstalled)` | 1. A `text-embedding-*` model uses OpenAI (the only case that sends document text to a cloud service for embedding). 2. A `.gguf` file name uses the built-in provider. 3. Any other name except the default uses Ollama with that model. 4. The default uses the built-in model when its file is installed, and Ollama `all-minilm` when it is not. |
| `static bool IsOpenAiEmbeddingModel(string? model)` | `true` for ids starting with `text-embedding-`. |
| `static int? KnownDimensions(EmbeddingTarget target)` | Vector size of well-known models by id prefix (`all-minilm` 384, `nomic-embed-text` 768, `mxbai-embed-large` and `bge-m3` 1024, `text-embedding-3-small` and `text-embedding-ada-002` 1536, `text-embedding-3-large` 3072, `llama-3.2-3b` 3072, `llama-3.2-1b` 2048), or `null`. |

---

## 3. Document Services

### IDocumentService

```csharp
namespace AgentX.Core.Documents;

public interface IDocumentService
```

Runs the document import pipeline: file validation, SHA-256 content hashing, text extraction and
the database record. A new document is left in `"pending"` status and handed to the indexing
pipeline, which chunks and embeds it.

**Namespace:** `AgentX.Core.Documents`
**Assembly:** `AgentX.Core`
**Implementation:** `DocumentService`

#### Events

| Event | Type | Description |
|-------|------|-------------|
| `DocumentPendingIndexing` | `EventHandler<DocumentPendingIndexingEventArgs>?` | Raised after a document was imported or reset for re-indexing and waits in `"pending"` status. `IndexingService` subscribes and queues the document at once. Handlers run on the caller's thread and must not block. |

#### Methods

---

##### ImportFileAsync

```csharp
Task<DocumentEntity> ImportFileAsync(
    string filePath,
    long? collectionId = null,
    CancellationToken ct = default);
```

Imports one file: validates the path, hashes the content, extracts the text with the processor
that claims the file, and creates the `DocumentEntity`.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `filePath` | `string` | -- | Absolute path to the file. |
| `collectionId` | `long?` | `null` | Collection to add the document to. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** The new document, in `"pending"` status. When the processor cannot extract text (the
file is encrypted, corrupt, has no text layer, and so on) it throws `DocumentExtractionException`
and the document is recorded as `"failed"` with that reason instead; it stays visible in the vault
and is not queued.

**Exceptions:** `DuplicateDocumentException` (an `InvalidOperationException`) when a document with
the same content already exists.

---

##### ImportFilesAsync

```csharp
Task<IReadOnlyList<DocumentEntity>> ImportFilesAsync(
    IReadOnlyList<string> filePaths,
    long? collectionId = null,
    IProgress<int>? progress = null,
    CancellationToken ct = default);
```

Imports several files and reports the number of files completed through `progress`. It runs
`ImportFilesWithReportAsync` without `allowDuplicates`, so duplicates and files that cannot be
imported are skipped (and logged) rather than thrown.

**Returns:** The documents created.

---

##### ImportFilesWithReportAsync

```csharp
Task<DocumentImportReport> ImportFilesWithReportAsync(
    IReadOnlyList<string> filePaths,
    long? collectionId = null,
    bool allowDuplicates = false,
    IProgress<int>? progress = null,
    CancellationToken ct = default);
```

Imports several files and reports what happened to each one. The failure of one file does not
stop the batch.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `filePaths` | `IReadOnlyList<string>` | -- | Absolute paths of the files. |
| `collectionId` | `long?` | `null` | Collection to add every document to. |
| `allowDuplicates` | `bool` | `false` | When `true`, files whose content matches an existing document are imported anyway; otherwise they are listed in `DocumentImportReport.Duplicates`. |
| `progress` | `IProgress<int>?` | `null` | Number of files completed. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** A [`DocumentImportReport`](#documentimportreport).

---

##### ImportExternalContentAsync

```csharp
Task<DocumentEntity> ImportExternalContentAsync(
    string filePath,
    string fileTypeOverride,
    string displayName,
    string? sourceUrl = null,
    long? collectionId = null,
    CancellationToken ct = default);
```

Imports a file written for a connector item (calendar event, email) and stores the semantic type
given in `fileTypeOverride` (for example `"CalendarEvent"` or `"EmailMessage"`) instead of the
extension, with `displayName` as the file name.

---

##### ImportPreparedDocumentAsync

```csharp
Task<DocumentEntity> ImportPreparedDocumentAsync(
    DocumentEntity document,
    long? collectionId = null,
    CancellationToken ct = default);
```

Saves a document for a file Agent-X wrote itself from content it already holds (a page saved by
Web Import). The caller fills in the new `"pending"` document, including `ContentHash`; the file is
not read. The document and its collection link are saved together, so the call either adds the
document to the collection or adds nothing. Raises `DocumentPendingIndexing`.

**Exceptions:** `DuplicateDocumentException` when the content already exists;
`InvalidOperationException` when the collection does not exist.

---

##### GetDocumentAsync

```csharp
Task<DocumentEntity?> GetDocumentAsync(long documentId);
```

Returns the document with its collection and tag links, or `null` when it does not exist.

---

##### GetDocumentPreviewTextAsync

```csharp
Task<string?> GetDocumentPreviewTextAsync(long documentId, int maxChars = 1800, CancellationToken ct = default);
```

Returns a short preview for launching a workflow from a document: the stored summary when there is
one, otherwise the start of the indexed content. `maxChars` is clamped to 200-4000.

---

##### GetAllDocumentsAsync

```csharp
Task<IReadOnlyList<DocumentEntity>> GetAllDocumentsAsync(
    string? fileTypeFilter = null,
    string? statusFilter = null,
    string? tagFilter = null,
    long? collectionId = null,
    DateTime? importedAfter = null,
    DateTime? importedBefore = null,
    string? sortBy = null,
    CancellationToken ct = default);
```

Returns documents with optional filters. The documents are not tracked by the database context.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `fileTypeFilter` | `string?` | `null` | One file type (for example `"pdf"`), or the category `"code"` or `"image"`, which matches every extension of the code or image processor ([`DocumentFileTypeFilter`](#documentfiletypefilter)). |
| `statusFilter` | `string?` | `null` | Indexing status (`"pending"`, `"processing"`, `"completed"`, `"failed"`), matched without regard to case. |
| `tagFilter` | `string?` | `null` | Tag name; documents with this tag, ignoring case. |
| `collectionId` | `long?` | `null` | Documents in this collection. |
| `importedAfter` | `DateTime?` | `null` | Inclusive lower bound for `ImportedAt` (UTC). |
| `importedBefore` | `DateTime?` | `null` | Inclusive upper bound for `ImportedAt` (UTC). |
| `sortBy` | `string?` | `null` | `"name"` (A-Z), `"size"` (largest first), `"type"` (then newest first), or `"date"` / `null` (newest import first). |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

---

##### Other members

| Member | Description |
|--------|-------------|
| `Task<IReadOnlyList<DocumentEntity>> GetRecentDocumentsAsync(int limit = 5, CancellationToken ct = default)` | The most recently imported documents, newest first (at least 1). |
| `Task DeleteDocumentAsync(long documentId)` | Deletes the document with its chunks, vectors and keyword index rows, and lowers the document count of every collection it belonged to. A missing id is logged and ignored. |
| `Task ReindexDocumentAsync(long documentId, CancellationToken ct = default)` | Extracts the text again first. On success it removes the old chunks, vectors and keyword rows, resets the status to `"pending"` and raises `DocumentPendingIndexing`. When the source file is missing or extraction fails, the document is marked `"failed"` and its current index data is kept. |
| `Task<DocumentEntity?> GetDocumentByHashAsync(string contentHash)` | Finds a document by its SHA-256 content hash. |
| `Task<long> GetTotalDocumentCountAsync()` | Number of documents. |
| `Task<long> GetTotalStorageBytesAsync()` | Sum of `FileSizeBytes`. |
| `Task<Dictionary<string, int>> GetFileTypeDistributionAsync()` | Document count per `FileType`, for example `{"pdf": 12, "docx": 5}`. |
| `bool CanProcess(string filePath)` | Whether a built-in processor, or a processor of an active plugin, claims the file. |
| `IReadOnlySet<string> GetSupportedExtensions()` | Extensions of the built-in processors, plus those of active plugin processors (computed on each call, because plugins activate and deactivate at run time). |
| `Task<DuplicateCheckResult> CheckForDuplicateAsync(string filePath, CancellationToken ct = default)` | Compares the file's SHA-256 hash with the vault. |
| `Task BulkDeleteAsync(IReadOnlyList<long> documentIds, CancellationToken ct = default)` | Deletes several documents; a failure is logged and the batch continues. |
| `Task BulkReindexAsync(IReadOnlyList<long> documentIds, CancellationToken ct = default)` | Calls `ReindexDocumentAsync` for each document; a failure is logged and the batch continues. |
| `Task BulkAssignToCollectionAsync(IReadOnlyList<long> documentIds, long collectionId, CancellationToken ct = default)` | Adds several documents to a collection; a failure is logged and the batch continues. |
| `Task<int> RequeueAudioAwaitingSpeechModelAsync(CancellationToken ct = default)` | Queues again the audio documents that have no transcript because the speech-to-text model was missing when they were read. Call it once the model is installed. Returns the number of documents queued. |

---

### IDocumentProcessor

```csharp
namespace AgentX.Core.Documents;

public interface IDocumentProcessor
{
    IReadOnlySet<string> SupportedExtensions { get; }
    bool CanProcess(string filePath);
    Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default);
}
```

Extracts text from one kind of file. `ProcessAsync` throws `DocumentExtractionException` with a
user-facing reason when a file cannot be turned into text.

**Namespace:** `AgentX.Core.Documents`
**Assembly:** `AgentX.Core`

#### Implementations

The processors are registered in this order, and the first one that claims a file reads it.
Processors of active plugins are asked only for files that no built-in processor claims.

| Processor | Extensions | Notes |
|-----------|-----------|-------|
| `PdfProcessor` | `.pdf` | Pages are separated with a form feed, so chunks carry page numbers. A PDF without a text layer, or whose text cannot be decoded, is reported as a failure. |
| `DocxProcessor` | `.docx` | Word documents. Legacy `.doc` files are not read. |
| `TextProcessor` | `.txt`, `.csv`, `.log`, `.json`, `.xml`, `.yaml`, `.yml`, `.toml`, `.ini`, `.cfg` | Plain and structured text. |
| `MarkdownProcessor` | `.md`, `.mdx`, `.markdown` | Converted to plain text with Markdig. YAML front matter is stripped, and the first level-1 heading becomes the title. |
| `CodeFileProcessor` | `.cs`, `.js`, `.ts`, `.py`, `.java`, `.cpp`, `.c`, `.h`, `.go`, `.rs`, `.swift`, `.kt`, `.rb`, `.php`, `.html`, `.htm`, `.css`, `.scss`, `.sql`, `.sh`, `.yaml`, `.yml`, `.toml`, `.ini`, `.cfg`, `.xaml` | `SupportedFileTypes.Code` (26 extensions). The configuration formats it lists are read by `TextProcessor`, which is registered first. |
| `ImageProcessor` | `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tiff` | Windows OCR (`OcrEngine`). An image without recognizable text yields empty text; a missing OCR language is reported as a failure. |
| `AudioProcessor` | `.mp3`, `.wav`, `.m4a`, `.flac`, `.ogg`, `.webm` | Transcription with the Whisper speech-to-text model. |
| `WebProcessor` | `.url`, `.webloc` | Fetches the page a shortcut file points to. URLs that point at this computer or the local network are refused. |

---

### IChunkingService

```csharp
namespace AgentX.Core.Documents;

public interface IChunkingService
```

Splits text into overlapping chunks for embedding: paragraphs first, then sentences, then words.

**Namespace:** `AgentX.Core.Documents`
**Assembly:** `AgentX.Core`
**Implementation:** `ChunkingService` (created with `ITokenCounter` and, when registered,
`IAdaptiveChunkingService`)

#### Methods

##### ChunkText

```csharp
IReadOnlyList<DocumentChunk> ChunkText(
    string text,
    int chunkSize = 512,
    int chunkOverlap = 50,
    string? sectionTitle = null,
    int? pageNumber = null);
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `text` | `string` | -- | Text to split. Empty or whitespace text returns an empty list. |
| `chunkSize` | `int` | `512` | Maximum tokens per chunk, counted with `ITokenCounter`. |
| `chunkOverlap` | `int` | `50` | Tokens carried from the end of one chunk to the start of the next. Whole trailing segments are carried while they fit, and the segment that does not fit contributes only its last words, so the overlap never exceeds this size. |
| `sectionTitle` | `string?` | `null` | Section title stored on every chunk. |
| `pageNumber` | `int?` | `null` | Page number stored on every chunk. |

**Exceptions:** `ArgumentOutOfRangeException` when `chunkSize` is not positive, `chunkOverlap` is
negative, or `chunkOverlap` is not smaller than `chunkSize`.

##### ChunkDocument

```csharp
IReadOnlyList<DocumentChunk> ChunkDocument(
    ProcessedDocument document,
    int chunkSize = 512,
    int chunkOverlap = 50);
```

Chunks a processed document. A document without extracted text returns an empty list. When the
adaptive analyzer detects code or table content, its recommended chunk size replaces `chunkSize`
(and the overlap is lowered below it when needed). A multi-page document whose text contains form
feeds is chunked page by page, so chunks keep their page numbers.

---

### Document Models

#### ProcessedDocument

```csharp
namespace AgentX.Core.Documents.Models;

public class ProcessedDocument
```

A document after text extraction and before chunking.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `FilePath` | `string` | `""` | Absolute path of the source file. |
| `FileName` | `string` | `""` | File name. |
| `FileType` | `string` | `""` | File type (the extension without the dot). |
| `FileSizeBytes` | `long` | `0` | File size in bytes. |
| `ContentHash` | `string` | `""` | SHA-256 hash of the content. |
| `ExtractedText` | `string` | `""` | The extracted text. |
| `ExtractedTitle` | `string?` | `null` | Title from the document metadata. |
| `PageCount` | `int` | `0` | Number of pages. |
| `WordCount` | `long` | `0` | Approximate word count. |
| `Language` | `string?` | `null` | Detected language. |
| `Metadata` | `DocumentMetadata` | `new()` | Additional metadata. |
| `Chunks` | `List<DocumentChunk>` | `new()` | Chunks, when a processor produces them. |

#### DocumentChunk

```csharp
namespace AgentX.Core.Documents.Models;

public class DocumentChunk
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Index` | `int` | `0` | Zero-based position in the document. |
| `Content` | `string` | `""` | Chunk text. |
| `StartCharOffset` | `int` | `0` | Start offset in the source text. |
| `EndCharOffset` | `int` | `0` | End offset in the source text. |
| `PageNumber` | `int?` | `null` | Page number, when known. |
| `SectionTitle` | `string?` | `null` | Section title, when known. |
| `TokenCount` | `int` | `0` | Token count. |
| `Embedding` | `float[]?` | `null` | Embedding vector, once generated. |

#### DocumentMetadata

```csharp
namespace AgentX.Core.Documents.Models;

public class DocumentMetadata
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Author` | `string?` | `null` | Author from the file metadata. |
| `Subject` | `string?` | `null` | Subject from the file metadata. |
| `CreatedDate` | `DateTime?` | `null` | Creation date from the file metadata. |
| `ModifiedDate` | `DateTime?` | `null` | Modification date from the file metadata. |
| `Custom` | `Dictionary<string, string>` | `new()` | Other key-value metadata. |

#### SupportedFileTypes

```csharp
namespace AgentX.Core.Documents.Models;

public static class SupportedFileTypes
```

Extension sets used for file categories (`FileTypeHelper`) and by the vault's Code and Images
filters. Which files can be imported is decided by the registered processors (see
[IDocumentProcessor](#idocumentprocessor)).

| Field | Extensions |
|-------|-----------|
| `Pdf` | `.pdf` |
| `Office` | `.docx`, `.doc` (no processor reads `.doc`) |
| `Text` | `.txt`, `.csv`, `.log`, `.xml`, `.json` |
| `Markdown` | `.md`, `.markdown` |
| `Image` | `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tiff` |
| `Code` | The 26 extensions of `CodeFileProcessor` (see above). |
| `All` | Union of the sets above, compared without regard to case. |

#### DocumentFileTypeFilter

```csharp
namespace AgentX.Core.Documents;

public static class DocumentFileTypeFilter
```

Turns a file type filter into the `DocumentEntity.FileType` values it matches.
`Resolve(string filter)` ignores a leading dot and case; the constants `Code` (`"code"`) and
`Image` (`"image"`) expand to every extension of `SupportedFileTypes.Code` and
`SupportedFileTypes.Image`, and any other value matches that single type.

#### DocumentImportReport

```csharp
namespace AgentX.Core.Documents;

public sealed class DocumentImportReport
```

| Member | Type | Description |
|--------|------|-------------|
| `Imported` | `List<DocumentEntity>` | Documents created, including those recorded as `"failed"` because no text could be extracted. |
| `Duplicates` | `List<DocumentImportDuplicate>` | Files skipped because a document with the same content exists: `DocumentImportDuplicate(string FilePath, long ExistingDocumentId, string ExistingFileName)`. |
| `Failed` | `List<DocumentImportFailure>` | Files that could not be imported: `DocumentImportFailure(string FilePath, string Reason)`. |
| `ExtractionFailedCount` | `int` | Imported documents in `"failed"` status. |

#### DuplicateCheckResult

```csharp
namespace AgentX.Core.Documents;

public class DuplicateCheckResult
```

| Property | Type | Description |
|----------|------|-------------|
| `IsDuplicate` | `bool` | A matching document exists. |
| `IsExactMatch` | `bool` | The match has the same SHA-256 hash. |
| `ExistingDocumentId` | `long?` | The matching document. |
| `ExistingFileName` | `string?` | Its file name. |
| `MatchScore` | `float` | `1.0` for an exact match. |

#### DocumentPendingIndexingEventArgs

```csharp
namespace AgentX.Core.Documents;

public sealed class DocumentPendingIndexingEventArgs : EventArgs
```

| Property | Type | Description |
|----------|------|-------------|
| `DocumentId` | `long` | The document waiting to be indexed. |
| `Extracted` | `ProcessedDocument?` | The extraction the import already did, when available. The indexer reuses it instead of parsing, OCR-ing or fetching the file again, as long as the file has not changed. |

#### Exceptions

| Type | Description |
|------|-------------|
| `DuplicateDocumentException` | Derives from `InvalidOperationException`. Properties `ExistingDocumentId` and `ExistingFileName`; message `A document with identical content already exists: '{name}' (ID {id}).` |
| `DocumentExtractionException` | Thrown by a processor when a file cannot be turned into text. The message is written for the user and becomes the document's `IndexingError`. |

---

## 4. Search and RAG

### ISemanticSearchService

```csharp
namespace AgentX.Core.Search;

public interface ISemanticSearchService
```

Vector search over the indexed chunks, plus the search history.

**Namespace:** `AgentX.Core.Search`
**Assembly:** `AgentX.Core`
**Implementation:** `SemanticSearchService`

#### SearchAsync

```csharp
Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default);
```

Embeds `QueryText`, searches the vector store, and returns the matching chunks with their document
metadata, highest score first, at most `TopK`.

**Behavior:**
- One result per chunk, so a document can appear more than once. Results below `MinScore` are
  dropped.
- Chunks embedded with a different `ModelVersion` than the current embedding model are left out
  (chunks without a version are kept); a warning is logged once per session.
- The collection, file type and date filters (`CreatedAfter` and `CreatedBefore` compare with
  `ImportedAt`) are applied to the candidates. For a filtered search the candidate pool is widened
  step by step, up to a bounded ceiling, until `TopK` results are found or no candidates are left.
- An empty query, a failed embedding or a failed vector search returns an empty list (logged)
  instead of throwing. Cancellation propagates.

#### Search history

```csharp
Task SaveSearchHistoryAsync(string queryText, int resultCount,
    double? minScore = null, int? maxResults = null,
    DateTime? dateAfter = null, DateTime? dateBefore = null,
    string? sortOrder = null, string? searchType = null);
Task<IReadOnlyList<SearchHistoryEntry>> GetSearchHistoryAsync(int limit = 20);
Task ClearSearchHistoryAsync();
Task SaveSearchFilterAsync(long historyId);
Task UnsaveSearchFilterAsync(long historyId);
Task<IReadOnlyList<SearchHistoryEntry>> GetSavedFiltersAsync();
```

| Method | Description |
|--------|-------------|
| `SaveSearchHistoryAsync` | Saves a search with its optional filter settings. `searchType` is the mode it ran in (`"semantic"`, `"keyword"` or `"hybrid"`); `"semantic"` when not given. |
| `GetSearchHistoryAsync` | Most recent entries first; an empty list when `limit` is 0 or less. |
| `ClearSearchHistoryAsync` | Deletes every history entry, saved filters included. |
| `SaveSearchFilterAsync` / `UnsaveSearchFilterAsync` | Sets or clears `IsSaved` on an entry. |
| `GetSavedFiltersAsync` | Entries saved as filters. |

#### SearchHistoryEntry

```csharp
namespace AgentX.Core.Search;

public class SearchHistoryEntry
```

All properties are init-only: `Id` (`long`), `QueryText` (`string`), `ResultCount` (`int`),
`SearchedAt` (`DateTime`), `IsSaved` (`bool`), `SearchType` (`string`, default `"semantic"`),
`CollectionFilter` (`string?`), and the saved filter settings `MinScore` (`double?`),
`MaxResults` (`int?`), `DateAfter` (`DateTime?`), `DateBefore` (`DateTime?`) and `SortOrder`
(`string?`).

---

### IKeywordSearchService

```csharp
namespace AgentX.Core.Search;

public interface IKeywordSearchService
```

Full-text search with SQLite FTS5 and BM25 ranking.

**Implementation:** `KeywordSearchService`

| Method | Description |
|--------|-------------|
| `Task InitializeFtsAsync(CancellationToken ct = default)` | Creates the FTS5 table when it does not exist. Called once at startup. |
| `Task IndexDocumentChunksAsync(long documentId, CancellationToken ct = default)` | Adds a document's chunks to the FTS table (the indexer does this before it reports the document complete). |
| `Task RemoveDocumentFromFtsAsync(long documentId, CancellationToken ct = default)` | Removes a document's FTS rows (on delete and re-index). |
| `Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default)` | FTS5 `MATCH` search. Stop words are ignored and any remaining term may match. Collection, file type and date filters are applied in the query. Scores are relative to the best match (0-1), and `MinScore` applies on that scale. |

---

### IHybridSearchOrchestrator

```csharp
namespace AgentX.Core.Search;

public interface IHybridSearchOrchestrator
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default);
}
```

Runs a search in the mode `SearchQuery.Mode` names: `Semantic` uses `ISemanticSearchService`,
`Keyword` uses `IKeywordSearchService`, and `Hybrid` runs both and merges them with Reciprocal Rank
Fusion.

**Implementation:** `HybridSearchOrchestrator`

---

### IRagPipeline

```csharp
namespace AgentX.Core.Search;

public interface IRagPipeline
```

Answers a question from the indexed documents (Retrieval-Augmented Generation).

**Implementation:** `RagPipeline`. The optional enhancement services (multi-query expansion, HyDE,
LLM reranking, parent document retrieval, contextual compression, evaluation) are used when they
are registered and enabled in the RAG configuration.

#### AskAsync

```csharp
Task<RagResponse> AskAsync(
    string question,
    long? collectionId = null,
    Action<string>? onToken = null,
    bool enableResearchMode = false,
    CancellationToken ct = default);
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `question` | `string` | -- | The question. |
| `collectionId` | `long?` | `null` | Collection to search; `null` searches everything. |
| `onToken` | `Action<string>?` | `null` | Called for each streamed piece of the answer. |
| `enableResearchMode` | `bool` | `false` | Adds web search results to the context (Research Mode). Needs a configured `IWebSearchService`; the number of web results is the Max Search Results setting (1-20, default 10). |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Behavior:**
1. Optional query expansion and HyDE produce more query variants.
2. Every variant is searched through `IHybridSearchOrchestrator` in the configured mode
   (`Rag:DefaultSearchMode` in `appsettings.json`, `Hybrid` by default). The number of chunks
   retrieved and kept is the Top-K Results setting, capped at `Rag:MaxTopK`; `Rag:DefaultTopK`
   applies when settings are unavailable.
3. When nothing is found, the answer says so and the answering model is not called.
4. When `Rag:EnablePiiRedaction` is on (the default), chunks are redacted as soon as they are built
   (and again after parent retrieval), so every model call that sees chunk text, reranking and
   compression included, gets the redacted text.
5. Heuristic reranking, optional LLM reranking, parent retrieval and compression shape the
   context; Research Mode adds web results.
6. The answer streams from the active provider, `[N]` citations are resolved, and an optional
   evaluation runs in the background.

**Returns:** A [`RagResponse`](#ragresponse) with the answer, citations, web citations (Research
Mode) and timings.

#### GetIndexedChunkCountAsync

```csharp
Task<long> GetIndexedChunkCountAsync(CancellationToken ct = default);
```

Number of indexed chunks available for questions.

---

### ICitationService

```csharp
namespace AgentX.Core.Search;

public interface ICitationService
{
    List<Citation> ExtractCitations(string responseText, IReadOnlyList<RagContextChunk> contextChunks);
}
```

Finds the `[N]` references in an answer and maps each to the N-th context chunk (1-based) given to
the model.

**Implementation:** `CitationService`

#### RagContextChunk

```csharp
namespace AgentX.Core.Search;

public class RagContextChunk
```

One chunk given to the model. All properties are init-only: `ChunkId` (`long`), `DocumentId`
(`long`), `FileName` (`string`), `FilePath` (`string`), `PageNumber` (`int?`), `ChunkIndex`
(`int`), `ChunkText` (`string`) and `RelevanceScore` (`float`).

---

### Search Models

#### SearchMode

```csharp
namespace AgentX.Core.Search.Models;

public enum SearchMode { Semantic, Keyword, Hybrid }
```

`Semantic` is vector search, `Keyword` is FTS5 with BM25 ranking, `Hybrid` merges both with
Reciprocal Rank Fusion.

#### SearchQuery

```csharp
namespace AgentX.Core.Search.Models;

public class SearchQuery
```

All properties use `init` accessors.

| Property | Type | Default | Required | Description |
|----------|------|---------|----------|-------------|
| `QueryText` | `string` | -- | **Yes** (`required`) | The query text. |
| `TopK` | `int` | `10` | No | Maximum number of results. |
| `MinScore` | `float` | `0.3f` | No | Minimum score (0.0 to 1.0). |
| `CollectionId` | `long?` | `null` | No | Collection scope. |
| `FileTypeFilter` | `string?` | `null` | No | File type (for example `"pdf"`). |
| `CreatedAfter` | `DateTime?` | `null` | No | Only documents imported at or after this time. |
| `CreatedBefore` | `DateTime?` | `null` | No | Only documents imported at or before this time. |
| `Mode` | `SearchMode` | `Semantic` | No | Search mode used by `IHybridSearchOrchestrator`. |

#### SearchResult

```csharp
namespace AgentX.Core.Search.Models;

public class SearchResult
```

One matching chunk. All properties are init-only except the computed one.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ChunkId` | `long` | -- | The chunk. |
| `DocumentId` | `long` | -- | Its document. |
| `FileName` | `string` | `""` | Document file name. |
| `FilePath` | `string` | `""` | Document file path. |
| `FileType` | `string` | `""` | Document file type. |
| `PageNumber` | `int?` | `null` | Page number. |
| `ChunkIndex` | `int` | -- | Chunk position in the document. |
| `MatchedText` | `string` | `""` | Full chunk text. |
| `Excerpt` | `string` | `""` | Shorter excerpt for display. |
| `Score` | `float` | -- | 0.0 to 1.0, higher is more relevant (cosine similarity for semantic results). |
| `RelevancePercent` | `int` | *(computed)* | `(int)(Score * 100)`. |
| `CollectionNames` | `List<string>` | `new()` | Collections the document belongs to. |

#### RagResponse

```csharp
namespace AgentX.Core.Search.Models;

public class RagResponse
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AnswerText` | `string` | `""` | The answer, with `[N]` references. |
| `Question` | `string` | `""` | The question (init-only). |
| `Citations` | `List<Citation>` | `new()` | Resolved document citations. |
| `ContextChunksUsed` | `int` | -- | Number of chunks given to the model (init-only). |
| `IsStreaming` | `bool` | `false` | Whether the answer is still streaming. |
| `TotalLatencyMs` | `double` | `0` | Search plus generation time. |
| `SearchLatencyMs` | `double` | `0` | Search time. |
| `CollectionScope` | `long?` | `null` | Collection searched; `null` means all (init-only). |
| `EvalMetrics` | `RagEvalMetrics?` | `null` | Quality evaluation, filled in asynchronously when an evaluator runs. |
| `WebCitations` | `IReadOnlyList<WebCitation>?` | `null` | Web sources used by Research Mode; `null` when it is off or found nothing. |

#### Citation

```csharp
namespace AgentX.Core.Search.Models;

public class Citation
```

All properties are init-only: `Number` (`int`, the N of `[N]`), `DocumentId` (`long`), `ChunkId`
(`long`), `FileName` (`string`), `FilePath` (`string`), `PageNumber` (`int?`), `ChunkIndex`
(`int`), `Excerpt` (`string`) and `RelevanceScore` (`float`).

#### WebCitation

```csharp
namespace AgentX.Core.Search.Models;

public sealed class WebCitation
```

A source that is either a vault document or a web page (Research Mode). Init-only properties:
`Title` (`string`), `Url` (`string`, the web URL, or the file path for a vault source), `Snippet`
(`string`), `Source` (`WebCitationSource`: `Vault` or `Web`) and `DocumentName` (`string?`, the
file name for a vault source).

---

## 5. Vector Store

### IVectorStore

```csharp
namespace AgentX.Core.Data.VectorDb;

public interface IVectorStore : IAsyncDisposable
```

Stores embedding vectors and finds the nearest ones.

**Namespace:** `AgentX.Core.Data.VectorDb`
**Assembly:** `AgentX.Core`
**Implementations:** `VectorStoreFactory.Create` returns `HnswVectorStore` when
`AppSettings.EnableHnswIndex` is on (the default; it scans linearly below
`HnswFallbackThreshold` vectors) and `SqliteVecStore` (linear scan) when it is off. Both keep the
vectors in `agentx.db` under `AppSettings.StoragePath` and open it through
`IEncryptedConnectionFactory`, so an encrypted database works. The HNSW index follows the vector
size of the current embedding model (vectors of other sizes are searched by linear scan). Its
index files are saved next to the database only while the database is not encrypted; with an
encrypted database the index is kept in memory and rebuilt at start.

#### Methods

| Method | Description |
|--------|-------------|
| `Task InitializeAsync(CancellationToken ct = default)` | Opens the store (creates tables, loads or builds the index). Must be called before other operations; the indexing service does this at startup. |
| `Task<long> InsertEmbeddingAsync(long chunkId, float[] embedding, CancellationToken ct = default)` | Stores the vector of a chunk and returns its row id. |
| `Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryEmbedding, int topK = 5, double minSimilarity = 0.3, CancellationToken ct = default)` | Nearest neighbors with cosine similarity of at least `minSimilarity`, highest first. |
| `Task DeleteEmbeddingAsync(long chunkId, CancellationToken ct = default)` | Deletes one chunk's vector. |
| `Task DeleteEmbeddingsForDocumentAsync(long documentId, IReadOnlyList<long> chunkIds, CancellationToken ct = default)` | Deletes the vectors of the given chunks (`documentId` is for logging). |
| `Task<long> GetEmbeddingCountAsync(CancellationToken ct = default)` | Number of stored vectors. |
| `Task OptimizeAsync(CancellationToken ct = default)` | Optimizes the index; may do nothing. |
| `Task SuspendAsync(CancellationToken ct = default)` | Waits for running operations, then closes the store's connection to the database file so it can be replaced (restore) or re-encrypted; on Windows an open handle makes that fail. Operations called while suspended wait. Suspensions nest. If `ct` is cancelled while waiting, the store stays in service and the suspension does not count. |
| `Task ResumeAsync(bool reloadFromDatabase, CancellationToken ct = default)` | Ends one suspension; the last one reopens the connection with the current database key and releases waiting operations (they fail if reopening fails). With `reloadFromDatabase`, the store drops what it derived from the previous file (the in-memory index and index files) and loads again, as after a restore. Does nothing when the store is not suspended. |

---

### VectorSearchResult

```csharp
namespace AgentX.Core.Data.VectorDb;

public class VectorSearchResult
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ChunkId` | `long` | `0` | The matching chunk. |
| `Distance` | `double` | `0` | Cosine distance: `0.0` identical, `2.0` opposite. |
| `Similarity` | `double` | *(computed)* | `1.0 - Distance`, from `-1.0` to `1.0`. |

---

## 6. Chat Services

### IChatService

```csharp
namespace AgentX.Core.Services.Chat;

public interface IChatService
```

Sends chat messages, streams the replies and saves both through `IConversationService`.

**Namespace:** `AgentX.Core.Services.Chat`
**Assembly:** `AgentX.Core`
**Implementation:** `ChatService`

#### Properties and events

| Member | Type | Description |
|--------|------|-------------|
| `IsGenerating` | `bool` | Whether a reply is being generated. |
| `GenerationStateChanged` | `EventHandler<bool>?` | Raised when `IsGenerating` changes; the argument is the new value. |

#### SendMessageAsync

```csharp
IAsyncEnumerable<string> SendMessageAsync(
    long conversationId,
    string userMessage,
    CancellationToken ct = default);

IAsyncEnumerable<string> SendMessageAsync(
    long conversationId,
    string userMessage,
    SupplementalContext? supplementalContext,
    CancellationToken ct);
```

Saves the user message, assembles the context (conversation history, memories and, in the second
overload, `supplementalContext`), streams the reply, and saves the complete reply.

**Behavior:**
- An empty or whitespace message yields nothing and saves nothing.
- Temperature, maximum tokens and context window come from Settings > Inference.
- With **Multi-Model Routing** on (`AppSettings.EnableModelRouting`), `IModelRouterService` picks a
  provider and model for this reply only; the active provider, the active model and the saved
  settings are not changed. When routing is off, fails, or picks a provider that is not registered
  or not reachable, the active provider answers.
- The reply is saved once it is complete, with the id of the model that wrote it (the routed model,
  otherwise the active model) and, when `supplementalContext` has citations, those sources in
  `MessageEntity.CitationsJson` (see `MessageCitations`). A reply that is stopped or fails is not
  saved.
- Memory extraction runs in the background after a saved reply.

`SupplementalContext` is `public sealed record SupplementalContext(string PromptContext,
IReadOnlyList<WebCitation> Citations)` in `AgentX.Core.Services.Chat.Models`. `PromptContext`
(for example Research Mode's web results) is added to this reply's context only and is not saved;
`Citations` are numbered in the order of the block (`[1]` is the first).

#### RegenerateResponseAsync

```csharp
IAsyncEnumerable<string> RegenerateResponseAsync(
    long conversationId,
    long userMessageId,
    CancellationToken ct = default);
```

Streams a new answer to a saved user message without saving that message again. The message must
close the conversation, optionally followed by its current answer. The old answer is removed only
after the new one has been saved, so stopping or a failure keeps it.

**Exceptions:** `InvalidOperationException` when the conversation does not exist, the message is
not a user message of the conversation, or anything other than its own answer follows it.

#### Other members

| Member | Description |
|--------|-------------|
| `Task<string> SendMessageAndWaitAsync(long conversationId, string userMessage, CancellationToken ct = default)` | Runs `SendMessageAsync` and returns the whole reply. |
| `ChatContextInspectionSnapshot? GetLatestContextInspection(long conversationId)` | The last context assembly captured for the conversation in this session (what went into the prompt and why), or `null`. |
| `Task<ConversationSummaryRefreshResult> RefreshConversationSummaryInspectionAsync(long conversationId, CancellationToken ct = default)` | Refreshes the conversation's durable summary and the cached snapshot. The result has `Succeeded`, `Snapshot` and `ErrorMessage`; a failure keeps the previous summary. |
| `Task StopGenerationAsync()` | Cancels the reply being generated. `IsGenerating` turns `false` when that reply's stream ends. |

---

### IConversationService

```csharp
namespace AgentX.Core.Services.Chat;

public interface IConversationService
```

Stores conversations and messages with EF Core.

**Implementation:** `ConversationService`

#### Conversations

| Member | Description |
|--------|-------------|
| `Task<ConversationEntity> CreateConversationAsync(string? title = null, string? systemPrompt = null, string? modelId = null)` | Creates a conversation. |
| `Task<ConversationEntity?> GetConversationAsync(long conversationId)` | The conversation with its messages in `SortOrder`, archived ones included; `null` when it does not exist. |
| `Task<IReadOnlyList<ConversationEntity>> GetAllConversationsAsync(bool includeArchived = false)` | Pinned conversations first, then the most recently updated. Archived ones only with `includeArchived`. |
| `Task<IReadOnlyList<ConversationEntity>> GetRecentConversationsAsync(int limit = 5, bool includeArchived = false, CancellationToken ct = default)` | The most recently updated conversations. |
| `Task<IReadOnlyList<ConversationEntity>> SearchConversationsAsync(string query)` | Conversations that are not archived whose title or any message contains the text (SQL `LIKE`), most recently updated first. A blank query returns all conversations that are not archived. |
| `Task UpdateConversationTitleAsync(long conversationId, string title)` | Renames a conversation. |
| `Task TogglePinAsync(long conversationId)` | Toggles `IsPinned`. |
| `Task ArchiveConversationAsync(long conversationId)` | Archives a conversation (hidden from the default list). |
| `Task DeleteConversationAsync(long conversationId)` | Deletes a conversation and its messages. Branches made from it are kept and promoted in the same save. |
| `Task<int> GetConversationCountAsync()` | Number of conversations that are not archived. |
| `Task<long> GetTotalTokensUsedAsync()` | Sum of `TokensUsed` over all conversations. |

#### Messages

| Member | Description |
|--------|-------------|
| `Task<IReadOnlyList<MessageEntity>> GetMessagesAsync(long conversationId)` | Messages in `SortOrder`. |
| `Task AddMessageAsync(long conversationId, string role, string content, int? tokenCount = null, double? generationTimeMs = null, string? modelId = null, string? citationsJson = null)` | Appends a message and updates `MessageCount`, `TokensUsed` (when `tokenCount` is given) and `UpdatedAt`. A blank `modelId` or `citationsJson` is stored as `null`. |
| `Task DeleteMessageAsync(long messageId)` | Deletes one message and updates the conversation. |
| `Task UpdateMessageContentAsync(long messageId, string newContent)` | Changes a message's text (message editing). |
| `Task<int> DeleteMessageAndFollowingAsync(long conversationId, long messageId)` | Deletes a message and every message after it in one save (used when an edited prompt is resent). Returns the number deleted, 0 when the message is not in the conversation. |

#### Folders and tags

| Member | Description |
|--------|-------------|
| `Task SetConversationFolderAsync(long conversationId, string? folderName)` | Sets the folder; `null` removes it from any folder. |
| `Task<IReadOnlyList<string>> GetAllFolderNamesAsync()` | Folder names in use. |
| `Task AddTagToConversationAsync(long conversationId, long tagId)` | Tags a conversation. |
| `Task RemoveTagFromConversationAsync(long conversationId, long tagId)` | Removes a tag from a conversation. |
| `Task<IReadOnlyList<ConversationEntity>> GetConversationsByFolderAsync(string folderName)` | Conversations in a folder. |

---

### ISystemPromptService

```csharp
namespace AgentX.Core.Services.Chat;

public interface ISystemPromptService
```

Manages reusable system prompts.

**Implementation:** `SystemPromptService`

| Member | Description |
|--------|-------------|
| `Task<IReadOnlyList<SystemPromptEntity>> GetAllPromptsAsync(string? category = null)` | All prompts, or one category; favorites first, then by `UsageCount` descending. |
| `Task<SystemPromptEntity?> GetPromptAsync(long id)` | One prompt. |
| `Task<SystemPromptEntity> CreatePromptAsync(string name, string content, string category)` | Creates a user prompt. `ArgumentException` when any argument is blank. |
| `Task UpdatePromptAsync(long id, string name, string content, string category)` | Updates a prompt. `ArgumentException` when any argument is blank. |
| `Task DeletePromptAsync(long id)` | Deletes a prompt. Built-in prompts cannot be deleted (`InvalidOperationException`). |
| `Task ToggleFavoriteAsync(long id)` | Toggles `IsFavorite`. |
| `Task IncrementUsageAsync(long id)` | Adds one to `UsageCount`. |
| `Task SeedBuiltInPromptsAsync()` | Adds the built-in prompts when they do not exist yet (categories `General`, `Writing`, `Code`, `Analysis` and `Creative`). |

---

## 7. Collections and Tags

### ICollectionService

```csharp
namespace AgentX.Core.Services.Collections;

public interface ICollectionService
```

Manages nested document collections and their documents.

**Implementation:** `CollectionService`

| Member | Description |
|--------|-------------|
| `Task<CollectionEntity> CreateCollectionAsync(string name, string? description = null, long? parentId = null)` | Creates a collection, optionally inside a parent. The name must not be empty. |
| `Task<IReadOnlyList<CollectionEntity>> GetAllCollectionsAsync()` | All collections, nested ones included, ordered by `SortOrder` then `Name`, with child collections loaded. Document counts are refreshed first. |
| `Task<IReadOnlyList<CollectionEntity>> GetRootCollectionsAsync()` | Collections without a parent, with their children loaded. |
| `Task<IReadOnlyList<CollectionEntity>> GetChildCollectionsAsync(long parentId)` | The direct children of a collection. |
| `Task<CollectionEntity?> GetCollectionAsync(long collectionId)` | One collection with its document links and children. |
| `Task UpdateCollectionAsync(long collectionId, string name, string? description = null)` | Renames a collection and sets its description. |
| `Task DeleteCollectionAsync(long collectionId, bool deleteDocuments = false)` | Deletes a collection; its children move to its parent. With `deleteDocuments`, its documents are deleted through `IDocumentService.DeleteDocumentAsync`, so their vectors and keyword rows go too. A missing id is logged and ignored. |
| `Task<bool> AddDocumentToCollectionAsync(long documentId, long collectionId)` | Adds a document. Returns `true` when it was added and `false` when it was already in the collection. `InvalidOperationException` when the document or the collection does not exist. |
| `Task RemoveDocumentFromCollectionAsync(long documentId, long collectionId)` | Removes a document from a collection. |
| `Task MoveCollectionAsync(long collectionId, long? newParentId)` | Moves a collection under another one, or to the top level with `null`. `InvalidOperationException` when either collection does not exist, or when the move would put a collection inside itself or one of its descendants. |
| `Task<int> GetCollectionCountAsync()` | Number of collections. |
| `Task<IReadOnlyList<DocumentEntity>> GetDocumentsInCollectionAsync(long collectionId)` | The collection's documents ordered by file name, not tracked by the context. |

---

### IAutoTagService

```csharp
namespace AgentX.Core.Services.Tagging;

public interface IAutoTagService
```

AI tag suggestions and tag management.

**Implementation:** `AutoTagService`

Tag names are normalized: trimmed, composed to Unicode NFC and lowercased; spaces and underscores
become hyphens; everything except letters, combining marks and digits of any script and hyphens is
removed; repeated hyphens are collapsed and edge hyphens trimmed. `"Machine Learning"` becomes
`machine-learning`.

| Member | Description |
|--------|-------------|
| `Task<IReadOnlyList<(string TagName, double Confidence)>> GenerateTagsAsync(string documentContent, int maxTags = 5, CancellationToken ct = default)` | Asks the AI service for tags with confidence scores (0.0 to 1.0). |
| `Task ApplyAutoTagsAsync(long documentId, CancellationToken ct = default)` | Generates tags for a document and saves them. Existing tags are matched by name ignoring case, new ones are created with `IsAutoGenerated = true`, and each normalized tag is applied once per call. |
| `Task<IReadOnlyList<TagEntity>> GetAllTagsAsync()` | All tags by name. |
| `Task<TagEntity> CreateTagAsync(string name, string? colorHex = null)` | Creates a tag with the normalized name. `ArgumentException` for a blank name; `InvalidOperationException` when the tag exists. |
| `Task DeleteTagAsync(long tagId)` | Deletes a tag and its document links. |
| `Task AssignTagAsync(long documentId, long tagId)` | Assigns a tag with confidence 1.0. |
| `Task RemoveTagAsync(long documentId, long tagId)` | Removes a tag from a document. |
| `Task<IReadOnlyList<TagEntity>> GetTagsForDocumentAsync(long documentId)` | Tags of one document. |
| `Task<IReadOnlyDictionary<long, IReadOnlyList<TagEntity>>> GetTagsForDocumentsAsync(IReadOnlyList<long> documentIds)` | Tags of several documents in one call, keyed by document id. |

---

## 8. Indexing Services

### IIndexingService

```csharp
namespace AgentX.Core.Services.Indexing;

public interface IIndexingService : IDisposable
```

The background indexing pipeline: it chunks the extracted text of pending documents, embeds the
chunks, stores the vectors and writes the keyword (FTS5) rows.

**Implementation:** `IndexingService`

**Behavior:**
- `InitializeAsync` runs at app launch, after the AI service is initialized; watch folder
  monitoring starts after it.
- It subscribes to `IDocumentService.DocumentPendingIndexing`, so imports and re-indexes are
  processed during the session, and it reuses the extraction handed over with the event. The idle
  loop also picks up `"pending"` documents written by other paths (Web Import, sync, the local
  API).
- At start, documents and jobs left in `"processing"` by an interrupted session are queued again;
  a document interrupted by shutdown goes back to `"pending"`.
- Chunks are stamped with `EmbeddingModelVersion`, `EmbeddingDimensions` and `EmbeddedAt`. When
  nothing is queued, completed documents whose chunks carry no version, or the legacy
  `all-minilm:1.0`, are embedded again from their stored chunk text, one document at a time.
- If the vector store cannot be initialized, the loop still runs and marks every document it takes
  as failed with the reason.

#### Properties and events

| Member | Type | Description |
|--------|------|-------------|
| `IsProcessing` | `bool` | Whether a document is being processed. |
| `ProgressChanged` | `EventHandler<IndexingProgressEventArgs>?` | Queue state changes. |
| `DocumentIndexed` | `EventHandler<long>?` | A document was indexed; the argument is its id. Raised on the indexing thread. |
| `DocumentIndexingFailed` | `EventHandler<DocumentIndexingFailedEventArgs>?` | Indexing failed, after the document was saved as `"failed"` with the reason. Raised on the indexing thread. |

#### Methods

| Method | Description |
|--------|-------------|
| `Task InitializeAsync(CancellationToken ct = default)` | Initializes the vector store, recovers interrupted work and starts the background loop. |
| `Task IndexDocumentAsync(long documentId, CancellationToken ct = default)` | Queues one document for the background loop. `InvalidOperationException` when the document does not exist. |
| `Task ReindexAllAsync(IProgress<(int Processed, int Total)>? progress = null, CancellationToken ct = default)` | Removes the chunks, vectors and keyword rows of every completed or failed document, resets it to `"pending"` and queues it. |
| `Task<int> GetQueueLengthAsync()` | Documents waiting in the in-memory queue plus the one being processed. |
| `Task<int> GetProcessedCountAsync()` | Number of completed jobs in the indexing job history (`IndexingJobEntity`). |

---

### IFileWatcherService

```csharp
namespace AgentX.Core.Services.Indexing;

public interface IFileWatcherService : IDisposable
```

Watches the registered watch folders and imports new or changed files through `IDocumentService`,
with per-file debouncing. Watch folders are managed in Settings > Knowledge Vault.

**Implementation:** `FileWatcherService`

| Member | Description |
|--------|-------------|
| `Task InitializeAsync(CancellationToken ct = default)` | Startup entry point, called after the indexing pipeline starts. When the `AutoIndexWatchFolders` setting is on, it starts watching every enabled folder and then catches up: files added while the app was closed are imported and files changed since their import are re-indexed. Does nothing when the setting is off. |
| `Task StartWatchingAsync(CancellationToken ct = default)` | Starts a watcher for every enabled folder. |
| `Task StopWatchingAsync()` | Stops all watchers; the folder list is kept. |
| `Task AddWatchFolderAsync(string path, bool includeSubfolders = true, string? fileTypeFilter = null, long? collectionId = null)` | Registers a folder and starts watching it at once. `fileTypeFilter` is a comma-separated extension list such as `"pdf,docx,txt"` (`null` for all supported types). `ArgumentException` for a blank path, `DirectoryNotFoundException` when the folder does not exist, `InvalidOperationException` when the folder is already registered (compared without regard to case) or the collection does not exist. |
| `Task RemoveWatchFolderAsync(long watchFolderId)` | Stops watching a folder and deletes its record. |
| `Task<IReadOnlyList<WatchFolderEntity>> GetWatchFoldersAsync()` | All registered folders. |
| `bool IsWatching` | Whether any folder is being watched. |
| `event EventHandler<string>? FileDetected` | A new or changed file was detected; the argument is its full path. |

---

### Indexing Event Data

#### IndexingProgressEventArgs

```csharp
namespace AgentX.Core.Services.Indexing;

public class IndexingProgressEventArgs : EventArgs
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `QueueLength` | `int` | -- | Items left in the queue (init-only). |
| `Processed` | `int` | -- | Items processed since initialization (init-only). |
| `CurrentDocument` | `string?` | `null` | File name being processed; `null` when idle (init-only). |
| `PercentComplete` | `double?` | `null` | 0 to 100, or `null` when unknown (init-only). |

#### DocumentIndexingFailedEventArgs

```csharp
namespace AgentX.Core.Services.Indexing;

public sealed class DocumentIndexingFailedEventArgs : EventArgs
```

| Property | Type | Description |
|----------|------|-------------|
| `DocumentId` | `long` | The document that could not be indexed. |
| `Error` | `string` | The reason, as saved in `DocumentEntity.IndexingError`. |

---

## 9. Settings

### ISettingsService

```csharp
namespace AgentX.Core.Services.Settings;

public interface ISettingsService
{
    Task<AppSettings> GetSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
    Task<T?> GetValueAsync<T>(string key);
    Task SetValueAsync<T>(string key, T value);
}
```

Reads and writes `%LOCALAPPDATA%\AgentX\settings.json` (camelCase JSON).

**Implementation:** `SettingsService`

| Method | Description |
|--------|-------------|
| `GetSettingsAsync` | Returns the cached settings, loading them on first use. A missing file is created with defaults. A file that cannot be read or parsed is copied to `settings.json.corrupt-<utc>` and defaults are used for the session; it is never overwritten with defaults. Each secret is decrypted on its own: one that cannot be decrypted (DPAPI data from another Windows account or machine) is cleared and logged, the file is copied to `settings.json.undecryptable-<utc>`, and every other setting is kept. Plaintext secrets found on disk are encrypted and rewritten. |
| `SaveSettingsAsync` | Validates the settings with `AppSettingsValidator`, encrypts the secrets and writes the file atomically (a temporary file swapped in), one save at a time. Clearly invalid values throw `SettingsValidationException` and nothing is written; when the rejected object is the cached instance, the cache is dropped so the next read returns the last saved settings. A missing cloud API key is only advisory, and a field that is already invalid on disk does not block unrelated saves. |
| `GetValueAsync<T>(string key)` | Reads the `AppSettings` property named `key` (exact, case-sensitive property name) by reflection; `default(T)` when there is no such property. |
| `SetValueAsync<T>(string key, T value)` | Sets the property named `key` and saves; does nothing when there is no such property. |

**Encrypted fields:** `OpenAiApiKey`, `AnthropicApiKey`, `WebSearchApiKey`, `LocalApiToken`,
`OAuth.Google.ClientSecret`, `OAuth.Microsoft.ClientSecret` and `BackupSchedule.EncryptionPassword`
are stored DPAPI-encrypted on disk and plaintext in memory.

`SettingsValidationException` derives from `ArgumentException`; its `Errors` property lists the
`ValidationError` items that blocked the save.

---

### AppSettings

```csharp
namespace AgentX.Core.Services.Settings;

public class AppSettings
```

The contents of `settings.json`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `OnboardingCompleted` | `bool` | `false` | First-run setup finished. |
| `Theme` | `string` | `"Dark"` | `"Dark"`, `"Light"` or `"Default"` (follows Windows). |
| `LanguageOverride` | `string?` | `null` | UI language (`en-US`, `de`, `es`, `fr`, `ja`, `zh-CN`); `null` follows the Windows display language. |
| `ActiveProviderId` | `string` | `"local"` | Preferred provider: `local`, `ollama`, `openai` or `anthropic`. |
| `LocalModelFileName` | `string` | `"llama-3.2-3b-instruct-q4_k_m.gguf"` | Built-in model file (also its embedding model). |
| `LocalContextSize` | `int` | `8192` | Built-in model context size. |
| `LocalGpuLayers` | `int` | `0` | Built-in model layers on the GPU: 0 automatic, a positive count, or negative for CPU only. |
| `OllamaEndpoint` | `string` | `"http://localhost:11434"` | Ollama server URL. |
| `DefaultModel` | `string` | `"llama3.2"` | Ollama chat model. |
| `EmbeddingModel` | `string` | `"all-minilm"` | Embedding Model setting (see `EmbeddingTargetResolver`). |
| `OpenAiApiKey` | `string?` | `null` | OpenAI API key (encrypted on disk). |
| `OpenAiEndpoint` | `string` | `"https://api.openai.com/v1/"` | OpenAI or compatible endpoint. |
| `OpenAiDefaultModel` | `string?` | `"gpt-4o-mini"` | OpenAI chat model. |
| `AnthropicApiKey` | `string?` | `null` | Anthropic API key (encrypted on disk). |
| `AnthropicEndpoint` | `string` | `"https://api.anthropic.com/v1/"` | Anthropic endpoint. |
| `AnthropicDefaultModel` | `string?` | `"claude-sonnet-5"` | Anthropic chat model (`AnthropicProvider.DefaultModelId`). |
| `Temperature` | `double` | `0.7` | Chat temperature. |
| `MaxTokens` | `int` | `4096` | Maximum tokens per chat reply. |
| `ContextWindow` | `int` | `8192` | Chat context window. |
| `ChunkSize` | `int` | `512` | Tokens per chunk. |
| `ChunkOverlap` | `int` | `50` | Overlap between chunks; must be smaller than `ChunkSize`. |
| `TopKResults` | `int` | `5` | Chunks the RAG pipeline retrieves and keeps. |
| `AutoIndexWatchFolders` | `bool` | `true` | Watch folders are monitored and imported. |
| `EnableModelRouting` | `bool` | `false` | Multi-Model Routing per reply. |
| `ActiveRoutingProfileId` | `string` | `"balanced"` | Routing profile. |
| `EnableResearchMode` | `bool` | `false` | Research Mode web search in chat. |
| `WebSearchProvider` | `WebSearchProvider` | `Brave` | `Brave`, `Serper` or `SearXng`. |
| `WebSearchApiKey` | `string?` | `null` | Brave or Serper API key, or the SearXNG instance URL (encrypted on disk). |
| `MaxSearchResults` | `int` | `10` | Web results per search, used up to 20. |
| `SearchCacheTtlMinutes` | `int` | `60` | Web result cache duration, used up to 1440. |
| `EnableScreenAwareness` | `bool` | `false` | Screen capture for Screen Awareness. |
| `LocalApiEnabled` | `bool` | `true` | Enable Local API. |
| `LocalApiToken` | `string?` | `null` | Local REST API bearer token (encrypted on disk); created the first time the API starts. |
| `EnableHnswIndex` | `bool` | `true` | Use the HNSW vector store. |
| `HnswM` | `int` | `16` | HNSW graph degree. |
| `HnswEfConstruction` | `int` | `200` | HNSW build parameter. |
| `HnswEfSearch` | `int` | `50` | Minimum HNSW search breadth (ef). A query already searches at least max(`HnswEfConstruction`, 2 x candidates), so only a larger value widens the search (better recall, slower queries). |
| `HnswFallbackThreshold` | `int` | `10000` | Below this many vectors the HNSW store scans linearly. |
| `OAuth` | `OAuthSettings` | `new()` | `Google` (`ClientId`, `ClientSecret`, `RedirectUri` default `http://localhost:8400/oauth/callback`), `Microsoft` (`ClientId`, `ClientSecret`, `TenantId` default `common`, `RedirectUri` default `http://localhost:8401/oauth/callback`), `TokenRefreshBufferMinutes` (5) and `AuthTimeoutSeconds` (300). |
| `CalendarConnector` | `CalendarSettings` | `new()` | `EnableCalendarSync` (false), `SyncIntervalMinutes` (15), `DaysPastToSync` (90), `DaysFutureToSync` (30), `ConflictResolution` (`"RemoteWins"`), `IncludeAttendeeDetails` (true), `IncludeDescriptions` (true). |
| `EmailConnector` | `EmailSettings` | `new()` | `EnableEmailSync` (false), `SyncIntervalMinutes` (10), `MessagesPerSync` (50), `DaysBackToSync` (30), `EnableAiCategorization` (true, not applied: messages are categorized by rules), `IncludeBodyContent` (true, not applied: the connector's own `EmailSyncSettings.IncludeHtmlBody` decides whether bodies are kept), `IncludeAttachmentMetadata` (false). |
| `BackupSchedule` | `BackupScheduleConfig` | `new()` | `Enabled` (false), `IntervalHours` (168, at most 720), `MaxBackupsToKeep` (5, 0 keeps all), `DestinationPath`, `EncryptionPassword` (encrypted on disk). |
| `StoragePath` | `string` | `%LOCALAPPDATA%\AgentX` | Folder for the built-in models, the vector store, web imports and exports. Shown read-only in Settings > Storage. |

---

## 10. Intelligence Services

### ISummaryService

```csharp
namespace AgentX.Core.Services.Intelligence;

public interface ISummaryService
```

Document summaries, key points and translation.

**Implementation:** `SummaryService`

| Member | Description |
|--------|-------------|
| `Task<string> SummarizeDocumentAsync(long documentId, CancellationToken ct = default)` | Reads the document's chunks in order, up to 8,000 characters, and builds the summary with `IHierarchicalSummaryService` (section summaries combined into one). `InvalidOperationException` when the document does not exist or has no indexed chunks. |
| `Task<IReadOnlyList<string>> ExtractKeyPointsAsync(long documentId, CancellationToken ct = default)` | Key points from the same hierarchical summary. Same exceptions. |
| `Task<string> TranslateTextAsync(string text, string targetLanguage, CancellationToken ct = default)` | Translates `text` into `targetLanguage` (for example `"Spanish"`). Text longer than 4,000 characters is translated in parts split at paragraph, line or sentence breaks, and the parts are joined in order, so nothing is cut off. Each request uses temperature 0.3 and at most 2,048 output tokens. `ArgumentException` when either argument is blank. |

---

### IDuplicateDetectionService

```csharp
namespace AgentX.Core.Services.Intelligence;

public interface IDuplicateDetectionService
```

**Implementation:** `DuplicateDetectionService`

| Member | Description |
|--------|-------------|
| `Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(CancellationToken ct = default)` | Groups documents with the same SHA-256 content hash (no AI). Each group has at least two documents; an empty list when there are none. |
| `Task<IReadOnlyList<DuplicateGroup>> FindNearDuplicatesAsync(float similarityThreshold = 0.9f, CancellationToken ct = default)` | Groups documents whose chunk embeddings reach `similarityThreshold` (cosine similarity), with the evidence per match. The scan covers at most 500 documents. |

---

### IOrganizationSuggestionService

```csharp
namespace AgentX.Core.Services.Intelligence;

public interface IOrganizationSuggestionService
{
    Task<IReadOnlyList<OrganizationSuggestion>> SuggestOrganizationAsync(
        int maxDocuments = 20, CancellationToken ct = default);
}
```

Asks the AI service to suggest a collection and tags for documents that are in no collection, at
most `maxDocuments` per call. Returns an empty list when every document is in a collection.

**Implementation:** `OrganizationSuggestionService`

---

### Intelligence Models

Namespace `AgentX.Core.Services.Intelligence.Models`.

#### DuplicateGroup

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ContentHash` | `string` | `""` | The shared hash (exact duplicates) or the reference document's hash (near duplicates) (init-only). |
| `MatchKind` | `DuplicateMatchKind` | `Exact` | `Exact` or `Semantic` (init-only). |
| `Documents` | `List<DuplicateDocument>` | `new()` | The documents; the first one is treated as the original (init-only). |
| `WastedStorageBytes` | `long` | *(computed)* | Size of all documents except the first. |

#### DuplicateDocument

| Property | Type | Description |
|----------|------|-------------|
| `DocumentId` | `long` | The document (init-only). |
| `FileName` | `string` | File name (init-only). |
| `FilePath` | `string` | File path (init-only). |
| `FileSizeBytes` | `long` | Size in bytes (init-only). |
| `ImportedAt` | `DateTime` | Import time (init-only). |
| `Evidence` | `DuplicateEvidence?` | For semantic matches: `DocumentId`, `SupportingChunkCount`, `MaxSimilarity`, `AverageSimilarity` and `Confidence`. Unset for exact duplicates (init-only). |

#### OrganizationSuggestion

| Property | Type | Description |
|----------|------|-------------|
| `DocumentId` | `long` | The document (init-only). |
| `FileName` | `string` | Its file name (init-only). |
| `SuggestedCollection` | `string` | An existing or new collection name (init-only). |
| `SuggestedTags` | `List<string>` | Suggested tags (init-only). |
| `Reasoning` | `string` | The model's reason (init-only). |
| `Confidence` | `float` | 0.0 to 1.0 (init-only). |

---

## 11. Database Entities

All entities are EF Core entities of `AgentXDbContext`. The database is the SQLite file
`%LOCALAPPDATA%\AgentX\agentx.db`, encrypted with SQLCipher when database encryption is on. One
context instance is shared by the app, and operations on it are serialized instead of running
concurrently. Dates are stored in UTC.

**Namespace:** `AgentX.Core.Data.Entities`
**Assembly:** `AgentX.Core`

This section covers the entities of the services above. The other tables (annotations, inbox
items, workflows, memories, sync logs, digests and more) are described in
[`DATABASE_SCHEMA.md`](../DATABASE_SCHEMA.md).

---

### ConversationEntity

A chat conversation.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key. |
| `Title` | `string` | `""` | Title. |
| `SystemPrompt` | `string?` | `null` | System prompt for the conversation. |
| `ModelId` | `string` | `""` | Model chosen for the conversation. |
| `CreatedAt` | `DateTime` | *(set on create)* | Creation time. |
| `UpdatedAt` | `DateTime` | *(set on change)* | Last change, including new messages. |
| `IsPinned` | `bool` | `false` | Pinned to the top of the list. |
| `IsArchived` | `bool` | `false` | Hidden from the default list. |
| `MessageCount` | `int` | `0` | Number of messages. |
| `TokensUsed` | `long` | `0` | Sum of the token counts given for its messages. |
| `FolderName` | `string?` | `null` | Folder the conversation is filed in. |
| `ParentConversationId` | `long?` | `null` | For a branch, the conversation it was branched from. |
| `BranchPointMessageId` | `long?` | `null` | For a branch, the message it was branched at. |
| `BranchLabel` | `string?` | `null` | Label of a branch. |

**Navigation Properties:** `Messages` (`ICollection<MessageEntity>`), `ConversationTags`
(`ICollection<ConversationTagEntity>`), `SummarySnapshots`
(`ICollection<ConversationSummarySnapshotEntity>`), `ThemeMembership`
(`ConversationThemeMembershipEntity?`), `SummaryState` (`ConversationSummaryStateEntity?`),
`ParentConversation` (`ConversationEntity?`) and `Branches` (`ICollection<ConversationEntity>`).

---

### MessageEntity

One message of a conversation.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key. |
| `ConversationId` | `long` | -- | Foreign key to `ConversationEntity`. |
| `Role` | `string` | `""` | `"user"`, `"assistant"` or `"system"`. |
| `Content` | `string` | `""` | Message text. |
| `Timestamp` | `DateTime` | *(set on create)* | Creation time. |
| `TokenCount` | `int` | `0` | Token count, when given. |
| `GenerationTimeMs` | `double?` | `null` | Generation time of an assistant message. |
| `ModelId` | `string?` | `null` | The model that wrote an assistant message (the routed model for a routed reply). |
| `CitationsJson` | `string?` | `null` | JSON array of the sources an assistant message cites (`MessageCitations`): web pages as `{"kind":"web","title","url","snippet"}` (Research Mode), documents as `{"fileName","pageNumber","excerpt"}`. |
| `SortOrder` | `int` | `0` | Position in the conversation. |
| `Embedding` | `string?` | `null` | Serialized embedding of the message, written for conversation recall. |
| `EmbeddingModel` | `string?` | `null` | Model that produced `Embedding`. |
| `EmbeddingDimensions` | `int?` | `null` | Vector size of `Embedding`. |
| `EmbeddedAt` | `DateTime?` | `null` | When `Embedding` was written. |

**Navigation Properties:** `Conversation` (`ConversationEntity`).

---

### DocumentEntity

A document in the Knowledge Vault.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key. |
| `FileName` | `string` | `""` | File name (or display name for connector items). |
| `FilePath` | `string` | `""` | Path of the source file. |
| `FileType` | `string` | `""` | Lower-case extension without the dot (for example `"pdf"`), or a type name such as `"CalendarEvent"` or `"EmailMessage"` for connector items. |
| `MimeType` | `string?` | `null` | MIME type. |
| `FileSizeBytes` | `long` | `0` | Size in bytes. |
| `ContentHash` | `string` | `""` | SHA-256 hash, used for duplicate detection. |
| `ImportedAt` | `DateTime` | *(set on create)* | Import time (UTC). |
| `FileModifiedAt` | `DateTime` | *(from file)* | Last write time of the source file. |
| `LastIndexedAt` | `DateTime?` | `null` | Last successful indexing. |
| `IndexingStatus` | `string` | `"pending"` | `"pending"`, `"processing"`, `"completed"` or `"failed"`. |
| `IndexingError` | `string?` | `null` | Why extraction or indexing failed. |
| `ChunkCount` | `int` | `0` | Number of chunks. |
| `PageCount` | `int` | `0` | Number of pages. |
| `WordCount` | `long` | `0` | Word count of the extracted text. |
| `Summary` | `string?` | `null` | Stored summary. |
| `ExtractedTitle` | `string?` | `null` | Title from the document metadata. |
| `Language` | `string?` | `null` | Detected language. |
| `ThumbnailPath` | `string?` | `null` | Thumbnail image path. |
| `MetadataJson` | `string?` | `null` | Extra metadata as JSON. |

**Navigation Properties:** `Chunks` (`ICollection<DocumentChunkEntity>`), `DocumentCollections`
(`ICollection<DocumentCollectionEntity>`) and `DocumentTags` (`ICollection<DocumentTagEntity>`).

---

### DocumentChunkEntity

A chunk of a document's text.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key. |
| `DocumentId` | `long` | -- | Foreign key to `DocumentEntity`. |
| `ChunkIndex` | `int` | `0` | Position in the document. |
| `Content` | `string` | `""` | Chunk text. |
| `StartCharOffset` | `int` | `0` | Start offset in the extracted text. |
| `EndCharOffset` | `int` | `0` | End offset in the extracted text. |
| `PageNumber` | `int?` | `null` | Page number, when known. |
| `SectionTitle` | `string?` | `null` | Section title, when known. |
| `TokenCount` | `int` | `0` | Token count. |
| `IsEmbedded` | `bool` | `false` | Whether the chunk has a stored vector. |
| `VectorRowId` | `long?` | `null` | Row id of the stored vector (from `IVectorStore.InsertEmbeddingAsync`). |
| `EmbeddingModelVersion` | `string?` | `null` | `IEmbeddingService.ModelVersion` of the vector (`provider:model:dimensions`). Search leaves out chunks of another version. |
| `EmbeddingDimensions` | `int?` | `null` | Vector size. |
| `EmbeddedAt` | `DateTime?` | `null` | When the vector was written. |

**Navigation Properties:** `Document` (`DocumentEntity`).

---

### CollectionEntity

A collection of documents; collections can be nested.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key. |
| `Name` | `string` | `""` | Name. |
| `Description` | `string?` | `null` | Description. |
| `IconGlyph` | `string?` | `null` | Segoe Fluent Icons glyph. |
| `ColorHex` | `string?` | `null` | Color, for example `"#3B82F6"`. |
| `ParentCollectionId` | `long?` | `null` | Parent collection; `null` at the top level. |
| `CreatedAt` | `DateTime` | *(set on create)* | Creation time. |
| `UpdatedAt` | `DateTime` | *(set on change)* | Last change. |
| `DocumentCount` | `int` | `0` | Number of documents (kept in step by the services). |
| `SortOrder` | `int` | `0` | Display order. |

**Navigation Properties:** `ParentCollection` (`CollectionEntity?`), `ChildCollections`
(`ICollection<CollectionEntity>`) and `DocumentCollections`
(`ICollection<DocumentCollectionEntity>`).

---

### DocumentCollectionEntity

Link between a document and a collection. The primary key is (`DocumentId`, `CollectionId`).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DocumentId` | `long` | -- | Foreign key to `DocumentEntity`. |
| `CollectionId` | `long` | -- | Foreign key to `CollectionEntity`. |
| `AddedAt` | `DateTime` | *(set on create)* | When the document was added. |

**Navigation Properties:** `Document` (`DocumentEntity`) and `Collection` (`CollectionEntity`).

---

### TagEntity

A tag for documents and conversations.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key. |
| `Name` | `string` | `""` | Normalized name (see `IAutoTagService`), unique ignoring case. |
| `ColorHex` | `string?` | `null` | Color, for example `"#FF5733"`. |
| `IsAutoGenerated` | `bool` | `false` | Created by auto-tagging. |
| `CreatedAt` | `DateTime` | *(set on create)* | Creation time. |

**Navigation Properties:** `DocumentTags` (`ICollection<DocumentTagEntity>`) and
`ConversationTags` (`ICollection<ConversationTagEntity>`).

---

### DocumentTagEntity

Link between a document and a tag. The primary key is (`DocumentId`, `TagId`).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DocumentId` | `long` | -- | Foreign key to `DocumentEntity`. |
| `TagId` | `long` | -- | Foreign key to `TagEntity`. |
| `Confidence` | `double` | `0.0` | Confidence of an auto-generated tag; manual assignments use `1.0`. |
| `AssignedAt` | `DateTime` | *(set on create)* | When the tag was assigned. |

**Navigation Properties:** `Document` (`DocumentEntity`) and `Tag` (`TagEntity`).

---

### SearchHistoryEntity

A search from the Search page, and saved filters.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key. |
| `Query` | `string` | `""` | Query text. |
| `SearchType` | `string` | `"semantic"` | Mode the search ran in: `"semantic"`, `"keyword"` or `"hybrid"`. |
| `ResultCount` | `int` | `0` | Number of results. |
| `SearchedAt` | `DateTime` | *(set on create)* | Search time. |
| `IsSaved` | `bool` | `false` | Saved as a filter. |
| `CollectionFilter` | `string?` | `null` | Comma-separated collection ids. |
| `MinScore` | `double?` | `null` | Saved minimum score. |
| `MaxResults` | `int?` | `null` | Saved result limit. |
| `DateAfter` | `DateTime?` | `null` | Saved lower date bound. |
| `DateBefore` | `DateTime?` | `null` | Saved upper date bound. |
| `SortOrder` | `string?` | `null` | Saved sort order. |

---

### SystemPromptEntity

A reusable system prompt.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key. |
| `Name` | `string` | `""` | Name. |
| `Content` | `string` | `""` | Prompt text. |
| `Category` | `string` | `"General"` | `"General"`, `"Writing"`, `"Code"`, `"Analysis"` or `"Creative"` for the built-in prompts; any text for user prompts. |
| `IsBuiltIn` | `bool` | `false` | Built-in prompt (cannot be deleted). |
| `IsFavorite` | `bool` | `false` | Marked as favorite. |
| `CreatedAt` | `DateTime` | *(set on create)* | Creation time. |
| `UpdatedAt` | `DateTime` | *(set on change)* | Last change. |
| `UsageCount` | `int` | `0` | Times the prompt was used. |

---

### UserSettingsEntity

Key-value settings stored in the database: feature flags (`FeatureFlagService`) and the sync
configuration, device id and state (`SyncService`).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key. |
| `Key` | `string` | `""` | Setting key (unique). |
| `Value` | `string` | `""` | Serialized value. |
| `ValueType` | `string` | `"string"` | `"string"`, `"int"`, `"bool"`, `"double"` or `"json"`. |
| `UpdatedAt` | `DateTime` | *(set on change)* | Last change. |

---

### WatchFolderEntity

A folder watched for automatic import.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key. |
| `FolderPath` | `string` | `""` | Full folder path. |
| `IsEnabled` | `bool` | `false` | Whether the folder is watched (`AddWatchFolderAsync` creates it enabled). |
| `IncludeSubfolders` | `bool` | `false` | Whether subfolders are watched. |
| `FileTypeFilter` | `string?` | `null` | Comma-separated extensions, for example `"pdf,docx,txt,md"`; `null` for all supported types. |
| `TargetCollectionId` | `long?` | `null` | Collection that imported documents are added to. |
| `CreatedAt` | `DateTime` | *(set on create)* | When the folder was added. |
| `LastScanAt` | `DateTime?` | `null` | Last scan. |
| `FilesIndexed` | `int` | `0` | Files imported from the folder. |

**Navigation Properties:** `TargetCollection` (`CollectionEntity?`).

---

### IndexingJobEntity

History of indexing runs. The live queue is kept in memory by `IndexingService`; these rows record
each run.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key. |
| `DocumentId` | `long` | -- | Foreign key to `DocumentEntity`. |
| `Status` | `string` | `"queued"` | `"queued"`, `"processing"`, `"completed"` or `"failed"`. |
| `QueuedAt` | `DateTime` | *(set on create)* | When the job was created. |
| `StartedAt` | `DateTime?` | `null` | Start of processing. |
| `CompletedAt` | `DateTime?` | `null` | End of processing. |
| `ErrorMessage` | `string?` | `null` | Error of a failed job. |
| `ChunksProcessed` | `int` | `0` | Chunks created. |
| `EmbeddingsGenerated` | `int` | `0` | Vectors generated. |
| `ProcessingTimeMs` | `double?` | `null` | Processing time. |

**Navigation Properties:** `Document` (`DocumentEntity`).

---

### OAuthCredentialEntity

```csharp
namespace AgentX.Core.Data.Entities;

public class OAuthCredentialEntity
```

The stored OAuth tokens of one provider (Google or Microsoft). The tokens are DPAPI-encrypted; one
row per provider (`ProviderId` is unique).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key. |
| `ProviderId` | `string` | `""` | `"google"` or `"microsoft"`. Unique. |
| `AccessToken` | `string` | `""` | DPAPI-encrypted access token. |
| `RefreshToken` | `string` | `""` | DPAPI-encrypted refresh token (an encrypted empty value when none was issued). |
| `TokenExpiry` | `DateTime` | -- | When the access token expires (UTC). |
| `Scopes` | `string` | `""` | The scopes requested at sign-in, space-separated. |
| `UserId` | `string` | `""` | The `user_id` field of the token response, when the provider sends one; Google and Microsoft do not, so it is usually empty. |
| `CreatedAt` | `DateTime` | -- | When the credential was first stored (UTC). |
| `UpdatedAt` | `DateTime` | -- | Last refresh or re-authorization (UTC). |

---

## 12. OAuth Services

### IOAuthService

```csharp
namespace AgentX.Core.Services.OAuth;

public interface IOAuthService
```

The OAuth 2.0 authorization code flow for Google and Microsoft, used by the built-in Calendar and
Email connectors. Tokens are stored DPAPI-encrypted in the database and decrypted only in memory.
The service is not offered to installed plugins (see [IPluginContext](#iplugincontext)).

**Namespace:** `AgentX.Core.Services.OAuth`
**Assembly:** `AgentX.Core`

| Method | Return Type | Description |
|--------|------------|-------------|
| `AuthorizeAsync(string provider, string? scopes = null, string? redirectUri = null, CancellationToken cancellationToken = default)` | `Task<OAuthCredential>` | Opens the consent page in the system browser, waits for the redirect on the loopback URI, exchanges the code for tokens and stores them. `scopes` are added to the provider's default scopes (space- or comma-separated; duplicates are dropped). The flow uses PKCE (`S256`) and a one-time `state` value. Throws `ArgumentException` for a blank provider or a redirect URI that does not start with `http://localhost:` or `http://127.0.0.1:`, `OAuthProviderNotConfiguredException` when the provider has no registered configuration, `InvalidOperationException` when consent is denied or the exchange fails, and `OperationCanceledException` on cancellation or when the sign-in times out (`OAuthSettings.AuthTimeoutSeconds`, 300 by default). |
| `GetAccessTokenAsync(string provider)` | `Task<string>` | A valid access token. A token that expires within the refresh buffer (`OAuthSettings.TokenRefreshBufferMinutes`, 5 by default) is refreshed first. Throws `InvalidOperationException` when no credential is stored, when there is no refresh token (the account has to be reconnected), or when the refresh fails. |
| `RefreshTokenAsync(string provider)` | `Task<bool>` | Refreshes the access token with the stored refresh token. `false` when there is no credential or the refresh failed. Refreshes are serialized per provider. |
| `RevokeAsync(string provider)` | `Task` | When the provider has a revocation endpoint (Google), revokes the refresh token (which ends the whole grant) or, without one, the access token; this is best effort. Then deletes the stored credential. Microsoft has no revocation endpoint, so only the local tokens are deleted. |
| `GetCredentialAsync(string provider)` | `Task<OAuthCredential?>` | The stored credential, decrypted, or `null`. Does not refresh. |
| `ApplyProviderSettings(OAuthSettings settings)` | `void` | Registers Google and Microsoft from the client credentials in the settings (OAuth App Credentials on the connector pages): a provider with a client ID is registered, replacing its old configuration; a provider whose client ID is empty is removed. Stored credentials are kept. Blank tenant or redirect URI values fall back to the defaults. Called at startup and when the credentials are saved, so changes apply without a restart. |

`OAuthProviderNotConfiguredException` derives from `InvalidOperationException` and has a
`Provider` property. The connector pages catch it to explain how to set up the OAuth client.

---

### OAuthService

```csharp
namespace AgentX.Core.Services.OAuth;

public sealed class OAuthService : IOAuthService, IDisposable
```

The implementation of `IOAuthService`.

```csharp
public OAuthService(AgentXDbContext db, IDpapiEncryptionService encryption, ILogger logger)
```

| Member | Description |
|--------|-------------|
| `void ApplySettings(OAuthSettings settings)` | Applies `TokenRefreshBufferMinutes` (clamped to 0-60) and `AuthTimeoutSeconds` (clamped to 30-3600). Called at startup. |
| `void RegisterProvider(OAuthProviderConfig config)` | Registers or replaces a provider configuration. An authorization or refresh already running keeps the configuration it started with. `ArgumentException` when `ProviderId` is blank. |
| `bool UnregisterProvider(string provider)` | Removes a configuration; `true` when one was registered. Stored credentials are kept. |
| `IReadOnlyDictionary<string, OAuthProviderConfig> GetRegisteredProviders()` | The registered configurations. |
| `void Dispose()` | Releases the HTTP client and the refresh locks. |

Token requests send `client_secret` only when the client has one; a Microsoft app registered as a
public client (mobile and desktop applications) has none. A re-authorization whose token response
carries no refresh token keeps the stored one.

---

### OAuthProviderConfig

```csharp
namespace AgentX.Core.Services.OAuth;

public sealed class OAuthProviderConfig
```

The configuration of one OAuth provider. All properties are init-only.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ProviderId` | `string` | `""` | `"google"` or `"microsoft"`; matches `OAuthCredential.ProviderId`. |
| `DisplayName` | `string` | `""` | Name for the UI (`"Google"`, `"Microsoft"`). |
| `AuthorizationEndpoint` | `string` | `""` | Authorization URL. |
| `TokenEndpoint` | `string` | `""` | Token URL. |
| `RevocationEndpoint` | `string?` | `null` | Revocation URL; empty or `null` skips server-side revocation. |
| `Scopes` | `string` | `""` | Default scopes, space-separated (commas are accepted too). |
| `ClientId` | `string` | `""` | OAuth client ID. |
| `ClientSecret` | `string` | `""` | Client secret, or empty for a public client. Stored DPAPI-encrypted in `settings.json`. |
| `RedirectUri` | `string` | `""` | Loopback redirect URI. |
| `ExtraAuthParameters` | `Dictionary<string, string>?` | `null` | Extra authorization URL parameters. |

---

### OAuthProviderRegistry

```csharp
namespace AgentX.Core.Services.OAuth;

public static class OAuthProviderRegistry
```

Builds the configurations for Google and Microsoft.

| Member | Description |
|--------|-------------|
| `const string ProviderIdGoogle = "google"` | Google provider id. |
| `const string ProviderIdMicrosoft = "microsoft"` | Microsoft provider id. |
| `static OAuthProviderConfig Google(string clientId, string clientSecret, string redirectUri)` | Endpoints `https://accounts.google.com/o/oauth2/v2/auth`, `https://oauth2.googleapis.com/token` and `https://oauth2.googleapis.com/revoke`; scopes `openid profile email https://www.googleapis.com/auth/calendar.readonly https://www.googleapis.com/auth/gmail.readonly`; extra parameters `access_type=offline` and `prompt=consent`. |
| `static OAuthProviderConfig Microsoft(string clientId, string clientSecret, string tenantId, string redirectUri)` | Endpoints `https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize` and `.../token`, no revocation endpoint; scopes `openid profile email offline_access Calendars.Read Mail.Read User.Read`; extra parameter `prompt=select_account`. |

---

### OAuthCredential

```csharp
namespace AgentX.Core.Services.OAuth;

public sealed class OAuthCredential
```

A decrypted credential, in memory only. All properties are init-only except the computed one.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ProviderId` | `string` | `""` | `"google"` or `"microsoft"`. |
| `AccessToken` | `string` | `""` | Decrypted access token. |
| `RefreshToken` | `string` | `""` | Decrypted refresh token; empty when none was issued. |
| `TokenExpiry` | `DateTime` | -- | Access token expiry (UTC). |
| `RequiresReauthorization` | `bool` | *(computed)* | `true` when there is no refresh token, so the account has to be reconnected once the access token expires. |
| `Scopes` | `string` | `""` | Scopes requested at sign-in, space-separated. |
| `UserId` | `string` | `""` | See `OAuthCredentialEntity.UserId`. |
| `CreatedAt` | `DateTime` | -- | First stored (UTC). |
| `UpdatedAt` | `DateTime` | -- | Last updated (UTC). |

---

## 13. Calendar Connector

### ICalendarService

```csharp
namespace AgentX.Core.Services.Plugins.Calendar;

public interface ICalendarService
```

Calendar operations over the connected providers, backed by `CalendarPlugin`.

**Implementation:** `CalendarService`

| Method | Return Type | Description |
|--------|------------|-------------|
| `GetUpcomingEventsAsync(int daysAhead = 7, CancellationToken cancellationToken = default)` | `Task<IReadOnlyList<CalEvent>>` | Upcoming events of the enabled calendars of all connected providers, sorted by start time. |
| `SyncEventsAsync(CancellationToken cancellationToken = default)` | `Task<SyncResult>` | Runs a sync of all enabled calendars and pushes new and changed events into the Smart Inbox. |
| `GetEventDetailsAsync(string eventId, string sourceProvider, string calendarId, CancellationToken cancellationToken = default)` | `Task<CalEvent?>` | One event with full details, or `null`. |
| `ListAvailableCalendarsAsync(CancellationToken cancellationToken = default)` | `Task<IReadOnlyList<CalendarInfo>>` | Calendars of the connected providers, for the calendar selection list. |
| `IsConnectedAsync()` | `Task<bool>` | Whether at least one provider has stored OAuth credentials. |
| `GetSyncSettingsAsync()` | `Task<CalendarSyncSettings>` | The connector's sync settings. |
| `UpdateSyncSettingsAsync(CalendarSyncSettings settings)` | `Task` | Saves the sync settings to the plugin data folder. |

---

### ICalendarProvider

```csharp
namespace AgentX.Core.Services.Plugins.Calendar;

public interface ICalendarProvider
```

One calendar API. **Implementations:** `GoogleCalendarProvider` (Google Calendar API v3) and
`OutlookCalendarProvider` (Microsoft Graph v1.0, which reads the calendar view so recurring series
are expanded into occurrences, with times in UTC).

| Member | Description |
|--------|-------------|
| `string ProviderId { get; }` | `"google"` or `"microsoft"`. |
| `Task<IReadOnlyList<CalendarInfo>> ListCalendarsAsync(CancellationToken cancellationToken = default)` | Calendars the user can read. |
| `Task<(IReadOnlyList<CalEvent> Events, string? DeltaToken)> GetEventsAsync(string calendarId, DateTime start, DateTime end, string? deltaToken = null, CancellationToken cancellationToken = default)` | Events in a UTC range, or the changes since `deltaToken`, plus the token for the next sync. The built-in providers return a `CalendarEventBatch`, whose `IsCompleteWindow` says whether the read listed the whole range (so an event missing from it is gone) or only changes; a deletion in an incremental read is a `CalEvent` with `IsDeleted` set. A stored token the provider no longer accepts is discarded in favor of a full read. |

---

### CalendarPlugin

```csharp
namespace AgentX.Core.Services.Plugins.Calendar;

public sealed class CalendarPlugin : IPlugin
```

The built-in Calendar Connector. It syncs Google Calendar and Outlook events into the Smart Inbox,
from where they reach the Knowledge Vault. `BuiltinConnectorLifecycleService` starts it and gives it
`IOAuthService` and `IInboxService`.

| Property | Value |
|----------|-------|
| `Id` | `"com.agentx.calendar"` |
| `Name` | `"Calendar Connector"` |
| `Description` | `"Syncs Outlook and Google Calendar events into your knowledge vault for AI-powered search."` |
| `Version` | `"1.0.0"` |
| `Author` | `"AgentX"` |
| `Type` | `PluginType.DataConnector` |

| Member | Description |
|--------|-------------|
| `event EventHandler<SyncResult>? SyncCompleted` | Raised after each sync cycle. |
| `SyncResult? LastSyncResult` | The last sync result, or `null`. |
| `InitializeAsync(IPluginContext context)` | Resolves `IOAuthService` and `IInboxService`, loads the sync settings from the plugin data folder, and creates a provider for each account with stored credentials. Starts no background work. |
| `ActivateAsync()` | Starts the sync timer (`CalendarSyncSettings.SyncIntervalMinutes`). |
| `DeactivateAsync()` | Stops the timer, cancels a running sync, waits up to 10 seconds for it, and saves the sync settings. |
| `Dispose()` | Releases the timer and resources. |

---

### Calendar Models

Namespace `AgentX.Core.Services.Plugins.Calendar.Models`.

#### CalEvent

`public sealed class CalEvent`, all properties init-only.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `string` | `""` | Provider event id (Google `id`, Microsoft `iCalUId`), used for de-duplication. |
| `Title` | `string` | `""` | Title. |
| `Description` | `string?` | `null` | Description; may contain HTML. |
| `Start` | `DateTime` | -- | Start (UTC). |
| `End` | `DateTime` | -- | End (UTC). |
| `Location` | `string?` | `null` | Location or meeting link. |
| `IsAllDay` | `bool` | `false` | All-day event. |
| `IsRecurring` | `bool` | `false` | Part of a recurring series. |
| `IsCancelled` | `bool` | `false` | Cancelled by the organizer but still in the calendar (Outlook). Still synced so the vault copy says so. |
| `IsDeleted` | `bool` | `false` | A deletion notice from an incremental read: only `Id`, `CalendarId` and `SourceProvider` are set. |
| `Attendees` | `IReadOnlyList<CalAttendee>` | `[]` | Attendees, organizer included. |
| `Organizer` | `string?` | `null` | Organizer name. |
| `CalendarName` | `string?` | `null` | Calendar name. |
| `SourceProvider` | `string` | `""` | `"google"` or `"microsoft"`. |
| `HtmlLink` | `string?` | `null` | Link to the event in the provider's web UI. |
| `CalendarId` | `string?` | `null` | Provider calendar id. |

#### CalAttendee

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DisplayName` | `string` | `""` | Name (may be empty). |
| `Email` | `string` | `""` | Email address. |
| `ResponseStatus` | `string` | `"needsAction"` | `"accepted"`, `"declined"`, `"tentative"` or `"needsAction"`. |
| `IsOrganizer` | `bool` | `false` | The attendee organizes the event. |

#### CalendarInfo

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `string` | `""` | Provider calendar id. |
| `Name` | `string` | `""` | Calendar name. |
| `Owner` | `string?` | `null` | Owner name or email. |
| `EventCount` | `int` | `0` | Approximate number of events in the sync window. |
| `SourceProvider` | `string` | `""` | `"google"` or `"microsoft"`. |
| `IsPrimary` | `bool` | `false` | The user's primary calendar. |
| `LastSyncedAt` | `DateTime?` | `null` | Last successful sync (UTC). |

#### SyncResult

Result of a calendar or email sync. The email connector uses the same type.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ItemsAdded` | `int` | -- | New items added to the inbox. |
| `ItemsUpdated` | `int` | -- | Items changed since the last sync. |
| `ItemsSkipped` | `int` | -- | Unchanged items. |
| `ItemsFailed` | `int` | -- | Items that failed (each is logged). |
| `ItemsRemoved` | `int` | -- | Stored items retired because they are gone at the source (for a calendar, also events moved out of the synced range). One that reached the vault is marked as removed and its document kept; one that never did is taken out of the inbox. |
| `TotalItemsProcessed` | `int` | *(computed)* | `ItemsAdded + ItemsUpdated + ItemsSkipped + ItemsRemoved + ItemsFailed`. |
| `IsSuccess` | `bool` | *(computed)* | `ItemsFailed == 0`. |
| `StartedAt` | `DateTime` | -- | Start (UTC). |
| `CompletedAt` | `DateTime` | -- | End (UTC). |
| `Duration` | `TimeSpan` | *(computed)* | `CompletedAt - StartedAt`. |
| `DeltaToken` | `string?` | `null` | Token for the next incremental sync, when the provider has one. |

#### CalendarSyncSettings

Saved as JSON in the plugin data folder.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EnabledCalendars` | `Dictionary<string, bool>` | `new()` | Calendar id to enabled; only `true` calendars are synced. |
| `SyncIntervalMinutes` | `int` | `15` | Sync interval. |
| `DaysFutureToSync` | `int` | `30` | Days ahead to sync. |
| `DaysPastToSync` | `int` | `90` | Days back to sync. |
| `ConflictResolution` | `string` | `"RemoteWins"` | `"LocalWins"`, `"RemoteWins"` or `"Merge"`. |
| `IncludeAttendeeDetails` | `bool` | `true` | Include attendee names, emails and responses. |
| `IncludeDescriptions` | `bool` | `true` | Include event descriptions. |

#### CalendarEventBatch and CalendarRemovalReason

`public sealed class CalendarEventBatch : ReadOnlyCollection<CalEvent>` is what the built-in
providers return from `GetEventsAsync`; `IsCompleteWindow` is `true` for a full read of the range
and `false` for the changes since a delta token. `public enum CalendarRemovalReason` says why the
sync retires a stored event: `Deleted` (an incremental read reported it deleted or cancelled) or
`NoLongerListed` (a full read of the range no longer lists it).

---

## 14. Email Connector

### IEmailService

```csharp
namespace AgentX.Core.Services.Plugins.Email;

public interface IEmailService
```

Email operations over the connected providers, backed by `EmailPlugin`.

**Implementation:** `EmailService`

| Method | Return Type | Description |
|--------|------------|-------------|
| `GetRecentMessagesAsync(int count = 20, CancellationToken cancellationToken = default)` | `Task<IReadOnlyList<EmailMessage>>` | Recent messages of the enabled folders of all providers. |
| `SyncMessagesAsync(CancellationToken cancellationToken = default)` | `Task<SyncResult>` | Runs a sync of all providers and enabled folders and pushes new and changed messages into the Smart Inbox. |
| `ListAvailableFoldersAsync(CancellationToken cancellationToken = default)` | `Task<IReadOnlyList<EmailFolderInfo>>` | The folders of every connected account, whether or not sync is on (used to choose which folders to sync). A provider that cannot be reached is left out and logged. |
| `GetSyncSettingsAsync()` | `Task<EmailSyncSettings>` | The sync settings. |
| `UpdateSyncSettingsAsync(EmailSyncSettings settings)` | `Task` | Saves the sync settings. |
| `IsConnectedAsync()` | `Task<bool>` | Whether the plugin has at least one provider; it creates one for each account with stored credentials when it is activated. |

---

### IEmailProvider

```csharp
namespace AgentX.Core.Services.Plugins.Email;

public interface IEmailProvider
```

One mail API. **Implementations:** `GmailProvider` (Gmail API v1; incremental sync through the
history id) and `OutlookEmailProvider` (Microsoft Graph v1.0; incremental sync through delta links).

| Member | Description |
|--------|-------------|
| `const string InboxFolderId = "INBOX"` | The folder id every provider uses for the inbox (Gmail's label id; Outlook reports its inbox under it too). |
| `string ProviderId { get; }` | `"google"` or `"microsoft"`. |
| `Task<IReadOnlyList<EmailFolderInfo>> ListFoldersAsync(CancellationToken cancellationToken = default)` | Folders or labels of the account; the inbox has the id `InboxFolderId`. |
| `Task<(IReadOnlyList<EmailMessage> Messages, string? DeltaToken)> GetMessagesAsync(string folderId, int maxResults = 50, string? deltaToken = null, DateTime? receivedAfterUtc = null, CancellationToken cancellationToken = default)` | Messages of a folder. A full read (no token) returns only messages received after `receivedAfterUtc` (the "days back" setting); an incremental read returns the changes since `deltaToken` and ignores it. The returned token is the provider's sync position, or a continuation when `maxResults` stopped the read early, so the next call resumes there. |

---

### EmailPlugin

```csharp
namespace AgentX.Core.Services.Plugins.Email;

public sealed class EmailPlugin : IPlugin
```

The built-in Email Connector. It syncs Gmail and Outlook mail into the Smart Inbox.
`BuiltinConnectorLifecycleService` starts it and gives it `IOAuthService` and `IInboxService`.

| Property | Value |
|----------|-------|
| `Id` | `"com.agentx.email"` |
| `Name` | `"Email Connector"` |
| `Description` | `"Syncs Gmail and Outlook emails into the knowledge vault."` |
| `Version` | `"1.0.0"` |
| `Author` | `"AgentX"` |
| `Type` | `PluginType.DataConnector` |

| Member | Description |
|--------|-------------|
| `IReadOnlyList<IEmailProvider> Providers` | The registered providers. |
| `event EventHandler<SyncResult>? SyncCompleted` | Raised after each sync cycle. |
| `SyncResult? LastSyncResult` | The last sync result, or `null`. |
| `EmailSyncSettings GetSettings()` / `void UpdateSettings(EmailSyncSettings settings)` | Reads or replaces the sync settings. |
| `Task<IReadOnlyList<IEmailProvider>> GetProvidersForFolderListingAsync()` | A provider for each account with stored credentials, for listing folders while sync is off. |
| `InitializeAsync(IPluginContext context)` | Resolves `IOAuthService` and `IInboxService` and loads the sync settings (`email-sync-settings.json` in the plugin data folder). |
| `ActivateAsync()` | Creates a provider for each account with stored credentials, the triage processor and the sync service, and starts the sync timer (first run after one minute, then every `SyncIntervalMinutes`). |
| `DeactivateAsync()` | Stops the timer, cancels a running sync and waits up to 10 seconds for it. |
| `Dispose()` | Releases the timer and resources. |

---

### Email Models

Namespace `AgentX.Core.Services.Plugins.Email.Models`.

#### EmailMessage

`public sealed class EmailMessage`, all properties init-only.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `string` | `""` | Provider message id. |
| `Subject` | `string` | `""` | Subject. |
| `BodyPreview` | `string` | `""` | Short preview. |
| `BodyHtml` | `string` | `""` | HTML body as received (not stored). |
| `BodyText` | `string` | `""` | Plain-text body. |
| `From` | `EmailContact` | `new()` | Sender. |
| `To` | `List<EmailContact>` | `[]` | To recipients. |
| `Cc` | `List<EmailContact>` | `[]` | Cc recipients. |
| `Bcc` | `List<EmailContact>` | `[]` | Bcc recipients. |
| `ReceivedAt` | `DateTime` | -- | Receive time (UTC). |
| `IsRead` | `bool` | `false` | Read. |
| `IsStarred` | `bool` | `false` | Starred or flagged. |
| `HasAttachments` | `bool` | `false` | Has attachments. |
| `FolderName` | `string` | `""` | Folder or label name. |
| `FolderId` | `string` | `""` | Folder id. |
| `ThreadId` | `string` | `""` | Thread id. |
| `SourceProvider` | `string` | `""` | `"google"` or `"microsoft"`. |
| `AttachmentNames` | `List<string>` | `[]` | Attachment file names. |
| `WebLink` | `string?` | `null` | Link to the message in the provider's web UI. |

#### EmailContact

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DisplayName` | `string` | `""` | Name. |
| `EmailAddress` | `string` | `""` | Address. |
| `IsMe` | `bool` | `false` | The signed-in user. |

#### EmailFolderInfo

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `string` | `""` | Folder or label id. |
| `Name` | `string` | `""` | Name. |
| `TotalCount` | `int` | `0` | Messages in the folder. |
| `UnreadCount` | `int` | `0` | Unread messages. |
| `SourceProvider` | `string` | `""` | `"google"` or `"microsoft"`. |

#### EmailSyncSettings

`public sealed class EmailSyncSettings`, saved as camelCase JSON in the plugin data folder.
`static EmailSyncSettings Load(string path)` returns defaults for a missing file and also for an
unreadable or corrupt one (a corrupt file is kept as `{path}.corrupt`); `void Save(string path)`
writes atomically through a temporary file.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EnabledFolders` | `Dictionary<string, bool>` | `{ ["INBOX"] = true }` | Folder id to enabled. |
| `SyncIntervalMinutes` | `int` | `10` | Sync interval. |
| `MaxMessagesPerSync` | `int` | `50` | Messages read per sync cycle. |
| `SyncDaysBack` | `int` | `30` | How far back the first (full) sync of a folder reaches; later syncs are incremental. |
| `EnableAiCategorization` | `bool` | `true` | Not applied: no AI categorizes email (see the note below). Kept so existing settings files load. |
| `CategorizationPrompt` | `string?` | `null` | Not applied. |
| `IncludeHtmlBody` | `bool` | `true` | For a message without a plain-text part: when on, its HTML body is converted to readable text (`HtmlParser.ConvertToPlainText`), stored, indexed and used for the inbox preview; when off, the message is stored with its headers and preview only. A plain-text part is always kept and raw HTML is never stored. Saved as `includeHtmlBodyText`; the old `includeHtmlBody` key is ignored. |
| `IncludeAttachmentNames` | `bool` | `true` | Include attachment names in the indexed content. |

**Triage categories.** Email triage is rule-based. `EmailTriageProcessor.Classify` gives each
message one `EmailCategory` from ordered keyword and sender rules, and the first match wins:
`ActionRequired`, `Meeting`, `Financial`, `Social`, `Promotion`, `Newsletter`, `Notification`,
otherwise `Other`. The rules run offline and give the same category for the same message every
time. The category name is stored in `InboxItemEntity.SourceCategory`. There is no setting to turn
categorization off.

---

## 15. Plugin Infrastructure

### IPlugin

```csharp
namespace AgentX.Core.Services.Plugins;

public interface IPlugin : IDisposable
```

The interface every plugin's entry type implements. Plugins are loaded into collectible
`AssemblyLoadContext` instances by `IPluginService`. They run in-process with the user's rights;
the host isolates their assemblies but does not sandbox file-system or network access.

| Member | Description |
|--------|-------------|
| `string Id` | Stable plugin id; matches `PluginManifest.Id`. |
| `string Name`, `string Version`, `string Author`, `string Description` | Display metadata. |
| `PluginType Type` | The plugin's main extension point. |
| `Task InitializeAsync(IPluginContext context)` | Called once after the assembly is loaded. One-time setup only; no background work. |
| `Task ActivateAsync()` | Called when the user enables the plugin, and at startup for a plugin left enabled. The host waits 30 seconds and treats a slower activation as a failure. |
| `Task DeactivateAsync()` | Called before the plugin is disabled or uninstalled, and at shutdown. The host waits at most 10 seconds, then disposes and unloads the plugin anyway. |
| `void Dispose()` | Called after deactivation. |

Installing a plugin only extracts it and records it as disabled; nothing is loaded until it is
enabled.

---

### IPluginContext

```csharp
namespace AgentX.Core.Services.Plugins;

public interface IPluginContext
```

The host resources given to a plugin in `InitializeAsync`. Plugins never receive the root
`IServiceProvider`.

| Property | Type | Description |
|----------|------|-------------|
| `Services` | `IServiceProvider` | A scoped provider with the host services approved for plugins: currently `IInboxService` alone. `IOAuthService` is deliberately not offered, because it can return the stored Google and Microsoft refresh tokens; only the built-in Calendar and Email connectors receive it, from `BuiltinConnectorLifecycleService`. |
| `PluginDataPath` | `string` | The plugin's own data folder, `%LOCALAPPDATA%\AgentX\Plugins\<plugin id>\data`, created before `InitializeAsync`. A convention, not a sandbox; manifest permissions are informational only. |
| `Logger` | `ILogger` | Serilog logger tagged with the plugin id and version. |

---

### PluginType

```csharp
namespace AgentX.Core.Services.Plugins;

public enum PluginType
```

Only `DocumentProcessor` and `DataConnector` have a host integration today; the other values are
labels the Plugin Manager shows. Every plugin receives the same `IPluginContext` services, whatever
its type.

| Value | Name | Description |
|-------|------|-------------|
| `0` | `DocumentProcessor` | Adds text extraction for file formats. The entry type implements `IDocumentProcessorPlugin`; while the plugin is active the host offers it every file that no built-in processor claims (`IPluginDocumentProcessorSource`). |
| `1` | `AiProvider` | Label only: the host does not call such plugins yet. |
| `2` | `QuickAction` | Label only: the host does not call such plugins yet. |
| `3` | `WorkflowStep` | Label only: the host does not call such plugins yet. |
| `4` | `DataConnector` | Pushes external items into the Smart Inbox through the `IInboxService` in `IPluginContext.Services`. |
| `5` | `Theme` | Label only: the host does not call such plugins yet. |
| `6` | `Custom` | Catch-all label; no host integration. |

---

### IDocumentProcessorPlugin

```csharp
namespace AgentX.Core.Services.Plugins;

public interface IDocumentProcessorPlugin : IPlugin, IDocumentProcessor
{
}
```

The entry type of a `DocumentProcessor` plugin (implementing `IDocumentProcessor` directly works
too). Built-in processors take precedence: a plugin processor is asked only for files that no
built-in processor claims, so a plugin can add formats but cannot change how PDF, DOCX, text,
Markdown, code, image, audio or web files are read.

---

### IPluginDocumentProcessorSource

```csharp
namespace AgentX.Core.Services.Plugins;

public interface IPluginDocumentProcessorSource
{
    IReadOnlyList<IDocumentProcessor> GetDocumentProcessors();
}
```

Returns a snapshot of the processors of the active plugins whose entry type implements
`IDocumentProcessor`, empty when there are none. `DocumentService` asks it after the built-in
processors, each time it chooses a processor; callers must not cache the list, because plugins are
enabled, disabled and unloaded at run time. The returned processors guard `CanProcess` and
`SupportedExtensions`, so a faulty plugin cannot break processor selection for other files;
exceptions from `ProcessAsync` fail only that import.

**Implementation:** `PluginService` (the same instance that is registered as `IPluginService`).

---

## 16. Local REST API Host

The HTTP routes, request and response bodies and status codes are documented in
[`API_ENDPOINTS.md`](../API_ENDPOINTS.md#local-rest-api). This section covers the .NET types.

### IApiHostService

```csharp
namespace AgentX.Core.Services.Api;

public interface IApiHostService
```

The local REST API host, an `HttpListener` inside the desktop process.

**Implementation:** `ApiHostService` (also `IAsyncDisposable`). In the app,
`ApiHostLifecycleService` (`IApiHostLifecycleService` in `AgentX.App.Services`) starts it on port
9846 after the database migration, creates the token on first start, and applies Settings changes
at run time through `ApplySettingsAsync`.

| Member | Description |
|--------|-------------|
| `bool IsRunning` | Whether the listener is running. |
| `int Port` | The port it was started on. |
| `string BaseUrl` | For example `http://localhost:9846/`. |
| `Task StartAsync(int port = 9846, string? authToken = null, CancellationToken ct = default)` | Starts listening on `http://localhost:{port}/` with the given bearer token. Calling it while running does nothing. A null or empty token fails closed: every route except the public extension health probe returns 401. Throws `HttpListenerException` when the listener cannot start (for example the port is in use). |
| `void SetAuthToken(string? authToken)` | Replaces the bearer token for the next request, so a regenerated token works at once and the previous one stops working, without a restart. Null or empty locks every data route. Safe to call from any thread. |
| `Task StopAsync(CancellationToken ct = default)` | Stops the listener. Calling it when stopped does nothing. |

The host processes at most 16 requests at a time and logs every request with its status and
duration.

---

### LocalApiSecurity

```csharp
namespace AgentX.Core.Services.Api;

public static class LocalApiSecurity
```

The authorization and CORS decisions of the host, free of `HttpListener` types so they are unit
tested directly.

| Member | Description |
|--------|-------------|
| `static bool IsPublicPath(string path)` | `true` only for `/api/extension/health` (ignoring case). |
| `static bool IsAuthorized(string? authorizationHeader, string? expectedToken)` | `true` when the header is `Bearer <token>` (scheme ignoring case, token trimmed) and the token equals `expectedToken`, compared in constant time. `false` whenever `expectedToken` is null or empty. |
| `static string? ResolveAllowedOrigin(string? origin)` | Echoes an origin that starts with `chrome-extension://`, `moz-extension://` or `ms-browser-extension://`; `null` for everything else, so web pages get no CORS grant. |
| `static string GenerateToken()` | A new random 256-bit token as 64 uppercase hexadecimal characters. |

---

### API Models

Namespace `AgentX.Core.Services.Api.Models`. The host serializes them as camelCase JSON and leaves
out null properties.

| Type | Shape |
|------|-------|
| `ApiResponse<T>` | `bool Success`, `T? Data`, `string? Error`, `DateTime Timestamp` (UTC, set when created). Factories `Ok(T data)` and `Fail(string error)`. |
| `ApiDocumentDto` | `record ApiDocumentDto(long Id, string FileName, string FileType, long FileSizeBytes, DateTime ImportedAt, string IndexingStatus)` |
| `ApiConversationDto` | `record ApiConversationDto(long Id, string Title, string ModelId, DateTime CreatedAt, DateTime UpdatedAt, int MessageCount, long TokensUsed)` |
| `ApiCollectionDto` | `record ApiCollectionDto(long Id, string Name, string? Description, int DocumentCount, DateTime CreatedAt)` |
| `ApiSearchRequest` | `string Query` (default `""`), `int TopK` (default 10), `float MinScore` (default 0.3) |
| `ApiSearchResultDto` | `record ApiSearchResultDto(long DocumentId, string FileName, string ChunkContent, float Score)` |
| `ApiHealthDto` | `string Status` (`"ok"`), `string Version`, `string Uptime`, `long DocumentCount`, `int ConversationCount` |
| `ApiClipRequest` | `string Title`, `string Content`, `string SourceUrl`, `string? Author`, `string? PublishedDate` (read by a lenient converter: a JSON string as is, a number as its text, anything else as null), `string ClipMode` (default `"selection"`), `int WordCount`, `Dictionary<string, string>? Metadata` |
| `ApiClipResponse` | `long InboxItemId`, `string Status`, `string Message` |
| `ApiExtensionHealthDto` | `bool Connected`, `string Version`, `bool InboxEnabled`, `string Provider` |
| `ApiAuthCheckDto` | `bool Authenticated`, `string Version` |

---

## 17. Database Encryption

### IDatabaseEncryptionManager

```csharp
namespace AgentX.Core.Services.Security;

public interface IDatabaseEncryptionManager
```

Turns on at-rest encryption (SQLCipher, AES-256) for the live application database and reports
whether it is on. It is the one entry point the UI uses (Settings > Database Encryption).

**Implementation:** `DatabaseEncryptionManager`

| Member | Description |
|--------|-------------|
| `bool IsEncryptionEnabled` | `true` when the encryption marker file (`encryption.info.json`) exists. |
| `KeyStorageMode? ProvisionedMode` | The key storage mode recorded in the marker, or `null` when the database is not encrypted or the marker cannot be read. |
| `Task<bool> EnableEncryptionAsync(CancellationToken ct = default)` | Provisions a DPAPI-wrapped key, releases the shared database connection and suspends the vector store, migrates and verifies the file, writes the encryption marker last, and reopens the connection with the key that matches the file. Returns `false` without doing anything when encryption is already on, `true` after a successful migration. On failure the database stays plaintext, no marker is written, the connection is reopened without a key, and the exception propagates. |

The key state lives outside the encrypted database, in the marker file
`%LOCALAPPDATA%\AgentX\encryption.info.json`, because the database cannot be opened without the key.
`KeyStorageMode` has two values: `DpapiWrapped = 0` (a generated 32-byte key, DPAPI-wrapped and tied
to the Windows account; the mode `EnableEncryptionAsync` uses) and `UserPassphrase = 1` (a key
derived from a passphrase entered at each launch with PBKDF2-HMAC-SHA256, kept for databases
encrypted by older builds).

---

## 18. Draft As Me

### IVoiceDraftService

```csharp
namespace AgentX.Core.Services.TemporalIdentity;

public interface IVoiceDraftService
{
    Task<VoiceDraft?> StartDraftAsync(VoiceDraftRequest request, CancellationToken ct = default);
}
```

"Draft as Me" on the Past Self page: the active AI provider writes a draft in the user's voice from
what Temporal Identity has recorded about how the user writes and what the user thought.

**Implementation:** `VoiceDraftService`

**StartDraftAsync** prepares the prompt and returns the draft ready to stream. The prompt carries
the learned voice profile, the stances the user held on related topics at `request.At`, and the
insights saved by then, and asks the model not to invent facts about the user. A context longer
than 6,000 characters is cut to its first 6,000. The request uses temperature 0.7 and at most
1,024 output tokens.

**Returns:** The draft, or `null` when no AI provider is available (none is set up, or the active
one cannot be reached); nothing is sent to a provider then.

**Exceptions:** `ArgumentException` when the request has no context; `OperationCanceledException`
when `ct` is cancelled.

| Type | Shape |
|------|-------|
| `VoiceDraftRequest` | `record VoiceDraftRequest(string Context, string? Goal, DateTime? At)`. `Context` is required; `At` is the point in time whose views the draft follows (only stances and insights recorded by then are used), `null` for now. |
| `VoiceDraft` | `record VoiceDraft(VoiceDraftBasis Basis, IAsyncEnumerable<string> Text)`. Enumerating `Text` runs the model; a provider failure surfaces there, and cancelling the token stops it. |
| `VoiceDraftBasis` | `record VoiceDraftBasis(string WrittenBy, DateTime AsOf, VoiceProfileEntity? VoiceProfile, IReadOnlyList<VoiceDraftView> Views, IReadOnlyList<VoiceDraftInsight> Insights)`. `WrittenBy` names the model and provider, for example `"llama3.2 (Ollama)"`. |
| `VoiceDraftView` | `record VoiceDraftView(string Topic, string Stance)`. |
| `VoiceDraftInsight` | `record VoiceDraftInsight(string Text, DateTime SavedAt)`. |

---

*This reference was checked against the `AgentX.Core` source on 2026-09-27.*
