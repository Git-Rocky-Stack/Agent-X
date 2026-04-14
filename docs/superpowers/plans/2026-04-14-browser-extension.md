# Browser Extension Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Chrome/Edge extension that clips web content into Agent-X's Smart Inbox via local REST API, enabling one-click knowledge capture from any web page.

**Architecture:** Manifest V3 extension with popup UI, content script for page extraction, and background service worker. Clips are sent to Agent-X's existing `ApiHostService` (localhost:9846) via a new `/api/inbox/clip` endpoint. The extension supports three clip modes: full page, selection, and reader mode. Metadata (title, author, date, URL) is preserved.

**Tech Stack:** TypeScript, Manifest V3, Chrome Extension API, Agent-X REST API (HttpListener), C# .NET 8

---

### Task 1: Add Inbox Clip Endpoint to ApiHostService

**Files:**
- Modify: `src/AgentX.Core/Services/Api/ApiHostService.cs`
- Modify: `src/AgentX.Core/Services/Api/IApiHostService.cs`
- Create: `src/AgentX.Core/Services/Api/Models/ApiClipModels.cs`
- Test: `tests/AgentX.Tests/Services/Api/InboxClipEndpointTests.cs`

- [ ] **Step 1: Write the API request/response models**

```csharp
// src/AgentX.Core/Services/Api/Models/ApiClipModels.cs
using System.Text.Json.Serialization;

namespace AgentX.Core.Services.Api.Models;

public class ApiClipRequest
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("sourceUrl")]
    public string SourceUrl { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("publishedDate")]
    public string? PublishedDate { get; set; }

    [JsonPropertyName("clipMode")]
    public string ClipMode { get; set; } = "full"; // "full", "selection", "reader"

    [JsonPropertyName("wordCount")]
    public int WordCount { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}

public class ApiClipResponse
{
    [JsonPropertyName("inboxItemId")]
    public long InboxItemId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "Clipped to Smart Inbox";
}
```

- [ ] **Step 2: Write the failing test**

```csharp
// tests/AgentX.Tests/Services/Api/InboxClipEndpointTests.cs
using AgentX.Core.Services.Api;
using AgentX.Core.Services.Api.Models;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.Settings;
using Moq;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace AgentX.Tests.Services.Api;

public class InboxClipEndpointTests
{
    [Fact]
    public async Task ClipEndpoint_ValidRequest_ReturnsCreatedItem()
    {
        // Arrange
        var inboxService = new Mock<IInboxService>();
        inboxService.Setup(x => x.AddToInboxAsync(It.IsAny<string>(), It.IsAny<long?>()))
            .ReturnsAsync((1L, "pending"));

        var settingsService = new Mock<ISettingsService>();
        var apiService = new ApiHostService(inboxService.Object, settingsService.Object);

        var request = new ApiClipRequest
        {
            Title = "Test Article",
            Content = "This is the article content.",
            SourceUrl = "https://example.com/article",
            Author = "Jane Doe",
            ClipMode = "full",
            WordCount = 6
        };

        var json = JsonSerializer.Serialize(request);

        // Act
        await apiService.StartAsync(9847); // Use different port for testing
        try
        {
            using var client = new HttpClient();
            var response = await client.PostAsync(
                "http://localhost:9847/api/inbox/clip",
                new StringContent(json, Encoding.UTF8, "application/json"));

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ApiClipResponse>(body);
            Assert.Equal("pending", result?.Status);
        }
        finally
        {
            await apiService.StopAsync();
        }
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests --filter "FullyQualifiedName~InboxClipEndpointTests" -v n`
Expected: FAIL — `/api/inbox/clip` route does not exist yet.

- [ ] **Step 4: Add the clip endpoint to ApiHostService**

Read `src/AgentX.Core/Services/Api/ApiHostService.cs`. In the `RouteAsync` method, add a new route handler:

```csharp
// In RouteAsync, add before the 404 fallback:
if (method == "POST" && path == "/api/inbox/clip")
{
    await HandleClipAsync(context, cancellationToken);
    return;
}
```

Add the handler method:

