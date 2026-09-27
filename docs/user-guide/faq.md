# Agent-X FAQ

**Frequently Asked Questions**

---

## Table of Contents

- [General](#general)
- [Installation & Setup](#installation--setup)
- [Features & Usage](#features--usage)
- [AI & Models](#ai--models)
- [Search & RAG](#search--rag)
- [Performance & Hardware](#performance--hardware)
- [Privacy & Security](#privacy--security)
- [Licensing](#licensing)
- [Troubleshooting](#troubleshooting)

---

## General

### What is Agent-X?

Agent-X is a local-first AI document intelligence application for Windows. Import your documents into the Knowledge Vault, ask questions about them in Ask Your Files, search them by meaning or by keyword, and chat with AI models. By default everything runs on your computer with a built-in model; your data leaves the computer only for services you set up yourself, such as a cloud AI provider or web search (see [Is my data sent to the cloud?](#is-my-data-sent-to-the-cloud)).

### What sets Agent-X apart?

| Feature | Agent-X |
|---------|---------|
| **Data location** | Documents, embeddings, conversations and settings stay in `%LocalAppData%\AgentX` on your computer |
| **Offline use** | Works offline once the built-in model is on the computer |
| **Cost** | Free and MIT-licensed; nothing to buy or activate |
| **Models** | Built-in Llama 3.2 3B, any model you run in Ollama, or OpenAI and Anthropic with your own API key |
| **Retrieval** | Hybrid semantic and keyword search, query rephrasing, HyDE, reranking and numbered citations |
| **Database encryption** | Optional SQLCipher (AES-256), with the key tied to your Windows account |

### Is Agent-X free?

Yes. Agent-X is free and open-source software under the MIT License (`LICENSE`). Every capability is available to every user, with no paid tiers, subscriptions, activation, quotas or feature gates. Cloud AI providers and web search providers you choose to use bill you directly under their own terms.

### What file formats does Agent-X support?

Each format is claimed by a processor in `src/AgentX.Core/Documents/Processors/`:

| Category | Formats | Processor |
|----------|---------|-----------|
| **PDF** | `.pdf` | `PdfProcessor.cs` |
| **Word** | `.docx` | `DocxProcessor.cs` |
| **Text and data** | `.txt`, `.csv`, `.log`, `.json`, `.xml`, `.yaml`, `.yml`, `.toml`, `.ini`, `.cfg` | `TextProcessor.cs` |
| **Markdown** | `.md`, `.markdown`, `.mdx` | `MarkdownProcessor.cs` |
| **Code** | `.cs`, `.js`, `.ts`, `.py`, `.java`, `.cpp`, `.c`, `.h`, `.go`, `.rs`, `.swift`, `.kt`, `.rb`, `.php`, `.html`, `.htm`, `.css`, `.scss`, `.sql`, `.sh`, `.xaml` | `CodeFileProcessor.cs` |
| **Images** | `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tiff` (text read with Windows OCR) | `ImageProcessor.cs` |
| **Audio** | `.mp3`, `.wav`, `.flac`, `.ogg`, `.m4a`, `.webm` (transcribed) | `AudioProcessor.cs` |
| **Web shortcuts** | `.url`, `.webloc` (the linked page is fetched) | `WebProcessor.cs` |

Plugins can add formats that no built-in processor handles.

A few things to know:

- **Not supported:** legacy `.doc` (save it as `.docx`), `.rtf`, Excel, PowerPoint and EPUB files.
- **PDFs** are read from their text layer. A scanned PDF without one is recorded as Failed with the reason; there is no OCR for PDFs.
- **The Import Files picker** lists document, data and code formats only. Bring in images, audio, web shortcuts and plugin formats by dragging them onto the Knowledge Vault page or with Import Folder.
- **Audio** is transcribed on your computer by the speech-to-text model (Whisper base, about 142 MB). Install it with **Download** under Speech-to-Text Model on the Model Manager page; nothing downloads it on its own. Without it an audio file is imported as Failed with a message saying so, and it is transcribed after you install the model. OGG and WebM need a Windows codec.
- **A file that cannot be read** (encrypted, damaged, no text) is kept in the vault as Failed, and its row and the Document Preview show why.

### Does Agent-X work offline?

Yes, once the built-in model is on the computer. How it gets there depends on the installer:

- **SLIM** (the default, attached to the GitHub release): the installer does not contain the model. On first run the onboarding wizard's "Your AI is Ready" step offers **Download built-in model**, a download of about 2 GB from Hugging Face. That download needs an internet connection; nothing after it does.
- **OFFLINE** (hosted at `downloads.strategia-x.com` and linked from the release notes): the model is inside the installer, so the machine never needs a connection.

Features that use the network by nature (cloud AI providers, Research Mode, Web Import, the calendar and email connectors) need a connection when you use them.

---

## Installation & Setup

### What are the system requirements?

| Requirement | Value |
|-------------|-------|
| **Windows** | Windows 10 build 19041 (version 2004) or later, or Windows 11 |
| **Architecture** | x64 |
| **Disk space** | The app, about 2 GB for the built-in model, about 142 MB for the optional speech-to-text model, and your data |
| **GPU** | Not required. The built-in model can offload layers to an NVIDIA GPU when the NVIDIA CUDA 12 Toolkit is installed |

The installer bundles the .NET runtime and the Windows App SDK, so nothing else has to be installed. No minimum amount of RAM is enforced; the Hardware Advisor page suggests model sizes for the memory your computer has.

### How do I install Agent-X?

1. Download an installer. The installers published with the v2.1.1 release are `AgentX-Setup-2.1.1-x64.exe` (SLIM, 228 MiB, attached to the release) and `AgentX-Setup-2.1.1-x64-offline.exe` (OFFLINE, 2.07 GiB, linked from the release notes). They are not code-signed, so Windows SmartScreen asks you to confirm. The v2.1.2 and v2.2.0 releases have no installers yet (they wait for a code-signing certificate), so the v2.1.1 files predate later fixes, among them the token that protects the local API; to run the current version, build it from source (see [the product README](../README.md#build-from-source)).
2. Run the installer. It needs no administrator rights and offers an optional desktop shortcut.
3. Launch Agent-X from the Start Menu, the desktop shortcut, or the installer's last page.
4. Follow the onboarding wizard: it can test an Ollama connection, pick Ollama models, download the built-in model (SLIM) and take optional OpenAI and Anthropic API keys. No passphrase is needed.

### Do I need administrator rights?

No. For your own account Agent-X installs to `%LocalAppData%\Programs\Agent-X`, which does not need elevation. The installer can also install for all users into Program Files, which asks for administrator rights.

### Can I install Agent-X on a USB drive?

You can choose another folder, including a USB drive, for the program files. Your data, the downloaded models and the logs are always kept in `%LocalAppData%\AgentX` on the computer's own drive, so the installation is not portable.

### How do I uninstall Agent-X?

1. Go to **Settings > Apps > Installed apps** in Windows
2. Find "Agent-X" and click **Uninstall**
3. The uninstaller removes the program and the log files. Your data in `%LocalAppData%\AgentX\` (database, settings, models, backups made there, plugins) is kept

To remove everything, uninstall and then delete `%LocalAppData%\AgentX\`. Agent-X stores nothing in Windows Credential Manager.

### Can I move my data to another computer?

Use the Backup & Restore page:

1. On the old computer, create a backup with **Create Backup** (with **Include indexed documents** on, it also carries the pages saved by Web Import). A password is optional.
2. Install Agent-X on the new computer, open Backup & Restore, choose the `.agentxbak` file under **Restore from Backup**, and click **Restore**. Restart Agent-X when it says so.
3. Enter your API keys and other settings again: settings are not part of a backup, and the secrets in `settings.json` are encrypted for your Windows account on the old computer.

Two limits apply. If database encryption is on, the database inside the backup is encrypted with that installation's key, so it can only be restored there. And documents are kept as extracted text plus the path to the original file: search and Ask Your Files work after the move, but re-indexing or opening a document needs the file at the same path on the new computer.

---

## Features & Usage

### How do I import documents?

1. Open the **Knowledge Vault** (`Ctrl+I`)
2. Click **Import Files** and pick one or more files, click **Import Folder**, or drag files and folders onto the page
3. With Import Files or drag and drop, if some files are exact copies of documents already in the vault, choose **Skip Duplicates** or **Import All** (Import Folder skips them)
4. Indexing (chunking, embedding and keyword indexing, then auto-tagging) runs in the background; each row shows its status

The Command Palette's Import Files action (`Ctrl+K`) opens the vault with the file picker. Titles come from the document itself (for example its first heading); **AI Title** asks the model for one on demand.

### Can I import entire folders?

Yes. **Import Folder** imports every supported file in a folder and its subfolders. To keep importing from a folder, add it under **Watch Folders** in Settings > Knowledge Vault: while **Auto-index watch folders** is on, Agent-X imports the supported files already there, then new and changed ones while it runs, straight into the vault.

### How do I organize my documents?

| Method | Description |
|--------|-------------|
| **Collections** | Groups you create on the Collections page and fill with **Add Documents**. A document can be in several; a Collection can be moved into another one level deep with **Move into...** |
| **Tags** | Assigned by auto-tagging after indexing (up to five per document) |
| **Saved Filters** | Searches saved on the Search page with their mode and settings |
| **Conversation Folders** | Work, Research, Personal, Archive or a name you type, set with the folder button in AI Chat |

Quick Actions > Organize suggests Collections and tags for documents that are in none, but applies nothing.

### What is the Knowledge Graph?

The Knowledge Graph (`Ctrl+G`) draws your vault as a network:
- Documents, Collections and tags are nodes
- A line joins each document to its Collections and tags
- A line joins two documents that share at least one Collection or tag, thicker the more they share

Click a node for its details; clicking a Collection or tag highlights its cluster. You can zoom (not pan) and search node names. The layout is a force-directed simulation run once per build; Refresh rebuilds it.

### How do I use the command palette?

Press `Ctrl+K` (or `Ctrl+Shift+P`) and type part of a name. The palette lists every page on the navigation rail, three actions (New Conversation, Import Files, Toggle Theme) and the shortcuts of the page you are on. The letters you type must appear in order but need not be adjacent. To open a specific document or conversation, use Jump To (`Ctrl+P`) instead.

### What are Workflows?

Workflows are multi-step AI text pipelines you run on demand: each step's output feeds the next. A step is one of five types: AI Prompt, Document Lookup, Text Transform, Conditional Branch or Output Format. Agent-X ships four built-in templates you can run as they are or copy with **Use Template**:

| Workflow | Description |
|----------|-------------|
| **Summarize & Act** | Summarize the input, extract key points, and generate action items |
| **Research Brief** | Analyze a topic, identify key arguments, and write a structured brief |
| **Document Review** | Summarize a document, list strengths and weaknesses, and suggest improvements |
| **Content Repurpose** | Rewrite content as a tweet thread, an email, and a blog post |

Workflows run only when you start them: there are no schedules, event triggers, or notifications.

### Can I annotate documents?

Yes. In the Knowledge Vault, open a document's preview with **Detail**. Under DOCUMENT TEXT, move through the indexed text with Previous passage and Next passage, select some text, add an optional note under NEW ANNOTATION and click **Save Annotation**. The preview lists the document's annotations under ANNOTATIONS, and the Annotations page lists all of them, where you can search, filter by color, edit the note and color, delete, and **Export as Markdown**. Annotations are not highlighted inside the passage text.

---

## AI & Models

### What AI models are supported?

| Provider (as named in Settings) | Models | Type |
|----------|--------|------|
| **Built-in LLM (Local)** | Llama 3.2 3B Instruct (the default); Llama 3.2 1B Instruct can also be downloaded | Local, on your computer |
| **Ollama (Local)** | Any model you pull into Ollama | Local, or on another computer if you point the endpoint there |
| **OpenAI** | The chat models your API key lists; `gpt-4o-mini` by default | Cloud (API key) |
| **Anthropic Claude** | The models your API key lists; Claude Opus 5.5, Claude Sonnet 5 and Claude Haiku 4.5 are offered when the list cannot be fetched; `claude-sonnet-5` by default | Cloud (API key) |

### What is the built-in model?

Agent-X's default provider is **Llama 3.2 3B Instruct**, a compact language model:

- **File**: `llama-3.2-3b-instruct-q4_k_m.gguf` (Q4_K_M quantization, about 2 GB) in `%LocalAppData%\AgentX\Models`
- **How you get it**: bundled in the OFFLINE installer; downloaded from the onboarding wizard with the default SLIM installer, or with Pull Model on the Model Manager page while the built-in provider is active
- **Runs with**: LLamaSharp (llama.cpp), with an 8,192-token context
- **Also used for**: embeddings, when it is installed and the Embedding Model setting is left at its default
- **License**: Llama 3.2 Community License

Throughput depends on your CPU, GPU and memory and is not benchmarked here. Each reply in AI Chat shows its token count and speed.

### How do I add more models?

**For Ollama (local):**
1. Install Ollama from [ollama.com](https://ollama.com)
2. Pull a model: `ollama pull llama3.2`, or type its name under Pull New Model on the Model Manager page while Ollama is the active provider
3. In Settings > AI Providers, set **Active Provider** to **Ollama (Local)**, check the **Endpoint** (`http://localhost:11434` by default) with **Test Connection**, and click **Save Settings**

**For cloud providers:**
1. Go to **Settings > AI Providers**
2. Paste the **API Key** under **OpenAI** or **Anthropic Claude**, and optionally change **Endpoint** and **Default Model**
3. Click **Test Connection**, choose the provider under **Active Provider**, and click **Save Settings**
4. The key is stored in `settings.json`, encrypted with Windows DPAPI for your account

### Can I use my own models?

Yes:
- Any model you run in Ollama
- An OpenAI-compatible server: enter its address as the OpenAI **Endpoint**
- Another GGUF file for the built-in provider: copy it into `%LocalAppData%\AgentX\Models`. Set Active in the Model Manager uses it until Agent-X closes; to keep it, set `localModelFileName` in `settings.json` while Agent-X is closed (that file then also produces the embeddings)

Plugins cannot add AI providers.

### How do I switch between models?

1. Choose the provider under **Active Provider** in Settings > AI Providers and click **Save Settings**
2. Pick a model of that provider in the model box at the top of AI Chat, or with **Set Active** on the Model Manager page
3. For Ollama, OpenAI and Anthropic the choice is remembered after a restart; for the built-in provider see the previous answer

With **Enable Auto-Routing** (Settings > Multi-Model Routing), each chat reply can go to another provider according to the Routing Profile, without changing the active provider.

### What is GPU acceleration?

GPU acceleration moves part of the built-in model onto an NVIDIA GPU, so those layers run in video memory instead of on the CPU. The setting is **Automatic GPU layers** under Settings > AI Providers > Built-in LLM (Local), on by default. Automatic gives an NVIDIA GPU this many of the model's layers:

| Detected VRAM | Layers offloaded |
|---|---|
| Under 2 GB, or no NVIDIA GPU | 0 (CPU only) |
| 2-4 GB | 16 |
| 4-6 GB | 28 |
| 6 GB and above | 33 (all of the 3B model) |

Turn Automatic off to enter **GPU Layers** yourself (0 to 999; 0 keeps the model on the CPU). **Save Settings** reloads the built-in model with the new value; no restart is needed.

The layers run on the GPU only when the **NVIDIA CUDA 12 Toolkit** is installed: Agent-X ships the CUDA 12 build of llama.cpp but not the CUDA runtime libraries it needs. Without the toolkit the model runs on the CPU whatever the setting says, and the onboarding wizard says what is missing. AMD and Intel GPUs are not used by the built-in model. Ollama decides its own GPU use.

---

## Search & RAG

### What is RAG?

**RAG** stands for **Retrieval-Augmented Generation**. In Ask Your Files, Agent-X:

1. Retrieves the passages of your indexed documents that best match your question
2. Gives them to the active AI model as numbered context
3. Streams an answer that cites them as [1], [2] and so on

The model is told to answer only from those passages and to say when they are not enough, but it can still make mistakes, so check the cited sources for anything that matters. AI Chat does not search your documents; use Ask Your Files for that.

### How does semantic search work?

Semantic search uses **vector embeddings**:

1. When a document is indexed, each chunk becomes a vector (a list of numbers) from the embedding model
2. Your query is turned into a vector with the same model
3. Chunks are compared by cosine similarity, and the closest come back with a relevance score

This finds related content even without exact keyword matches. Vaults with more than about 10,000 chunks are searched through an HNSW index, smaller ones exactly.

### What is hybrid search?

Hybrid search runs **semantic and keyword searches** together:

| Search Type | Best For |
|-------------|----------|
| **Semantic** | Concepts, meaning, related topics |
| **Keyword** | Exact phrases, names, technical terms (SQLite FTS5 with BM25 ranking) |
| **Hybrid** | Both, merged into one ranking |

Results are combined using **Reciprocal Rank Fusion (RRF, k=60)**. The Search page opens in Semantic mode; Ask Your Files always uses hybrid retrieval.

### What is HyDE?

**HyDE** (Hypothetical Document Embeddings) helps when your question is worded differently from your documents. For questions of 80 characters or more, Ask Your Files:

1. Has the model draft a hypothetical answer
2. Searches with that text as well as with your question and a few rephrasings of it

Each of these is an extra model call.

### What are citations?

Citations link an answer to its sources:

- **Ask Your Files**: each [n] in the answer that matches a retrieved passage becomes a card in the Sources panel, with the document name, the page number when the format has pages, the match percentage and an excerpt. Open source document shows the file in File Explorer; it does not jump to the passage.
- **Research Mode in AI Chat**: the web results the answer used are listed as numbered **Web sources** chips; a click opens the page in your browser.

---

## Performance & Hardware

### How fast is the built-in model?

It has not been benchmarked, and speed depends on your processor, memory and GPU. Each reply in AI Chat shows its token count and speed, so you can compare models on your own computer. For faster replies, see [AI responses are slow](#ai-responses-are-slow).

### Can Agent-X use multiple GPUs?

No. Agent-X passes a single layer count for the built-in model (`LocalLlmProvider.ResolveGpuLayers` in `src/AgentX.Core/AI/Providers/LocalLlmProvider.cs`); there is no multi-GPU setting.

### How much disk space do I need?

| Component | Size |
|-----------|------|
| **SLIM installer** | 228 MiB (v2.1.1) |
| **Built-in model** | About 2 GB (3B), or about 800 MB for the 1B model |
| **Speech-to-text model** | About 142 MB, only if you install it |
| **Database** | Grows with your vault: the extracted text, the chunks and one embedding per chunk. An embedding takes 1,536 bytes with `all-minilm` (384 dimensions) and 12,288 bytes with the built-in model (3,072 dimensions) |
| **Documents** | Imported files stay where they are. Agent-X keeps its own copies only of pages saved by Web Import (`%LocalAppData%\AgentX\WebImports`) and of browser clips, Smart Inbox and connector items (`Clips` and `Inbox` in `%LocalAppData%\AgentX`) |

### How much memory does Agent-X use?

It has not been measured and depends on the model. When the built-in model is the active provider, it is loaded into memory as Agent-X starts (the 3B file is about 2 GB). For large vaults the HNSW index is held in memory and takes roughly chunks x dimensions x 4 bytes plus its links. A smaller embedding model, a lower Context Window or a smaller chat model all reduce memory use.

---

## Privacy & Security

### Is my data sent to the cloud?

**Not by default.** With the built-in model, your documents, conversations and settings stay on your computer, and Agent-X sends no telemetry, usage analytics or crash reports. Data leaves the computer only for what you set up:

- **A cloud AI provider (OpenAI or Anthropic) as the active provider.** Every AI feature then sends its text there: chat messages with the conversation context and memories, Ask Your Files questions with the retrieved passages (with personal details such as e-mail addresses and phone numbers redacted first), auto-tagging (the first 2,000 characters of each newly indexed document), AI titles, Quick Actions, document comparisons, workflow AI steps, Smart Inbox previews, Draft As Me, conversation summaries and the background memory extraction after each chat reply.
- **Ollama on another computer**: the same text goes to that computer.
- **Enable Auto-Routing** with a cloud API key set: chat replies can go to that cloud provider.
- **An OpenAI embedding model** (`text-embedding-...`) as the Embedding Model: the text of every indexed chunk and search query goes to OpenAI.
- **Research Mode**: while it is on in Settings and in the chat, your message goes to the search provider (Brave, Serper, or your SearXNG instance, which forwards it to public search engines).
- **Web Import**, `.url` and `.webloc` files: Agent-X fetches the pages you ask for.
- **Calendar and email connectors**: Agent-X reads from your Google or Microsoft account.
- **Model downloads**: the built-in model and the speech-to-text model come from Hugging Face when you ask for them.
- **Collaborative Sync and backups** write encrypted files to the folder you choose, which may be a cloud-synced folder.

The Dashboard's privacy line and the LOCAL/NET lamp in the status strip list the cloud or remote AI provider, model routing, a configured web search provider and the connectors; they do not report an OpenAI embedding model (see [KNOWN-ISSUES](../KNOWN-ISSUES.md)).

### How is my data encrypted?

- **Database**: optional. Turn it on under Settings > Database Encryption; the whole `agentx.db` is then encrypted with SQLCipher (AES-256). The key is a random 256-bit key, wrapped with Windows DPAPI for your account and kept in `%LocalAppData%\AgentX\encryption.info.json`. Encryption cannot be turned off again.
- **Secrets**: API keys, the web search key or SearXNG address, the local API token, OAuth client secrets and the scheduled-backup password are DPAPI-encrypted in `settings.json`; Google and Microsoft sign-in tokens are DPAPI-encrypted in the database.
- **Backups**: with a password, AES-256-GCM with a key derived by PBKDF2 (600,000 iterations).
- **Collaborative Sync packages**: AES-256-GCM with a key derived by PBKDF2-SHA256 from your Encryption Key (100,000 iterations).

### What happens if I forget my passphrase?

Database encryption turned on in current versions uses no passphrase: the key is tied to your Windows account, and you are not asked for anything at startup. Keep `encryption.info.json` and your Windows account: without them the encrypted database cannot be opened.

A database encrypted by an earlier version with a passphrase asks for it at startup, and backup passwords and the sync Encryption Key are also yours alone. **None of them can be recovered.** Store them in a password manager.

### Are my API keys safe?

API keys are stored in `%LocalAppData%\AgentX\settings.json`, encrypted with Windows DPAPI:

- Only your Windows account can decrypt them
- They are never written in plain text; a plain-text key found in the file is encrypted on the next start
- They are sent only to the provider they belong to

They are not in Windows Credential Manager, and they are not part of backups.

### Can I use Agent-X in a corporate environment?

Yes. Consider:

| Factor | Notes |
|--------|-------|
| **Data policy** | Data stays on the computer unless you set up the services listed above |
| **Approval** | Check with IT before installing; the current installers are not code-signed |
| **Licensing** | MIT: free for commercial use, no per-seat fees |
| **Support** | GitHub issues on the project repository, or support@strategia-x.com |

---

## Licensing

### How is Agent-X licensed?

Agent-X is free and open-source software released under the **MIT License** (`LICENSE`). Every feature is available to every user: the built-in model, semantic search, GPU offloading, Ask Your Files, the Knowledge Graph, multi-provider AI, the local API, sync and analytics. There is nothing to buy, activate, or upgrade.

### Is there anything I need to pay for?

Not for Agent-X. There are no tiers, subscriptions, trials or quotas. If you use OpenAI, Anthropic, Brave Search or Serper, those services bill you under your own account; the Cost Tracking card in Settings estimates what the OpenAI and Anthropic calls cost.

### Can I use Agent-X commercially?

Yes. The MIT License lets you use, copy, modify, merge, publish, distribute, sublicense, and sell copies of Agent-X, for personal, business, or enterprise use, with no per-user or per-seat restrictions. The only condition is that the MIT copyright and permission notice be included in copies of the software.

---

## Troubleshooting

### Agent-X won't start

**Possible causes:**

1. **It is already running in the notification area**
   - Closing the window hides Agent-X instead of quitting it
   - Open it from its tray icon, or check Task Manager for `AgentX.App.exe`. Use Exit in the tray menu to quit

2. **The database could not be opened or upgraded**
   - Agent-X then shows "Agent-X could not start" and stops before loading any feature
   - Follow the dialog's steps, and include the log files from `%LocalAppData%\AgentX\Logs` when you report it

3. **Corrupt installation**
   - Uninstall and reinstall Agent-X
   - Your data in `%LocalAppData%\AgentX\` is preserved

### AI responses are slow

**Solutions:**

1. **Offload the built-in model to an NVIDIA GPU**
   - Install the NVIDIA CUDA 12 Toolkit; with **Automatic GPU layers** on, the layers then run on the GPU
   - See [What is GPU acceleration?](#what-is-gpu-acceleration)

2. **Switch to a smaller model**
   - For example the built-in model, or a small Ollama model the Hardware Advisor suggests

3. **Reduce the context**
   - Lower **Context Window** or **Max Tokens** under Settings > Inference
   - Start a new conversation when you change topics

### Search returns no results

**Possible causes:**

1. **Documents not indexed yet**
   - Check the status in the **Knowledge Vault** (Indexed, Processing, Pending or Failed)
   - A Failed row shows why; fix the cause and click **Re-index**

2. **Minimum relevance too high**
   - Lower **MIN RELEVANCE** under Advanced on the Search page (30% by default)

3. **Wrong search mode or scope**
   - Try **Hybrid** or **Keyword** mode, and clear the Collection and file type filters

### Import fails

**Common issues:**

| Issue | Solution |
|-------|----------|
| **Unsupported format** | Check the format list above; save legacy `.doc` files as `.docx` |
| **Scanned PDF** | The PDF has no text layer; run it through OCR software first |
| **Corrupt or encrypted file** | The row shows the reason; open the file in its own application and save a clean copy |
| **Audio marked Failed** | Install the speech-to-text model on the Model Manager page; the file is transcribed afterwards |

### GPU acceleration not working

**Check:**

1. **Is the CUDA 12 Toolkit installed?**
   - The built-in model needs it for GPU offload (`CUDA_PATH` must point at it)
   - Updating the NVIDIA driver alone is not enough

2. **Enough video memory?**
   - With Automatic, a GPU with under 2 GB gets no layers
   - Turn Automatic off and enter a lower **GPU Layers** value if the model does not load; 0 keeps it on the CPU

3. **Is it an NVIDIA GPU?**
   - AMD and Intel GPUs are not used by the built-in model; Ollama can use them where it supports them

### Database locked

**Causes:**

1. **A second copy of Agent-X is running**
   - Check Task Manager for more than one `AgentX.App.exe` and exit the extra copy

2. **Another program has the database open**
   - For example a backup or sync tool copying `agentx.db`; close it and try again

3. **File lock not released**
   - Restart your computer

---

## Still Have Questions?

| Resource | Link |
|----------|------|
| **User Guide** | [Full user guide](../USER-GUIDE.md) |
| **Troubleshooting** | [Detailed troubleshooting guide](troubleshooting.md) |
| **Known issues** | [Current limitations](../KNOWN-ISSUES.md) |
| **GitHub Issues** | Report bugs or request features on the project repository |

---

*Last updated: 2026-09-27*
