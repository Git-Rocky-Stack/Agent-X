# Agent-X - Intelligence Hub

**Local-first AI document intelligence for Windows**

Agent-X is a native Windows desktop application that turns the documents on your computer into a knowledge base you can search and question in plain language. You import files, web pages and audio; Agent-X extracts their text, splits it into passages, embeds the passages and adds them to a keyword index. You can then search by meaning or by keyword, ask questions that are answered from your own documents with citations, and chat with a language model that keeps facts you told it.

The default AI model runs inside Agent-X on your computer. Text leaves the computer only through services you configure: a cloud AI provider (OpenAI or Anthropic), an Ollama server on another machine, an OpenAI embedding model, web search for Research Mode, the calendar and email connectors, Web Import, and model downloads. [Privacy: What Leaves the Computer and When](#privacy-what-leaves-the-computer-and-when) lists every case. Agent-X sends no telemetry and needs no account.

**What makes Agent-X different:**

- **Built-in local model.** Llama 3.2 3B Instruct (a Q4_K_M GGUF file of about 2 GB) runs inside the app through LLamaSharp, with no separate server. The OFFLINE installer bundles it; with the default SLIM installer the onboarding wizard downloads it from Hugging Face (`src/AgentX.Core/AI/BuiltInModelCatalog.cs`).
- **A retrieval pipeline for Ask Your Files.** Multi-query expansion, a hypothetical answer (HyDE) for long questions, hybrid semantic and keyword search merged with Reciprocal Rank Fusion, PII redaction, reranking, parent-passage expansion, compression and numbered citations (`src/AgentX.Core/Search/RagPipeline.cs`).
- **One local database.** SQLite with 37 Entity Framework Core tables, a vector table and an FTS5 keyword index; optional SQLCipher encryption; an HNSW index once the vault holds more than 10,000 embeddings.
- **29 pages on the navigation rail and six UI languages**, a Command Palette and Jump To, and a local REST API for the browser extension and the Android companion.
- **Optional NVIDIA GPU offload** of the built-in model, sized from the GPU's video memory. It needs the NVIDIA CUDA 12 Toolkit on the computer (`src/AgentX.Core/AI/Providers/LocalLlmProvider.cs`).

> **Version:** 2.2.0 "Command Console" (`Directory.Build.props`); see the [CHANGELOG](../CHANGELOG.md)
> **Scope:** 29 navigation pages (`src/AgentX.App/MainWindow.xaml`), 37 database tables (`src/AgentX.Core/Data/AgentXDbContext.cs`), 6 UI languages (`src/AgentX.App/Strings/`)
> **Publisher:** Rocky Elsalaymeh (Strategia-X)
> **Platform:** Windows 10 version 2004 (build 19041) or later, x64
> **License:** MIT, see [LICENSE](../LICENSE)

---

## Table of Contents

1. [Release Status](#release-status)
2. [Feature Overview](#feature-overview)
3. [Privacy: What Leaves the Computer and When](#privacy-what-leaves-the-computer-and-when)
4. [Prerequisites](#prerequisites)
5. [Installation](#installation)
6. [Build from Source](#build-from-source)
7. [Configuration](#configuration)
8. [Application Architecture](#application-architecture)
9. [Pages and Navigation](#pages-and-navigation)
10. [Core Service Layer](#core-service-layer)
11. [Data Storage](#data-storage)
12. [Backup, Sync and Connectors](#backup-sync-and-connectors)
13. [Companion Apps and Plugins](#companion-apps-and-plugins)
14. [Keyboard Shortcuts](#keyboard-shortcuts)
15. [Pricing](#pricing)
16. [Project Structure](#project-structure)
17. [Technology Stack](#technology-stack)
18. [Further Documentation](#further-documentation)
19. [Contributing](#contributing)
20. [License](#license)

---

## Release Status

Version 2.2.0 "Command Console" is the version in this source tree. It applied the Command Console design system ([DESIGN.md](../DESIGN.md)) to every page, completed the six UI languages, and connected features that were finished but could not be reached from the UI. The [CHANGELOG](../CHANGELOG.md) has the details.

The v2.1.2 and v2.2.0 GitHub releases carry source code and release notes only; their installers wait for a code-signing certificate. The most recent release with installers attached is [v2.1.1](https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v2.1.1). To run 2.2.0, build it from this source tree ([Build from Source](#build-from-source)).

---

## Feature Overview

Every feature is free and available to every user. The built-in model and Ollama run on your computer; OpenAI and Anthropic are optional and use your own API keys.

### Documents and Knowledge

| Feature | What it does |
|---|---|
| **Import** | Import Files (a file picker), Import Folder (every supported file in a folder and its subfolders), drag and drop onto the Knowledge Vault, watch folders, Web Import, and pages clipped with the browser extension. A file whose SHA-256 hash matches a document already in the vault is reported as a duplicate before it is processed. |
| **File formats** | PDF (text layer), Word `.docx`, text and data files, Markdown, source code, images (text read with Windows OCR), audio (transcribed on the computer) and web shortcuts (`.url`, `.webloc`). See [Document Processing Pipeline](#document-processing-pipeline). |
| **Indexing** | Each document is split into passages (Chunk Size 512 tokens and Chunk Overlap 50 by default), embedded, and added to the keyword index. The AI then proposes tags from the first 2,000 characters. |
| **Knowledge Vault** | Filters for file type, status, tags, Collection and import date, sorting, a box that filters by file name or tag, multi-select to re-index or delete, and a Document Preview with details, tags, the reason a file could not be read, **Generate Title** (an AI title), the indexed text under DOCUMENT TEXT and the document's annotations. Deleting asks first and never deletes the file on disk. |
| **Annotations** | In the Document Preview, select text in a passage, add an optional note and click **Save Annotation**. The Annotations page lists every annotation, filters by color, edits notes and colors, and exports them with **Export as Markdown**. Annotations are not highlighted inside the passage text. |
| **Collections** | Groups of documents that nest one level deep, with **Add Documents**, rename, **Move into...**, multi-select delete (the documents stay in the vault) and **Export collection**, a ZIP with a manifest and a README listing the documents (not the files themselves). |
| **Watch folders** | Settings > Knowledge Vault > Watch Folders: Agent-X imports the supported files in each folder (with **Include subfolders**, its subfolders too) and keeps importing new and changed files while it runs. |
| **Web Import** | Web pages and YouTube transcripts from a list of URLs, every item of an RSS or Atom feed (read once, at import), or up to 100 pages from a sitemap. A page built by scripts is rendered in a headless Chromium when Playwright's browser is installed. |
| **Knowledge Graph** | Documents, Collections and tags drawn as a force-directed graph with node search, type filters, cluster highlighting, node details and zoom. The graph shows memberships and shared tags; it does not extract entities from the text. |
| **Compare Documents** | Two or more documents compared by the AI, with an optional focus topic and a detail level: similarities, differences, contradictions, unique points and a summary, saved with **Export Report** as Markdown. |

### Search and Answers

| Feature | What it does |
|---|---|
| **Semantic Search** | Semantic (the default), Keyword (BM25) and Hybrid modes; a Collection filter; advanced filters for file type, minimum relevance (30% by default), maximum results (20 by default, up to 100), creation dates and sort order; Saved Filters and Search History. Results are cached for five minutes, and the cache is cleared when documents change. |
| **Hybrid search** | Semantic and keyword search run in parallel, each asked for three times as many results (at most 500), and are merged with Reciprocal Rank Fusion (k = 60). |
| **Ask Your Files** | Answers from your indexed documents with numbered citations and a Sources panel, optionally limited to one Collection. **Top-K Results** in Settings (default 5) sets how many passages an answer keeps. |
| **Research Mode** | With **Enable Research Mode** on in Settings and the globe button on in AI Chat, the message is also sent to a web search provider (Brave, Serper or a SearXNG instance), and the results are added to the answer with their sources. |

### Conversation and AI

| Feature | What it does |
|---|---|
| **AI Chat** | Streaming chat with the active provider: pinning, folders (Work, Research, Personal, Archive or a name of your own), search over titles and message text, ten built-in system prompts, a model picker, branching with **Branch from here**, three suggested follow-up questions, voice input, and **Export conversation** as Markdown, HTML, PDF, JSON, plain text, CSV, Word or PowerPoint. |
| **Message actions** | Copy, delete, **Good response** / **Poor response**, **Regenerate response** (the latest answer only) and **Edit message** with **Save & Resend**, which removes the edited message and everything after it and sends the new text. |
| **Chat modes** | Solo (one model call), Multi (a researcher, a critic and a synthesizer answer in parallel) and Debate (agents challenge each other's positions before a final synthesis). |
| **Memories** | After each reply Agent-X asks the model, in the background, to note facts worth keeping. Up to eight stored memories whose meaning is close to a new message (similarity 0.65 or more) are added to its context, whichever conversation they came from. The **Memories** card in Context Inspection lists them, deletes one, or deletes all after a confirmation. |
| **Context Inspection** | **Inspect Context** shows what was assembled for a reply: the context story, the conversation's durable summary, messages recalled from earlier conversations, and the Memories card. |
| **Quick Actions** | Summarize, Key Points, Translate (ten languages; long text is translated in parts), Duplicates (exact and semantic), Organize (suggestions for up to 20 documents) and Recommended For This Context. |
| **Workflows** | Multi-step pipelines built from five step types (AI prompt, document lookup, text transform, conditional branch, output format), four templates, JSON import and export. A workflow runs when you start it. |
| **Past Self** | Temporal Identity keeps a local record of the beliefs you state in chat, insights and your writing voice. Past Self searches it by topic and time period, shows how a belief changed, lists active topics, and **Draft as Me** writes in your measured voice. Belief tracking recognizes English phrasing only. |
| **Quick Chat** | `Win+Shift+A` opens a small always-on-top window for a one-off question to the active provider, also while the main window is hidden. |
| **Multi-Model Routing** | Optional (Settings > Multi-Model Routing): picks a provider and model for each reply by task type, with the Cost Optimized, Quality Optimized or Balanced profile. With a cloud API key saved, it can send prompts to that provider. |
| **Cost tracking** | Estimated spend for OpenAI and Anthropic from a built-in price list (local models count as free), shown as TOTAL SPEND, TODAY and TOTAL TOKENS in Settings. The records are kept in `usage-history.json` for 90 days, at most 20,000 of them, so the figures survive a restart. |

### Interface

| Feature | What it does |
|---|---|
| **Navigation** | A navigation rail with 29 pages (28 in five groups, and Settings in the footer), a Command Palette (`Ctrl+K`), Jump To (`Ctrl+P`) for pages, documents and conversations, and a Keyboard Shortcuts dialog (`F1`). |
| **Status strip** | Readouts for the model, indexing and the vault, the version, and lamps for the model (MDL), the Smart Inbox (INBOX), sync (SYNC), jobs (JOBS), backups (BAK) and privacy (LOCAL or NET). Clicking a lamp opens its page. The strip is refreshed a few seconds after start and every 30 seconds after that. |
| **Themes** | Dark (the default), Light and System Default. A Windows contrast theme switches the app to the system contrast colors. |
| **Display language** | Settings > Appearance > Language: Windows default, or English, Spanish, German, French, Japanese or Chinese (Simplified), each listed under its own name. The choice is saved at once; restart Agent-X to see every page in the new language. |
| **Notification area** | Closing the window hides Agent-X in the notification area; the icon's menu has Open Agent-X, Quick Chat, Settings and Exit. |

---

## Privacy: What Leaves the Computer and When

Agent-X keeps its database, settings, models and logs in `%LOCALAPPDATA%\AgentX`. Nothing is sent anywhere until you set up one of the services below, and Agent-X has no telemetry, crash reporting or update check. Speech-to-text, OCR, the built-in model, the search indexes and backups stay on the computer.

| When | What leaves the computer | Where it goes |
|---|---|---|
| The active provider is OpenAI or Anthropic Claude | The text of every AI request: chat messages with the context Agent-X adds (passages from your documents, memories, summaries), and the text the other AI features send: auto-tagging (the first 2,000 characters of each newly indexed document), AI titles, Quick Actions, Compare Documents, Workflows, Smart Inbox previews, Draft as Me, conversation summaries (opening Analytics can refresh up to four), memory extraction after each reply, and follow-up questions | The provider's API (`api.openai.com`, `api.anthropic.com`, or the Endpoint you set) |
| The active provider is Ollama and its Endpoint is not this computer | The same request text | That Ollama server |
| The Embedding Model setting names an OpenAI model (`text-embedding-...`) | Every passage that is indexed, and every search query and question | OpenAI |
| **Enable Auto-Routing** is on and an OpenAI or Anthropic key is saved | Prompts the router assigns to a cloud model | That provider |
| Research Mode is on (the Settings switch and the globe button in AI Chat) | The chat message | Brave Search, Serper, or your SearXNG instance, which forwards it to public search engines |
| You connect an account on the Calendar or Email page | Sign-in through your own OAuth app; with Calendar sync or Email sync on (or after **Sync Now**), Agent-X reads events or mail (read-only) | Google or Microsoft |
| You use Web Import or import a `.url` / `.webloc` file | Requests for the pages, feeds and sitemaps you name | Those web sites (YouTube for transcripts) |
| You download a model | The download request | Hugging Face (the built-in models and the speech-to-text model); with Ollama active, **Pull Model** asks your Ollama server to fetch from the Ollama library |
| You install and enable a plugin | Whatever the plugin does: plugins run with the same rights as Agent-X and are not sandboxed | Plugin-dependent |
| Collaborative Sync is configured | Encrypted `.axs` packages written to the sync folder you choose; if a cloud service syncs that folder, the packages travel with it | Your sync folder |

With `enableScreenAwareness` set to `true` in `settings.json` (off by default, with no switch in the UI), Quick Chat adds text read by OCR from the window in front to its question, and that text goes to the active provider.

The Dashboard's privacy line and the LOCAL/NET lamp report a cloud or remote AI provider, model routing with a cloud key, a configured web search provider, and calendar or email sync. They do not report an OpenAI embedding model.

The local REST API listens on `http://localhost:9846`, the loopback name, and is deliberately not opened to the network ([MOBILE-TRANSPORT.md](MOBILE-TRANSPORT.md)); every route except the extension's health probe requires the API token. Database encryption at rest (SQLCipher) is optional and off until you turn it on in Settings; API keys and other secrets in `settings.json` are always encrypted with Windows DPAPI for your account. Nothing is stored in Windows Credential Manager.

---

## Prerequisites

### Required

| Requirement | Minimum | Notes |
|---|---|---|
| Windows | 10 version 2004 (build 19041) | The installer refuses older versions; Windows 11 works too |
| Architecture | x64 | The installers package win-x64 only |
| Disk space | The program, plus about 2 GB for the built-in model | The database grows with your documents and their embeddings |

### Optional

| Component | What it enables |
|---|---|
| NVIDIA GPU and the NVIDIA CUDA 12 Toolkit | GPU offload of the built-in model. LLamaSharp tries its CUDA 12 backend only when the Toolkit is installed (`CUDA_PATH`), because Agent-X does not ship `cudart64_12.dll` or `cublas64_12.dll`. Without the Toolkit the model runs on the CPU. |
| [Ollama](https://ollama.com) | Chat and embedding models served by Ollama, by default at `http://localhost:11434` |
| OpenAI or Anthropic API key | Cloud chat models, billed by the provider |
| Brave or Serper API key, or a SearXNG instance | Research Mode |
| Your own Google or Microsoft OAuth app | The calendar and email connectors |
| Windows codecs for OGG and WebM | Transcribing audio in those formats |
| Playwright's Chromium | Rendering script-built pages in Web Import |

### Build Prerequisites

| Requirement | Notes |
|---|---|
| .NET SDK 8.0.421 or newer | Pinned in [`global.json`](../global.json) with `rollForward: latestFeature` |
| Windows | Windows 10 version 2004 or later; the app project targets `net8.0-windows10.0.22621.0` |
| Visual Studio 2022 (optional) | With the .NET desktop and WinUI workloads; the CLI alone also works |
| Inno Setup 6 | Only to build the installers |
| Node 20 | Only for `browser-extension/` |
| MAUI Android workload | Only for `src/AgentX.Mobile` (`dotnet workload install maui-android`) |

---

## Installation

### Installers

The installers attached to the [v2.1.1 release](https://github.com/Git-Rocky-Stack/Agent-X/releases/tag/v2.1.1) are:

- **SLIM**: `AgentX-Setup-2.1.1-x64.exe` (228 MiB), attached to the release. It does not contain the model; the onboarding wizard downloads it on first run.
- **OFFLINE**: `AgentX-Setup-2.1.1-x64-offline.exe` (2.07 GiB), hosted at `downloads.strategia-x.com` and linked from the release notes, because it exceeds GitHub's size limit for a release file. It contains the model, so the computer never needs a connection.

They are not code-signed, so Windows SmartScreen asks you to confirm, and they predate the security fixes of 2.1.2, among them the token that now protects the local REST API. For 2.2.0, build the installers yourself ([Build from Source](#build-from-source)).

### Setup

1. Run the installer. By default it installs for the current user, without administrator rights, to `%LocalAppData%\Programs\Agent-X`; the installer also offers an install for all users, which needs administrator rights.
2. The installer closes a running Agent-X, creates `%LOCALAPPDATA%\AgentX\Logs` and `%LOCALAPPDATA%\AgentX\Models`, and (OFFLINE only) places the model in `Models`. A Start Menu entry is always created; a desktop icon only when you tick it.
3. On first launch the onboarding wizard runs: Welcome, Connect to Ollama, Choose Your Models, Your AI is Ready (with **Download built-in model**, or fields for OpenAI and Anthropic keys), and You're All Set. Leaving it any other way counts as skipping it; `Ctrl+P` > Onboarding opens it again.

### Uninstall

Use Settings > Apps in Windows or Uninstall Agent-X in the Start Menu. The uninstaller removes the program and the log files. Everything else in `%LOCALAPPDATA%\AgentX` is kept: the database, settings, models (including one installed by the OFFLINE installer), backups saved there, and plugins. Delete that folder to remove it all.

---

## Build from Source

### Clone and Build

```bash
git clone https://github.com/Git-Rocky-Stack/Agent-X.git
cd Agent-X
dotnet build -p:Platform=x64
```

The platform argument is required: a bare `dotnet build` fails because the solution has no AnyCPU configuration.

### Run

```bash
dotnet run --project src/AgentX.App -c Release -p:Platform=x64
```

### Test

```bash
dotnet restore tests/AgentX.Tests/AgentX.Tests.csproj
dotnet build   tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64 --no-restore
dotnet test    tests/AgentX.Tests/AgentX.Tests.csproj -c Release -p:Platform=x64 --no-build
```

After editing anything in `src/AgentX.App`, build again before testing: the test project compiles some App files, so `--no-build` would test stale code. Some tests drive a headless Chromium through Playwright; [CONTRIBUTING.md](../CONTRIBUTING.md) shows how to install it.

### Publish

The installers package a self-contained, unpackaged, ReadyToRun build that needs neither the .NET runtime nor the Windows App Runtime on the target computer:

```bash
dotnet publish src/AgentX.App/AgentX.App.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsPackageType=None -o publish/win-x64
```

The project also lists the `win-x86` and `win-arm64` runtime identifiers (`src/AgentX.App/AgentX.App.csproj`), but only win-x64 is packaged.

### Build the Installers

`scripts/build-installers.ps1` publishes the app and compiles the installers with Inno Setup 6 (`ISCC.exe` from the usual install folders or the PATH):

```powershell
./scripts/build-installers.ps1                    # publish, then SLIM and OFFLINE
./scripts/build-installers.ps1 -Profiles slim     # SLIM only
./scripts/build-installers.ps1 -SkipPublish       # reuse publish/win-x64
```

- The OFFLINE profile needs `models\llama-3.2-3b-instruct-q4_k_m.gguf`; `scripts/download-model.ps1` fetches it.
- The output is `installer-output\AgentX-Setup-2.2.0-x64.exe` and `installer-output\AgentX-Setup-2.2.0-x64-offline.exe`, plus `SHA256SUMS.txt`.
- The script stops if the published `AgentX.Core.dll` lacks the security types of the audit fixes (a provenance check). Signing is optional: pass `-CertificateThumbprint` or `-CertificatePath` (with `-CertificatePassword`), and `-RequireSign` to fail without a certificate. See [RELEASE-SIGNING.md](RELEASE-SIGNING.md).

---

## Configuration

### Settings Page

Changes on the Settings page are saved with **Save Settings** (`Ctrl+S`), except where the table says otherwise. **Reset to Defaults** asks first; it clears the OpenAI, Anthropic and web search keys and keeps the API token, the theme, the language and the watch folders.

| Section | Setting | Default | Notes |
|---|---|---|---|
| AI Providers | Active Provider | Built-in LLM (Local) | Built-in LLM (Local), Ollama (Local), OpenAI or Anthropic Claude. The **API Keys Secured** bar says keys are encrypted with DPAPI |
| AI Providers > Built-in LLM (Local) | Automatic GPU layers | On | Gives an NVIDIA GPU 16, 28 or 33 layers by its video memory (none below 2 GB) |
| | GPU Layers | - | 0 to 999 with Automatic off; 0 keeps the model on the CPU. Saving reloads the built-in model. Layers reach the GPU only with the CUDA 12 Toolkit |
| AI Providers > Ollama (Local) | Endpoint | `http://localhost:11434` | **Test Connection** checks it |
| | Default Chat Model | `llama3.2` | |
| | Embedding Model | `all-minilm` | Also decides where passages are embedded ([Embeddings](#embeddings)) |
| AI Providers > OpenAI | API Key, Endpoint, Default Model | -, `https://api.openai.com/v1/`, `gpt-4o-mini` | An OpenAI-compatible server can be set as Endpoint |
| AI Providers > Anthropic Claude | API Key, Endpoint, Default Model | -, `https://api.anthropic.com/v1/`, `claude-sonnet-5` | Anthropic has no embedding API |
| Appearance | Theme | Dark | Dark, Light, System Default; applies at once |
| | Language | Windows default | Saved at once; restart to see every page in the new language |
| Multi-Model Routing | Enable Auto-Routing | Off | |
| | Routing Profile | Balanced | Cost Optimized, Quality Optimized, Balanced |
| Cost Tracking | TOTAL SPEND, TODAY, TOTAL TOKENS | - | Read-only; TODAY starts at local midnight |
| Inference | Temperature | 0.7 | 0 to 2 |
| | Max Tokens | 4096 | 256 to 32768 |
| | Context Window | 8192 | 2048 to 131072 |
| Knowledge Vault | Chunk Size (tokens) | 512 | 128 to 2048 |
| | Chunk Overlap | 50 | 0 to 256, and less than the chunk size |
| | Top-K Results | 5 | 1 to 20; passages Ask Your Files keeps for an answer |
| | Auto-index watch folders | On | |
| | Watch Folders | None | **Add Folder** and **Include subfolders**; adding or removing a folder takes effect at once |
| Research Mode | Enable Research Mode | Off ("No web search") | On reads "Chat can add web results" |
| | Search Provider | Brave | Brave, Serper or SearXng |
| | API Key or Instance URL | - | A Brave or Serper API key, or the SearXNG URL |
| | Max Search Results | 10 | 1 to 20 |
| | Cache Duration (minutes) | 60 | 5 to 1440 |
| Storage | Data Location | `%LOCALAPPDATA%\AgentX` | Read-only |
| Database Encryption | Encrypted / Not encrypted | Not encrypted | Turning it on encrypts the database at once. It cannot be turned off |
| Connections | Enable Local API | On ("Enabled (authenticated)") | Takes effect when you save |
| | API Token | Generated on first start | Masked; **Show**, **Copy** and **Regenerate** (which revokes the old token at once) |

The version appears at the bottom of the page ("Agent-X v2.2.0") and in the status strip.

### settings.json

`%LOCALAPPDATA%\AgentX\settings.json` holds the settings as camelCase JSON. Agent-X creates it on first launch and rewrites it whenever it saves settings, so edit it only while Agent-X is closed. Secrets are stored encrypted with DPAPI for the current Windows user: `openAiApiKey`, `anthropicApiKey`, `webSearchApiKey`, `localApiToken`, the OAuth client secrets and the scheduled-backup password. A file that cannot be read is kept as `settings.json.corrupt-<time>` and defaults are used; secrets that cannot be decrypted (for example after copying the file to another account) are cleared, and the original is kept as `settings.json.undecryptable-<time>`.

| Key | Default | Set by |
|---|---|---|
| `onboardingCompleted` | `false` | The onboarding wizard (finished or skipped) |
| `theme` | `"Dark"` | Appearance > Theme |
| `languageOverride` | `null` (Windows default) | Appearance > Language |
| `activeProviderId` | `"local"` | Active Provider: `local`, `ollama`, `openai` or `anthropic` |
| `localModelFileName` | `"llama-3.2-3b-instruct-q4_k_m.gguf"` | File only: the GGUF file in `Models` |
| `localContextSize` | `8192` | File only: the built-in model's context size |
| `localGpuLayers` | `0` | `0` Automatic, a positive number of layers, or a negative number for CPU only (Automatic GPU layers and GPU Layers) |
| `ollamaEndpoint`, `defaultModel`, `embeddingModel` | `"http://localhost:11434"`, `"llama3.2"`, `"all-minilm"` | Ollama (Local) |
| `openAiApiKey`, `openAiEndpoint`, `openAiDefaultModel` | none, `"https://api.openai.com/v1/"`, `"gpt-4o-mini"` | OpenAI |
| `anthropicApiKey`, `anthropicEndpoint`, `anthropicDefaultModel` | none, `"https://api.anthropic.com/v1/"`, `"claude-sonnet-5"` | Anthropic Claude |
| `temperature`, `maxTokens`, `contextWindow` | `0.7`, `4096`, `8192` | Inference |
| `chunkSize`, `chunkOverlap`, `topKResults` | `512`, `50`, `5` | Knowledge Vault |
| `autoIndexWatchFolders` | `true` | Auto-index watch folders (the folders themselves are kept in the database) |
| `enableModelRouting`, `activeRoutingProfileId` | `false`, `"balanced"` | Multi-Model Routing |
| `enableResearchMode`, `webSearchProvider`, `webSearchApiKey`, `maxSearchResults`, `searchCacheTtlMinutes` | `false`, `0`, none, `10`, `60` | Research Mode (`webSearchProvider`: `0` Brave, `1` Serper, `2` SearXNG) |
| `enableScreenAwareness` | `false` | File only: lets Quick Chat add OCR text from the window in front |
| `localApiEnabled`, `localApiToken` | `true`, generated | Connections |
| `enableHnswIndex`, `hnswM`, `hnswEfConstruction`, `hnswEfSearch`, `hnswFallbackThreshold` | `true`, `16`, `200`, `50`, `10000` | File only: the vector index |
| `oAuth` | Empty Google and Microsoft client IDs and secrets; redirect URIs `http://localhost:8400/oauth/callback` (Google) and `http://localhost:8401/oauth/callback` (Microsoft); Microsoft `tenantId` `"common"`; `tokenRefreshBufferMinutes` `5`; `authTimeoutSeconds` `300` | OAuth App Credentials on the Calendar and Email pages (client IDs and secret); the rest file only |
| `calendarConnector` | `enableCalendarSync` `false`, `syncIntervalMinutes` `15`, `daysPastToSync` `90`, `daysFutureToSync` `30`, `conflictResolution` `"RemoteWins"`, `includeAttendeeDetails` `true`, `includeDescriptions` `true` | Calendar page (`conflictResolution` is saved but not used) |
| `emailConnector` | `enableEmailSync` `false`, `syncIntervalMinutes` `10`, `messagesPerSync` `50`, `daysBackToSync` `30`, `includeAttachmentMetadata` `false`, `enableAiCategorization` `true`, `includeBodyContent` `true` | Email page (`enableAiCategorization` and `includeBodyContent` are not used) |
| `backupSchedule` | `enabled` `false`, `intervalHours` `168`, `maxBackupsToKeep` `5`, `destinationPath` `""` (the data folder), `encryptionPassword` none | Backup & Restore > Scheduled Backups |
| `storagePath` | `%LOCALAPPDATA%\AgentX` | Shown as Data Location. Leave it at the default: the database, settings and logs always stay in `%LOCALAPPDATA%\AgentX`, while models, Web Import pages, exports and the vector store follow this value (see [KNOWN-ISSUES](KNOWN-ISSUES.md)) |

### appsettings.json and RagPrompts.json

Both files ship next to `AgentX.App.exe`.

- `appsettings.json` has a `Rag` section, read when Agent-X starts (a change needs a restart), that tunes retrieval, for example `DefaultSearchMode` (`Hybrid`), `RetrievalMultiplier` (`3`), `RetrievalCap` (`500`), `EnableLlmReranking` (`true`), `EnableHyde` (`true`), `HydeMinQueryLength` (`80`), `EnablePiiRedaction` (`true`) and `PiiRedactionMask` (`***`). A few keys in the file are not read by this version: `AssociativeLinkThreshold`, `MaxMemoriesPerQuery`, `VectorStoreFallbackThreshold`, `ResearchMaxWebResults` and `EnableResearchMode` (the vector index threshold and Research Mode come from `settings.json`).
- `RagPrompts.json` holds the prompt templates of the retrieval pipeline; changes to it apply without a restart.

### Environment Variables

The only environment variable Agent-X reads itself is `CUDA_PATH`, to find the CUDA 12 Toolkit. API keys and other settings are never read from environment variables.

### Logging

Logs are written to `%LOCALAPPDATA%\AgentX\Logs\agentx-YYYYMMDD.log`, one file per day, and the last 7 files are kept. The level is Debug. Each line looks like this:

```
2026-09-27 14:32:01.123 [INF] Generated a new local REST API token
```

The logs help with indexing failures, provider connection problems and database errors. The Operations page shows the state of indexing, sync, workflows and connectors without opening them.

---

## Application Architecture

`AgentX.sln` holds the desktop app, its core library and the tests. The desktop app depends on the core library; the core library has no WinUI dependency.

```
AgentX.sln
  src/AgentX.App           WinUI 3 desktop app (views, view models, shell, UI services)
  src/AgentX.Core          Services, AI providers, search, data layer
  tests/AgentX.Tests       xUnit tests for Core, plus App view models and services compiled in by link
  tools/LocaleAudit        Translation key parity checker (with tests/LocaleAudit.Tests)
```

Outside the solution: `src/AgentX.Mobile` (the MAUI Android companion), `browser-extension/` (the AgentX Web Clipper) and `plugins/sample-plugin/`.

### AgentX.App

- **Composition root.** `App.xaml.cs` builds the host with `Microsoft.Extensions.Hosting`, registers the services, loads `appsettings.json` and `RagPrompts.json`, and configures Serilog.
- **Shell.** `MainWindow` hosts the `NavigationView` rail and the page `Frame`, the Command Palette, Jump To (`MainWindow.JumpTo.cs`), keyboard shortcut routing (`ShortcutCatalog`, `ShortcutInputRouter`), the status strip, the notification-area icon and the onboarding flow (`MainWindow.StatusTrayOnboarding.cs`), and the window backdrop (Mica Alt, then Desktop Acrylic, then a solid background, in `ChromeService`).
- **Views and view models.** Pages in `Views/`, view models in `ViewModels/` built with `CommunityToolkit.Mvvm` (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`). Pages keep their instance in the `Frame` cache (`NavigationCacheMode="Enabled"`), except the Knowledge Graph and Onboarding, which are rebuilt on each visit. Some pages create their view model with `PageViewModelFactory.Create<T>()`, which uses `ActivatorUtilities` so the root container does not hold a disposable view model after its page is gone; the others, and code outside constructors, resolve services with `App.GetService<T>()`.
- **Resources.** UI text comes from `Strings/<locale>/Resources.resw` through `x:Uid`; styles and theme dictionaries follow the Command Console design system in [DESIGN.md](../DESIGN.md).

### AgentX.Core

| Folder | Contents |
|---|---|
| `AI/` | Provider interface and providers, `AiService`, embeddings, context window management, model routing, multi-agent orchestration, hardware detection, the built-in model catalog and download |
| `Configuration/` | `RagConfiguration` (the `Rag` section of `appsettings.json`) |
| `Data/` | `AgentXDbContext`, entities, EF Core migrations and the migration runner, the encrypted connection factory, vector stores |
| `Documents/` | `DocumentService`, file processors, chunking |
| `Search/` | Semantic, keyword and hybrid search, and the stages of the retrieval pipeline |
| `Services/` | Analytics, Annotations, Api, Audio, Backup, Chat, Collections, Export, FeatureFlags, Feedback, Inbox, Indexing, Intelligence, Localization, OAuth, Plugins, Privacy, Screen, Search (web search), Security, Settings, Shortcuts, Sync, Tagging, TemporalIdentity, Web, Workflows, Workspace |
| Other | `Constants/`, `DTOs/`, `Exceptions/`, `Helpers/`, `Math/`, `Observability/`, `Validation/` |

### Startup Sequence

1. `OnLaunched` builds the host and applies the saved UI language before any window exists, so the shell loads in that language.
2. The main window is created and shown, and the notification-area icon is set up.
3. `InitializeCoreServicesAsync` finishes or undoes an encryption change a crash interrupted, then unlocks the database: with the DPAPI-wrapped key, or for an older passphrase keystore by asking for the passphrase.
4. The startup orchestrator applies pending EF Core migrations and, only if that succeeds, starts the local REST API and the built-in connectors. If migration fails, Agent-X starts no data feature and shows "Agent-X could not start" with recovery steps.
5. The FTS5 keyword index is initialized, auto-sync resumes when it was on, and workflow runs left running by an earlier session are marked as interrupted.
6. The AI service creates the active provider and checks its connection (this loads the built-in model when it is the active provider), then feature flags, the theme, enabled plugins, scheduled backups, the indexing pipeline and the watch folders start.
7. If onboarding has not been completed, the shell hides the navigation rail and opens the onboarding wizard.

There is no single-instance check: starting Agent-X a second time opens a second copy.

---

## Pages and Navigation

The rail in `MainWindow.xaml` has 29 pages: 28 in five groups and Settings in its footer. The tag is the page's navigation key. Onboarding is not on the rail; it opens from Jump To.

| Group | Page | Tag | What it shows |
|---|---|---|---|
| INTELLIGENCE | Dashboard | `Dashboard` | Counts of documents, collections, storage, AI sessions and tokens; the privacy line; Operations overview cards; recent documents and conversations; file type distribution; top collections; Your Belief Evolution; Recommended Next Steps; a search box that opens Semantic Search with the query |
| | Operations | `Operations` | One view of conversation summaries (**Refresh Summaries**), sync (**Sync Now**), the ingestion backlog (**Retry Index**, **Generate Previews**), workflow runs and connectors (**Enable Connector**), with Suggested Fixes |
| | Weekly Digest | `Digest` | **Generate New Digest** builds a report of the last seven days: new documents, conversations, searches, tokens used and the storage change. Reports are kept; there is no schedule |
| | Analytics | `Analytics` | Usage counters, Workflow Intelligence, Conversation Intelligence (durable summaries and their freshness), the Semantic Recall Probe and Conversation Themes |
| | Past Self | `PastSelf` | Search Past Self, Get Relevant Insights, Show Belief Evolution and Get Active Topics for a time period (All time, Past week, Past month, Past year or a custom date), and Draft as Me |
| | AI Chat | `Chat` | Conversations with the active provider (see [Conversation and AI](#conversation-and-ai)) |
| | Ask Your Files | `AskFiles` | Questions answered from indexed documents, with citations and a Sources panel |
| | Quick Actions | `QuickActions` | Summarize, Key Points, Translate, Duplicates, Organize, Recommended For This Context |
| | Workflows | `Workflows` | New Workflow, Import Workflow, templates, Run Workflow, Export JSON, and saving a result to the Knowledge Vault |
| KNOWLEDGE | Knowledge Vault | `KnowledgeVault` | The document library, its filters and the Document Preview with passages and annotations |
| | Web Import | `WebImport` | URLs, RSS or Atom feeds, and sitemaps |
| | Collections | `Collections` | Create, rename, move, delete and export Collections; add and remove documents |
| | Semantic Search | `Search` | Semantic, Keyword and Hybrid search with filters, Saved Filters and Search History |
| | Knowledge Graph | `KnowledgeGraph` | The force-directed graph of documents, Collections and tags |
| | Compare Documents | `Comparison` | AI comparison of two or more documents |
| | Annotations | `Annotations` | Every annotation, with search, color filter, editing and Export as Markdown |
| TRIAGE | Smart Inbox | `Inbox` | Pages clipped with the browser extension, to **Accept**, **Defer** or **Reject** one at a time or with **Accept All Pending**; **Generate AI Previews** and **Clean Up Processed**. Calendar and email items are accepted into the vault automatically; a plugin connector can add items for review or as already accepted |
| SYSTEM | Model Manager | `ModelManager` | The active provider's installed models (Set Active, copy the name, delete without confirmation); **Pull Model** by name (an Ollama library model, or with the built-in provider one of its two catalog files); and the **Speech-to-Text Model** section (Download, Cancel download, Remove) |
| | Hardware Advisor | `HardwareAdvisor` | Detected GPU, CPU, memory and NPU, a performance tier, advice, and recommended Ollama models to install |
| | Backup & Restore | `BackupRestore` | Create Backup, Restore from Backup, Backup History and Scheduled Backups |
| | Workspace Profiles | `WorkspaceProfiles` | Named records (a description, an Ollama model name, Collection IDs and free-form settings), one of them marked as the default. Profiles are not applied: all of them share one vault, one set of conversations and one set of settings |
| | Plugin Manager | `PluginManager` | Install, enable, disable and uninstall plugins |
| | Collaborative Sync | `SyncSettings` | Sync Folder, Encryption Key, Auto-Sync, Sync Scope, Sync Now and Sync History |
| | Calendar | `CalendarSettings` | OAuth App Credentials, Google Calendar and Outlook Calendar accounts, and sync settings |
| | Email | `EmailSettings` | OAuth App Credentials, Gmail and Outlook accounts, sync settings and Folders to sync |
| SUPPORT | User Guide | `UserGuide` | The in-app guide, in all six languages |
| | Privacy Policy | `PrivacyPolicy` | The privacy policy |
| | Terms of Service | `TermsOfService` | The terms of service |
| (footer) | Settings | `Settings` | See [Settings Page](#settings-page) |
| (not on the rail) | Onboarding | `Onboarding` | The first-run wizard; `Ctrl+P` > Onboarding opens it again |

---

## Core Service Layer

### AI Providers

Each backend implements `IAiProvider` (`src/AgentX.Core/AI/IAiProvider.cs`):

```csharp
public interface IAiProvider : IDisposable
{
    string ProviderId { get; }
    string DisplayName { get; }
    bool IsAvailable { get; }

    Task<bool> CheckConnectionAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default);
    Task PullModelAsync(string modelName, IProgress<ModelDownloadProgress>? progress = null, CancellationToken ct = default);
    Task DeleteModelAsync(string modelName, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamChatAsync(IReadOnlyList<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default);
    Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default);
    Task<float[]> GenerateEmbeddingAsync(string text, string modelName, CancellationToken ct = default);
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, string modelName, CancellationToken ct = default);
}
```

| Id | Class | Transport and behavior |
|---|---|---|
| `local` | `LocalLlmProvider` | LLamaSharp 0.19.0 inside the app, loading a GGUF file from `Models` with an 8,192-token context. Two catalog models: Llama 3.2 3B Instruct Q4_K_M (the default) and Llama 3.2 1B Instruct Q4_K_M, both from Hugging Face. A downloaded file is checked for its announced size and the GGUF format; no SHA-256 is pinned. Picking a model with Set Active in Model Manager lasts for the session only |
| `ollama` | `OllamaProvider` | OllamaSharp 4.0.6 over HTTP to the Endpoint. The connection check gives up after 3 seconds, so a missing Ollama server is reported quickly |
| `openai` | `OpenAiProvider` | `HttpClient` to the Chat Completions API, streamed with server-sent events. Reasoning models (o1, o3, o4 and gpt-5 families) get `max_completion_tokens` and no sampling parameters |
| `anthropic` | `AnthropicProvider` | `HttpClient` to the Messages API, streamed with server-sent events. The model list comes from the Models API, with a fallback list (Claude Opus 5.5, Claude Sonnet 5, Claude Haiku 4.5). Temperature is sent only to older model families, clamped to 0 to 1. No embeddings |

Cloud usage is recorded by the cost tracker (`src/AgentX.Core/AI/Models/CostTracker.cs`).

### Embeddings

`EmbeddingTargetResolver` picks the embedding provider from the Embedding Model setting, independently of the chat provider:

1. A `text-embedding-...` name embeds with OpenAI, the only way document text is embedded in the cloud.
2. A `.gguf` file name embeds with the built-in provider.
3. Any other name except the default is an Ollama model.
4. The default, `all-minilm`, uses the built-in model when its file is installed, and Ollama's `all-minilm` otherwise.

Sizes differ by model: `all-minilm` gives 384 dimensions, `nomic-embed-text` 768, `text-embedding-3-small` 1,536 and the built-in 3B model 3,072. Passages embedded with another model are left out of semantic search, so re-index the vault after changing the embedding model (Knowledge Vault: **Select**, **Select All**, **Re-index**).

### Hybrid Search and RRF Fusion

`HybridSearchOrchestrator` runs a `SearchQuery` in one of three modes:

- **Semantic:** `SemanticSearchService` embeds the query and asks `IVectorStore` for the nearest passages (cosine similarity).
- **Keyword:** `KeywordSearchService` queries the `fts_chunks` FTS5 table with BM25 ranking. Stop words are dropped, the remaining terms are combined with OR, and scores are relative to the best match.
- **Hybrid:** both run in parallel with `TopK` multiplied by `RetrievalMultiplier` (3, at most `RetrievalCap`, 500). Each side first drops results below the minimum score on its own scale, then the lists are merged with Reciprocal Rank Fusion:

```
RRF_score = sum(1 / (k + rank_i))   k = 60, over each list the passage appears in
```

The fused score is divided by the highest possible score, 2 / (k + 1), to fall between 0 and 1. If one backend fails, the results of the other are returned.

### Vector Store

Embeddings are stored as float BLOBs with a precomputed magnitude in the `vec_embeddings` table of the main database. `VectorStoreFactory` picks the store from `settings.json`:

- `enableHnswIndex` `true` (the default): `HnswVectorStore` builds an in-memory HNSW index (HnswLite; `hnswM` 16, `hnswEfConstruction` 200) and uses it once there are more than `hnswFallbackThreshold` (10,000) embeddings. Below that, and for a query embedding of another size, it scans all embeddings. The index is saved as `hnsw-index.bin`, `hnsw-index.json` and `hnsw-stale-ids.json` in the data folder while the database is not encrypted; with encryption on it is kept in memory only and rebuilt at each start.
- `enableHnswIndex` `false`: `SqliteVecStore` always scans.

The database uses WAL journal mode, so searches can run while documents are indexed.

### Document Processing Pipeline

1. `DocumentService.ImportDocumentAsync` computes the file's SHA-256 hash. A match with a document in the vault stops the import as a duplicate, unless you chose **Import All** in the duplicate warning.
2. The processor for the file's extension extracts the text and metadata. A file that cannot be read (encrypted, damaged, no text) is still saved, as Failed, with the reason.
3. The document is saved as pending. It keeps the path to the original file, which is not copied; pages from Web Import, the browser extension and the connectors are saved under the data folder.
4. `IndexingService` receives the `DocumentPendingIndexing` event and queues the document. It indexes one document at a time, records each run in `indexing_jobs`, and at startup queues the documents still pending from an earlier session.
5. Indexing splits the text with `IChunkingService` (Chunk Size, Chunk Overlap), saves the passages, embeds them in batches through `IEmbeddingService`, writes the vectors through `IVectorStore`, and adds the passages to `fts_chunks`.
6. `IAutoTagService` asks the AI for tags from the first 2,000 characters (the `ai.auto_tagging` feature flag, on by default). A failure here does not affect the index.

| Extensions | Processor | Notes |
|---|---|---|
| `.pdf` | `PdfProcessor` | PDFsharp; reads the text layer (no OCR for scanned PDFs) |
| `.docx` | `DocxProcessor` | DocumentFormat.OpenXml |
| `.txt`, `.csv`, `.log`, `.json`, `.xml`, `.yaml`, `.yml`, `.toml`, `.ini`, `.cfg` | `TextProcessor` | |
| `.md`, `.markdown`, `.mdx` | `MarkdownProcessor` | Markdig |
| `.cs`, `.js`, `.ts`, `.py`, `.java`, `.cpp`, `.c`, `.h`, `.go`, `.rs`, `.swift`, `.kt`, `.rb`, `.php`, `.html`, `.htm`, `.css`, `.scss`, `.sql`, `.sh`, `.xaml` | `CodeFileProcessor` | Records the programming language |
| `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tiff` | `ImageProcessor` | Windows OCR; images over 4096 pixels on a side are scaled down first |
| `.mp3`, `.wav`, `.m4a`, `.flac`, `.ogg`, `.webm` | `AudioProcessor` | Whisper base (`ggml-base.bin`, about 142 MB) on the CPU, once it is downloaded in Model Manager; language detected automatically, no speaker labels. Without the model the file is saved as Failed and transcribed after the download |
| `.url`, `.webloc` | `WebProcessor` | Fetches the linked page; addresses on this computer or the local network are refused |

Plugins can add formats that no built-in processor handles. The Import Files picker lists only `.pdf`, `.docx`, `.txt`, `.md`, `.csv`, `.json`, `.html`, `.htm`, `.xml`, `.py`, `.cs`, `.js`, `.ts`, `.java`, `.cpp`, `.c` and `.h`; drag and drop and Import Folder take every supported format.

### Retrieval Pipeline (Ask Your Files)

`RagPipeline.AskAsync` runs these stages; a stage that fails is skipped and the answer continues with what it has:

1. **Multi-query:** the model writes three variations of the question.
2. **HyDE:** for a question of 80 characters or more, the model writes a hypothetical answer that is searched as well.
3. **Hybrid search** for every query, and the results are merged.
4. **PII redaction:** email addresses, phone numbers, social security numbers, credit card numbers, API keys and IP addresses in the passages are masked before any text is sent to the model (again after parent retrieval).
5. **Reranking:** a heuristic pass (duplicates, diversity, query terms), then a model pass when more than two passages remain.
6. **Parent retrieval** widens passages with their neighbors, and **compression** trims them to what answers the question.
7. The model writes the answer from the numbered passages, and the citations are extracted from it.

The number of passages kept is Top-K Results (Settings, default 5). The prompts are in `RagPrompts.json`.

### Conversation Memory

The desktop app uses `SemanticMemoryService`. After each completed reply it extracts memories with a background model call, in categories such as `user_preference`, `project_context` and `technical_preference`, each with a confidence. For every new message it retrieves up to 8 memories whose embedding similarity to the message is 0.65 or more and adds them to the context. `ConversationMemoryService` reads and deletes the stored memories for the Memories card, asks the model for the three suggested follow-up questions after a reply, and extracts memories itself only when no semantic memory service is registered. Long conversations also get durable summaries (`ConversationSummaryService`), which are part of the context and appear in Context Inspection and on the Analytics page.

### Knowledge Graph Construction

`KnowledgeGraphService.BuildGraphAsync`:

1. Loads the documents with their Collections and tags, all Collections and all tags.
2. Creates nodes: a document node grows with its passage count (14 + 2 per passage, at most 40), Collection nodes are 32 and tag nodes 16.
3. Creates edges from documents to their Collections and tags, and between documents that share a Collection or tag, weighted by how many they share.
4. Places the nodes at random positions from a fixed seed (42), so the layout is the same each time.
5. Runs 100 iterations of a force-directed layout: repulsion between all nodes (strength 5000), attraction along edges (strength 0.01, ideal length 100), gravity toward the center (0.01) and damping (0.85).

The page draws the result on a `Canvas` and zooms from 0.25x to 4x; it does not pan.

### Local REST API

`ApiHostService` listens on `http://localhost:9846/` while **Enable Local API** is on. Every route except `GET /api/extension/health` needs the header `Authorization: Bearer <API Token>`.

| Method and path | Purpose |
|---|---|
| `GET /api/health` | Status of the app |
| `GET /api/documents`, `GET /api/documents/{id}` | Documents |
| `GET /api/conversations`, `GET /api/conversations/{id}` | Conversations |
| `GET /api/collections` | Collections |
| `POST /api/search` | Search the vault |
| `POST /api/inbox/clip` | Add a clipped page to the Smart Inbox |
| `GET /api/auth/check` | Check a token |
| `GET /api/extension/health` | Health probe for the browser extension (no token) |

[API-REFERENCE.md](API-REFERENCE.md) documents the requests and responses.

---

## Data Storage

All data lives under `%LOCALAPPDATA%\AgentX\`:

```
%LOCALAPPDATA%\AgentX\
    agentx.db (-wal, -shm)          SQLite database; SQLCipher-encrypted when encryption is on
    settings.json                   Settings, with secrets encrypted by DPAPI
    encryption.info.json            The DPAPI-wrapped database key (only once encryption is on)
    usage-history.json              Cost tracking records
    hnsw-index.bin, hnsw-index.json, hnsw-stale-ids.json
                                    Saved vector index (only while the database is not encrypted)
    agentx-backup-<date>.agentxbak  Backups saved to the default folder
    Logs\                           agentx-YYYYMMDD.log, the last 7 days
    Models\                         Built-in model GGUF files
    Models\Whisper\                 Speech-to-text model (ggml-base.bin)
    Plugins\<plugin id>\            Installed plugins and their data
    WebImports\                     Pages saved by Web Import
    Clips\                          Pages clipped with the browser extension
    Inbox\External, Inbox\Accepted  Connector items
    Exports\                        Exported conversations, collections and reports
    Temp\                           Audio conversion files
```

### Database Schema

`AgentXDbContext` maps 37 entity types, one table each; `vec_embeddings`, the `fts_chunks` FTS5 table and `__EFMigrationsHistory` are managed outside the model. [DATABASE_SCHEMA.md](../DATABASE_SCHEMA.md) has the columns.

| Area | Tables |
|---|---|
| Documents and organization | `documents`, `document_chunks`, `collections`, `document_collections`, `tags`, `document_tags`, `annotations`, `watch_folders`, `indexing_jobs` |
| Conversations | `conversations`, `messages`, `conversation_tags`, `conversation_summary_snapshots`, `conversation_summary_states`, `conversation_theme_clusters`, `conversation_theme_daily_metrics`, `conversation_theme_memberships`, `memories`, `system_prompts`, `feedback` |
| Search and reports | `search_history`, `digest_reports` |
| Automation and integration | `workflows`, `workflow_steps`, `workflow_runs`, `inbox_items`, `sync_logs`, `plugins`, `oauth_credentials`, `backups`, `workspace_profiles`, `user_settings` |
| Temporal Identity | `temporal_beliefs`, `belief_conflicts`, `insight_moments`, `engagement_metrics`, `voice_profiles` |

EF Core migrations are applied at startup by the migration runner, which adopts databases created before migrations existed.

### Encryption at Rest

Settings > Database Encryption turns on SQLCipher (AES-256) encryption of `agentx.db`. The database is converted with `sqlcipher_export` and checked before the switch; a failed attempt leaves it unencrypted. The key is a random 256-bit key, wrapped with DPAPI for your Windows account and kept outside the database in `encryption.info.json`, so Windows unlocks it at startup without a prompt. Keystores created by older versions with a passphrase (PBKDF2-HMAC-SHA256, 600,000 iterations) still work and ask for the passphrase at startup.

- Encryption cannot be turned off again.
- Without `encryption.info.json` the database cannot be opened; keep it with any copy of `agentx.db`.
- A backup of an encrypted database can be restored only by the same installation under the same Windows account.
- The app opens its database connections through `IEncryptedConnectionFactory`, which applies the key before any query.

---

## Backup, Sync and Connectors

### Backup & Restore

- **Create Backup** writes `agentx-backup-<date>.agentxbak`, a ZIP archive with the database (`database/agentx.db`), a `manifest.json` and, with **Include indexed documents**, the pages saved by Web Import. **Destination Folder** defaults to `%LOCALAPPDATA%\AgentX`; **Encrypt backup (AES-256)** protects the file with a password (AES-256-GCM, PBKDF2 with 600,000 iterations); **Notes (optional)** are stored with it. Settings, API keys, models and plugins are not included.
- **Restore from Backup** checks the file, stages the restore and rolls back if it fails. Restart Agent-X afterwards.
- **Scheduled Backups** (**Back up automatically**, **Interval (hours)** 1 to 720, default 168; **Scheduled backups to keep**, default 5, 0 keeps all; **Destination Folder**; **Encrypt scheduled backups (AES-256)**; **Save Schedule**) run while Agent-X is running. Manual backups are never deleted by the schedule.

### Collaborative Sync

The Collaborative Sync page exchanges encrypted `.axs` packages (AES-256-GCM, key derived with PBKDF2-SHA256, 100,000 iterations) through a shared **Sync Folder**, with the same **Encryption Key** on every installation. **Auto-Sync** repeats every **Sync Interval** (30 minutes by default); **Sync Scope** takes everything or only the **Collections to Sync**.

A package carries document records, Collections, tags, conversations, annotations and system prompts. It does not carry document files, passages or embeddings, chat messages or settings. A document arrives pending when its file exists at the same path on the receiving computer. The sync configuration, including the key, is stored in the database (`user_settings`).

### Calendar and Email Connectors

The Calendar page connects Google Calendar or Outlook Calendar, and the Email page connects Gmail or Outlook mail. Both read only.

1. Agent-X ships no OAuth client credentials. Under **OAuth App Credentials** on either page, enter your own: for Google a **Client ID** and **Client secret** of an OAuth client of type Desktop app; for Microsoft the **Application (client) ID** of an app registration (the **Redirect URI** is shown to copy into it). **Save Credentials** applies them at once. Disconnect an account before changing its client ID.
2. **Connect** opens the provider's sign-in in the browser; the consent must be completed within 300 seconds. Tokens are stored encrypted with DPAPI in `oauth_credentials`. An account whose token cannot be refreshed shows "Reconnect required".
3. Sync settings: on the Calendar page **Calendar sync**, **Sync interval (minutes)** (15), **Sync range: past days** (90), **Sync range: future days** (30), **Conflict resolution** (saved but not used), **Include attendee details** and **Include event descriptions**; on the Email page **Email sync**, **Sync interval (minutes)** (10), **Max messages per sync** (50), **Sync days back** (30), **Include attachment names in search index** and **Folders to sync** (the inbox by default; with no folder selected nothing is synced). **Save Settings** and **Sync Now** are on both pages.

Synced events and messages are accepted into the vault automatically as documents. Only Google and Microsoft are supported; there is no IMAP, CalDAV or Exchange Web Services.

---

## Companion Apps and Plugins

### Browser Extension

`browser-extension/` holds the AgentX Web Clipper, a Manifest V3 extension written in TypeScript. Paste the API Token from Settings > Connections into its popup to pair it ("Paired with AgentX."); it then sends pages to `POST /api/inbox/clip`, and they wait in the Smart Inbox. The popup reads "Not paired" without a token and "Offline" when Agent-X or its local API is not running.

### Android Companion

`src/AgentX.Mobile` is a .NET MAUI app for Android with Documents, Conversations, Search and Settings pages. It only reads. Because the local API answers on the PC's loopback address only, the phone reaches it through the Android emulator (`http://10.0.2.2:9846`) or over USB with `adb reverse tcp:9846 tcp:9846`; there is no connection over the local network. See [MOBILE-TRANSPORT.md](MOBILE-TRANSPORT.md).

### Plugins

A plugin is a `.agentx-plugin` package (a ZIP archive with `manifest.json` at its root), installed on the Plugin Manager page into `%LOCALAPPDATA%\AgentX\Plugins\<plugin id>`. A new plugin is disabled until you enable it. Plugins load into a collectible `AssemblyLoadContext`, and initializing or activating one may take at most 30 seconds. They are not sandboxed: a plugin runs with the same rights as Agent-X. The host uses two plugin types today, document processors (formats no built-in processor reads) and data connectors (items for the Smart Inbox); the other declared types (AI provider, quick action, workflow step, theme, custom) load but nothing calls them. Uninstalling asks first and deletes the plugin's folder with its data. See [PLUGIN-DEVELOPMENT-GUIDE.md](PLUGIN-DEVELOPMENT-GUIDE.md) and `plugins/sample-plugin/`.

---

## Keyboard Shortcuts

Global shortcuts are defined in `ShortcutCatalog` (`src/AgentX.App/Services/ShortcutCatalog.cs`) and routed by `ShortcutInputRouter` from the root grid's `PreviewKeyDown`; pages add their own while they are open. The Command Palette and the Keyboard Shortcuts dialog read the same registry. [keyboard-shortcuts.md](user-guide/keyboard-shortcuts.md) has the full list.

| Shortcut | Action |
|---|---|
| `Ctrl+K`, `Ctrl+Shift+P` | Command Palette |
| `Ctrl+P` | Jump To (pages, documents, conversations) |
| `F1`, `Ctrl+Shift+/` | Keyboard Shortcuts |
| `Ctrl+N` | New Conversation |
| `Ctrl+,` | Settings |
| `Ctrl+D`, `Ctrl+I`, `Ctrl+F` or `Ctrl+Shift+F`, `Ctrl+G` | Dashboard, Knowledge Vault, Semantic Search, Knowledge Graph |
| `Ctrl+Shift+W`, `Ctrl+Shift+E`, `Ctrl+Shift+A`, `Ctrl+Shift+O` | Workflows, Web Import, Analytics, Operations |
| `Ctrl+1` to `Ctrl+9` | Dashboard, AI Chat, Ask Your Files, Semantic Search, Knowledge Vault, Collections, Workflows, Model Manager, Settings |
| `Win+Shift+A` | Quick Chat, system-wide |
| `Ctrl+Shift+N`, `Ctrl+B` (AI Chat) | New conversation, show or hide the conversation pane |
| `F5` (Knowledge Vault) | Refresh the document list |
| `Ctrl+S` (Settings) | Save settings |

---

## Pricing

Agent-X is free and open-source under the MIT License ([LICENSE](../LICENSE)). There are no paid tiers, subscriptions, activation, document limits or feature gates. Cloud providers (OpenAI, Anthropic) and web search services bill you directly under your own accounts.

---

## Project Structure

```
Agent-X/
    AgentX.sln                  Desktop app, core library, tests, LocaleAudit
    Directory.Build.props       Version 2.2.0, C# 12, shared build settings
    global.json                 .NET SDK 8.0.421, rollForward latestFeature
    src/
        AgentX.App/             WinUI 3 desktop app
            App.xaml.cs         Host, service registration, startup, logging
            MainWindow.*        Shell: navigation, Jump To, status strip, tray, onboarding
            Assets/             Icon and bundled fonts
            Controls/           Command Palette, faceplate, lamp tiles, chat messages,
                                notifications, OAuth App Credentials panel
            Converters/, Helpers/, Models/, Selectors/
            Services/           Shortcuts, localization, theme, tray, status strip, Operations
            Strings/<locale>/   Resources.resw for en-US, de, es, fr, ja, zh-CN
            Styles/, Themes/    Resource dictionaries (Command Console design system)
            ViewModels/         Page and dialog view models
            Views/              Pages, dialogs, Quick Chat window
            appsettings.json    Rag section
            RagPrompts.json     Retrieval prompts
        AgentX.Core/            Services, AI, search, data (no WinUI)
            AI/, Configuration/, Constants/, Data/, Documents/, DTOs/, Exceptions/,
            Helpers/, Math/, Observability/, Search/, Services/, Validation/
        AgentX.Mobile/          MAUI Android companion (not in AgentX.sln)
    tests/
        AgentX.Tests/           xUnit tests
        LocaleAudit.Tests/      Tests for the locale audit tool
    tools/LocaleAudit/          Translation key parity checker (CI gate)
    browser-extension/          AgentX Web Clipper (Manifest V3, TypeScript)
    plugins/sample-plugin/      Sample document processor plugin
    installer/AgentX-Setup.iss  Inno Setup 6 script (SLIM and OFFLINE profiles)
    scripts/                    build-installers.ps1, download-model.ps1, check-coverage.ps1,
                                uia-nav-smoke.ps1 and localization helpers
    models/                     The GGUF file the OFFLINE installer bundles (empty in git)
    docs/                       Documentation
```

---

## Technology Stack

| Category | Package | Version |
|---|---|---|
| UI framework | Microsoft.WindowsAppSDK (WinUI 3, self-contained) | 1.6.250108002 |
| MVVM | CommunityToolkit.Mvvm | 8.2.2 |
| WinUI controls and animations | CommunityToolkit.WinUI.Controls.Primitives, CommunityToolkit.WinUI.Animations | 8.1.240916 |
| Hosting and DI | Microsoft.Extensions.Hosting | 8.0.1 |
| ORM | Microsoft.EntityFrameworkCore.Sqlite.Core | 8.0.11 |
| SQLite driver | Microsoft.Data.Sqlite.Core | 8.0.11 |
| SQLite with encryption | SQLitePCLRaw.bundle_e_sqlcipher | 2.1.7 |
| Vector index | HnswLite | 1.0.6 |
| AI abstractions | Microsoft.Extensions.AI.Abstractions | 9.0.0-preview.9.24525.1 |
| Built-in model runtime | LLamaSharp, LLamaSharp.Backend.Cpu, LLamaSharp.Backend.Cuda12 | 0.19.0 |
| Ollama client | OllamaSharp | 4.0.6 |
| Speech-to-text | Whisper.Net, Whisper.Net.Runtime | 1.5.0 |
| Audio | NAudio | 2.2.1 |
| PDF reading | PDFsharp | 6.1.1 |
| PDF export | QuestPDF | 2024.12.2 |
| Word and PowerPoint | DocumentFormat.OpenXml | 3.2.0 |
| Markdown | Markdig | 0.37.0 |
| HTML parsing | HtmlAgilityPack | 1.11.67 |
| Headless browser | Microsoft.Playwright | 1.59.0 |
| Logging | Serilog, Serilog.Sinks.File | 4.0.2, 6.0.0 |
| System information | System.Management | 8.0.0 |
| Notification-area icon | H.NotifyIcon.WinUI | 2.1.3 |
| Tests | xunit, Moq, FluentAssertions | 2.9.2, 4.20.72, 6.12.2 |
| Installer | Inno Setup | 6 |
| Language | C# 12 | .NET 8 |
| Target framework | net8.0-windows10.0.22621.0 (minimum 10.0.19041) | |

---

## Further Documentation

| Document | Contents |
|---|---|
| [USER-GUIDE.md](USER-GUIDE.md) | The user guide |
| [Quick start](user-guide/getting-started/quick-start.md) | First steps |
| [FAQ](user-guide/faq.md), [Troubleshooting](user-guide/troubleshooting.md), [Glossary](user-guide/glossary.md), [Keyboard shortcuts](user-guide/keyboard-shortcuts.md) | Answers, fixes, terms and keys |
| [KNOWN-ISSUES.md](KNOWN-ISSUES.md) | Current limitations |
| [ARCHITECTURE.md](ARCHITECTURE.md), [DEVELOPER-GUIDE.md](DEVELOPER-GUIDE.md), [SERVICE-REFERENCE.md](SERVICE-REFERENCE.md) | Architecture, development and services |
| [API-REFERENCE.md](API-REFERENCE.md), [MOBILE-TRANSPORT.md](MOBILE-TRANSPORT.md) | Local REST API and the Android connection |
| [PLUGIN-DEVELOPMENT-GUIDE.md](PLUGIN-DEVELOPMENT-GUIDE.md) | Writing plugins |
| [CI.md](CI.md), [RELEASE-SIGNING.md](RELEASE-SIGNING.md) | CI workflows and signing releases |
| [DESIGN.md](../DESIGN.md) | The Command Console design system |

---

## Contributing

[CONTRIBUTING.md](../CONTRIBUTING.md) covers building, the tests, what CI checks (build and tests, formatting, locale parity, dependency audit, the extension and the Android build) and the code conventions. Report security problems as described in [SECURITY.md](../SECURITY.md), and see [CODE_OF_CONDUCT.md](../CODE_OF_CONDUCT.md). Bugs and feature requests go to [GitHub Issues](https://github.com/Git-Rocky-Stack/Agent-X/issues).

---

## License

Copyright (c) 2026 Rocky Elsalaymeh.

Agent-X is released under the MIT License; see [LICENSE](../LICENSE) at the repository root. You may use, copy, modify, merge, publish, distribute, sublicense and sell copies of the software, provided the copyright notice and the permission notice are included.