```csharp
private async Task HandleClipAsync(HttpListenerContext context, CancellationToken ct)
{
    using var reader = new StreamReader(context.Request.InputStream);
    var body = await reader.ReadToEndAsync(ct);

    var clipRequest = JsonSerializer.Deserialize<ApiClipRequest>(body, _jsonOptions);
    if (clipRequest == null || string.IsNullOrWhiteSpace(clipRequest.Content))
    {
        await WriteJsonResponseAsync(context, 400, new ApiResponse<object>(
            false, "Invalid clip request: content is required", null));
        return;
    }

    // Validate and sanitize input to prevent path traversal
    var sanitizedTitle = SanitizeFileName(clipRequest.Title);

    // Save clipped content to a temp file that InboxService can process
    var clipDir = Path.Combine(Path.GetTempPath(), "AgentX_Clips");
    Directory.CreateDirectory(clipDir);
    var fileName = sanitizedTitle + ".md";
    var filePath = Path.Combine(clipDir, fileName);

    // Write as markdown with frontmatter
    var sb = new StringBuilder();
    sb.AppendLine("---");
    sb.AppendLine($"title: {clipRequest.Title}");
    sb.AppendLine($"source: {clipRequest.SourceUrl}");
    if (!string.IsNullOrEmpty(clipRequest.Author))
        sb.AppendLine($"author: {clipRequest.Author}");
    if (!string.IsNullOrEmpty(clipRequest.PublishedDate))
        sb.AppendLine($"date: {clipRequest.PublishedDate}");
    sb.AppendLine($"clip-mode: {clipRequest.ClipMode}");
    sb.AppendLine($"word-count: {clipRequest.WordCount}");
    sb.AppendLine("---");
    sb.AppendLine();
    sb.AppendLine(clipRequest.Content);
    await File.WriteAllTextAsync(filePath, sb.ToString(), ct);

    // Add to Smart Inbox
    var (itemId, status) = await _inboxService.AddToInboxAsync(filePath, null);

    var response = new ApiClipResponse
    {
        InboxItemId = itemId,
        Status = status,
        Message = "Clipped to Smart Inbox"
    };

    await WriteJsonResponseAsync(context, 200, new ApiResponse<ApiClipResponse>(
        true, "Clip received", response));
}

private static string SanitizeFileName(string name)
{
    if (string.IsNullOrWhiteSpace(name)) name = "untitled";
    var invalid = Path.GetInvalidFileNameChars();
    return string.Join("_", name.Split(invalid)).Trim('_');
}
```

Also add `IInboxService` as a constructor dependency of `ApiHostService`.

- [ ] **Step 5: Run tests**

Run: `cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet test tests/AgentX.Tests --filter "FullyQualifiedName~InboxClipEndpointTests" -v n`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/AgentX.Core/Services/Api/ApiHostService.cs src/AgentX.Core/Services/Api/Models/ApiClipModels.cs tests/AgentX.Tests/Services/Api/InboxClipEndpointTests.cs
git commit -m "feat(api): add /api/inbox/clip endpoint for browser extension"
```

---

### Task 2: Add CORS and Health Endpoint for Extension Connectivity

**Files:**
- Modify: `src/AgentX.Core/Services/Api/ApiHostService.cs`

- [ ] **Step 1: Ensure CORS headers allow chrome-extension:// origin**

Read `src/AgentX.Core/Services/Api/ApiHostService.cs`. Update `WriteCorsHeaders()` to allow the `chrome-extension://` and `extension://` origins:

```csharp
private void WriteCorsHeaders(HttpListenerResponse response, string? origin = null)
{
    response.Headers.Add("Access-Control-Allow-Origin", origin ?? "*");
    response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
    response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-Extension-Version");
    response.Headers.Add("Access-Control-Max-Age", "86400");
}
```

Add OPTIONS preflight handler in `RouteAsync`:

```csharp
if (method == "OPTIONS")
{
    WriteCorsHeaders(context.Response, context.Request.Headers["Origin"]);
    context.Response.StatusCode = 204;
    context.Response.Close();
    return;
}
```

