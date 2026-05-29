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

Agent-X is a local-first AI-powered document intelligence application for Windows. It transforms your personal document collection into a queryable, AI-augmented knowledge base. Import documents, ask questions in natural language, search semantically across your entire vault, and interact with AI models — all without your data leaving your machine.

### What makes Agent-X different from other AI tools?

| Feature | Agent-X | Other AI Tools |
|---------|---------|----------------|
| **Data Privacy** | 100% local — your data never leaves your machine | Cloud-based with data sent to external servers |
| **Offline Capability** | Fully functional offline with bundled model | Requires internet connection |
| **No Subscription** | One-time purchase, perpetual use | Monthly/annual subscription required |
| **Open Models** | Uses open-source Llama models | Often uses proprietary closed models |
| **RAG Pipeline** | Enterprise-grade 6-stage retrieval | Basic or no retrieval |
| **Database Encryption** | AES-256-CBC at-rest encryption | Varies, often unencrypted |

### Is Agent-X free?

Yes — completely. Agent-X is 100% free and open-source software, released under the MIT License. Every capability is unconditionally available to every user: unlimited documents, advanced RAG, GPU acceleration, multi-provider AI, the REST API, the full intelligence stack, plugins, integrations, and encryption. There are no paid tiers, no subscriptions, no activation, no quotas, and no feature gates of any kind.

### What file formats does Agent-X support?

Agent-X supports the following formats:

| Category | Formats |
|----------|---------|
| **Documents** | PDF (.pdf), Word (.docx, .doc), RTF (.rtf) |
| **Text** | Plain text (.txt), Markdown (.md) |
| **Code** | All programming languages (.cs, .py, .js, .ts, .go, .rs, etc.) |
| **Data** | JSON (.json), XML (.xml), CSV (.csv) |
| **Web** | HTML (.html, .htm) |

### Does Agent-X work offline?

Yes! Agent-X ships with Llama 3.2 3B bundled in the installer (~2 GB). You get fully functional offline AI out of the box — no internet connection required after installation.

---

## Installation & Setup

### What are the system requirements?

| Requirement | Minimum | Recommended |
|-------------|----------|-------------|
| **Windows** | 10 build 19041+ (2004) | 11 |
| **Architecture** | x64 | x64 |
| **RAM** | 8 GB | 16 GB+ |
| **Disk Space** | 5 GB | 10 GB+ |
| **GPU** | None required | NVIDIA with CUDA 12 support |

### How do I install Agent-X?

1. Download `AgentX-Setup-2.1.0-preview.1-x64.exe`
2. Run the installer (no admin privileges required)
3. Launch from Start Menu or desktop shortcut
4. Create a passphrase on first launch

### Do I need administrator rights?

No. Agent-X installs to `%LocalAppData%\Programs\Agent-X` by default, which doesn't require elevation. Administrator rights are only needed if you want to install to Program Files for all users.

### Can I install Agent-X on a USB drive?

Yes. During installation, choose a custom location and select your USB drive. Note that performance will be slower than installing to an internal SSD.

### How do I uninstall Agent-X?

1. Go to **Settings → Apps → Installed Apps** in Windows
2. Find "Agent-X" and click **Uninstall**
3. Your data in `%LocalAppData%\AgentX\` is preserved

To completely remove Agent-X including data:
1. Uninstall the application
2. Delete `%LocalAppData%\AgentX\` manually
3. Delete your Windows credential manager entries for Agent-X

### Can I migrate my data to another computer?

Yes. Your database and documents can be migrated:

1. Copy `%LocalAppData%\AgentX\` to the new computer
2. Install Agent-X on the new computer
3. Replace the newly created data directory with your backup
4. Launch Agent-X and unlock with your original passphrase

---

## Features & Usage

### How do I import documents?

1. Navigate to **Knowledge Vault** (`Ctrl+L`)
2. Click **[+ Import Documents]**
3. Select files or folders
4. Choose import options (auto-title, auto-tag)
5. Click **Import**

### Can I import entire folders?

Yes. Use **[Select Folder]** during import to recursively import all supported files in a directory tree.

### How do I organize my documents?

Agent-X provides several organization methods:

| Method | Description |
|--------|-------------|
| **Collections** | Group documents manually or by rules |
| **Tags** | Auto-generated or manually applied |
| **Search Folders** | Save searches as virtual folders |
| **Conversation Folders** | Organize chats (Work, Research, Personal) |

### What is the Knowledge Graph?

The Knowledge Graph is an interactive visualization showing connections between:
- Documents (nodes)
- Collections (nodes)
- Tags (nodes)

Edges show relationships:
- Document → Collection membership
- Document → Tag associations
- Tag co-occurrence

### How do I use the command palette?

Press `Ctrl+K` to open the command palette. Type commands like:

- "Import documents"
- "Search for meeting notes"
- "Open settings"
- "Start new chat"
- "Show knowledge graph"

### What are Workflows?

Workflows automate repetitive tasks:

| Workflow | Description |
|----------|-------------|
| **Batch Import** | Import and process multiple documents |
| **Weekly Digest** | Generate summary reports |
| **Tag Cleanup** | Merge duplicate tags |
| **Re-index Vault** | Refresh all document embeddings |

---

## AI & Models

### What AI models are supported?

| Provider | Models | Type |
|----------|--------|------|
| **Bundled** | Llama 3.2 3B Instruct | Local (ships with app) |
| **Ollama** | Llama 3.x, Phi 4, Mistral, etc. | Local (user-managed) |
| **OpenAI** | GPT-4o, GPT-4o-mini, etc. | Cloud (API key) |
| **Anthropic** | Claude 3.5 Sonnet, Haiku, etc. | Cloud (API key) |

### What is the bundled model?

Agent-X ships with **Llama 3.2 3B Instruct**, a compact but capable language model:

- **Size**: ~2 GB
- **Performance**: ~3 tokens/sec (CPU), ~15-25 tokens/sec (GPU)
- **Capability**: Chat, summarization, question-answering
- **License**: Apache 2.0 (open source)

### How do I add more models?

**For Ollama (local):**
1. Install Ollama from [ollama.com](https://ollama.com)
2. Pull models: `ollama pull llama3.2` or `ollama pull phi4`
3. Agent-X auto-detects Ollama at `http://localhost:11434`

