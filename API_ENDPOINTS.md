# Agent-X External API Documentation

## Overview

Agent-X integrates with multiple **external AI services, web platforms, and APIs** to provide comprehensive AI-native functionality. This document catalogs all external endpoints, authentication methods, and usage patterns.

---

## AI Provider Integrations

### OpenAI API

**Provider ID:** `openai`  
**Base URL:** `https://api.openai.com/v1/` (configurable)  
**Documentation:** https://platform.openai.com/docs/api-reference

#### Authentication

```csharp
// Bearer token in Authorization header
_http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
```

#### Endpoints Used

| Method | Endpoint | Purpose | Notes |
|--------|----------|---------|-------|
| GET | `/models` | List available models | Called on startup |
| POST | `/chat/completions` | Chat completion | Supports streaming |

#### Request Format (Chat Completions)

```json
POST /chat/completions
{
  "model": "gpt-4o",
  "messages": [
    { "role": "system", "content": "You are a helpful assistant." },
    { "role": "user", "content": "Hello!" }
  ],
  "stream": true,
  "temperature": 0.7,
  "max_tokens": 4096
}
```

#### Streaming Response Format (Server-Sent Events)

```
data: {"id":"chatcmpl-123","object":"chat.completion.chunk","created":1699000000,"model":"gpt-4o","choices":[{"index":0,"delta":{"content":"Hello"}}]}

data: [DONE]
```

#### Supported Models

| Model ID | Display Name | Context | Features |
|----------|--------------|---------|----------|
| `gpt-4o` | GPT-4 Omni | 128K | Vision, streaming |
| `gpt-4o-mini` | GPT-4o Mini | 128K | Faster, lower cost |
| `gpt-4-turbo` | GPT-4 Turbo | 128K | Legacy support |
| `o1-preview` | o1 Preview | Variable | Chain-of-thought |
| `o1-mini` | o1 Mini | Variable | Fast reasoning |

#### Code Reference

```csharp
// src/AgentX.Core/AI/Providers/OpenAiProvider.cs
public sealed class OpenAiProvider : IAiProvider
{
    public async IAsyncEnumerable<string> StreamChatAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default)
    {
        // Implementation uses HttpClient with SSE parsing
    }
}
```

---

### Anthropic Claude API

**Provider ID:** `anthropic`  
**Base URL:** `https://api.anthropic.com/v1/` (configurable)  
**Documentation:** https://docs.anthropic.com/claude/reference/

#### Authentication

```csharp
// x-api-key header (not Authorization)
_http.DefaultRequestHeaders.Add("x-api-key", apiKey);
_http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
```

#### Endpoints Used

| Method | Endpoint | Purpose | Notes |
|--------|----------|---------|-------|
| POST | `/messages` | Chat completion | Anthropic-specific format |
| GET | N/A | List models | No endpoint; static catalog |

#### Request Format (Messages)

```json
POST /messages
{
  "model": "claude-sonnet-4-20250514",
  "max_tokens": 4096,
  "system": "You are a helpful assistant.",
  "messages": [
    { "role": "user", "content": "Hello!" }
  ],
  "stream": true
}
```

#### Streaming Response Format

```
event: message_start
data: {"type":"message_start","message":{"id":"msg-123","role":"assistant","content":[]}}

event: content_block_delta
data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello"}}

event: message_stop
```

#### Supported Models

| Model ID | Display Name | Context | Features |
|----------|--------------|---------|----------|
| `claude-sonnet-4-20250514` | Claude Sonnet 4 | 200K | Balanced performance |
| `claude-haiku-4-5-20251001` | Claude Haiku 4.5 | 200K | Fast, low cost |
| `claude-opus-4-20250514` | Claude Opus 4 | 200K | Highest quality |
| `claude-3-5-sonnet-20241022` | Claude 3.5 Sonnet | 200K | Legacy |

#### Code Reference