- [ ] **Step 2: Add extension-specific health endpoint**

Add `GET /api/extension/health` that returns extension-relevant info:

```csharp
if (method == "GET" && path == "/api/extension/health")
{
    var health = new
    {
        connected = true,
        version = "1.4.0",
        inboxEnabled = true,
        provider = _aiService?.ActiveProvider?.DisplayName ?? "none"
    };
    await WriteJsonResponseAsync(context, 200, new ApiResponse<object>(true, null, health));
    return;
}
```

- [ ] **Step 3: Commit**

```bash
git add src/AgentX.Core/Services/Api/ApiHostService.cs
git commit -m "feat(api): add CORS for browser extensions and extension health endpoint"
```

---

### Task 3: Browser Extension — Project Scaffold

**Files:**
- Create: `browser-extension/package.json`
- Create: `browser-extension/tsconfig.json`
- Create: `browser-extension/manifest.json`
- Create: `browser-extension/webpack.config.js`

- [ ] **Step 1: Create the extension project directory**

```bash
mkdir -p "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X/browser-extension/src"
```

- [ ] **Step 2: Create manifest.json (Manifest V3)**

```json
{
  "manifest_version": 3,
  "name": "Agent-X Web Clipper",
  "version": "1.0.0",
  "description": "Clip web content directly to your Agent-X knowledge vault",
  "permissions": ["activeTab", "scripting", "storage"],
  "host_permissions": ["http://localhost:9846/*"],
  "action": {
    "default_popup": "popup.html",
    "default_icon": {
      "16": "icons/icon16.png",
      "48": "icons/icon48.png",
      "128": "icons/icon128.png"
    }
  },
  "icons": {
    "16": "icons/icon16.png",
    "48": "icons/icon48.png",
    "128": "icons/icon128.png"
  },
  "background": {
    "service_worker": "background.js"
  },
  "content_scripts": [
    {
      "matches": ["<all_urls>"],
      "js": ["content.js"],
      "css": []
    }
  ]
}
```

- [ ] **Step 3: Create package.json**

```json
{
  "name": "agentx-web-clipper",
  "version": "1.0.0",
  "private": true,
  "scripts": {
    "build": "webpack --mode production",
    "dev": "webpack --mode development --watch",
    "lint": "tsc --noEmit"
  },
  "devDependencies": {
    "typescript": "^5.4.0",
    "webpack": "^5.90.0",
    "webpack-cli": "^5.1.0",
    "ts-loader": "^9.5.0",
    "copy-webpack-plugin": "^12.0.0",
    "@types/chrome": "^0.0.270"
  }
}
```

- [ ] **Step 4: Create tsconfig.json**

```json
{
  "compilerOptions": {
    "target": "ES2020",
    "module": "ESNext",
    "moduleResolution": "bundler",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "outDir": "./dist",
    "rootDir": "./src",
    "lib": ["ES2020", "DOM", "DOM.Iterable"]
  },
  "include": ["src/**/*"]
}
```

- [ ] **Step 5: Create webpack.config.js**

```javascript
const path = require('path');
const CopyPlugin = require('copy-webpack-plugin');

module.exports = {
  entry: {
    popup: './src/popup/popup.ts',
    background: './src/background/background.ts',
    content: './src/content/content.ts',
  },
  output: {
    path: path.resolve(__dirname, 'dist'),
    filename: '[name].js',
    clean: true,
  },
  resolve: {
    extensions: ['.ts', '.js'],
  },
  module: {
    rules: [
      {
        test: /\.ts$/,
        use: 'ts-loader',
        exclude: /node_modules/,
      },
    ],
  },
  plugins: [
    new CopyPlugin({
      patterns: [
        { from: 'manifest.json', to: 'manifest.json' },
        { from: 'src/popup/popup.html', to: 'popup.html' },
        { from: 'src/popup/popup.css', to: 'popup.css' },
        { from: 'icons', to: 'icons', noErrorOnMissing: true },
      ],
    }),
  ],
};
```

- [ ] **Step 6: Install dependencies and verify build**

