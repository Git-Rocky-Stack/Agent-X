# Agent-X User Guide

**Agent-X -- Local-First AI Personal Intelligence Hub for Windows**

Version 2.1.2 | Last updated: June 21, 2026

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
20. [Analytics](#20-analytics)
21. [Model Manager](#21-model-manager)
22. [Hardware Advisor](#22-hardware-advisor)
23. [Backup and Restore](#23-backup-and-restore)
24. [Collaborative Sync](#24-collaborative-sync)
25. [Calendar and Email Connectors](#25-calendar-and-email-connectors)
26. [Annotations](#26-annotations)
27. [Settings](#27-settings)
28. [Command Palette, Jump To, and Shortcuts](#28-command-palette-jump-to-and-shortcuts)
29. [Instrument Strip, Notifications, and Tray](#29-instrument-strip-notifications-and-tray)
30. [Privacy and Security](#30-privacy-and-security)
31. [Troubleshooting](#31-troubleshooting)
32. [FAQ](#32-faq)
33. [Supported File Types](#33-supported-file-types)

---

## 1. Product Promise and Data Boundaries

Agent-X turns a Windows machine into a local AI intelligence hub. It imports documents, indexes them, searches by meaning, chats with local or optional cloud models, runs repeatable AI workflows, and helps triage new information without making a cloud account the center of the product.

### What stays local by default

- Documents, text chunks, embeddings, conversations, memories, workflow runs, sync logs, annotations, and settings are stored under `%LocalAppData%\AgentX\`.
- Ollama-backed chat, embedding, RAG, summaries, document analysis, and workflow runs execute on the user's machine.
- Search indexes live in SQLite and the local vector store. The app does not need an internet connection for already-installed local models.

### Optional external connections

Agent-X can connect to services the user explicitly configures:

- **OpenAI and Anthropic** for cloud model access.
- **Google and Microsoft** for calendar/email connectors.
- **Web import and web search providers** when the user imports URLs or enables provider-backed search.
- **Collaborative sync folders** on local, network, or shared drives.

When a cloud provider is active, prompts and selected context are sent to that provider. Keep sensitive documents on local Ollama models when data residency matters.

---

## 2. System Requirements

### Minimum

| Component | Requirement |
| --- | --- |
| OS | Windows 10 build 19041+ or Windows 11 |
| Architecture | x64 |
| Runtime | Self-contained installer bundles app runtime dependencies |
| RAM | 8 GB minimum |
| Storage | 500 MB for the app, plus space for models, documents, indexes, backups, and sync packages |
| AI runtime | Ollama for local model features |

### Recommended

| Component | Recommendation |
| --- | --- |
| RAM | 16 GB+ for 7B models; 32 GB+ for larger models |
| GPU | NVIDIA, AMD, or Intel GPU where supported by the selected local runtime |
| Storage | SSD with at least 50 GB free for practical model and vault growth |
| Models | One chat model and one embedding model installed before heavy document work |

Agent-X works on CPU-only systems. CPU inference is slower, so the Hardware Advisor helps choose smaller models and more conservative settings.

---

## 3. Installation and Model Setup

### Install Agent-X

1. Run the Agent-X installer.
2. Launch Agent-X from the Start Menu or desktop shortcut.
3. Complete first-run onboarding.

The installer creates the local data directories under `%LocalAppData%\AgentX\` and preserves user data during uninstall.

### Install Ollama

1. Download Ollama for Windows from `https://ollama.com/download`.
2. Install and start Ollama.
3. Verify it is available:

```powershell
ollama list
```

### Pull practical starter models

Use at least one chat model and one embedding model:

```powershell
ollama pull llama3.2
ollama pull all-minilm
```

Other strong embedding options include `nomic-embed-text`, `mxbai-embed-large`, and local models recommended by the Hardware Advisor.

---

## 4. First Run Onboarding

On first launch, Agent-X hides the navigation pane and presents a focused five-step wizard that transforms your Windows machine into an enterprise-grade AI intelligence hub. This isn't just setup — it's your gateway to **224+ unit-tested features**, **55+ services**, and **26 navigation pages** that deliver Fortune 10 capabilities in a local-first package.

### Step 0: Welcome

The welcome step introduces you to Agent-X's core promise: **uncompromising intelligence without cloud dependency**. You'll discover:

- **Local-First Architecture**: Your entire knowledge vault — documents, embeddings, conversations, memories, and workflows — lives under `%LocalAppData%\AgentX\`. Zero telemetry requirements. No subscription checks.
- **Bundled AI Model**: Agent-X ships with **Llama 3.2 3B Instruct** (~2 GB) pre-packaged in the installer. You get fully functional offline AI out of the box — zero downloads required.
- **Hybrid Intelligence Stack**: Choose between bundled local inference, Ollama integration for custom models, or optional cloud providers (OpenAI GPT-family, Anthropic Claude). Switch anytime without data migration.
- **Enterprise Data Layer**: SQLCipher AES-256-CBC at-rest encryption (optional), EF Core migrations with baseline adoption, and a **40-table relational schema** powering everything from vector search to conversation memory.

### Step 1: Connect to Ollama (Optional)

The wizard defaults to the Ollama standard endpoint:

```text
http://localhost:11434
```

**Why Ollama?** While Agent-X includes a bundled model for immediate use, Ollama unlocks:

- **7B+ Parameter Models**: Llama 3.1/3.2, Phi 4, Mistral, Gemma, and dozens more
- **GPU Acceleration**: CUDA 12, ROCm (AMD), and CPU fallback with automatic hardware detection
- **Model Ecosystem**: Pull models on-demand from Ollama's 100+ model library

**Hardware Detection**: Agent-X reads your GPU VRAM, system RAM, and CPU specifications to recommend realistic model tiers:
- **8 GB RAM**: 3B models (bundled Llama 3.2) → ~2-4 tokens/sec on CPU
- **16 GB RAM**: 7B models (Llama 3.1 8B, Phi 4) → ~8-15 tokens/sec with GPU
- **32 GB+ RAM**: 13B+ models with full context windows → ~20-40 tokens/sec with GPU

Use **Test Connection** to verify Ollama is reachable. If Ollama isn't installed yet, continue anyway — the bundled model has you covered.

### Step 2: Select Models

Agent-X auto-detects installed Ollama models and presents the bundled model status. Understanding model roles is critical:

| Model Role | Purpose | Example Models | Used By |
| --- | --- | --- | --- |
| **Chat Model** | Text generation, reasoning, dialogue | Llama 3.2 3B (bundled), Llama 3.1 8B, Phi 4, Mistral 7B, GPT-4o, Claude Sonnet | AI Chat, Ask Your Files, Quick Actions, Workflows, Summaries, Comparisons, Digest Reports |
| **Embedding Model** | Vector generation for semantic search | all-minilm, nomic-embed-text, mxbai-embed-large | Knowledge Vault indexing, Semantic Search, RAG retrieval, Duplicate detection, Knowledge Graph |

**Advanced RAG Pipeline Configuration**: Your embedding model choice directly impacts retrieval quality:

- **all-minilm** (330 MB): Fastest, good for English-only vaults under 10K documents
- **nomic-embed-text** (275 MB): Strong multilingual support, recommended default
- **mxbai-embed-large** (668 MB): Highest retrieval accuracy for technical/legal content

**GPU Acceleration**: When Agent-X detects an NVIDIA GPU, it enables **CUDA 12 layer offloading** automatically:
- **2-4 GB VRAM**: 20-30% layers → 2-3x speedup
- **8 GB VRAM**: All layers → full GPU inference (30-50 tokens/sec on 7B models)
- **12+ GB VRAM**: Supports 13B+ models with full context windows

### Step 3: Built-in Model Status and Cloud Providers

This step delivers a comprehensive AI readiness report:

#### Built-in Local Model Status

| Indicator | Meaning |
| --- | --- |
| ✓ Model Installed | Llama 3.2 3B detected at `%LocalAppData%\AgentX\Models\` |
| ✓ GPU Ready | CUDA-capable GPU detected with X GB VRAM |
| ⚠ CPU Fallback | No GPU detected — inference will be ~5-10x slower |

**The bundled model delivers**:
- ~3 tokens/sec on modern CPUs (sufficient for exploration)
- ~15-25 tokens/sec on mid-range GPUs (NVIDIA RTX 3060+)
- Fully offline operation — no internet required after initial install
- 128K context window support (model-dependent)

#### Optional Cloud Providers

Add API keys when you need cloud-class capabilities:

| Provider | Use Cases | Models | Pricing |
| --- | --- | --- | --- |
| **OpenAI** | GPT-4o for complex reasoning, o1 for multi-step analysis | GPT-4o, GPT-4o-mini, o1-preview, o1-mini | Pay-per-token (billed by OpenAI) |
| **Anthropic** | Claude Sonnet/Opus for nuanced analysis, long-context work | Claude 3.5 Sonnet, Claude 3 Opus, Haiku | Pay-per-token (billed by Anthropic) |

**Security Note**: API keys are stored locally in `%LocalAppData%\AgentX\settings.json` and transmitted **only** to their respective providers. Agent-X does not proxy, log, or transmit prompts anywhere else.

### Step 4: Summary and Launch

The summary screen provides a complete readiness report:

```
✓ Local AI: Llama 3.2 3B (bundled) — READY
✓ GPU Acceleration: CUDA 12 detected — ENABLED
✓ Ollama Connection: http://localhost:11434 — CONNECTED
  • 3 models installed (llama3.2, phi4, nomic-embed-text)
☐ Cloud Providers: Not configured (optional)
✓ Storage Path: C:\Users\<User>\AppData\Local\AgentX\
✓ Database Encryption: Disabled (enable in Settings)
```

**What happens on Launch?**

1. **Database Initialization**: EF Core migrations run, creating 16 tables (conversations, messages, documents, chunks, embeddings, collections, tags, memories, workflows, and more)
2. **Vector Store Setup**: HNSW ANN index initializes for semantic search (or linear-scan fallback for small vaults)
3. **Indexing Queue**: Background service starts for async document processing
4. **File System Watcher**: Enables auto-import from configured watch folders
5. **Dashboard Loads**: Your operational command center surfaces recent activity, recommended actions, and system health

### Re-running Onboarding

- **Dashboard → Setup AI**: Revisit model/provider configuration anytime
- **Force Onboarding**: Delete `%LocalAppData%\AgentX\settings.json` while app is closed
- **Skip Onboarding**: Set `"onboardingCompleted": true` in settings.json (developer workflow)

### Post-Onboarding: What to Do First

| Priority | Action | Why It Matters |
| --- | --- | --- |
| **1** | Import 5-10 representative documents | Establish your knowledge baseline and test embedding quality |
| **2** | Run your first **Ask Your Files** query | Validate RAG retrieval and citation quality |
| **3** | Create a **Collection** for a project | Enable scoped search, RAG, and sync workflows |
| **4** | Try a **Workflow Template** | Experience multi-step AI automation (action item extraction, research briefing, etc.) |
| **5** | Explore **Knowledge Graph** | Visualize document relationships and discover content clusters |
| **6** | Review **Analytics** | Understand your usage patterns and intelligence coverage |
| **7** | Enable **Database Encryption** (optional) | Add AES-256-CBC at-rest protection for your vault (free for everyone) |

---

## 4.2 Agent-X Capability Matrix

After onboarding, you have access to an enterprise-grade intelligence platform. Here's what ships in v2.1.2:

### Intelligence Engine (Core)

| Capability | Description | Technical Foundation |
| --- | --- | --- |
| **Hybrid Search** | Semantic (vector) + Keyword (FTS5) merged via Reciprocal Rank Fusion (k=60) | HNSW ANN index or linear cosine scan; SQLite FTS5 virtual tables |
| **Advanced RAG** | Multi-query retrieval, HyDE embeddings, LLM reranking, parent document expansion, contextual compression | 6-stage pipeline with citation chaining |
| **Conversation Memory** | Durable extraction of facts, preferences, instructions, topics with importance-weighted injection | EF Core `MemoryEntity` with recency decay |
| **Knowledge Graph** | Force-directed visualization (100-iteration spring-electric layout) of documents, collections, tags | WinUI 3 Canvas-rendered with zoom/pan/hover |
| **Auto-Tagging** | AI-powered tag generation with confidence scores on every import | `AutoTagService` with `TagEntity` junction table |

### Data Layer (v2.1 "Bedrock")

| Feature | Specification |
| --- | --- |
| **Database Engine** | SQLite 3.x with EF Core 8.0.11 ORM |
| **Encryption** | SQLCipher AES-256-CBC (opt-in) with DPAPI or PBKDF2-HMAC-SHA256 (600k iterations) |
| **Migrations** | EF Core migration runner with baseline adoption for pre-existing installs |
| **Tables** | entity types (conversations, messages, documents, chunks, embeddings, collections, tags, memories, workflows, and more) |
| **Vector Store** | BLOB-based float arrays with pre-computed L2 magnitude; HNSW indexing for large vaults |

### Productivity Accelerators

| Feature | What It Does |
| --- | --- |
| **Workflows** | 15+ template-driven multi-step prompt chains (action items, research briefings, critiques, repurposing) with run history and token tracking |
| **Quick Actions** | One-click AI tasks: summarize, extract keypoints, translate, rewrite/explain, duplicate review, organization suggestions, Q&A generation |
| **Compare Documents** | Multi-document synthesis revealing similarities, differences, contradictions, unique points, and metrics |
| **Batch Operations** | Multi-select documents for bulk delete, re-index, collection assignment, tag operations |
| **Web Import** | URL-to-vault ingestion with preview, collection assignment, and auto-indexing |
| **Smart Inbox** | Triage queue for watch-folder, connector, and browser-clipped content with AI previews |

### Advanced Features

| Feature | Description |
| --- | --- |
| **Analytics Dashboard** | 30-day activity charts, model usage breakdown, file-type distribution, performance metrics (avg/median/P95 latency, throughput), conversation intelligence (summary freshness, recall results) |
| **Collaborative Sync** | Encrypted package exchange via local/network/cloud folders with conflict resolution and auto-scheduling |
| **Calendar/Email Connectors** | OAuth2-based Google/Microsoft integration for event-driven inbox flows and message ingestion |
| **REST API** | Embedded HTTP listener (port 9846) with `/api/documents`, `/api/conversations`, `/api/search`, and more |
| **Plugin Ecosystem** | Extensible plugin API for ingestion, providers, workflows, and UI extensions with markdown documentation rendering |
| **Database Encryption** | SQLCipher with automatic DPAPI-wrapped key management tied to your Windows account |

### Developer Quality Bar

| Metric | Status |
| --- | --- |
| **Unit Tests** | Comprehensive test suite across Settings, Collections, Export, Search Cache, and all validators |
| **Code Coverage** | Critical paths in AI, search, indexing, and data layers fully tested |
| **Validation Layer** | `IValidator<T>` with typed validators for AppSettings, SyncConfiguration, PluginManifest |
| **Error Handling** | 7 typed exception classes with structured error propagation |
| **Logging** | Serilog with 7-day rolling retention at `%LocalAppData%\AgentX\Logs\` |
| **Feature Flags** | 15 feature gates for experimental capabilities and phased rollouts |

### UX Polish

| Feature | Details |
| --- | --- |
| **Per-Message Actions** | Copy, delete, regenerate, thumbs up/down feedback on every chat bubble |
| **Message Editing** | Inline edit with "Save & Resend" that truncates subsequent messages and re-sends |
| **Code Syntax Highlighting** | 18 languages (C#, Python, JS/TS, SQL, JSON, HTML/XML, Rust, Go, Java, Bash, YAML, CSS, C/C++) with One Dark Pro color palette |
| **Notification System** | Toast overlay with severity icons, auto-dismiss, and max-5 visible notifications |
| **Keyboard Shortcuts** | 18+ global shortcuts with command palette (`Ctrl+K`), jump-to (`Ctrl+P`), and cheatsheet (`F1`) |
| **Theme Toggle** | Dark/Light/System Default with instant switching via theme service |
| **Conversation Folders** | Organize conversations into Work, Research, Personal, Archive, or custom folders |

### Localization

| Locale | Status |
| --- | --- |
| English (en-US) | ✓ Full translation |
| German (de) | ✓ Full translation |
| French (fr) | ✓ Full translation |
| Japanese (ja) | ✓ Full translation |
| Chinese Simplified (zh-CN) | ✓ Full translation |

---

## 5. Navigation Map

Agent-X is organized into four primary work areas plus support pages.

| Area | Pages |
| --- | --- |
| Intelligence | Dashboard, Operations, Weekly Digest, Analytics, AI Chat, Ask Your Files, Quick Actions, Workflows |
| Knowledge | Knowledge Vault, Web Import, Collections, Semantic Search, Knowledge Graph, Compare Documents |
| Triage | Smart Inbox |
| System | Model Manager, Hardware Advisor, Backup and Restore, Workspace Profiles, Plugin Manager, Collaborative Sync, Calendar, Email, Annotations, Settings |
| Support | User Guide, Privacy Policy, Terms of Service |

The command palette and Jump To dialog expose many of the same destinations without using the mouse.

---

## 6. Dashboard

The Dashboard is the first operational surface after onboarding. It summarizes system health, recent work, and recommended next actions.

### What to check first

- **AI connection status:** Confirms whether the selected provider is reachable.
- **Model status:** Shows active chat and embedding model readiness.
- **Document and storage metrics:** Tracks vault growth and indexed material.
- **Recent documents:** Opens recently imported material without returning to the vault.
- **Recent conversations:** Resumes prior AI sessions.
- **Recommended actions:** Prioritizes setup, remediation, and useful next steps such as configuring AI, importing documents, reviewing sync, or running workflows.

### Common dashboard flows

| Goal | Action |
| --- | --- |
| Start a clean conversation | Use **New Chat** |
| Add source material | Use **Import Documents** |
| Search the vault | Use **Search** |
| Ask grounded questions | Use **Ask Files** |
| Repair setup | Use **Setup AI** or follow the recommended action |

Use **Refresh** when another page has changed documents, models, conversations, sync, or indexing state.

---

## 7. Operations

Operations is the mission-control page for ongoing system health. It brings together signals from conversation intelligence, sync posture, ingestion backlog, workflows, imported-document indexing, and connectors.

### Status areas

| Area | What it tells you |
| --- | --- |
| Conversation intelligence | Whether recent conversations have summary/recall context available |
| Sync health | Manual and automatic sync status, pending changes, and recent sync passes |
| Ingestion backlog | Smart Inbox queue and pending import work |
| Imported documents | Recent vault documents, indexing state, chunk count, and errors |
| Workflow activity | Recent workflow runs, failures, and runs that need review |
| Connectors | Calendar/email readiness and setup gaps |

### Recommended actions

Operations surfaces direct actions when a status area needs attention:

- Run a manual sync.
- Open sync configuration.
- Generate inbox previews.
- Re-index a document that failed or needs attention.
- Refresh conversation summaries.
- Open the relevant connector setup page.
- Drill into a workflow run that failed or is waiting for review.

### Drill-in behavior

Clicking a preview opens the destination page with enough context to resolve the issue. For example, a sync item opens Collaborative Sync with the relevant log focused; a workflow item opens Workflows with the selected historical run lifted to the top.

---

## 8. AI Chat and Quick Chat

AI Chat is the full conversational workspace. Quick Chat is the tray/shortcut-style lightweight entry point for fast questions.

### Core chat workflow

1. Select **AI Chat** or press `Ctrl+N`.
2. Pick a model when needed.
3. Type a prompt and send it.
4. Watch the response stream in real time.
5. Copy, regenerate, branch, export, or continue the conversation.

### Conversation management

- Conversations are stored locally.
- The sidebar supports history review, search, pinning, and deletion.
- Each conversation has its own message list and model context.
- Export saves selected conversations as portable text/Markdown artifacts.

### Prompt and context tools

| Feature | Use |
| --- | --- |
| System prompts | Shape the assistant's role for a conversation |
| Conversation memory | Reuses durable facts, preferences, instructions, and topics |
| Context story | Shows which prior context influenced a reply |
| Context inspection | Helps explain what Agent-X assembled before sending a prompt |
| Branching | Explore alternate responses without losing the original path |
| Suggested questions | Continue a thread with relevant follow-ups |
| Voice input | Dictate text into chat through local transcription |

### Message behavior

AI responses can render Markdown, lists, tables, and code blocks. Code blocks include copy actions when the message renderer recognizes them.

---

## 9. Ask Your Files

Ask Your Files is Agent-X's RAG workflow. It searches indexed document chunks, assembles source context, and asks the selected model to answer with citations.

### When to use it

- Ask questions across a collection or selected source set.
- Generate grounded answers from imported documents.
- Find the source passages behind a recommendation or summary.
- Keep the model constrained to your own material instead of general memory.

### How it works

1. Select a collection or source scope.
2. Ask a natural-language question.
3. Agent-X embeds the query and retrieves matching chunks.
4. Optional reranking improves source ordering.
5. The model generates an answer grounded in retrieved passages.
6. Citations connect answer claims back to documents and chunks.

### Reading citations

Use citations to verify source quality. If a citation looks weak, rephrase the question, narrow the collection, or re-index the relevant document with the desired embedding model.

---

## 10. Knowledge Vault

The Knowledge Vault is the document repository and indexing control center.

### Import methods

- File picker for selected files.
- Folder import for batches.
- Drag and drop from Windows Explorer.
- Web Import and Smart Inbox handoff.
- Workflow result save-to-vault.

### Document metadata

Each document tracks:

- File name, type, size, and path.
- Import timestamp.
- SHA-256 content hash for exact duplicate checks.
- Tags and collection membership.
- Chunk count.
- Indexing status and indexing error detail.

### Bulk operations

Multi-select documents to:

- Re-index multiple files.
- Delete multiple files from the vault.
- Add selected documents to a collection.
- Apply or remove tags where supported by the current page controls.

### Indexing lifecycle

| Status | Meaning |
| --- | --- |
| Pending | Imported but not embedded yet |
| Indexing | Background pipeline is extracting/chunking/embedding |
| Indexed | Search and RAG can retrieve chunks |
| Failed | The error column or Operations page should show the failure reason |

Re-index after changing embedding models, moving source files, or resolving an extraction failure.

---

## 11. Web Import

Web Import turns URLs into vault documents.

### Single-page import

1. Paste a URL.
2. Preview the page title, site, author, word count, and extracted text when available.
3. Choose a collection.
4. Import the content into the Knowledge Vault.

### Batch and discovery flows

Web Import also supports source discovery paths such as feeds and sitemaps where configured. Results show success/failure counts, imported document names, word counts, and error messages for failed URLs.

### Best practices

- Prefer canonical article URLs over homepages.
- Use collections to group imported sources by project.
- Re-index imported pages if the embedding model changes.
- Check failed rows for paywalls, unsupported dynamic pages, or blocked fetches.

---

## 12. Collections and Workspace Profiles

Collections organize documents inside a workspace. Workspace Profiles isolate broader working contexts.

### Collections

Use Collections for project, client, research area, or topic groupings.

| Action | Result |
| --- | --- |
| Create collection | Adds a new organizational container |
| Nest collection | Creates parent/child structure |
| Add documents | Associates documents without duplicating files |
| Remove documents | Removes only the collection relationship |
| Delete collection | Leaves original vault documents intact |

Collections improve RAG scope, search filtering, sync scope, and dashboard insights.

### Workspace Profiles

Workspace Profiles are separate environments for different contexts. The default workspace is seeded automatically and cannot be removed.

Use separate workspaces when:

- You need project-specific conversations and vault data.
- You want different model/provider settings for a client or domain.
- You need cleaner separation between personal, business, and testing data.

---

## 13. Semantic Search

Semantic Search finds meaning, not just exact words. It can search by vector similarity, keyword FTS5, or hybrid ranking.

### Search modes

| Mode | Best for |
| --- | --- |
| Semantic | Concepts phrased differently from the original text |
| Keyword | Exact names, codes, invoice numbers, quoted phrases |
| Hybrid | Broad discovery where both meaning and exact terms matter |

Hybrid search merges semantic and keyword results using Reciprocal Rank Fusion so strong candidates from either backend can rank well.

### Result tools

- Relevance score.
- Source document and chunk preview.
- Search history chips for repeated queries.
- Saved filters where configured.
- Collection and file-type filtering.
- Direct open into source context.

### Search quality tips

- Index documents before searching.
- Use natural questions for semantic mode.
- Use exact terms for keyword mode.
- Use hybrid mode when unsure.
- Re-index after embedding model changes.

---

## 14. Knowledge Graph

Knowledge Graph visualizes relationships among documents, collections, tags, and shared context.

### What it shows

- Document nodes sized by document/chunk characteristics.
- Collection and tag nodes.
- Edges based on shared collection membership, tags, or extracted relationships.
- Counts for nodes, edges, documents, collections, and tags.

### Controls

- Refresh graph data.
- Filter node types.
- Zoom in/out and reset view.
- Select nodes to inspect metadata.
- Navigate from graph items back to source material where supported.

Use the graph to spot isolated documents, densely connected topics, and clusters that deserve their own collection or workflow.

---

## 15. Compare Documents

Compare Documents analyzes two or more vault documents together.

### Inputs

- Select at least two documents.
- Optionally provide a focus query.
- Choose the desired detail level.

### Output

| Section | Description |
| --- | --- |
| Summary | Overall comparative readout |
| Similarities | Shared ideas, claims, themes, or structure |
| Differences | Where documents diverge |
| Contradictions | Conflicts that may need verification |
| Unique points | Document-specific findings grouped by source |
| Metrics | Tokens used and runtime duration |

Export the comparison as Markdown when the report should become part of a project record.

---

## 16. Quick Actions

Quick Actions are one-click AI tasks over selected documents.

### Available action families

| Action | Output |
| --- | --- |
| Summarize | Concise summary of selected content |
| Extract key points | Structured takeaways and facts |
| Translate | Translated text with meaning preserved |
| Rewrite/explain | Clearer or domain-adjusted language |
| Duplicate review | Exact or semantic duplication signals |
| Organize | Collection/tag suggestions |
| Q&A generation | Study or review questions with answers |

### Contextual guidance

Quick Actions can recommend useful actions based on selected document state, indexing readiness, and setup gaps. If no document is ready, follow the guidance to import, index, or repair provider configuration first.

---

## 17. Workflows

Workflows are reusable multi-step prompt chains. They are useful when a task needs the same logic repeatedly, such as preparing briefs, extracting action items, or repurposing content.

### Built-in starter templates

The workflow page includes guided starters for common jobs:

- Action item extraction from notes or transcripts.
- Research briefing from source material.
- Document critique and review.
- Content repurposing into multiple formats.

Template guides explain best-fit inputs, expected outcomes, and example use cases.

### Creating a workflow

1. Create or select a workflow.
2. Add ordered steps.
3. Configure each step's prompt template.
4. Optionally override model, temperature, or token limits per step.
5. Save and run the workflow against the current input.

### Run inspection

Workflow runs track:

- Step progress.
- Final output.
- Per-step output.
- Model used.
- Tokens used.
- Duration.
- Failure status and error text.

Historical runs can be reopened from the workflow page or from Operations drill-ins.

### Saving and exporting

Current or historical workflow results can be saved back into the Knowledge Vault or exported as text artifacts. Saved results become searchable and available to RAG after indexing.

---

## 18. Smart Inbox

Smart Inbox is the triage queue for files or external items that should be reviewed before entering the main vault.

### Inbox item details

Each item can show:

- File name, path, type, and size.
- Source type and source URL.
- AI preview when generated.
- Suggested collection.
- Suggested tags.
- Status: pending, accepted, rejected, or deferred.

### Actions

| Action | Result |
| --- | --- |
| Generate preview | Uses AI to summarize or classify pending items |
| Accept | Imports the item into the selected collection |
| Reject | Dismisses it from the import flow |
| Defer | Leaves it for later review |
| Focus from Operations | Opens an item that needs action |

Use Smart Inbox for watch-folder review, connector-sourced items, browser clips, and backlog grooming.

---

## 19. Weekly Digest

Weekly Digest summarizes recent activity in the knowledge system.

### Digest contents

- New document counts.
- Conversation activity.
- Top searches.
- File type distribution.
- Storage changes.
- Token usage.
- AI-generated insights where available.

### Actions

- Generate or refresh a digest.
- Review digest history.
- Export a digest for reporting or archival use.

Use Weekly Digest as an operating rhythm: review it at the end of a project week to decide which documents need indexing, which conversations should become decisions, and which workflows deserve automation.

---

## 20. Analytics

Analytics gives a deeper view into usage, quality, and intelligence coverage.

### Metric groups

| Group | Examples |
| --- | --- |
| Daily activity | Conversations, documents, searches, workflow runs |
| Model usage | Model counts, provider usage, token totals |
| File distribution | Document type mix |
| Workflow analytics | Top workflows and recent runs |
| Conversation intelligence | Summary freshness, recent summaries, recall results |
| Theme analysis | Conversation theme clusters and theme trends |

### Use cases

- See which workflows are actually used.
- Audit whether conversation summaries are fresh.
- Identify dominant topics.
- Watch indexing/search activity after an import push.
- Spot model usage patterns before changing defaults.

---

## 21. Model Manager

Model Manager provides a UI for Ollama model inventory and lifecycle management.

### Capabilities

- List installed models with size and metadata.
- Pull a model by name.
- Track pull progress.
- Delete unused models.
- Refresh the installed model list.
- Set or confirm defaults through Settings when needed.

### Model naming tips

Use explicit model names where possible:

```powershell
ollama pull llama3.2
ollama pull mistral
ollama pull nomic-embed-text
```

Embedding models should include names such as `embed`, `nomic`, `bge`, or `minilm` so the app can identify them reliably during setup.

---

## 22. Hardware Advisor

Hardware Advisor detects system capacity and recommends suitable models.

### Detection areas

- GPU name and acceleration summary.
- VRAM and system RAM.
- CPU and operating environment.
- Recommended model size tier.
- Chat, code, and embedding model suggestions.

### How to use recommendations

- Use smaller or quantized models when VRAM is limited.
- Keep context windows smaller on low-memory machines.
- Prefer embedding models optimized for retrieval speed when indexing large vaults.
- Refresh after changing GPUs, drivers, or runtime configuration.

---

## 23. Backup and Restore

Backup and Restore protects local Agent-X data.

### Backup options

| Option | Purpose |
| --- | --- |
| Destination | Folder where backup packages are written |
| Include documents | Includes vault source artifacts, not just the database |
| Encryption | Adds password protection to the backup package |
| Notes | Adds human-readable context to the backup history |
| Scheduled backups | Runs recurring backups at the selected interval |
| Retention | Limits how many backups are kept |

### Restore behavior

Choose a backup package, provide the password if encrypted, and run restore. Review the restore summary afterward. Restore operations should be treated as data-changing maintenance; close other Agent-X windows or background jobs first.

### Backup before high-risk changes

Create a fresh backup before:

- Enabling at-rest database encryption.
- Importing a large new corpus.
- Changing sync scope.
- Moving to a new machine.
- Running major cleanup or duplicate removal.

---

## 24. Collaborative Sync

Collaborative Sync packages local changes and imports remote changes through a configured sync folder.

### Configuration

| Field | Meaning |
| --- | --- |
| Sync folder | Local, network, or shared folder used for exchange |
| Encryption key | Protects sync packages |
| Auto-sync | Enables recurring sync passes |
| Interval | Minutes between auto-sync passes |
| Scope | All data or selected collections |
| Selected collections | Included when scope is set to selected collections |

### Manual sync

Use **Sync Now** to:

1. Export local changes.
2. Read remote packages.
3. Import remote changes.
4. Update status, duration, pending changes, and history.

### History and conflicts

The history list tracks recent sync passes and conflicts. Operations can focus a specific sync log when an action is needed. If conflicts appear, review the sync status before starting another pass.

---

## 25. Calendar and Email Connectors

Calendar and Email pages configure external productivity connectors.

### Calendar

Calendar sync supports:

- Provider enable/disable.
- OAuth connection and disconnection.
- Manual sync.
- Sync interval selection.
- Past/future window configuration.
- Conflict resolution: remote wins, local wins, or merge.
- Last sync and next sync indicators.

### Email

Email sync supports:

- Provider enable/disable.
- OAuth connection and disconnection.
- Manual sync.
- Sync interval selection.
- Maximum messages per sync.
- Days-back sync window.
- Last sync and next sync indicators.

Connector output can feed Smart Inbox and Operations so external items are reviewed before becoming vault material.

---

## 26. Annotations

Annotations capture highlights and notes connected to documents.

### Annotation fields

- Source document.
- Highlighted text.
- Note text.
- Color.
- Created and updated timestamps.

### Tools

- Search annotations.
- Filter by color.
- Edit note text and color.
- Delete annotations.
- Export annotations as Markdown.

Annotations are useful for turning reading notes into searchable project evidence.

---

## 27. Settings

Settings is the control plane for provider, inference, indexing, security, storage, and app behavior.

### Key groups

| Group | Controls |
| --- | --- |
| AI Provider | Ollama endpoint, active provider, OpenAI key, Anthropic key |
| Inference | Temperature, max tokens, context window |
| Knowledge Vault | Chunk size, chunk overlap, top-K, indexing behavior |
| Web Search | Provider and API configuration where enabled |
| Database Encryption | SQLCipher enablement and key/passphrase flow |
| Language/UI | Locale follows Windows display language |

### Database encryption

Agent-X can encrypt the local SQLite vault with SQLCipher. The encryption key is managed automatically and tied to your Windows user profile, and the feature is available to every user free of charge.

Before enabling encryption:

1. Create a fresh backup.
2. Confirm the backup restores in a safe environment if the data is critical.
3. Store passphrases securely.
4. Keep `encryption.info.json` with the encrypted database when backing up manually.

Disabling encryption in-place is not supported in this release; restore a plaintext backup if you need to return to an unencrypted vault.

---

## 28. Command Palette, Jump To, and Shortcuts

### Command Palette

Open the command palette with `Ctrl+K` or `Ctrl+Shift+P`. Type to filter registered pages and actions, press `Enter` to run the selected command, and press `Esc` to dismiss it.

### Jump To

Open Jump To with `Ctrl+P`. Use it for fast navigation to documents, conversations, or supported destinations.

### Cheatsheet

Open the shortcuts cheatsheet with `F1` or `Ctrl+Shift+?`.

### Shipped global shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+K` | Command Palette |
| `Ctrl+Shift+P` | Command Palette |
| `Ctrl+N` | AI Chat / new conversation |
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
| `F1` | Keyboard shortcuts |
| `Ctrl+Shift+?` | Keyboard shortcuts |
| `Ctrl+1` through `Ctrl+9` | Quick-access page slots |

---

## 29. Instrument Strip, Notifications, and Tray

### Instrument strip (status bar)

The bottom of the main window is an instrument strip: a row of live readouts and stencil word-lamps that stays dark in both the dark and light themes. Every value is real and updates on a background poll.

- `MDL` lamp and readout: the loaded model name when the AI provider is linked (green), or an amber caution with "Ollama not detected" when it is not.
- `Ctrl+K` key hint for the command palette.
- `IDX` readout: the embedding queue depth. It burns amber while indexing and rests dim at zero.
- `VAULT` readout: total document count.
- Annunciator lamps: `INBOX` (amber when triage items are waiting), `SYNC` (green when configured and idle, teal while syncing, red on a sync error, unlit when not configured), `JOBS` (teal while a workflow runs, red if the latest run failed), `BAK` (green when the last backup is under a week old, amber when older, unlit if no backup exists).
- `LOCAL` / `NET` privacy lamp: green `LOCAL` when every configured provider is local, amber `NET` when a cloud provider is active.
- Version label.

Clicking any lit lamp jumps to its source page (for example, `INBOX` opens the Smart Inbox and `BAK` opens Backup & Restore). A blinking lamp indicates an unacknowledged warning; clicking it once acknowledges it and stops the blink.

### Notifications

Agent-X uses in-app notifications for long-running and asynchronous work such as imports, indexing, sync, backup, and workflow outcomes.

### System tray

The tray icon provides:

- Open Agent-X.
- Quick Chat.
- Settings.
- Exit.

Use the tray when Agent-X should stay available without occupying the main window.

---

## 30. Privacy and Security

### Local-first security model

- No telemetry is required for core functionality.
- Local AI is the default architecture.
- The vault, settings, logs, embeddings, and conversations stay in the user's profile directory.
- Optional SQLCipher encryption protects the database at rest.
- DPAPI is used where Windows user-bound secrets are required.

### Provider keys

API keys are entered by the user and stored locally. They are only sent to their respective providers when those providers are used.

### Open-source license

Agent-X is free and open-source software released under the MIT License. Every capability is unconditionally available to every user — there are no tiers, no activation, no document limits, and no subscription checks of any kind.

---

## 31. Troubleshooting

### Ollama not detected

1. Run `ollama list`.
2. Confirm Ollama is listening on `http://localhost:11434`.
3. Verify the endpoint in Settings.
4. Restart Ollama.
5. Re-run Dashboard **Setup AI**.

### Models do not appear

1. Pull at least one model with `ollama pull <model-name>`.
2. Confirm `ollama list` shows it.
3. Test the Agent-X Ollama connection.
4. Refresh Model Manager.

### Documents remain pending

1. Check that an embedding model is selected.
2. Confirm Ollama is running.
3. Review Knowledge Vault indexing errors.
4. Re-index the document.
5. Check Operations for imported-document health.

### Search returns no results

1. Confirm documents are indexed.
2. Try Hybrid mode.
3. Broaden the query.
4. Remove overly narrow filters.
5. Re-index after changing embedding models.

### Ask Your Files gives weak citations

1. Narrow the collection or selected documents.
2. Use a more specific question.
3. Re-index source documents.
4. Confirm chunk size/top-K settings are reasonable.
5. Check whether the document text extraction is complete.

### Workflow run fails

1. Open the run from Workflows or Operations.
2. Review the failed step and error text.
3. Confirm the model/provider is reachable.
4. Reduce max tokens or context length if the model runs out of memory.
5. Save useful partial output before retrying.

### Sync is stuck or reports conflicts

1. Verify the sync folder is reachable.
2. Confirm the encryption key matches across machines.
3. Review sync history.
4. Run manual sync once.
5. Resolve focused Operations sync items before enabling auto-sync again.

### High memory usage

1. Use a smaller model.
2. Reduce context window size.
3. Use quantized models.
4. Close other memory-heavy apps.
5. Follow Hardware Advisor recommendations.

### App starts into onboarding unexpectedly

Check `%LocalAppData%\AgentX\settings.json`. If `"onboardingCompleted"` is missing or false, onboarding runs. Finish the wizard or set the value to `true` while the app is closed.

---

## 32. FAQ

**Does Agent-X send my data to the cloud?**

Not by default. Local Ollama workflows keep documents and prompts on your machine. Data is sent externally only when you explicitly use a configured cloud provider, connector, or web feature.

**Can I use Agent-X without a GPU?**

Yes. CPU inference works but is slower. Use smaller models and the Hardware Advisor.

**What is the difference between chat and embedding models?**

Chat models generate text. Embedding models convert text into vectors used by Semantic Search, RAG, relatedness, and duplicate analysis.

**What happens when I delete a document?**

Agent-X removes the vault record, chunks, embeddings, and relationships. The original source file is not necessarily deleted unless a specific workflow says so.

**Where is my data?**

By default: `%LocalAppData%\AgentX\`.

**Can I move Agent-X to another machine?**

Use Backup and Restore or Collaborative Sync. If the database is encrypted, move the required encryption metadata or passphrase too.

**Why should I use Collections if Search already works?**

Collections create better scopes for RAG, filters, sync, workflows, and project separation.

**When should I use Workflows instead of Quick Actions?**

Use Quick Actions for one-off document tasks. Use Workflows when the same multi-step prompt process should be repeated, inspected, saved, or exported.

---

## 33. Supported File Types

### Documents

| Extension | Type | Processing |
| --- | --- | --- |
| `.pdf` | PDF | Text extraction with document metadata |
| `.docx` | Word document | OpenXML text extraction |
| `.doc` | Legacy Word | Legacy document extraction where supported |

### Text and data

| Extension | Type | Processing |
| --- | --- | --- |
| `.txt` | Plain text | Direct text extraction |
| `.csv` | CSV | Text/table-like extraction |
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
| `.markdown` | Markdown | Markdown-aware text extraction |

### Images

| Extension | Type | Processing |
| --- | --- | --- |
| `.png` | Image | Metadata/OCR path where enabled |
| `.jpg`, `.jpeg` | Image | Metadata/OCR path where enabled |
| `.bmp` | Image | Metadata/OCR path where enabled |
| `.tiff` | Image | Metadata/OCR path where enabled |

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
| `.html`, `.css`, `.scss` | Web source |
| `.sql` | SQL |
| `.sh` | Shell |
| `.xaml` | XAML |

---

*Agent-X is developed by Rocky Elsalaymeh / Strategia. For support, feature requests, or bug reports, use the official support channel included with the product.*