```csharp
// src/AgentX.Core/AI/Providers/AnthropicProvider.cs
public sealed class AnthropicProvider : IAiProvider
{
    private const string AnthropicApiVersion = "2023-06-01";
    
    public async IAsyncEnumerable<string> StreamChatAsync(...)
    {
        // Implements Anthropic-specific SSE event parsing
    }
}
```

---

### Ollama API (Local LLM)

**Provider ID:** `ollama`  
**Base URL:** `http://localhost:11434` (default, configurable)  
**Documentation:** https://github.com/ollama/ollama/blob/main/docs/api.md

#### Authentication

None (local API).

#### Endpoints Used

| Method | Endpoint | Purpose | Notes |
|--------|----------|---------|-------|
| GET | `/api/tags` | List local models | Equivalent to /models |
| POST | `/api/chat` | Chat completion | Streaming supported |
| POST | `/api/embeddings` | Generate embeddings | For local embedding models |

#### Request Format (Chat)

```json
POST /api/chat
{
  "model": "llama3.2",
  "messages": [
    { "role": "user", "content": "Hello!" }
  ],
  "stream": true,
  "options": {
    "temperature": 0.7,
    "num_ctx": 4096
  }
}
```

#### Streaming Response Format

```
{"model":"llama3.2","created_at":"2024-01-01T00:00:00Z","message":{"role":"assistant","content":"Hello"},"done":false}

{"model":"llama3.2","done":true,"total_duration":123456789}
```

#### Supported Models

Models are dynamically discovered from local Ollama installation. Common models:

| Model ID | Display Name | Parameters |
|----------|--------------|------------|
| `llama3.2` | Llama 3.2 | 3B/70B |
| `mistral` | Mistral 7B | 7B |
| `codellama` | Code Llama | 7B/13B/34B |
| `phi3` | Phi-3 | 3.8B/14B |
| `gemma2` | Gemma 2 | 9B/27B |

#### Code Reference

```csharp
// src/AgentX.Core/AI/Providers/OllamaProvider.cs
public sealed class OllamaProvider : IAiProvider
{
    private readonly OllamaApiClient _client;
    
    public async Task<bool> CheckConnectionAsync(CancellationToken ct = default)
    {
        // Uses OllamaSharp library with 3s timeout
        return await _client.IsRunningAsync(ct);
    }
}
```

---

### Local LLM (LLamaSharp)

**Provider ID:** `local-llm`  
**Base URL:** N/A (in-process)  
**Documentation:** https://github.com/SciSharp/LLamaSharp

#### Usage

```csharp
// src/AgentX.Core/AI/Providers/LocalLlmProvider.cs
public sealed class LocalLlmProvider : IAiProvider
{
    // Loads GGUF model files directly
    // Supports CPU and CUDA backends
    // In-process inference (no HTTP API)
}
```

#### Supported Model Formats

- GGUF (primary)
- GGML (legacy)

---

## Embedding Services

### OpenAI Embeddings

**Endpoint:** `https://api.openai.com/v1/embeddings`

```json
POST /embeddings
{
  "model": "text-embedding-3-small",
  "input": "Your text here",
  "dimensions": 1536
}
```

| Model ID | Dimensions | Cost |
|----------|------------|------|
| `text-embedding-3-small` | 1536 | $0.02/1M tokens |
| `text-embedding-3-large` | 3072 | $0.13/1M tokens |
| `text-embedding-ada-002` | 1536 | Legacy |

---

### Ollama Embeddings

**Endpoint:** `POST http://localhost:11434/api/embeddings`

```json
{
  "model": "nomic-embed-text",
  "prompt": "Your text here"
}
```

| Model ID | Dimensions |
|----------|------------|
| `nomic-embed-text` | 768 |
| `mxbai-embed-large` | 1024 |
| `all-minilm` | 384 |

---

## Web Search APIs

### Tavily API (Default)

**Base URL:** `https://api.tavily.com/search`  
**Documentation:** https://docs.tavily.com/docs/tavily-api/rest-api