```bash
cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X/browser-extension" && npm install && npm run build
```
Expected: Build succeeds (may have warnings for missing source files — that's fine, we'll add them next).

- [ ] **Step 7: Commit**

```bash
git add browser-extension/
git commit -m "feat(extension): scaffold browser extension project with Manifest V3"
```

---

### Task 4: Browser Extension — Content Script (Page Extraction)

**Files:**
- Create: `browser-extension/src/content/content.ts`
- Create: `browser-extension/src/content/extractors.ts`

- [ ] **Step 1: Create the content extractor**

```typescript
// browser-extension/src/content/extractors.ts

export interface PageData {
  title: string;
  content: string;
  author: string | null;
  publishedDate: string | null;
  wordCount: number;
  url: string;
  clipMode: 'full' | 'selection' | 'reader';
}

export function extractFullPage(): PageData {
  const title = document.title;
  const content = document.documentElement.outerHTML;
  return {
    title,
    content,
    author: extractAuthor(),
    publishedDate: extractPublishedDate(),
    wordCount: countWords(extractTextContent(content)),
    url: location.href,
    clipMode: 'full',
  };
}

export function extractSelection(): PageData | null {
  const selection = window.getSelection();
  if (!selection || selection.isCollapsed) return null;

  const content = selection.toString();
  return {
    title: document.title,
    content,
    author: extractAuthor(),
    publishedDate: extractPublishedDate(),
    wordCount: countWords(content),
    url: location.href,
    clipMode: 'selection',
  };
}

export function extractReaderMode(): PageData {
  const article = extractArticleContent();
  const content = articleToMarkdown(article);
  return {
    title: document.title,
    content,
    author: extractAuthor(),
    publishedDate: extractPublishedDate(),
    wordCount: countWords(content),
    url: location.href,
    clipMode: 'reader',
  };
}

function extractAuthor(): string | null {
  const metaAuthor = document.querySelector('meta[name="author"]');
  if (metaAuthor) return metaAuthor.getAttribute('content');

  const ogAuthor = document.querySelector('meta[property="article:author"]');
  if (ogAuthor) return ogAuthor.getAttribute('content');

  const schemaAuthor = document.querySelector('[itemprop="author"] [itemprop="name"]');
  if (schemaAuthor) return schemaAuthor.textContent;

  return null;
}

function extractPublishedDate(): string | null {
  const metaDate = document.querySelector('meta[property="article:published_time"]');
  if (metaDate) return metaDate.getAttribute('content');

  const timeTag = document.querySelector('time[datetime]');
  if (timeTag) return timeTag.getAttribute('datetime');

  return null;
}

function extractArticleContent(): string {
  const candidates = document.querySelectorAll('article, main, [role="main"], .post-content, .article-content, .entry-content');
  if (candidates.length > 0) {
    return candidates[0].textContent ?? '';
  }

  // Fallback: find the paragraph-dense section using textContent (safe, no innerHTML)
  const sections = document.querySelectorAll('section, div');
  let bestSection: Element | null = null;
  let maxParagraphs = 0;

  sections.forEach(section => {
    const paragraphs = section.querySelectorAll('p').length;
    if (paragraphs > maxParagraphs) {
      maxParagraphs = paragraphs;
      bestSection = section;
    }
  });

  return bestSection?.textContent ?? document.body.textContent ?? '';
}

function articleToMarkdown(textContent: string): string {
  // Since we use textContent (not innerHTML), we get clean text already.
  // Split into paragraphs and format.
  const paragraphs = textContent.split(/\n\s*\n/).filter(p => p.trim().length > 0);
  return paragraphs.join('\n\n');
}

function extractTextContent(html: string): string {
  const temp = document.createElement('div');
  temp.textContent = html; // Safe: uses textContent, not innerHTML
  return temp.textContent ?? '';
}

function countWords(text: string): number {
  return text.split(/\s+/).filter(w => w.length > 0).length;
}
```

- [ ] **Step 2: Create the content script message handler**

