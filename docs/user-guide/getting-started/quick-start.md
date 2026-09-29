# Agent-X Quick Start Guide

**From installation to your first answers from your own documents**

This guide walks through installing Agent-X, the setup wizard it shows on first run,
choosing where the AI runs, importing documents, and asking questions about them. Page,
button and setting names are given as the English interface shows them. Agent-X also runs
in German, Spanish, French, Japanese and Simplified Chinese: it follows your Windows display
language, and **Settings > Appearance > Language** changes it.

---

## Table of Contents

1. [Install Agent-X](#1-install-agent-x)
2. [First run: the setup wizard](#2-first-run-the-setup-wizard)
3. [Choose where the AI runs](#3-choose-where-the-ai-runs)
4. [Import your documents](#4-import-your-documents)
5. [Ask questions about your files](#5-ask-questions-about-your-files)
6. [Chat with the AI](#6-chat-with-the-ai)
7. [Search your documents](#7-search-your-documents)
8. [Next steps](#8-next-steps)
9. [Getting help](#9-getting-help)

---

## 1. Install Agent-X

**Requirements:** a 64-bit (x64) PC with Windows 10 version 2004 (build 19041) or later, or
Windows 11. The installer refuses older Windows builds.

Download an installer from the
[GitHub Releases page](https://github.com/Git-Rocky-Stack/Agent-X/releases). There are two:

| Installer | Built-in AI model | Use it when |
|-----------|-------------------|-------------|
| `AgentX-Setup-<version>-x64.exe` (SLIM) | Not included. The setup wizard offers to download it (about 1.9 GB) on first run. | Most installs |
| `AgentX-Setup-<version>-x64-offline.exe` (OFFLINE) | Included (Llama 3.2 3B Instruct), so the model works without a download | Machines with no or slow internet access |

The OFFLINE installer is about 2 GB, too large for a GitHub release asset, so its download
link is in the release notes. To check that a download is genuine, follow
[`RELEASE-SIGNING.md`](../../RELEASE-SIGNING.md#verifying-a-download).

**Running the installer:**

1. Double-click the installer. Setup asks whether to install for you only (no administrator
   rights needed; the default folder is `%LOCALAPPDATA%\Programs\Agent-X`) or for all users
   (in Program Files, with administrator rights).
2. A Start menu shortcut is always created. A desktop shortcut is optional and off by
   default.
3. On the last page, leave **Launch Agent-X** checked to start the app right away.

Agent-X keeps its data in `%LOCALAPPDATA%\AgentX`: the database (`agentx.db`), the
`settings.json` file, the downloaded models in `Models` and the log files in `Logs`.

---

## 2. First run: the setup wizard

The first time Agent-X starts, it opens the setup wizard instead of the Dashboard. There is
no passphrase or unlock screen on a new installation. (Database encryption is optional and
uses a key tied to your Windows account; see [Next steps](#8-next-steps).)

The wizard has five steps. A row of five dots at the top shows where you are, **Back** at the
bottom left returns to the previous step, and the navigation rail stays hidden until you
finish. Nothing you enter is saved until the last step.

### Step 1: Welcome to Agent-X

Three cards introduce the app: **Private & Secure**, **Unlimited & Free** and
**Your Data, Your Rules**. Click **Get Started**.

### Step 2: Connect to Ollama

[Ollama](https://ollama.com) is a free program that runs open AI models on your computer.
Agent-X does not need it: the built-in model works without it. Use this step only if you
already run Ollama or want to.

- **OLLAMA ENDPOINT** holds the address of your Ollama server, `http://localhost:11434` by
  default.
- **Test Connection** checks that address. It reports "Connected to Ollama successfully!" or
  "Could not connect to Ollama. Make sure Ollama is running."
- Without Ollama, click **Skip for now** (or **Next**).

### Step 3: Choose Your Models

A line at the top summarizes your hardware: graphics card, memory, and the model size it
can handle.

- **If the Ollama test succeeded**, the **CHAT MODEL** and **EMBEDDING MODEL** lists show the
  models installed in your Ollama. Agent-X preselects a Llama 3, Mistral or Phi model for chat
  and a model whose name contains "minilm", "nomic", "embed" or "bge" for embeddings.
- **Otherwise** the step shows **No models available** and the defaults Agent-X uses with
  Ollama: `llama3.2` for chat and `all-minilm` for embeddings.

The embedding model turns your documents into vectors for search. If you pick an Ollama
embedding model here, Agent-X uses it for all indexing and searching from then on, even when
the built-in model is installed, so Ollama must be running whenever you import or search.
You can change this later in **Settings** (see [section 3](#3-choose-where-the-ai-runs)).

### Step 4: Your AI is Ready

**BUILT-IN LOCAL MODEL** shows the model that ships with Agent-X, **Llama 3.2 3B Instruct**
(Q4_K_M quantization, about 2 GB, run on your device through LLamaSharp):

- If it is installed, its status reads **Ready** with its size.
- If not (the SLIM installer), click **Download built-in model (~1.9 GB)**. A progress bar
  shows the download from Hugging Face. When it completes, the built-in model becomes the
  active AI provider immediately.

Below the model, a line says what the model will run on: the CPU, or an NVIDIA graphics card.
GPU acceleration needs an NVIDIA GPU with at least 2 GB of video memory and the NVIDIA CUDA 12
Toolkit installed; without the toolkit the line says "GPU acceleration needs the NVIDIA CUDA 12
Toolkit."

**CLOUD PROVIDERS (OPTIONAL)** has an **OpenAI** and an **Anthropic** API key box. Leave them
empty to stay fully local. A key you add here is stored encrypted on your computer (Windows
DPAPI) and sent only to that provider.

### Step 5: You're All Set!

**CONFIGURATION SUMMARY** lists the Ollama status, chat model, embedding model, built-in
model, cloud providers and where your data is stored (`%LOCALAPPDATA%\AgentX`). Click
**Launch Agent-X**. Agent-X saves your choices and opens the Dashboard. The active AI
provider becomes the first available of:

1. the built-in model, if it is installed;
2. Ollama, if the connection test succeeded;
3. OpenAI, if you entered a key;
4. Anthropic, if you entered a key.

If you leave the wizard before **Launch Agent-X** (for example with a keyboard shortcut), it
counts as skipped and does not come back on the next start; what you typed is not saved. To
open it again, press `Ctrl+P` (Jump To), type **Onboarding** and press Enter.

---

## 3. Choose where the AI runs

Agent-X works with one AI provider at a time. You pick it in **Settings > AI Providers >
Active Provider** (`Ctrl+,`), then click **Save Settings** at the bottom of the page:

| Active Provider | Where it runs | What it needs |
|-----------------|---------------|---------------|
| **Built-in LLM (Local)** (the default) | On your computer | The built-in model file (downloaded in the wizard or bundled by the OFFLINE installer) |
| **Ollama (Local)** | In Ollama, usually on your computer | Ollama running at the **Endpoint**; the **Default Chat Model** pulled in Ollama (the **Model Manager** page can pull models) |
| **OpenAI** | OpenAI's servers | An **API Key**; **Default Model** is `gpt-4o-mini` unless you change it |
| **Anthropic Claude** | Anthropic's servers | An **API Key**; **Default Model** is `claude-sonnet-5` unless you change it |

**Where your content goes.** Every AI feature sends its text to the active provider. With a
local provider nothing leaves your computer. With OpenAI or Anthropic, that provider receives
your chat messages, your Ask Your Files questions together with the document passages
retrieved for them, the start of each document for its tags and AI title, and whatever you
run through Quick Actions and Workflows. **Multi-Model Routing** in Settings (off by default)
can also send a prompt to a cloud provider you have a key for. An empty AI Chat conversation
states where your messages will be sent.

**Embeddings are separate from the chat provider.** Importing and searching documents needs
an embedding model. With the default **Embedding Model** setting (`all-minilm`, in the
**Ollama (Local)** card), Agent-X uses the built-in model when it is installed and otherwise
asks Ollama for `all-minilm` (install it with `ollama pull all-minilm`). An OpenAI embedding
model name such as `text-embedding-3-small` in that box sends document text to OpenAI for
embedding. Anthropic has no embedding service. If you use only a cloud chat provider, make
sure one of these embedding options is available, or documents cannot be indexed.

**GPU acceleration for the built-in model.** In **Settings > AI Providers > Built-in LLM
(Local)**, **Automatic GPU layers** is on by default: with an NVIDIA GPU it puts 16 layers on
the GPU for 2 to 4 GB of video memory, 28 for 4 to 6 GB and 33 for 6 GB or more (none below
2 GB). Turn it off to set **GPU Layers** yourself (0 keeps the model on the CPU). Layers run on
the GPU only when the NVIDIA CUDA 12 Toolkit is installed. **Save Settings** reloads the
built-in model; no restart is needed.

---

## 4. Import your documents

Open the **Knowledge Vault** from the rail (KNOWLEDGE group), with `Ctrl+I`, or with the
**Import Files** tile on the Dashboard.

**Ways to import:**

- **Import Files** opens a file picker for PDF, Word (`.docx`), text, Markdown, CSV, JSON,
  HTML and XML files and common code files. Select one or more.
- **Import Folder** imports every supported file in a folder and all its subfolders.
- **Drag and drop:** while the vault is empty, you can drag files or folders onto the
  **Drag and drop files here** area.
- **Watch folders:** in **Settings > Knowledge Vault > Watch Folders**, **Add Folder** (with
  or without **Include subfolders**) makes Agent-X import the supported files already in the
  folder and then new and changed ones while it runs. This happens while **Auto-index watch
  folders** is on (the default); adding or removing a folder takes effect at once, the switch
  when you save settings.

Before importing picked or dropped files, Agent-X compares their content with the vault. Exact
copies of documents already there are reported under **Duplicate Documents Detected**:
choose **Skip Duplicates** or **Import All**. Import Folder skips duplicates and says how many.

**Supported formats:**

| Kind | Extensions | Notes |
|------|------------|-------|
| Documents | `.pdf`, `.docx`, `.md`, `.markdown`, `.mdx`, `.txt` | Legacy `.doc` and `.rtf` files are not supported |
| Data and config | `.csv`, `.json`, `.xml`, `.log`, `.yaml`, `.yml`, `.toml`, `.ini`, `.cfg` | |
| Code | `.cs`, `.js`, `.ts`, `.py`, `.java`, `.cpp`, `.c`, `.h`, `.go`, `.rs`, `.swift`, `.kt`, `.rb`, `.php`, `.html`, `.htm`, `.css`, `.scss`, `.sql`, `.sh`, `.xaml` | |
| Images | `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tiff` | Text is read with Windows OCR. Use Import Folder or drag and drop |
| Audio | `.mp3`, `.wav`, `.m4a`, `.flac`, `.ogg`, `.webm` | Transcribed on your computer once the speech-to-text model is installed (**Model Manager > Speech-to-Text Model > Download**, about 142 MB). Use Import Folder or drag and drop |
| Web shortcuts | `.url`, `.webloc` | Agent-X fetches the linked page. For web pages in general, use the **Web Import** page or the browser extension |

**What happens after import.** The vault stores the extracted text and the path to your file;
the file itself is not copied or moved. Each document is then indexed in the background: its
text is split into chunks, embedded, and added to the keyword index. A document's badge moves
from **Pending** to **Processing** to **Indexed**, and the **In Queue** figure and the
**Indexing in progress...** bar show what is left. A document that cannot be indexed is marked
**Failed** with the reason in red; **Re-index** tries again. After indexing, the AI model
suggests up to five tags for the document.

Each row has buttons for **AI Title** (asks the model for a short title), **Detail** (opens the
**Document Preview** with the details, tags and the document text, where you can select a
passage to annotate it), **Workflow**, re-index, open file location and delete. **Select**
turns on multi-select for re-indexing or deleting several documents. Deleting asks first and
never deletes the file on disk. The search box filters the list by file name or tag; to search
inside documents, use Semantic Search or Ask Your Files.

---

## 5. Ask questions about your files

**Ask Your Files** answers questions from your indexed documents and cites its sources. Open
it from the rail (INTELLIGENCE group), with `Ctrl+3`, or with the **Ask Files** tile on the
Dashboard.

1. A badge at the top shows how many knowledge chunks are available, or "Documents are being
   indexed..." or "Import documents to get started".
2. To narrow the search, pick a collection in the list next to it; **All Collections**
   searches the whole vault.
3. Type a question in **Ask a question about your documents...** and press Enter. Clicking
   one of the examples under **TRY ASKING** puts it in the box.
4. Agent-X retrieves the most relevant passages (semantic and keyword search combined), gives
   them to the active AI model, and streams the answer with numbered citations such as `[1]`.
5. The **Sources** panel on the right lists each cited passage with the document name, the
   page when the format has pages, a match percentage and an excerpt. **Open source document**
   shows the file in File Explorer.

The model is told to answer only from the retrieved passages, but check the cited sources for
anything that matters. **Clear** empties the page and stops an answer that is still being
written. Questions and answers on this page are not saved as conversations.

---

## 6. Chat with the AI

**AI Chat** is a general conversation with the active model. Open it with `Ctrl+2`, or press
`Ctrl+N` anywhere to start a new conversation.

- The model box at the top (**Select model...**) lists the active provider's models. To use
  another provider, change it in Settings.
- An empty conversation shows **Start a Conversation** and a line saying where your messages
  go. With a local model it adds a **100% Private** badge.
- Type in **Message Agent-X...**: Enter sends, Shift+Enter starts a new line, and
  **Stop generation** ends a reply early.
- Replies have buttons to rate (**Good response**, **Poor response**), copy, regenerate and
  delete them. Your own messages can be edited (**Save & Resend**), copied, branched
  (**Branch from here**) or deleted.
- Conversations are saved in the local database and listed in the sidebar, where you can
  search, pin and file them in folders.

AI Chat does not search your Knowledge Vault; for answers from your documents, use Ask Your
Files. Chat can add live web results in Research Mode: first turn on **Settings > Research
Mode > Enable Research Mode** and enter a Brave or Serper API key or a SearXNG address, then
switch on the Research mode button next to the message box. Research Mode is off by default,
and with it on your questions are sent to the search provider.

---

## 7. Search your documents

**Semantic Search** finds passages by meaning. Open it with `Ctrl+F` (or `Ctrl+4`).

1. Type in **Search your documents...** and press Enter.
2. Under **SEARCH MODE**, choose **Semantic** (the default, by meaning), **Keyword**
   (full-text search for exact words, names and IDs) or **Hybrid** (both, merged with
   Reciprocal Rank Fusion).
3. Narrow the results with **COLLECTION** and **FILE TYPE**, or open **Advanced** for
   **MIN RELEVANCE** (30% by default), **MAX RESULTS** (20 by default) and a date range.
4. Each result shows its relevance, an excerpt, and the page and chunk it came from.
   **Open** shows the file in File Explorer; **Workflow** sends the result to the Workflows
   page.

**Save** keeps the current search under **Saved Filters**, and **Search History** lists your
recent searches.

---

## 8. Next steps

| Page | What it does | Shortcut |
|------|--------------|----------|
| **Collections** | Group documents; Ask Your Files and Semantic Search can be limited to a collection | `Ctrl+6` |
| **Knowledge Graph** | Shows how your documents, tags and collections connect | `Ctrl+G` |
| **Workflows** | Runs multi-step AI text pipelines on demand | `Ctrl+Shift+W` |
| **Quick Actions** | Summarizes a document or extracts its key points, translates pasted text, scans for duplicates and suggests collections | none |
| **Compare Documents** | Compares two or more documents: similarities, differences and contradictions | none |
| **Web Import** | Imports web pages, YouTube videos, RSS or Atom feeds and sitemaps into the vault | `Ctrl+Shift+E` |
| **Model Manager** | Pulls and removes Ollama models; installs the speech-to-text model | `Ctrl+8` |
| **Hardware Advisor** | Recommends models for your hardware | none |
| **Settings** | Providers, inference, Knowledge Vault, Research Mode, storage, encryption, local API | `Ctrl+,` |

- **Database encryption:** **Settings > Database Encryption** encrypts the database with
  SQLCipher, using a key tied to your Windows account. It is off by default.
- **Keyboard:** `Ctrl+K` opens the Command Palette, `Ctrl+P` opens Jump To (pages, documents
  and conversations), and `F1` lists every shortcut. `Win+Shift+A` opens Quick Chat from
  anywhere in Windows while Agent-X runs.
- **Closing the window** hides Agent-X to the notification area, where it keeps running (watch
  folders, the local API for the browser extension). Its tray menu has **Open Agent-X**,
  **Quick Chat**, **Settings** and **Exit**; use **Exit** to quit.
- **In-app guide:** **User Guide**, in the SUPPORT group at the end of the rail, covers every
  page.

Further reading:

- [User Guide](../../USER-GUIDE.md): complete feature documentation
- [Scenarios](../scenarios/README.md): worked examples
- [Templates](../templates/README.md): system prompts, workflow templates and prompts to reuse
- [Keyboard shortcuts](../keyboard-shortcuts.md)
- [FAQ](../faq.md) and [Troubleshooting](../troubleshooting.md)

---

## 9. Getting help

| Document | Description |
|----------|-------------|
| [Product documentation](../../README.md) | Features, install, build, configuration, data storage |
| [Architecture](../../ARCHITECTURE.md) | System architecture overview |
| [Developer Guide](../../DEVELOPER-GUIDE.md) | Developer reference |
| [API Reference](../../API-REFERENCE.md) | Public API documentation |

- **Bugs:** open an issue on [GitHub](https://github.com/Git-Rocky-Stack/Agent-X/issues) with
  the version shown at the bottom of the Settings page. The log files in
  `%LOCALAPPDATA%\AgentX\Logs` usually show what went wrong.
- **Security problems:** report them privately as described in
  [`SECURITY.md`](../../../SECURITY.md).
- **What changed:** see the [CHANGELOG](../../../CHANGELOG.md).