#### Authentication

API Key as query parameter or in request body.

#### Request Format

```json
POST /search
{
  "api_key": "your-key-here",
  "query": "search query",
  "search_depth": "basic",
  "max_results": 10,
  "include_answer": true,
  "include_raw_content": false
}
```

#### Response Format

```json
{
  "answer": "AI-generated answer",
  "query": "search query",
  "results": [
    {
      "title": "Page title",
      "url": "https://example.com",
      "content": "Page content snippet...",
      "score": 0.95,
      "raw_content": null
    }
  ]
}
```

#### Code Reference

```csharp
// src/AgentX.Core/Search/WebSearchService.cs
public interface IWebSearchService
{
    Task<WebSearchResult> SearchAsync(
        string query,
        int maxResults = 10,
        CancellationToken ct = default);
}
```

---

## Web Content Extraction

### Built-in Web Scraper

**Technology:**
- **HtmlAgilityPack** - HTML parsing
- **Playwright** - JavaScript rendering (optional)
- **Readability-like** algorithm - Content extraction

#### No External API

The web scraper is fully local and does not call external services:

```csharp
// src/AgentX.Core/Services/Web/WebScraperService.cs
public interface IWebScraperService
{
    Task<WebContent> ScrapeAsync(
        string url,
        bool enableJsRendering = false,
        CancellationToken ct = default);
}
```

#### JavaScript Rendering

When enabled, Playwright launches a headless browser:

```csharp
// src/AgentX.Core/Services/Web/JsRenderingService.cs
public interface IJsRenderingService
{
    Task<string> RenderWithJsAsync(
        string url,
        CancellationToken ct = default);
}
```

---

## OAuth Integrations

### Google OAuth 2.0

**Provider ID:** `google`  
**Discovery Document:** `https://accounts.google.com/.well-known/openid-configuration`

#### Scopes Used

| Scope | Purpose |
|-------|---------|
| `openid` | OpenID Connect |
| `email` | User email |
| `profile` | Basic profile info |
| `https://www.googleapis.com/auth/calendar` | Calendar access |
| `https://www.googleapis.com/auth/gmail.readonly` | Gmail read access |

#### Code Reference

```csharp
// src/AgentX.Core/Services/OAuth/OAuthProviderRegistry.cs
public static class OAuthProviderRegistry
{
    public static OAuthProvider Google(
        string clientId,
        string clientSecret,
        string redirectUri) => new()
    {
        ProviderId = "google",
        AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
        TokenEndpoint = "https://oauth2.googleapis.com/token",
        // ...
    };
}
```

---

### Microsoft Graph OAuth

**Provider ID:** `microsoft`  
**Base URL:** `https://login.microsoftonline.com/`  

#### Scopes Used

| Scope | Purpose |
|-------|---------|
| `openid` | OpenID Connect |
| `email` | User email |
| `profile` | Basic profile |
| `Calendars.ReadWrite` | Calendar access |
| `Mail.Read` | Outlook email access |

---

## Plugin System APIs

### Plugin Manifest Format

**File:** `plugin.json` in plugin directory

```json
{
  "id": "agentx-plugin-example",
  "name": "Example Plugin",
  "version": "1.0.0",
  "description": "An example plugin",
  "author": "Your Name",
  "type": "DataConnector",
  "entryPoint": "ExamplePlugin.Plugin, ExamplePlugin",
  "permissions": ["read:documents", "write:documents"],
  "settings": {
    "apiKey": { "type": "string", "required": true }
  }
}
```

### Plugin API Contracts

Plugins implement one or more of these interfaces:

```csharp
// Data connector plugin
public interface IDataConnectorPlugin
{
    Task<IReadOnlyList<InboxItem>> FetchItemsAsync(
        CancellationToken ct = default);
}

// AI provider plugin
public interface IAiProviderPlugin
{
    IAiProvider CreateProvider(
        string apiKey,
        string endpoint,
        ILogger logger);
}
```