**For cloud providers:**
1. Go to **Settings → AI Providers**
2. Click **Configure** next to the provider
3. Enter your API key
4. Keys are stored encrypted in Windows credential manager

### Can I use my own models?

Yes. Agent-X supports any model exposed via:
- Ollama (run `ollama run <model-name>`)
- OpenAI-compatible endpoints
- Custom providers (via plugin system)

### How do I switch between models?

1. Go to **Settings → AI Runtime**
2. Select your preferred provider from the dropdown
3. Choose a specific model from that provider
4. Your selection persists across sessions

### What is GPU acceleration?

GPU acceleration moves AI computation from CPU to GPU:

| Hardware | Speedup | Notes |
|----------|---------|-------|
| **CPU only** | 1x (baseline) | Works everywhere |
| **NVIDIA 4 GB VRAM** | 2-5x | Entry-level gaming GPU |
| **NVIDIA 8 GB VRAM** | 5-15x | Mid-range GPU (recommended) |
| **NVIDIA 16+ GB VRAM** | 15-30x | High-end GPU (RTX 4090, etc.) |

Enable in **Settings → AI Runtime → GPU Acceleration**.

---

## Search & RAG

### What is RAG?

**RAG** stands for **Retrieval-Augmented Generation**. It's a technique that:

1. Retrieves relevant documents from your knowledge base
2. Includes them in the AI prompt
3. Generates answers grounded in your data

This means Agent-X doesn't just "hallucinate" — it cites sources from your actual documents.

### How does semantic search work?

Semantic search uses **vector embeddings**:

1. Your query is converted to a vector (list of numbers)
2. Document embeddings are compared using cosine similarity
3. Most similar documents are returned

This finds conceptually related content even without exact keyword matches.

### What is hybrid search?

Hybrid search runs **both semantic and keyword searches** in parallel:

| Search Type | Best For |
|-------------|----------|
| **Semantic** | Concepts, meaning, related topics |
| **Keyword** | Exact phrases, names, technical terms |
| **Hybrid (RRF)** | Best of both — merged results |

Results are combined using **Reciprocal Rank Fusion (RRF, k=60)**.

### What is HyDE?

**HyDE** (Hypothetical Document Embeddings) improves retrieval:

1. AI generates a "hypothetical" ideal answer to your query
2. This hypothetical answer is embedded
3. Used to find similar real documents

This helps when your query doesn't match the language in your documents.

### What are citations?

Citations link AI responses to source documents:

```
According to the project plan, Phase 1 focuses on foundation work
including architecture design and database setup.

📎 Source: project-plan.pdf, page 3
```

Click the citation to open the document at the relevant location.

---

## Performance & Hardware

### How fast is the bundled model?

| Hardware | Tokens/sec | Notes |
|----------|------------|-------|
| **CPU (modern)** | 2-4 | Usable for chat |
| **CPU (older)** | 1-2 | Slow but functional |
| **GPU 4 GB** | 8-15 | Good experience |
| **GPU 8 GB** | 15-25 | Excellent |
| **GPU 16+ GB** | 30-50+ | Near-instant |

### Can Agent-X use multiple GPUs?

Not currently. Agent-X uses a single GPU for inference. Multi-GPU support is planned for a future release.

### How much disk space do I need?

| Component | Size |
|-----------|------|
| **Application** | ~500 MB |
| **Bundled Model** | ~2 GB |
| **Database** | Varies by usage (~100 MB per 1000 documents) |
| **Documents** | Your actual file sizes |

**Total minimum:** 5 GB free space
**Recommended:** 10 GB+ free space

### How much RAM does Agent-X use?

