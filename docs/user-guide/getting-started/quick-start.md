# Agent-X Quick Start Guide

**Get up and running with Agent-X in 15 minutes**

---

## Table of Contents

1. [Installation](#installation)
2. [First Launch](#first-launch)
3. [Import Your First Documents](#import-your-first-documents)
4. [Start Chatting](#start-chatting)
5. [Search Your Knowledge Base](#search-your-knowledge-base)
6. [Next Steps](#next-steps)

---

## Installation

### Step 1: Download the Installer

Download `AgentX-Setup-2.1.2-x64.exe` from:
- The GitHub Releases page, or
- The `installer-output/` directory

### Step 2: Run the Installer

1. Double-click the installer executable
2. The installer does not require administrator privileges
3. Agent-X installs to `%LocalAppData%\Programs\Agent-X`
4. **The bundled Llama 3.2 3B model (~2 GB) is installed automatically**

### Step 3: Launch Agent-X

Open Agent-X from:
- The Start Menu (search "Agent-X"), or
- The desktop shortcut (created during installation)

```
┌─────────────────────────────────────────────┐
│  Welcome to Agent-X                         │
│  Intelligence Hub for Windows              │
│                                             │
│  The application is initializing...         │
│                                             │
│  ├─ Loading AI models                       │
│  ├─ Initializing database                   │
│  └─ Preparing services                      │
│                                             │
│  ████████████████████████████ 100%          │
└─────────────────────────────────────────────┘
```

---

## First Launch

### Unlock Screen

On first launch, you'll see the unlock screen:

```
┌─────────────────────────────────────────────┐
│  🔐 Agent-X - Unlock                        │
│                                             │
│  Enter your passphrase to decrypt the       │
│  vault and start using Agent-X.             │
│                                             │
│  ┌─────────────────────────────────────┐   │
│  │ ••••••••••••••••                    │   │
│  └─────────────────────────────────────┘   │
│                                             │
│  [Unlock]                    [Show Password]│
│                                             │
│  ℹ️ First time? Create a secure passphrase. │
└─────────────────────────────────────────────┘
```

### Create Your Passphrase

1. Choose a secure passphrase (minimum 8 characters)
2. This passphrase encrypts your database with AES-256-CBC
3. **There is no password recovery** — keep it safe!

```
Security Tips:
├─ Use at least 12 characters
├─ Mix letters, numbers, and symbols
├─ Avoid common words or phrases
└─ Consider using a passphrase manager
```

### Main Dashboard

After unlocking, you'll see the main dashboard:

```
┌─────────────────────────────────────────────────────────────┐
│  Agent-X                    [🔔] [⚙️]          [👤 Rocky]  │
├─────────────────────────────────────────────────────────────┤
│  │                                                         │
│  │  ┌───────────────────────────────────────────────────┐ │
│  │  │  📊 Dashboard                                    │ │
│  │  │                                                   │ │
│  │  │  ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐           │ │
│  │  │  │ 156  │ │  23  │ │ 1.2M │ │  47  │           │ │
│  │  │  │ Docs │ │ Chats│ │Tokens│ │Tags │           │ │
│  │  │  └──────┘ └──────┘ └──────┘ └──────┘           │ │
│  │  │                                                   │ │
│  │  │  Recent Activity...                               │ │
│  │  │  Storage Usage...                                 │ │
│  │  │  AI Provider Status...                            │ │
│  │  └───────────────────────────────────────────────────┘ │
│  │                                                         │
│  [🏠 Home] [💬 Chat] [📚 Vault] [🔍 Search] [🕸️ Graph]    │
└─────────────────────────────────────────────────────────────┘
```

---

## Import Your First Documents

### Step 1: Navigate to Knowledge Vault

Click the **[📚 Vault]** button in the left navigation, or press `Ctrl+L`.

### Step 2: Import Documents

Click the **[+ Import Documents]** button in the top-right corner.

```
┌─────────────────────────────────────────────┐
│  📄 Import Documents                         │
│                                             │
│  Select files or folders to import:         │
│                                             │
│  ┌─────────────────────────────────────┐   │
│  │ Browse...                            │   │
│  │                                     │   │
│  │ Supported formats:                  │   │
│  │ • PDF (.pdf)                         │   │
│  │ • Word (.docx, .doc)                 │   │
│  │ • Text (.txt, .md)                   │   │
│  │ • Markdown (.md)                     │   │
│  │ • Code (.cs, .py, .js, etc.)         │   │
│  │ • JSON (.json)                       │   │
│  │ • HTML (.html, .htm)                 │   │
│  │                                     │   │
│  │ [Select Files]  [Select Folder]     │   │
│  └─────────────────────────────────────┘   │
│                                             │
│  ☐ Auto-generate titles with AI            │
│  ☐ Auto-tag documents on import            │
│                                             │
│              [Import]  [Cancel]             │
└─────────────────────────────────────────────┘
```

### Step 3: Watch the Progress

Your documents will be processed:

```
┌─────────────────────────────────────────────┐
│  ⏳ Importing Documents...                  │
│                                             │
│  ┌─────────────────────────────────────┐   │
│  │ project-plan.pdf                     │   │
│  │ ├─ Extracting text... ✓              │   │
│  │ ├─ Generating embedding... ✓         │   │
│  │ ├─ Auto-tagging... ✓                 │   │
│  │ └─ Done!                             │   │
│  │                                       │   │
│  │ meeting-notes.docx                    │   │
│  │ ├─ Extracting text... ▸              │   │
│  │ └─ 67% complete...                   │   │
│  │                                       │   │
│  │ research-paper.pdf                    │   │
│  │ └─ Queued...                         │   │
│  └─────────────────────────────────────┘   │
│                                             │
│  Progress: 2/3 documents                    │
└─────────────────────────────────────────────┘
```

### What Happens During Import?

For each document, Agent-X:

1. **Extracts text content** using specialized parsers
2. **Generates embeddings** (vector representations) using the bundled model
3. **Auto-generates a title** based on content analysis
4. **Auto-assigns tags** with confidence scores
5. **Indexes for search** (both keyword and semantic)

---

## Start Chatting

### Step 1: Open AI Chat

Click the **[💬 Chat]** button in the left navigation, or press `Ctrl+I`.

### Step 2: Ask Your First Question

Type a question in the chat input:

```
┌─────────────────────────────────────────────────────────────┐
│  💬 AI Chat                                                 │
├─────────────────────────────────────────────────────────────┤
│  │                                                         │
│  │  ┌───────────────────────────────────────────────────┐ │
│  │  │ You: What are the key milestones from the         │ │
│  │  │       project plan I imported?                    │ │
│  │  └───────────────────────────────────────────────────┘ │
│  │                                                         │
│  │  ┌───────────────────────────────────────────────────┐ │
│  │  │ Agent-X: Based on the project plan document...     │ │
│  │  │                                                     │ │
│  │  │ The key milestones are:                           │ │
│  │  │                                                     │ │
│  │  │ 1. **Phase 1: Foundation** (Weeks 1-4)           │ │
│  │  │    - Architecture design                         │ │
│  │  │    - Database schema                             │ │
│  │  │    - Core API development                        │ │
│  │  │                                                     │ │
│  │  │ 2. **Phase 2: Features** (Weeks 5-8)             │ │
│  │  │    - User authentication                         │ │
│  │  │    - Dashboard implementation                     │ │
│  │  │    - Reporting module                            │ │
│  │  │                                                     │ │
│  │  │ 3. **Phase 3: Launch** (Weeks 9-12)              │ │
│  │  │    - Beta testing                                │ │
│  │  │    - Performance optimization                     │ │
│  │  │    - Production deployment                        │ │
│  │  │                                                     │ │
│  │  │ 📎 Source: project-plan.pdf, page 3             │ │
│  │  └───────────────────────────────────────────────────┘ │
│  │                                                         │
│  │  ┌───────────────────────────────────────────────────┐ │
│  │  │ [📋 Copy] [🔄 Regenerate] [👍] [👎] [🗑️ Delete] │ │
│  │  └───────────────────────────────────────────────────┘ │
│  │                                                         │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ Type your message...                   [Send] 📤     │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### RAG in Action

Agent-X uses **Retrieval-Augmented Generation (RAG)**:

1. Your question is converted to a vector embedding
2. Similar documents are retrieved from your vault
3. Retrieved content is included in the AI prompt
4. The AI generates an answer with citations

---

## Search Your Knowledge Base

### Step 1: Open Search

Click the **[🔍 Search]** button in the left navigation, or press `Ctrl+K`.

### Step 2: Run a Semantic Search

Type a query — you don't need exact keywords!

```
┌─────────────────────────────────────────────────────────────┐
│  🔍 Semantic Search                                        │
├─────────────────────────────────────────────────────────────┤
│  │                                                         │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ 🔎 deployment checklist                     [Search] │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  🔍 Recent Searches:                                       │
│  [project timeline] [meeting notes] [api documentation]    │
│                                                             │
│  ─────────────────────────────────────────────────────────│
│                                                             │
│  📄 Deployment Checklist.docx                    94% match  │
│     ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━│
│     This document contains the production deployment      │
│     checklist including server setup, database migration,  │
│     and verification steps...                              │
│     #deployment #production #checklist                    │
│                                                             │
│  📄 Project Plan Overview.pdf                     87% match  │
│     ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━│
│     Includes deployment phase with milestones...           │
│     #project #planning #milestones                         │
│                                                             │
│  📄 Meeting Notes - 2024-01-15.txt               72% match  │
│     ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━│
│     Discussed deployment requirements and timeline...      │
│     #meeting #notes                                       │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Hybrid Search

Agent-X runs **both semantic and keyword searches** in parallel:

- **Semantic Search**: Finds conceptually similar content using vector embeddings
- **Keyword Search**: Finds exact matches using SQLite FTS5
- **Results Merged**: Combined using Reciprocal Rank Fusion (RRF)

This means you get the best of both worlds — fuzzy conceptual matching AND precise keyword results.

---

## Next Steps

### Explore More Features

| Feature | Description | Shortcut |
|---------|-------------|-----------|
| **Knowledge Graph** | Visualize connections between documents | `Ctrl+G` |
| **Workflows** | Automate repetitive tasks | `Ctrl+W` |
| **Analytics** | View usage statistics and insights | `Ctrl+A` |
| **Model Manager** | Manage AI models and providers | `Ctrl+M` |
| **Settings** | Configure application preferences | `Ctrl+,` |

### Configure Cloud AI (Optional)

While Agent-X works fully offline with the bundled model, you can add cloud providers:

1. Navigate to **[⚙️ Settings] → AI Providers**
2. Add your API key for OpenAI or Anthropic
3. Your keys are stored encrypted in Windows credential manager

```
┌─────────────────────────────────────────────┐
│  ⚙️ AI Provider Settings                     │
│                                             │
│  Bundled Local Model                        │
│  ├─ Model: Llama 3.2 3B Instruct           │
│  ├─ Status: ✓ Active                       │
│  └─ Location: Local                        │
│                                             │
│  Ollama (Local)                             │
│  ├─ Status: ✗ Not detected                 │
│  └─ Expected: http://localhost:11434       │
│                                             │
│  OpenAI                                    │
│  ├─ Status: ✗ Not configured               │
│  └─ [Configure API Key]                    │
│                                             │
│  Anthropic (Claude)                         │
│  ├─ Status: ✗ Not configured               │
│  └─ [Configure API Key]                    │
└─────────────────────────────────────────────┘
```

### Enable GPU Acceleration (Optional)

If you have an NVIDIA GPU:

1. Navigate to **[⚙️ Settings] → AI Runtime**
2. Enable **GPU Acceleration**
3. Select your VRAM tier (2 GB to 8+ GB)

```
┌─────────────────────────────────────────────┐
│  ⚡ GPU Acceleration Settings                │
│                                             │
│  CUDA Support                               │
│  ├─ Status: ✓ Detected (CUDA 12.6)         │
│  ├─ GPU: NVIDIA GeForce RTX 4090           │
│  └─ VRAM: 24 GB                            │
│                                             │
│  Layer Offloading                           │
│  ├─ ☐ 2 GB tier (minimal offloading)       │
│  ├─ ☐ 4 GB tier                            │
│  ├─ ☑ 8 GB tier (recommended)              │
│  └─ ☐ Max tier (aggressive offloading)     │
│                                             │
│  Expected Speedup: 20-30x                  │
└─────────────────────────────────────────────┘
```

### Read Full Documentation

- **[Comprehensive User Guide](../comprehensive-user-guide.md)** — Complete feature documentation
- **[FAQ](../faq.md)** — Frequently asked questions
- **[Troubleshooting](../troubleshooting.md)** — Common issues and solutions

---

## Getting Help

### Documentation

| Document | Description |
|----------|-------------|
| [User Guide](../comprehensive-user-guide.md) | Complete product documentation |
| [Architecture](../ARCHITECTURE.md) | System architecture overview |
| [Developer Guide](../DEVELOPER-GUIDE.md) | Developer reference |
| [API Reference](../API-REFERENCE.md) | Public API documentation |

### Support

- **Issues**: Report bugs on GitHub Issues
- **Discussions**: Ask questions in GitHub Discussions
- **Changelog**: See what's new in [CHANGELOG.md](../../CHANGELOG.md)

---

**Congratulations!** You've completed the Agent-X Quick Start. You now have:

- ✅ A fully functional local AI assistant
- ✅ Imported documents indexed for semantic search
- ✅ RAG-powered chat with your knowledge base
- ✅ Offline-first privacy and security

**Happy exploring!** 🚀
