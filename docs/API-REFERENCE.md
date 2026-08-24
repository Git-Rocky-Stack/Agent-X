# Agent-X Service API Reference

**Application:** Agent-X
**Platform:** Windows Desktop (.NET 8 / WinUI 3)
**Core Library:** `AgentX.Core`
**Last Updated:** 2026-04-16

---

## Table of Contents

1. [AI Services](#1-ai-services)
   - [IAiService](#iaiservice)
   - [IAiProvider](#iaiprovider)
   - [IHardwareDetector](#ihardwaredetector)
   - [IModelManager](#imodelmanager)
   - [IEmbeddingService](#iembeddingservice)
2. [AI Models](#2-ai-models)
   - [ChatMessage](#chatmessage)
   - [ChatOptions](#chatoptions)
   - [AiModel](#aimodel)
   - [HardwareCapability](#hardwarecapability)
   - [ModelDownloadProgress](#modeldownloadprogress)
3. [Document Services](#3-document-services)
   - [IDocumentService](#idocumentservice)
   - [IDocumentProcessor](#idocumentprocessor)
   - [IChunkingService](#ichunkingservice)
   - [Document Models](#document-models)
4. [Search and RAG](#4-search-and-rag)
   - [ISemanticSearchService](#isemanticsearchservice)
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
   - [IIndexingQueueService](#iindexingqueueservice)
   - [IFileWatcherService](#ifilewatcherservice)
   - [IndexingProgressEventArgs](#indexingprogresseventargs)
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
    - [IOAuthService](#ioauthservice-1)
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
    - [IPluginContext](#iplugincontext)
    - [PluginType](#plugintype)

---

## 1. AI Services

### IAiService

```csharp
namespace AgentX.Core.AI;

public interface IAiService : IDisposable
```

High-level AI service that orchestrates provider selection and provides the primary interface for all AI operations. Wraps the active `IAiProvider` and adds application-specific capabilities such as summarization and tagging.

**Namespace:** `AgentX.Core.AI`
**Assembly:** `AgentX.Core`
**Implementation:** `AiService` (sealed)

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ActiveProvider` | `IAiProvider` | The currently active AI provider instance. Throws `InvalidOperationException` if the service has not been initialized via `InitializeAsync`. |
| `IsConnected` | `bool` | Indicates whether the active provider is connected and operational. Updated during initialization and provider switching. |
| `ActiveModelId` | `string` | The model identifier currently selected for inference. Set during initialization from persisted settings and updated by `SetActiveModelAsync`. |

#### Methods

---

##### InitializeAsync

```csharp
Task InitializeAsync(CancellationToken ct = default);
```

Initializes the AI service by creating providers and establishing the initial connection based on persisted settings.

**Behavior:**
- Reads the `OllamaEndpoint` from `AppSettings` via `ISettingsService`.
- Creates an `OllamaProvider` instance with the configured endpoint URI.
- Tests the connection via `CheckConnectionAsync`.
- On success, sets `IsConnected = true` and `ActiveModelId` to the `DefaultModel` from settings.
- On connection failure, the provider is still assigned (allowing retry) but `IsConnected` is set to `false`.
- Throws on non-connection errors (e.g., invalid URI).

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ct` | `CancellationToken` | `default` | Cancellation token. |

---

##### SwitchProviderAsync

```csharp
Task<bool> SwitchProviderAsync(string providerId, CancellationToken ct = default);
```

Switches the active provider to the one identified by `providerId`.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `providerId` | `string` | -- | The provider identifier (e.g., `"ollama"`). Case-insensitive lookup. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** `true` if the switch succeeded and the new provider is connected; `false` if the provider was not found or the connection check failed.

**Exceptions:**
- `ArgumentException` -- `providerId` is null or whitespace.
- `ObjectDisposedException` -- Service has been disposed.

---

##### SetActiveModelAsync

```csharp
Task SetActiveModelAsync(string modelId, CancellationToken ct = default);
```

Sets the active model for subsequent inference operations and persists the choice to `AppSettings.DefaultModel`.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `modelId` | `string` | -- | The model identifier to activate (e.g., `"llama3.2"`). |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Behavior:**
- Updates the in-memory `ActiveModelId` immediately.
- Persists the selection to settings. If persistence fails, the in-memory value is still updated and a warning is logged.

**Exceptions:**
- `ArgumentException` -- `modelId` is null or whitespace.
- `ObjectDisposedException` -- Service has been disposed.

---

##### StreamChatAsync

```csharp
IAsyncEnumerable<string> StreamChatAsync(
    IReadOnlyList<ChatMessage> messages,
    string? systemPrompt = null,
    ChatOptions? options = null,
    CancellationToken ct = default);
```

Streams a chat completion token-by-token. Optionally prepends a system prompt message to the conversation history before sending to the provider.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `messages` | `IReadOnlyList<ChatMessage>` | -- | The conversation message history. |
| `systemPrompt` | `string?` | `null` | Optional system prompt text. When provided, a `ChatMessage` with `Role = "system"` is prepended to the message list. |
| `options` | `ChatOptions?` | `null` | Optional inference parameters. If `null` or if `ModelId` is not set, the `ActiveModelId` is used. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** `IAsyncEnumerable<string>` -- An async stream of generated text tokens.

**Exceptions:**
- `InvalidOperationException` -- No active provider (service not initialized).
- `ObjectDisposedException` -- Service has been disposed.

---

##### ChatAsync

```csharp
Task<string> ChatAsync(
    IReadOnlyList<ChatMessage> messages,
    string? systemPrompt = null,
    ChatOptions? options = null,
    CancellationToken ct = default);
```

Generates a complete (non-streaming) chat response. Optionally prepends a system prompt to the conversation history.

**Parameters:** Same as `StreamChatAsync`.

**Returns:** `string` -- The full generated response text.

**Exceptions:**
- `InvalidOperationException` -- No active provider.
- `ObjectDisposedException` -- Service has been disposed.

---

##### SummarizeAsync

```csharp
Task<string> SummarizeAsync(string content, CancellationToken ct = default);
```

Generates a concise AI summary of the provided content.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `content` | `string` | -- | The text content to summarize. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** `string` -- A summary of 2-3 paragraphs maximum.

**Behavior:** Uses a built-in system prompt instructing the model to produce a clear, concise summary focused on key points and main ideas.

**Exceptions:**
- `ArgumentException` -- `content` is null, empty, or whitespace.
- `ObjectDisposedException` -- Service has been disposed.

---

##### GenerateTagsAsync

```csharp
Task<IReadOnlyList<string>> GenerateTagsAsync(
    string content,
    int maxTags = 5,
    CancellationToken ct = default);
```

Generates descriptive tags for the provided content using the active model.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `content` | `string` | -- | The text content to generate tags for. |
| `maxTags` | `int` | `5` | Maximum number of tags to generate. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** `IReadOnlyList<string>` -- A list of lowercase, deduplicated tags (1-3 words each).

**Behavior:**
- Instructs the model to return a JSON array of tag strings.
- Parses the response as JSON first. If JSON parsing fails (e.g., model returns non-JSON text), falls back to splitting by commas/newlines and cleaning the results.
- Tags are lowercased, deduplicated, and capped at `maxTags`.
- On complete failure, returns an empty list rather than throwing.

**Exceptions:**
- `ArgumentException` -- `content` is null, empty, or whitespace.
- `ObjectDisposedException` -- Service has been disposed.

---

### IAiProvider

```csharp
namespace AgentX.Core.AI;

public interface IAiProvider : IDisposable
```

Low-level abstraction over AI inference providers (Ollama, LLamaSharp, etc.). Each implementation wraps a specific backend and exposes a unified interface for model management, chat inference, and embedding generation.

**Namespace:** `AgentX.Core.AI`
**Assembly:** `AgentX.Core`
**Known Implementation:** `OllamaProvider` (in `AgentX.Core.AI.Providers`)

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ProviderId` | `string` | Unique identifier for this provider (e.g., `"ollama"`, `"llamasharp"`). |
| `DisplayName` | `string` | Human-readable display name for UI presentation (e.g., `"Ollama"`, `"LLamaSharp"`). |
| `IsAvailable` | `bool` | Indicates whether the provider is currently reachable and operational. Updated by `CheckConnectionAsync`. |

#### Methods

---

##### CheckConnectionAsync

```csharp
Task<bool> CheckConnectionAsync(CancellationToken ct = default);
```

Tests the connection to the AI provider backend.

**Returns:** `true` if the provider is reachable and operational; `false` on timeout or error.

**Behavior:** Uses a 3-second timeout. Returns `false` rather than throwing on connection failures.

---

##### ListModelsAsync

```csharp
Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default);
```

Lists all models currently available (installed) on this provider.

**Returns:** An ordered list of `AiModel` instances representing installed models.

---

##### PullModelAsync

```csharp
Task PullModelAsync(
    string modelName,
    IProgress<ModelDownloadProgress>? progress = null,
    CancellationToken ct = default);
```

Downloads/pulls a model from the provider's model registry.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `modelName` | `string` | -- | The name/tag of the model to pull (e.g., `"llama3.2:latest"`). |
| `progress` | `IProgress<ModelDownloadProgress>?` | `null` | Optional progress reporter for download status updates. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

---

##### DeleteModelAsync

```csharp
Task DeleteModelAsync(string modelName, CancellationToken ct = default);
```

Deletes a locally installed model from the provider.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `modelName` | `string` | -- | The name/tag of the model to delete. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

---

##### StreamChatAsync

```csharp
IAsyncEnumerable<string> StreamChatAsync(
    IReadOnlyList<ChatMessage> messages,
    ChatOptions? options = null,
    CancellationToken ct = default);
```

Streams a chat completion token-by-token for the given conversation history.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `messages` | `IReadOnlyList<ChatMessage>` | -- | The conversation message history. |
| `options` | `ChatOptions?` | `null` | Optional inference parameters (temperature, max tokens, etc.). |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** `IAsyncEnumerable<string>` -- An async stream of generated text tokens.

---

##### ChatAsync

```csharp
Task<string> ChatAsync(
    IReadOnlyList<ChatMessage> messages,
    ChatOptions? options = null,
    CancellationToken ct = default);
```

Generates a complete chat response for the given conversation history.

**Returns:** `string` -- The full generated response text.

---

##### GenerateEmbeddingAsync

```csharp
Task<float[]> GenerateEmbeddingAsync(
    string text,
    string modelName,
    CancellationToken ct = default);
```

Generates a vector embedding for a single text input.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `text` | `string` | -- | The text to embed. |
| `modelName` | `string` | -- | The embedding model to use (e.g., `"all-minilm"`). |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** `float[]` -- The embedding vector.

---

##### GenerateEmbeddingsAsync

```csharp
Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
    IReadOnlyList<string> texts,
    string modelName,
    CancellationToken ct = default);
```

Generates vector embeddings for multiple text inputs in a batch.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `texts` | `IReadOnlyList<string>` | -- | The texts to embed. |
| `modelName` | `string` | -- | The embedding model to use. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** `IReadOnlyList<float[]>` -- A list of embedding vectors, one per input text, in corresponding order.

---

### IHardwareDetector

```csharp
namespace AgentX.Core.AI;

public interface IHardwareDetector
```

Detects system hardware capabilities relevant to local AI inference -- GPU, VRAM, NPU, CPU cores, and available system memory.

**Namespace:** `AgentX.Core.AI`
**Assembly:** `AgentX.Core`

#### Methods

---

##### DetectAsync

```csharp
Task<HardwareCapability> DetectAsync(CancellationToken ct = default);
```

Detects the local hardware capabilities using WMI (Windows Management Instrumentation) queries.

**Returns:** A `HardwareCapability` instance with detected GPU, CPU, RAM, and NPU information.

**Behavior:** Results are cached for the duration of the session. Subsequent calls return the cached result without re-querying hardware.

---

### IModelManager

```csharp
namespace AgentX.Core.AI;

public interface IModelManager
```

Manages locally available AI models -- listing, downloading, deleting, and querying model information. Delegates to the active `IAiProvider` and provides caching and change notification.

**Namespace:** `AgentX.Core.AI`
**Assembly:** `AgentX.Core`
**Implementation:** `ModelManager`

#### Methods

---

##### GetAvailableModelsAsync

```csharp
Task<IReadOnlyList<AiModel>> GetAvailableModelsAsync(CancellationToken ct = default);
```

Gets all models available from the remote registry for the active provider. For Ollama, this returns the same as installed models since there is no separate "available" vs "installed" distinction locally.

---

##### GetInstalledModelsAsync

```csharp
Task<IReadOnlyList<AiModel>> GetInstalledModelsAsync(CancellationToken ct = default);
```

Gets all models currently installed on the local system.

---

##### PullModelAsync

```csharp
Task PullModelAsync(
    string modelName,
    IProgress<ModelDownloadProgress>? progress = null,
    CancellationToken ct = default);
```

Downloads/pulls a model from the provider's registry. Fires `ModelListChanged` on completion.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `modelName` | `string` | -- | The model name/tag to pull (e.g., `"llama3.2:latest"`). |
| `progress` | `IProgress<ModelDownloadProgress>?` | `null` | Optional progress reporter for download status. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

---

##### DeleteModelAsync

```csharp
Task DeleteModelAsync(string modelName, CancellationToken ct = default);
```

Deletes a locally installed model. Fires `ModelListChanged` on completion.

---

##### GetModelInfoAsync

```csharp
Task<AiModel?> GetModelInfoAsync(string modelName, CancellationToken ct = default);
```

Retrieves detailed information for a specific model by name.

**Returns:** The `AiModel` instance, or `null` if not found.

---

##### IsModelAvailableAsync

```csharp
Task<bool> IsModelAvailableAsync(string modelName, CancellationToken ct = default);
```

Checks whether a specific model is currently installed and available locally.

**Returns:** `true` if the model is installed locally.

#### Events

| Event | Type | Description |
|-------|------|-------------|
| `ModelListChanged` | `EventHandler<AiModel>?` | Raised when the local model list changes (after a pull or delete operation). The event argument is the affected `AiModel`. |

---

### IEmbeddingService

```csharp
namespace AgentX.Core.AI;

public interface IEmbeddingService
```

Generates vector embeddings from text content using a local embedding model.

**Namespace:** `AgentX.Core.AI`
**Assembly:** `AgentX.Core`
**Implementation:** `EmbeddingService`

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Dimensions` | `int` | The dimensionality of the embedding vectors produced by the configured model. |
| `ModelName` | `string` | The name of the embedding model being used (e.g., `"all-minilm"`). |

#### Methods

---

##### EmbedAsync

```csharp
Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
```

Generates a single embedding vector for the given text.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `text` | `string` | -- | The text to embed. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** `float[]` -- The embedding vector with `Dimensions` elements.

---

##### EmbedBatchAsync

```csharp
Task<IReadOnlyList<float[]>> EmbedBatchAsync(
    IEnumerable<string> texts,
    CancellationToken ct = default);
```

Generates embedding vectors for multiple texts in a batch.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `texts` | `IEnumerable<string>` | -- | The texts to embed. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** `IReadOnlyList<float[]>` -- A list of embedding vectors in the same order as the input texts.

---

## 2. AI Models

### ChatMessage

```csharp
namespace AgentX.Core.AI.Models;

public class ChatMessage
```

Represents a single message in a chat conversation.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Role` | `string` | `""` | The message role. Valid values: `"user"`, `"assistant"`, `"system"`. |
| `Content` | `string` | `""` | The text content of the message. |
| `Timestamp` | `DateTime` | `DateTime.UtcNow` | The timestamp when the message was created. Defaults to the current UTC time. |

---

### ChatOptions

```csharp
namespace AgentX.Core.AI.Models;

public class ChatOptions
```

Configuration options for AI chat inference, controlling model behavior such as temperature, token limits, and sampling parameters.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ModelId` | `string?` | `null` | The model identifier to use for this request. When `null`, the active model from `IAiService.ActiveModelId` is used. |
| `Temperature` | `double` | `0.7` | Controls randomness in the output. Higher values (e.g., 1.0) produce more creative responses; lower values (e.g., 0.2) produce more deterministic responses. |
| `MaxTokens` | `int` | `2048` | Maximum number of tokens to generate in the response. |
| `ContextWindow` | `int` | `4096` | Size of the context window used for token generation. |
| `TopP` | `double` | `0.9` | Nucleus sampling parameter. Controls the cumulative probability threshold for token selection. A value of 0.9 considers tokens comprising the top 90% of probability mass. |
| `FrequencyPenalty` | `double` | `0` | Penalizes tokens based on their frequency in the generated text so far, reducing repetition of common phrases. |
| `PresencePenalty` | `double` | `0` | Penalizes tokens based on whether they have appeared in the generated text so far, encouraging the model to explore new topics. |
| `StopSequences` | `string[]?` | `null` | Sequences that will cause the model to stop generating further tokens when encountered. |

---

### AiModel

```csharp
namespace AgentX.Core.AI.Models;

public class AiModel
```

Represents a locally available AI model with its metadata.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `string` | `""` | Unique model identifier. |
| `Name` | `string` | `""` | Human-readable model name. |
| `Family` | `string` | `""` | Model family (e.g., `"llama"`, `"mistral"`). |
| `SizeBytes` | `long` | `0` | Model file size in bytes. |
| `QuantizationLevel` | `string` | `""` | Quantization level (e.g., `"Q4_K_M"`, `"Q8_0"`). |
| `ParameterCount` | `int` | `0` | Number of model parameters (e.g., `7000000000` for 7B). |
| `ContextLength` | `int` | `0` | Maximum context length the model supports. |
| `ModifiedAt` | `DateTime` | `default` | Last modification timestamp of the model. |
| `Digest` | `string` | `""` | Content digest/hash of the model file. |
| `SizeFormatted` | `string` | *(computed)* | Human-readable file size. Returns `"{MB} MB"` for sizes under 1 GB, `"{GB} GB"` for sizes at or above 1 GB. |

**Computed Property Logic:**

```csharp
public string SizeFormatted => SizeBytes switch
{
    < 1_000_000_000 => $"{SizeBytes / 1_000_000.0:F1} MB",
    _ => $"{SizeBytes / 1_000_000_000.0:F1} GB"
};
```

---

### HardwareCapability

```csharp
namespace AgentX.Core.AI.Models;

public class HardwareCapability
```

Represents the detected hardware capabilities of the local system, used for model recommendations.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `GpuName` | `string` | `"Unknown"` | Display name of the detected GPU. |
| `GpuVramBytes` | `long` | `0` | Amount of dedicated GPU VRAM in bytes. |
| `HasNpu` | `bool` | `false` | Whether a Neural Processing Unit was detected. |
| `NpuName` | `string` | `"None"` | Display name of the detected NPU. |
| `CpuCores` | `int` | `0` | Number of logical CPU cores. |
| `CpuName` | `string` | `"Unknown"` | Display name of the CPU. |
| `TotalRamBytes` | `long` | `0` | Total system RAM in bytes. |
| `AvailableRamBytes` | `long` | `0` | Currently available (free) RAM in bytes. |

**Computed Properties:**

| Property | Type | Description |
|----------|------|-------------|
| `GpuVramFormatted` | `string` | Returns `"No dedicated GPU"` when VRAM is 0, otherwise formats as MB or GB. |
| `TotalRamFormatted` | `string` | Total RAM formatted as `"{N} GB"`. |
| `AvailableRamFormatted` | `string` | Available RAM formatted as `"{N.N} GB"`. |
| `RecommendedMaxModelSize` | `string` | Human-readable model size recommendation based on available RAM. |

**RecommendedMaxModelSize Logic:**

| Available RAM | Recommendation |
|--------------|----------------|
| < 4 GB | `"Up to 3B parameter models"` |
| < 8 GB | `"Up to 7B parameter models"` |
| < 16 GB | `"Up to 13B parameter models"` |
| < 32 GB | `"Up to 34B parameter models"` |
| >= 32 GB | `"Up to 70B+ parameter models"` |

---

### ModelDownloadProgress

```csharp
namespace AgentX.Core.AI.Models;

public class ModelDownloadProgress
```

Reports progress during a model download/pull operation.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ModelId` | `string` | `""` | The identifier of the model being downloaded. |
| `Status` | `string` | `""` | Current status description (e.g., `"pulling manifest"`, `"downloading"`, `"verifying"`). |
| `CompletedBytes` | `long` | `0` | Number of bytes downloaded so far. |
| `TotalBytes` | `long` | `0` | Total number of bytes to download. |
| `PercentComplete` | `double` | *(computed)* | Download completion percentage (0.0 to 100.0). Returns `0` when `TotalBytes` is 0 to avoid division by zero. |

---

## 3. Document Services

### IDocumentService

```csharp
namespace AgentX.Core.Documents;

public interface IDocumentService
```

Orchestrates the document import pipeline: file validation, text extraction, metadata capture, and database record creation. Imported documents are left in `"pending"` status for the indexing pipeline to pick up for chunking and embedding.

**Namespace:** `AgentX.Core.Documents`
**Assembly:** `AgentX.Core`
**Implementation:** `DocumentService`

#### Methods

---

##### ImportFileAsync

```csharp
Task<DocumentEntity> ImportFileAsync(
    string filePath,
    long? collectionId = null,
    CancellationToken ct = default);
```

Imports a single file: validates the file path, computes a SHA-256 content hash, extracts text via the appropriate `IDocumentProcessor`, and creates a `DocumentEntity` record.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `filePath` | `string` | -- | Absolute path to the file to import. |
| `collectionId` | `long?` | `null` | Optional collection to associate the document with. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** The created `DocumentEntity` with `IndexingStatus = "pending"`.

---

##### ImportFilesAsync

```csharp
Task<IReadOnlyList<DocumentEntity>> ImportFilesAsync(
    IReadOnlyList<string> filePaths,
    long? collectionId = null,
    IProgress<int>? progress = null,
    CancellationToken ct = default);
```

Imports multiple files, reporting progress as each file completes.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `filePaths` | `IReadOnlyList<string>` | -- | Absolute paths to the files to import. |
| `collectionId` | `long?` | `null` | Optional collection to associate all documents with. |
| `progress` | `IProgress<int>?` | `null` | Optional progress reporter. Reports the number of files completed so far. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** The list of created `DocumentEntity` records.

---

##### GetDocumentAsync

```csharp
Task<DocumentEntity?> GetDocumentAsync(long documentId);
```

Retrieves a single document by its primary key.

**Returns:** The `DocumentEntity`, or `null` if not found.

---

##### GetAllDocumentsAsync

```csharp
Task<IReadOnlyList<DocumentEntity>> GetAllDocumentsAsync(
    string? fileTypeFilter = null,
    string? statusFilter = null);
```

Retrieves all documents with optional filtering. Results are ordered by `ImportedAt` descending (newest first).

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `fileTypeFilter` | `string?` | `null` | Optional file type to filter by (e.g., `"pdf"`, `"docx"`). |
| `statusFilter` | `string?` | `null` | Optional indexing status to filter by (e.g., `"completed"`, `"pending"`, `"failed"`). |

---

##### GetDocumentsByCollectionAsync

```csharp
Task<IReadOnlyList<DocumentEntity>> GetDocumentsByCollectionAsync(long collectionId);
```

Retrieves all documents belonging to a specific collection.

---

##### DeleteDocumentAsync

```csharp
Task DeleteDocumentAsync(long documentId);
```

Deletes a document, its chunks, and any associated vector embeddings. Cascades through `DocumentChunkEntity` records and their vector store entries.

---

##### ReindexDocumentAsync

```csharp
Task ReindexDocumentAsync(long documentId, CancellationToken ct = default);
```

Re-processes a document by deleting existing chunks and re-extracting text. Resets the document status to `"pending"` for re-indexing by the background indexing pipeline.

---

##### GetDocumentByHashAsync

```csharp
Task<DocumentEntity?> GetDocumentByHashAsync(string contentHash);
```

Looks up a document by its SHA-256 content hash. Used for duplicate detection during import.

**Returns:** The matching `DocumentEntity`, or `null` if no document has the specified hash.

---

##### GetTotalDocumentCountAsync

```csharp
Task<long> GetTotalDocumentCountAsync();
```

Returns the total number of documents in the knowledge vault.

---

##### GetTotalStorageBytesAsync

```csharp
Task<long> GetTotalStorageBytesAsync();
```

Returns the total storage consumed by all imported documents in bytes.

---

##### GetFileTypeDistributionAsync

```csharp
Task<Dictionary<string, int>> GetFileTypeDistributionAsync();
```

Returns a distribution of file types and their document counts.

**Returns:** A dictionary mapping file type strings to counts (e.g., `{"pdf": 12, "docx": 5, "txt": 3}`).

---

##### CanProcess

```csharp
bool CanProcess(string filePath);
```

Checks whether the given file can be processed by any registered document processor. Evaluates the file extension against all registered `IDocumentProcessor` instances.

---

##### GetSupportedExtensions

```csharp
IReadOnlySet<string> GetSupportedExtensions();
```

Returns the union of all supported file extensions across all registered processors.

---

### IDocumentProcessor

```csharp
namespace AgentX.Core.Documents;

public interface IDocumentProcessor
```

Extracts text content from a specific file type. Each supported format gets its own processor implementation.

**Namespace:** `AgentX.Core.Documents`
**Assembly:** `AgentX.Core`

#### Implementations

| Processor | Supported Extensions | Notes |
|-----------|---------------------|-------|
| `PdfProcessor` | `.pdf` | PDF text extraction. |
| `DocxProcessor` | `.docx`, `.doc` | Microsoft Word document extraction. |
| `TextProcessor` | `.txt`, `.csv`, `.log`, `.xml`, `.json` | Plain text and structured text formats. |
| `MarkdownProcessor` | `.md`, `.markdown` | Markdown files with metadata extraction. |
| `CodeFileProcessor` | `.cs`, `.js`, `.ts`, `.py`, `.java`, `.cpp`, `.c`, `.h`, `.go`, `.rs`, `.swift`, `.kt`, `.rb`, `.php`, `.html`, `.css`, `.scss`, `.sql`, `.sh`, `.yaml`, `.yml`, `.toml`, `.ini`, `.cfg`, `.xaml` | Source code files (26 extensions). |
| `ImageProcessor` | `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tiff` | Image files with OCR via Windows OCR engine. |

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `SupportedExtensions` | `IReadOnlySet<string>` | The set of file extensions this processor can handle (case-insensitive). |

#### Methods

---

##### CanProcess

```csharp
bool CanProcess(string filePath);
```

Returns `true` if this processor supports the given file's extension.

---

##### ProcessAsync

```csharp
Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default);
```

Extracts text content and metadata from the specified file.

**Returns:** A `ProcessedDocument` containing the extracted text, file metadata, page count, word count, and optional chunks.

---

### IChunkingService

```csharp
namespace AgentX.Core.Documents;

public interface IChunkingService
```

Splits text content into overlapping chunks suitable for embedding generation. Uses a recursive character text splitter strategy: paragraphs, then sentences, then words.

**Namespace:** `AgentX.Core.Documents`
**Assembly:** `AgentX.Core`
**Implementation:** `ChunkingService`

#### Methods

---

##### ChunkText

```csharp
IReadOnlyList<DocumentChunk> ChunkText(
    string text,
    int chunkSize = 512,
    int chunkOverlap = 50,
    string? sectionTitle = null,
    int? pageNumber = null);
```

Splits raw text into overlapping chunks with metadata.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `text` | `string` | -- | The text content to chunk. |
| `chunkSize` | `int` | `512` | Maximum number of tokens (approximated as word count) per chunk. |
| `chunkOverlap` | `int` | `50` | Number of overlapping tokens between consecutive chunks. |
| `sectionTitle` | `string?` | `null` | Optional section title to attach to all generated chunks. |
| `pageNumber` | `int?` | `null` | Optional page number to attach to all generated chunks. |

**Returns:** An ordered list of `DocumentChunk` instances with content, character offsets, and token counts.

---

##### ChunkDocument

```csharp
IReadOnlyList<DocumentChunk> ChunkDocument(
    ProcessedDocument document,
    int chunkSize = 512,
    int chunkOverlap = 50);
```

Splits a processed document into overlapping chunks, respecting page boundaries when page-level text is available.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `document` | `ProcessedDocument` | -- | The processed document containing extracted text and metadata. |
| `chunkSize` | `int` | `512` | Maximum number of tokens per chunk. |
| `chunkOverlap` | `int` | `50` | Number of overlapping tokens between consecutive chunks. |

**Returns:** An ordered list of `DocumentChunk` instances covering the entire document.

---

### Document Models

#### ProcessedDocument

```csharp
namespace AgentX.Core.Documents.Models;

public class ProcessedDocument
```

Represents a document after text extraction and before chunking/embedding.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `FilePath` | `string` | `""` | Absolute path to the source file. |
| `FileName` | `string` | `""` | The file name (without directory path). |
| `FileType` | `string` | `""` | The file type extension (e.g., `"pdf"`, `"docx"`). |
| `FileSizeBytes` | `long` | `0` | File size in bytes. |
| `ContentHash` | `string` | `""` | SHA-256 hash of the file content for duplicate detection. |
| `ExtractedText` | `string` | `""` | The full extracted text content. |
| `ExtractedTitle` | `string?` | `null` | Title extracted from document metadata (if available). |
| `PageCount` | `int` | `0` | Number of pages in the source document. |
| `WordCount` | `long` | `0` | Approximate word count of the extracted text. |
| `Language` | `string?` | `null` | Detected language of the content (if available). |
| `Metadata` | `DocumentMetadata` | `new()` | Additional metadata extracted from the document. |
| `Chunks` | `List<DocumentChunk>` | `new()` | Pre-chunked content (populated during processing if applicable). |

#### DocumentChunk

```csharp
namespace AgentX.Core.Documents.Models;

public class DocumentChunk
```

Represents a single chunk of text within a document, suitable for embedding.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Index` | `int` | `0` | Zero-based index of this chunk within the document. |
| `Content` | `string` | `""` | The text content of the chunk. |
| `StartCharOffset` | `int` | `0` | Starting character offset within the source document text. |
| `EndCharOffset` | `int` | `0` | Ending character offset within the source document text. |
| `PageNumber` | `int?` | `null` | The page number this chunk belongs to (if available). |
| `SectionTitle` | `string?` | `null` | The section title this chunk falls under (if available). |
| `TokenCount` | `int` | `0` | Approximate token count for this chunk. |
| `Embedding` | `float[]?` | `null` | The generated embedding vector (populated after embedding generation). |

#### DocumentMetadata

```csharp
namespace AgentX.Core.Documents.Models;

public class DocumentMetadata
```

Additional metadata extracted from a document during processing.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Author` | `string?` | `null` | Document author (from PDF/DOCX metadata). |
| `Subject` | `string?` | `null` | Document subject (from PDF/DOCX metadata). |
| `CreatedDate` | `DateTime?` | `null` | Document creation date (from metadata). |
| `ModifiedDate` | `DateTime?` | `null` | Document last modified date (from metadata). |
| `Custom` | `Dictionary<string, string>` | `new()` | Arbitrary key-value metadata pairs. |

#### SupportedFileTypes

```csharp
namespace AgentX.Core.Documents.Models;

public static class SupportedFileTypes
```

Static reference for all supported file extensions, grouped by category.

| Field | Extensions |
|-------|-----------|
| `Pdf` | `.pdf` |
| `Office` | `.docx`, `.doc` |
| `Text` | `.txt`, `.csv`, `.log`, `.xml`, `.json` |
| `Markdown` | `.md`, `.markdown` |
| `Image` | `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tiff` |
| `Code` | `.cs`, `.js`, `.ts`, `.py`, `.java`, `.cpp`, `.c`, `.h`, `.go`, `.rs`, `.swift`, `.kt`, `.rb`, `.php`, `.html`, `.css`, `.scss`, `.sql`, `.sh`, `.yaml`, `.yml`, `.toml`, `.ini`, `.cfg`, `.xaml` |
| `All` | Union of all above sets (case-insensitive). |

---

## 4. Search and RAG

### ISemanticSearchService

```csharp
namespace AgentX.Core.Search;

public interface ISemanticSearchService
```

Performs semantic (vector-based) search across indexed document chunks. Combines embedding generation, vector similarity search, and result enrichment with document metadata.

**Namespace:** `AgentX.Core.Search`
**Assembly:** `AgentX.Core`
**Implementation:** `SemanticSearchService`

#### Methods

---

##### SearchAsync

```csharp
Task<IReadOnlyList<SearchResult>> SearchAsync(
    SearchQuery query,
    CancellationToken ct = default);
```

Performs a semantic search using the given query. The query text is embedded, matched against the vector store, and results are enriched with document metadata.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `query` | `SearchQuery` | -- | The search query with optional filters (collection, file type, date range). |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** An ordered list of `SearchResult` instances, highest relevance first.

---

##### SaveSearchHistoryAsync

```csharp
Task SaveSearchHistoryAsync(string queryText, int resultCount);
```

Saves a search query to the search history for later re-use.

**Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `queryText` | `string` | The search query text. |
| `resultCount` | `int` | The number of results returned. |

---

##### GetSearchHistoryAsync

```csharp
Task<IReadOnlyList<SearchHistoryEntry>> GetSearchHistoryAsync(int limit = 20);
```

Retrieves recent search history entries, ordered by most recent first.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `limit` | `int` | `20` | Maximum number of entries to return. |

---

##### ClearSearchHistoryAsync

```csharp
Task ClearSearchHistoryAsync();
```

Clears all search history records.

#### Supporting Types

##### SearchHistoryEntry

```csharp
namespace AgentX.Core.Search;

public class SearchHistoryEntry
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | -- | Primary key (init-only). |
| `QueryText` | `string` | `""` | The search query text (init-only). |
| `ResultCount` | `int` | -- | Number of results returned (init-only). |
| `SearchedAt` | `DateTime` | -- | Timestamp of the search (init-only). |

---

### IRagPipeline

```csharp
namespace AgentX.Core.Search;

public interface IRagPipeline
```

Orchestrates the Retrieval-Augmented Generation pipeline:
1. Embeds the user question.
2. Retrieves relevant context chunks via semantic search.
3. Builds a grounded prompt with context.
4. Streams the AI response.
5. Extracts citations from the response.

**Namespace:** `AgentX.Core.Search`
**Assembly:** `AgentX.Core`
**Implementation:** `RagPipeline`

#### Methods

---

##### AskAsync

```csharp
Task<RagResponse> AskAsync(
    string question,
    long? collectionId = null,
    Action<string>? onToken = null,
    CancellationToken ct = default);
```

Executes the full RAG pipeline: search for context, build prompt, stream response, and extract citations.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `question` | `string` | -- | The user's natural language question. |
| `collectionId` | `long?` | `null` | Optional collection scope. When `null`, searches across all collections. |
| `onToken` | `Action<string>?` | `null` | Callback invoked for each streamed token during generation. Enables real-time UI updates. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** A `RagResponse` containing the complete answer text, citations, and latency metrics.

---

##### GetIndexedChunkCountAsync

```csharp
Task<long> GetIndexedChunkCountAsync(CancellationToken ct = default);
```

Gets the number of indexed chunks available for RAG queries. Used to show the user how much knowledge is available for question answering.

---

### ICitationService

```csharp
namespace AgentX.Core.Search;

public interface ICitationService
```

Extracts and resolves citation references from AI-generated RAG responses. Citation format: `[N]` where N is the 1-based index of the context chunk.

**Namespace:** `AgentX.Core.Search`
**Assembly:** `AgentX.Core`
**Implementation:** `CitationService`

#### Methods

---

##### ExtractCitations

```csharp
List<Citation> ExtractCitations(
    string responseText,
    IReadOnlyList<RagContextChunk> contextChunks);
```

Extracts all `[N]` citation references from the response text and maps them to the corresponding source chunks.

**Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `responseText` | `string` | The AI-generated response text containing `[N]` references. |
| `contextChunks` | `IReadOnlyList<RagContextChunk>` | The ordered list of context chunks that were provided to the AI (1-indexed in citations). |

**Returns:** A list of resolved `Citation` instances with document metadata.

#### Supporting Types

##### RagContextChunk

```csharp
namespace AgentX.Core.Search;

public class RagContextChunk
```

Represents a single chunk of context that was provided to the AI during RAG. Used by `CitationService` to resolve citation references back to source documents.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ChunkId` | `long` | -- | The document chunk entity ID (init-only). |
| `DocumentId` | `long` | -- | The parent document entity ID (init-only). |
| `FileName` | `string` | `""` | The source document file name (init-only). |
| `FilePath` | `string` | `""` | The source document file path (init-only). |
| `PageNumber` | `int?` | `null` | Page number within the source document (init-only). |
| `ChunkIndex` | `int` | -- | The chunk index within the document (init-only). |
| `ChunkText` | `string` | `""` | The text content of the chunk (init-only). |
| `RelevanceScore` | `float` | -- | The similarity score from the vector search (init-only). |

---

### Search Models

#### SearchQuery

```csharp
namespace AgentX.Core.Search.Models;

public class SearchQuery
```

Represents a semantic search query with optional filters.

| Property | Type | Default | Required | Description |
|----------|------|---------|----------|-------------|
| `QueryText` | `string` | -- | **Yes** | The natural language query text. Uses `required` keyword. |
| `TopK` | `int` | `10` | No | Maximum number of results to return. |
| `MinScore` | `float` | `0.3f` | No | Minimum similarity score (0.0 to 1.0) to include in results. |
| `CollectionId` | `long?` | `null` | No | Optional collection ID to scope the search. |
| `FileTypeFilter` | `string?` | `null` | No | Optional file type filter (e.g., `"pdf"`, `"docx"`). |
| `CreatedAfter` | `DateTime?` | `null` | No | Only include documents created after this date. |
| `CreatedBefore` | `DateTime?` | `null` | No | Only include documents created before this date. |

All properties use `init` accessors.

---

#### SearchResult

```csharp
namespace AgentX.Core.Search.Models;

public class SearchResult
```

A single semantic search result with matched chunk, relevance score, and source document metadata.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ChunkId` | `long` | -- | The document chunk entity ID (init-only). |
| `DocumentId` | `long` | -- | The parent document entity ID (init-only). |
| `FileName` | `string` | `""` | The source document file name (init-only). |
| `FilePath` | `string` | `""` | The source document file path (init-only). |
| `FileType` | `string` | `""` | The source document file type, e.g., `"pdf"`, `"docx"` (init-only). |
| `PageNumber` | `int?` | `null` | Page number within the source document (init-only). |
| `ChunkIndex` | `int` | -- | The chunk index within the document (init-only). |
| `MatchedText` | `string` | `""` | The full matched text from the chunk (init-only). |
| `Excerpt` | `string` | `""` | A shorter excerpt suitable for display, with the most relevant section highlighted (init-only). |
| `Score` | `float` | -- | Cosine similarity score between 0.0 and 1.0. Higher = more relevant (init-only). |
| `RelevancePercent` | `int` | *(computed)* | Relevance as a percentage (0-100), derived as `(int)(Score * 100)`. |
| `CollectionNames` | `List<string>` | `new()` | Collection names this document belongs to (init-only). |

---

#### RagResponse

```csharp
namespace AgentX.Core.Search.Models;

public class RagResponse
```

The complete response from a RAG (Retrieval-Augmented Generation) query.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AnswerText` | `string` | `""` | The AI-generated answer text. May contain `[N]` citation references. |
| `Question` | `string` | `""` | The original user question (init-only). |
| `Citations` | `List<Citation>` | `new()` | All citations referenced in the answer, resolved to source documents. |
| `ContextChunksUsed` | `int` | -- | Number of context chunks that were provided to the AI (init-only). |
| `IsStreaming` | `bool` | `false` | Whether the response is still being streamed. |
| `TotalLatencyMs` | `double` | `0` | Total time taken for search + generation in milliseconds. |
| `SearchLatencyMs` | `double` | `0` | Time taken for the semantic search portion in milliseconds. |
| `CollectionScope` | `long?` | `null` | The collection scope used for the query. `null` means all collections (init-only). |

---

#### Citation

```csharp
namespace AgentX.Core.Search.Models;

public class Citation
```

A citation reference extracted from an AI-generated RAG response, linking back to a source document and chunk.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Number` | `int` | -- | The citation number as it appears in the response text (e.g., `1` for `[1]`) (init-only). |
| `DocumentId` | `long` | -- | The source document entity ID (init-only). |
| `ChunkId` | `long` | -- | The source chunk entity ID (init-only). |
| `FileName` | `string` | `""` | The source document file name (init-only). |
| `FilePath` | `string` | `""` | The source document file path (init-only). |
| `PageNumber` | `int?` | `null` | Page number within the source document (init-only). |
| `ChunkIndex` | `int` | -- | The chunk index within the document (init-only). |
| `Excerpt` | `string` | `""` | A short excerpt from the cited chunk (init-only). |
| `RelevanceScore` | `float` | -- | The relevance score of this chunk to the original query (init-only). |

---

## 5. Vector Store

### IVectorStore

```csharp
namespace AgentX.Core.Data.VectorDb;

public interface IVectorStore : IAsyncDisposable
```

Abstraction over the vector database used for semantic embedding storage and retrieval. Implementations may use SQLite with custom distance functions, FAISS, or other backends.

**Namespace:** `AgentX.Core.Data.VectorDb`
**Assembly:** `AgentX.Core`
**Implementation:** `SqliteVecStore`

#### Methods

---

##### InitializeAsync

```csharp
Task InitializeAsync(CancellationToken ct = default);
```

Initializes the vector store (creates tables, loads indexes, etc.). Must be called before any other operations.

---

##### InsertEmbeddingAsync

```csharp
Task<long> InsertEmbeddingAsync(
    long chunkId,
    float[] embedding,
    CancellationToken ct = default);
```

Inserts a single embedding vector associated with a document chunk.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `chunkId` | `long` | -- | The ID of the document chunk this embedding represents. |
| `embedding` | `float[]` | -- | The embedding vector (float array). |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** `long` -- The row ID of the inserted embedding record.

---

##### SearchAsync

```csharp
Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
    float[] queryEmbedding,
    int topK = 5,
    double minSimilarity = 0.3,
    CancellationToken ct = default);
```

Searches for the nearest neighbors to the given query embedding.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `queryEmbedding` | `float[]` | -- | The query embedding vector. |
| `topK` | `int` | `5` | Maximum number of results to return. |
| `minSimilarity` | `double` | `0.3` | Minimum cosine similarity threshold (0.0 to 1.0) for inclusion. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** An ordered list of `VectorSearchResult` instances ranked by similarity (highest first).

---

##### DeleteEmbeddingAsync

```csharp
Task DeleteEmbeddingAsync(long chunkId, CancellationToken ct = default);
```

Deletes the embedding associated with a specific chunk.

---

##### DeleteEmbeddingsForDocumentAsync

```csharp
Task DeleteEmbeddingsForDocumentAsync(
    long documentId,
    IReadOnlyList<long> chunkIds,
    CancellationToken ct = default);
```

Deletes all embeddings associated with a document's chunks.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `documentId` | `long` | -- | The parent document ID (used for logging/auditing). |
| `chunkIds` | `IReadOnlyList<long>` | -- | The chunk IDs whose embeddings should be removed. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

---

##### GetEmbeddingCountAsync

```csharp
Task<long> GetEmbeddingCountAsync(CancellationToken ct = default);
```

Returns the total number of embedding vectors currently stored.

---

##### OptimizeAsync

```csharp
Task OptimizeAsync(CancellationToken ct = default);
```

Optimizes the vector index for faster search (e.g., rebuild HNSW, vacuum). May be a no-op for some implementations.

---

### VectorSearchResult

```csharp
namespace AgentX.Core.Data.VectorDb;

public class VectorSearchResult
```

Represents a single result from a vector similarity search.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ChunkId` | `long` | `0` | The ID of the document chunk that matched the query. |
| `Distance` | `double` | `0` | The raw cosine distance metric between the query vector and this result. `0.0` = identical, `2.0` = opposite. |
| `Similarity` | `double` | *(computed)* | Cosine similarity derived from distance: `1.0 - Distance`. Range: `-1.0` to `1.0` where `1.0` = identical. |

---

## 6. Chat Services

### IChatService

```csharp
namespace AgentX.Core.Services.Chat;

public interface IChatService
```

Orchestrates AI chat operations: sends messages, streams responses, manages generation state, and coordinates persistence via `IConversationService`.

**Namespace:** `AgentX.Core.Services.Chat`
**Assembly:** `AgentX.Core`
**Implementation:** `ChatService`

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsGenerating` | `bool` | Indicates whether an AI response is currently being generated. |

#### Events

| Event | Type | Description |
|-------|------|-------------|
| `GenerationStateChanged` | `EventHandler<bool>?` | Fires when `IsGenerating` changes. The event argument is the new value of `IsGenerating`. |

#### Methods

---

##### SendMessageAsync

```csharp
IAsyncEnumerable<string> SendMessageAsync(
    long conversationId,
    string userMessage,
    CancellationToken ct = default);
```

Sends a user message and streams the assistant response token-by-token. The user message and final assistant response are persisted automatically to the database.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `conversationId` | `long` | -- | The conversation to send the message in. |
| `userMessage` | `string` | -- | The user's message content. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** `IAsyncEnumerable<string>` -- An async stream of response tokens as they arrive.

---

##### SendMessageAndWaitAsync

```csharp
Task<string> SendMessageAndWaitAsync(
    long conversationId,
    string userMessage,
    CancellationToken ct = default);
```

Sends a user message and waits for the complete assistant response. The user message and assistant response are persisted automatically.

**Returns:** `string` -- The complete assistant response.

---

##### RegenerateLastResponseAsync

```csharp
Task RegenerateLastResponseAsync(
    long conversationId,
    CancellationToken ct = default);
```

Deletes the last assistant message and re-sends the last user message to generate a new response.

---

##### StopGenerationAsync

```csharp
Task StopGenerationAsync();
```

Cancels any in-progress generation. Sets `IsGenerating` to `false` and fires `GenerationStateChanged`.

---

### IConversationService

```csharp
namespace AgentX.Core.Services.Chat;

public interface IConversationService
```

Manages conversation and message persistence. Provides CRUD operations for conversations and their associated messages via Entity Framework Core.

**Namespace:** `AgentX.Core.Services.Chat`
**Assembly:** `AgentX.Core`
**Implementation:** `ConversationService`

#### Methods

---

##### CreateConversationAsync

```csharp
Task<ConversationEntity> CreateConversationAsync(
    string? title = null,
    string? systemPrompt = null,
    string? modelId = null);
```

Creates a new conversation with optional title, system prompt, and model.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `title` | `string?` | `null` | Display title for the conversation. |
| `systemPrompt` | `string?` | `null` | System prompt to use for all messages in this conversation. |
| `modelId` | `string?` | `null` | The AI model to use for this conversation. |

**Returns:** The newly created `ConversationEntity`.

---

##### GetConversationAsync

```csharp
Task<ConversationEntity?> GetConversationAsync(long conversationId);
```

Retrieves a conversation by ID, including its messages.

**Returns:** The `ConversationEntity` with loaded `Messages` navigation, or `null` if not found.

---

##### GetAllConversationsAsync

```csharp
Task<IReadOnlyList<ConversationEntity>> GetAllConversationsAsync(
    bool includeArchived = false);
```

Returns all conversations ordered by `UpdatedAt` descending.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `includeArchived` | `bool` | `false` | When `false` (default), archived conversations are excluded. |

---

##### SearchConversationsAsync

```csharp
Task<IReadOnlyList<ConversationEntity>> SearchConversationsAsync(string query);
```

Searches conversations by title or message content matching the query text.

---

##### UpdateConversationTitleAsync

```csharp
Task UpdateConversationTitleAsync(long conversationId, string title);
```

Updates the title of an existing conversation.

---

##### TogglePinAsync

```csharp
Task TogglePinAsync(long conversationId);
```

Toggles the `IsPinned` state of a conversation.

---

##### ArchiveConversationAsync

```csharp
Task ArchiveConversationAsync(long conversationId);
```

Archives a conversation, hiding it from the default conversation list.

---

##### DeleteConversationAsync

```csharp
Task DeleteConversationAsync(long conversationId);
```

Permanently deletes a conversation and all its messages. Cascades through `MessageEntity` records.

---

##### GetMessagesAsync

```csharp
Task<IReadOnlyList<MessageEntity>> GetMessagesAsync(long conversationId);
```

Returns all messages for a conversation, ordered by `SortOrder` ascending.

---

##### AddMessageAsync

```csharp
Task AddMessageAsync(
    long conversationId,
    string role,
    string content,
    int? tokenCount = null,
    double? generationTimeMs = null);
```

Adds a new message to a conversation and updates conversation metadata (`MessageCount`, `TokensUsed`, `UpdatedAt`).

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `conversationId` | `long` | -- | The target conversation. |
| `role` | `string` | -- | Message role: `"user"`, `"assistant"`, or `"system"`. |
| `content` | `string` | -- | The message content. |
| `tokenCount` | `int?` | `null` | Optional estimated token count for the message. |
| `generationTimeMs` | `double?` | `null` | Optional generation time in milliseconds (for assistant messages). |

---

##### DeleteLastAssistantMessageAsync

```csharp
Task DeleteLastAssistantMessageAsync(long conversationId);
```

Removes the most recent assistant message from a conversation. Used by the regeneration flow to replace the last response.

---

##### GetConversationCountAsync

```csharp
Task<int> GetConversationCountAsync();
```

Returns the count of non-archived conversations.

---

##### GetTotalTokensUsedAsync

```csharp
Task<long> GetTotalTokensUsedAsync();
```

Returns the sum of `TokensUsed` across all conversations.

---

### ISystemPromptService

```csharp
namespace AgentX.Core.Services.Chat;

public interface ISystemPromptService
```

Manages system prompt templates. Provides CRUD operations, favorites, usage tracking, and seeding of built-in prompts.

**Namespace:** `AgentX.Core.Services.Chat`
**Assembly:** `AgentX.Core`
**Implementation:** `SystemPromptService`

#### Methods

---

##### GetAllPromptsAsync

```csharp
Task<IReadOnlyList<SystemPromptEntity>> GetAllPromptsAsync(string? category = null);
```

Returns all prompts, optionally filtered by category. Results are ordered by `IsFavorite` descending, then `UsageCount` descending.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `category` | `string?` | `null` | Optional category filter (e.g., `"General"`, `"Writing"`, `"Code"`, `"Analysis"`, `"Creative"`). |

---

##### GetPromptAsync

```csharp
Task<SystemPromptEntity?> GetPromptAsync(long id);
```

Retrieves a single prompt by its primary key.

---

##### CreatePromptAsync

```csharp
Task<SystemPromptEntity> CreatePromptAsync(
    string name,
    string content,
    string category);
```

Creates a new user-defined prompt.

**Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `name` | `string` | Display name for the prompt. |
| `content` | `string` | The system prompt text. |
| `category` | `string` | Category classification (e.g., `"General"`, `"Writing"`, `"Code"`). |

---

##### UpdatePromptAsync

```csharp
Task UpdatePromptAsync(long id, string name, string content, string category);
```

Updates an existing prompt's name, content, and category.

---

##### DeletePromptAsync

```csharp
Task DeletePromptAsync(long id);
```

Deletes a prompt by ID. Built-in prompts (where `IsBuiltIn = true`) cannot be deleted.

---

##### ToggleFavoriteAsync

```csharp
Task ToggleFavoriteAsync(long id);
```

Toggles the `IsFavorite` status of a prompt.

---

##### IncrementUsageAsync

```csharp
Task IncrementUsageAsync(long id);
```

Increments the `UsageCount` counter for a prompt. Called when a prompt is selected for a conversation.

---

##### SeedBuiltInPromptsAsync

```csharp
Task SeedBuiltInPromptsAsync();
```

Seeds the database with built-in prompts if they do not already exist. Should be called once during application startup.

---

## 7. Collections and Tags

### ICollectionService

```csharp
namespace AgentX.Core.Services.Collections;

public interface ICollectionService
```

Manages document collections, including CRUD operations, hierarchical organization, and document-collection associations.

**Namespace:** `AgentX.Core.Services.Collections`
**Assembly:** `AgentX.Core`
**Implementation:** `CollectionService`

#### Methods

---

##### CreateCollectionAsync

```csharp
Task<CollectionEntity> CreateCollectionAsync(
    string name,
    string? description = null,
    long? parentId = null);
```

Creates a new collection with the given name, optional description, and optional parent for hierarchical nesting.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `name` | `string` | -- | The name of the collection (must not be empty). |
| `description` | `string?` | `null` | Optional description of the collection. |
| `parentId` | `long?` | `null` | Optional parent collection ID for nesting. |

**Returns:** The newly created `CollectionEntity`.

---

##### GetAllCollectionsAsync

```csharp
Task<IReadOnlyList<CollectionEntity>> GetAllCollectionsAsync();
```

Retrieves all collections, ordered by `SortOrder` then `Name`, with child collections included.

---

##### GetRootCollectionsAsync

```csharp
Task<IReadOnlyList<CollectionEntity>> GetRootCollectionsAsync();
```

Retrieves only root-level collections (those without a parent), with child collections included.

---

##### GetChildCollectionsAsync

```csharp
Task<IReadOnlyList<CollectionEntity>> GetChildCollectionsAsync(long parentId);
```

Retrieves the immediate child collections of the specified parent collection.

---

##### GetCollectionAsync

```csharp
Task<CollectionEntity?> GetCollectionAsync(long collectionId);
```

Retrieves a single collection by ID, including its document associations and child collections.

**Returns:** The `CollectionEntity`, or `null` if not found.

---

##### UpdateCollectionAsync

```csharp
Task UpdateCollectionAsync(
    long collectionId,
    string name,
    string? description = null);
```

Updates the name and description of an existing collection.

---

##### DeleteCollectionAsync

```csharp
Task DeleteCollectionAsync(long collectionId, bool deleteDocuments = false);
```

Deletes a collection. Children are re-parented to the deleted collection's parent.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `collectionId` | `long` | -- | The ID of the collection to delete. |
| `deleteDocuments` | `bool` | `false` | If `true`, cascade-deletes all documents associated with this collection. If `false`, only the collection and its associations are removed; documents remain. |

---

##### AddDocumentToCollectionAsync

```csharp
Task AddDocumentToCollectionAsync(long documentId, long collectionId);
```

Associates a document with a collection. Creates a `DocumentCollectionEntity` join record.

---

##### RemoveDocumentFromCollectionAsync

```csharp
Task RemoveDocumentFromCollectionAsync(long documentId, long collectionId);
```

Removes the association between a document and a collection.

---

##### MoveCollectionAsync

```csharp
Task MoveCollectionAsync(long collectionId, long? newParentId);
```

Moves a collection to a new parent, or to root level if `newParentId` is `null`.

---

##### GetCollectionCountAsync

```csharp
Task<int> GetCollectionCountAsync();
```

Returns the total number of collections in the database.

---

##### GetDocumentsInCollectionAsync

```csharp
Task<IReadOnlyList<DocumentEntity>> GetDocumentsInCollectionAsync(long collectionId);
```

Retrieves all documents belonging to a specific collection via the `DocumentCollectionEntity` join table.

---

### IAutoTagService

```csharp
namespace AgentX.Core.Services.Tagging;

public interface IAutoTagService
```

AI-powered automatic tagging and manual tag management. Provides both AI-generated tag suggestions and CRUD operations for the tag system.

**Namespace:** `AgentX.Core.Services.Tagging`
**Assembly:** `AgentX.Core`
**Implementation:** `AutoTagService`

#### Methods

---

##### GenerateTagsAsync

```csharp
Task<IReadOnlyList<(string TagName, double Confidence)>> GenerateTagsAsync(
    string documentContent,
    int maxTags = 5,
    CancellationToken ct = default);
```

Uses the AI service to generate descriptive tags for the given document content.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `documentContent` | `string` | -- | The text content to analyze for tag generation. |
| `maxTags` | `int` | `5` | Maximum number of tags to generate. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** A list of tuples containing tag names paired with confidence scores (0.0 to 1.0).

---

##### ApplyAutoTagsAsync

```csharp
Task ApplyAutoTagsAsync(long documentId, CancellationToken ct = default);
```

Generates tags for a document and persists them as `TagEntity`/`DocumentTagEntity` records.

**Behavior:**
- Existing tags are matched by name (case-insensitive); new tags are created as auto-generated (`IsAutoGenerated = true`).
- Duplicate document-tag associations are skipped.

---

##### GetAllTagsAsync

```csharp
Task<IReadOnlyList<TagEntity>> GetAllTagsAsync();
```

Retrieves all tags in the system, ordered by name.

---

##### CreateTagAsync

```csharp
Task<TagEntity> CreateTagAsync(string name, string? colorHex = null);
```

Creates a new tag with the given name and optional display color.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `name` | `string` | -- | The tag name (must not be empty, must be unique). |
| `colorHex` | `string?` | `null` | Optional hex color string for display (e.g., `"#FF5733"`). |

---

##### DeleteTagAsync

```csharp
Task DeleteTagAsync(long tagId);
```

Deletes a tag by ID. Cascade removes all `DocumentTagEntity` associations.

---

##### AssignTagAsync

```csharp
Task AssignTagAsync(long documentId, long tagId);
```

Manually assigns a tag to a document with full confidence (1.0).

---

##### RemoveTagAsync

```csharp
Task RemoveTagAsync(long documentId, long tagId);
```

Removes a tag assignment from a document.

---

##### GetTagsForDocumentAsync

```csharp
Task<IReadOnlyList<TagEntity>> GetTagsForDocumentAsync(long documentId);
```

Retrieves all tags currently assigned to a specific document.

---

## 8. Indexing Services

### IIndexingService

```csharp
namespace AgentX.Core.Services.Indexing;

public interface IIndexingService : IDisposable
```

Manages the background indexing pipeline: processes pending documents by chunking their extracted text, generating embeddings, and storing vectors for semantic search.

**Namespace:** `AgentX.Core.Services.Indexing`
**Assembly:** `AgentX.Core`
**Implementation:** `IndexingService`

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsProcessing` | `bool` | Indicates whether the indexing service is currently processing a document. |

#### Events

| Event | Type | Description |
|-------|------|-------------|
| `ProgressChanged` | `EventHandler<IndexingProgressEventArgs>?` | Raised when the indexing queue state changes (item queued, processing, completed, etc.). |
| `DocumentIndexed` | `EventHandler<long>?` | Raised when a document has been successfully indexed. The event argument is the document ID. |

#### Methods

---

##### InitializeAsync

```csharp
Task InitializeAsync(CancellationToken ct = default);
```

Initializes the indexing service: sets up the vector store and starts the background processing loop for queued indexing jobs.

---

##### IndexDocumentAsync

```csharp
Task IndexDocumentAsync(long documentId, CancellationToken ct = default);
```

Indexes a single document: re-processes the file, chunks the text, generates embeddings, and stores them in the vector database.

---

##### ReindexAllAsync

```csharp
Task ReindexAllAsync(
    IProgress<(int Processed, int Total)>? progress = null,
    CancellationToken ct = default);
```

Re-indexes all completed documents. Useful after changing chunking or embedding settings.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `progress` | `IProgress<(int Processed, int Total)>?` | `null` | Optional progress reporter with (processed, total) tuple. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

---

##### GetQueueLengthAsync

```csharp
Task<int> GetQueueLengthAsync();
```

Returns the number of documents currently waiting in the indexing queue.

---

##### GetProcessedCountAsync

```csharp
Task<int> GetProcessedCountAsync();
```

Returns the total number of documents that have been successfully indexed.

---

### IIndexingQueueService

```csharp
namespace AgentX.Core.Services.Indexing;

public interface IIndexingQueueService
```

Manages the persistent indexing job queue backed by the database. Provides enqueue, dequeue, and status update operations for `IndexingJobEntity` records.

**Namespace:** `AgentX.Core.Services.Indexing`
**Assembly:** `AgentX.Core`
**Implementation:** `IndexingQueueService`

#### Methods

---

##### EnqueueAsync

```csharp
Task EnqueueAsync(long documentId);
```

Creates a new indexing job for the specified document with `Status = "queued"`.

---

##### EnqueueBatchAsync

```csharp
Task EnqueueBatchAsync(IReadOnlyList<long> documentIds);
```

Creates indexing jobs for multiple documents at once.

---

##### DequeueAsync

```csharp
Task<IndexingJobEntity?> DequeueAsync(CancellationToken ct = default);
```

Atomically dequeues the oldest queued job by setting its status to `"processing"` and recording the start time.

**Returns:** The dequeued `IndexingJobEntity`, or `null` if the queue is empty.

---

##### MarkCompletedAsync

```csharp
Task MarkCompletedAsync(
    long jobId,
    int chunksProcessed,
    int embeddingsGenerated,
    double processingTimeMs);
```

Marks a job as successfully completed with processing metrics.

**Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `jobId` | `long` | The ID of the indexing job. |
| `chunksProcessed` | `int` | Number of text chunks created. |
| `embeddingsGenerated` | `int` | Number of embedding vectors generated. |
| `processingTimeMs` | `double` | Total processing time in milliseconds. |

---

##### MarkFailedAsync

```csharp
Task MarkFailedAsync(long jobId, string errorMessage);
```

Marks a job as failed with a descriptive error message.

**Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `jobId` | `long` | The ID of the indexing job. |
| `errorMessage` | `string` | A description of the error that caused the failure. |

---

##### GetPendingCountAsync

```csharp
Task<int> GetPendingCountAsync();
```

Returns the count of jobs that are either queued or currently processing.

---

##### GetRecentJobsAsync

```csharp
Task<IReadOnlyList<IndexingJobEntity>> GetRecentJobsAsync(int limit = 50);
```

Returns the most recent indexing jobs, ordered by `QueuedAt` descending.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `limit` | `int` | `50` | Maximum number of jobs to return. |

---

### IFileWatcherService

```csharp
namespace AgentX.Core.Services.Indexing;

public interface IFileWatcherService : IDisposable
```

Monitors registered watch folders for new or modified files and automatically imports them into the knowledge vault via `IDocumentService`. Uses `FileSystemWatcher` with per-file debouncing to avoid duplicate events.

**Namespace:** `AgentX.Core.Services.Indexing`
**Assembly:** `AgentX.Core`
**Implementation:** `FileWatcherService`

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsWatching` | `bool` | Indicates whether any watch folders are currently being monitored. |

#### Events

| Event | Type | Description |
|-------|------|-------------|
| `FileDetected` | `EventHandler<string>?` | Raised when a new or modified file is detected in a watched folder. The event argument is the full file path. |

#### Methods

---

##### StartWatchingAsync

```csharp
Task StartWatchingAsync(CancellationToken ct = default);
```

Loads all enabled watch folders from the database and starts a `FileSystemWatcher` for each one.

---

##### StopWatchingAsync

```csharp
Task StopWatchingAsync();
```

Stops all active file system watchers and clears internal state. The watch folder configuration is preserved in the database.

---

##### AddWatchFolderAsync

```csharp
Task AddWatchFolderAsync(
    string path,
    bool includeSubfolders = true,
    string? fileTypeFilter = null,
    long? collectionId = null);
```

Registers a new watch folder, persists it to the database, and starts watching immediately.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `path` | `string` | -- | Absolute path to the folder to watch. |
| `includeSubfolders` | `bool` | `true` | Whether to recursively monitor subdirectories. |
| `fileTypeFilter` | `string?` | `null` | Comma-separated list of extensions to watch (e.g., `"pdf,docx,txt"`). `null` means all supported types. |
| `collectionId` | `long?` | `null` | Optional collection to associate imported documents with. |

---

##### RemoveWatchFolderAsync

```csharp
Task RemoveWatchFolderAsync(long watchFolderId);
```

Stops watching a folder, removes its watcher, and deletes the database record.

---

##### GetWatchFoldersAsync

```csharp
Task<IReadOnlyList<WatchFolderEntity>> GetWatchFoldersAsync();
```

Returns all registered watch folders from the database.

---

### IndexingProgressEventArgs

```csharp
namespace AgentX.Core.Services.Indexing;

public class IndexingProgressEventArgs : EventArgs
```

Event data for indexing progress updates.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `QueueLength` | `int` | -- | Number of items remaining in the indexing queue (init-only). |
| `Processed` | `int` | -- | Number of items processed so far in the current batch or since initialization (init-only). |
| `CurrentDocument` | `string?` | `null` | The file name of the document currently being processed (`null` if idle) (init-only). |
| `PercentComplete` | `double?` | `null` | Overall completion percentage (0-100), or `null` if indeterminate (init-only). |

---

## 9. Settings

### ISettingsService

```csharp
namespace AgentX.Core.Services.Settings;

public interface ISettingsService
```

Manages application settings persistence. Settings are stored as a JSON file at `%LOCALAPPDATA%/AgentX/settings.json`.

**Namespace:** `AgentX.Core.Services.Settings`
**Assembly:** `AgentX.Core`
**Implementation:** `SettingsService`

#### Methods

---

##### GetSettingsAsync

```csharp
Task<AppSettings> GetSettingsAsync();
```

Returns the current application settings. Results are cached in memory after the first load. If no settings file exists, creates one with default values.

**Returns:** The current `AppSettings` instance.

---

##### SaveSettingsAsync

```csharp
Task SaveSettingsAsync(AppSettings settings);
```

Persists the settings to disk and updates the in-memory cache.

**Exceptions:** Throws on I/O errors.

---

##### GetValueAsync\<T\>

```csharp
Task<T?> GetValueAsync<T>(string key);
```

Retrieves a single setting value by property name using reflection.

**Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `key` | `string` | The name of the `AppSettings` property to read. |

**Returns:** The value cast to `T`, or `default(T)` if the property is not found.

---

##### SetValueAsync\<T\>

```csharp
Task SetValueAsync<T>(string key, T value);
```

Sets a single setting value by property name and persists to disk.

**Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `key` | `string` | The name of the `AppSettings` property to write. |
| `value` | `T` | The value to set. |

---

### AppSettings

```csharp
namespace AgentX.Core.Services.Settings;

public class AppSettings
```

Application configuration stored as `settings.json` in `%LOCALAPPDATA%/AgentX/`.

| Property | Type | Default | Category | Description |
|----------|------|---------|----------|-------------|
| `OnboardingCompleted` | `bool` | `false` | Onboarding | Whether the user has completed the first-run setup wizard. |
| `OllamaEndpoint` | `string` | `"http://localhost:11434"` | AI Provider | The base URL for the Ollama API. |
| `DefaultModel` | `string` | `"llama3.2"` | AI Provider | The default model identifier for chat inference. |
| `EmbeddingModel` | `string` | `"all-minilm"` | AI Provider | The model used for generating vector embeddings. |
| `Temperature` | `double` | `0.7` | Inference | Default temperature for AI inference. |
| `MaxTokens` | `int` | `4096` | Inference | Default maximum token count for AI responses. |
| `ContextWindow` | `int` | `8192` | Inference | Default context window size. |
| `ChunkSize` | `int` | `512` | Knowledge Vault | Maximum tokens per text chunk during document indexing. |
| `ChunkOverlap` | `int` | `50` | Knowledge Vault | Number of overlapping tokens between consecutive chunks. |
| `TopKResults` | `int` | `5` | Knowledge Vault | Number of top results to retrieve during semantic search. |
| `AutoIndexWatchFolders` | `bool` | `true` | Knowledge Vault | Whether to automatically index files detected in watch folders. |
| `StoragePath` | `string` | `%LOCALAPPDATA%/AgentX` | Storage | Base path for application data storage (databases, indexes, etc.). |

---

## 10. Intelligence Services

### ISummaryService

```csharp
namespace AgentX.Core.Services.Intelligence;

public interface ISummaryService
```

Provides AI-powered document summarization, key-point extraction, and text translation capabilities.

**Namespace:** `AgentX.Core.Services.Intelligence`
**Assembly:** `AgentX.Core`
**Implementation:** `SummaryService`

#### Methods

---

##### SummarizeDocumentAsync

```csharp
Task<string> SummarizeDocumentAsync(long documentId, CancellationToken ct = default);
```

Generates a concise summary of a document by its ID. Loads the document and its chunks from the database, concatenates chunk text (up to 8000 characters), and uses the AI service to produce a summary.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `documentId` | `long` | -- | The primary key of the document to summarize. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** `string` -- A concise AI-generated summary.

**Exceptions:**
- `InvalidOperationException` -- Thrown when the document is not found or has no indexed chunks.

---

##### ExtractKeyPointsAsync

```csharp
Task<IReadOnlyList<string>> ExtractKeyPointsAsync(
    long documentId,
    CancellationToken ct = default);
```

Extracts key points (bullet list) from a document by its ID. Each key point is a concise, single-sentence summary of an important finding or topic.

**Returns:** An ordered list of key point strings.

**Exceptions:**
- `InvalidOperationException` -- Thrown when the document is not found or has no indexed chunks.

---

##### TranslateTextAsync

```csharp
Task<string> TranslateTextAsync(
    string text,
    string targetLanguage,
    CancellationToken ct = default);
```

Translates the given text to the specified target language. Input text is capped at 4000 characters to fit within context limits.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `text` | `string` | -- | The source text to translate. |
| `targetLanguage` | `string` | -- | The target language (e.g., `"Spanish"`, `"French"`, `"Japanese"`). |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** `string` -- The translated text.

**Exceptions:**
- `ArgumentException` -- Thrown when `text` or `targetLanguage` is null or empty.

---

### IDuplicateDetectionService

```csharp
namespace AgentX.Core.Services.Intelligence;

public interface IDuplicateDetectionService
```

Detects duplicate and near-duplicate documents in the knowledge vault. Supports both exact-match detection via content hashes and semantic near-duplicate detection via vector embedding similarity.

**Namespace:** `AgentX.Core.Services.Intelligence`
**Assembly:** `AgentX.Core`
**Implementation:** `DuplicateDetectionService`

#### Methods

---

##### FindDuplicatesAsync

```csharp
Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(CancellationToken ct = default);
```

Scans all documents and groups those with identical content hashes. This is an efficient operation that requires no AI inference -- it relies solely on the SHA-256 content hashes computed during document import.

**Returns:** A list of `DuplicateGroup` instances, each containing two or more documents that share the same content hash. Returns an empty list if no duplicates are found.

---

##### FindNearDuplicatesAsync

```csharp
Task<IReadOnlyList<DuplicateGroup>> FindNearDuplicatesAsync(
    float similarityThreshold = 0.9f,
    CancellationToken ct = default);
```

Finds documents that are near-duplicates based on semantic similarity. Uses vector embeddings to identify documents whose content is similar but not necessarily byte-for-byte identical (e.g., reformatted versions, minor edits, or different file formats of the same content).

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `similarityThreshold` | `float` | `0.9f` | The minimum cosine similarity (0.0 to 1.0) required to consider two documents as near-duplicates. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** A list of `DuplicateGroup` instances for near-duplicate documents. Returns an empty list if none are found.

**Performance Note:** This operation is more expensive than exact-hash detection as it requires loading and comparing vector embeddings. The scan is capped at the first 500 documents to avoid excessive computation time.

---

### IOrganizationSuggestionService

```csharp
namespace AgentX.Core.Services.Intelligence;

public interface IOrganizationSuggestionService
```

Analyzes uncategorized documents and provides AI-powered suggestions for organizing them into collections with appropriate tags.

**Namespace:** `AgentX.Core.Services.Intelligence`
**Assembly:** `AgentX.Core`
**Implementation:** `OrganizationSuggestionService`

#### Methods

---

##### SuggestOrganizationAsync

```csharp
Task<IReadOnlyList<OrganizationSuggestion>> SuggestOrganizationAsync(
    int maxDocuments = 20,
    CancellationToken ct = default);
```

Analyzes documents that have no collection associations and suggests appropriate collections and tags for each one based on their content.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `maxDocuments` | `int` | `20` | The maximum number of uncategorized documents to analyze in a single batch. Defaults to 20 to balance thoroughness with response time. |
| `ct` | `CancellationToken` | `default` | Cancellation token. |

**Returns:** A list of `OrganizationSuggestion` instances, one per analyzed document. Returns an empty list if all documents are already categorized.

---

### Intelligence Models

#### DuplicateGroup

```csharp
namespace AgentX.Core.Services.Intelligence.Models;

public class DuplicateGroup
```

Represents a group of documents that share identical or near-identical content.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ContentHash` | `string` | `""` | The content hash shared by all documents in this group (for exact duplicates), or the hash of the reference document (for near-duplicates) (init-only). |
| `Documents` | `List<DuplicateDocument>` | `new()` | The documents in this group. The first document is the "original"; subsequent entries are duplicates (init-only). |
| `WastedStorageBytes` | `long` | *(computed)* | Total storage consumed by duplicate copies (all documents except the first/original). |

#### DuplicateDocument

```csharp
namespace AgentX.Core.Services.Intelligence.Models;

public class DuplicateDocument
```

Metadata for a single document within a duplicate group.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DocumentId` | `long` | -- | Primary key of the document (init-only). |
| `FileName` | `string` | `""` | Original file name (init-only). |
| `FilePath` | `string` | `""` | Absolute file path (init-only). |
| `FileSizeBytes` | `long` | -- | File size in bytes (init-only). |
| `ImportedAt` | `DateTime` | -- | Timestamp when imported into the knowledge vault (init-only). |

#### OrganizationSuggestion

```csharp
namespace AgentX.Core.Services.Intelligence.Models;

public class OrganizationSuggestion
```

An AI-generated suggestion for organizing an uncategorized document.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DocumentId` | `long` | -- | Primary key of the document this suggestion applies to (init-only). |
| `FileName` | `string` | `""` | File name of the document (init-only). |
| `SuggestedCollection` | `string` | `""` | The collection name the AI suggests. May be an existing or new collection name (init-only). |
| `SuggestedTags` | `List<string>` | `new()` | A list of 2-3 descriptive tags the AI suggests (init-only). |
| `Reasoning` | `string` | `""` | The AI's reasoning for the suggested organization (init-only). |
| `Confidence` | `float` | -- | Confidence score from 0.0 (no confidence) to 1.0 (certain) (init-only). |

---

## 11. Database Entities

All entities are managed by Entity Framework Core via `AgentXDbContext`. The database is SQLite, stored at the path configured in `AppSettings.StoragePath`.

**Namespace:** `AgentX.Core.Data.Entities`
**Assembly:** `AgentX.Core`

---

### ConversationEntity

Represents a chat conversation with an AI model.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key (auto-increment). |
| `Title` | `string` | `""` | Display title of the conversation. |
| `SystemPrompt` | `string?` | `null` | System prompt used for all messages in this conversation. |
| `ModelId` | `string` | `""` | The AI model identifier used for this conversation. |
| `CreatedAt` | `DateTime` | *(set on create)* | When the conversation was created. |
| `UpdatedAt` | `DateTime` | *(set on modify)* | When the conversation was last updated. |
| `IsPinned` | `bool` | `false` | Whether the conversation is pinned to the top of the list. |
| `IsArchived` | `bool` | `false` | Whether the conversation is archived (hidden from default view). |
| `MessageCount` | `int` | `0` | Total number of messages in the conversation. |
| `TokensUsed` | `long` | `0` | Cumulative token count across all messages. |

**Navigation Properties:**

| Property | Type | Relationship |
|----------|------|-------------|
| `Messages` | `ICollection<MessageEntity>` | One-to-many: a conversation has many messages. |

---

### MessageEntity

Represents a single message within a conversation.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key (auto-increment). |
| `ConversationId` | `long` | -- | Foreign key to `ConversationEntity`. |
| `Role` | `string` | `""` | Message role: `"user"`, `"assistant"`, or `"system"`. |
| `Content` | `string` | `""` | The message text content. |
| `Timestamp` | `DateTime` | *(set on create)* | When the message was created. |
| `TokenCount` | `int` | `0` | Estimated token count for this message. |
| `GenerationTimeMs` | `double?` | `null` | Generation time in milliseconds (for assistant messages only). |
| `ModelId` | `string?` | `null` | The AI model that generated this message (for assistant messages). |
| `CitationsJson` | `string?` | `null` | JSON array of `Citation` objects (for RAG-sourced assistant messages). |
| `SortOrder` | `int` | `0` | Ordering index within the conversation. |

**Navigation Properties:**

| Property | Type | Relationship |
|----------|------|-------------|
| `Conversation` | `ConversationEntity` | Many-to-one: each message belongs to one conversation. |

---

### DocumentEntity

Represents an imported document in the knowledge vault.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key (auto-increment). |
| `FileName` | `string` | `""` | The original file name. |
| `FilePath` | `string` | `""` | Absolute path to the source file. |
| `FileType` | `string` | `""` | File type extension without dot (e.g., `"pdf"`, `"docx"`, `"txt"`). |
| `MimeType` | `string?` | `null` | MIME type of the file. |
| `FileSizeBytes` | `long` | `0` | File size in bytes. |
| `ContentHash` | `string` | `""` | SHA-256 hash of the file content for duplicate detection. |
| `ImportedAt` | `DateTime` | *(set on create)* | When the document was imported. |
| `FileModifiedAt` | `DateTime` | *(from file)* | Last modification time of the source file. |
| `LastIndexedAt` | `DateTime?` | `null` | When the document was last successfully indexed. |
| `IndexingStatus` | `string` | `"pending"` | Indexing status: `"pending"`, `"processing"`, `"completed"`, `"failed"`. |
| `IndexingError` | `string?` | `null` | Error message if indexing failed. |
| `ChunkCount` | `int` | `0` | Number of text chunks created from this document. |
| `PageCount` | `int` | `0` | Number of pages in the source document. |
| `WordCount` | `long` | `0` | Approximate word count of extracted text. |
| `Summary` | `string?` | `null` | AI-generated summary of the document content. |
| `ExtractedTitle` | `string?` | `null` | Title extracted from document metadata. |
| `Language` | `string?` | `null` | Detected language of the document content. |
| `ThumbnailPath` | `string?` | `null` | Path to a generated thumbnail image. |
| `MetadataJson` | `string?` | `null` | Additional metadata stored as a JSON string. |

**Navigation Properties:**

| Property | Type | Relationship |
|----------|------|-------------|
| `Chunks` | `ICollection<DocumentChunkEntity>` | One-to-many: a document has many chunks. |
| `DocumentCollections` | `ICollection<DocumentCollectionEntity>` | Many-to-many join: document-collection associations. |
| `DocumentTags` | `ICollection<DocumentTagEntity>` | Many-to-many join: document-tag associations. |

---

### DocumentChunkEntity

Represents a single text chunk extracted from a document, suitable for embedding.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key (auto-increment). |
| `DocumentId` | `long` | -- | Foreign key to `DocumentEntity`. |
| `ChunkIndex` | `int` | `0` | Zero-based index of this chunk within the document. |
| `Content` | `string` | `""` | The text content of the chunk. |
| `StartCharOffset` | `int` | `0` | Starting character offset within the source document text. |
| `EndCharOffset` | `int` | `0` | Ending character offset within the source document text. |
| `PageNumber` | `int?` | `null` | The page number this chunk belongs to (if available). |
| `SectionTitle` | `string?` | `null` | The section title this chunk falls under (if available). |
| `TokenCount` | `int` | `0` | Approximate token count for this chunk. |
| `IsEmbedded` | `bool` | `false` | Whether an embedding vector has been generated for this chunk. |
| `VectorRowId` | `long?` | `null` | Foreign key to the sqlite-vec virtual table row containing the embedding. |

**Navigation Properties:**

| Property | Type | Relationship |
|----------|------|-------------|
| `Document` | `DocumentEntity` | Many-to-one: each chunk belongs to one document. |

---

### CollectionEntity

Represents a hierarchical document collection (folder-like organization).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key (auto-increment). |
| `Name` | `string` | `""` | Display name of the collection. |
| `Description` | `string?` | `null` | Optional description. |
| `IconGlyph` | `string?` | `null` | Segoe Fluent Icons glyph for UI display. |
| `ColorHex` | `string?` | `null` | Hex color code for UI display (e.g., `"#3B82F6"`). |
| `ParentCollectionId` | `long?` | `null` | Foreign key to parent `CollectionEntity`. `null` for root collections. |
| `CreatedAt` | `DateTime` | *(set on create)* | When the collection was created. |
| `UpdatedAt` | `DateTime` | *(set on modify)* | When the collection was last updated. |
| `DocumentCount` | `int` | `0` | Number of documents in this collection. |
| `SortOrder` | `int` | `0` | Display ordering index. |

**Navigation Properties:**

| Property | Type | Relationship |
|----------|------|-------------|
| `ParentCollection` | `CollectionEntity?` | Self-referential many-to-one: parent collection. |
| `ChildCollections` | `ICollection<CollectionEntity>` | Self-referential one-to-many: child collections. |
| `DocumentCollections` | `ICollection<DocumentCollectionEntity>` | Many-to-many join: collection-document associations. |

---

### DocumentCollectionEntity

Join table for the many-to-many relationship between documents and collections.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DocumentId` | `long` | -- | Foreign key to `DocumentEntity`. Composite primary key part 1. |
| `CollectionId` | `long` | -- | Foreign key to `CollectionEntity`. Composite primary key part 2. |
| `AddedAt` | `DateTime` | *(set on create)* | When the document was added to the collection. |

**Navigation Properties:**

| Property | Type | Relationship |
|----------|------|-------------|
| `Document` | `DocumentEntity` | Many-to-one. |
| `Collection` | `CollectionEntity` | Many-to-one. |

---

### TagEntity

Represents a tag that can be applied to documents.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key (auto-increment). |
| `Name` | `string` | `""` | Tag name (unique, case-insensitive). |
| `ColorHex` | `string?` | `null` | Hex color code for UI display (e.g., `"#FF5733"`). |
| `IsAutoGenerated` | `bool` | `false` | Whether this tag was created by the AI auto-tagging system. |
| `CreatedAt` | `DateTime` | *(set on create)* | When the tag was created. |

**Navigation Properties:**

| Property | Type | Relationship |
|----------|------|-------------|
| `DocumentTags` | `ICollection<DocumentTagEntity>` | One-to-many: tag-document associations. |

---

### DocumentTagEntity

Join table for the many-to-many relationship between documents and tags.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DocumentId` | `long` | -- | Foreign key to `DocumentEntity`. Composite primary key part 1. |
| `TagId` | `long` | -- | Foreign key to `TagEntity`. Composite primary key part 2. |
| `Confidence` | `double` | `0.0` | Confidence score (0.0 to 1.0) for auto-generated tags. Manual assignments use `1.0`. |
| `AssignedAt` | `DateTime` | *(set on create)* | When the tag was assigned to the document. |

**Navigation Properties:**

| Property | Type | Relationship |
|----------|------|-------------|
| `Document` | `DocumentEntity` | Many-to-one. |
| `Tag` | `TagEntity` | Many-to-one. |

---

### SearchHistoryEntity

Stores search query history for re-use and analytics.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key (auto-increment). |
| `Query` | `string` | `""` | The search query text. |
| `SearchType` | `string` | `"semantic"` | Type of search: `"semantic"`, `"keyword"`, or `"rag"`. |
| `ResultCount` | `int` | `0` | Number of results returned for this search. |
| `SearchedAt` | `DateTime` | *(set on create)* | Timestamp of the search. |
| `IsSaved` | `bool` | `false` | Whether the user has explicitly saved this search. |
| `CollectionFilter` | `string?` | `null` | Comma-separated collection IDs used as filters. |

---

### SystemPromptEntity

Represents a reusable system prompt template.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key (auto-increment). |
| `Name` | `string` | `""` | Display name of the prompt. |
| `Content` | `string` | `""` | The full system prompt text. |
| `Category` | `string` | `"General"` | Category classification: `"General"`, `"Writing"`, `"Code"`, `"Analysis"`, `"Creative"`. |
| `IsBuiltIn` | `bool` | `false` | Whether this is a built-in prompt (cannot be deleted). |
| `IsFavorite` | `bool` | `false` | Whether the user has favorited this prompt. |
| `CreatedAt` | `DateTime` | *(set on create)* | When the prompt was created. |
| `UpdatedAt` | `DateTime` | *(set on modify)* | When the prompt was last updated. |
| `UsageCount` | `int` | `0` | Number of times this prompt has been used. |

---

### UserSettingsEntity

Key-value store for individual user settings, providing a flexible schema for settings that do not fit in `AppSettings`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key (auto-increment). |
| `Key` | `string` | `""` | The setting key (unique). |
| `Value` | `string` | `""` | The serialized setting value. |
| `ValueType` | `string` | `"string"` | The data type of the value: `"string"`, `"int"`, `"bool"`, `"double"`, `"json"`. |
| `UpdatedAt` | `DateTime` | *(set on modify)* | When the setting was last updated. |

---

### WatchFolderEntity

Represents a folder being monitored for automatic document import.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key (auto-increment). |
| `FolderPath` | `string` | `""` | Absolute path to the watched folder. |
| `IsEnabled` | `bool` | `false` | Whether this watch folder is currently active. |
| `IncludeSubfolders` | `bool` | `false` | Whether to recursively monitor subdirectories. |
| `FileTypeFilter` | `string?` | `null` | Comma-separated extension filter (e.g., `"pdf,docx,txt,md"`). `null` = all supported types. |
| `TargetCollectionId` | `long?` | `null` | Foreign key to `CollectionEntity`. Documents imported from this folder are associated with this collection. |
| `CreatedAt` | `DateTime` | *(set on create)* | When the watch folder was registered. |
| `LastScanAt` | `DateTime?` | `null` | When the folder was last scanned. |
| `FilesIndexed` | `int` | `0` | Cumulative count of files imported from this folder. |

**Navigation Properties:**

| Property | Type | Relationship |
|----------|------|-------------|
| `TargetCollection` | `CollectionEntity?` | Many-to-one: optional target collection. |

---

### IndexingJobEntity

Represents a single document indexing job in the processing queue.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key (auto-increment). |
| `DocumentId` | `long` | -- | Foreign key to `DocumentEntity`. |
| `Status` | `string` | `"queued"` | Job status: `"queued"`, `"processing"`, `"completed"`, `"failed"`. |
| `QueuedAt` | `DateTime` | *(set on create)* | When the job was added to the queue. |
| `StartedAt` | `DateTime?` | `null` | When processing began. |
| `CompletedAt` | `DateTime?` | `null` | When processing finished (success or failure). |
| `ErrorMessage` | `string?` | `null` | Error description if the job failed. |
| `ChunksProcessed` | `int` | `0` | Number of text chunks created during indexing. |
| `EmbeddingsGenerated` | `int` | `0` | Number of embedding vectors generated during indexing. |
| `ProcessingTimeMs` | `double?` | `null` | Total processing time in milliseconds. |

**Navigation Properties:**

| Property | Type | Relationship |
|----------|------|-------------|
| `Document` | `DocumentEntity` | Many-to-one: each job references one document. |

---


---

## 12. OAuth Services

### IOAuthService

```csharp
namespace AgentX.Core.Services.OAuth;

public interface IOAuthService
```

Manages the OAuth2 authorization code flow for external providers (Google, Microsoft). Handles browser-based consent, token exchange, DPAPI-encrypted persistence, automatic refresh, and server-side revocation.

**Namespace:** `AgentX.Core.Services.OAuth`
**Assembly:** `AgentX.Core`

#### Methods

| Method | Return Type | Description |
|--------|------------|-------------|
| `AuthorizeAsync(string provider, string? scopes = null, string? redirectUri = null, CancellationToken cancellationToken = default)` | `Task<OAuthCredential>` | Opens a browser consent screen, exchanges the authorization code for tokens, encrypts and persists the credential. Returns the decrypted credential. |
| `GetAccessTokenAsync(string provider)` | `Task<string>` | Returns a valid access token for the provider. Automatically refreshes if the token is expired or within 5 minutes of expiry. |
| `RefreshTokenAsync(string provider)` | `Task<bool>` | Refreshes the access token using the stored refresh token. Returns `true` if refresh succeeded. Uses per-provider semaphore to prevent concurrent refresh races. |
| `RevokeAsync(string provider)` | `Task` | Sends a server-side revocation request to the provider's revocation endpoint (if configured), then deletes the local credential. |
| `GetCredentialAsync(string provider)` | `Task<OAuthCredential?>` | Returns the stored credential for the provider, or `null` if the user has not authorized. Does not trigger a refresh. |

---

### OAuthService

```csharp
namespace AgentX.Core.Services.OAuth;

public sealed class OAuthService : IOAuthService, IDisposable
```

Production implementation of `IOAuthService`. Manages the full OAuth2 authorization code flow for desktop applications, including browser-based consent, token exchange, DPAPI-encrypted persistence, automatic token refresh (5-minute buffer), and server-side revocation.

**Namespace:** `AgentX.Core.Services.OAuth`
**Assembly:** `AgentX.Core`

**Thread Safety:** Per-provider `SemaphoreSlim` guards prevent concurrent token refresh operations.

**DPAPI Encryption:** All tokens are encrypted via `IDpapiEncryptionService` before being persisted to SQLite. Decryption happens only at runtime, in memory.

**Auto-Refresh:** `GetAccessTokenAsync` checks whether the stored access token is expired or within 5 minutes of expiry. If so, it calls `RefreshTokenAsync` automatically before returning the token.

#### Constructor

```csharp
public OAuthService(AgentXDbContext db, IDpapiEncryptionService encryption, ILogger logger)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `db` | `AgentXDbContext` | Application database context for credential persistence. |
| `encryption` | `IDpapiEncryptionService` | DPAPI encryption service for token protection. |
| `logger` | `ILogger` | Application-level Serilog logger. |

#### Methods

| Method | Return Type | Description |
|--------|------------|-------------|
| `RegisterProvider(OAuthProviderConfig config)` | `void` | Registers an OAuth provider configuration. Must be called before `AuthorizeAsync` or `RefreshTokenAsync`. |
| `GetRegisteredProviders()` | `IReadOnlyDictionary<string, OAuthProviderConfig>` | Returns all registered provider configurations. |

#### Internal Methods

| Method | Description |
|--------|-------------|
| `BuildAuthorizationUrl(...)` | Builds the full authorization URL with CSRF state parameter and PKCE code_challenge. |
| `ExchangeCodeForTokensAsync(...)` | Exchanges authorization code for tokens via the token endpoint, including PKCE code_verifier. |
| `RefreshAccessTokenAsync(...)` | Refreshes an expired access token using the stored refresh token. |
| `PersistCredentialAsync(...)` | Encrypts tokens and persists the credential to the database. |
| `RevokeTokenAsync(...)` | Sends a server-side revocation request to the provider. |
| `DecryptCredential(...)` | Decrypts an `OAuthCredentialEntity` into a plain `OAuthCredential`. |

---

### OAuthProviderConfig

```csharp
namespace AgentX.Core.Services.OAuth;

public sealed class OAuthProviderConfig
```

Immutable configuration for an OAuth2 provider. Contains the endpoints, client credentials, and default scopes needed to initiate and complete the authorization code flow.

**Namespace:** `AgentX.Core.Services.OAuth`
**Assembly:** `AgentX.Core`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ProviderId` | `string` | `""` | Stable identifier (e.g. `"google"`, `"microsoft"`). Must match `OAuthCredential.ProviderId`. |
| `DisplayName` | `string` | `""` | Display name for UI (e.g. `"Google Calendar"`, `"Microsoft Outlook"`). |
| `ClientId` | `string` | `""` | OAuth2 client ID from the provider's developer console. |
| `ClientSecret` | `string` | `""` | OAuth2 client secret. DPAPI-encrypted at rest when loaded from settings. |
| `AuthorizationEndpoint` | `string` | `""` | OAuth2 authorization endpoint URL. |
| `TokenEndpoint` | `string` | `""` | OAuth2 token exchange endpoint URL. |
| `RevocationEndpoint` | `string?` | `null` | OAuth2 revocation endpoint URL. Leave empty to skip server-side revocation. |
| `Scopes` | `string` | `""` | Comma-separated default scopes for authorization. |
| `RedirectUri` | `string` | `""` | Redirect URI for the OAuth2 flow. Defaults to localhost listener. |
| `ExtraAuthParameters` | `Dictionary<string, string>` | `new()` | Provider-specific auth parameters (e.g. Google: `access_type=offline`, `prompt=consent`; Microsoft: `prompt=select_account`). |

---

### OAuthProviderRegistry

```csharp
namespace AgentX.Core.Services.OAuth;

public static class OAuthProviderRegistry
```

Static factory for pre-configured OAuth2 provider configurations (Google, Microsoft). These factories incorporate the correct authorization/token/revocation endpoints, default scopes, and provider-specific extra parameters.

**Namespace:** `AgentX.Core.Services.OAuth`
**Assembly:** `AgentX.Core`

#### Constants

| Constant | Type | Value | Description |
|----------|------|-------|-------------|
| `ProviderIdGoogle` | `string` | `"google"` | Stable identifier for the Google OAuth2 provider. |
| `ProviderIdMicrosoft` | `string` | `"microsoft"` | Stable identifier for the Microsoft OAuth2 provider. |

#### Methods

| Method | Return Type | Description |
|--------|------------|-------------|
| `CreateGoogleConfig(string clientId, string clientSecret, string? redirectUri = null)` | `OAuthProviderConfig` | Factory for Google OAuth2 config with correct endpoints, scopes, and `access_type=offline`, `prompt=consent`. |
| `CreateMicrosoftConfig(string clientId, string clientSecret, string? redirectUri = null)` | `OAuthProviderConfig` | Factory for Microsoft OAuth2 config with Graph endpoints, scopes, and `prompt=select_account`. |

---

### OAuthCredential

```csharp
namespace AgentX.Core.Services.OAuth;

public sealed class OAuthCredential
```

Decrypted OAuth2 credential DTO returned by `IOAuthService` methods. This is the clean in-memory representation, as opposed to the persisted `OAuthCredentialEntity` which stores encrypted tokens.

**Namespace:** `AgentX.Core.Services.OAuth`
**Assembly:** `AgentX.Core`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ProviderId` | `string` | `""` | Stable identifier of the OAuth provider (e.g. `"google"`, `"microsoft"`). |
| `AccessToken` | `string` | `""` | Decrypted access token for API calls. Never persisted in plaintext. |
| `RefreshToken` | `string` | `""` | Decrypted refresh token. Never persisted in plaintext. |
| `TokenExpiry` | `DateTime` | — | UTC timestamp when the access token expires. |
| `Scopes` | `string` | `""` | Comma-separated OAuth scopes granted (e.g. `"calendar.read,calendar.write,email.read"`). |
| `UserId` | `string` | `""` | Provider-specific user identifier (Google `sub` claim or Microsoft `oid` claim). |
| `CreatedAt` | `DateTime` | — | UTC timestamp when credential was first stored. |
| `UpdatedAt` | `DateTime` | — | UTC timestamp when credential was last refreshed/updated. |

---

## 13. Calendar Connector

### ICalendarService

```csharp
namespace AgentX.Core.Services.Plugins.Calendar;

public interface ICalendarService
```

High-level calendar service exposed by the `CalendarPlugin`. Provides upcoming event queries, full sync operations, and event detail retrieval. This is the public API that other AgentX services (search, RAG, Quick Chat) consume to access calendar data.

**Namespace:** `AgentX.Core.Services.Plugins.Calendar`
**Assembly:** `AgentX.Core`

#### Methods

| Method | Return Type | Description |
|--------|------------|-------------|
| `GetUpcomingEventsAsync(int daysAhead = 7, CancellationToken cancellationToken = default)` | `Task<IReadOnlyList<CalEvent>>` | Returns upcoming calendar events within the specified number of days ahead. Queries all enabled calendars across all connected providers. Sorted by start time. |
| `SyncCalendarsAsync(CancellationToken cancellationToken = default)` | `Task<SyncResult>` | Triggers a full sync cycle: fetches events from all enabled calendars across all connected providers and pushes new/updated items into the Smart Inbox pipeline. |
| `GetEventAsync(string eventId, string sourceProvider, string calendarId, CancellationToken cancellationToken = default)` | `Task<CalEvent?>` | Retrieves the full details of a specific calendar event by its provider-specific event ID and source provider. |
| `ListCalendarsAsync(CancellationToken cancellationToken = default)` | `Task<IReadOnlyList<CalendarInfo>>` | Lists all calendars available from connected providers. Used by the settings UI. |
| `IsConnected` | `bool` | Whether at least one calendar provider is connected (has valid OAuth credentials). |
| `GetSyncSettingsAsync()` | `Task<CalendarSyncSettings>` | Returns the current sync settings for the calendar connector. |
| `UpdateSyncSettingsAsync(CalendarSyncSettings settings)` | `Task` | Updates and persists the sync settings. |

---

### ICalendarProvider

```csharp
namespace AgentX.Core.Services.Plugins.Calendar;

public interface ICalendarProvider
```

Abstraction over a specific calendar API provider (Google Calendar, Microsoft Outlook). Implementations are registered per provider and use `IOAuthService` for authentication.

**Namespace:** `AgentX.Core.Services.Plugins.Calendar`
**Assembly:** `AgentX.Core`

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ProviderId` | `string` | Provider identifier matching `OAuthProviderConfig.ProviderId` (e.g. `"google"`, `"microsoft"`). |

#### Methods

| Method | Return Type | Description |
|--------|------------|-------------|
| `ListCalendarsAsync(CancellationToken cancellationToken = default)` | `Task<IReadOnlyList<CalendarInfo>>` | Lists all calendars the authenticated user has read access to. |
| `GetEventsAsync(string calendarId, DateTime start, DateTime end, string? deltaToken = null, CancellationToken cancellationToken = default)` | `Task<(IReadOnlyList<CalEvent> Events, string? NewDeltaToken)>` | Fetches events from a specific calendar within the given time range. Supports incremental sync via `deltaToken`. Returns events and optional new delta token. |

---

### CalendarPlugin

```csharp
namespace AgentX.Core.Services.Plugins.Calendar;

public sealed class CalendarPlugin : IPlugin
```

First-party DataConnector plugin that syncs Google Calendar and Microsoft Outlook calendar events into the AgentX knowledge vault. Events flow through the Smart Inbox pipeline and become searchable alongside documents.

**Namespace:** `AgentX.Core.Services.Plugins.Calendar`
**Assembly:** `AgentX.Core`

#### IPlugin Metadata

| Property | Value |
|----------|-------|
| `Id` | `"com.agentx.calendar"` |
| `Name` | `"Calendar Connector"` |
| `Description` | `"Syncs Outlook and Google Calendar events into your knowledge vault for AI-powered search."` |
| `Version` | `"1.0.0"` |
| `Author` | `"AgentX"` |
| `Type` | `PluginType.DataConnector` |

#### Events

| Event | Type | Description |
|-------|------|-------------|
| `SyncCompleted` | `EventHandler<SyncResult>` | Fired after each sync cycle completes. Subscribers can update UI without polling. |

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `LastSyncResult` | `SyncResult?` | The last sync result, or `null` if no sync has run yet. |

#### Lifecycle Methods

| Method | Description |
|--------|-------------|
| `InitializeAsync(IPluginContext)` | Resolves `IOAuthService` from plugin context, loads persisted sync settings, registers provider implementations. Does NOT start background sync. |
| `ActivateAsync()` | Registers providers and starts the periodic sync timer. |
| `DeactivateAsync()` | Stops the sync timer and flushes pending operations. |
| `DisposeAsync()` | Disposes the sync timer and releases resources. |

---

### Calendar Models

#### CalEvent

```csharp
namespace AgentX.Core.Services.Plugins.Calendar.Models;

public sealed class CalEvent
```

Unified calendar event DTO that provider implementations map their API-specific responses into.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `string` | `""` | Provider-specific event identifier (Google `id` or Microsoft `iCalUId`). Used for deduplication. |
| `Title` | `string` | `""` | Event title / subject line. |
| `Description` | `string?` | `null` | Full event description. May contain HTML from the provider. |
| `Start` | `DateTime` | — | Event start time (UTC). |
| `End` | `DateTime` | — | Event end time (UTC). |
| `Location` | `string?` | `null` | Event location (e.g. "Conference Room B" or video call URL). |
| `IsAllDay` | `bool` | `false` | Whether this is an all-day event. |
| `IsRecurring` | `bool` | `false` | Whether this event is part of a recurring series. |
| `Attendees` | `IReadOnlyList<CalAttendee>` | `[]` | List of attendees including the organizer. |
| `Organizer` | `string?` | `null` | Display name of the event organizer. |
| `CalendarName` | `string?` | `null` | Name of the calendar this event belongs to. |
| `SourceProvider` | `string` | `""` | Provider identifier: `"google"` or `"microsoft"`. |
| `HtmlLink` | `string?` | `null` | Link to view the event in the provider's web UI. |
| `CalendarId` | `string?` | `null` | Provider-specific calendar identifier for this event. |

#### CalAttendee

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DisplayName` | `string` | `""` | Attendee display name (may be empty if only email available). |
| `Email` | `string` | `""` | Attendee email address. |
| `ResponseStatus` | `string` | `"needsAction"` | Response status: `"accepted"`, `"declined"`, `"tentative"`, `"needsAction"`. |
| `IsOrganizer` | `bool` | `false` | Whether this attendee is the event organizer. |

#### CalendarInfo

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `string` | `""` | Provider-specific calendar identifier. |
| `Name` | `string` | `""` | Human-readable calendar name. |
| `Owner` | `string?` | `null` | Display name or email of the calendar owner. |
| `EventCount` | `int` | `0` | Approximate number of events in sync window. |
| `SourceProvider` | `string` | `""` | Provider identifier: `"google"` or `"microsoft"`. |
| `IsPrimary` | `bool` | `false` | Whether this is the user's primary calendar. |
| `LastSyncedAt` | `DateTime?` | `null` | UTC timestamp of last successful sync. Null if never synced. |

#### SyncResult

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ItemsAdded` | `int` | — | New items fetched and added to the inbox. |
| `ItemsUpdated` | `int` | — | Existing items modified since last sync. |
| `ItemsSkipped` | `int` | — | Items unchanged since last sync (detected via delta token). |
| `ItemsFailed` | `int` | — | Items that failed to process. Errors are logged individually. |
| `TotalItemsProcessed` | `int` | *(computed)* | `ItemsAdded + ItemsUpdated + ItemsSkipped + ItemsFailed`. |
| `IsSuccess` | `bool` | *(computed)* | `ItemsFailed == 0`. |
| `StartedAt` | `DateTime` | — | UTC timestamp when the sync started. |
| `CompletedAt` | `DateTime` | — | UTC timestamp when the sync completed. |
| `Duration` | `TimeSpan` | *(computed)* | `CompletedAt - StartedAt`. |
| `DeltaToken` | `string?` | `null` | Provider-specific delta token for incremental sync. Null for full syncs. |

#### CalendarSyncSettings

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EnabledCalendars` | `Dictionary<string, bool>` | `new()` | Map of calendar ID to enabled state. Only `true` calendars are synced. |
| `SyncIntervalMinutes` | `int` | `15` | Polling interval in minutes. |
| `DaysFutureToSync` | `int` | `30` | Days in the future to include. |
| `DaysPastToSync` | `int` | `90` | Days in the past to include. |
| `ConflictResolution` | `string` | `"RemoteWins"` | Conflict strategy: `"LocalWins"`, `"RemoteWins"`, or `"Merge"`. |
| `IncludeAttendeeDetails` | `bool` | `true` | Whether to include attendee names, emails, and response status. |
| `IncludeDescriptions` | `bool` | `true` | Whether to include full event description/body. |

---

## 14. Email Connector

### IEmailService

```csharp
namespace AgentX.Core.Services.Plugins.Email;

public interface IEmailService
```

High-level email service exposed by the EmailPlugin. Delegates to registered `IEmailProvider` instances.

**Namespace:** `AgentX.Core.Services.Plugins.Email`
**Assembly:** `AgentX.Core`

#### Methods

| Method | Return Type | Description |
|--------|------------|-------------|
| `GetRecentMessagesAsync(int count = 20, CancellationToken cancellationToken = default)` | `Task<IReadOnlyList<EmailMessage>>` | Gets recent messages across all enabled folders and providers, sorted by `ReceivedAt` descending. |
| `SyncMessagesAsync(CancellationToken cancellationToken = default)` | `Task<SyncResult>` | Triggers a sync cycle across all providers and folders. Pushes new/updated messages into the Smart Inbox. |
| `ListFoldersAsync(CancellationToken cancellationToken = default)` | `Task<IReadOnlyList<EmailFolderInfo>>` | Lists available mail folders from all connected providers. |
| `GetSyncSettingsAsync()` | `Task<EmailSyncSettings>` | Returns the current sync settings. |
| `UpdateSyncSettingsAsync(EmailSyncSettings settings)` | `Task` | Updates and persists sync settings. |
| `IsConnected` | `bool` | Whether at least one email provider is connected. |

---

### IEmailProvider

```csharp
namespace AgentX.Core.Services.Plugins.Email;

public interface IEmailProvider
```

Abstraction for an email provider (Gmail, Outlook). Each provider handles API-specific pagination, auth, and normalization.

**Namespace:** `AgentX.Core.Services.Plugins.Email`
**Assembly:** `AgentX.Core`

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ProviderId` | `string` | Unique provider identifier (e.g. `"google"`, `"microsoft"`). |

#### Methods

| Method | Return Type | Description |
|--------|------------|-------------|
| `ListFoldersAsync(CancellationToken cancellationToken = default)` | `Task<IReadOnlyList<EmailFolderInfo>>` | Lists all mail folders/labels available to the authenticated user. |
| `GetMessagesAsync(string folderId, int maxResults = 50, string? deltaToken = null, CancellationToken cancellationToken = default)` | `Task<(IReadOnlyList<EmailMessage> Messages, string? NewDeltaToken)>` | Fetches messages from a specific folder. Returns messages and optional delta token for incremental sync. |

---

### EmailPlugin

```csharp
namespace AgentX.Core.Services.Plugins.Email;

public sealed class EmailPlugin : IPlugin
```

Email Connector plugin. Implements the `IPlugin` lifecycle to provide email sync capabilities from Gmail and Microsoft Outlook.

**Namespace:** `AgentX.Core.Services.Plugins.Email`
**Assembly:** `AgentX.Core`

#### IPlugin Metadata

| Property | Value |
|----------|-------|
| `Id` | `"com.agentx.email"` |
| `Name` | `"Email Connector"` |
| `Description` | `"Syncs Gmail and Outlook emails into the knowledge vault."` |
| `Version` | `"1.0.0"` |
| `Author` | `"AgentX"` |
| `Type` | `PluginType.DataConnector` |

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Providers` | `IReadOnlyList<IEmailProvider>` | Currently registered email provider instances. |

#### Lifecycle Methods

| Method | Description |
|--------|-------------|
| `InitializeAsync(IPluginContext)` | Resolves `IOAuthService` and `IInboxService` from plugin context, loads persisted sync settings. |
| `ActivateAsync()` | Registers providers, creates the triage processor and sync service, starts the periodic sync timer. |
| `DeactivateAsync()` | Stops the sync timer and flushes pending operations. |
| `DisposeAsync()` | Disposes the sync timer and releases resources. |

---

### Email Models

#### EmailMessage

```csharp
namespace AgentX.Core.Services.Plugins.Email.Models;

public sealed class EmailMessage
```

Unified email message DTO returned by all email providers. Provider-specific JSON is normalized into this shape.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `string` | `""` | Provider-specific message identifier. |
| `Subject` | `string` | `""` | Message subject line. |
| `BodyPreview` | `string` | `""` | Short preview of the message body. |
| `BodyHtml` | `string` | `""` | Full HTML body content. |
| `BodyText` | `string` | `""` | Plain text body content. |
| `From` | `EmailContact` | `new()` | Sender contact information. |
| `To` | `List<EmailContact>` | `[]` | To recipients. |
| `Cc` | `List<EmailContact>` | `[]` | CC recipients. |
| `Bcc` | `List<EmailContact>` | `[]` | BCC recipients. |
| `ReceivedAt` | `DateTime` | — | UTC timestamp when the message was received. |
| `IsRead` | `bool` | `false` | Whether the message has been read. |
| `IsStarred` | `bool` | `false` | Whether the message is starred/flagged. |
| `HasAttachments` | `bool` | `false` | Whether the message has attachments. |
| `FolderName` | `string` | `""` | Display name of the folder/label. |
| `FolderId` | `string` | `""` | Provider-specific folder identifier. |
| `ThreadId` | `string` | `""` | Conversation/thread identifier. |
| `SourceProvider` | `string` | `""` | Provider identifier: `"google"` or `"microsoft"`. |
| `AttachmentNames` | `List<string>` | `[]` | Names of attached files. |
| `WebLink` | `string?` | `null` | Link to view the message in the provider's web UI. |

#### EmailContact

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DisplayName` | `string` | `""` | Contact display name. |
| `EmailAddress` | `string` | `""` | Contact email address. |
| `IsMe` | `bool` | `false` | Whether this contact is the authenticated user. |

#### EmailFolderInfo

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `string` | `""` | Provider-specific folder/label identifier. |
| `Name` | `string` | `""` | Display name of the folder. |
| `TotalCount` | `int` | `0` | Total number of messages in the folder. |
| `UnreadCount` | `int` | `0` | Number of unread messages. |
| `SourceProvider` | `string` | `""` | Provider identifier: `"google"` or `"microsoft"`. |

#### EmailSyncSettings

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EnabledFolders` | `Dictionary<string, bool>` | `{ ["INBOX"] = true }` | Which folders to sync. Key = folder ID, Value = enabled. |
| `SyncIntervalMinutes` | `int` | `10` | Polling interval in minutes. |
| `MaxMessagesPerSync` | `int` | `50` | Maximum messages to fetch per sync cycle. |
| `SyncDaysBack` | `int` | `30` | How many days back to sync on first connection. |
| `EnableAiCategorization` | `bool` | `true` | Whether to use AI to categorize emails during triage. |
| `CategorizationPrompt` | `string?` | `null` | Custom prompt for AI categorization (null = default prompt). |
| `IncludeHtmlBody` | `bool` | `false` | Whether to include full HTML body in indexed content. |
| `IncludeAttachmentNames` | `bool` | `true` | Whether to include attachment names in indexed content. |

---

## 15. Plugin Infrastructure

### IPluginContext

```csharp
namespace AgentX.Core.Services.Plugins;

public interface IPluginContext
```

Provides a plugin with controlled, safe access to host application resources. An instance of this interface is created per plugin by `IPluginService` and passed to `IPlugin.InitializeAsync` before activation.

**Namespace:** `AgentX.Core.Services.Plugins`
**Assembly:** `AgentX.Core`

**Design Rationale:** Plugins must never receive the root `IServiceProvider` directly. Instead, `Services` is a dedicated child scope containing only safe services. File-system access is constrained to `PluginDataPath`.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Services` | `IServiceProvider` | Scoped service provider exposing only host-approved services. `IOAuthService` is available for `DataConnector` plugins. |
| `PluginDataPath` | `string` | Absolute path to a per-plugin data directory for reading/writing private data (config, caches, state). Created by the host before `InitializeAsync`. |
| `Logger` | `ILogger` | Serilog logger pre-enriched with plugin identifier and version via `ForContext`. |

---

### PluginType

```csharp
namespace AgentX.Core.Services.Plugins;

public enum PluginType
```

Defines the type of plugin, which determines its capabilities and what host services it can access.

| Value | Name | Description |
|-------|------|-------------|
| `0` | `DataConnector` | Plugin that connects to external data sources (Calendar, Email) and syncs data into the knowledge vault. Has access to `IOAuthService` for OAuth2 authentication. |

---

### OAuthCredentialEntity

```csharp
namespace AgentX.Core.Data.Entities;

public class OAuthCredentialEntity
```

Persists OAuth2 tokens for external providers (Google, Microsoft) in the SQLite database. Tokens are stored in DPAPI-encrypted form; the host decrypts them at runtime before passing them to provider clients. One row per provider; `ProviderId` is enforced unique.

**Namespace:** `AgentX.Core.Data.Entities`
**Assembly:** `AgentX.Core`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `long` | *(auto)* | Primary key (auto-increment). |
| `ProviderId` | `string` | `""` | Stable OAuth provider identifier (e.g. `"google"`, `"microsoft"`). Unique index. |
| `AccessToken` | `string` | `""` | DPAPI-encrypted access token. Base64-encoded encrypted blob. |
| `RefreshToken` | `string` | `""` | DPAPI-encrypted refresh token. Base64-encoded encrypted blob. |
| `TokenExpiry` | `DateTime` | — | UTC timestamp when the access token expires. |
| `Scopes` | `string` | `""` | Comma-separated OAuth scopes granted (e.g. `"calendar.read,email.read"`). |
| `UserId` | `string` | `""` | Provider-specific user identifier (Google `sub` or Microsoft `oid`). |
| `CreatedAt` | `DateTime` | — | UTC timestamp when this credential was first stored. |
| `UpdatedAt` | `DateTime` | — | UTC timestamp when this credential was last refreshed/updated. |

---

*This document was generated from the Agent-X source code in `AgentX.Core` and reflects the public API surface as of 2026-04-16.*
