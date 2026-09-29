# Agent-X User Guide

**Agent-X -- Local-First AI Personal Intelligence Hub for Windows**

Version 2.2.0 | Last updated: September 27, 2026

---

## Table of Contents

1. [Product Promise and Data Boundaries](#1-product-promise-and-data-boundaries)
2. [System Requirements](#2-system-requirements)
3. [Installation and Model Setup](#3-installation-and-model-setup)
4. [First Run Onboarding](#4-first-run-onboarding)
5. [Navigation Map](#5-navigation-map)
6. [Dashboard](#6-dashboard)
7. [Operations](#7-operations)
8. [AI Chat and Quick Chat](#8-ai-chat-and-quick-chat)
9. [Ask Your Files](#9-ask-your-files)
10. [Knowledge Vault](#10-knowledge-vault)
11. [Web Import](#11-web-import)
12. [Collections and Workspace Profiles](#12-collections-and-workspace-profiles)
13. [Semantic Search](#13-semantic-search)
14. [Knowledge Graph](#14-knowledge-graph)
15. [Compare Documents](#15-compare-documents)
16. [Quick Actions](#16-quick-actions)
17. [Workflows](#17-workflows)
18. [Smart Inbox](#18-smart-inbox)
19. [Weekly Digest](#19-weekly-digest)
20. [Past Self and Draft as Me](#20-past-self-and-draft-as-me)
21. [Analytics](#21-analytics)
22. [Model Manager](#22-model-manager)
23. [Hardware Advisor](#23-hardware-advisor)
24. [Backup and Restore](#24-backup-and-restore)
25. [Collaborative Sync](#25-collaborative-sync)
26. [Calendar and Email Connectors](#26-calendar-and-email-connectors)
27. [Annotations](#27-annotations)
28. [Settings](#28-settings)
29. [Command Palette, Jump To, and Shortcuts](#29-command-palette-jump-to-and-shortcuts)
30. [Instrument Strip, Notifications, and Tray](#30-instrument-strip-notifications-and-tray)
31. [Privacy and Security](#31-privacy-and-security)
32. [Troubleshooting](#32-troubleshooting)
33. [FAQ](#33-faq)
34. [Supported File Types](#34-supported-file-types)

---

## 1. Product Promise and Data Boundaries

Agent-X turns a Windows machine into a local AI workspace. It imports documents, indexes them, searches them by meaning, answers questions from them with citations, chats with a local or an optional cloud model, runs repeatable prompt workflows, and helps triage new material, without making a cloud account the center of the product.

### What stays local by default

- Documents' extracted text, chunks, embeddings, conversations, memories, workflow runs, sync logs, annotations, and settings are stored under `%LocalAppData%\AgentX\`. Imported files are not copied; the vault keeps the path to each original file.
- With the built-in local model (the default AI provider) or an Ollama server on the same machine, chat, embeddings, Ask Your Files, summaries, document analysis, and workflow runs execute on your machine.
- The database is encrypted only after you turn on database encryption in Settings (see [Database encryption](#database-encryption)).
- Search indexes live in SQLite and the local vector store. Already-installed local models need no internet connection.

### Optional external connections

Agent-X connects to services only when you set them up or ask for them:

- **OpenAI and Anthropic** when one of them is the active provider, or when Multi-Model Routing is on with their keys.
- **An Ollama server on another computer**, when the Ollama address in Settings points there.
- **Google and Microsoft** for the calendar and email connectors and their sign-in.
- **Web Import and the Research Mode search provider** when you import URLs or search the web from AI Chat.
- **Model downloads**: the built-in model, the speech-to-text model, and Ollama pulls, when you start them.
- **Collaborative Sync folders** on local, network, or cloud-synced drives.

When a cloud provider is active, your prompts and the context assembled for them are sent to that provider. Keep sensitive documents on the built-in model or a local Ollama model when data residency matters. The privacy line on the Dashboard and in an empty chat reads "100% Private" when nothing is configured to send data off the machine, and otherwise lists the services that do (a cloud AI provider, a remote Ollama server, model routing with a cloud key, web search, calendar sync, email sync).

---

## 2. System Requirements

| Component | Requirement |
| --- | --- |
| OS | Windows 10 build 19041 or later, or Windows 11 (the installer checks the build) |
| Architecture | x64 |
| Runtime | Nothing to install separately; the installer carries the app's runtime |
| Memory | No fixed minimum is enforced. The model you run must fit in free memory; the Hardware Advisor suggests model sizes for your GPU memory or RAM |
| Storage | The app, about 1.9 GB for the built-in model if you use it, about 142 MB for the optional speech-to-text model, plus space for Ollama models, the database, backups, and sync packages |
| GPU (optional) | The built-in model can offload layers to an NVIDIA GPU with 2 GB of video memory or more when the NVIDIA CUDA 12 Toolkit is installed. Ollama uses the GPUs it supports on its own |

Agent-X works on CPU-only systems; CPU inference is slower, so choose smaller models there.

---

## 3. Installation and Model Setup

### Install Agent-X

There are two installers for each release:

- `AgentX-Setup-<version>-x64.exe` does not include the built-in model. Onboarding offers **Download built-in model (~1.9 GB)**, and OpenAI or Anthropic keys work at once without it.
- `AgentX-Setup-<version>-x64-offline.exe` includes the built-in model (Llama 3.2 3B, Q4_K_M) for a first run without internet access.

1. Run the installer.
2. Launch Agent-X from the Start Menu or the desktop shortcut.
3. Complete first-run onboarding.

The installer creates the data folders under `%LocalAppData%\AgentX\`. Uninstalling removes the program and its log files and leaves your data folder, including a downloaded or bundled built-in model, in place.

### Install Ollama (optional)

Agent-X chats with its built-in local model by default, and OpenAI and Anthropic need only an API key. Install Ollama if you want to run other open models on your machine.

1. Download Ollama for Windows from `https://ollama.com/download`.
2. Install and start Ollama.
3. Verify it is available:

```powershell
ollama list
```

### Pull practical starter models

If you use Ollama, pull at least one chat model. Embeddings come from the Embedding Model setting: with its default value (`all-minilm`), Agent-X embeds with the built-in model when that model is installed and otherwise asks Ollama for `all-minilm`, so pull it too when the built-in model is not installed:

```powershell
ollama pull llama3.2
ollama pull all-minilm
```

Any other Ollama embedding model name in the Embedding Model setting (for example `nomic-embed-text` or `mxbai-embed-large`) is used through Ollama, and a `text-embedding-*` name uses OpenAI.

---

## 4. First Run Onboarding

On first launch, Agent-X hides the navigation pane and shows a five-step wizard. Back and Next move between the steps, and **Launch Agent-X** on the last step saves your choices.

### Step 0: Welcome

Three cards summarize the product:

- **Private & Secure**: with the built-in model, or Ollama on this computer, your documents and conversations are processed here; cloud providers, Research Mode web search, and the email and calendar connectors send data only when you set them up.
- **Free Local Models**: local models cost nothing to use; OpenAI and Anthropic Claude are optional and bill you for what you use.
- **Your Data, Your Rules**: index your documents, search them, and ask questions about them.

### Step 1: Connect to Ollama (optional)

Ollama is optional. The step shows the **OLLAMA ENDPOINT**, `http://localhost:11434` by default, and **Test Connection** checks it. If you do not use Ollama, choose **Skip for now, I'll set it up later**; the built-in model is offered on a later step.

### Step 2: Choose Your Models

When Ollama is connected, the step lists its models under **CHAT MODEL** and **EMBEDDING MODEL**; a model whose name contains `minilm`, `nomic`, `embed`, or `bge` is offered as an embedding model. A line above the lists shows your GPU, RAM, and the largest model size that fits. Without Ollama the step reads **No models available**, and you can pick models later in Settings or the Model Manager.

| Model role | Purpose | Used by |
| --- | --- | --- |
| Chat model | Writes text | AI Chat, Ask Your Files, Quick Actions, Workflows, Compare Documents, Draft as Me |
| Embedding model | Turns text into vectors | Indexing, Semantic Search, Ask Your Files retrieval, semantic duplicate scans |

Embedding model sizes, for reference: `all-minilm` about 46 MB (384 dimensions, the default setting), `nomic-embed-text` about 274 MB (768 dimensions), `mxbai-embed-large` about 670 MB (1024 dimensions). Changing the embedding model changes the vector space, so re-index existing documents afterwards.

### Step 3: Your AI is Ready

**BUILT-IN LOCAL MODEL** shows whether the built-in model file is installed (**Ready** with its size, or **Not installed yet**). **Download built-in model (~1.9 GB)** downloads it with a progress bar and makes it the active provider when it finishes. A line under it says how the model will run:

- **CUDA acceleration available** with the video memory and the number of layers it will offload;
- **NVIDIA GPU detected**, but GPU acceleration needs the NVIDIA CUDA 12 Toolkit;
- **CPU inference**, when there is no NVIDIA GPU or it has less than 2 GB of video memory.

**CLOUD PROVIDERS (OPTIONAL)** takes an OpenAI and an Anthropic API key. Keys are stored in `settings.json`, encrypted with Windows DPAPI, and are sent only to their provider.

### Step 4: You're All Set!

**CONFIGURATION SUMMARY** lists the Ollama status, chat model, embedding model, built-in model, cloud providers, and data storage (`%LOCALAPPDATA%\AgentX`). **Launch Agent-X** saves the Ollama address, the API keys, and the models you picked, and chooses the active provider: the built-in model when it is installed, otherwise Ollama when it is connected, otherwise OpenAI or Anthropic when you entered a key. Everything can be changed later in Settings.

After launch, the background indexer queues documents that were left pending or interrupted, and, with **Auto-index watch folders** on, Agent-X catches up on files added or changed in your watch folders while it was closed. The Dashboard opens.

### Re-running Onboarding

- **Reopen the wizard**: press `Ctrl+P` (Jump To) and choose **Onboarding**.
- **Dashboard > Setup AI** opens Settings, where the provider, models, and keys can be changed at any time.
- **Leaving early**: leaving the wizard before its final **Launch Agent-X** step (with a shortcut, the command palette, Jump To, the tray menu, or a status lamp) counts as skipping it, so it does not reappear on the next launch.
- **Force onboarding**: delete `%LocalAppData%\AgentX\settings.json` while the app is closed.
- **Skip onboarding**: set `"onboardingCompleted": true` in `settings.json` while the app is closed.

### After onboarding

1. Import a few representative documents (Knowledge Vault, `Ctrl+I`).
2. Ask a question about them in **Ask Your Files** (`Ctrl+3`) and check the cited sources.
3. Create a **Collection** for a project so that search and Ask Your Files can be limited to it.
4. Run a built-in **Workflow** template on a pasted text.
5. Turn on **Database Encryption** in Settings if the database should be encrypted at rest.

---

## 4.2 What Ships in 2.2.0

A summary of the main capabilities, each described in its own section.

### Search and answers

| Capability | Description |
| --- | --- |
| Hybrid search | Semantic (vector) and keyword (FTS5) results merged with Reciprocal Rank Fusion (k=60); vaults with fewer than about 10,000 embedded chunks are searched with an exact scan, larger ones through an HNSW index |
| Ask Your Files | Question rephrasings and, for long questions, a hypothetical answer (HyDE); hybrid retrieval, deduplication, reranking, neighboring text, compression; numbered citations |
| Conversation memory | Facts noted after each reply; up to eight that match a new message are added to its context; the Memories card in Inspect Context lists and deletes them |
| Knowledge Graph | Force-directed view of documents, collections, and tags, with search, zoom, and cluster highlighting |
| Tagging | After indexing, the AI model suggests up to five tags per document |

### Data layer

| Feature | Description |
| --- | --- |
| Database | SQLite through EF Core, upgraded by migrations at startup |
| Encryption | SQLCipher (opt-in), with a key tied to your Windows account |
| Vector store | Embeddings stored with each chunk; HNSW index for large vaults |
| Logs | Daily log files under `%LocalAppData%\AgentX\Logs\`, the last seven kept |

### Productivity

| Feature | Description |
| --- | --- |
| Workflows | Multi-step prompt chains you run on demand, with four built-in templates (Summarize & Act, Research Brief, Document Review, Content Repurpose), run history, and token counts |
| Quick Actions | Layered summary and key points for one document, translation of pasted text (ten languages), exact and semantic duplicate scans, collection and tag suggestions |
| Compare Documents | Similarities, differences, contradictions, and unique points across two or more documents |
| Batch operations | Multi-select documents in the Knowledge Vault to re-index or delete them |
| Web Import | URLs, RSS or Atom feeds, sitemaps, and YouTube captions into the vault |
| Smart Inbox | Triage for browser clips; connector and plugin items arrive already accepted |

### Other features

| Feature | Description |
| --- | --- |
| Analytics | 30-day activity, model usage, document types, response times, workflow and conversation statistics |
| Collaborative Sync | Encrypted record exchange through a shared folder, with last-writer-wins conflicts and optional auto-sync |
| Calendar and email connectors | Read-only Google and Microsoft connectors, with your own OAuth client, that import events and messages |
| Local API | Token-protected HTTP listener on `localhost:9846` for the browser extension and the Android app (`/api/documents`, `/api/conversations`, `/api/search`, `/api/collections`, `/api/inbox/clip`, and health checks) |
| Plugins | `.agentx-plugin` packages that add document formats (document processors) or push items into the Smart Inbox (data connectors); other plugin types are listed but not called yet. Plugins run unsandboxed, and Plugin Manager renders each plugin's README |
| Past Self and Draft as Me | A local record of stances you state in chat, looked up by topic and time, and drafts in your voice written by your AI provider |

### Chat details

| Feature | Details |
| --- | --- |
| Per-message actions | Rate, copy, and delete replies; regenerate the latest reply; copy, edit (Save & Resend), and delete your own messages |
| Code highlighting | Code blocks in replies are highlighted for languages such as C#, Python, JavaScript, TypeScript, SQL, JSON, HTML, XML, Rust, Go, Java, Bash, YAML, CSS, and C/C++ |
| Conversation folders | Work, Research, Personal, Archive, or a folder you name |
| Themes | Dark, Light, or System Default |

### Localization

| Language | User interface |
| --- | --- |
| English (en-US) | Yes |
| German (de) | Yes |
| Spanish (es) | Yes |
| French (fr) | Yes |
| Japanese (ja) | Yes |
| Chinese, Simplified (zh-CN) | Yes |

A few messages that are built in code, such as the Operations suggested fixes and some chat dialogs, still appear in English in every language.

---

## 5. Navigation Map

The navigation rail groups the pages as follows.

| Group | Pages |
| --- | --- |
| Intelligence | Dashboard, Operations, Weekly Digest, Analytics, Past Self, AI Chat, Ask Your Files, Quick Actions, Workflows |
| Knowledge | Knowledge Vault, Web Import, Collections, Semantic Search, Knowledge Graph, Compare Documents, Annotations |
| Triage | Smart Inbox |
| System | Model Manager, Hardware Advisor, Backup & Restore, Workspace Profiles, Plugin Manager, Collaborative Sync, Calendar, Email |
| Support | User Guide, Privacy Policy, Terms of Service |

Settings sits at the bottom of the rail. The command palette (`Ctrl+K`) and Jump To (`Ctrl+P`) reach the same destinations from the keyboard.

---

## 6. Dashboard

The Dashboard is the page Agent-X opens on at startup (after onboarding), and `Ctrl+D` returns to it. It summarizes the state of your AI provider, your vault, and your recent work.

### What to check first

- **AI connection status:** names the active provider (the built-in model by default), whether it is reachable, and the current model; when the provider cannot be reached, it says what to check for that provider. **Setup AI** opens Settings.
- **Quick search:** type a query and press Enter to open Semantic Search with it.
- **Document and storage figures:** document and collection counts, the storage documents use and the share already indexed, conversations and tokens used, and GPU, RAM, and video memory.
- **Recent documents and recent conversations:** the latest imports and chats. The entries are a list only; **View All** opens the Knowledge Vault or AI Chat.
- **Operations Overview:** conversation intelligence, sync health, connectors and plugins, the Smart Inbox backlog, and workflow activity, each with a button to its page.
- **Your Belief Evolution:** up to five topics whose recorded stance changed, with the earlier and the current stance; acknowledge one to dismiss it, or open Past Self. Until a view has been recorded, the card reads "No beliefs to compare yet"; with views recorded and none changed, it reads "Your beliefs are consistent", and right after you acknowledge the last change, "No open conflicts".
- **Recommended Next Steps:** setup, remediation, and next steps such as finishing AI setup, indexing waiting documents, triaging the inbox, configuring sync, or creating a workflow.
- **Status strip:** available RAM, vault storage, and the privacy line ("100% Private" or the list of services that send data off the machine).

### Common dashboard flows

| Goal | Action |
| --- | --- |
| Start chatting | **New Chat** starts a new conversation, like `Ctrl+N` |
| Add source material | **Import Files** opens the Knowledge Vault |
| Search the vault | **Search** or the quick search box |
| Ask grounded questions | **Ask Files** |
| Run a document task | **Quick Actions** |
| Repair setup | **Setup AI** or the recommended step |

Use **Refresh** when another page has changed documents, models, conversations, sync, or indexing state.

---

## 7. Operations

The Operations page (`Ctrl+Shift+O`) gathers the status of work Agent-X does in the background. It reads the status when you open the page and when you click **Refresh**; it does not update by itself.

### Status areas

**CURRENT OPERATIONS STATUS** says how many areas need attention and shows a tile for each.

| Area | What it shows | Buttons |
| --- | --- | --- |
| Conversation intelligence | Durable conversation summaries and recent summaries | Refresh Summaries, Open Analytics |
| Sync health | Collaborative Sync state and recent sync passes | Sync Now, Open Sync |
| Ingestion backlog | Pending Smart Inbox items and recently imported documents, with indexing state | Generate Previews, Open Inbox, Open Vault, Retry Index on a document |
| Workflow activity | Recent workflow runs | Open Workflows |
| Connectors & plugins | Connector and plugin status | Enable Connector, Open Plugin, Open Plugin Manager |

### Suggested Fixes

Suggested Fixes lists what the current status calls for, each with a button that does it: refresh stale conversation summaries, run a manual sync or finish setting up sync, generate AI previews for pending inbox items, retry indexing a recently imported document that needs attention, enable a connector that is off (or open Plugin Manager when none is enabled), and review a workflow run that needs it. When nothing needs fixing, it suggests opening Analytics. The texts of these suggestions are in English in every language.

### Drill-in behavior

Clicking an item opens its page with the item in focus: a sync entry opens Collaborative Sync with that history entry pinned, and a workflow run opens Workflows with that stored run at the top. Pages opened this way show an **Opened from Operations** badge.

---

## 8. AI Chat and Quick Chat

AI Chat is the full conversational workspace. Quick Chat is a small window for one-off questions.

### Core chat workflow

1. Select **AI Chat**, press `Ctrl+2`, or press `Ctrl+N` for a new conversation.
2. Pick a model in the model box at the top when needed; it lists the active provider's models.
3. Type in the message box. Enter sends and Shift+Enter starts a new line.
4. The reply appears as it is written; **Stop generation** ends it early and keeps the text written so far.
5. Up to three suggested follow-up questions appear under the reply; clicking one puts it in the message box.

AI Chat does not search the Knowledge Vault. Ask about your documents in [Ask Your Files](#9-ask-your-files).

### Conversation management

- Conversations are stored in the local SQLite database (encrypted only when database encryption is on).
- The sidebar lists pinned conversations first, then the most recent. Search matches titles and message text; folder buttons (All plus each folder in use) filter the list.
- Right-click a conversation to pin, unpin, or delete it (deletion asks first). The folder button in the top bar moves the open conversation to Work, Research, Personal, Archive, or a named folder. Conversations cannot be renamed, and there is no clear-all command.
- `Ctrl+N` (anywhere) or `Ctrl+Shift+N` (on the chat page) starts a new conversation; `Ctrl+B` shows or hides the conversation list.
- Each conversation keeps its own history. When a conversation no longer fits in the Context Window set in Settings, Agent-X keeps the latest messages and the earlier ones that best match the new question, and may add a summary of the rest.
- **Export conversation** (top bar) saves the open conversation as Markdown, HTML, PDF, JSON, plain text, CSV, Word (.docx), or PowerPoint (.pptx), with switches for **Include citations**, **Include model info**, **Include metadata**, and **Include timestamps**; a Markdown export can follow a template (Research Report, Executive Summary, or Annotated Bibliography). **Copy as Markdown** in the same dialog puts the conversation on the clipboard instead. **Export all conversations** (bottom of the conversation list) saves every listed conversation in one Markdown file. Files go to the Exports folder under the Agent-X data folder, and Agent-X says where.
- Every answer is saved with the name of the model that wrote it, and a Research Mode answer with the web pages it was given. In the export dialog, **Include citations** lists each answer's sources under it (numbered as its [n] markers) and **Include model info** names its model.

### Prompt and context tools

| Feature | Use |
| --- | --- |
| System prompts | Apply one of ten built-in prompts (such as Code Helper or Socratic Teacher) to the messages you send; custom prompts cannot be added yet |
| Conversation memory | After each reply, a background model call notes facts worth keeping; up to eight stored facts that closely match a new message are added to its context, whichever conversation they came from. With a cloud provider this is one more billed call per reply |
| Inspect Context | Shows what was assembled for a reply, including the conversation's Durable Summary (which can be refreshed there) and messages recalled from earlier conversations. Its **Memories** card lists every noted fact and how many are stored: the delete button removes one for good, and **Clear all** asks first, then deletes them all |
| Branching | **Branch from here** on one of your own messages continues in a separate branch; the **Branches** list opens, merges (after asking), or deletes branches. **Compare branches** opens a window with the open branch next to the thread it was branched from, showing their titles, labels, branch point, and number of sub-branches, not their messages |
| Mode (Solo, Multi, Debate) | Solo is the normal chat. Multi sends your message to a researcher, a critic, and a synthesizer agent in parallel and combines their answers; Debate lets a researcher, a critic, and a creative agent argue for two rounds and then writes a synthesis. In Multi and Debate the answer appears when it is complete, several model calls are made, and the agents see your new message, the active system prompt, and any Research Mode results, not the earlier messages of the conversation |
| Research Mode | The globe button beside the message box adds web search results to your next answers. It also needs **Enable Research Mode** in Settings (the switch reads "Chat can add web results" when on and "No web search" when off) and a search provider. The search results an answer was given (title, address, and snippet only; pages are not fetched) are listed under it as numbered **Web sources** chips, and clicking one opens it in your browser. The message text is the search query, only the selected provider is used, and AI Chat never searches the Knowledge Vault |
| Voice input | Click the microphone to dictate into the message box, or right-click it to transcribe an audio file; it needs the speech-to-text model from the Model Manager page (see [Audio](#audio)) |

### Message behavior

Replies render basic Markdown: headings, lists, bold, inline code, and code blocks with a language label, syntax highlighting, and a Copy button. Tables, block quotes, and math are not rendered. Replies can be rated, copied, or deleted, and the latest reply can be regenerated; your own messages can be copied, edited (**Save & Resend**), or deleted.

### Quick Chat

Press `Win+Shift+A` anywhere in Windows, or choose **Quick Chat** in the tray menu, to open a small always-on-top window. It sends one question at a time to the active AI provider and shows the reply; it does not search the Knowledge Vault, and questions and replies are not saved. When **Screen awareness in Quick Chat** is on (Settings, Research Mode section; off by default), Quick Chat also reads the title and the on-screen text (OCR) of the window that was in front when you opened it and adds them to the question; with a cloud provider, that text is sent too.

---

## 9. Ask Your Files

Ask Your Files is Agent-X's retrieval-augmented question answering. It searches your indexed documents, gives the best passages to the active chat model, and shows the answer with numbered citations.

### When to use it

- Ask questions across the whole vault or one collection.
- Get answers grounded in imported documents.
- Find the source passages behind a claim.

### How it works

1. Pick a collection in the list at the top, or leave it on **All Collections**. Single documents cannot be selected.
2. Type a question and press Enter.
3. Agent-X asks the model for a few rephrasings of the question and, for questions of 80 characters or more, drafts a hypothetical answer (HyDE); each version is searched with semantic and keyword retrieval, merged with Reciprocal Rank Fusion.
4. The passages are deduplicated, reranked (a pass that spreads results across documents, then a model-based pass), widened with neighboring text, and compressed, keeping as many as **Top-K Results** in Settings allows (5 by default).
5. The model writes the answer from the passages, citing them as [1], [2], and so on.

### Reading citations

The **Sources** panel lists each cited passage with its number, document name, page number when the format has pages, match percentage, and an excerpt; badges under the answer repeat the cited numbers with the document names. **Open source document** shows the file in File Explorer; it does not jump to the passage. Numbers with no matching passage are left out of the panel, and the [n] markers in the answer text are plain text. If a citation looks weak, rephrase the question, narrow the collection, or re-index the document. Answers are not saved with your conversations and cannot be exported.

---

## 10. Knowledge Vault

The Knowledge Vault (`Ctrl+I`) is the document repository and indexing control center. It stores each document's extracted text, chunks, and the path to the original file; the file itself is not copied, so re-indexing needs it to stay where it was imported from. The search box filters by part of a file name or tag name, ignoring case; it does not search document content, which Semantic Search and Ask Your Files do.

### Import methods

- **Import Files**: pick one or more files. The picker offers every format Agent-X can read, including images, audio, web shortcuts and formats added by active plugins.
- **Import Folder**: imports every supported file in a folder and its subfolders, including images and audio.
- **Drag and drop**: while the vault list is empty, a drop zone fills the page; drop files or folders on it. Once documents are listed there is no drop area.
- **Watch folders** (Settings > Knowledge Vault > Watch Folders): folders Agent-X keeps importing from. Add one with **Add Folder**, with or without its subfolders. While **Auto-index watch folders** is on, Agent-X imports the supported files already in it, then new and changed files while it runs. Removing a folder stops watching it; documents already imported stay. Adding or removing a folder takes effect at once, and turning the switch on or off takes effect when you save settings.
- Web Import, accepted Smart Inbox items, and workflow results saved as documents.

Import Files and drag and drop first compare each file's content with the vault: exact copies of documents already there are listed as duplicates, and you choose **Skip Duplicates** or **Import All**.

### Document details

Each row shows the file name, type, indexing status, the extracted or generated title, page, word, and chunk counts, size, import date, and tags; a Failed row also shows why indexing failed. Select a row or click **Detail** to open the Document Preview, which adds the reason under **WHY INDEXING FAILED**, **Open in Explorer**, and **Workflow** (opens the Workflows page with the document's name, summary, and a text preview as input). Under **DOCUMENT TEXT**, the preview shows the indexed text one passage at a time; select text there to create an annotation (see [Annotations](#27-annotations)).

### Deleting and bulk operations

**Delete document** on a row, or **Delete** for several selected documents, asks first, naming the document or giving the count; Cancel is the default. Deleting removes the document from the vault together with its text, chunks, embeddings, keyword entries, tags, collection memberships, and annotations, and cannot be undone; the original file on disk is not touched. Turn on multi-select mode, or use **Select All**, to re-index or delete several documents together. There is no bulk collection assignment or tagging on this page; tags come from auto-tagging, and documents join a collection through the Collections page.

### Indexing lifecycle

| Status | Meaning |
| --- | --- |
| Pending | Imported, waiting to be indexed |
| Processing | Being extracted, chunked, and embedded |
| Indexed | Search and Ask Your Files can use it |
| Failed | The row, the Document Preview, and the Operations page show why |

Re-index (`Re-index` on a document, or `F5` to refresh the list) after changing the embedding model, moving source files, or fixing an extraction failure.

Documents indexed by earlier versions of Agent-X, which did not record the embedding model behind each chunk, are embedded again automatically while nothing else is being indexed. Their stored chunk text is reused (nothing is extracted again) and embedded with the embedding model currently selected in Settings.

---

## 11. Web Import

Web Import (`Ctrl+Shift+E`) turns web pages into vault documents. Each page is saved as a Markdown file in the `WebImports` folder of the data folder and indexed like a local file. A page whose text is already in the vault is reported as a duplicate and not imported again.

### Single-page import

1. Paste one or more URLs, one per line, under **Enter URLs** (web articles and YouTube videos).
2. **Preview First URL** shows the first page's title, site, author, word count, and extracted text without importing it.
3. Pick a collection under **Add to Collection (optional)** if you like; there is no tag field.
4. **Import All** fetches, extracts, and imports each URL in turn.

### Batch and discovery flows

Web Import also accepts several URLs at once, an RSS or Atom feed (**Feed URL**, **Import Feed**: it imports the items the feed lists at that moment; feeds are not re-checked on a schedule, so import the feed again for new items), and a sitemap (**Sitemap URL**, **Import from Sitemap**: it imports up to 100 pages the sitemap lists). Results show success and failure counts, imported document names, word counts, and error messages for failed URLs.

### What is extracted

- A readability pass removes scripts, navigation, headers, footers, sidebars, form controls, embedded media, figures and captions, hidden elements, and blocks whose class names mark them as comments, ads, or cookie notices, then keeps the article (or the densest block of text).
- The text keeps paragraphs, headings as separate lines, list items, and data tables (as Markdown tables); heading levels, emphasis, code formatting, and link addresses are dropped.
- The title, author, publication date, site name, and description come from the page's meta tags, OpenGraph, and JSON-LD data.
- For a YouTube URL, the video's captions become the text; a video without captions fails with a message.
- When a page is empty, or runs scripts and shows fewer than 200 visible characters, Agent-X loads it again in a headless Chromium browser (Playwright) and extracts the rendered page. This needs a Playwright Chromium browser on the computer, which Agent-X does not install; without one, the page as downloaded is used. There is no setting or per-site choice for this.

A URL you type may point to this computer or your local network, so intranet pages import normally. Links that come from remote content (the items of a public feed or sitemap, a redirect from a public page, and what a public page loads when it is rendered) may not; they fail with a "Blocked" message. The check is repeated when the connection is opened, against the address the name resolved to when it was checked. Cloud metadata endpoints such as 169.254.169.254 are never read, even from a URL you type.

### Best practices

- Prefer canonical article URLs over homepages.
- Use collections to group imported sources by project.
- Re-index imported pages if the embedding model changes.
- Check failed rows for paywalls, pages that need scripts, or blocked fetches.

---

## 12. Collections and Workspace Profiles

Collections organize documents inside your vault. Workspace Profiles are saved presets you can keep for reference.

### Collections

Use Collections for project, client, research area, or topic groupings. A document can belong to several collections, and a collection can hold sub-collections one level deep.

| Action | Result |
| --- | --- |
| Create collection | Adds a new collection with a name and an optional description |
| Rename collection | Changes the name only |
| Export collection | Saves a ZIP to the Exports folder of the data folder with `manifest.json` (the collection's details and each document's name, path, type, size, import date, page and word counts, summary, language, indexing status, and content hash) and `README.txt`; it contains no files, text, chunks, or embeddings and cannot be imported |
| Add documents | Imports the picked files and adds them to the collection. A file already in the vault adds the existing document instead of a copy, and a summary reports how many files were added, were already in the collection, or failed |
| Remove documents | Removes only the collection relationship |
| Move into... | Opens Move Collection; its Destination list offers Top level (for a sub-collection) and the other top-level collections. A collection that has sub-collections of its own stays at the top level |
| Delete collection | Asks first and names the collection (Delete Selected asks with the count); Cancel is the default. Its documents stay in the vault, and its sub-collections move up one level |

Collections scope Semantic Search, Ask Your Files, workflow document lookups, the vault filter, and Collaborative Sync.

### Workspace Profiles

A workspace profile is a named preset that stores a description, an Ollama model identifier, a comma-separated list of collection IDs, and free-form custom settings. Profiles are records only:

- Selecting, saving, or marking a profile as the default does not switch the active model, change the collections in scope, or change any setting.
- Agent-X does not load a profile at startup, including the default profile.
- All profiles share the same vault, conversations, and settings. A profile is not a separate environment.

No profile exists until you create one, and any profile can be deleted.

| Action | Result |
| --- | --- |
| Create Profile | Adds a profile with a name and an optional description |
| Save Profile | Stores the edited fields and the Default Profile switch |
| Set as Default | Marks the profile as the default and removes the mark from every other profile |
| Duplicate | Copies the profile as "Name (Copy)", without the default mark |
| Delete Profile | Removes the profile only; documents, collections, and conversations are not affected |

At most one profile carries the default mark. Turning the Default Profile switch off and saving leaves no default profile.

---

## 13. Semantic Search

Semantic Search (`Ctrl+F`) finds meaning, not just exact words. It can search by vector similarity, keyword FTS5, or hybrid ranking.

### Search modes

| Mode | Best for |
| --- | --- |
| Semantic | Concepts phrased differently from the original text |
| Keyword | Exact names, codes, invoice numbers, quoted phrases |
| Hybrid | Broad discovery where both meaning and exact terms matter |

Hybrid search merges semantic and keyword results using Reciprocal Rank Fusion so strong candidates from either backend can rank well. The Search page opens in Semantic mode; Ask Your Files always retrieves in hybrid mode.

### Result tools

- Relevance score (one score per result; no semantic or keyword sub-scores).
- Source document, excerpt, page number where the format has pages, and chunk number.
- Search history list for repeating recent queries.
- Saved filters (query, mode, advanced filters, and sort order).
- Collection, file-type, minimum-relevance (30% by default), maximum-results (20 by default), and import-date filters (CREATED AFTER and CREATED BEFORE compare the date a document was imported).
- Open shows the file in File Explorer; Workflow sends the result to the Workflows page.

Phrases such as "in the Pricing collection" or "from last week" in a query are searched as ordinary words; use the filters instead.

### Search quality tips

- Index documents before searching.
- Use natural questions for semantic mode.
- Use exact terms for keyword mode.
- Use hybrid mode when unsure.
- Re-index after embedding model changes.

---

## 14. Knowledge Graph

Knowledge Graph (`Ctrl+G`) visualizes how documents relate through their collections and tags.

### What it shows

- Document nodes sized by chunk count.
- Collection and tag nodes (fixed sizes), colored as in the legend.
- Edges from each document to its collections and tags, and between two documents that share a collection or tag (thicker when they share more). There are no content-similarity edges.
- Counts for documents, collections, tags, and connections.

### Controls

- Refresh rebuilds the graph and its force-directed layout, which is computed once per build.
- The SHOW check boxes hide or show documents, collections, and tags.
- Zoom with the mouse wheel or the zoom buttons (0.25x to 4x) and reset the zoom; the view does not pan.
- Search highlights the nodes whose name or subtitle contains the text.
- Click a node to see its details; clicking a collection or tag also highlights its cluster. Nodes do not open documents, and there is no context menu.

Use the graph to spot isolated documents, densely connected topics, and clusters that deserve their own collection.

---

## 15. Compare Documents

Compare Documents analyzes two or more vault documents together. It takes the passages of each document that best match the focus topic (or a general overview query) and asks the active model for a structured comparison.

### Inputs

- Select at least two documents.
- Optionally enter a focus topic.
- The **Detail Level** list (summary or detailed) does not change the report in this version.

### Output

| Section | Description |
| --- | --- |
| Summary | Overall comparison, with the tokens used and the time taken |
| Similarities | Shared ideas, claims, themes, or structure |
| Differences | Where documents diverge |
| Contradictions | Conflicts that may need verification |
| Unique Points by Document | Document-specific findings grouped by source |

**Export Report** saves the comparison as Markdown.

---

## 16. Quick Actions

Quick Actions runs AI and maintenance tasks from one page. The summary and key points work on one selected, indexed document; the other tabs do not need a selection. Results appear on the page; nothing is saved to the document or applied automatically.

### Available actions

| Tab | Output |
| --- | --- |
| Summarize | Layered summary: section summaries combined into a final overview |
| Key Points | Bullet list of the document's main facts and takeaways |
| Translate | Translation of pasted text into one of ten languages (Spanish, French, German, Chinese, Japanese, Korean, Portuguese, Italian, Russian, Arabic); longer text is translated in parts split at paragraph, line, or sentence breaks |
| Duplicates | Exact duplicate groups (same SHA-256 hash) or semantic near-duplicate groups; report only |
| Organize | Collection and tag suggestions for up to 20 documents outside any collection; not applied automatically |

There is no rewrite, explain, or Q&A generation action, and results are not exported or saved as annotations.

### Contextual guidance

The Recommended For This Context panel suggests up to four next steps based on the selected document's indexing state, the Smart Inbox backlog, and connector setup. If no document is ready, follow the guidance to import or index one first.

---

## 17. Workflows

Workflows (`Ctrl+Shift+W` or `Ctrl+7`) are reusable multi-step prompt chains. Each step's output feeds the next, which suits tasks that need the same logic repeatedly, such as preparing briefs, extracting action items, or repurposing content.

### Built-in starter templates

The Workflows page includes four built-in templates, each made of AI prompt steps:

- **Summarize & Act**: summary, key points as a numbered list, and prioritized action items.
- **Research Brief**: topic analysis, key arguments, and a structured brief.
- **Document Review**: summary, strengths and weaknesses, and improvement suggestions.
- **Content Repurpose**: the core message, then a tweet thread, a professional email, and a blog post.

Template guides explain best-fit inputs, expected outcomes, and example use cases. **Use Template** copies a template so you can change its steps.

Workflows run only when you click Run. There are no schedules, event triggers, or notifications.

### Creating a workflow

1. Click **New Workflow** (or **Use Template**), and name the workflow.
2. Click **Add Step** for each step and pick its **Step Type**: AiPrompt, DocumentLookup, TextTransform, ConditionalBranch, or OutputFormat.
3. Write each step's prompt template, where `{{input}}` stands for the workflow's input and `{{previous_output}}` for the previous step's output, and fill in the step's settings when its type has them (see below).
4. Arrange the steps with the move up and move down buttons, then **Save Workflow**.

The editor has no per-step model, temperature, or token-limit fields. Steps use the active model; some built-in template steps carry a temperature, and an exported workflow JSON file keeps any per-step overrides when you bring it back with **Import Workflow**.

### Step types and settings

- **AiPrompt** sends its prompt to the active AI provider.
- **DocumentLookup** asks your documents the way Ask Your Files does, with the prompt as the question, and passes on the answer. Settings (optional): `{"collectionId": 3}` limits it to the collection with that ID.
- **TextTransform** applies one operation to its template text, or to the previous output when the template is empty. Settings (optional): `{"transform": "lowercase"}`; the transforms are uppercase, lowercase, titlecase, trim, extract_lines, word_count, char_count, reverse_lines, deduplicate_lines, sort_lines, and number_lines. Without settings, the text is made uppercase.
- **ConditionalBranch** tests the previous step's output and outputs one of two texts; it does not skip steps. Settings (required): `{"condition": "contains", "value": "urgent", "trueBranch": "Urgent: {{previous_output}}", "falseBranch": "{{previous_output}}"}`. The conditions are contains, not_contains, starts_with, ends_with, equals, matches (a regular expression), and length_greater_than (a whole number in quotes, such as "100"); comparisons ignore letter case. A branch that is left out passes the previous output through.
- **OutputFormat** reformats its template text or the previous output. Settings (optional): `{"format": "bullet_list"}`; the formats are json, markdown, html, bullet_list, and numbered_list, and `prefix` and `suffix` add text before and after the output.

The empty settings box shows an example, the line under the box lists what the type reads, and a mistake is described under the box as you type. A workflow is not saved while any step's settings have a problem. Setting names are case-sensitive, and text values go in double quotes.

### Running and inspecting

Type or paste the input under **Run Workflow** and click **Run**; **Cancel** stops the run. **Step Outputs** shows what each step produced, with tokens, and **Final Output** the result. Runs record progress, per-step output, model, tokens, duration, and any error, and **Recent Runs** reopens stored results without running again, also from Operations drill-ins.

### Saving and exporting

**Copy to Clipboard** copies the result, **Save as Document** adds it to the Knowledge Vault (it becomes searchable after indexing), and **Export Result** saves it as a file. **Export JSON** saves a workflow definition, and **Import Workflow** brings one back.

---

## 18. Smart Inbox

Smart Inbox is where pages you clip with the browser extension wait until you decide what to do with them; they reach the Knowledge Vault only when you accept them. Calendar events and emails from the connectors, and items that data connector plugins add, appear here already accepted, because they are imported into the vault as they arrive. Files from watch folders go straight to the vault.

### Inbox item details

Each item can show its file name, source (a **Web Clip** badge for browser clips), the **AI Preview** when generated, the suggested collection and tags, and its status. The **STATUS** list (pending, accepted, rejected, deferred, or all) chooses which items are shown.

### Actions

| Action | Result |
| --- | --- |
| Generate AI Previews | For each pending item without a preview, asks the active AI provider for a two- or three-sentence summary of the first 2,000 characters, a suggested collection, and up to five tags |
| Accept | Imports the item into the vault, into the collection chosen under **TARGET COLLECTION** or else the suggested one (when it names one of your collections). Suggested tags are only shown |
| Defer | Changes the status only |
| Reject | Changes the status only; nothing is imported |
| Accept All Pending | Accepts every pending item, each into its suggested collection |
| Clean Up Processed | Removes accepted and rejected items from the list and keeps deferred ones; documents already imported stay in the vault |
| Opened from Operations | An item opened from Operations is shown in focus with this badge |

---

## 19. Weekly Digest

Weekly Digest is a statistical report on the last seven days, computed from the database; no AI model writes it.

### Digest contents

- New documents, new conversations, searches, tokens used in chat messages, and the storage the new documents added.
- Top searches, top collections (by documents added), and imported file types, up to five each, with a trend against the previous seven days.
- The three most active conversations, with their message and token counts.

### Actions

- Generate New Digest builds a report for the last seven days; the window is fixed and nothing runs on a schedule.
- Review earlier reports in the report history.
- Reports stay in the app: there is no export or email delivery.

Use Weekly Digest at the end of a project week to see what came in, what you searched for, and where your conversations went.

---

## 20. Past Self and Draft as Me

Agent-X keeps a local record of views you state in AI Chat, built with fixed word rules on English phrasing, not with an AI model: a sentence with "I think", "I believe", or "I feel" followed by "that" becomes a belief with a topic and a stance, and each message you send updates a simple voice profile (words per sentence and formality). AI replies with marker words such as "key insight" or "important", and each annotation you save, are also kept as insights.

### Past Self

- **Time period**: All time, Past week, Past month (the default), Past year, or Custom date. All time looks up the stance you recorded first; the other options look up the stance you held at that moment.
- **Search Past Self** finds a topic typed with the words it was recorded in (capitals and surrounding spaces do not matter). It shows your stance then, with a confidence bar, an excerpt of the message it came from, and up to three related conversations and documents. When your view changed later, a "Your view has evolved" tag appears, and "Your view since <date>" gives your current stance.
- **Show Belief Evolution** says since which month a topic has been tracked, whether it has changed, and your current stance. The earlier stance is shown on the Dashboard's belief card.
- **Get Active Topics** lists up to 15 topics you stated views on in the last 30 days, with when each was first and last recorded.
- **Get Relevant Insights** lists up to five saved insights that contain a word you typed. Insights cannot be edited or deleted in the app.
- **Load Profile** shows the voice profile: samples, average sentence length, and a style label (Casual, Balanced, or Formal).

### Draft as Me

Enter what you want to write and, if you like, a goal, then click **Generate Draft**. Your active AI provider writes the draft; the request includes your voice profile, up to five stances you had recorded by the chosen time on topics that share a word with your request, and up to three related insights. The draft appears as it is written; **Cancel** stops it. A note under the draft names the model and the views that were used. **Copy** puts the finished draft on the clipboard. With a cloud provider, this text leaves your computer; nothing is sent when no AI provider is available.

---

## 21. Analytics

Analytics (`Ctrl+Shift+A`) shows how you use Agent-X, computed on this computer from your own data when the page opens and when you click **Refresh**.

### Metric groups

| Group | Contents |
| --- | --- |
| Counters | Conversations, messages, tokens used (with the average per message), documents (indexed and pending), searches, average response time, tokens per conversation, and knowledge indexing |
| Workflow Intelligence | Total runs, success rate, average run duration, active workflows, a 30-day run trend, top workflows, and recent runs |
| Conversation Intelligence | Durable conversation summaries: how many exist, are stale, or wait for a refresh |
| Semantic Recall Probe | Search past messages across conversations by meaning |
| Conversation Themes and Theme Trends | Clusters of conversations built from durable summaries, and their activity |
| Activity Trends | Conversations, documents imported, and searches per day over the last 30 days |
| Model Usage | Models by conversation count |
| Document Types | File type breakdown of the vault |
| Performance Metrics | Average, median, P95, fastest and slowest response, throughput, and total inference time |

### Use cases

- See which workflows are actually used and how often they fail.
- Check whether conversation summaries are fresh.
- Identify dominant topics.
- Watch indexing and search activity after a large import.
- Compare response times before changing models.

---

## 22. Model Manager

Model Manager works on the active AI provider: Ollama models when Ollama is active, the model files (.gguf) of the built-in provider when it is active (only the Llama 3.2 3B and 1B files of its catalog can be downloaded; other GGUF files can be copied into its models folder), and a read-only list for OpenAI and Anthropic. It also installs the speech-to-text model.

### Capabilities

- List installed models with family, parameter count, quantization, context length, size, and last-modified date.
- **Set Active** makes a model the active provider's model everywhere and keeps it after a restart.
- Pull a model by name and track the progress.
- Delete unused models. Delete is one click per model, with no confirmation and no undo; pull the model again if you remove one by mistake.
- Refresh the installed model list.

### Speech-to-Text Model

This section installs the Whisper base model (`whisper-base`, about 142 MB) that transcribes imported audio files and voice input in AI Chat on this computer. It reads **Installed** with the size on disk, or **Not installed**. **Download** fetches the model with a progress bar, **Cancel download** stops and keeps nothing, and **Remove** deletes it. Nothing is downloaded until you choose Download. After a download, audio files that could not be transcribed without the model are queued again, and the page says how many.

### Model naming tips

Use explicit model names where possible:

```powershell
ollama pull llama3.2
ollama pull mistral
ollama pull nomic-embed-text
```

Embedding models should include names such as `embed`, `nomic`, `bge`, or `minilm` so onboarding can offer them as embedding models.

---

## 23. Hardware Advisor

Hardware Advisor detects system capacity and recommends suitable Ollama models. Recommendations depend on one figure: the GPU's video memory, or the available system RAM when no GPU memory is reported. **Install** downloads a recommended model through the active provider, which works when Ollama is active. The advisor runs no benchmarks and does not check CUDA, ROCm, or oneAPI support.

### Detection areas

- GPU name and video memory, from Windows, and a tier by memory: No dedicated GPU, Entry, Mainstream, Performance, Enthusiast, or Professional.
- System RAM.
- CPU and operating environment.
- Recommended model size tier.
- Chat, code, and embedding model suggestions.

Tier names, advice, and model descriptions follow the app language.

### How to use recommendations

- Use smaller or quantized models when video memory is limited.
- Keep context windows smaller on low-memory machines.
- Prefer small embedding models such as `all-minilm` when indexing large vaults.
- Refresh after changing GPUs, drivers, or runtime configuration.

---

## 24. Backup and Restore

Backup and Restore protects local Agent-X data. A backup is a single `.agentxbak` file.

### What a backup contains

- The Agent-X database: extracted document text and chunks, embeddings, conversations, workflows, collections, annotations, and history.
- With **Include indexed documents** on: the document files Agent-X keeps in its own storage folder, which are the pages saved by Web Import (`WebImports`). Files you imported from other folders are indexed where they live and are not copied into the backup; back those folders up separately.
- Never included: settings and API keys, the database encryption marker (`encryption.info.json`), logs, downloaded models, plugins, and caches.

If database encryption is on, the database inside the backup stays encrypted with this installation's key, so only this installation, on the same Windows account, can restore it.

### Backup options

| Option | Purpose |
| --- | --- |
| Destination | Folder where the backup file is written |
| Include indexed documents | Adds the web-imported document files described above |
| Encryption | Encrypts the backup file with a password. The password is needed to restore and cannot be recovered |
| Notes | Adds human-readable context to the backup history |

### Scheduled backups

Scheduled backups are off by default. To turn them on, open **Backup & Restore**, turn on **Back up automatically** under **Scheduled Backups**, adjust the fields below, and select **Save Schedule**. The schedule applies at once: saving starts it, or stops it when the switch is off. There is no need to restart Agent-X or edit a file.

- **Interval (hours)** (1 to 720, default 168, which is weekly): a backup is due one interval after the last scheduled backup. One that is due when the schedule starts (at launch, or when you save it) runs about five minutes later. Scheduled backups run only while Agent-X is running.
- **Scheduled backups to keep** (default 5): after each scheduled backup, the oldest scheduled backups beyond this number are deleted (0 keeps all). Manual backups are never deleted this way.
- **Destination Folder**: leave it empty to use `%LocalAppData%\AgentX`; a folder on another drive is safer. Agent-X creates the folder when you save, and does not save a folder it cannot create.
- **Encrypt scheduled backups**: protects every scheduled backup with the password you enter. You need it to restore those backups. It is stored in `settings.json`, encrypted with Windows DPAPI.

The schedule is kept in the `backupSchedule` section of `%LocalAppData%\AgentX\settings.json`.

Backups raise no notifications; the `BAK` lamp on the instrument strip shows how old your latest backup is.

### Restore behavior

Choose a backup file and select **Restore**. If the backup is encrypted, Agent-X asks for its password.

Restore checks the backup before changing anything: the file is decrypted and validated, and its database is unpacked next to the current one and verified with this installation's database key. A backup made before you turned on database encryption is encrypted with the current key as it is restored. Only then is the current database replaced. If anything fails after that point, the previous database and document files are put back.

Restore replaces the database with the backup's database and puts the backup's web-imported document files back, overwriting files with the same name; other files in that folder are left in place. It does not change settings, API keys, or the encryption marker. While the database is replaced, other work in Agent-X (indexing, sync, status updates) waits, and semantic search closes its connection and then reloads its index from the restored database. Restart Agent-X when the restore completes: open pages and cached search results keep the previous data until then, and the restored database is upgraded to the current version at startup.

- If Agent-X reports that the database is in use, another program, such as a second Agent-X window, has the database file open. Close it, then restore again. Nothing is changed in that case.
- A backup whose database is encrypted with another key (another installation or Windows account) is refused, and nothing is changed.

### Backup before high-risk changes

Create a fresh backup before:

- Enabling at-rest database encryption.
- Importing a large new corpus.
- Changing sync scope.
- Moving to a new machine.
- Running major cleanup or duplicate removal.

---

## 25. Collaborative Sync

Collaborative Sync exchanges records between Agent-X installations through a folder they can all reach, such as a network share or a OneDrive or Dropbox folder, with no server and no account. Each installation writes its changes there as encrypted packages (`.axs` files, AES-256, with a key derived from the Encryption Key passphrase) and reads the others'.

### What is synced

- Documents (their details, not the original files, their text, or their index; a synced document whose file is not on the other computer is marked failed there), collections, tags, conversations (without their messages), annotations, and system prompts.
- Settings are not synced.
- With the scope set to selected collections, only those collections (and their parent collections), their documents, and those documents' annotations are exchanged; tags, conversations, and system prompts are left out.

### Configuration

| Field | Meaning |
| --- | --- |
| Sync Folder | Local, network, or cloud-synced folder used for exchange (**Browse** picks one) |
| Encryption Key | The passphrase for the packages; it must be the same on every installation |
| Auto-Sync | Runs a sync pass at a regular interval |
| Sync Interval | Time between auto-sync passes |
| Sync Scope | All data or selected collections |
| Collections to Sync | Included when the scope is set to selected collections |

**Save Configuration** stores the settings.

### Manual sync

**Sync Now** exports local changes, reads the other installations' packages, imports their changes, and updates the status (last synced, pending changes, duration, state) and the history.

### History and conflicts

When the same item was changed on two installations, the newer change wins (last writer wins); on an exact tie, the installation with the higher device ID wins. There is no merge. **Sync History** lists each pass with its number of changes and conflicts; **Reload** refreshes it and **Clear History** empties it. Operations can focus a specific history entry when an action is needed.

---

## 26. Calendar and Email Connectors

The Calendar and Email pages configure the productivity connectors. Two providers are supported, Microsoft (Outlook) and Google (Gmail and Google Calendar), both through OAuth; there is no CalDAV, IMAP, or Exchange Web Services connector. The connectors only read your account: they never send, reply, or change anything, and they do not trigger workflows.

### OAuth App Credentials

Agent-X ships no OAuth client, so connecting needs your own. Enter it once under **OAuth App Credentials** (below Connected Accounts; the Calendar and Email pages share it) and click **Save Credentials**. The credentials apply right away, with no restart.

- **Google** (Gmail and Google Calendar): in the Google Cloud Console (APIs & Services > Credentials), create an OAuth client ID of type Desktop app in a project with the Gmail API and Google Calendar API enabled and your account added as a test user. Enter the Client ID (it ends with `.apps.googleusercontent.com`) and the Client secret. The secret is masked until you click Show and is stored encrypted.
- **Microsoft** (Outlook mail and calendar): in the Microsoft Entra admin center (App registrations), register an app for accounts in any organizational directory and personal Microsoft accounts, and under Authentication add the Mobile and desktop applications platform with the Redirect URI shown on the page (`http://localhost:8401/oauth/callback` by default). Enter the Application (client) ID; no secret is needed.
- The form refuses spaces or line breaks, a Google client ID that does not end with `.apps.googleusercontent.com`, a Google client ID without its secret, and a Microsoft ID that is not in the form `00000000-0000-0000-0000-000000000000`, with a message under the field.
- To remove a provider, clear its client ID and save; the Google secret is removed with it.
- To change a client ID while an account is connected, first click **Disconnect** under Connected Accounts, then save the new ID and connect again. Changing only the Google client secret keeps the account connected.

### Calendar

Calendar sync supports:

- The **Calendar sync** switch.
- OAuth connection and disconnection for Google Calendar and Outlook Calendar.
- **Sync Now**.
- **Sync interval (minutes)** (15 by default).
- **Sync range: past days** and **future days** (90 and 30 by default).
- **Include attendee details** and **Include event descriptions**.
- Last synced and next sync indicators.

An event that is deleted at the source (or, for Google, cancelled), or that no longer appears in the synced date range, is retired on the next sync. If it never reached the vault, it leaves the Smart Inbox. If it did, its vault document is kept, never deleted, and is marked instead: its name ends in "removed" and its text says why. An event that only aged out past the start of the range is left as it is.

### Email

Email sync supports:

- Provider enable and disable.
- OAuth connection and disconnection.
- Manual sync.
- Sync interval selection.
- Maximum messages per sync (50 by default).
- Days-back sync window (30 by default).
- Folder selection: the folders of every connected account are listed once an account is connected, and mail is read only from the checked folders (only the inbox by default). The Gmail and Outlook inboxes share one entry.
- Last sync and next sync indicators.

Each message is filed under a category such as action required, meeting, financial, newsletter, or notification by fixed keyword and sender rules; no AI model sorts your mail. A message without a plain-text part is stored and indexed as readable text converted from its HTML.

Each synced event or message is added to the Smart Inbox as an already-accepted item and imported into the vault as a searchable document, and Operations shows connector health.

---

## 27. Annotations

Annotations are highlights and notes on the text of vault documents.

### Creating annotations

Open a document's preview in the Knowledge Vault with **Detail**. Under **DOCUMENT TEXT**, the indexed text is shown one passage at a time (Passage N / M, with Previous passage and Next passage). Select text in the passage to open **NEW ANNOTATION** with the selection, add a note if you like ("Add a note (optional)"), and click **Save Annotation**; Cancel discards it. Annotations are saved in yellow, and moving to another passage or document drops an unsaved selection. A document that is not indexed yet has no text to annotate. **ANNOTATIONS** in the preview lists the document's annotations, newest first, each with a delete button that acts without asking.

### Annotation fields

- Source document.
- Highlighted text.
- Note text.
- Color.
- Created and updated timestamps.

### Tools on the Annotations page

- Search annotations by their highlighted text or note.
- Filter by color; DISTRIBUTION shows how many annotations each color has.
- Edit note text and color.
- Delete annotations (after a confirmation that names the document; the document itself is not changed).
- **Export as Markdown** saves every annotation to a Markdown file you name.

The passage text is not highlighted where annotations are. Annotations do not affect Ask Your Files or search ranking, and they are not part of the Weekly Digest or the Knowledge Graph. Each new annotation also becomes an insight (see [Past Self and Draft as Me](#20-past-self-and-draft-as-me)). Deleting a document deletes its annotations.

---

## 28. Settings

Settings (`Ctrl+,`) is one scrolling page of sections. It has no search box and no collapsible groups. Theme and Language are saved as soon as you pick them, and Database Encryption has its own switch; the rest of the page is stored by **Save Settings** (`Ctrl+S`) in `settings.json` in the data folder, with API keys encrypted by Windows, and applies at once. **Reset to Defaults** asks first, then restores and saves the defaults. Backups do not include settings.

### Sections

| Section | Controls |
| --- | --- |
| AI Providers | **Active Provider**: Built-in LLM (Local), Ollama (Local), OpenAI, or Anthropic Claude. The built-in provider has **Automatic GPU layers** (on by default: an NVIDIA GPU gets 16 layers with 2 to 4 GB, 28 with 4 to 6 GB, and 33 with 6 GB or more of video memory, none below 2 GB); turning it off shows **GPU Layers** (0 to 999; 0 keeps the model on the CPU). Layers run on the GPU only when the NVIDIA CUDA 12 Toolkit is installed, and saving reloads the built-in model. Ollama takes its Endpoint, Default Chat Model, and Embedding Model; OpenAI and Anthropic Claude take an API Key, Endpoint, and Default Model; each has **Test Connection** |
| Appearance | Theme (Dark, Light, or System Default) and Language: "Windows default" (follows the Windows display language) or English, Deutsch, Español, Français, 日本語, or 简体中文. Restart Agent-X to see every page in a new language. Relative times such as "5m ago" and "just now", and older dates, follow the app language |
| Multi-Model Routing | **Enable Auto-Routing** (off by default) routes prompts to models by task type, with a **Routing Profile**: Cost Optimized, Quality Optimized, or Balanced (the default). With cloud keys set, routing can send prompts to a cloud provider |
| Cost Tracking | **TOTAL SPEND** and **TODAY** (from local midnight) estimate what OpenAI and Anthropic calls cost, from the app's built-in price table, and **TOTAL TOKENS** counts the tokens of the OpenAI, Anthropic, and Ollama calls it tracks; local models cost nothing. The totals survive restarts, and the details of each call are kept for 90 days in `usage-history.json` in the data folder. There are no breakdowns by model, conversation, or feature, and no budget caps |
| Inference | Temperature (0 to 2, 0.7 by default), Max Tokens (256 to 32,768, 4,096 by default), and Context Window (2,048 to 131,072, 8,192 by default). They apply to every conversation |
| Knowledge Vault | Chunk Size (tokens) (128 to 2,048, 512 by default), Chunk Overlap (0 to 256, 50 by default; smaller than the chunk size, or Save Settings says why nothing was saved), Top-K Results (1 to 20, 5 by default), Watch Folders, and the **Auto-index watch folders** switch |
| Research Mode | **Enable Research Mode** (the switch reads "Chat can add web results" when on and "No web search" when off), the Search Provider (Brave Search, Serper, or SearXNG), its credential (the API key for Brave or Serper, or the instance URL for SearXNG), Max Search Results (1 to 20, 10 by default), and the cache duration. Research Mode adds web search results to chat answers; chat never searches the Knowledge Vault |
| Storage | Where the data folder is |
| Database Encryption | SQLCipher encryption of the database (see below) |
| Connections | **Enable Local API** for the browser extension and the Android app, and its API token. The local API listens only on `localhost:9846`, and turning it on or off takes effect when you save settings |

### Database encryption

Agent-X can encrypt the local SQLite database with SQLCipher (AES-256). The encryption key is managed automatically and tied to your Windows user account, and the feature is available to every user free of charge.

Before enabling encryption:

1. Create a fresh backup.
2. Confirm the backup restores in a safe environment if the data is critical.
3. Keep `encryption.info.json` with the encrypted database when backing up manually.

Turning encryption off is not supported in this release. Restoring a backup keeps encryption on: a backup made before encryption was enabled is encrypted with the current key as it is restored. Because the key is tied to this installation and Windows account, a backup of an encrypted database cannot be restored on another machine.

---

## 29. Command Palette, Jump To, and Shortcuts

### Command Palette

Open the command palette with `Ctrl+K` or `Ctrl+Shift+P`. Type to filter the pages and a few actions (New Conversation, Import Files, Toggle Theme) plus the shortcuts of the page you are on, press `Enter` to run the selected command, and press `Esc` to dismiss it.

### Jump To

Open Jump To with `Ctrl+P` to open a page, a document, or a conversation by name. It lists every page, including **Onboarding**, the 50 most recently imported documents, and 50 conversations (pinned first, then the most recently updated); open older items from the Knowledge Vault or AI Chat.

### Keyboard Shortcuts list

Open the **Keyboard Shortcuts** list with `F1` or `Ctrl+Shift+/`. It lists the global shortcuts plus the ones the current page registers; it has no search box. Shortcuts are fixed in this release; they cannot be remapped. The names of page-specific shortcuts appear in English.

### Global shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+K` | Command Palette |
| `Ctrl+Shift+P` | Command Palette |
| `Ctrl+N` | New conversation in AI Chat |
| `Ctrl+I` | Knowledge Vault |
| `Ctrl+F` | Semantic Search |
| `Ctrl+Shift+F` | Semantic Search |
| `Ctrl+,` | Settings |
| `Ctrl+Shift+A` | Analytics |
| `Ctrl+Shift+O` | Operations |
| `Ctrl+D` | Dashboard |
| `Ctrl+Shift+W` | Workflows |
| `Ctrl+Shift+E` | Web Import |
| `Ctrl+G` | Knowledge Graph |
| `Ctrl+P` | Jump To |
| `F1` | Keyboard Shortcuts list |
| `Ctrl+Shift+/` | Keyboard Shortcuts list |
| `Ctrl+1` to `Ctrl+9` | Dashboard, AI Chat, Ask Your Files, Semantic Search, Knowledge Vault, Collections, Workflows, Model Manager, Settings |
| `Win+Shift+A` | Quick Chat, from anywhere in Windows |

### Page shortcuts

| Page | Shortcut | Action |
| --- | --- | --- |
| AI Chat | `Ctrl+Shift+N` | New conversation |
| AI Chat | `Ctrl+B` | Show or hide the conversation list |
| Knowledge Vault | `F5` | Refresh the document list |
| Settings | `Ctrl+S` | Save settings |

---

## 30. Instrument Strip, Notifications, and Tray

### Instrument strip (status bar)

The bottom of the main window is an instrument strip: a row of readouts and stencil word-lamps that stays dark in both the dark and light themes. Every value is real and updates on a background poll.

- `MDL` lamp and readout: the loaded model name when the AI provider is reachable (green), or an amber caution naming the active provider, such as "Ollama not available", when it is not.
- `Ctrl+K` key hint for the command palette.
- `IDX` readout: the embedding queue depth. It burns amber while indexing and rests dim at zero.
- `VAULT` readout: total document count.
- Annunciator lamps: `INBOX` (amber when triage items are waiting), `SYNC` (green when configured and idle, teal while syncing, red on a sync error, unlit when not configured), `JOBS` (teal while a workflow runs, red if the latest run failed), `BAK` (green when the last backup is under a week old, amber when older, unlit if no backup exists).
- `LOCAL` / `NET` privacy lamp: green `LOCAL` when nothing is configured to send data off the machine, amber `NET` otherwise.
- Version label.

Clicking any lit lamp jumps to its source page (for example, `INBOX` opens the Smart Inbox and `BAK` opens Backup & Restore). A blinking lamp indicates an unacknowledged warning; clicking it once acknowledges it and stops the blink.

### Notifications

Agent-X shows in-app toast notifications from AI Chat: errors, and confirmations such as a deleted message or a new branch. Imports, indexing, sync, backups, and workflow runs do not raise notifications; follow them on the instrument strip (`IDX`, `SYNC`, `JOBS`, `BAK`), in Operations, and on each feature's own page.

### System tray

The tray icon provides:

- Open Agent-X.
- Quick Chat.
- Settings.
- Exit.

Its tooltip shows whether the AI provider is connected and the document count.

---

## 31. Privacy and Security

### Local-first security model

- Agent-X sends no telemetry: no usage analytics, crash reports, or logs of AI calls.
- The built-in local model is the default AI provider.
- The local API used by the browser extension and the Android app listens on `localhost:9846` only and requires its token.
- The vault, settings, logs, embeddings, and conversations stay in the user's profile directory.
- Optional SQLCipher encryption protects the database at rest.
- DPAPI protects secrets tied to your Windows account: API keys, the Google client secret, the scheduled-backup password, and the database key.
- Network use is limited to what you set up or start (see [Optional external connections](#optional-external-connections)). Quick Chat's screen reading is off unless you turn it on in `settings.json`.

### Provider keys

API keys are entered by you and stored locally, encrypted. They are only sent to their respective providers when those providers are used.

### Open-source license

Agent-X is free and open-source software released under the MIT License. Every capability is available to every user: there are no tiers, no activation, no document limits, and no subscription checks of any kind.

---

## 32. Troubleshooting

### Ollama not detected

1. Run `ollama list`.
2. Confirm Ollama is listening on `http://localhost:11434`.
3. Verify the Ollama endpoint in Settings > AI Providers and use **Test Connection**.
4. Restart Ollama.

### Models do not appear

1. Pull at least one model with `ollama pull <model-name>`.
2. Confirm `ollama list` shows it.
3. Test the Agent-X Ollama connection in Settings.
4. Refresh Model Manager.

### Documents remain pending or fail

1. Read the reason on the document's row in the Knowledge Vault, under **WHY INDEXING FAILED** in its Document Preview, or in the Recent imported documents list on the Operations page.
2. Check that an embedding model is available: with the default Embedding Model setting, the built-in model must be installed or Ollama must be running with `all-minilm` pulled.
3. For audio files, install the speech-to-text model on the Model Manager page; waiting files are queued again.
4. Confirm the original file is still where it was imported from; indexing reads it again.
5. Use **Re-index** in the Knowledge Vault or **Retry Index** in Operations.

### Search returns no results

1. Confirm documents are indexed.
2. Try Hybrid mode.
3. Broaden the query.
4. Remove overly narrow filters.
5. Re-index after changing embedding models.

### Ask Your Files gives weak citations

1. Narrow the collection scope (single documents cannot be selected).
2. Use a more specific question.
3. Re-index source documents.
4. Confirm the chunk size and Top-K Results settings are reasonable.
5. Check whether the document text extraction is complete.

### Workflow run fails

1. Open the run from Workflows or Operations.
2. Review the failed step and error text.
3. Confirm the model or provider is reachable.
4. For a ConditionalBranch or other step with settings, read the message under its settings box.
5. Reduce Max Tokens or the Context Window if the model runs out of memory.

### Sync is stuck or reports conflicts

1. Verify the sync folder is reachable.
2. Confirm the encryption key matches across machines.
3. Review Sync History.
4. Run a manual sync once.
5. Resolve focused Operations sync items before enabling auto-sync again.

### Responses are slow

1. Use a smaller or quantized model; the Hardware Advisor suggests sizes for your GPU memory or RAM.
2. Lower Context Window or Max Tokens under Settings > Inference.
3. The built-in model uses an NVIDIA GPU only when the NVIDIA CUDA 12 Toolkit is installed; **Automatic GPU layers** under Settings > AI Providers then picks the layer count, or turn it off and set **GPU Layers** yourself. Ollama uses a supported GPU on its own.

### High memory usage

1. Use a smaller model.
2. Reduce the context window size.
3. Use quantized models.
4. Close other memory-heavy apps.
5. Follow Hardware Advisor recommendations.

### App starts into onboarding unexpectedly

Check `%LocalAppData%\AgentX\settings.json`. If `"onboardingCompleted"` is missing or false, onboarding runs. Finish the wizard or set the value to `true` while the app is closed.

---

## 33. FAQ

**Does Agent-X send my data to the cloud?**

Not by default. The built-in model (the default provider) and an Ollama server on the same machine keep documents and prompts on your machine. Data is sent externally only when you use a configured cloud provider, Multi-Model Routing with a cloud key, a connector, or a web feature.

**Can I use Agent-X without a GPU?**

Yes. CPU inference works but is slower. Use smaller models and the Hardware Advisor.

**What is the difference between chat and embedding models?**

Chat models generate text. Embedding models convert text into vectors used by Semantic Search, Ask Your Files, and semantic duplicate scans.

**What happens when I delete a document?**

Agent-X asks first. Deleting removes the vault record, chunks, embeddings, keyword entries, tags, collection memberships, and annotations; it cannot be undone. The original source file on disk is never deleted.

**Where is my data?**

By default: `%LocalAppData%\AgentX\`.

**Can I move Agent-X to another machine?**

Backup and Restore moves the database and the web-imported pages; files you imported from other folders are not in the backup, so copy them to the same paths or re-import them. A backup of an encrypted database can only be restored by the installation and Windows account that created it. Collaborative Sync exchanges records only: no document files and no conversation messages.

**Why should I use Collections if Search already works?**

Collections create scopes for Ask Your Files, search filters, workflow document lookups, sync, and project separation.

**When should I use Workflows instead of Quick Actions?**

Use Quick Actions for one-off document tasks. Use Workflows when the same multi-step prompt process should be repeated, inspected, saved, or exported.

---

## 34. Supported File Types

### Documents

| Extension | Type | Processing |
| --- | --- | --- |
| `.pdf` | PDF | Text layer, page by page, with document metadata; scanned PDFs without a text layer are rejected (no OCR) |
| `.docx` | Word document | OpenXML paragraph text, including tables |
| `.doc` | Legacy Word | Not supported; save the file as `.docx` first |

Excel, PowerPoint, EPUB, and RTF files are not supported.

### Text and data

| Extension | Type | Processing |
| --- | --- | --- |
| `.txt` | Plain text | Direct text extraction |
| `.csv` | CSV | Read as plain text |
| `.log` | Log | Plain text extraction |
| `.xml` | XML | Plain text extraction |
| `.json` | JSON | Plain text extraction |
| `.yaml`, `.yml` | YAML | Plain text extraction |
| `.toml` | TOML | Plain text extraction |
| `.ini`, `.cfg` | Config | Plain text extraction |

### Markdown

| Extension | Type | Processing |
| --- | --- | --- |
| `.md` | Markdown | Markdown-aware text extraction |
| `.markdown`, `.mdx` | Markdown | Markdown-aware text extraction |

### Images

| Extension | Type | Processing |
| --- | --- | --- |
| `.png` | Image | Windows OCR in the languages of your Windows profile |
| `.jpg`, `.jpeg` | Image | Windows OCR in the languages of your Windows profile |
| `.bmp` | Image | Windows OCR in the languages of your Windows profile |
| `.tiff` | Image | Windows OCR in the languages of your Windows profile |

An image without text is kept with nothing to search; with no OCR language installed, the import fails with a message. Images are not sent to a vision model.

### Web shortcuts

| Extension | Type | Processing |
| --- | --- | --- |
| `.url`, `.webloc` | Web shortcut | Imports the page the shortcut points to (public internet addresses only) |

### Code

| Extension | Type |
| --- | --- |
| `.cs` | C# |
| `.js`, `.ts` | JavaScript / TypeScript |
| `.py` | Python |
| `.java` | Java |
| `.cpp`, `.c`, `.h` | C / C++ |
| `.go` | Go |
| `.rs` | Rust |
| `.swift` | Swift |
| `.kt` | Kotlin |
| `.rb` | Ruby |
| `.php` | PHP |
| `.html`, `.htm`, `.css`, `.scss` | Web source |
| `.sql` | SQL |
| `.sh` | Shell |
| `.xaml` | XAML |

Code files are read as text; chunks follow blank lines and sentences, and there is no syntax-aware splitting.

### Audio

| Extension | Type | Processing |
| --- | --- | --- |
| `.wav` | WAV audio | Decoded by the app, converted to 16 kHz mono PCM, transcribed with Whisper |
| `.mp3`, `.m4a`, `.flac` | Compressed audio | Decoded with Windows Media Foundation, then converted and transcribed |
| `.ogg`, `.webm` | Compressed audio | Transcribed only when Windows has a codec installed for the format |

Audio is transcribed on your machine with the Whisper base model, on the CPU. The transcript starts with a short header (file name, detected language, duration, model) and has one line per segment, prefixed with its time range, such as `[00:01:05 --> 00:01:12]`. The language is detected automatically and cannot be forced, and there are no speaker labels (diarization is not implemented). Agent-X has no audio playback.

- Transcription needs the speech-to-text model. Install it with **Download** under **Speech-to-Text Model** on the Model Manager page; nothing downloads it on its own. The model file is `ggml-base.bin` in `%LOCALAPPDATA%\AgentX\Models\Whisper`.
- Without the model, voice input in AI Chat says to install it there, and an imported audio file is marked Failed with the reason "The speech-to-text model is not installed. Install it on the Model Manager page to transcribe this audio file." After the download, those files, and audio that earlier versions imported with a placeholder transcript, are queued for transcription again.
- Audio that cannot be decoded, or a Whisper runtime that cannot load on this computer, also marks the file Failed with its reason.
- Pick audio files with Import Files, or use Import Folder, a watch folder, or the drop zone of an empty vault.
- In AI Chat, click the microphone to dictate, or right-click it to pick an audio file; the text lands in the message box.

---

*Agent-X is developed by Rocky Elsalaymeh / Strategia. For support, feature requests, or bug reports, use the official support channel included with the product.*
