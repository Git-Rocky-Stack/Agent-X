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

Agent-X exposes a **local REST API** for the browser extension and the Android companion. It is an
`HttpListener` host inside the desktop process (`src/AgentX.Core/Services/Api/ApiHostService.cs`);
there is no separate server, no ASP.NET Core, and no native messaging host.

### Base URL and lifecycle

```
http://localhost:9846/
```

- **Loopback only.** The listener is registered for the HTTP.sys prefix `http://localhost:9846/`
  and is not reachable from the LAN. Plain HTTP, no TLS. HTTP.sys also rejects a request whose
  `Host` header names another host (`400 Invalid Hostname`), which is why the Android client sends
  `Host: localhost:9846` when it connects through the emulator alias `10.0.2.2`
  (see [`docs/MOBILE-TRANSPORT.md`](docs/MOBILE-TRANSPORT.md)).
- **Started at app launch** by `ApiHostLifecycleService` when **Settings > Connections > Enable
  Local API** is on (the default). Saving settings applies the toggle at once (the listener stops
  or starts), and regenerating the token applies it at once (see Authentication).
- **Routing:** paths are matched case-insensitively and a trailing slash is ignored. An unknown
  path, or a known path with the wrong method, returns `404`.
- **Concurrency:** at most 16 requests are processed at the same time.

### Authentication

Every route except `GET /api/extension/health` requires a bearer token:

```
Authorization: Bearer <token>
```

- The token is a per-install, 256-bit random value, hex-encoded (64 characters). It is generated
  on first start, stored DPAPI-encrypted in `settings.json`, and shown masked in **Settings >
  Connections** with **Show**, **Copy** and **Regenerate**.
- **Regenerate** revokes the previous token immediately: from the next request only the new token
  is accepted, so paired clients must be re-paired.
- A missing or wrong token gets `401` with `WWW-Authenticate: Bearer`. If no token is provisioned,
  every protected route returns `401` (fail closed). Tokens are compared in constant time
  (`LocalApiSecurity.IsAuthorized`).
- Clients validate a token with `GET /api/auth/check`. The public extension health probe accepts
  any token and must not be used to decide that a client is paired.

### CORS

`Access-Control-Allow-Origin` is echoed back only for browser-extension origins
(`chrome-extension://`, `moz-extension://`, `ms-browser-extension://`), together with `Vary: Origin`.
Web pages get no CORS grant, so they cannot read responses. A preflight `OPTIONS` request on any
path returns `204`. Allowed methods: `GET, POST, OPTIONS`; allowed headers: `Content-Type,
Authorization, Accept, X-Requested-With`; `Access-Control-Max-Age: 86400`.

### Response envelope

Every response body, success or error, is JSON (`application/json; charset=utf-8`) with camelCase
names, wrapped in the same envelope (`ApiResponse<T>`). Null properties are omitted, so `error` is
absent on success and `data` is absent on error.

```json
{ "success": true, "data": { "status": "ok" }, "timestamp": "2026-09-26T10:00:00.0000000Z" }
```

```json
{ "success": false, "error": "Unauthorized. A valid API token is required. Pair the client with the token from AgentX Settings.", "timestamp": "2026-09-26T10:00:00.0000000Z" }
```

| Status | When |
|--------|------|
| `200` | Success |
| `201` | `POST /api/inbox/clip` created an inbox item |
| `204` | CORS preflight (`OPTIONS`) |
| `400` | Malformed JSON body, or a required field is missing or empty |
| `401` | Missing or wrong bearer token |
| `404` | Unknown route, wrong method, non-numeric id, or an item that does not exist |
| `500` | Unexpected server error (`"An internal server error occurred."`) or failed inbox ingestion |