| Usage | RAM |
|-------|-----|
| **Idle** | ~200 MB |
| **Chat (CPU inference)** | ~1-2 GB |
| **Chat (GPU inference)** | ~500 MB - 1 GB |
| **Large vault (10k+ docs)** | ~2-4 GB |

---

## Privacy & Security

### Is my data sent to the cloud?

**No.** Agent-X is local-first:

- All processing happens on your machine
- No telemetry, analytics, or phone-home
- Your data never leaves your computer

The exception is if you explicitly configure cloud AI providers (OpenAI, Anthropic). In that case, only your prompts and retrieved documents are sent to generate responses.

### How is my data encrypted?

Agent-X uses **SQLCipher** with:

- **Algorithm:** AES-256-CBC
- **Key derivation:** PBKDF2-HMAC-SHA256 (100,000 iterations)
- **Key storage:** Windows DPAPI, tied to your Windows account (available to every user)

### What happens if I forget my passphrase?

**There is no password recovery.** Agent-X uses strong encryption specifically so that not even the developers can access your data.

If you forget your passphrase:
- Your data is permanently inaccessible
- You can delete the database and start fresh

**Tip:** Store your passphrase in a secure password manager.

### Are my API keys safe?

Yes. API keys are stored in **Windows Credential Manager**:

- Encrypted with Windows DPAPI
- Tied to your Windows user account
- Never stored in plain text or config files

### Can I use Agent-X in a corporate environment?

Yes, but consider:

| Factor | Recommendation |
|--------|----------------|
| **Data Policy** | Local-first means data stays on your machine |
| **Approval** | Check with IT before installing |
| **Licensing** | MIT — free for commercial use with no per-seat fees |
| **Support** | Community support via the project repository |

---

## Licensing

### How is Agent-X licensed?

Agent-X is free and open-source software released under the **MIT License**. Every feature — bundled model, semantic search, GPU acceleration, advanced RAG, Knowledge Graph, multi-provider AI, REST API, sync, and analytics — is unconditionally available to every user. There is nothing to buy, activate, or upgrade.

### Is there anything I need to pay for?

No. Agent-X is completely free. There are no tiers, no subscriptions, no trials, and no quotas. Install it and use everything, forever, at no cost.

### Can I use Agent-X commercially?

Yes. The MIT License lets you use, copy, modify, merge, publish, distribute, sublicense, and even sell copies of Agent-X — for personal, business, or enterprise use — with no per-user or per-seat restrictions. The only condition is that the MIT copyright and permission notice be included in copies of the software.

---

## Troubleshooting

### Agent-X won't start

**Possible causes:**

1. **Another instance is running**
   - Check Task Manager for `AgentX.exe`
   - End the process and restart

2. **Database locked**
   - Ensure no other process is accessing the database
   - Restart your computer

3. **Corrupt installation**
   - Uninstall and reinstall Agent-X
   - Your data is preserved in `%LocalAppData%\AgentX\`

### AI responses are slow

**Solutions:**

1. **Enable GPU acceleration** (if you have a compatible GPU)
   - Go to **Settings → AI Runtime**
   - Enable **GPU Acceleration**

2. **Switch to a smaller model**
   - Use Llama 3.2 3B (bundled) instead of larger models
   - Smaller models are faster

3. **Reduce context window**
   - Smaller context = faster processing
   - Settings → AI Runtime → Context Window

### Search returns no results

**Possible causes:**

1. **Documents not indexed**
   - Check **Knowledge Vault** for "Indexed" status
   - Re-index if needed

2. **Query too specific**
   - Try broader terms
   - Use semantic search for concepts

3. **Wrong search mode**
   - Try **Hybrid** search instead of pure semantic or keyword

### Import fails

**Common issues:**

| Issue | Solution |
|-------|----------|
| **Unsupported format** | Check file is in supported format list |
| **Corrupt file** | File may be damaged — try opening separately |
| **Access denied** | Ensure file isn't open in another application |
| **Password protected** | Remove password protection first |

### GPU acceleration not working

**Check:**

1. **CUDA installed?**
   - Agent-X includes CUDA 12 runtime
   - May need NVIDIA GPU driver update

2. **VRAM insufficient?**
   - Try a lower tier (2 GB instead of 8 GB)
   - Close other GPU-intensive applications

3. **Incompatible GPU?**
   - NVIDIA GPUs with CUDA 12 support required
   - AMD GPUs not currently supported

### Database locked

**Causes:**

1. **Another Agent-X instance running**
   - Check Task Manager
   - End duplicate processes

2. **Backup in progress**
   - Wait for backup to complete

3. **File lock not released**
   - Restart your computer

---

## Still Have Questions?

| Resource | Link |
|----------|------|
| **User Guide** | [Comprehensive documentation](../comprehensive-user-guide.md) |
| **Troubleshooting** | [Detailed troubleshooting guide](../troubleshooting.md) |
| **GitHub Issues** | Report bugs or request features |
| **GitHub Discussions** | Community Q&A |

---

*Last updated: 2026-05-03*