---

## Local REST API

Agent-X exposes a **local REST API** for the browser extension and mobile companion.

### Base URL

```
http://localhost:5324/api/v1
```

### Endpoints

#### Health Check

```
GET /api/v1/health
```

**Response:**
```json
{
  "status": "healthy",
  "version": "1.0.0",
  "timestamp": "2025-01-03T12:00:00Z"
}
```

---

#### Chat Completions

```
POST /api/v1/chat/completions
```

**Request:**
```json
{
  "modelId": "claude-sonnet-4-20250514",
  "messages": [
    { "role": "user", "content": "Hello!" }
  ],
  "stream": true
}
```

**Response:** SSE stream matching provider format

---

#### Semantic Search

```
POST /api/v1/search
```

**Request:**
```json
{
  "query": "search query",
  "collectionIds": [1, 2, 3],
  "limit": 10
}
```

**Response:**
```json
{
  "results": [
    {
      "documentId": 123,
      "chunkId": 456,
      "content": "matched content",
      "score": 0.95,
      "citations": [
        {
          "documentId": 123,
          "fileName": "example.pdf",
          "filePath": "/path/to/file.pdf",
          "page": 10
        }
      ]
    }
  ]
}
```

---

#### Index Document

```
POST /api/v1/documents/index
```

**Request:**
```json
{
  "filePath": "/path/to/document.pdf",
  "collectionId": 1
}
```

**Response:**
```json
{
  "documentId": 123,
  "status": "indexing",
  "chunkCount": 0
}
```

---

#### Get Conversations

```
GET /api/v1/conversations
```

**Response:**
```json
{
  "conversations": [
    {
      "id": 1,
      "title": "Example Chat",
      "modelId": "claude-sonnet-4-20250514",
      "createdAt": "2025-01-03T12:00:00Z",
      "updatedAt": "2025-01-03T12:30:00Z",
      "messageCount": 10
    }
  ]
}
```

---

## Browser Extension Integration

### Message Passing

The extension uses **Chrome runtime messaging** to communicate with the local API:

```javascript
// Extension side
chrome.runtime.sendNativeMessage(
    "com.agentx.bridge",
    { type: "search", query: "example" },
    (response) => console.log(response)
);
```

### Native Messaging Host

**Manifest:** `com.agentx.bridge.json` (installed to registry)

```json
{
  "name": "com.agentx.bridge",
  "description": "Agent-X Native Messaging Host",
  "path": "C:\\Path\\To\\AgentX.NativeMessagingHost.exe",
  "type": "stdio",
  "allowed_origins": [
    "chrome-extension://YOUR_EXTENSION_ID/"
  ]
}
```

---

## Mobile Companion API

### Authentication

Uses **shared secret** negotiated during QR code pairing.

### Endpoints

Mobile companion uses the same REST API as browser extension, with additional:

| Endpoint | Purpose |
|----------|---------|
| `POST /api/v1/pair/initiate` | Initiate pairing flow |
| `POST /api/v1/pair/confirm` | Confirm pairing code |
| `GET /api/v1/sync/status` | Sync status |
| `POST /api/v1/sync/pull` | Pull data from desktop |

---

## Rate Limits & Quotas

### OpenAI

| Tier | Rate Limit |
|------|------------|
| Free | 3 requests/minute |
| Tier 1 | 10,000 TPM (tokens per minute) |
| Tier 2 | 60,000 TPM |
| Tier 3 | 300,000 TPM |

### Anthropic

| Tier | Rate Limit |
|------|------------|
| Free | 5 requests/minute |
| Paid | 50 requests/minute (standard) |
| Enterprise | Custom |

### Ollama

No rate limit (local).

---

## Error Handling

### Standard Error Response

