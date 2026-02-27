# Agent-X Service Reference

Complete API reference for all public services in the Agent-X platform. This document covers every service interface, method signature, parameter, return type, and implementation detail necessary for integration and development.

## Table of Contents

1. [AI Services](#ai-services)
2. [Chat Services](#chat-services)
3. [Document Services](#document-services)
4. [Search & RAG Services](#search--rag-services)
5. [Indexing Services](#indexing-services)
6. [Collection & Tagging Services](#collection--tagging-services)
7. [Intelligence Services](#intelligence-services)
8. [Settings & License Services](#settings--license-services)
9. [Vector Database](#vector-database)
10. [Models & Data Structures](#models--data-structures)

---

## AI Services

### IAiService

**Namespace**: `AgentX.Core.AI`

High-level orchestration service for AI operations. Wraps the active IAiProvider and provides application-specific capabilities (summarization, tagging).

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ActiveProvider` | `IAiProvider` | The currently active AI provider instance |
| `IsConnected` | `bool` | Whether the active provider is connected and operational |
| `ActiveModelId` | `string` | The model identifier currently selected for inference |

#### Methods

##### InitializeAsync

```csharp
Task InitializeAsync(CancellationToken ct = default)
```

Initializes the AI service by creating providers and establishing the initial connection based on persisted settings.

**Parameters**:
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

**Throws**: May throw exceptions if provider initialization fails

**Example**:
```csharp
var aiService = serviceProvider.GetRequiredService<IAiService>();
await aiService.InitializeAsync();
```

##### SwitchProviderAsync

```csharp
Task<bool> SwitchProviderAsync(string providerId, CancellationToken ct = default)
```

Switches the active provider to the one identified by the providerId.

**Parameters**:
- `providerId` (string): The provider identifier (e.g., "ollama", "openai", "anthropic")
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<bool>` — True if the switch succeeded and the new provider is connected

**Example**:
```csharp
bool success = await aiService.SwitchProviderAsync("openai");
if (success)
{
    // New provider is connected and ready
}
```

##### SetActiveModelAsync

```csharp
Task SetActiveModelAsync(string modelId, CancellationToken ct = default)
```

Sets the active model for subsequent inference operations and persists the choice to settings.

**Parameters**:
- `modelId` (string): The model identifier to activate
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

**Example**:
```csharp
await aiService.SetActiveModelAsync("llama3.2:latest");
```

##### StreamChatAsync

```csharp
IAsyncEnumerable<string> StreamChatAsync(
    IReadOnlyList<ChatMessage> messages,
    string? systemPrompt = null,
    ChatOptions? options = null,
    CancellationToken ct = default)
```

Streams a chat completion token-by-token. Optionally prepends a system prompt to the conversation history.

**Parameters**:
- `messages` (IReadOnlyList<ChatMessage>): The conversation message history
- `systemPrompt` (string, optional): System prompt to prepend to the conversation
- `options` (ChatOptions, optional): Inference parameters (temperature, max tokens, etc.)
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `IAsyncEnumerable<string>` — An async enumerable of generated text tokens

**Example**:
```csharp
var messages = new List<ChatMessage>
{
    new() { Role = "user", Content = "What is AI?" }
};

await foreach (var token in aiService.StreamChatAsync(messages))
{
    Console.Write(token);
}
```

##### ChatAsync

```csharp
Task<string> ChatAsync(
    IReadOnlyList<ChatMessage> messages,
    string? systemPrompt = null,
    ChatOptions? options = null,
    CancellationToken ct = default)
```

Generates a complete chat response.

**Parameters**:
- `messages` (IReadOnlyList<ChatMessage>): The conversation message history
- `systemPrompt` (string, optional): System prompt to prepend
- `options` (ChatOptions, optional): Inference parameters
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<string>` — The full generated response text

**Example**:
```csharp
var response = await aiService.ChatAsync(messages, systemPrompt: "You are a helpful assistant.");
```

##### SummarizeAsync

```csharp
Task<string> SummarizeAsync(string content, CancellationToken ct = default)
```

Generates a concise summary of the provided content using the active model.

**Parameters**:
- `content` (string): The text content to summarize
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<string>` — A summary of the content

**Example**:
```csharp
var summary = await aiService.SummarizeAsync(longDocument);
```

##### GenerateTagsAsync

```csharp
Task<IReadOnlyList<string>> GenerateTagsAsync(
    string content,
    int maxTags = 5,
    CancellationToken ct = default)
```

Generates descriptive tags for the provided content.

**Parameters**:
- `content` (string): The text content to generate tags for
- `maxTags` (int): Maximum number of tags to generate (default: 5)
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<IReadOnlyList<string>>` — A list of generated tags

**Example**:
```csharp
var tags = await aiService.GenerateTagsAsync("Machine learning is a subset of AI...", maxTags: 3);
```

---

### IAiProvider

**Namespace**: `AgentX.Core.AI`

Low-level abstraction over AI inference providers (Ollama, OpenAI, Anthropic). Each provider implementation wraps a specific backend and exposes a unified interface.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ProviderId` | `string` | Unique identifier for this provider (e.g., "ollama", "openai") |
| `DisplayName` | `string` | Human-readable display name (e.g., "Ollama", "OpenAI") |
| `IsAvailable` | `bool` | Whether the provider is currently reachable and operational |

#### Methods

##### CheckConnectionAsync

```csharp
Task<bool> CheckConnectionAsync(CancellationToken ct = default)
```

Tests the connection to the AI provider backend.

**Returns**: `Task<bool>` — True if the provider is reachable and operational

##### ListModelsAsync

```csharp
Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
```

Lists all models currently available (installed) on this provider.

**Returns**: `Task<IReadOnlyList<AiModel>>` — List of available models

##### PullModelAsync

```csharp
Task PullModelAsync(
    string modelName,
    IProgress<ModelDownloadProgress>? progress = null,
    CancellationToken ct = default)
```

Downloads/pulls a model from the provider's model registry.

**Parameters**:
- `modelName` (string): The name/tag of the model to pull (e.g., "llama3.2:latest")
- `progress` (IProgress<ModelDownloadProgress>, optional): Progress reporter for download status updates
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

##### DeleteModelAsync

```csharp
Task DeleteModelAsync(string modelName, CancellationToken ct = default)
```

Deletes a locally installed model from the provider.

**Parameters**:
- `modelName` (string): The name/tag of the model to delete
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

##### StreamChatAsync

```csharp
IAsyncEnumerable<string> StreamChatAsync(
    IReadOnlyList<ChatMessage> messages,
    ChatOptions? options = null,
    CancellationToken ct = default)
```

Streams a chat completion token-by-token.

**Parameters**:
- `messages` (IReadOnlyList<ChatMessage>): The conversation message history
- `options` (ChatOptions, optional): Inference parameters
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `IAsyncEnumerable<string>` — An async enumerable of generated tokens

##### ChatAsync

```csharp
Task<string> ChatAsync(
    IReadOnlyList<ChatMessage> messages,
    ChatOptions? options = null,
    CancellationToken ct = default)
```

Generates a complete chat response.

**Returns**: `Task<string>` — The full generated response text

##### GenerateEmbeddingAsync

```csharp
Task<float[]> GenerateEmbeddingAsync(
    string text,
    string modelName,
    CancellationToken ct = default)
```

Generates a vector embedding for a single text input.

**Parameters**:
- `text` (string): The text to embed
- `modelName` (string): The embedding model to use
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<float[]>` — The embedding vector as a float array

##### GenerateEmbeddingsAsync

```csharp
Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
    IReadOnlyList<string> texts,
    string modelName,
    CancellationToken ct = default)
```

Generates vector embeddings for multiple text inputs in a batch.

**Parameters**:
- `texts` (IReadOnlyList<string>): The texts to embed
- `modelName` (string): The embedding model to use
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<IReadOnlyList<float[]>>` — A list of embedding vectors

#### Implementations

- **OllamaProvider**: Wraps Ollama local inference engine
- **OpenAiProvider**: Wraps OpenAI API (GPT models)
- **AnthropicProvider**: Wraps Anthropic API (Claude models)

---

### IModelManager

**Namespace**: `AgentX.Core.AI`

Manages locally available AI models — listing, downloading, deleting, and querying model information.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ModelListChanged` | `EventHandler<AiModel>` | Raised when the local model list changes (after pull or delete) |

#### Methods

##### GetAvailableModelsAsync

```csharp
Task<IReadOnlyList<AiModel>> GetAvailableModelsAsync(CancellationToken ct = default)
```

Gets all models available from the remote registry for the active provider.

**Returns**: `Task<IReadOnlyList<AiModel>>` — List of available models

##### GetInstalledModelsAsync

```csharp
Task<IReadOnlyList<AiModel>> GetInstalledModelsAsync(CancellationToken ct = default)
```

Gets all models currently installed on the local system.

**Returns**: `Task<IReadOnlyList<AiModel>>` — List of installed models

##### PullModelAsync

```csharp
Task PullModelAsync(
    string modelName,
    IProgress<ModelDownloadProgress>? progress = null,
    CancellationToken ct = default)
```

Downloads/pulls a model from the provider's registry.

**Parameters**:
- `modelName` (string): The model name/tag (e.g., "llama3.2:latest")
- `progress` (IProgress<ModelDownloadProgress>, optional): Progress reporter
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

##### DeleteModelAsync

```csharp
Task DeleteModelAsync(string modelName, CancellationToken ct = default)
```

Deletes a locally installed model.

**Parameters**:
- `modelName` (string): The model name/tag to delete
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

##### GetModelInfoAsync

```csharp
Task<AiModel?> GetModelInfoAsync(string modelName, CancellationToken ct = default)
```

Retrieves detailed information for a specific model by name.

**Parameters**:
- `modelName` (string): The model name/tag
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<AiModel?>` — The model info, or null if not found

##### IsModelAvailableAsync

```csharp
Task<bool> IsModelAvailableAsync(string modelName, CancellationToken ct = default)
```

Checks whether a specific model is currently installed and available locally.

**Parameters**:
- `modelName` (string): The model name/tag
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<bool>` — True if the model is installed locally

---

### IEmbeddingService

**Namespace**: `AgentX.Core.AI`

Generates vector embeddings from text content using a local embedding model.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Dimensions` | `int` | The dimensionality of generated embeddings |
| `ModelName` | `string` | The name of the embedding model being used |

#### Methods

##### EmbedAsync

```csharp
Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
```

Generates an embedding for a single text input.

**Parameters**:
- `text` (string): The text to embed
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<float[]>` — The embedding vector

##### EmbedBatchAsync

```csharp
Task<IReadOnlyList<float[]>> EmbedBatchAsync(
    IEnumerable<string> texts,
    CancellationToken ct = default)
```

Generates embeddings for multiple text inputs in a batch.

**Parameters**:
- `texts` (IEnumerable<string>): The texts to embed
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<IReadOnlyList<float[]>>` — List of embedding vectors

---

### IHardwareDetector

**Namespace**: `AgentX.Core.AI`

Detects hardware capabilities to recommend appropriate models and settings.

#### Methods

##### DetectAsync

```csharp
Task<HardwareCapability> DetectAsync()
```

Detects and returns hardware capabilities.

**Returns**: `Task<HardwareCapability>` — Hardware capability information

---

### IContextWindowManager

**Namespace**: `AgentX.Core.AI`

Manages token counting and context window fitting for chat messages.

#### Methods

##### FitMessagesToContext

Fits messages to the available context window by removing older messages if necessary.

##### CalculateAvailableTokens

Calculates the number of tokens available for a response given the input messages.

---

### IRetryPolicy

**Namespace**: `AgentX.Core.AI`

Defines retry behavior for transient AI service failures.

#### Methods

##### ExecuteAsync

```csharp
Task<T> ExecuteAsync<T>(
    Func<Task<T>> operation,
    int maxRetries,
    CancellationToken ct)
```

Executes an operation with exponential backoff retry logic.

**Type Parameters**:
- `T`: The return type of the operation

**Parameters**:
- `operation` (Func<Task<T>>): The async operation to execute
- `maxRetries` (int): Maximum number of retry attempts
- `ct` (CancellationToken): Cancellation token

**Returns**: `Task<T>` — The result of the operation

---

## Chat Services

### IChatService

**Namespace**: `AgentX.Core.Services.Chat`

Orchestrates AI chat operations: sends messages, streams responses, manages generation state, and coordinates persistence.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsGenerating` | `bool` | Whether an AI response is currently being generated |

#### Events

| Event | Parameters | Description |
|-------|-----------|-------------|
| `GenerationStateChanged` | `bool` | Fires when IsGenerating changes |

#### Methods

##### SendMessageAsync

```csharp
IAsyncEnumerable<string> SendMessageAsync(
    long conversationId,
    string userMessage,
    CancellationToken ct = default)
```

Sends a user message and streams the assistant response token-by-token.

**Parameters**:
- `conversationId` (long): The conversation to send the message in
- `userMessage` (string): The user's message content
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `IAsyncEnumerable<string>` — Response tokens as they arrive

**Example**:
```csharp
await foreach (var token in chatService.SendMessageAsync(conversationId, "Hello!"))
{
    Console.Write(token);
}
```

##### SendMessageAndWaitAsync

```csharp
Task<string> SendMessageAndWaitAsync(
    long conversationId,
    string userMessage,
    CancellationToken ct = default)
```

Sends a user message and waits for the complete assistant response.

**Parameters**:
- `conversationId` (long): The conversation to send the message in
- `userMessage` (string): The user's message content
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<string>` — The complete assistant response

**Example**:
```csharp
var response = await chatService.SendMessageAndWaitAsync(conversationId, "Hello!");
```

##### RegenerateLastResponseAsync

```csharp
Task RegenerateLastResponseAsync(
    long conversationId,
    CancellationToken ct = default)
```

Deletes the last assistant message and re-sends the last user message to generate a new response.

**Parameters**:
- `conversationId` (long): The conversation to regenerate in
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

##### StopGenerationAsync

```csharp
Task StopGenerationAsync()
```

Cancels any in-progress generation.

**Returns**: Task

---

### IConversationService

**Namespace**: `AgentX.Core.Services.Chat`

Manages conversation and message persistence via EF Core.

#### Methods

##### CreateConversationAsync

```csharp
Task<ConversationEntity> CreateConversationAsync(
    string? title = null,
    string? systemPrompt = null,
    string? modelId = null)
```

Creates a new conversation.

**Parameters**:
- `title` (string, optional): Conversation title
- `systemPrompt` (string, optional): System prompt for the conversation
- `modelId` (string, optional): Model ID to use for this conversation

**Returns**: `Task<ConversationEntity>` — The created conversation

##### GetConversationAsync

```csharp
Task<ConversationEntity?> GetConversationAsync(long conversationId)
```

Retrieves a conversation by ID, including its messages.

**Parameters**:
- `conversationId` (long): The conversation ID

**Returns**: `Task<ConversationEntity?>` — The conversation or null if not found

##### GetAllConversationsAsync

```csharp
Task<IReadOnlyList<ConversationEntity>> GetAllConversationsAsync(bool includeArchived = false)
```

Returns all conversations ordered by UpdatedAt descending.

**Parameters**:
- `includeArchived` (bool): Whether to include archived conversations (default: false)

**Returns**: `Task<IReadOnlyList<ConversationEntity>>` — All conversations

##### SearchConversationsAsync

```csharp
Task<IReadOnlyList<ConversationEntity>> SearchConversationsAsync(string query)
```

Searches conversations by title or message content.

**Parameters**:
- `query` (string): Search query text

**Returns**: `Task<IReadOnlyList<ConversationEntity>>` — Matching conversations

##### UpdateConversationTitleAsync

```csharp
Task UpdateConversationTitleAsync(long conversationId, string title)
```

Updates the title of an existing conversation.

**Parameters**:
- `conversationId` (long): The conversation ID
- `title` (string): The new title

**Returns**: Task

##### TogglePinAsync

```csharp
Task TogglePinAsync(long conversationId)
```

Toggles the pinned state of a conversation.

**Parameters**:
- `conversationId` (long): The conversation ID

**Returns**: Task

##### ArchiveConversationAsync

```csharp
Task ArchiveConversationAsync(long conversationId)
```

Archives a conversation, hiding it from the default list.

**Parameters**:
- `conversationId` (long): The conversation ID

**Returns**: Task

##### DeleteConversationAsync

```csharp
Task DeleteConversationAsync(long conversationId)
```

Permanently deletes a conversation and all its messages.

**Parameters**:
- `conversationId` (long): The conversation ID

**Returns**: Task

##### GetMessagesAsync

```csharp
Task<IReadOnlyList<MessageEntity>> GetMessagesAsync(long conversationId)
```

Returns all messages for a conversation, ordered by SortOrder ascending.

**Parameters**:
- `conversationId` (long): The conversation ID

**Returns**: `Task<IReadOnlyList<MessageEntity>>` — All messages

##### AddMessageAsync

```csharp
Task AddMessageAsync(
    long conversationId,
    string role,
    string content,
    int? tokenCount = null,
    double? generationTimeMs = null)
```

Adds a new message to a conversation and updates conversation metadata.

**Parameters**:
- `conversationId` (long): The target conversation
- `role` (string): Message role: "user", "assistant", or "system"
- `content` (string): The message content
- `tokenCount` (int, optional): Estimated token count
- `generationTimeMs` (double, optional): Generation time in milliseconds (for assistant messages)

**Returns**: Task

##### DeleteLastAssistantMessageAsync

```csharp
Task DeleteLastAssistantMessageAsync(long conversationId)
```

Removes the most recent assistant message from a conversation.

**Parameters**:
- `conversationId` (long): The conversation ID

**Returns**: Task

##### GetConversationCountAsync

```csharp
Task<int> GetConversationCountAsync()
```

Returns the count of non-archived conversations.

**Returns**: `Task<int>` — Number of conversations

##### GetTotalTokensUsedAsync

```csharp
Task<long> GetTotalTokensUsedAsync()
```

Returns the sum of TokensUsed across all conversations.

**Returns**: `Task<long>` — Total tokens used

---

### ISystemPromptService

**Namespace**: `AgentX.Core.Services.Chat`

Manages system prompt templates with CRUD operations, favorites, and usage tracking.

#### Methods

##### GetAllPromptsAsync

```csharp
Task<IReadOnlyList<SystemPromptEntity>> GetAllPromptsAsync(string? category = null)
```

Returns all prompts, optionally filtered by category. Ordered by IsFavorite descending, then UsageCount descending.

**Parameters**:
- `category` (string, optional): Category to filter by

**Returns**: `Task<IReadOnlyList<SystemPromptEntity>>` — All prompts

##### GetPromptAsync

```csharp
Task<SystemPromptEntity?> GetPromptAsync(long id)
```

Retrieves a single prompt by ID.

**Parameters**:
- `id` (long): The prompt ID

**Returns**: `Task<SystemPromptEntity?>` — The prompt or null if not found

##### CreatePromptAsync

```csharp
Task<SystemPromptEntity> CreatePromptAsync(string name, string content, string category)
```

Creates a new user-defined prompt.

**Parameters**:
- `name` (string): The prompt name
- `content` (string): The prompt content
- `category` (string): The category

**Returns**: `Task<SystemPromptEntity>` — The created prompt

##### UpdatePromptAsync

```csharp
Task UpdatePromptAsync(long id, string name, string content, string category)
```

Updates an existing prompt.

**Parameters**:
- `id` (long): The prompt ID
- `name` (string): The new name
- `content` (string): The new content
- `category` (string): The new category

**Returns**: Task

##### DeletePromptAsync

```csharp
Task DeletePromptAsync(long id)
```

Deletes a prompt. Built-in prompts cannot be deleted.

**Parameters**:
- `id` (long): The prompt ID

**Returns**: Task

##### ToggleFavoriteAsync

```csharp
Task ToggleFavoriteAsync(long id)
```

Toggles the favorite status of a prompt.

**Parameters**:
- `id` (long): The prompt ID

**Returns**: Task

##### IncrementUsageAsync

```csharp
Task IncrementUsageAsync(long id)
```

Increments the usage counter for a prompt.

**Parameters**:
- `id` (long): The prompt ID

**Returns**: Task

##### SeedBuiltInPromptsAsync

```csharp
Task SeedBuiltInPromptsAsync()
```

Seeds the database with built-in prompts if they do not already exist. Should be called once during application startup.

**Returns**: Task

---

### IConversationMemoryService

**Namespace**: `AgentX.Core.Services.Chat`

Extracts and manages conversation memories for contextual recall and suggestions.

#### Methods

##### ExtractMemoriesAsync

Extracts key information from conversation messages to create persistent memories.

##### GetMemoryContextAsync

Retrieves relevant memories for the current conversation context.

##### GetSuggestedQuestionsAsync

Generates suggested follow-up questions based on conversation memories.

##### GetAllMemoriesAsync

Retrieves all extracted memories.

##### DismissMemoryAsync

Marks a memory as dismissed by the user.

##### GetMemoryCountAsync

Returns the total count of extracted memories.

---

## Document Services

### IDocumentService

**Namespace**: `AgentX.Core.Documents`

Orchestrates the document import pipeline: file validation, text extraction, metadata capture, and DB record creation.

#### Methods

##### ImportFileAsync

```csharp
Task<DocumentEntity> ImportFileAsync(
    string filePath,
    long? collectionId = null,
    CancellationToken ct = default)
```

Imports a single file: validates, hashes, extracts text, creates DocumentEntity.

**Parameters**:
- `filePath` (string): Absolute path to the file
- `collectionId` (long, optional): Collection to associate the document with
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<DocumentEntity>` — The created document with status "pending"

**Example**:
```csharp
var doc = await documentService.ImportFileAsync("/path/to/document.pdf");
```

##### ImportFilesAsync

```csharp
Task<IReadOnlyList<DocumentEntity>> ImportFilesAsync(
    IReadOnlyList<string> filePaths,
    long? collectionId = null,
    IProgress<int>? progress = null,
    CancellationToken ct = default)
```

Imports multiple files, reporting progress as each file completes.

**Parameters**:
- `filePaths` (IReadOnlyList<string>): Absolute paths to the files
- `collectionId` (long, optional): Collection to associate all documents with
- `progress` (IProgress<int>, optional): Progress reporter (files completed)
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<IReadOnlyList<DocumentEntity>>` — The created documents

**Example**:
```csharp
var docs = await documentService.ImportFilesAsync(
    new[] { "/path/1.pdf", "/path/2.docx" },
    progress: new Progress<int>(completed => Console.WriteLine($"{completed} files imported"))
);
```

##### GetDocumentAsync

```csharp
Task<DocumentEntity?> GetDocumentAsync(long documentId)
```

Retrieves a single document by its primary key.

**Parameters**:
- `documentId` (long): The document ID

**Returns**: `Task<DocumentEntity?>` — The document or null if not found

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
    CancellationToken ct = default)
```

Retrieves all documents with optional filtering. Results are ordered by ImportedAt descending (newest first).

**Parameters**:
- `fileTypeFilter` (string, optional): File type to filter by (e.g., "pdf")
- `statusFilter` (string, optional): Indexing status (e.g., "completed", "pending")
- `tagFilter` (string, optional): Tag name to filter documents that have this tag
- `collectionId` (long, optional): Collection ID to filter by
- `importedAfter` (DateTime, optional): Lower bound (inclusive) for ImportedAt
- `importedBefore` (DateTime, optional): Upper bound (inclusive) for ImportedAt
- `sortBy` (string, optional): Sort field: "name", "date" (default), "size", or "type"
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<IReadOnlyList<DocumentEntity>>` — All matching documents

**Example**:
```csharp
var pdfDocs = await documentService.GetAllDocumentsAsync(fileTypeFilter: "pdf");
var recentDocs = await documentService.GetAllDocumentsAsync(
    importedAfter: DateTime.UtcNow.AddDays(-7)
);
```

##### GetDocumentsByCollectionAsync

```csharp
Task<IReadOnlyList<DocumentEntity>> GetDocumentsByCollectionAsync(long collectionId)
```

Retrieves all documents belonging to a specific collection.

**Parameters**:
- `collectionId` (long): The collection ID

**Returns**: `Task<IReadOnlyList<DocumentEntity>>` — All documents in the collection

##### DeleteDocumentAsync

```csharp
Task DeleteDocumentAsync(long documentId)
```

Deletes a document, its chunks, and any associated vector embeddings.

**Parameters**:
- `documentId` (long): The document ID

**Returns**: Task

##### ReindexDocumentAsync

```csharp
Task ReindexDocumentAsync(long documentId, CancellationToken ct = default)
```

Re-processes a document by deleting existing chunks and re-extracting text. Resets status to "pending" for re-indexing.

**Parameters**:
- `documentId` (long): The document ID
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

##### GetDocumentByHashAsync

```csharp
Task<DocumentEntity?> GetDocumentByHashAsync(string contentHash)
```

Looks up a document by its SHA-256 content hash (for duplicate detection).

**Parameters**:
- `contentHash` (string): The SHA-256 content hash

**Returns**: `Task<DocumentEntity?>` — The document or null if not found

##### GetTotalDocumentCountAsync

```csharp
Task<long> GetTotalDocumentCountAsync()
```

Returns the total number of documents in the knowledge vault.

**Returns**: `Task<long>` — The document count

##### GetTotalStorageBytesAsync

```csharp
Task<long> GetTotalStorageBytesAsync()
```

Returns the total storage consumed by all imported documents in bytes.

**Returns**: `Task<long>` — Total storage in bytes

##### GetFileTypeDistributionAsync

```csharp
Task<Dictionary<string, int>> GetFileTypeDistributionAsync()
```

Returns a distribution of file types and their counts.

**Returns**: `Task<Dictionary<string, int>>` — File type counts (e.g., {"pdf": 12, "docx": 5})

##### CanProcess

```csharp
bool CanProcess(string filePath)
```

Checks whether the given file can be processed by any registered document processor.

**Parameters**:
- `filePath` (string): The file path

**Returns**: `bool` — True if the file can be processed

##### GetSupportedExtensions

```csharp
IReadOnlySet<string> GetSupportedExtensions()
```

Returns the union of all supported file extensions across all registered processors.

**Returns**: `IReadOnlySet<string>` — All supported extensions (e.g., ".pdf", ".docx", ".txt")

##### CheckForDuplicateAsync

```csharp
Task<DuplicateCheckResult> CheckForDuplicateAsync(
    string filePath,
    CancellationToken ct = default)
```

Checks an incoming file against the knowledge vault for duplicate content using SHA-256 hash comparison.

**Parameters**:
- `filePath` (string): Absolute path to the file to check
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<DuplicateCheckResult>` — Result indicating whether a duplicate exists

##### BulkDeleteAsync

```csharp
Task BulkDeleteAsync(IReadOnlyList<long> documentIds, CancellationToken ct = default)
```

Deletes multiple documents by their IDs. Failures for individual documents are logged but do not abort the batch.

**Parameters**:
- `documentIds` (IReadOnlyList<long>): The document IDs to delete
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

##### BulkReindexAsync

```csharp
Task BulkReindexAsync(IReadOnlyList<long> documentIds, CancellationToken ct = default)
```

Re-indexes multiple documents. Each is reset to "pending" status.

**Parameters**:
- `documentIds` (IReadOnlyList<long>): The document IDs to reindex
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

##### BulkAssignToCollectionAsync

```csharp
Task BulkAssignToCollectionAsync(
    IReadOnlyList<long> documentIds,
    long collectionId,
    CancellationToken ct = default)
```

Associates multiple documents with a collection.

**Parameters**:
- `documentIds` (IReadOnlyList<long>): The document IDs
- `collectionId` (long): The collection ID
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

---

### IDocumentProcessor

**Namespace**: `AgentX.Core.Documents`

Extracts text content from a specific file type. Each supported format gets its own processor.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `SupportedExtensions` | `IReadOnlySet<string>` | Extensions this processor can handle |

#### Methods

##### CanProcess

```csharp
bool CanProcess(string filePath)
```

Determines whether this processor can handle the given file.

**Parameters**:
- `filePath` (string): The file path

**Returns**: `bool` — True if this processor can handle the file

##### ProcessAsync

```csharp
Task<ProcessedDocument> ProcessAsync(string filePath, CancellationToken ct = default)
```

Processes a file and extracts text content.

**Parameters**:
- `filePath` (string): Absolute path to the file
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<ProcessedDocument>` — The processed document with extracted text and metadata

#### Implementations

- **PdfProcessor**: Extracts text from PDF files with page tracking
- **DocxProcessor**: Extracts text from Microsoft Word documents
- **TextProcessor**: Handles plain text files
- **MarkdownProcessor**: Extracts structured content from Markdown files
- **CodeFileProcessor**: Processes source code files (preserves syntax context)
- **ImageProcessor**: Extracts text from images via OCR

---

### IChunkingService

**Namespace**: `AgentX.Core.Documents`

Splits text content into overlapping chunks suitable for embedding generation. Uses recursive character text splitter strategy.

#### Methods

##### ChunkText

```csharp
IReadOnlyList<DocumentChunk> ChunkText(
    string text,
    int chunkSize = 512,
    int chunkOverlap = 50,
    string? sectionTitle = null,
    int? pageNumber = null)
```

Splits raw text into overlapping chunks with metadata.

**Parameters**:
- `text` (string): The text content to chunk
- `chunkSize` (int): Maximum number of tokens per chunk (default: 512)
- `chunkOverlap` (int): Number of overlapping tokens between chunks (default: 50)
- `sectionTitle` (string, optional): Section title to attach to all chunks
- `pageNumber` (int, optional): Page number to attach to all chunks

**Returns**: `IReadOnlyList<DocumentChunk>` — Ordered list of chunks

**Example**:
```csharp
var chunks = chunkingService.ChunkText(longText, chunkSize: 512, chunkOverlap: 50);
```

##### ChunkDocument

```csharp
IReadOnlyList<DocumentChunk> ChunkDocument(
    ProcessedDocument document,
    int chunkSize = 512,
    int chunkOverlap = 50)
```

Splits a processed document into overlapping chunks, respecting page boundaries.

**Parameters**:
- `document` (ProcessedDocument): The processed document
- `chunkSize` (int): Maximum tokens per chunk (default: 512)
- `chunkOverlap` (int): Number of overlapping tokens (default: 50)

**Returns**: `IReadOnlyList<DocumentChunk>` — Chunks covering the entire document

---

## Search & RAG Services

### ISemanticSearchService

**Namespace**: `AgentX.Core.Search`

Performs semantic (vector-based) search across indexed document chunks.

#### Methods

##### SearchAsync

```csharp
Task<IReadOnlyList<SearchResult>> SearchAsync(
    SearchQuery query,
    CancellationToken ct = default)
```

Performs a semantic search using the given query.

**Parameters**:
- `query` (SearchQuery): The search query with optional filters
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<IReadOnlyList<SearchResult>>` — Ordered list of results (highest relevance first)

**Example**:
```csharp
var results = await semanticSearch.SearchAsync(new SearchQuery
{
    QueryText = "What is machine learning?",
    TopK = 10,
    MinScore = 0.3f
});
```

##### SaveSearchHistoryAsync

```csharp
Task SaveSearchHistoryAsync(string queryText, int resultCount)
```

Saves a search query to the search history.

**Parameters**:
- `queryText` (string): The query text
- `resultCount` (int): Number of results returned

**Returns**: Task

##### GetSearchHistoryAsync

```csharp
Task<IReadOnlyList<SearchHistoryEntry>> GetSearchHistoryAsync(int limit = 20)
```

Retrieves recent search history entries.

**Parameters**:
- `limit` (int): Maximum number of entries (default: 20)

**Returns**: `Task<IReadOnlyList<SearchHistoryEntry>>` — Recent searches

##### ClearSearchHistoryAsync

```csharp
Task ClearSearchHistoryAsync()
```

Clears all search history.

**Returns**: Task

---

### IKeywordSearchService

**Namespace**: `AgentX.Core.Search`

Full-text keyword search using SQLite FTS5 with BM25 ranking.

#### Methods

##### InitializeFtsAsync

Initializes the FTS5 index for keyword search.

##### IndexDocumentChunksAsync

Indexes document chunks for full-text search.

##### RemoveDocumentFromFtsAsync

Removes a document's chunks from the FTS index.

##### SearchAsync

Performs a keyword search and returns ranked results.

##### RebuildFtsIndexAsync

Rebuilds the FTS index from scratch.

---

### IHybridSearchOrchestrator

**Namespace**: `AgentX.Core.Search`

Routes queries by SearchMode and combines results via Reciprocal Rank Fusion for hybrid search.

#### Methods

Routes search queries to semantic, keyword, or hybrid engines based on SearchMode.

---

### IRagPipeline

**Namespace**: `AgentX.Core.Search`

Orchestrates the Retrieval-Augmented Generation pipeline: embeds questions, retrieves context, builds grounded prompts, streams responses, and extracts citations.

#### Methods

##### AskAsync

```csharp
Task<RagResponse> AskAsync(
    string question,
    long? collectionId = null,
    Action<string>? onToken = null,
    CancellationToken ct = default)
```

Executes the full RAG pipeline: search for context, build prompt, stream response.

**Parameters**:
- `question` (string): The user's natural language question
- `collectionId` (long, optional): Collection scope (null = search all)
- `onToken` (Action<string>, optional): Callback invoked for each streamed token
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<RagResponse>` — Complete RAG response with citations

**Example**:
```csharp
var response = await ragPipeline.AskAsync(
    "What are the main benefits of AI?",
    onToken: token => Console.Write(token)
);
Console.WriteLine($"Used {response.ContextChunksUsed} context chunks");
foreach (var citation in response.Citations)
{
    Console.WriteLine($"[{citation.Number}] {citation.FileName}");
}
```

##### GetIndexedChunkCountAsync

```csharp
Task<long> GetIndexedChunkCountAsync(CancellationToken ct = default)
```

Gets the number of indexed chunks available for RAG queries.

**Parameters**:
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<long>` — Number of indexed chunks

---

### ICitationService

**Namespace**: `AgentX.Core.Search`

Extracts and validates citations from RAG responses.

---

### IRagReranker

**Namespace**: `AgentX.Core.Search`

Reranks search results by relevance to improve RAG response quality.

---

## Indexing Services

### IIndexingService

**Namespace**: `AgentX.Core.Services.Indexing`

Manages the background indexing pipeline: processes pending documents by chunking, generating embeddings, and storing vectors.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsProcessing` | `bool` | Whether the service is currently processing a document |

#### Events

| Event | Parameters | Description |
|-------|-----------|-------------|
| `ProgressChanged` | `IndexingProgressEventArgs` | Raised when queue state changes |
| `DocumentIndexed` | `long` | Raised when a document is successfully indexed (document ID) |

#### Methods

##### InitializeAsync

```csharp
Task InitializeAsync(CancellationToken ct = default)
```

Initializes the indexing service: sets up the vector store and starts the background processing loop.

**Parameters**:
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

##### IndexDocumentAsync

```csharp
Task IndexDocumentAsync(long documentId, CancellationToken ct = default)
```

Indexes a single document: re-processes the file, chunks the text, generates embeddings, stores vectors.

**Parameters**:
- `documentId` (long): The ID of the document to index
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

**Example**:
```csharp
await indexingService.IndexDocumentAsync(documentId);
```

##### ReindexAllAsync

```csharp
Task ReindexAllAsync(
    IProgress<(int Processed, int Total)>? progress = null,
    CancellationToken ct = default)
```

Re-indexes all completed documents. Useful after changing chunking or embedding settings.

**Parameters**:
- `progress` (IProgress<(int, int)>, optional): Progress reporter with (processed, total) counts
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

**Example**:
```csharp
await indexingService.ReindexAllAsync(
    progress: new Progress<(int, int)>(p => Console.WriteLine($"{p.Item1}/{p.Item2} reindexed"))
);
```

##### GetQueueLengthAsync

```csharp
Task<int> GetQueueLengthAsync()
```

Returns the number of documents currently waiting in the indexing queue.

**Returns**: `Task<int>` — Queue length

##### GetProcessedCountAsync

```csharp
Task<int> GetProcessedCountAsync()
```

Returns the total number of documents that have been successfully indexed.

**Returns**: `Task<int>` — Number of processed documents

---

### IIndexingQueueService

**Namespace**: `AgentX.Core.Services.Indexing`

Thread-safe FIFO queue management for indexing jobs.

---

### IFileWatcherService

**Namespace**: `AgentX.Core.Services.Indexing`

Monitors registered watch folders for new or modified files and automatically imports them into the knowledge vault.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsWatching` | `bool` | Whether any watch folders are currently being monitored |

#### Events

| Event | Parameters | Description |
|-------|-----------|-------------|
| `FileDetected` | `string` | Raised when a new or modified file is detected (full file path) |

#### Methods

##### StartWatchingAsync

```csharp
Task StartWatchingAsync(CancellationToken ct = default)
```

Loads all enabled watch folders from the database and starts a FileSystemWatcher for each one.

**Parameters**:
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

##### StopWatchingAsync

```csharp
Task StopWatchingAsync()
```

Stops all active file system watchers. Watch folder configuration is preserved in the database.

**Returns**: Task

##### AddWatchFolderAsync

```csharp
Task AddWatchFolderAsync(
    string path,
    bool includeSubfolders = true,
    string? fileTypeFilter = null,
    long? collectionId = null)
```

Registers a new watch folder and starts watching immediately.

**Parameters**:
- `path` (string): Absolute path to the folder to watch
- `includeSubfolders` (bool): Whether to recursively monitor subdirectories (default: true)
- `fileTypeFilter` (string, optional): Comma-separated extensions (e.g., "pdf,docx,txt"). Null = all supported
- `collectionId` (long, optional): Collection to associate imported documents with

**Returns**: Task

##### RemoveWatchFolderAsync

```csharp
Task RemoveWatchFolderAsync(long watchFolderId)
```

Stops watching a folder and deletes its database record.

**Parameters**:
- `watchFolderId` (long): The watch folder ID

**Returns**: Task

##### GetWatchFoldersAsync

```csharp
Task<IReadOnlyList<WatchFolderEntity>> GetWatchFoldersAsync()
```

Returns all registered watch folders from the database.

**Returns**: `Task<IReadOnlyList<WatchFolderEntity>>` — All watch folders

---

## Collection & Tagging Services

### ICollectionService

**Namespace**: `AgentX.Core.Services.Collections`

Manages document collections with hierarchical organization and document-collection associations.

#### Methods

##### CreateCollectionAsync

```csharp
Task<CollectionEntity> CreateCollectionAsync(
    string name,
    string? description = null,
    long? parentId = null)
```

Creates a new collection.

**Parameters**:
- `name` (string): The name of the collection (must not be empty)
- `description` (string, optional): Description of the collection
- `parentId` (long, optional): Parent collection ID for nesting

**Returns**: `Task<CollectionEntity>` — The created collection

##### GetAllCollectionsAsync

```csharp
Task<IReadOnlyList<CollectionEntity>> GetAllCollectionsAsync()
```

Retrieves all collections ordered by sort order then name, with child collections included.

**Returns**: `Task<IReadOnlyList<CollectionEntity>>` — All collections

##### GetRootCollectionsAsync

```csharp
Task<IReadOnlyList<CollectionEntity>> GetRootCollectionsAsync()
```

Retrieves only root-level collections (those without a parent).

**Returns**: `Task<IReadOnlyList<CollectionEntity>>` — Root collections

##### GetChildCollectionsAsync

```csharp
Task<IReadOnlyList<CollectionEntity>> GetChildCollectionsAsync(long parentId)
```

Retrieves the immediate child collections of the specified parent.

**Parameters**:
- `parentId` (long): The ID of the parent collection

**Returns**: `Task<IReadOnlyList<CollectionEntity>>` — Child collections

##### GetCollectionAsync

```csharp
Task<CollectionEntity?> GetCollectionAsync(long collectionId)
```

Retrieves a single collection by ID, including its document associations and child collections.

**Parameters**:
- `collectionId` (long): The collection ID

**Returns**: `Task<CollectionEntity?>` — The collection or null if not found

##### UpdateCollectionAsync

```csharp
Task UpdateCollectionAsync(long collectionId, string name, string? description = null)
```

Updates the name and description of an existing collection.

**Parameters**:
- `collectionId` (long): The collection ID
- `name` (string): The new name (must not be empty)
- `description` (string, optional): The new description

**Returns**: Task

##### DeleteCollectionAsync

```csharp
Task DeleteCollectionAsync(long collectionId, bool deleteDocuments = false)
```

Deletes a collection. Children are re-parented to the deleted collection's parent.

**Parameters**:
- `collectionId` (long): The collection ID
- `deleteDocuments` (bool): If true, cascade-deletes all documents in the collection. If false, only the collection is removed (default: false)

**Returns**: Task

##### AddDocumentToCollectionAsync

```csharp
Task AddDocumentToCollectionAsync(long documentId, long collectionId)
```

Associates a document with a collection.

**Parameters**:
- `documentId` (long): The document ID
- `collectionId` (long): The collection ID

**Returns**: Task

##### RemoveDocumentFromCollectionAsync

```csharp
Task RemoveDocumentFromCollectionAsync(long documentId, long collectionId)
```

Removes the association between a document and a collection.

**Parameters**:
- `documentId` (long): The document ID
- `collectionId` (long): The collection ID

**Returns**: Task

##### MoveCollectionAsync

```csharp
Task MoveCollectionAsync(long collectionId, long? newParentId)
```

Moves a collection to a new parent, or to root level if newParentId is null.

**Parameters**:
- `collectionId` (long): The collection ID
- `newParentId` (long, optional): The new parent collection ID, or null for root level

**Returns**: Task

##### GetCollectionCountAsync

```csharp
Task<int> GetCollectionCountAsync()
```

Returns the total number of collections in the database.

**Returns**: `Task<int>` — Collection count

##### GetDocumentsInCollectionAsync

```csharp
Task<IReadOnlyList<DocumentEntity>> GetDocumentsInCollectionAsync(long collectionId)
```

Retrieves all documents belonging to a specific collection.

**Parameters**:
- `collectionId` (long): The collection ID

**Returns**: `Task<IReadOnlyList<DocumentEntity>>` — All documents in the collection

---

### IAutoTagService

**Namespace**: `AgentX.Core.Services.Tagging`

AI-powered automatic tagging and manual tag management.

#### Methods

##### GenerateTagsAsync

```csharp
Task<IReadOnlyList<(string TagName, double Confidence)>> GenerateTagsAsync(
    string documentContent,
    int maxTags = 5,
    CancellationToken ct = default)
```

Generates descriptive tags for document content using AI.

**Parameters**:
- `documentContent` (string): The text content to analyze
- `maxTags` (int): Maximum number of tags (default: 5)
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<IReadOnlyList<(string, double)>>` — Tag names with confidence scores (0.0 to 1.0)

**Example**:
```csharp
var tags = await tagService.GenerateTagsAsync("AI and machine learning are...", maxTags: 5);
foreach (var (tag, confidence) in tags)
{
    Console.WriteLine($"{tag} ({confidence:P0})");
}
```

##### ApplyAutoTagsAsync

```csharp
Task ApplyAutoTagsAsync(long documentId, CancellationToken ct = default)
```

Generates tags for a document and persists them as TagEntity/DocumentTagEntity records.

**Parameters**:
- `documentId` (long): The document ID to auto-tag
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

##### GetAllTagsAsync

```csharp
Task<IReadOnlyList<TagEntity>> GetAllTagsAsync()
```

Retrieves all tags in the system, ordered by name.

**Returns**: `Task<IReadOnlyList<TagEntity>>` — All tags

##### CreateTagAsync

```csharp
Task<TagEntity> CreateTagAsync(string name, string? colorHex = null)
```

Creates a new tag.

**Parameters**:
- `name` (string): The tag name (must not be empty, must be unique)
- `colorHex` (string, optional): Hex color string (e.g., "#FF5733")

**Returns**: `Task<TagEntity>` — The created tag

##### DeleteTagAsync

```csharp
Task DeleteTagAsync(long tagId)
```

Deletes a tag. Cascade removes all document-tag associations.

**Parameters**:
- `tagId` (long): The tag ID

**Returns**: Task

##### AssignTagAsync

```csharp
Task AssignTagAsync(long documentId, long tagId)
```

Manually assigns a tag to a document with full confidence (1.0).

**Parameters**:
- `documentId` (long): The document ID
- `tagId` (long): The tag ID to assign

**Returns**: Task

##### RemoveTagAsync

```csharp
Task RemoveTagAsync(long documentId, long tagId)
```

Removes a tag assignment from a document.

**Parameters**:
- `documentId` (long): The document ID
- `tagId` (long): The tag ID to remove

**Returns**: Task

##### GetTagsForDocumentAsync

```csharp
Task<IReadOnlyList<TagEntity>> GetTagsForDocumentAsync(long documentId)
```

Retrieves all tags currently assigned to a specific document.

**Parameters**:
- `documentId` (long): The document ID

**Returns**: `Task<IReadOnlyList<TagEntity>>` — All tags assigned to the document

---

## Intelligence Services

### ISummaryService

**Namespace**: `AgentX.Core.Services.Intelligence`

Provides AI-powered document summarization, key-point extraction, and text translation.

#### Methods

##### SummarizeDocumentAsync

```csharp
Task<string> SummarizeDocumentAsync(long documentId, CancellationToken ct = default)
```

Generates a concise summary of a document by its ID.

**Parameters**:
- `documentId` (long): The document ID
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<string>` — AI-generated summary

**Throws**: InvalidOperationException if document not found or has no chunks

**Example**:
```csharp
var summary = await summaryService.SummarizeDocumentAsync(docId);
```

##### ExtractKeyPointsAsync

```csharp
Task<IReadOnlyList<string>> ExtractKeyPointsAsync(long documentId, CancellationToken ct = default)
```

Extracts key points (bullet list) from a document.

**Parameters**:
- `documentId` (long): The document ID
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<IReadOnlyList<string>>` — Ordered list of key points

**Throws**: InvalidOperationException if document not found or has no chunks

##### TranslateTextAsync

```csharp
Task<string> TranslateTextAsync(
    string text,
    string targetLanguage,
    CancellationToken ct = default)
```

Translates text to the specified target language.

**Parameters**:
- `text` (string): The source text to translate
- `targetLanguage` (string): The target language (e.g., "Spanish", "French", "Japanese")
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<string>` — The translated text

**Throws**: ArgumentException if text or targetLanguage is empty

---

### IDuplicateDetectionService

**Namespace**: `AgentX.Core.Services.Intelligence`

Detects duplicate and near-duplicate documents in the knowledge vault.

#### Methods

##### FindDuplicatesAsync

```csharp
Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(CancellationToken ct = default)
```

Scans all documents and groups those with identical content hashes (exact duplicates).

**Parameters**:
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<IReadOnlyList<DuplicateGroup>>` — List of duplicate groups

**Example**:
```csharp
var duplicates = await duplicateService.FindDuplicatesAsync();
foreach (var group in duplicates)
{
    Console.WriteLine($"Hash: {group.ContentHash}");
    foreach (var doc in group.Documents)
    {
        Console.WriteLine($"  - {doc.FileName} ({doc.FileSizeBytes} bytes)");
    }
}
```

##### FindNearDuplicatesAsync

```csharp
Task<IReadOnlyList<DuplicateGroup>> FindNearDuplicatesAsync(
    float similarityThreshold = 0.9f,
    CancellationToken ct = default)
```

Finds documents that are near-duplicates based on semantic similarity.

**Parameters**:
- `similarityThreshold` (float): Minimum cosine similarity (0.0 to 1.0) to consider as near-duplicate (default: 0.9)
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<IReadOnlyList<DuplicateGroup>>` — List of near-duplicate groups

**Note**: This operation is more expensive than exact-hash detection. Scan is capped at first 500 documents.

---

### IOrganizationSuggestionService

**Namespace**: `AgentX.Core.Services.Intelligence`

Generates AI-powered suggestions for organizing documents into collections with appropriate tags.

#### Methods

##### SuggestOrganizationAsync

Analyzes a document and suggests an optimal collection and tags for organization.

---

### IKnowledgeGraphService

**Namespace**: `AgentX.Core.Services.Intelligence`

Builds a knowledge graph representation of document relationships and applies force-directed layout.

#### Methods

##### BuildGraphAsync

```csharp
Task<KnowledgeGraphData> BuildGraphAsync(CancellationToken ct = default)
```

Loads all documents, collections, and tags from the database and constructs a force-directed graph.

**Parameters**:
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<KnowledgeGraphData>` — Positioned nodes and weighted edges

**Example**:
```csharp
var graphData = await knowledgeGraphService.BuildGraphAsync();
Console.WriteLine($"Nodes: {graphData.Nodes.Length}, Edges: {graphData.Edges.Length}");
```

---

### IDigestService

**Namespace**: `AgentX.Core.Services.Intelligence`

Generates and manages weekly digest reports summarizing knowledge vault activity.

#### Methods

##### GenerateDigestAsync

```csharp
Task<DigestReportEntity> GenerateDigestAsync(
    DateTime? periodStart = null,
    DateTime? periodEnd = null,
    CancellationToken ct = default)
```

Generates a digest report for the specified period. Defaults to the past 7 days if no dates provided.

**Parameters**:
- `periodStart` (DateTime, optional): Start of the reporting period (inclusive)
- `periodEnd` (DateTime, optional): End of the reporting period (inclusive)
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<DigestReportEntity>` — The generated and persisted digest report

**Example**:
```csharp
var digest = await digestService.GenerateDigestAsync();
// Or for a specific period:
var digest = await digestService.GenerateDigestAsync(
    periodStart: DateTime.UtcNow.AddDays(-30),
    periodEnd: DateTime.UtcNow
);
```

##### GetReportHistoryAsync

```csharp
Task<IReadOnlyList<DigestReportEntity>> GetReportHistoryAsync(int limit = 10, CancellationToken ct = default)
```

Retrieves the most recent digest reports, ordered by generation date descending.

**Parameters**:
- `limit` (int): Maximum number of reports (default: 10)
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<IReadOnlyList<DigestReportEntity>>` — Recent digest reports

##### GetLatestReportAsync

```csharp
Task<DigestReportEntity?> GetLatestReportAsync(CancellationToken ct = default)
```

Retrieves the most recently generated digest report.

**Parameters**:
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<DigestReportEntity?>` — Latest report or null if none exist

##### MarkAsReadAsync

```csharp
Task MarkAsReadAsync(long reportId, CancellationToken ct = default)
```

Marks a specific report as read by the user.

**Parameters**:
- `reportId` (long): The report ID
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

##### HasUnreadReportsAsync

```csharp
Task<bool> HasUnreadReportsAsync(CancellationToken ct = default)
```

Checks whether there are any unread digest reports.

**Parameters**:
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<bool>` — True if there are unread reports

---

## Settings & License Services

### ISettingsService

**Namespace**: `AgentX.Core.Services.Settings`

Manages application settings persistence.

#### Methods

##### GetSettingsAsync

```csharp
Task<AppSettings> GetSettingsAsync()
```

Retrieves all application settings.

**Returns**: `Task<AppSettings>` — Current settings

##### SaveSettingsAsync

```csharp
Task SaveSettingsAsync(AppSettings settings)
```

Persists application settings.

**Parameters**:
- `settings` (AppSettings): The settings to save

**Returns**: Task

##### GetValueAsync<T>

```csharp
Task<T?> GetValueAsync<T>(string key)
```

Gets a specific setting value by key.

**Type Parameters**:
- `T`: The setting value type

**Parameters**:
- `key` (string): The setting key

**Returns**: `Task<T?>` — The setting value or null if not found

##### SetValueAsync<T>

```csharp
Task SetValueAsync<T>(string key, T value)
```

Sets a specific setting value.

**Type Parameters**:
- `T`: The setting value type

**Parameters**:
- `key` (string): The setting key
- `value` (T): The value to set

**Returns**: Task

---

### ILicenseService

**Namespace**: `AgentX.Core.Services.License`

License activation, validation, and querying. Uses offline-first validation — no network calls required.

#### Methods

##### GetCurrentLicenseAsync

```csharp
Task<LicenseInfo> GetCurrentLicenseAsync()
```

Returns the current license info. If no license is activated, returns a Trial-tier license.

**Returns**: `Task<LicenseInfo>` — Current license information

**Example**:
```csharp
var license = await licenseService.GetCurrentLicenseAsync();
Console.WriteLine($"Tier: {license.Tier}");
Console.WriteLine($"Can use AI models: {license.CanUseAdvancedModels}");
```

##### ActivateLicenseAsync

```csharp
Task<LicenseActivationResult> ActivateLicenseAsync(string licenseKey)
```

Activates a license key. Validates format, checksum, and stores the activation.

**Parameters**:
- `licenseKey` (string): The license key to activate

**Returns**: `Task<LicenseActivationResult>` — Activation result with success/failure status

**Example**:
```csharp
var result = await licenseService.ActivateLicenseAsync("LICENSE-KEY-1234");
if (result.Success)
{
    Console.WriteLine($"Activated: {result.LicenseInfo?.Tier}");
}
else
{
    Console.WriteLine($"Error: {result.Error}");
}
```

##### DeactivateLicenseAsync

```csharp
Task<bool> DeactivateLicenseAsync()
```

Deactivates the current license, reverting to Trial tier.

**Returns**: `Task<bool>` — True if deactivation succeeded

##### ValidateCurrentLicenseAsync

```csharp
Task<bool> ValidateCurrentLicenseAsync()
```

Re-validates the currently stored license (format + checksum).

**Returns**: `Task<bool>` — True if the license is valid

##### GetMachineFingerprint

```csharp
string GetMachineFingerprint()
```

Generates a deterministic machine fingerprint based on hardware characteristics.

**Returns**: `string` — The machine fingerprint

---

## Vector Database

### IVectorStore

**Namespace**: `AgentX.Core.Data.VectorDb`

Abstraction over the vector database used for semantic embedding storage and retrieval.

#### Methods

##### InitializeAsync

```csharp
Task InitializeAsync(CancellationToken ct = default)
```

Initializes the vector store (creates tables, loads indexes, etc.).

**Parameters**:
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

##### InsertEmbeddingAsync

```csharp
Task<long> InsertEmbeddingAsync(
    long chunkId,
    float[] embedding,
    CancellationToken ct = default)
```

Inserts a single embedding vector associated with a document chunk.

**Parameters**:
- `chunkId` (long): The ID of the document chunk
- `embedding` (float[]): The embedding vector
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<long>` — The row ID of the inserted embedding

##### SearchAsync

```csharp
Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
    float[] queryEmbedding,
    int topK = 5,
    double minSimilarity = 0.3,
    CancellationToken ct = default)
```

Searches for the nearest neighbors to the given query embedding.

**Parameters**:
- `queryEmbedding` (float[]): The query embedding vector
- `topK` (int): Maximum number of results (default: 5)
- `minSimilarity` (double): Minimum cosine similarity threshold 0.0-1.0 (default: 0.3)
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<IReadOnlyList<VectorSearchResult>>` — Ordered results (highest similarity first)

##### DeleteEmbeddingAsync

```csharp
Task DeleteEmbeddingAsync(long chunkId, CancellationToken ct = default)
```

Deletes the embedding associated with a specific chunk.

**Parameters**:
- `chunkId` (long): The chunk ID
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

##### DeleteEmbeddingsForDocumentAsync

```csharp
Task DeleteEmbeddingsForDocumentAsync(
    long documentId,
    IReadOnlyList<long> chunkIds,
    CancellationToken ct = default)
```

Deletes all embeddings associated with a document's chunks.

**Parameters**:
- `documentId` (long): The parent document ID (for logging)
- `chunkIds` (IReadOnlyList<long>): The chunk IDs whose embeddings should be removed
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: Task

##### GetEmbeddingCountAsync

```csharp
Task<long> GetEmbeddingCountAsync(CancellationToken ct = default)
```

Returns the total number of embedding vectors currently stored.

**Parameters**:
- `ct` (CancellationToken): Cancellation token (optional)

**Returns**: `Task<long>` — Number of embeddings

##### OptimizeAsync

```csharp
Task OptimizeAsync(CancellationToken ct = default)
```

Optimizes the vector index for faster search (rebuild HNSW, vacuum, etc.).

**Returns**: Task

---

## Models & Data Structures

### ChatMessage

**Namespace**: `AgentX.Core.AI.Models`

Represents a single message in a conversation.

```csharp
public class ChatMessage
{
    public string Role { get; set; }              // "user", "assistant", "system"
    public string Content { get; set; }           // Message text
    public DateTime Timestamp { get; set; }       // When the message was created
}
```

---

### ChatOptions

**Namespace**: `AgentX.Core.AI.Models`

Configuration options for AI chat inference.

```csharp
public class ChatOptions
{
    public string? ModelId { get; set; }          // Model to use (null = active model)
    public double Temperature { get; set; } = 0.7;         // Randomness (0.0-2.0)
    public int MaxTokens { get; set; } = 2048;             // Max response length
    public int ContextWindow { get; set; } = 4096;         // Context size
    public double TopP { get; set; } = 0.9;                // Nucleus sampling
    public double FrequencyPenalty { get; set; }           // Repeat penalty
    public double PresencePenalty { get; set; }            // Topic diversity
    public string[]? StopSequences { get; set; }           // Stop tokens
}
```

---

### AiModel

**Namespace**: `AgentX.Core.AI.Models`

Represents an available AI model.

```csharp
public class AiModel
{
    public string Id { get; set; }                    // Unique identifier
    public string Name { get; set; }                  // Display name
    public string ProviderId { get; set; }            // Provider (ollama, openai, etc.)
    public string Family { get; set; }                // Model family (llama, gpt, claude, etc.)
    public bool IsAvailable { get; set; }             // Is currently available
    public long SizeBytes { get; set; }               // Model size in bytes
    public string QuantizationLevel { get; set; }     // Quantization (Q4, Q8, etc.)
    public int ParameterCount { get; set; }           // Number of parameters
    public int ContextLength { get; set; }            // Max context tokens
    public DateTime ModifiedAt { get; set; }          // Last modified
    public string Digest { get; set; }                // Content hash
    public string SizeFormatted => ...                // Formatted size (MB/GB)
}
```

---

### ModelDownloadProgress

**Namespace**: `AgentX.Core.AI.Models`

Progress information for model downloads.

```csharp
public class ModelDownloadProgress
{
    public string ModelId { get; set; }
    public string Status { get; set; }
    public long CompletedBytes { get; set; }
    public long TotalBytes { get; set; }
    public double PercentComplete => ...
}
```

---

### HardwareCapability

**Namespace**: `AgentX.Core.AI.Models`

Hardware capability information for model recommendations.

```csharp
public class HardwareCapability
{
    public string GpuName { get; set; }              // GPU name or "Unknown"
    public long GpuVramBytes { get; set; }           // GPU VRAM in bytes
    public bool HasNpu { get; set; }                 // Has NPU
    public string NpuName { get; set; }              // NPU name or "None"
    public int CpuCores { get; set; }                // CPU core count
    public string CpuName { get; set; }              // CPU name
    public long TotalRamBytes { get; set; }          // Total RAM
    public long AvailableRamBytes { get; set; }      // Available RAM
    public string GpuVramFormatted => ...            // Formatted GPU RAM
    public string TotalRamFormatted => ...           // Formatted total RAM
    public string AvailableRamFormatted => ...       // Formatted available RAM
    public string RecommendedMaxModelSize => ...     // Recommendation string
}
```

---

### SearchResult

**Namespace**: `AgentX.Core.Search.Models`

A single semantic search result with metadata.

```csharp
public class SearchResult
{
    public long ChunkId { get; init; }               // Document chunk ID
    public long DocumentId { get; init; }            // Parent document ID
    public string FileName { get; init; }            // Source file name
    public string FilePath { get; init; }            // Source file path
    public string FileType { get; init; }            // File type (pdf, docx, etc.)
    public int? PageNumber { get; init; }            // Page number (if available)
    public int ChunkIndex { get; init; }             // Chunk index
    public string MatchedText { get; init; }         // Full matched text
    public string Excerpt { get; init; }             // Display excerpt
    public float Score { get; init; }                // Cosine similarity (0.0-1.0)
    public int RelevancePercent => ...               // Score as percentage
    public List<string> CollectionNames { get; init; }  // Collection associations
}
```

---

### RagResponse

**Namespace**: `AgentX.Core.Search.Models`

Complete response from a RAG query.

```csharp
public class RagResponse
{
    public string AnswerText { get; set; }           // AI-generated answer
    public string Question { get; init; }            // Original question
    public List<Citation> Citations { get; set; }    // Source citations
    public int ContextChunksUsed { get; init; }      // Number of context chunks
    public bool IsStreaming { get; set; }            // Currently streaming
    public double TotalLatencyMs { get; set; }       // Total time (ms)
    public double SearchLatencyMs { get; set; }      // Search time (ms)
    public long? CollectionScope { get; init; }      // Collection filter used
}
```

---

### Citation

**Namespace**: `AgentX.Core.Search.Models`

A citation reference in a RAG response.

```csharp
public class Citation
{
    public int Number { get; init; }                 // Citation number [N]
    public long DocumentId { get; init; }            // Source document ID
    public long ChunkId { get; init; }               // Source chunk ID
    public string FileName { get; init; }            // Source file name
    public string FilePath { get; init; }            // Source file path
    public int? PageNumber { get; init; }            // Page number
    public int ChunkIndex { get; init; }             // Chunk index
    public string Excerpt { get; init; }             // Excerpt from chunk
    public float RelevanceScore { get; init; }       // Relevance to query
}
```

---

### SearchQuery

**Namespace**: `AgentX.Core.Search.Models`

Configurable search query with filters.

```csharp
public class SearchQuery
{
    public required string QueryText { get; init; }  // Query text
    public int TopK { get; init; } = 10;             // Result limit
    public float MinScore { get; init; } = 0.3f;     // Min similarity
    public long? CollectionId { get; init; }         // Collection filter
    public string? FileTypeFilter { get; init; }     // File type filter
    public DateTime? CreatedAfter { get; init; }     // Date range start
    public DateTime? CreatedBefore { get; init; }    // Date range end
    public SearchMode Mode { get; init; } = SearchMode.Semantic;  // Search mode
}
```

---

### SearchMode

**Namespace**: `AgentX.Core.Search.Models`

Search strategy enumeration.

```csharp
public enum SearchMode
{
    Semantic,   // Vector similarity search
    Keyword,    // Full-text FTS5 search
    Hybrid      // Combined via Reciprocal Rank Fusion
}
```

---

### LicenseInfo

**Namespace**: `AgentX.Core.Services.License`

License information and feature gates.

```csharp
public class LicenseInfo
{
    public LicenseTier Tier { get; init; }           // License tier
    public bool IsActivated { get; init; }           // Is activated
    public string? CustomerName { get; init; }       // Customer name
    public string? CustomerEmail { get; init; }      // Customer email
    public DateTime? ActivatedAt { get; init; }      // Activation date
    public DateTime? ExpiresAt { get; init; }        // Expiration date
    public int MaxDocuments { get; init; }           // Document limit

    // Feature gates
    public bool CanUseAdvancedModels => ...          // Starter+
    public bool CanUseIntelligenceFeatures => ...    // Professional+
    public bool CanUseUnlimitedDocuments => ...      // Professional+
    public bool CanUsePrioritySupport => ...         // Ultimate only

    public bool HasFeature(string feature) => ...    // Check feature by name
}
```

---

### LicenseTier

**Namespace**: `AgentX.Core.Services.License`

License tier enumeration.

```csharp
public enum LicenseTier
{
    Trial,          // 50 documents, basic features
    Starter,        // 500 documents, advanced models
    Professional,   // Unlimited documents, intelligence features
    Ultimate        // All features + priority support
}
```

---

### DuplicateGroup

**Namespace**: `AgentX.Core.Services.Intelligence.Models`

A group of duplicate documents.

```csharp
public class DuplicateGroup
{
    public string ContentHash { get; init; }         // Shared content hash
    public List<DuplicateDocument> Documents { get; init; }  // Duplicate documents
    public long WastedStorageBytes => ...            // Wasted storage (duplicates only)
}
```

---

### DuplicateDocument

**Namespace**: `AgentX.Core.Services.Intelligence.Models`

A document within a duplicate group.

```csharp
public class DuplicateDocument
{
    public long DocumentId { get; init; }            // Document ID
    public string FileName { get; init; }            // File name
    public string FilePath { get; init; }            // File path
    public long FileSizeBytes { get; init; }         // File size
    public DateTime ImportedAt { get; init; }        // Import time
}
```

---

### KnowledgeGraphData

**Namespace**: `AgentX.Core.Services.Intelligence.Models`

Knowledge graph representation with nodes and edges.

```csharp
public class KnowledgeGraphData
{
    public GraphNode[] Nodes { get; init; }          // Graph nodes
    public GraphEdge[] Edges { get; init; }          // Graph edges
    public int DocumentCount { get; init; }          // Total documents
    public int CollectionCount { get; init; }        // Total collections
    public int TagCount { get; init; }               // Total tags
}
```

---

### GraphNode

**Namespace**: `AgentX.Core.Services.Intelligence.Models`

A node in the knowledge graph.

```csharp
public class GraphNode
{
    public string Id { get; init; }                  // Node ID
    public string Label { get; init; }               // Display label
    public string Type { get; init; }                // Document/Collection/Tag
    public string Color { get; init; }               // Display color
    public double Size { get; init; }                // Node size
    public double X { get; init; }                   // X position
    public double Y { get; init; }                   // Y position
}
```

---

### GraphEdge

**Namespace**: `AgentX.Core.Services.Intelligence.Models`

An edge in the knowledge graph.

```csharp
public class GraphEdge
{
    public string Source { get; init; }              // Source node ID
    public string Target { get; init; }              // Target node ID
    public string Label { get; init; }               // Edge label
    public double Weight { get; init; }              // Edge weight (strength)
    public string Color { get; init; }               // Display color
}
```

---

### DuplicateCheckResult

**Namespace**: `AgentX.Core.Documents`

Result of duplicate detection check.

```csharp
public class DuplicateCheckResult
{
    public bool IsDuplicate { get; init; }           // Is a duplicate
    public bool IsExactMatch { get; init; }          // Exact match vs similarity
    public long ExistingDocumentId { get; init; }    // Existing document ID
    public string ExistingFileName { get; init; }    // Existing file name
    public float MatchScore { get; init; }           // Match score (0.0-1.0)
}
```

---

### ProcessedDocument

**Namespace**: `AgentX.Core.Documents.Models`

Document with extracted text and metadata.

Contains:
- Full text content
- Page-by-page content (if available)
- Metadata (title, author, etc.)
- Detected language

---

### DocumentChunk

**Namespace**: `AgentX.Core.Documents.Models`

A chunk of a document suitable for embedding.

Contains:
- Chunk text content
- Start/end character offsets
- Token count estimate
- Page number (if available)
- Section title (if available)

---

### IndexingProgressEventArgs

**Namespace**: `AgentX.Core.Services.Indexing`

Progress information for indexing operations.

```csharp
public class IndexingProgressEventArgs : EventArgs
{
    public int QueueLength { get; init; }            // Items remaining
    public int Processed { get; init; }              // Items processed
    public string? CurrentDocument { get; init; }    // Currently indexing
    public double? PercentComplete { get; init; }    // Progress percentage
}
```

---

## Quick Reference Table

| Service | Namespace | Primary Use |
|---------|-----------|------------|
| `IAiService` | AgentX.Core.AI | Chat, summarization, tagging |
| `IAiProvider` | AgentX.Core.AI | Provider abstraction (Ollama, OpenAI, Anthropic) |
| `IModelManager` | AgentX.Core.AI | Model lifecycle management |
| `IEmbeddingService` | AgentX.Core.AI | Text to vector embedding |
| `IChatService` | AgentX.Core.Services.Chat | Chat operations and streaming |
| `IConversationService` | AgentX.Core.Services.Chat | Conversation persistence |
| `ISystemPromptService` | AgentX.Core.Services.Chat | Prompt templates |
| `IDocumentService` | AgentX.Core.Documents | Document import and management |
| `IDocumentProcessor` | AgentX.Core.Documents | Text extraction |
| `IChunkingService` | AgentX.Core.Documents | Text chunking |
| `ISemanticSearchService` | AgentX.Core.Search | Vector-based search |
| `IKeywordSearchService` | AgentX.Core.Search | Full-text search |
| `IRagPipeline` | AgentX.Core.Search | RAG orchestration |
| `IIndexingService` | AgentX.Core.Services.Indexing | Background indexing |
| `IFileWatcherService` | AgentX.Core.Services.Indexing | Folder monitoring |
| `ICollectionService` | AgentX.Core.Services.Collections | Document organization |
| `IAutoTagService` | AgentX.Core.Services.Tagging | Auto-tagging |
| `ISummaryService` | AgentX.Core.Services.Intelligence | Document summaries |
| `IDuplicateDetectionService` | AgentX.Core.Services.Intelligence | Duplicate detection |
| `IKnowledgeGraphService` | AgentX.Core.Services.Intelligence | Graph visualization |
| `IDigestService` | AgentX.Core.Services.Intelligence | Weekly reports |
| `ISettingsService` | AgentX.Core.Services.Settings | Configuration |
| `ILicenseService` | AgentX.Core.Services.License | License management |
| `IVectorStore` | AgentX.Core.Data.VectorDb | Vector storage |

---

## Common Usage Patterns

### Initialize the AI Service

```csharp
var aiService = serviceProvider.GetRequiredService<IAiService>();
await aiService.InitializeAsync();
if (!aiService.IsConnected)
{
    throw new InvalidOperationException("AI provider not available");
}
```

### Import and Index Documents

```csharp
var documentService = serviceProvider.GetRequiredService<IDocumentService>();
var indexingService = serviceProvider.GetRequiredService<IIndexingService>();

// Import document
var doc = await documentService.ImportFileAsync("/path/to/document.pdf");

// Index it (happens automatically via background queue)
await indexingService.IndexDocumentAsync(doc.Id);
```

### Perform RAG Query

```csharp
var ragPipeline = serviceProvider.GetRequiredService<IRagPipeline>();

var response = await ragPipeline.AskAsync(
    "What are the main topics in the documents?",
    onToken: token => Console.Write(token)
);

Console.WriteLine($"\nFound {response.Citations.Count} citations");
foreach (var citation in response.Citations)
{
    Console.WriteLine($"[{citation.Number}] {citation.FileName}: {citation.Excerpt}");
}
```

### Chat with Streaming

```csharp
var chatService = serviceProvider.GetRequiredService<IChatService>();
var conversationService = serviceProvider.GetRequiredService<IConversationService>();

// Create conversation
var conv = await conversationService.CreateConversationAsync("My Chat");

// Stream response
await foreach (var token in chatService.SendMessageAsync(conv.Id, "Hello!"))
{
    Console.Write(token);
}
```

### Search Documents

```csharp
var searchService = serviceProvider.GetRequiredService<ISemanticSearchService>();

var results = await searchService.SearchAsync(new SearchQuery
{
    QueryText = "machine learning benefits",
    TopK = 5,
    MinScore = 0.5f
});

foreach (var result in results)
{
    Console.WriteLine($"{result.FileName}: {result.Excerpt} ({result.RelevancePercent}%)");
}
```

---

**Last Updated**: 2026-02-27
**Version**: 1.0.0
