# Agent-X Video Tutorial Scripts

**Scripts for Agent-X tutorial videos**

---

## Overview

These scripts guide creation of tutorial videos demonstrating Agent-X features and workflows.

---

## Video 1: Quick Start (10 minutes)

**Target Audience:** New users, first-time Agent-X users

**Learning Objectives:**
- Install and launch Agent-X
- Create unlock passphrase
- Import first documents
- Run first AI chat query
- Perform semantic search

### Script

**[0:00-0:30] Intro**

**Visual:** Agent-X logo, desktop screen recording

**Audio:**
"Welcome to Agent-X, your local-first AI-powered document intelligence assistant. In the next 10 minutes, I'll show you how to transform your personal document collection into a queryable, AI-augmented knowledge base — all without your data ever leaving your machine."

---

**[0:30-2:00] Installation**

**Visual:** Download page, installer

**Audio:**
"First, download Agent-X from the releases page or the installer-output directory. Run the installer — it doesn't require administrator privileges by default.

The installer is about 500 MB for the application, plus it automatically downloads the bundled Llama 3.2 3B model — that's an additional 2 GB, giving you fully functional offline AI out of the box.

Installation takes about 2-3 minutes depending on your internet speed for the model download."

---

**[2:00-3:30] First Launch**

**Visual:** Unlock screen, passphrase creation

**Audio:**
"When you first launch Agent-X, you'll see the unlock screen. This is where database encryption happens. Create a secure passphrase — this encrypts your entire document vault with AES-256-CBC encryption.

Important: there's no password recovery. Your passphrase is the only way to access your data. Use a password manager to store it safely.

After entering your passphrase, Agent-X initializes the database and loads the bundled AI model."

---

**[3:30-5:00] Main Dashboard**

**Visual:** Dashboard overview with statistics cards

**Audio:**
"Welcome to the Agent-X dashboard. This is your intelligence hub.

At the top, you'll see statistics cards: total documents, conversations, tokens used, and tags. These update in real-time as you use Agent-X.

The left navigation gives you access to all major features: Dashboard, AI Chat, Knowledge Vault, Search, Knowledge Graph, Workflows, Analytics, Model Manager, and Settings.

Let's start by importing some documents."

---

**[5:00-7:00] Import Documents**

**Visual:** Knowledge Vault, Import dialog, progress

**Audio:**
"Navigate to Knowledge Vault using the left navigation or press Ctrl+L. Click the blue '+ Import Documents' button.

Select any documents — PDFs, Word files, text files, even code files. Agent-X supports 20+ formats. I'll select a few project documents and meeting notes.

Before importing, you can enable auto-title generation and auto-tagging. This uses AI to analyze your documents and generate descriptive titles and relevant tags automatically.

Click Import and watch as Agent-X processes each document: extracting text, generating embeddings, and analyzing content."

---

**[7:00-8:30] AI Chat**

**Visual:** AI Chat interface, query and response

**Audio:**
"Now let's chat with our documents. Click AI Chat in the left navigation or press Ctrl+I.

Type a question like 'What are the key milestones from the project plan?' and press Enter.

Agent-X uses RAG — Retrieval-Augmented Generation. It searches your documents, finds relevant content, and generates an answer grounded in your actual data. Notice the citation at the bottom — it tells you exactly which document and page the information came from.

You can ask follow-up questions, request clarifications, or explore related topics."

---

**[8:30-10:00] Semantic Search**

**Visual:** Search interface, results with excerpts

**Audio:**
"Let's explore semantic search. Click Search or press Ctrl+F.

Type a query — you don't need exact keywords. Try 'deployment checklist' and notice how Agent-X finds conceptually similar content even if those exact words don't appear.

The results show a match percentage and an excerpt. This is hybrid search — combining vector-based semantic search with keyword search for the best of both worlds.

You can filter by collection, tag, or date. Click any result to open the document at the relevant location."

---

**[10:00] Next Steps**

**Visual:** Documentation links, feature highlights

**Audio:**
"Congratulations! You've completed the Agent-X Quick Start. You now have:

- A fully functional local AI assistant
- Imported documents indexed for semantic search
- RAG-powered chat with your knowledge base
- Complete data privacy and security

Next steps: Explore the Knowledge Graph visualization, try GPU acceleration if you have an NVIDIA GPU, configure additional AI providers like Ollama, and check out the comprehensive documentation in the Help menu.

Thanks for watching, and happy exploring!"

---

## Video 2: Advanced RAG Features (12 minutes)

**Target Audience:** Users familiar with basics who want deeper understanding

**Learning Objectives:**
- Understand RAG pipeline components
- Configure retrieval settings
- Use HyDE for complex queries
- Leverage LLM reranking
- Optimize for accuracy vs speed

### Script Outline

**[0:00-1:00] Intro**
- What is RAG?
- Why Agent-X's RAG is enterprise-grade
- Video roadmap