```json
{
  "error": {
    "code": "rate_limit_exceeded",
    "message": "Rate limit exceeded. Please retry after 60 seconds.",
    "details": {
      "retryAfter": 60,
      "limit": 100,
      "remaining": 0
    }
  }
}
```

### Error Codes

| Code | Description | Retry |
|------|-------------|-------|
| `rate_limit_exceeded` | API rate limit | Yes, after retry-after |
| `invalid_api_key` | Authentication failed | No |
| `insufficient_quota` | Quota exceeded | No |
| `model_not_found` | Model unavailable | No |
| `timeout` | Request timeout | Yes (exponential backoff) |
| `network_error` | Connection failed | Yes (exponential backoff) |

---

## Retry Policy

Agent-X uses **exponential backoff with jitter**:

```csharp
// src/AgentX.Core/AI/ExponentialBackoffRetryPolicy.cs
public class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    private readonly TimeSpan _initialDelay = TimeSpan.FromSeconds(1);
    private readonly TimeSpan _maxDelay = TimeSpan.FromSeconds(60);
    private readonly int _maxRetries = 5;
    
    public async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        CancellationToken ct = default)
    {
        // Exponential backoff: 1s, 2s, 4s, 8s, 16s...
    }
}
```

---

## Cost Tracking

Agent-X tracks **API usage and costs**:

```csharp
// src/AgentX.Core/AI/Models/CostTracker.cs
public interface ICostTracker
{
    void RecordTokens(string modelId, int inputTokens, int outputTokens);
    Task<CostReport> GetCostReportAsync(
        DateTime start,
        DateTime end);
}

public record CostReport(
    decimal TotalCost,
    int TotalInputTokens,
    int TotalOutputTokens,
    IDictionary<string, ModelCost> ByModel);
```

### Pricing Reference

| Model | Input (per 1M tokens) | Output (per 1M tokens) |
|-------|----------------------|------------------------|
| GPT-4o | $2.50 | $10.00 |
| GPT-4o-mini | $0.15 | $0.60 |
| Claude Sonnet 4 | $3.00 | $15.00 |
| Claude Haiku 4.5 | $0.80 | $4.00 |
| Llama 3.2 (local) | $0 | $0 |

---

## Monitoring & Logging

### API Call Logging

All API calls are logged via Serilog:

```
[DEBUG] Sending POST https://api.anthropic.com/v1/messages
[DEBUG] Response 200 in 1.2s
[INFO] Tokens: input=123, output=456, cost=$0.002
```

### Telemetry

Optional telemetry sends anonymous usage data:

```csharp
// src/AgentX.Core/Services/Analytics/IAnalyticsService.cs
public interface IAnalyticsService
{
    Task TrackApiCallAsync(
        string provider,
        string model,
        int tokens,
        decimal cost);
}
```

---

## Security Considerations

### API Key Storage

API keys are **encrypted at rest** using Windows DPAPI:

```csharp
// src/AgentX.Core/Services/Security/DpapiEncryptionService.cs
public interface IDpapiEncryptionService
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}
```

### Transport Security

- All external APIs use **HTTPS**
- TLS 1.2+ required
- Certificate validation enabled

### Data in Transit

- Local API uses **localhost only** (no network exposure)
- Native messaging uses **stdio pipes**

---

## Future API Integrations

### Planned

| Service | Purpose | Status |
|---------|---------|--------|
| Perplexity API | Web search | Backlog |
| Brave Search API | Web search alternative | Backlog |
| Cohere API | Reranking | Backlog |
| Pinecone | Cloud vector store | Backlog |
| Weaviate Cloud | Cloud vector store | Backlog |

### Contribution Guide

To add a new AI provider:

1. Implement `IAiProvider` interface
2. Add provider to `AiServiceFactory`
3. Update `OAuthProviderRegistry` if needed
4. Add provider-specific configuration to `AppSettings`
5. Document costs and rate limits

---

**Document Version:** 1.0  
**Last Updated:** 2025-01-03  
**Maintained By:** Agent-X Development Team