### Endpoints

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| `GET` | `/api/extension/health` | none | Liveness probe for the browser extension |
| `GET` | `/api/auth/check` | bearer | Confirms the token (pairing) |
| `GET` | `/api/health` | bearer | Status, version, uptime and counts (mobile connectivity check) |
| `GET` | `/api/documents` | bearer | All documents in the Knowledge Vault |
| `GET` | `/api/documents/{id}` | bearer | One document |
| `GET` | `/api/conversations` | bearer | All non-archived conversations |
| `GET` | `/api/conversations/{id}` | bearer | One conversation |
| `GET` | `/api/collections` | bearer | All collections |
| `POST` | `/api/search` | bearer | Semantic search over indexed documents |
| `POST` | `/api/inbox/clip` | bearer | Clip web content into the Smart Inbox |

#### GET /api/extension/health

Public (no token). Returns no user data; lets the extension detect that Agent-X is running.

```json
{
  "success": true,
  "data": {
    "connected": true,
    "version": "2.2.0",
    "inboxEnabled": true,
    "provider": "local"
  },
  "timestamp": "2026-09-26T10:00:00.0000000Z"
}
```

- `version` is the application version (`AppVersionInfo.Display`).
- `provider` is the active provider from Settings (`local`, `ollama`, `openai` or `anthropic`).
- `inboxEnabled` is always `true`: the Smart Inbox has no off switch, and the clip route is served
  whenever the API runs.

#### GET /api/auth/check

Answers only when the bearer token is valid (otherwise `401`).

```json
{ "success": true, "data": { "authenticated": true, "version": "2.2.0" }, "timestamp": "..." }
```

#### GET /api/health

```json
{
  "success": true,
  "data": {
    "status": "ok",
    "version": "2.2.0",
    "uptime": "2h 15m 40s",
    "documentCount": 42,
    "conversationCount": 7
  },
  "timestamp": "..."
}
```

`uptime` is measured since the listener started. `conversationCount` counts non-archived
conversations.

#### GET /api/documents and GET /api/documents/{id}

The list returns every document (no paging or filters); the by-id form returns one object, or
`404` when the id does not exist or is not a number.

```json
{
  "success": true,
  "data": [
    {
      "id": 12,
      "fileName": "report.pdf",
      "fileType": "pdf",
      "fileSizeBytes": 204800,
      "importedAt": "2026-09-20T08:30:00Z",
      "indexingStatus": "completed"
    }
  ],
  "timestamp": "..."
}
```

#### GET /api/conversations and GET /api/conversations/{id}

The list returns non-archived conversations; the by-id form returns one object, or `404`.

```json
{
  "success": true,
  "data": [
    {
      "id": 5,
      "title": "Planning",
      "modelId": "llama3.2",
      "createdAt": "2026-09-20T08:30:00Z",
      "updatedAt": "2026-09-21T10:00:00Z",
      "messageCount": 12,
      "tokensUsed": 3400
    }
  ],
  "timestamp": "..."
}
```

#### GET /api/collections

```json
{
  "success": true,
  "data": [
    { "id": 1, "name": "Finance", "description": "Quarterly reports", "documentCount": 4, "createdAt": "2026-09-01T12:00:00Z" }
  ],
  "timestamp": "..."
}
```

#### POST /api/search

Semantic search. Field names are camelCase.

```json
{ "query": "vector databases", "topK": 10, "minScore": 0.3 }
```

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| `query` | string | (required) | Empty or whitespace returns `400` |
| `topK` | int | `10` | Clamped to 1-50 |
| `minScore` | float | `0.3` | Clamped to 0.0-1.0 |

```json
{
  "success": true,
  "data": [
    { "documentId": 3, "fileName": "a.pdf", "chunkContent": "matched snippet", "score": 0.91 }
  ],
  "timestamp": "..."
}
```

#### POST /api/inbox/clip

Saves clipped web content as a Markdown file and adds it to the Smart Inbox as a pending item
(`sourceType` `browser-extension`).

```json
{
  "title": "Example Article",
  "content": "The clipped text, as Markdown or plain text.",
  "sourceUrl": "https://example.com/post",
  "author": "Jane Doe",
  "publishedDate": "2024-03-05T10:00:00+0000",
  "clipMode": "reader",
  "wordCount": 1234,
  "metadata": { "category": "tech" }
}
```