**[1:00-3:00] RAG Pipeline Overview**
- 6-stage retrieval diagram
- Multi-query expansion
- HyDE embeddings
- LLM reranking
- Citation chaining

**[3:00-5:00] Configuration**
- Settings → AI Runtime
- Retrieval depth control
- Reranking toggle
- Trade-offs explained

**[5:00-7:00] Multi-Query Expansion**
- How it works
- When to enable
- Example query showing expanded retrieval

**[7:00-9:00] HyDE Demonstrated**
- Hypothetical document generation
- Improved retrieval quality
- Best use cases

**[9:00-11:00] LLM Reranking**
- Initial retrieval vs reranked
- Quality improvement examples
- Performance impact

**[11:00-12:00] Tips & Best Practices**
- Query formulation
- When to use which mode
- Balancing accuracy and speed

---

## Video 3: Knowledge Graph Visualization (8 minutes)

**Target Audience:** Users interested in visualizing document relationships

**Learning Objectives:**
- Understand graph nodes and edges
- Navigate the graph interface
- Use force-directed layout
- Identify document clusters
- Leverage graph for exploration

### Script Outline

**[0:00-1:00] Intro**
- What is the Knowledge Graph?
- Visualizing relationships
- Navigation overview

**[1:00-3:00] Graph Interface**
- Node types (documents, collections, tags)
- Edge meanings (membership, associations)
- Zoom and pan controls
- Legend explanation

**[3:00-5:00] Interaction**
- Click to open documents
- Highlight related nodes
- Filter by node type
- Search within graph

**[5:00-6:30] Layout Controls**
- Force-directed algorithm
- Iteration control
- Physics tuning
- Export as image

**[6:30-8:00] Use Cases**
- Finding related documents
- Discovering unexpected connections
- Tag cleanup visualization
- Collection planning

---

## Video 4: Workflows & Automation (10 minutes)

**Target Audience:** Power users, automation enthusiasts

**Learning Objectives:**
- Understand workflow system
- Create custom workflows
- Schedule recurring tasks
- Monitor workflow execution
- Troubleshoot workflows

### Script Outline

**[0:00-1:00] Intro**
- What are workflows?
- Common workflow examples
- Workflow benefits

**[1:00-3:00] Built-in Workflows**
- Weekly digest
- Batch reindex
- Tag cleanup
- Import and process

**[3:00-5:30] Creating Custom Workflows**
- Workflow editor interface
- Step types and configuration
- Variables and conditions
- Saving and organizing

**[5:30-7:00] Scheduling**
- Recurring workflow setup
- Triggers and conditions
- Error handling
- Notifications

**[7:00-8:30] Monitoring**
- Execution history
- Performance metrics
- Error logs
- Debugging failed runs

**[8:30-10:00] Advanced Examples**
- Multi-step processing pipeline
- Conditional branching
- External API integration
- Best practices

---

## Video 5: GPU Acceleration (7 minutes)

**Target Audience:** Users with NVIDIA GPUs

**Learning Objectives:**
- Understand GPU benefits
- Configure CUDA settings
- Optimize VRAM usage
- Troubleshoot GPU issues

### Script Outline

**[0:00-1:00] Intro**
- Why GPU matters for AI
- Speedup expectations
- Supported hardware

**[1:00-2:30] Setup**
- Verify CUDA detection
- Install GPU drivers
- Enable in settings
- VRAM tier selection

**[2:30-4:00] VRAM Tiers**
- 2 GB tier explanation
- 4 GB tier explanation
- 8 GB tier explanation
- Max tier (aggressive)

**[4:00-5:30] Performance**
- Benchmark comparisons
- CPU vs GPU vs different tiers
- Model size considerations
- Thermal management

**[5:30-7:00] Troubleshooting**
- GPU not detected
- Out of memory errors
- Fallback to CPU
- Performance tips

---

## Production Guidelines

### Recording Guidelines

- **Resolution:** 1920×1080 minimum
- **Frame rate:** 30fps
- **Format:** MP4 (H.264)
- **Audio:** Clear voiceover, minimal background music
- **Captions:** Include for accessibility

### Editing Guidelines

- **Zoom in:** On UI elements for clarity
- **Highlight:** Cursor with yellow circle
- **Text overlays:** Key shortcuts on screen
- **Chapters:** Include markers for navigation
- **Duration:** Keep focused, avoid filler

### Thumbnail Guidelines

- **Title:** Clear, bold text
- **Image:** Screenshot of feature
- **Branding:** Include Agent-X logo
- **Style:** Consistent across series

---

## Additional Video Topics

Future video ideas:

- **REST API Tutorial** — Programmatic access
- **Plugin Development** — Extending Agent-X
- **Cloud Provider Setup** — OpenAI, Anthropic integration
- **Migration from Legacy Tools** — Switching from Evernote, OneNote
- **Security Deep-Dive** — Encryption, keys, best practices

---

*Last updated: 2026-05-03*
