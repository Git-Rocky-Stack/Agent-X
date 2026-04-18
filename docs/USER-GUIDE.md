# Agent-X User Guide

**Agent-X -- Local-First AI Personal Intelligence Hub for Windows**

Version 1.3.0 | Last Updated: April 2026

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [System Requirements](#2-system-requirements)
3. [Installation](#3-installation)
4. [Getting Started: Onboarding Wizard](#4-getting-started-onboarding-wizard)
5. [Dashboard](#5-dashboard)
6. [AI Chat](#6-ai-chat)
7. [Knowledge Vault](#7-knowledge-vault)
8. [Collections](#8-collections)
9. [Semantic Search](#9-semantic-search)
10. [Ask Your Files (RAG)](#10-ask-your-files-rag)
11. [Quick Actions](#11-quick-actions)
12. [Model Manager](#12-model-manager)
13. [Hardware Advisor](#13-hardware-advisor)
14. [Settings](#14-settings)
15. [Command Palette](#15-command-palette)
16. [Keyboard Shortcuts](#16-keyboard-shortcuts)
17. [Status Bar](#17-status-bar)
18. [Workspace Profiles](#18-workspace-profiles) *(v1.3.0)*
19. [Smart Inbox](#19-smart-inbox) *(v1.3.0)*
20. [Comparative Analysis](#20-comparative-analysis) *(v1.3.0)*
21. [Voice Input](#21-voice-input) *(v1.3.0)*
22. [Plugin Manager](#22-plugin-manager) *(v1.3.0)*
23. [Sync Settings](#23-sync-settings) *(v1.3.0)*
24. [Troubleshooting](#24-troubleshooting)
25. [FAQ](#25-faq)
26. [Appendix: Supported File Types](#26-appendix-supported-file-types)

---

## 1. Introduction

Agent-X is a local-first AI personal intelligence hub that runs entirely on your Windows machine. It connects to [Ollama](https://ollama.com) to power all AI features -- including chat, document analysis, semantic search, and retrieval-augmented generation (RAG) -- without sending any of your data to the cloud.

### Key Principles

- **Privacy by design.** All processing happens on your hardware. Your documents, conversations, and embeddings never leave your machine.
- **Powered by Ollama.** Agent-X uses Ollama as its AI backend, giving you access to hundreds of open-source language models (Llama, Mistral, Phi, DeepSeek, and many more).
- **Knowledge management.** Import documents, organize them into collections, and ask questions across your entire knowledge base using semantic search and RAG.
- **Hardware-aware.** Agent-X detects your GPU, CPU, RAM, and NPU to recommend the best models for your specific hardware.

---

## 2. System Requirements

### Minimum Requirements

| Component       | Requirement                                      |
|-----------------|--------------------------------------------------|
| Operating System | Windows 10 version 1809 or later / Windows 11   |
| Runtime         | .NET 8.0 Desktop Runtime                         |
| RAM             | 8 GB (16 GB recommended)                         |
| Disk Space      | 500 MB for Agent-X + space for AI models         |
| AI Backend      | Ollama installed and running                      |

### Recommended Hardware

| Component | Recommendation                                                |
|-----------|---------------------------------------------------------------|
| GPU       | NVIDIA GPU with 8+ GB VRAM (for GPU-accelerated inference)    |
| RAM       | 16 GB or more                                                 |
| CPU       | Modern multi-core processor (Intel 12th Gen+ or AMD Ryzen 5000+) |
| Storage   | SSD with at least 50 GB free (for models and document storage)|

> **Note:** Agent-X works without a dedicated GPU. Models will run on CPU, which is slower but fully functional. The Hardware Advisor page will recommend appropriate models regardless of your configuration.

---

## 3. Installation

### Step 1: Install Ollama

Ollama is the AI backend that Agent-X uses for all language model operations. You must install it before using AI features.

1. Visit [https://ollama.com/download](https://ollama.com/download).
2. Download the Windows installer.
3. Run the installer and follow the prompts.
4. After installation, Ollama runs as a background service on `http://localhost:11434`.

To verify Ollama is running, open a terminal and run:

```
ollama list
```

If this returns a list of models (or an empty list), Ollama is running correctly.

### Step 2: Pull an Initial Model

Before launching Agent-X, it is helpful (but not required) to pull at least one chat model and one embedding model:

```
ollama pull llama3.2
ollama pull all-minilm
```

The `llama3.2` model is an excellent general-purpose chat model, and `all-minilm` provides fast text embeddings for semantic search.

### Step 3: Install Agent-X

1. Run the `AgentX-Setup-1.0.0-x64.exe` installer.
2. Follow the installation wizard.
3. Launch Agent-X from the Start Menu or desktop shortcut.

---

## 4. Getting Started: Onboarding Wizard

On first launch, Agent-X presents a 5-step onboarding wizard to configure your environment. The navigation sidebar is hidden during onboarding to provide a focused setup experience.

### Step 0: Welcome

The welcome screen introduces Agent-X and its core capabilities. Click **Get Started** to begin configuration.

### Step 1: Ollama Connection

This step configures and tests the connection to your Ollama instance.

| Field             | Default Value              | Description                                           |
|-------------------|----------------------------|-------------------------------------------------------|
| Ollama Endpoint   | `http://localhost:11434`   | The URL where Ollama is running. Change only if you are running Ollama on a different machine or port. |

**To test the connection:**

1. Ensure Ollama is running in the background.
2. Verify or modify the endpoint URL.
3. Click **Test Connection**.
4. A green status message confirms a successful connection. A red message indicates Ollama is not reachable.

If you want to configure Ollama later, click **Skip** to proceed without testing.

### Step 2: Model Selection

Once connected to Ollama, this step loads all installed models and allows you to select defaults.

- **Chat Model** -- The model used for conversations and text generation (e.g., `llama3.2`, `mistral`).
- **Embedding Model** -- The model used for creating vector embeddings when indexing documents (e.g., `all-minilm`, `nomic-embed-text`).

Agent-X auto-selects recommended models based on their names. You can change the selection at any time.

The hardware summary line at the top of this step shows your detected GPU, RAM, and recommended maximum model size.

> **Tip:** If no models appear, make sure you have pulled at least one model via `ollama pull <model-name>` before reaching this step.

### Step 3: License Key (Optional)

Enter a license key to unlock additional features and higher document limits. This step is entirely optional -- Agent-X works in trial mode without a key.

If you do not have a license key, click **Next** to skip this step.

### Step 4: Summary

Review your configuration:

- Ollama connection status and endpoint
- Selected chat model
- Selected embedding model

Click **Finish** to save your settings and enter Agent-X. You will land on the Dashboard.

---

## 5. Dashboard

The Dashboard is your home base in Agent-X. It provides an at-a-glance overview of your knowledge base, AI connection status, and quick access to common actions.

### AI Connection Status

At the top of the Dashboard, a status banner indicates whether Agent-X is connected to Ollama:

- **Connected** (green indicator) -- Ollama is running and responsive.
- **Not Detected** (red indicator) -- Ollama is not running or not reachable. Click **Setup AI** to navigate to Settings and configure the endpoint.

### Quick Search

Below the status banner, a search bar provides instant access to semantic search across your knowledge base. You can also invoke this from anywhere using the `Ctrl+K` shortcut.

### Stat Cards

Four stat cards provide key metrics:

| Card        | Description                                                    |
|-------------|----------------------------------------------------------------|
| Documents   | Total number of documents imported into the Knowledge Vault.   |
| Storage     | Total disk space used by imported documents and their embeddings. |
| AI Sessions | Number of AI chat conversations stored.                        |
| System      | Detected GPU name and available RAM.                           |

### Recent Documents

A list of the most recently imported documents, showing filename, type, size, and import date. Click a document to view it or navigate to the Knowledge Vault for full details.

### Recent Conversations

A list of your most recent AI chat conversations, showing the conversation title, last message date, and message count. Click a conversation to resume it in AI Chat.

### Insights

- **File Type Distribution** -- A breakdown of your imported documents by type (PDF, DOCX, Markdown, Code, etc.).
- **Top Collections** -- Your most populated collections with document counts.

### Quick-Action Buttons

A row of action buttons at the bottom of the Dashboard provides one-click access to frequently used features:

| Button       | Action                                     |
|--------------|--------------------------------------------|
| New Chat     | Opens the AI Chat page with a new conversation. |
| Import Files | Opens the Knowledge Vault file import dialog.   |
| Search       | Navigates to the Semantic Search page.          |
| Ask Files    | Navigates to the Ask Your Files (RAG) page.     |

### Refresh

Click the **Refresh** button in the page header to reload all Dashboard data (document counts, conversations, insights, and connection status).

---

## 6. AI Chat

AI Chat provides a full-featured conversational interface for chatting with local language models. All conversations are stored locally on your machine.

### Starting a Conversation

1. Navigate to **AI Chat** from the sidebar or press `Ctrl+N`.
2. Type your message in the input field at the bottom.
3. Press **Enter** or click the **Send** button.
4. Agent-X streams the response from the selected model in real time.

### Conversation Sidebar

The left panel displays all your conversations. Use it to:

- **Search** conversations by title or content.
- **Pin** important conversations to keep them at the top of the list.
- **Delete** conversations you no longer need.
- Click a conversation to switch to it.

### Creating and Managing Conversations

| Action              | How                                                            |
|---------------------|----------------------------------------------------------------|
| New Conversation    | Click the **New Conversation** button in the toolbar or press `Ctrl+N`. |
| Clear Messages      | Click the **Clear** button to remove all messages from the current conversation while keeping the conversation entry. |
| Delete Conversation | Right-click a conversation in the sidebar and select **Delete**, or use the delete button. |

### System Prompt Picker

Click the **System Prompt** button in the toolbar to select a system prompt that shapes the AI's behavior for the conversation.

System prompts are organized into categories:

| Category  | Description                                          |
|-----------|------------------------------------------------------|
| General   | All-purpose assistant prompts.                       |
| Writing   | Prompts optimized for writing, editing, and composition. |
| Code      | Programming-focused prompts for code generation, review, and debugging. |
| Analysis  | Prompts for data analysis, research, and critical thinking. |
| Creative  | Prompts for brainstorming, storytelling, and creative work. |

You can mark system prompts as **favorites** for quick access.

### Model Selection

Use the **Model** dropdown in the toolbar to switch between installed Ollama models for the current conversation. The selected model applies to all subsequent messages in the conversation.

### Message Metadata

Each AI response displays metadata beneath the message:

- **Token count** -- The number of tokens generated.
- **Generation time** -- How long the response took to complete.
- **Tokens/second** -- The generation speed, useful for benchmarking model performance.

### Message Actions

Hover over any AI message to reveal action buttons:

| Action          | Description                                            |
|-----------------|--------------------------------------------------------|
| Copy            | Copies the message text to the clipboard.              |
| Regenerate      | Re-generates the last AI response with the same input. |

### Stopping Generation

While a response is being streamed, a **Stop** button appears. Click it to halt generation immediately. The partial response up to that point is preserved.

---

## 7. Knowledge Vault

The Knowledge Vault is where you import, manage, and index your documents. Once indexed, documents become searchable through Semantic Search and available to the RAG pipeline in Ask Your Files.

### Importing Documents

There are three ways to import files:

1. **File Picker** -- Click the **Import Files** button and select one or more files from the file dialog.
2. **Folder Picker** -- Click the **Import Folder** button to import all supported files from a directory.
3. **Drag and Drop** -- Drag files directly from Windows Explorer onto the Knowledge Vault page.

### Supported File Formats

Agent-X supports a wide range of document and code file formats:

| Category   | Extensions                                                                        |
|------------|-----------------------------------------------------------------------------------|
| PDF        | `.pdf`                                                                            |
| Documents  | `.docx`, `.doc`                                                                   |
| Text       | `.txt`, `.csv`, `.log`, `.xml`, `.json`                                           |
| Markdown   | `.md`, `.markdown`                                                                |
| Images     | `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tiff` (processed via OCR)                      |
| Code       | `.cs`, `.js`, `.ts`, `.py`, `.java`, `.cpp`, `.c`, `.h`, `.go`, `.rs`, `.swift`, `.kt`, `.rb`, `.php`, `.html`, `.css`, `.scss`, `.sql`, `.sh`, `.yaml`, `.yml`, `.toml`, `.ini`, `.cfg`, `.xaml` |

See [Appendix: Supported File Types](#20-appendix-supported-file-types) for a complete reference table.

### Document List

Each document in the vault displays:

| Field           | Description                                                       |
|-----------------|-------------------------------------------------------------------|
| Filename        | The original name of the imported file.                           |
| Type Badge      | A color-coded badge indicating the file category (PDF, Code, etc.). |
| File Size       | The size of the file on disk.                                     |
| Import Date     | When the file was imported into the vault.                        |
| Chunk Count     | The number of text chunks the document was split into during indexing. |
| Indexing Status  | One of: **Pending**, **Indexing**, **Indexed**, or **Failed**.    |

### Filtering and Searching

Use the controls at the top of the document list to narrow results:

- **File type filter** -- Filter by PDF, Document, Text, Markdown, Image, or Code.
- **Indexing status filter** -- Show only documents with a specific indexing status.
- **Search** -- Filter documents by filename.

### Document Actions

| Action           | Description                                                     |
|------------------|-----------------------------------------------------------------|
| Re-index         | Re-processes and re-embeds the document. Useful if you updated the source file or changed embedding settings. |
| Delete           | Removes the document and all its embeddings from the vault.     |
| Open in Explorer | Opens the file's location in Windows File Explorer.             |

### Indexing Process

When a document is imported, it goes through the following pipeline:

1. **Text Extraction** -- The document's text content is extracted (or OCR is applied for images).
2. **Chunking** -- The text is split into overlapping chunks based on your configured chunk size and overlap settings.
3. **Embedding** -- Each chunk is passed through the embedding model to generate a vector representation.
4. **Storage** -- The vectors are stored in the local vector database (SQLite-backed) for semantic search.

The indexing status of each document is visible in the document list. Indexing requires Ollama to be running with the configured embedding model.

---

## 8. Collections

Collections allow you to organize your documents into logical groups. They support hierarchical nesting (parent/child relationships) for flexible organization.

### Interface Layout

- **Left Panel** -- The collection tree showing all your collections in a hierarchical view.
- **Right Panel** -- The detail view for the currently selected collection, showing its documents.

### Creating a Collection

1. Click the **Create Collection** button at the top of the left panel.
2. Enter a name for the collection.
3. Optionally, select a parent collection to create a nested hierarchy.
4. Click **Create**.

### Managing Collections

| Action           | Description                                                     |
|------------------|-----------------------------------------------------------------|
| Select           | Click a collection in the tree to view its contents.            |
| Delete           | Remove a collection. Documents are not deleted from the vault.  |
| View Documents   | The right panel shows all documents assigned to the selected collection. |

### Adding and Removing Documents

- To **add** documents to a collection, use the **Add Documents** button in the collection detail view. A picker lets you select from all vault documents.
- To **remove** a document from a collection, click the remove button next to the document in the collection detail view. This removes the association only; the document remains in the Knowledge Vault.

### Document Count Badges

Each collection in the tree displays a badge showing the number of documents it contains.

---

## 9. Semantic Search

Semantic Search finds documents based on meaning, not just keyword matching. It uses vector embeddings to understand the intent behind your query and return the most relevant results.

### How to Search

1. Navigate to **Search** from the sidebar or press `Ctrl+F`.
2. Type your query in the search bar. Use natural language -- for example, "What are the performance benchmarks for the Q4 release?" rather than just "Q4 benchmarks."
3. Press **Enter** or click the **Search** button.

### Search Results

Each result displays:

| Field           | Description                                                       |
|-----------------|-------------------------------------------------------------------|
| Relevance Score | A percentage indicating how closely the result matches your query. Higher scores mean stronger semantic matches. |
| Matched Text    | An excerpt from the document chunk that matched your query.       |
| Source File     | The filename and location of the source document.                 |
| Page Number     | The page number within the source document (when available).      |

### Filtering Results

Use the filter controls to narrow your search:

- **File Type** -- Restrict results to specific document types (PDF, Code, Markdown, etc.).
- **Collection** -- Search only within documents belonging to a specific collection.

### Search History

The search page maintains a list of your recent queries for quick re-use. Click any entry in the history to re-execute that search.

### Opening Results

Click on a search result to open the source file's location in Windows File Explorer.

> **Important:** Semantic search requires documents to be indexed. If a document's status shows as "Pending," it has not yet been embedded and will not appear in search results. Ensure Ollama is running with the embedding model to complete indexing.

---

## 10. Ask Your Files (RAG)

Ask Your Files combines semantic search with AI generation to answer questions directly from your document library. This is known as Retrieval-Augmented Generation (RAG).

### How It Works

1. Navigate to **Ask Files** from the sidebar.
2. Type your question in the input field (e.g., "Summarize the key findings from the Q3 financial report").
3. Optionally, select a **collection scope** to restrict the AI's search to documents in a specific collection. Leave it as "All Documents" to search your entire vault.
4. Press **Enter** or click **Ask**.

### The Response

Agent-X retrieves the most relevant document chunks, passes them to the selected chat model as context, and streams an AI-generated response.

Key elements of the response:

- **Streamed answer** -- The AI's response appears in real time as it is generated.
- **Citation references** -- Inline references appear as numbered markers (e.g., `[1]`, `[2]`, `[3]`) within the response text. Each number corresponds to a source document.
- **Citations panel** -- Below or beside the response, a citations panel lists each referenced source with:
  - Source document filename
  - Page number (when available)
  - The exact text excerpt that was used as context

### Indexed Chunk Count

An indicator shows the total number of indexed chunks available for retrieval. A higher count means more granular search coverage across your documents.

> **Tip:** For the best results, ensure your documents are indexed and organized into relevant collections. Scoping a question to a specific collection can improve answer precision by reducing noise from unrelated documents.

---

## 11. Quick Actions

Quick Actions provide one-click AI-powered operations on individual documents. Select a document and choose an action to execute.

### Accessing Quick Actions

Navigate to **Quick Actions** from the sidebar. The interface is organized into tabbed sections:

### Summarize

Generates a concise AI-powered summary of the selected document.

1. Select a document from the picker.
2. Click **Summarize**.
3. The AI reads the document content and produces a summary.

### Key Points

Extracts the most important points from a document as a structured bullet list.

1. Select a document.
2. Click **Extract Key Points**.
3. A bulleted list of key findings, arguments, or data points is generated.

### Translate

Translates document content into a target language.

1. Select a document.
2. Choose the **target language** from the language picker (e.g., Spanish, French, German, Japanese, etc.).
3. Click **Translate**.
4. The AI produces a translation of the document content.

### Duplicates

Detects duplicate and near-duplicate documents in your vault.

- **Exact Duplicates** -- Found by comparing content hashes. These are byte-for-byte identical files.
- **Near Duplicates** -- Found by computing the semantic similarity between document embeddings. These are files with substantially similar content but potentially different formatting or minor variations.

### Organize

Provides AI-powered suggestions for organizing your documents.

1. Select a document.
2. Click **Analyze**.
3. The AI suggests:
   - **Collections** the document should belong to, based on its content.
   - **Tags** that could be applied to the document for categorization.

---

## 12. Model Manager

The Model Manager gives you full control over the Ollama models installed on your system.

### Installed Models

The main view lists all models currently installed in Ollama. Each model entry shows:

| Field          | Description                                               |
|----------------|-----------------------------------------------------------|
| Model Name     | The identifier of the model (e.g., `llama3.2:latest`).   |
| Size           | The disk space consumed by the model weights.             |
| Family         | The model architecture family (e.g., Llama, Mistral, Phi). |
| Quantization   | The quantization level (e.g., Q4_0, Q4_K_M, F16).        |

### Pulling New Models

To download a new model from the Ollama library:

1. Enter the model name in the **Pull Model** input field (e.g., `mistral:7b` or `deepseek-r1:8b`).
2. Click **Pull**.
3. A progress indicator shows the download status.
4. Once complete, the model appears in the installed list and becomes available for selection throughout Agent-X.

You can browse available models at [https://ollama.com/library](https://ollama.com/library).

### Deleting Models

To remove a model from your system:

1. Locate the model in the installed list.
2. Click the **Delete** button.
3. Confirm the deletion.

Deleting a model frees up the disk space occupied by its weights. If the deleted model was set as your default, you will need to select a new default in Settings.

### Cache Management

The Model Manager provides controls for managing Ollama's model cache, allowing you to free up disk space when needed.

---

## 13. Hardware Advisor

The Hardware Advisor detects your system hardware and provides tailored AI model recommendations.

### Hardware Detection

When you open the Hardware Advisor, it automatically scans your system for:

| Component | Details Detected                                                |
|-----------|-----------------------------------------------------------------|
| GPU       | Name, VRAM amount, and tier classification (Entry / Mainstream / Performance / Enthusiast / Professional). |
| CPU       | Name, core count, and architecture (x64 / ARM64).              |
| RAM       | Total installed memory, currently available memory, and usage percentage. |
| NPU       | Whether a Neural Processing Unit is present and its name.       |

### Performance Tier

Based on your effective available memory (GPU VRAM if available, otherwise system RAM), the advisor assigns a performance tier:

| Effective Memory | Performance Tier | Recommended Model Size  |
|------------------|------------------|-------------------------|
| Less than 4 GB   | Basic            | Up to 3B parameters     |
| 4 -- 8 GB        | Standard         | Up to 7B parameters     |
| 8 -- 16 GB       | Performance      | Up to 13B parameters    |
| 16 -- 32 GB      | High-End         | Up to 32B parameters    |
| 32 GB+           | Professional     | Up to 70B+ parameters   |

### Model Recommendations

Recommendations are categorized into three sections:

**Chat Models** -- General-purpose conversational models for everyday AI tasks, writing, analysis, and Q&A.

**Code Models** -- Models specialized for code generation, review, completion, and debugging.

**Embedding Models** -- Compact models designed for generating vector embeddings used by Semantic Search and Ask Your Files.

Each recommended model shows:

- **Model name** (the Ollama identifier you would use to pull it).
- **Description** of the model's strengths and use cases.
- **Size** on disk.
- **Installed badge** if the model is already present on your system.

### Installing Recommended Models

Click the **Install** button next to any recommendation to pull the model directly from the Hardware Advisor page. Alternatively, note the model name and use the Model Manager for more detailed download tracking.

### Advisory Message

Below the recommendations, an advisory message provides context-specific guidance about your hardware capabilities and suggestions for getting the best performance (e.g., whether GPU acceleration is available, memory considerations, or NPU support).

### Refreshing

Click the **Refresh** button to re-run hardware detection. This is useful if you have added or removed hardware, or if the initial detection encountered an error.

---

## 14. Settings

The Settings page allows you to configure Agent-X's behavior, AI parameters, knowledge vault indexing, and license status. Access Settings from the sidebar or press `Ctrl+,` (Ctrl + comma).

### AI Provider

| Setting          | Default                    | Description                                           |
|------------------|----------------------------|-------------------------------------------------------|
| Ollama Endpoint  | `http://localhost:11434`   | The URL of your Ollama instance. Modify if running Ollama on a remote machine or non-default port. |
| Default Chat Model | `llama3.2`               | The model used for AI Chat and Quick Actions.         |
| Embedding Model  | `all-minilm`               | The model used for generating document embeddings during indexing. |

### Inference

These parameters control how the AI generates responses:

| Setting         | Default | Range           | Description                                           |
|-----------------|---------|-----------------|-------------------------------------------------------|
| Temperature     | 0.7     | 0.0 -- 2.0      | Controls response randomness. Lower values (0.1--0.3) produce more deterministic, focused responses. Higher values (0.8--1.5) produce more creative and varied output. |
| Max Tokens      | 4096    | 256 -- 32,768    | The maximum number of tokens the AI can generate in a single response. |
| Context Window  | 8192    | 2,048 -- 131,072 | The total context size (prompt + response) in tokens. Larger windows allow more document context in RAG but require more memory. |

### Knowledge Vault (Indexing)

| Setting                   | Default | Range       | Description                                           |
|---------------------------|---------|-------------|-------------------------------------------------------|
| Chunk Size (tokens)       | 512     | 128 -- 2,048 | The target size of each document chunk. Larger chunks preserve more context per chunk but reduce granularity. |
| Chunk Overlap             | 50      | 0 -- 256     | The number of tokens that overlap between adjacent chunks. Overlap prevents information loss at chunk boundaries. |
| Top-K Results             | 5       | 1 -- 20      | The number of document chunks returned by semantic search. Higher values provide more context but may include less relevant results. |
| Auto-index watch folders  | On      | On / Off     | When enabled, Agent-X automatically monitors configured watch folders for new or modified files and indexes them. |

### License

The license section displays your current licensing status and provides controls for activation.

| Element            | Description                                                    |
|--------------------|----------------------------------------------------------------|
| Tier Badge         | Shows your current tier (e.g., "Trial", "Pro", "Enterprise"). |
| Activation Date    | When the license was activated (visible only for active licenses). |
| Document Limit     | The maximum number of documents allowed under your tier.       |
| License Key Input  | Enter a license key in the format `AX-X-...`.                 |
| Activate Button    | Validates and activates the entered key.                       |
| Deactivate Button  | Deactivates the current license (visible only when a license is active). |
| Status Message     | Displays success or error messages after activation attempts.  |

### Actions

| Button             | Description                                                   |
|--------------------|---------------------------------------------------------------|
| Save Settings      | Persists all current settings to disk.                        |
| Reset to Defaults  | Reverts all settings to their factory default values.         |

Settings are stored as JSON at `%LocalAppData%\AgentX\settings.json`.

---

## 15. Command Palette

The Command Palette provides quick keyboard-driven access to every page and action in Agent-X.

### Opening the Palette

Press `Ctrl+K` from anywhere in the application. A floating search dialog appears at the top of the window.

### Using the Palette

1. Start typing to filter the available commands.
2. Use the **Up/Down arrow keys** to navigate through results.
3. Press **Enter** to execute the selected command.
4. Press **Escape** to close the palette without taking action.

### Available Commands

**Page Navigation:**

| Command             | Action                        |
|---------------------|-------------------------------|
| Dashboard           | Navigate to the Dashboard     |
| AI Chat             | Navigate to AI Chat           |
| Ask Files           | Navigate to Ask Your Files    |
| Quick Actions       | Navigate to Quick Actions     |
| Knowledge Vault     | Navigate to Knowledge Vault   |
| Collections         | Navigate to Collections       |
| Search              | Navigate to Semantic Search   |
| Model Manager       | Navigate to Model Manager     |
| Hardware Advisor    | Navigate to Hardware Advisor  |
| Settings            | Navigate to Settings          |

**Actions:**

| Command             | Action                                    |
|---------------------|-------------------------------------------|
| New Conversation    | Opens AI Chat with a new conversation.    |
| Import Files        | Navigates to Knowledge Vault for import.  |
| Refresh Dashboard   | Reloads all Dashboard data.               |
| Toggle Theme        | Opens Settings (theme toggle support).    |

### Footer Hints

The bottom of the palette displays keyboard hints as a quick reference:

- Up/Down arrows -- Navigate results
- Enter -- Open / execute
- Esc -- Close

---

## 16. Keyboard Shortcuts

Agent-X provides keyboard shortcuts for fast navigation and common actions. All shortcuts work from any page in the application.

| Shortcut          | Action                                     |
|-------------------|--------------------------------------------|
| `Ctrl+K`          | Open / toggle the Command Palette          |
| `Ctrl+N`          | Navigate to AI Chat (new conversation)     |
| `Ctrl+I`          | Navigate to Knowledge Vault (import files) |
| `Ctrl+F`          | Navigate to Semantic Search                |
| `Ctrl+Shift+F`    | Navigate to Semantic Search (alternate)    |
| `Ctrl+,`          | Navigate to Settings                       |
| `Escape`          | Close the Command Palette (when open)      |

---

## 17. Status Bar

The status bar runs along the bottom of the Agent-X window and provides persistent system-level information. It updates automatically every 30 seconds.

### Status Bar Elements

| Element             | Description                                                   |
|---------------------|---------------------------------------------------------------|
| Connection Indicator | A colored dot showing the Ollama connection status. **Green** means connected; **Red** means not detected. |
| Active Model Name   | Displays the name of the currently active AI model (e.g., "Connected -- llama3.2"). If disconnected, shows "Ollama not detected." |
| Indexing Progress   | When documents are being indexed, shows a progress ring and the count of documents remaining in the queue (e.g., "Indexing (3 remaining)"). Hidden when no indexing is in progress. |
| Document Count      | Displays the total number of documents in the Knowledge Vault (e.g., "42 docs"). Hidden when the vault is empty. |
| App Version         | Shown in the Settings page footer as "Agent-X v1.0.0".       |

---

## 18. Troubleshooting

### "Ollama not detected"

**Symptom:** The Dashboard shows "Ollama not detected" with a red indicator, and AI features are unavailable.

**Solutions:**

1. **Ensure Ollama is installed.** Download it from [https://ollama.com/download](https://ollama.com/download).
2. **Ensure Ollama is running.** Open a terminal and run `ollama serve` or check that the Ollama service is running in the background. You can verify by running `ollama list`.
3. **Check the endpoint.** Go to **Settings** and verify the Ollama Endpoint is set correctly (default: `http://localhost:11434`). If you run Ollama on a different port or machine, update the endpoint accordingly.
4. **Check firewall rules.** Ensure your firewall is not blocking connections to the Ollama port.

### Slow Application Startup

**Symptom:** Agent-X takes several seconds to start up.

**Explanation:** On startup, Agent-X attempts to connect to Ollama with a 3-second timeout. If Ollama is not running, the connection check must time out before the UI becomes responsive. This is expected behavior.

**Solution:** Start Ollama before launching Agent-X for instant startup. Alternatively, the application will become responsive after the timeout completes.

### Documents Stuck on "Pending" Status

**Symptom:** Imported documents remain in "Pending" indexing status and never progress to "Indexed."

**Solutions:**

1. **Ensure Ollama is running** with an embedding model available.
2. **Verify the embedding model.** Go to **Settings** and check that the configured Embedding Model (default: `all-minilm`) is installed. Run `ollama list` in a terminal to verify. If not installed, run `ollama pull all-minilm`.
3. **Check document count limits.** If you are using a trial license, you may have reached the document limit for indexing.
4. **Re-index manually.** In the Knowledge Vault, click the **Re-index** button on a stuck document.

### No Search Results

**Symptom:** Semantic Search returns no results even though you have imported documents.

**Solutions:**

1. **Ensure documents are indexed.** Only documents with an "Indexed" status appear in search results. Check the Knowledge Vault for indexing status.
2. **Verify the embedding model is consistent.** If you changed the embedding model after indexing, previously indexed documents will not match new queries. Re-index your documents with the new model.
3. **Broaden your query.** Semantic search matches on meaning, not keywords. Try rephrasing your query in more general terms.

### Model Download Fails

**Symptom:** Pulling a model from the Model Manager fails or stalls.

**Solutions:**

1. **Check your internet connection.** Model downloads require internet access to fetch weights from the Ollama library.
2. **Check disk space.** Large models (7B+) require several gigabytes of disk space. Ensure your drive has sufficient free space.
3. **Try from the command line.** Run `ollama pull <model-name>` in a terminal for more detailed error output.

### High Memory Usage

**Symptom:** Agent-X or Ollama consumes a large amount of RAM.

**Explanation:** Running local AI models requires significant memory. A 7B model typically uses 4--5 GB of RAM (or VRAM if GPU-accelerated).

**Solutions:**

1. **Use a smaller model.** Switch to a model with fewer parameters (e.g., `phi3:mini` at 2.3 GB instead of a 7B model).
2. **Close other applications.** Free up RAM for AI inference.
3. **Use the Hardware Advisor.** Navigate to the Hardware Advisor page for model recommendations optimized for your available memory.

### OCR Not Working on Images

**Symptom:** Imported images show no extracted text or fail to index.

**Solution:** Ensure the image contains legible text. Very low-resolution images, heavily stylized fonts, or handwritten content may not be recognized reliably by the OCR engine.

---

## 19. FAQ

**Q: Does Agent-X send my data to the cloud?**

A: No. Agent-X is entirely local-first. All AI inference runs on your machine via Ollama. Your documents, conversations, embeddings, and settings never leave your computer.

**Q: Can I use Agent-X without a GPU?**

A: Yes. Ollama supports CPU-only inference. Performance will be slower compared to GPU-accelerated inference, but all features work correctly. The Hardware Advisor will recommend appropriate smaller models for CPU-only systems.

**Q: What is the difference between the Chat Model and the Embedding Model?**

A: The **Chat Model** (e.g., `llama3.2`, `mistral`) is a large language model used for generating text responses in AI Chat, Ask Your Files, and Quick Actions. The **Embedding Model** (e.g., `all-minilm`, `nomic-embed-text`) is a smaller, specialized model that converts text into vector representations used for Semantic Search and RAG retrieval.

**Q: How much disk space do AI models require?**

A: It varies by model. Small embedding models like `all-minilm` are around 100 MB. A 7B chat model is typically 4--5 GB. Large models like `llama3.1:70b` can be 40 GB or more. You can see exact sizes in the Model Manager.

**Q: Can I run Ollama on a different computer on my network?**

A: Yes. Change the **Ollama Endpoint** in Settings to point to the remote machine (e.g., `http://192.168.1.100:11434`). Ensure the remote Ollama instance is configured to accept external connections.

**Q: What happens if I delete a document from the Knowledge Vault?**

A: The document, all its text chunks, and all associated vector embeddings are permanently removed from Agent-X. The original source file on disk is not deleted.

**Q: Can I use Agent-X without a license key?**

A: Yes. Agent-X works in trial mode without a license key. Trial mode has a document limit, shown in Settings. Enter a license key to unlock higher limits.

**Q: How do I change the default model?**

A: Go to **Settings** and update the **Default Chat Model** field, then click **Save Settings**. The new model will be used for all new conversations.

**Q: What is chunk size and why does it matter?**

A: When documents are indexed, their text is split into smaller pieces called "chunks." The chunk size (default: 512 tokens) controls how large each piece is. Smaller chunks provide finer-grained search results but may lose context. Larger chunks preserve more context but may dilute search precision. The default of 512 tokens is a good balance for most use cases.

**Q: Where is my data stored?**

A: All data is stored locally at `%LocalAppData%\AgentX\` by default. This includes the SQLite database, vector store, and settings. You can change the storage path in Settings.

---

## 20. Appendix: Supported File Types

The table below lists every file extension supported by Agent-X, its category, and how it is processed.

### PDF and Documents

| Extension   | Category   | Display Name              | Processing Method            |
|-------------|------------|---------------------------|------------------------------|
| `.pdf`      | PDF        | PDF Document              | Text extraction with page awareness |
| `.docx`     | Document   | Word Document             | XML-based text extraction    |
| `.doc`      | Document   | Word Document (Legacy)    | Binary format extraction     |

### Text Files

| Extension   | Category   | Display Name              | Processing Method            |
|-------------|------------|---------------------------|------------------------------|
| `.txt`      | Text       | Text File                 | Plain text reading           |
| `.csv`      | Text       | CSV Spreadsheet           | Plain text reading           |
| `.log`      | Text       | Log File                  | Plain text reading           |
| `.xml`      | Text       | XML Document              | Plain text reading           |
| `.json`     | Text       | JSON File                 | Plain text reading           |

### Markdown

| Extension   | Category   | Display Name              | Processing Method            |
|-------------|------------|---------------------------|------------------------------|
| `.md`       | Markdown   | Markdown Document         | Markdown-aware text extraction |
| `.markdown` | Markdown   | Markdown Document         | Markdown-aware text extraction |

### Images (OCR)

| Extension   | Category   | Display Name              | Processing Method            |
|-------------|------------|---------------------------|------------------------------|
| `.png`      | Image      | PNG Image                 | Optical Character Recognition (OCR) |
| `.jpg`      | Image      | JPEG Image                | Optical Character Recognition (OCR) |
| `.jpeg`     | Image      | JPEG Image                | Optical Character Recognition (OCR) |
| `.bmp`      | Image      | Bitmap Image              | Optical Character Recognition (OCR) |
| `.tiff`     | Image      | TIFF Image                | Optical Character Recognition (OCR) |

### Code Files

| Extension   | Category   | Display Name              | Processing Method            |
|-------------|------------|---------------------------|------------------------------|
| `.cs`       | Code       | C# Source File            | Code-aware text extraction   |
| `.js`       | Code       | JavaScript File           | Code-aware text extraction   |
| `.ts`       | Code       | TypeScript File           | Code-aware text extraction   |
| `.py`       | Code       | Python Script             | Code-aware text extraction   |
| `.java`     | Code       | Java Source File          | Code-aware text extraction   |
| `.cpp`      | Code       | C++ Source File           | Code-aware text extraction   |
| `.c`        | Code       | C Source File             | Code-aware text extraction   |
| `.h`        | Code       | C/C++ Header File         | Code-aware text extraction   |
| `.go`       | Code       | Go Source File            | Code-aware text extraction   |
| `.rs`       | Code       | Rust Source File          | Code-aware text extraction   |
| `.swift`    | Code       | Swift Source File         | Code-aware text extraction   |
| `.kt`       | Code       | Kotlin Source File        | Code-aware text extraction   |
| `.rb`       | Code       | Ruby Script               | Code-aware text extraction   |
| `.php`      | Code       | PHP Source File           | Code-aware text extraction   |
| `.html`     | Code       | HTML Document             | Code-aware text extraction   |
| `.css`      | Code       | CSS Stylesheet            | Code-aware text extraction   |
| `.scss`     | Code       | SCSS Stylesheet           | Code-aware text extraction   |
| `.sql`      | Code       | SQL Script                | Code-aware text extraction   |
| `.sh`       | Code       | Shell Script              | Code-aware text extraction   |
| `.yaml`     | Code       | YAML Configuration        | Code-aware text extraction   |
| `.yml`      | Code       | YAML Configuration        | Code-aware text extraction   |
| `.toml`     | Code       | TOML Configuration        | Code-aware text extraction   |
| `.ini`      | Code       | INI Configuration         | Code-aware text extraction   |
| `.cfg`      | Code       | Configuration File        | Code-aware text extraction   |
| `.xaml`     | Code       | XAML Markup               | Code-aware text extraction   |

---

## 18. Workspace Profiles *(v1.3.0)*

Workspace Profiles let you create isolated environments for different projects or contexts. Each workspace has its own conversations, knowledge base, and AI settings.

### Creating a Workspace

1. Navigate to **Workspaces** in the sidebar
2. Click **New Workspace**
3. Enter a name, choose an icon and color
4. Click **Create**

### Switching Workspaces

- Click any workspace in the sidebar to switch
- The Default workspace cannot be renamed or deleted
- Switching workspaces changes your active conversations and settings

### Workspace Storage

Each workspace stores its data in a separate directory under `%LocalAppData%\AgentX\Workspaces\{id}\`. This ensures complete isolation between workspaces.

---

## 19. Smart Inbox *(v1.3.0)*

The Smart Inbox automatically detects new files added to your watch folders and presents them for review before importing into your knowledge base.

### Inbox Workflow

1. **New files appear** in the Inbox when detected in watch folders
2. **AI-powered previews** are generated for each item
3. **Accept** to import into a collection, **Reject** to dismiss, or **Defer** for later
4. **Batch operations** — Accept or Reject multiple items at once

### Accepting Items

When you accept an item, you can optionally assign it to a specific collection. If no collection is selected, it goes to the default collection.

### Auto-Preview

The Inbox can generate AI previews for all pending items. Click **Generate All Previews** to batch-process your queue.

---

## 20. Comparative Analysis *(v1.3.0)*

Comparative Analysis lets you select multiple documents and have AI analyze the similarities, differences, and patterns across them.

### Running a Comparison

1. Navigate to **Comparison** in the sidebar
2. Select 2 or more documents from your knowledge base
3. Choose analysis dimensions (themes, arguments, methodology, sentiment)
4. Click **Compare**

### Comparison Options

| Option | Description |
|--------|-------------|
| **Analysis Depth** | Quick summary vs. detailed deep-dive |
| **Focus Areas** | Themes, Arguments, Methodology, Sentiment |
| **Output Format** | Structured report with citations |

### Exporting Results

Use **Export as Markdown** to save the full comparison report for sharing or archival.

---

## 21. Voice Input *(v1.3.0)*

Voice Input lets you transcribe speech to text using local Whisper models. All processing happens on your machine — no audio data leaves your device.

### Using Voice Input

- **Click the microphone button** in the chat input bar to start recording
- **Click again** (or the stop button) to stop recording and transcribe
- **Right-click the microphone button** to select an audio file for transcription

### Supported Audio Formats

`.mp3`, `.wav`, `.m4a`, `.flac`, `.ogg`, `.webm`

### Setting Up Voice Input

1. Go to **Settings > Voice**
2. Download a Whisper model (recommended: `base` for most users)
3. Choose your language or leave on auto-detect

### How It Works

Voice recording uses your microphone via NAudio (WaveIn 16kHz mono) and transcribes the audio locally using Whisper.net. The transcribed text is inserted into the chat input box for review before sending.

### Model Sizes

| Model | Size | Speed | Accuracy |
|-------|------|-------|----------|
| tiny | ~75 MB | Fastest | Good for quick notes |
| base | ~142 MB | Fast | Recommended for most users |
| small | ~466 MB | Moderate | Higher accuracy |
| medium | ~1.5 GB | Slow | High accuracy |
| large | ~3 GB | Slowest | Best accuracy |

---

## 22. Plugin Manager *(v1.3.0)*

The Plugin Manager lets you install, enable, disable, and uninstall third-party plugins that extend Agent-X's capabilities.

### Installing a Plugin

1. Navigate to **Plugin Manager** in the sidebar
2. Click **Install Plugin**
3. Select a `.agentx-plugin` package file
4. The plugin is installed in disabled state
5. Click **Enable** to activate it

### Plugin Types

| Type | Description |
|------|-------------|
| **Document Processor** | Adds support for new file formats |
| **AI Provider** | Integrates additional AI backends |
| **Quick Action** | Adds single-click commands |
| **Workflow Step** | Extends the workflow builder |
| **Theme** | Applies custom visual themes |
| **Custom** | Catch-all for other extensions |

### Creating a Plugin

See the [Plugin Development Guide](PLUGIN-DEVELOPMENT-GUIDE.md) for detailed instructions on creating your own plugins.

### Plugin Safety

- Plugins run in isolated `AssemblyLoadContext` instances
- Each plugin has a sandboxed data directory
- Plugins declare required permissions in their manifest
- Disable or uninstall any plugin from the Plugin Manager

---

## 23. Sync Settings *(v1.3.0)*

Sync Settings allow you to configure data synchronization between Agent-X instances or backup destinations.

### Setting Up Sync

1. Navigate to **Sync Settings** in the sidebar
2. Configure your sync destination (local path or network share)
3. Choose what to sync: conversations, knowledge base, settings
4. Set sync interval (manual, 5 minutes, 15 minutes, 30 minutes, 1 hour)

### Export and Import

- **Export**: Creates an encrypted sync package of your selected data
- **Import**: Restores data from a sync package, with conflict detection

### Conflict Resolution

When importing changes that conflict with local data, choose:
- **Keep Local** — Discard the incoming change
- **Keep Remote** — Accept the incoming change
- **Merge** — Combine both versions

### Auto-Sync

Enable automatic sync to run at your chosen interval. Changes are encrypted with AES-256-GCM before transmission.

---

## Default Settings Reference

For quick reference, the table below summarizes all configurable settings and their default values.

| Setting                  | Default Value              | Location             |
|--------------------------|----------------------------|----------------------|
| Ollama Endpoint          | `http://localhost:11434`   | Settings > AI Provider |
| Default Chat Model       | `llama3.2`                 | Settings > AI Provider |
| Embedding Model          | `all-minilm`              | Settings > AI Provider |
| Temperature              | `0.7`                      | Settings > Inference |
| Max Tokens               | `4096`                     | Settings > Inference |
| Context Window           | `8192`                     | Settings > Inference |
| Chunk Size               | `512`                      | Settings > Knowledge Vault |
| Chunk Overlap            | `50`                       | Settings > Knowledge Vault |
| Top-K Results            | `5`                        | Settings > Knowledge Vault |
| Auto-Index Watch Folders | `On`                       | Settings > Knowledge Vault |
| Storage Path             | `%LocalAppData%\AgentX`    | Internal             |

---

## Database Encryption

Agent-X can encrypt your local vault with SQLCipher (AES-256). Open Settings → Database Encryption → toggle the switch on.

**Starter and Professional tiers** use a transparent key that is automatically generated and tied to your Windows user account. You never see it. The database is unlocked automatically on every launch for as long as you are signed in as the same Windows user.

**Ultimate tier** prompts you to set a passphrase of at least 12 characters. The passphrase is used to derive the encryption key on each launch — Agent-X never stores your passphrase itself. **Write your passphrase down and keep it safe.** If you lose it, your database cannot be recovered. Create an unencrypted backup first via Settings → Backup & Restore if you are not sure.

Once encryption is enabled, every file Agent-X writes to its vault — documents, conversations, collections, memories, settings — is protected on disk. Encrypted backups preserve the same key: restoring an encrypted backup on a different machine requires the same passphrase (Ultimate) or the same Windows user profile (Starter / Professional).

**Disabling encryption is not supported in v2.1.** To revert to a plaintext vault, restore from a pre-encryption backup.

**Troubleshooting**

- *Forgot your passphrase?* The key cannot be recovered. Restore the most recent unencrypted backup.
- *Moved to a new machine (Starter / Professional tier)?* The DPAPI-wrapped key is bound to the original Windows user. Create an export via Settings → Backup & Restore on the old machine, import it on the new one.
- *App says "file is not a database" on launch?* The DB file and the encryption marker file have gotten out of sync. Close the app, rename `%LocalAppData%\AgentX\encryption.info.json` to `encryption.info.json.bak`, and relaunch — Agent-X will start in plaintext mode. If the DB is actually encrypted, it will fail to open; restore from your most recent backup. If the DB is plaintext and the marker was stale, the app will resume normally.

---

*Agent-X is developed by Rocky Stack / Strategia. For support, feature requests, or bug reports, please contact the development team.*