```typescript
// browser-extension/src/content/content.ts

import { extractFullPage, extractSelection, extractReaderMode, PageData } from './extractors';

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message.action === 'extractPage') {
    try {
      let data: PageData | null = null;

      switch (message.mode) {
        case 'full':
          data = extractFullPage();
          break;
        case 'selection':
          data = extractSelection();
          if (!data) {
            sendResponse({ error: 'No text selected' });
            return true;
          }
          break;
        case 'reader':
          data = extractReaderMode();
          break;
        default:
          data = extractFullPage();
      }

      sendResponse({ data });
    } catch (error) {
      sendResponse({ error: String(error) });
    }
    return true; // Keep message channel open for async response
  }
});
```

- [ ] **Step 3: Build and verify no TypeScript errors**

```bash
cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X/browser-extension" && npm run build
```
Expected: Build succeeds with content.js output.

- [ ] **Step 4: Commit**

```bash
git add browser-extension/src/content/
git commit -m "feat(extension): add content script with full/selection/reader extraction"
```

---

### Task 5: Browser Extension — Background Service Worker and API Client

**Files:**
- Create: `browser-extension/src/background/background.ts`
- Create: `browser-extension/src/api/agentx-api.ts`

- [ ] **Step 1: Create the Agent-X API client**

```typescript
// browser-extension/src/api/agentx-api.ts

const DEFAULT_PORT = 9846;
const DEFAULT_BASE_URL = `http://localhost:${DEFAULT_PORT}`;

export interface ClipRequest {
  title: string;
  content: string;
  sourceUrl: string;
  author?: string | null;
  publishedDate?: string | null;
  clipMode: string;
  wordCount: number;
  metadata?: Record<string, string>;
}

export interface ClipResponse {
  inboxItemId: number;
  status: string;
  message: string;
}

export interface HealthResponse {
  connected: boolean;
  version: string;
  inboxEnabled: boolean;
  provider: string;
}

export class AgentXApi {
  private baseUrl: string;

  constructor(baseUrl?: string) {
    this.baseUrl = baseUrl ?? DEFAULT_BASE_URL;
  }

  async checkHealth(): Promise<HealthResponse> {
    const response = await fetch(`${this.baseUrl}/api/extension/health`);
    if (!response.ok) throw new Error(`Agent-X not reachable: ${response.status}`);
    const json = await response.json();
    return json.data;
  }

  async clipToInbox(clip: ClipRequest): Promise<ClipResponse> {
    const response = await fetch(`${this.baseUrl}/api/inbox/clip`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(clip),
    });

    if (!response.ok) {
      const errorBody = await response.text();
      throw new Error(`Clip failed: ${response.status} — ${errorBody}`);
    }

    const json = await response.json();
    return json.data;
  }
}
```

- [ ] **Step 2: Create the background service worker**

```typescript
// browser-extension/src/background/background.ts

import { AgentXApi } from '../api/agentx-api';

const api = new AgentXApi();

// Listen for clip commands from popup
chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message.action === 'clipPage') {
    handleClip(message.mode, message.tabId)
      .then(result => sendResponse({ success: true, data: result }))
      .catch(error => sendResponse({ success: false, error: String(error) }));
    return true; // async response
  }

  if (message.action === 'checkConnection') {
    api.checkHealth()
      .then(health => sendResponse({ connected: true, health }))
      .catch(() => sendResponse({ connected: false, health: null }));
    return true;
  }

  if (message.action === 'clipAllTabs') {
    clipAllTabs()
      .then(results => sendResponse({ success: true, data: results }))
      .catch(error => sendResponse({ success: false, error: String(error) }));
    return true;
  }
});

async function handleClip(mode: 'full' | 'selection' | 'reader', tabId: number) {
  // Ask content script to extract page data
  const extractResponse = await chrome.tabs.sendMessage(tabId, {
    action: 'extractPage',
    mode,
  });

  if (extractResponse.error) {
    throw new Error(extractResponse.error);
  }

  const pageData = extractResponse.data;

  // Send to Agent-X
  const clipResult = await api.clipToInbox({
    title: pageData.title,
    content: pageData.content,
    sourceUrl: pageData.url,
    author: pageData.author,
    publishedDate: pageData.publishedDate,
    clipMode: pageData.clipMode,
    wordCount: pageData.wordCount,
  });

  return clipResult;
}

