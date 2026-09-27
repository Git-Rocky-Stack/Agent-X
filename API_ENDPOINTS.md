# Agent-X API Endpoints

## Overview

This document lists every HTTP interface Agent-X has:

1. The **Local REST API** that the desktop app serves on this computer. The Agent-X browser
   extension (AgentX Web Clipper) and the Android companion app use it.
2. The **outside services** the desktop app calls. It calls them only for features you set up.

The code is the reference. The Local REST API lives in `src/AgentX.Core/Services/Api/`
(`ApiHostService`, `LocalApiSecurity`, `Models/ApiModels.cs`, `Models/ApiClipModels.cs`) and is
started and stopped by `src/AgentX.App/Services/ApiHostLifecycleService.cs`.

---

## Local REST API

`ApiHostService` is an `HttpListener` (HTTP.sys) host inside the desktop process. There is no
separate server, no ASP.NET Core and no native messaging host.

### Base URL

```
http://localhost:9846/
```

- **Loopback only.** The listener registers the HTTP.sys prefix `http://localhost:9846/` and is
  not reachable from other machines. Plain HTTP, no TLS.
- HTTP.sys answers a request whose `Host` header names another host with
  `400 Bad Request - Invalid Hostname` before Agent-X sees it. This is why the Android app sends
  `Host: localhost:9846` when it connects through the emulator alias `10.0.2.2`
  (see [`docs/MOBILE-TRANSPORT.md`](docs/MOBILE-TRANSPORT.md)).
- The port is fixed (`ApiHostLifecycleService.DefaultPort`); there is no setting for it.

### Turning the API on and off

- The switch is **Settings > Connections > Enable Local API** (`AppSettings.LocalApiEnabled`,
  on by default).
- At launch the API starts after the database migration has succeeded (`StartupOrchestrator`),
  and only when the switch is on. If the migration fails, the API is not started. If the listener
  cannot start, for example because another program uses port 9846, the error is written to the
  log and the rest of the app runs without the API.
- Changes apply without a restart. **Save Settings** calls
  `IApiHostLifecycleService.ApplySettingsAsync`, which stops the listener when the switch is off,
  starts it when the switch is on and the listener is not running, and otherwise hands the running
  listener the current token. **Reset to Defaults** turns the API on and keeps the existing token.
- The listener stops when the app shuts down.

### Authentication

Every route except `GET /api/extension/health` requires the bearer token:

```
Authorization: Bearer <token>
```

- The token is a per-install random 256-bit value written as 64 uppercase hexadecimal characters
  (`LocalApiSecurity.GenerateToken`). It is created the first time the API starts with no token
  saved, and stored DPAPI-encrypted in `%LOCALAPPDATA%\AgentX\settings.json`
  (`AppSettings.LocalApiToken`).
- **Settings > Connections** shows it under **API Token**, masked until you click **Show**.
  **Copy** copies the real token and **Regenerate** replaces it. The token row is shown only while
  Enable Local API is on.
- **Regenerate** saves the new token and applies it at once through
  `IApiHostService.SetAuthToken`, without Save Settings and without a restart. Requests that arrive
  afterwards need the new token and the old one is rejected, so paired clients have to be paired
  again.
- The scheme word `Bearer` is matched without regard to case and spaces around the token are
  ignored; the token itself must match exactly. The comparison runs in constant time
  (`LocalApiSecurity.IsAuthorized`).
- A missing or wrong token gets `401` with the header `WWW-Authenticate: Bearer`. If no token is
  set, every protected route returns `401` (fail closed).