| Field | Type | Notes |
|-------|------|-------|
| `content` | string | Required and non-empty, otherwise `400` |
| `title` | string | Optional; missing or blank becomes `Untitled` |
| `sourceUrl` | string | Optional |
| `author` | string | Optional |
| `publishedDate` | string | Optional. Parsed leniently (ISO 8601 with or without the offset colon, `yyyy-MM-dd HH:mm:ss`, `yyyy-MM`, `yyyyMMdd`, RFC 1123, ...). A value that cannot be read is dropped; it never fails the clip. |
| `clipMode` | string | `full`, `selection` (default) or `reader` |
| `wordCount` | int | Optional |
| `metadata` | object of strings | Optional; written as extra front matter keys. Keys that collide with the host's own keys are ignored. |

The file is written to `%LOCALAPPDATA%\AgentX\Clips\` as
`<title, sanitized, at most 80 characters>-<yyyyMMdd-HHmmss UTC>-<8 hex>.md`, never overwriting an
existing file. Every value in the front matter is an escaped YAML double-quoted string, and dates
and numbers are culture-invariant:

```yaml
---
title: "Example Article"
source_url: "https://example.com/post"
author: "Jane Doe"
published_date: "2024-03-05"
clip_mode: reader
word_count: 1234
clipped_at: "2026-09-26T10:00:00.0000000Z"
"category": "tech"
---

The clipped text, as Markdown or plain text.
```

Response (`201`):

```json
{
  "success": true,
  "data": { "inboxItemId": 12, "status": "clipped", "message": "Content clipped to inbox as item #12." },
  "timestamp": "..."
}
```

If the inbox rejects the item, the file is deleted and the route returns `500`.

---

## Browser Extension Integration

The extension (`browser-extension/`, Manifest V3) talks to the Local REST API over HTTP from its
service worker; there is no native messaging host.

- **Pairing:** the user pastes the token from **Settings > Connections** into the popup. The service
  worker validates it with `GET /api/auth/check` before storing it; a token Agent-X rejects is not
  stored.
- **Liveness and status:** `GET /api/extension/health` (public) plus `GET /api/auth/check`, so the
  popup distinguishes paired, "Not paired" and "Offline".
- **Clipping:** the extractor is injected into the page on demand with `chrome.scripting` and the
  result is posted to `POST /api/inbox/clip`. The token stays in the service worker; the injected
  script never reads it.

---

## Mobile Companion API

The Android companion (`src/AgentX.Mobile`) uses the same routes and the same bearer token:
`GET /api/health` (connectivity check and Settings > Test Connection), `GET /api/documents`,
`GET /api/conversations` and `POST /api/search`; its client also wraps the by-id routes and
`GET /api/collections`.

- **Pairing:** paste the desktop token into the app's Settings. There is no QR pairing and no sync
  API.
- **Reaching the desktop:** the listener is loopback only, so the app connects through the Android
  emulator alias `http://10.0.2.2:9846` or, on a device, through `adb reverse tcp:9846 tcp:9846`
  and `http://localhost:9846`. LAN connections are not supported. See
  [`docs/MOBILE-TRANSPORT.md`](docs/MOBILE-TRANSPORT.md).

> Earlier revisions of this document described a different API (`http://localhost:5324/api/v1`,
> chat completions, document indexing, a `com.agentx.bridge` native messaging host, and pairing
> and sync routes). None of those exist; the routes above are the complete API.

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
| `timeout` | Request timeout | Yes, send again |
| `network_error` | Connection failed | Yes, send again |

---

## Retry Policy

Agent-X does not retry a failed provider request on its own. The error is reported where the
request was made (for example in the chat, which keeps the prompt so it can be sent again), and
the "Retry" column above says whether sending it again can help.

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

- The Local REST API listens on **localhost only** (no network exposure). It is plain HTTP on
  loopback, authenticated with a bearer token; there is no native messaging host.

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