async function clipAllTabs() {
  const tabs = await chrome.tabs.query({ currentWindow: true });
  const results = [];

  for (const tab of tabs) {
    if (!tab.id || tab.url?.startsWith('chrome://')) continue;
    try {
      const result = await handleClip('reader', tab.id);
      results.push({ title: tab.title, status: 'clipped', result });
    } catch (error) {
      results.push({ title: tab.title, status: 'failed', error: String(error) });
    }
  }

  return results;
}
```

- [ ] **Step 3: Build and verify**

```bash
cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X/browser-extension" && npm run build
```
Expected: Build succeeds with background.js output.

- [ ] **Step 4: Commit**

```bash
git add browser-extension/src/background/ browser-extension/src/api/
git commit -m "feat(extension): add background service worker, API client, and batch clip"
```

---

### Task 6: Browser Extension — Popup UI

**Files:**
- Create: `browser-extension/src/popup/popup.html`
- Create: `browser-extension/src/popup/popup.css`
- Create: `browser-extension/src/popup/popup.ts`

- [ ] **Step 1: Create the popup HTML**

```html
<!-- browser-extension/src/popup/popup.html -->
<!DOCTYPE html>
<html>
<head>
  <link rel="stylesheet" href="popup.css">
</head>
<body>
  <div class="container">
    <header>
      <h1>Agent-X Clipper</h1>
      <div id="status" class="status disconnected">Not Connected</div>
    </header>

    <div id="connection-error" class="error" style="display:none">
      Agent-X is not running. Start the app and enable the REST API.
    </div>

    <div id="clip-actions" style="display:none">
      <button id="clip-full" class="btn btn-primary">
        Clip Full Page
      </button>
      <button id="clip-selection" class="btn btn-secondary">
        Clip Selection
      </button>
      <button id="clip-reader" class="btn btn-secondary">
        Clip Reader Mode
      </button>
      <button id="clip-all" class="btn btn-secondary">
        Clip All Tabs
      </button>
    </div>

    <div id="clip-success" class="success" style="display:none">
      Clipped to Smart Inbox!
    </div>

    <div id="clip-error" class="error" style="display:none"></div>

    <footer>
      <div id="clip-history">
        <h2>Recent Clips</h2>
        <ul id="recent-clips"></ul>
      </div>
    </footer>
  </div>

  <script src="popup.js"></script>
</body>
</html>
```

- [ ] **Step 2: Create the popup CSS**

```css
/* browser-extension/src/popup/popup.css */
* { box-sizing: border-box; margin: 0; padding: 0; }

body {
  width: 320px;
  font-family: 'Segoe UI', -apple-system, sans-serif;
  font-size: 13px;
  color: #1a1a1a;
  background: #fafafa;
}

.container { padding: 16px; }

header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

header h1 { font-size: 16px; font-weight: 600; }

.status {
  font-size: 11px;
  padding: 2px 8px;
  border-radius: 10px;
  font-weight: 500;
}