- The token check runs before routing: without a valid token, every request returns `401`,
  unknown paths included, except a preflight `OPTIONS` request (see [CORS](#cors)) and requests
  for `/api/extension/health`.
- Clients check a token with `GET /api/auth/check`. The public `GET /api/extension/health`
  answers whatever token is sent, so it must not be used to decide that a client is paired.

### Requests

- Paths are matched without regard to case, a trailing slash is ignored, and the query string is
  ignored (no route takes query parameters).
- An unknown path, or a known path with the wrong method, returns `404` with the error
  `Route not found: <METHOD> <path>`.
- Request bodies are JSON. Field names are camelCase and case-sensitive (`Query` is not read as
  `query`), and unknown fields are ignored. The body is decoded with the charset named in
  `Content-Type`, or as UTF-8 when none is named; the `Content-Type` value is not otherwise
  checked.
- At most 16 requests are processed at the same time; further requests wait for a free slot.
- There is no rate limit and no size limit on request bodies.
- Every request is logged as `<METHOD> <path> -> <status> (<n>ms)`.

### CORS

Only browser-extension origins get a CORS grant. When the `Origin` header starts with
`chrome-extension://`, `moz-extension://` or `ms-browser-extension://`, the response echoes it
in `Access-Control-Allow-Origin` and adds `Vary: Origin`,
`Access-Control-Allow-Methods: GET, POST, OPTIONS`,
`Access-Control-Allow-Headers: Content-Type, Authorization, Accept, X-Requested-With` and
`Access-Control-Max-Age: 86400`. Web pages get none of these headers, so the browser does not let
them read a response. A preflight `OPTIONS` request on any path returns `204` without a token.

### Response envelope

Every response body, success or error, is JSON (`application/json; charset=utf-8`) with camelCase
names, wrapped in the same envelope (`ApiResponse<T>`). Properties whose value is null are left
out: `error` is absent on success, `data` is absent on error, and a null field inside `data` is
absent too.

```json
{ "success": true, "data": { "authenticated": true, "version": "2.2.0" }, "timestamp": "2026-09-27T10:15:30.1234567Z" }
```

```json
{ "success": false, "error": "Unauthorized. A valid API token is required. Pair the client with the token from AgentX Settings.", "timestamp": "2026-09-27T10:15:30.1234567Z" }
```

`timestamp` is the server's UTC time. Dates inside `data` are UTC as well; a date read from the
database can be written without a zone designator (for example `2026-09-20T08:30:00`), so read
such values as UTC.

| Status | When |
|--------|------|
| `200` | Success |
| `201` | `POST /api/inbox/clip` created an inbox item |
| `204` | CORS preflight (`OPTIONS`) |
| `400` | The body is not valid JSON for the route (including a value of the wrong type), or a required field is missing or blank |
| `401` | Missing or wrong bearer token |
| `404` | Unknown route, wrong method, non-numeric id, or an id that does not exist |
| `500` | Unexpected server error (`An internal server error occurred.`), or the Smart Inbox did not accept a clip (`Failed to add clip to inbox.`) |

### Endpoints

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| `GET` | `/api/extension/health` | none | Tells the extension that Agent-X is running |
| `GET` | `/api/auth/check` | bearer | Confirms a token (pairing) |
| `GET` | `/api/health` | bearer | Status, version, uptime and counts |
| `GET` | `/api/documents` | bearer | All documents in the Knowledge Vault |
| `GET` | `/api/documents/{id}` | bearer | One document |
| `GET` | `/api/conversations` | bearer | All conversations that are not archived |
| `GET` | `/api/conversations/{id}` | bearer | One conversation |
| `GET` | `/api/collections` | bearer | All collections |
| `POST` | `/api/search` | bearer | Semantic search over indexed documents |
| `POST` | `/api/inbox/clip` | bearer | Saves clipped web content to the Smart Inbox |

No other routes exist. The API has no paging, filtering, chat, import, sync or pairing routes, and
the clip is its only write operation.

#### GET /api/extension/health

Public (no token). Returns no user data.

```json
{
  "success": true,
  "data": {
    "connected": true,
    "version": "2.2.0",
    "inboxEnabled": true,
    "provider": "local"
  },
  "timestamp": "2026-09-27T10:15:30.1234567Z"
}
```

- `version` is the application version (`AppVersionInfo.Display`).
- `provider` is the provider chosen in Settings (`AppSettings.ActiveProviderId`): `local` for the
  built-in model, `ollama`, `openai` or `anthropic`. It is the saved choice, not a check that the
  provider is reachable.
- `connected` and `inboxEnabled` are always `true`. The Smart Inbox has no off switch, so the clip
  route is available whenever this probe answers.

#### GET /api/auth/check

Answers only when the bearer token is valid; otherwise the request gets `401`.

```json
{ "success": true, "data": { "authenticated": true, "version": "2.2.0" }, "timestamp": "2026-09-27T10:15:30.1234567Z" }
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
  "timestamp": "2026-09-27T10:15:30.1234567Z"
}
```

- `uptime` is the time since the listener last started, as `<hours>h <minutes>m <seconds>s`
  (the hours are not wrapped at 24). Turning the API off and on starts it again.
- `documentCount` counts every document; `conversationCount` counts conversations that are not
  archived.

#### GET /api/documents

Every document in the Knowledge Vault, newest import first. No document text is returned.

```json
{
  "success": true,
  "data": [
    {
      "id": 12,
      "fileName": "report.pdf",
      "fileType": "pdf",
      "fileSizeBytes": 204800,
      "importedAt": "2026-09-20T08:30:00",
      "indexingStatus": "completed"
    }
  ],
  "timestamp": "2026-09-27T10:15:30.1234567Z"
}
```

- `fileType` is the lower-case file extension without the dot, or a type name such as
  `CalendarEvent` or `EmailMessage` for items that came from the connectors.
- `indexingStatus` is `pending`, `processing`, `completed` or `failed`.

#### GET /api/documents/{id}

`{id}` is the numeric document id. Returns one object with the fields above. A missing document
returns `404` with `Document {id} not found.`; an id that is not a number returns the route
`404`.

#### GET /api/conversations

Conversations that are not archived, pinned ones first, then the most recently updated. Messages
are not included.

```json
{
  "success": true,
  "data": [
    {
      "id": 5,
      "title": "Planning",
      "modelId": "llama3.2",
      "createdAt": "2026-09-20T08:30:00",
      "updatedAt": "2026-09-21T10:00:00",
      "messageCount": 12,
      "tokensUsed": 3400
    }
  ],
  "timestamp": "2026-09-27T10:15:30.1234567Z"
}
```

#### GET /api/conversations/{id}

One conversation with the fields above; archived conversations are returned too. A missing
conversation returns `404` with `Conversation {id} not found.`

#### GET /api/collections

All collections as one flat list, nested collections included (the parent is not reported),
ordered by their sort order and then by name. `documentCount` is refreshed before the list is
returned, and `description` is left out when it is null.

```json
{
  "success": true,
  "data": [
    { "id": 1, "name": "Finance", "description": "Quarterly reports", "documentCount": 4, "createdAt": "2026-09-01T12:00:00" }
  ],
  "timestamp": "2026-09-27T10:15:30.1234567Z"
}
```

#### POST /api/search

Semantic (vector) search over the indexed chunks. There is no keyword or hybrid mode and no
collection, file type or date filter on this route.

```json
{ "query": "vector databases", "topK": 10, "minScore": 0.3 }
```

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| `query` | string | (required) | Missing, empty or whitespace returns `400` (`Request body must include a non-empty 'query' field.`) |
| `topK` | integer | `10` | Clamped to 1-50 |
| `minScore` | number | `0.3` | Minimum cosine similarity, clamped to 0.0-1.0 |

```json
{
  "success": true,
  "data": [
    { "documentId": 3, "fileName": "a.pdf", "chunkContent": "The matching chunk text...", "score": 0.91 }
  ],
  "timestamp": "2026-09-27T10:15:30.1234567Z"
}
```

- There is one entry per matching chunk, highest score first, so a document can appear more than
  once. `chunkContent` is the full text of the chunk and `score` is its cosine similarity. This is
  the only route that returns document text.
- The query is embedded with the configured embedding model. If it cannot be embedded (for
  example no embedding model is available) or the vector search fails, the route still returns
  `200` with an empty list; the cause is written to the log only.

#### POST /api/inbox/clip

Saves clipped web content as a Markdown file and adds it to the Smart Inbox as a pending item with
the source type `browser-extension` and the given source URL.

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
| `content` | string | Required. Missing, empty or whitespace returns `400` (`Request body must include a non-empty 'content' field.`) |
| `title` | string | Optional. Missing or blank becomes `Untitled` in the front matter and `untitled` in the file name |
| `sourceUrl` | string | Optional. Stored with the inbox item and as `source_url` (empty when missing) |
| `author` | string | Optional. Written only when it is not blank |
| `publishedDate` | string or number | Optional. Parsed leniently (see below); a value that cannot be read is dropped and never fails the clip |
| `clipMode` | string | `full`, `selection` (the default) or `reader`. Another value is kept, written as a quoted string |
| `wordCount` | integer | Optional, default `0`. A negative value is written as `0` |
| `metadata` | object of strings | Optional. Each pair becomes an extra front matter key. Blank keys, null values, keys the host writes itself (`title`, `source_url`, `author`, `published_date`, `clip_mode`, `word_count`, `clipped_at`) and repeated keys are skipped, compared without regard to case |

A field of the wrong JSON type (for example `"wordCount": "1234"`, or a number as a `metadata`
value) makes the body invalid and returns `400` (`Invalid JSON in request body.`). The one
exception is `publishedDate`, which accepts a string or a number and treats any other type as no
date.

**Published date.** `ApiHostService.ParsePublishedDate` reads the value only when it is at most
64 characters long and contains a four-digit year. It accepts the compact form `yyyyMMdd`
(`20240305`, also as a JSON number) and whatever `DateTimeOffset.TryParse` reads with the
invariant culture, for example:

- ISO 8601 with or without the colon in the offset: `2024-03-05T10:00:00+0000`
- date and time separated by a space: `2024-03-05 10:00:00`
- year and month: `2024-03`, read as the first day of that month
- RFC 1123: `Tue, 05 Mar 2024 10:00:00 GMT`

The calendar date is kept as the page states it, in the page's own offset
(`2024-03-05T23:30:00-08:00` stays `2024-03-05`); a value without an offset is read as UTC. Only
the date is written, as `published_date: "yyyy-MM-dd"`.

**Storage.** The file is written to `%LOCALAPPDATA%\AgentX\Clips\` (the app data folder from
`IAppPathService`, not `%TEMP%`) as

```
<title stem>-<yyyyMMdd-HHmmss>-<8 hex characters>.md
```

- The title stem is the title with characters that Windows does not allow in file names, and
  control characters, replaced by `_`; runs of `_` are collapsed and leading and trailing `_` are
  removed. It is cut to at most 80 characters (never inside a surrogate pair), and trailing spaces,
  dots and underscores are then trimmed. When nothing is left the stem is `untitled`.
- The timestamp is the UTC clip time and the suffix is random, so several clips with the same
  title in the same second (Clip All Tabs) each get their own file. The file is created with
  `FileMode.CreateNew` and never replaces an existing file.
- The file is UTF-8 without a byte order mark. `content` is written as sent; there is no size
  limit.
- Agent-X does not delete clip files. Accepting a clip in the Smart Inbox imports the file into the
  Knowledge Vault, and neither accepting nor rejecting removes it from the folder.

Every front matter value is an escaped YAML double-quoted string (backslashes, quotes, line
breaks, tabs, control characters, U+2028, U+2029 and U+FEFF are escaped), metadata keys are quoted
the same way, and dates and numbers use the invariant culture and the Gregorian calendar.
`clip_mode` is written bare for the three known modes:

```yaml
---
title: "Example Article"
source_url: "https://example.com/post"
author: "Jane Doe"
published_date: "2024-03-05"
clip_mode: reader
word_count: 1234
clipped_at: "2026-09-27T10:15:30.1234567Z"
"category": "tech"
---

The clipped text, as Markdown or plain text.
```

Response (`201`):

```json
{
  "success": true,
  "data": { "inboxItemId": 12, "status": "clipped", "message": "Content clipped to inbox as item #12." },
  "timestamp": "2026-09-27T10:15:30.1234567Z"
}
```

If the Smart Inbox does not accept the item, the file is deleted and the route returns `500` with
`Failed to add clip to inbox.` If the file cannot be written, the route returns `500` with
`An internal server error occurred.`

---

## Browser Extension

The extension (`browser-extension/`, Manifest V3, "AgentX Web Clipper") calls the Local REST API
from its service worker at `http://localhost:9846` (host permission `http://localhost:9846/*`).

| Route | When |
|-------|------|
| `GET /api/extension/health` | Each time the popup opens, and after pairing, to see whether Agent-X answers |
| `GET /api/auth/check` | To validate a pasted token before storing it, and to check the stored token each time the popup opens |
| `POST /api/inbox/clip` | **Full Page** (`clipMode` `full`), **Selection** (`selection`), **Reader Mode** (`reader`), and **Clip All Tabs** (`reader`, one request per tab) |

- **Pairing:** paste the token from **Settings > Connections** into the popup's Connection section
  and click **Save**. A token that Agent-X rejects is not stored; when Agent-X does not answer, the
  token is stored as not verified. Saving an empty field unpairs the extension.
- **Status line:** the Agent-X version when the stored token is accepted, "Not paired" when Agent-X
  answers but rejects the token, "Offline" when it does not answer.
- **What a clip sends:** `title`, `content`, `sourceUrl`, `author`, `publishedDate`, `clipMode` and
  `wordCount`. The extension normalizes `publishedDate` to `YYYY-MM-DD` itself, or leaves it out,
  and never sends `metadata`.
- The token is kept in `chrome.storage.local` and used only by the service worker. The page
  extractor is injected on demand with `chrome.scripting` and never reads the token.
- Clips are not queued: when Agent-X is not running, a clip fails and has to be sent again later.

---

## Android Companion

The Android app (`src/AgentX.Mobile`, client `Services/AgentXApiClient.cs`) uses the same routes
and the same bearer token.

| Route | Used by |
|-------|---------|
| `GET /api/health` | **Settings > Test Connection**, which probes the URL and token on screen with a separate client and shows the version, uptime and counts |
| `GET /api/documents` | Documents tab |
| `GET /api/conversations` | Conversations tab |
| `POST /api/search` | Search tab, with `topK` 20 and `minScore` 0.3 |

The client also has methods for `GET /api/documents/{id}`, `GET /api/conversations/{id}` and
`GET /api/collections`, but no page calls them.

- **Pairing:** paste the desktop token into the app's **Settings > API Token** and tap **Save**.
  The token is kept in Android secure storage. There is no QR pairing and no sync API.
- **Reaching the desktop:** the listener is loopback only, so the app connects through the
  emulator alias `http://10.0.2.2:9846` (sending `Host: localhost:9846`) or, on a phone, through
  `adb reverse tcp:9846 tcp:9846` and `http://localhost:9846`. The client refuses plain HTTP to any
  other host and requires HTTPS there, which the desktop does not serve, so LAN connections are not
  supported. See [`docs/MOBILE-TRANSPORT.md`](docs/MOBILE-TRANSPORT.md).
- Requests time out after 15 seconds. A `401` or `403` is shown as not paired, no answer as
  "Cannot reach Agent-X", and any other error status with the envelope's `error` text.

---

## Outbound Connections

Agent-X connects to outside services only for the features below, and only after you set them
up. It sends no telemetry: `IAnalyticsService` only reads the local database for the Analytics
page.

### AI providers

`AiService.InitializeAsync` registers the providers from the saved settings:

| Provider id | What it is | Default endpoint | Registered when | Authentication |
|-------------|-----------|------------------|-----------------|----------------|
| `local` | Built-in model (LLamaSharp, runs in the app process) | none | Always | none |
| `ollama` | Ollama server | `http://localhost:11434` | The endpoint is an absolute `http` or `https` URL | none |
| `openai` | OpenAI or an OpenAI-compatible server | `https://api.openai.com/v1/` | An API key is saved | `Authorization: Bearer <key>` |
| `anthropic` | Anthropic | `https://api.anthropic.com/v1/` | An API key is saved | `x-api-key: <key>` and `anthropic-version: 2023-06-01` |

The active provider is `AppSettings.ActiveProviderId` (default `local`). When that provider is not
registered, the built-in model is used if its file is installed, otherwise Ollama, otherwise any
registered provider. Endpoints and keys are set in **Settings > AI Providers**; the keys are
stored DPAPI-encrypted in `settings.json`.

**Built-in model (`local`).** No network traffic while it runs. Model files are GGUF files in
`%LOCALAPPDATA%\AgentX\Models` (`AppSettings.StoragePath` + `Models`). Downloads are listed under
[Model downloads](#model-downloads).

**Ollama (`ollama`)**, through OllamaSharp 4.0.6:

| Operation | Request |
|-----------|---------|
| Connection check (3 second timeout) | `IsRunningAsync`, a `GET` on the server root |
| List models | `GET /api/tags` |
| Download and delete a model | `POST /api/pull`, `DELETE /api/delete` |
| Chat | `POST /api/chat`, streamed. A stream that ends without its final `done` chunk throws, so a cut-off answer is not reported as complete |
| Embeddings | `POST /api/embed`, with the embedding model named in the request |

Token counts from the final chunk are recorded in the cost tracker at zero cost.

**OpenAI (`openai`)**, requests relative to the endpoint:

| Operation | Request |
|-----------|---------|
| Connection check (10 second timeout) and model list | `GET models`. The list keeps chat models only |
| Chat | `POST chat/completions`, always streamed (`"stream": true`) |
| Embeddings | `POST embeddings` with `{ "model", "input" }`, only when the Embedding Model setting is an OpenAI `text-embedding-*` model |

- Reasoning models (ids starting with `o1`, `o3`, `o4` or `gpt-5`) get `max_completion_tokens`
  and no sampling or penalty parameters. Other models get `max_tokens` and `temperature`; `top_p`
  only when it lies between 0 and 1 (the options default of 0.9 is sent), and
  `frequency_penalty` and `presence_penalty` only when they are not 0.
- `stream_options.include_usage` is sent only when the endpoint host is `api.openai.com`, because
  compatible servers may reject it.
- JSON mode sends `response_format` `json_object`, or `json_schema` with `strict: true` when the
  caller supplies a schema.
- Downloading or deleting a model does nothing for this provider.

**Anthropic (`anthropic`)**, requests relative to the endpoint:

| Operation | Request |
|-----------|---------|
| Connection check (10 second timeout) | `GET models?limit=1`. A `200` or a `429` counts as a working key; no tokens are generated |
| Model list | `GET models?limit=100`, falling back to a built-in list (`claude-opus-5-5`, `claude-sonnet-5`, `claude-haiku-4-5-20251001`) when the request fails |
| Chat | `POST messages`, always streamed |

- `max_tokens` comes from the request options (default 2048). Only `temperature` is ever sent,
  clamped to 0-1, and only to model families known to accept it; `top_p` is never sent.
- Structured output uses forced tool use; models that reject forced tool use get JSON mode with
  the schema in the instructions instead.
- Anthropic has no embedding API: the provider throws `NotSupportedException` for embeddings and
  is never chosen as the embedding provider.
- An `event: error` that arrives after the HTTP 200 throws with the error type and message.

### Embeddings

The embedding provider is chosen independently of the chat provider
(`EmbeddingTargetResolver.Resolve`, from the **Embedding Model** setting):

1. An OpenAI model id (`text-embedding-*`) uses OpenAI. This is the only case in which document
   text is sent to a cloud service for embedding.
2. A GGUF file name uses the built-in model.
3. Any other name except the default `all-minilm` uses Ollama with that model.
4. The default setting uses the built-in model when its file is installed, and Ollama with
   `all-minilm` when it is not.

### Research Mode web search

Used only for chat messages sent with the chat's Research mode button on, and only while
**Settings > Research Mode > Enable Research Mode** is on and a provider is configured. Only the
selected **Search Provider** is used; there is no fallback to another provider.

| Provider | Request | Credential (the **API Key or Instance URL** field) |
|----------|---------|------------------|
| Brave (default) | `GET https://api.search.brave.com/res/v1/web/search?q=<query>&count=<n>` | API key in the `X-Subscription-Token` header |
| Serper | `POST https://google.serper.dev/search` with `{ "q": "<query>", "num": <n> }` | API key in the `X-API-KEY` header |
| SearXNG | `GET <instance URL>/search?q=<query>&format=json&pageno=1` | The instance URL itself (http or https) |

The field is stored DPAPI-encrypted (`AppSettings.WebSearchApiKey`). **Max Search Results**
(default 10, at most 20) caps the results, and results are cached for **Cache Duration (minutes)**
(default 60, at most 1440). Settings changes apply to the next search
(`SettingsAwareWebSearchService`).

### Web Import

Web Import fetches the pages, feeds and sitemaps you give it directly; no third-party scraping
service is involved.

- `IWebScraperService.ExtractContentAsync` downloads a page and extracts its content with
  HtmlAgilityPack. When the download returns an empty page or a script shell with little visible
  text, `WebContentFetcher` renders the page again with `IJsRenderingService.RenderPageAsync`, in
  headless Chromium through Microsoft.Playwright; if Chromium cannot be started, the downloaded
  HTML is used.
- YouTube links: `ExtractYouTubeTranscriptAsync` reads the watch page
  (`https://www.youtube.com/watch?v=<id>`) and downloads the caption track it lists, English
  preferred.
- A URL that Agent-X finds in remote content (a redirect, a feed item, a sitemap entry, a resource
  a rendered page requests) may reach a private or local address only when that content itself came
  from such an address, and cloud metadata addresses are never contacted (`PrivateNetworkGuard`).
  A URL you enter yourself may point at any host.

### OAuth and the Calendar and Email connectors

Agent-X ships no OAuth client. You enter your own under **OAuth App Credentials** on the Calendar
Connector and Email Connector pages; the client secrets are stored DPAPI-encrypted.

| | Google | Microsoft |
|--|--------|-----------|
| Authorization | `https://accounts.google.com/o/oauth2/v2/auth` | `https://login.microsoftonline.com/<tenant>/oauth2/v2.0/authorize` |
| Token and refresh | `https://oauth2.googleapis.com/token` | `https://login.microsoftonline.com/<tenant>/oauth2/v2.0/token` |
| Revocation | `https://oauth2.googleapis.com/revoke` | none (Disconnect deletes the local tokens only) |
| Redirect URI (default) | `http://localhost:8400/oauth/callback` | `http://localhost:8401/oauth/callback` |
| Tenant | not used | `common` by default |
| Extra parameters | `access_type=offline`, `prompt=consent` | `prompt=select_account` |

- The sign-in opens the system browser and waits for the redirect on the loopback URI. It uses
  PKCE (`code_challenge_method=S256`) and a one-time `state` value, and only `http://localhost:`
  or `http://127.0.0.1:` redirect URIs are accepted.
- `client_secret` is sent only when one is set. A Microsoft app registered as a public client has
  no secret.
- Tokens are stored DPAPI-encrypted in the database and refreshed 5 minutes before they expire
  (`OAuthSettings.TokenRefreshBufferMinutes`). The browser sign-in times out after 300 seconds
  (`OAuthSettings.AuthTimeoutSeconds`).
- There is one stored credential per provider, shared by both connectors, so **Disconnect** on
  either page signs that account out of both.

**Scopes.** Each provider has default scopes (`OAuthProviderRegistry`), and `OAuthService`
merges the scopes a Connect button asks for into them, so one sign-in covers both connectors:

| Provider | Default scopes | Added by Calendar Connect | Added by Email Connect |
|----------|----------------|---------------------------|------------------------|
| Google | `openid profile email https://www.googleapis.com/auth/calendar.readonly https://www.googleapis.com/auth/gmail.readonly` | `https://www.googleapis.com/auth/userinfo.profile` | `https://www.googleapis.com/auth/userinfo.profile` |
| Microsoft | `openid profile email offline_access Calendars.Read Mail.Read User.Read` | nothing new | nothing new |

All requested access is read-only.

APIs the connectors call, each with `Authorization: Bearer <access token>`:

| Connector | Service | Requests |
|-----------|---------|----------|
| Calendar | Google Calendar API v3 | `GET https://www.googleapis.com/calendar/v3/users/me/calendarList`, `GET https://www.googleapis.com/calendar/v3/calendars/{calendarId}/events` |
| Calendar | Microsoft Graph v1.0 | `GET https://graph.microsoft.com/v1.0/me/calendars`, `GET .../me/calendars/{id}/calendarView` (times requested in UTC) |
| Email | Gmail API v1 | `https://gmail.googleapis.com/gmail/v1/users/me/labels`, `.../messages`, `.../messages/{id}?format=full`, `.../profile` and `.../history` (incremental sync from the stored history id) |
| Email | Microsoft Graph v1.0 | `GET https://graph.microsoft.com/v1.0/me/mailFolders`, `GET .../me/mailFolders/{id}/messages/delta` (incremental sync from the stored delta link) |

### Model downloads

Downloads start only when you ask for them, in first-run setup or on the Model Manager page:

| Model | Source |
|-------|--------|
| Built-in chat model, Llama 3.2 3B Instruct Q4_K_M (default) | `https://huggingface.co/hugging-quants/Llama-3.2-3B-Instruct-Q4_K_M-GGUF/resolve/main/llama-3.2-3b-instruct-q4_k_m.gguf` |
| Llama 3.2 1B Instruct Q4_K_M (pulled by its file name on the Model Manager page while the built-in provider is active) | `https://huggingface.co/hugging-quants/Llama-3.2-1B-Instruct-Q4_K_M-GGUF/resolve/main/llama-3.2-1b-instruct-q4_k_m.gguf` |
| Speech-to-text model, Whisper base (Model Manager, **Speech-to-Text Model**) | `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin` |

`TranscriptionService` also knows the `tiny`, `small`, `medium` and `large` (`ggml-large-v3.bin`)
files from the same repository, but the app only asks for `base`. The built-in model downloads
are checked against a minimum size (`BuiltInModelCatalog`); no SHA-256 hash is pinned yet, and the
URLs follow the publisher's `main` branch. Ollama models are downloaded by the Ollama server
(`POST /api/pull`), not by Agent-X.

### Errors, retries and usage costs

- A provider request that fails throws: a non-success HTTP status becomes an
  `HttpRequestException` that carries the provider's error text, and an error reported inside a
  stream throws as well. The error is shown where the request was made, for example in the chat,
  which keeps the prompt so it can be sent again.
- Agent-X does not retry a failed request and does no rate limiting of its own; the provider's
  own limits and error messages apply.
- Token usage reported by Ollama, OpenAI and Anthropic is recorded through `ICostTracker`
  (`src/AgentX.Core/AI/Models/CostTracker.cs`):

```csharp
public interface ICostTracker
{
    void RecordUsage(string modelId, string providerId, int inputTokens, int outputTokens);
    void RecordUsage(string modelId, string providerId, int inputTokens, int outputTokens,
        int cacheCreationInputTokens, int cacheReadInputTokens);
    double GetTotalCostUsd();
    double GetCostForPeriod(DateTime start, DateTime end);
    IReadOnlyList<UsageRecord> GetUsageHistory(int limit = 50);
    int GetTotalInputTokens();
    int GetTotalOutputTokens();
}
```

Usage is kept in `%LOCALAPPDATA%\AgentX\usage-history.json`: each record for 90 days (at most
20,000 records), with the totals of dropped records carried forward. Costs are estimates from a
built-in price table, matched by the longest model id prefix, so a dated id such as
`gpt-4o-mini-2024-07-18` is priced as `gpt-4o-mini`. The built-in model and Ollama cost nothing,
and a model not in the table is recorded at zero cost. Prompt-cache writes cost 1.25 times the
input price; prompt-cache reads cost the model's cache-read price, or 10% of the input price when
none is listed.

| Model id prefix | Input (USD per 1M tokens) | Output (USD per 1M tokens) |
|-----------------|---------------------------|----------------------------|
| `gpt-4o` | 2.50 | 10.00 |
| `gpt-4o-mini` | 0.15 | 0.60 |
| `gpt-4-turbo` | 10.00 | 30.00 |
| `o1` | 15.00 | 60.00 |
| `o1-mini` | 3.00 | 12.00 |
| `o3-mini` | 1.10 | 4.40 |
| `claude-fable-5-1` (cache read 0.25) | 10.00 | 50.00 |
| `claude-fable-5` | 10.00 | 50.00 |
| `claude-opus-5-5` (cache read 0.20) | 4.00 | 20.00 |
| `claude-opus-5`, `claude-opus-4-8`, `claude-opus-4-7`, `claude-opus-4-6`, `claude-opus-4-5` | 5.00 | 25.00 |
| `claude-opus-4-1`, `claude-opus-4-0`, `claude-opus-4-20250514` | 15.00 | 75.00 |
| `claude-sonnet-5` | 2.00 | 10.00 |
| `claude-sonnet-4-6`, `claude-sonnet-4-5`, `claude-sonnet-4-0`, `claude-sonnet-4-20250514`, `claude-3-5-sonnet-20241022` | 3.00 | 15.00 |
| `claude-haiku-4-5` | 1.00 | 5.00 |
| `claude-3-5-haiku-20241022` | 0.80 | 4.00 |

---

## Logging

The desktop app writes its log with Serilog to `%LOCALAPPDATA%\AgentX\Logs\agentx-<yyyyMMdd>.log`:
one file per day, the last 7 kept, at Debug level and above. The Local REST API logs one line per
request (see [Requests](#requests)); provider calls, connection checks and failures are logged by
each provider.

---

## Security Summary

- **Secrets at rest:** the OpenAI and Anthropic API keys, the web search key or SearXNG URL, the
  Local API token, the OAuth client secrets and the scheduled backup password are stored
  DPAPI-encrypted in `settings.json` (`SettingsService`, `IDpapiEncryptionService`). OAuth tokens
  are stored DPAPI-encrypted in the database.
- **Local REST API:** loopback only, plain HTTP, a bearer token on every route except the public
  health probe, and CORS grants for browser-extension origins only.
- **Outbound transport:** the cloud providers, web search APIs, OAuth endpoints, connector APIs and
  model downloads use HTTPS at their default addresses. The Ollama endpoint, a SearXNG URL and a
  custom OpenAI-compatible endpoint can be plain `http` when you enter them that way. The desktop
  code does not change .NET's default certificate validation.

---

## Plugins

Plugins are .NET assemblies loaded into the app with a `manifest.json`; they are not an HTTP
interface. See [`docs/PLUGIN-DEVELOPMENT-GUIDE.md`](docs/PLUGIN-DEVELOPMENT-GUIDE.md).

---

**Last updated:** 2026-09-27