.status.connected { background: #e6f4ea; color: #137333; }
.status.disconnected { background: #fce8e6; color: #c5221f; }

.error {
  background: #fce8e6;
  color: #c5221f;
  padding: 8px 12px;
  border-radius: 6px;
  font-size: 12px;
  margin-bottom: 8px;
}

.success {
  background: #e6f4ea;
  color: #137333;
  padding: 8px 12px;
  border-radius: 6px;
  font-size: 12px;
  margin-bottom: 8px;
}

.btn {
  width: 100%;
  padding: 10px 12px;
  border: 1px solid #dadce0;
  border-radius: 6px;
  font-size: 13px;
  cursor: pointer;
  margin-bottom: 6px;
  text-align: center;
  transition: all 0.15s ease;
}

.btn:hover { transform: translateY(-1px); box-shadow: 0 2px 4px rgba(0,0,0,0.1); }

.btn-primary {
  background: #1a73e8;
  color: white;
  border-color: #1a73e8;
}

.btn-primary:hover { background: #1557b0; }

.btn-secondary { background: white; color: #1a1a1a; }
.btn-secondary:hover { background: #f1f3f4; }

footer { margin-top: 12px; border-top: 1px solid #dadce0; padding-top: 8px; }
footer h2 { font-size: 12px; color: #5f6368; margin-bottom: 4px; }
#recent-clips { list-style: none; }
#recent-clips li {
  font-size: 11px;
  color: #5f6368;
  padding: 2px 0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
```

- [ ] **Step 3: Create the popup TypeScript**

```typescript
// browser-extension/src/popup/popup.ts

const statusEl = document.getElementById('status')!;
const connectionErrorEl = document.getElementById('connection-error')!;
const clipActionsEl = document.getElementById('clip-actions')!;
const clipSuccessEl = document.getElementById('clip-success')!;
const clipErrorEl = document.getElementById('clip-error')!;
const recentClipsEl = document.getElementById('recent-clips')!;

// Check connection on popup open
chrome.runtime.sendMessage({ action: 'checkConnection' }, (response) => {
  if (response?.connected) {
    statusEl.textContent = 'Connected';
    statusEl.className = 'status connected';
    connectionErrorEl.style.display = 'none';
    clipActionsEl.style.display = 'block';
  } else {
    statusEl.textContent = 'Not Connected';
    statusEl.className = 'status disconnected';
    connectionErrorEl.style.display = 'block';
    clipActionsEl.style.display = 'none';
  }
});

// Load recent clips from storage
loadRecentClips();

// Clip buttons — use textContent for all dynamic content (no innerHTML)
document.getElementById('clip-full')?.addEventListener('click', () => clipPage('full'));
document.getElementById('clip-selection')?.addEventListener('click', () => clipPage('selection'));
document.getElementById('clip-reader')?.addEventListener('click', () => clipPage('reader'));
document.getElementById('clip-all')?.addEventListener('click', clipAllTabs);

async function clipPage(mode: 'full' | 'selection' | 'reader') {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab?.id) return;

  setButtonsEnabled(false);
  clipSuccessEl.style.display = 'none';
  clipErrorEl.style.display = 'none';

  chrome.runtime.sendMessage(
    { action: 'clipPage', mode, tabId: tab.id },
    (response) => {
      if (response?.success) {
        clipSuccessEl.style.display = 'block';
        saveRecentClip(tab.title ?? 'Untitled', tab.url ?? '');
      } else {
        clipErrorEl.textContent = response?.error ?? 'Unknown error';
        clipErrorEl.style.display = 'block';
      }
      setButtonsEnabled(true);
    }
  );
}

async function clipAllTabs() {
  setButtonsEnabled(false);
  clipSuccessEl.style.display = 'none';
  clipErrorEl.style.display = 'none';

  chrome.runtime.sendMessage({ action: 'clipAllTabs' }, (response) => {
    if (response?.success) {
      clipSuccessEl.textContent = `Clipped ${response.data.length} tabs!`;
      clipSuccessEl.style.display = 'block';
    } else {
      clipErrorEl.textContent = response?.error ?? 'Batch clip failed';
      clipErrorEl.style.display = 'block';
    }
    setButtonsEnabled(true);
  });
}

function setButtonsEnabled(enabled: boolean) {
  document.querySelectorAll('.btn').forEach(btn => {
    (btn as HTMLButtonElement).disabled = !enabled;
  });
}

async function saveRecentClip(title: string, url: string) {
  const result = await chrome.storage.local.get('recentClips');
  const clips: Array<{ title: string; url: string; time: number }> = result.recentClips ?? [];
  clips.unshift({ title, url, time: Date.now() });
  const trimmed = clips.slice(0, 10);
  await chrome.storage.local.set({ recentClips: trimmed });
  loadRecentClips();
}

async function loadRecentClips() {
  const result = await chrome.storage.local.get('recentClips');
  const clips: Array<{ title: string; url: string; time: number }> = result.recentClips ?? [];
  recentClipsEl.textContent = ''; // Safe: clears children via textContent
  clips.forEach(clip => {
    const li = document.createElement('li');
    const time = new Date(clip.time).toLocaleTimeString();
    li.textContent = `${time} — ${clip.title}`; // Safe: uses textContent
    li.title = clip.url;
    recentClipsEl.appendChild(li); // Safe: appending text-only elements
  });
}
```

- [ ] **Step 4: Build and verify**

```bash
cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X/browser-extension" && npm run build
```
Expected: Full build succeeds with popup.js, content.js, background.js in dist/.

- [ ] **Step 5: Load extension in Chrome and test connectivity**

1. Open Chrome → `chrome://extensions/` → Enable Developer Mode → Load Unpacked → Select `browser-extension/dist/`
2. Start Agent-X app and verify REST API is running (localhost:9846)
3. Click the extension icon → should show "Connected" status
4. Navigate to any web page → Click "Clip Reader Mode" → Check Agent-X Smart Inbox for the clipped item

- [ ] **Step 6: Commit**

```bash
git add browser-extension/src/popup/
git commit -m "feat(extension): add popup UI with clip actions, connection status, and clip history"
```

---

### Task 7: Extension Icons

**Files:**
- Create: `browser-extension/icons/` (PNG exports at 16, 48, 128)

- [ ] **Step 1: Create extension icons**

Create Agent-X branded icons at 16x16, 48x48, and 128x128 in `browser-extension/icons/`. Use the existing Agent-X logo/colors from `src/AgentX.App/Assets/` if a suitable logo file exists, or create a simple "AX" monogram on the red accent (#AA2024) background.

- [ ] **Step 2: Rebuild extension with icons**

```bash
cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X/browser-extension" && npm run build
```

Reload the extension in Chrome → verify icons appear in toolbar and extensions page.

- [ ] **Step 3: Commit**

```bash
git add browser-extension/icons/
git commit -m "feat(extension): add Agent-X branded extension icons"
```

---

### Task 8: Smart Inbox "via Browser Extension" Badge

**Files:**
- Modify: `src/AgentX.Core/Data/Entities/InboxItemEntity.cs`
- Modify: `src/AgentX.App/Views/InboxPage.xaml`

- [ ] **Step 1: Add source metadata to InboxItemEntity**

Read `src/AgentX.Core/Data/Entities/InboxItemEntity.cs`. Add:

```csharp
public string? SourceType { get; set; } // "file-watcher", "browser-extension", "manual"
public string? SourceUrl { get; set; }  // Original URL for browser clips
```

Add an EF Core migration:

```bash
cd "C:/Users/User/Desktop/Development Projects/Strategia-Enhanced-App/Agent-X" && dotnet ef migrations add AddInboxItemSourceFields --project src/AgentX.Core --startup-project src/AgentX.App
```

- [ ] **Step 2: Update clip endpoint to set source type**

In `ApiHostService.HandleClipAsync()`, after creating the inbox item, set the source metadata on the saved markdown file's frontmatter (the `clip-mode` and `source` fields already serve this purpose — the inbox item's `WatchFolderId` can be set to a sentinel value, or a new `SourceType` column can be added).

Update the markdown frontmatter in `HandleClipAsync` to include the source type, and update the InboxService to parse it during triage.

- [ ] **Step 3: Add badge to InboxPage UI**

Read `src/AgentX.App/Views/InboxPage.xaml`. In the inbox item template, add a badge that displays when `SourceType == "browser-extension"`:

```xml
<Border Visibility="{x:Bind HasBrowserSource, Mode=OneWay}"
        Background="{ThemeResource AccentAAFillColorDefaultBrush}"
        CornerRadius="4"
        Padding="4,2"
        Margin="0,0,8,0">
    <TextBlock Text="Web Clip" FontSize="10" Foreground="White"/>
</Border>
```

- [ ] **Step 4: Commit**

```bash
git add src/AgentX.Core/Data/Entities/InboxItemEntity.cs src/AgentX.App/Views/InboxPage.xaml
git commit -m "feat(inbox): add browser extension source badge to Smart Inbox items"
```