# Agent-X Video Tutorial Scripts

**Scripts for Agent-X tutorial videos**

---

## Overview

These scripts guide the recording of tutorial videos. Every step shows a page, button or
setting that exists in the app, named as the English interface names it; the
[Quick Start](../getting-started/quick-start.md) and the in-app **User Guide** cover the same
ground in writing. Record with the English interface unless a video is made for another
language.

---

## Video 1: Quick Start (10 minutes)

**Target Audience:** New users

**Learning Objectives:**
- Install Agent-X and finish the setup wizard
- Choose where the AI runs
- Import documents into the Knowledge Vault
- Ask a question about them in Ask Your Files
- Use AI Chat and Semantic Search

### Script

**[0:00-0:30] Intro**

**Visual:** Agent-X Dashboard, then the desktop

**Audio:**
"Welcome to Agent-X, a Windows app for working with your own documents and an AI model. By
default the model runs on your computer, so your documents and questions stay on it. In the
next ten minutes we'll install it, import a few documents and ask questions about them."

---

**[0:30-1:30] Installation**

**Visual:** GitHub Releases page, installer wizard

**Audio:**
"Download the installer from the Releases page on GitHub. The standard installer is small
and downloads the built-in AI model on first run; the offline installer, linked from the
release notes, already contains it.

Setup asks whether to install just for you, which needs no administrator rights, or for all
users. Leave Launch Agent-X checked on the last page."

---

**[1:30-4:00] The setup wizard**

**Visual:** The five wizard steps, one after the other

**Audio:**
"On first run, Agent-X opens a short setup wizard. There's no password to create.

Step two connects to Ollama, a free program for running open models. You don't need it:
if you don't use Ollama, click Skip for now.

Step three lists your Ollama models, if the connection worked. Otherwise it shows the
defaults and you can move on.

Step four is the important one: the built-in model, Llama 3.2 3B. If it isn't installed yet,
click Download built-in model. It's about 1.9 gigabytes, and once it's downloaded it becomes
your AI provider. Below it, Agent-X says whether the model will run on your processor or on an
NVIDIA graphics card. You can also paste an OpenAI or Anthropic API key here, but you don't
have to.

The last step summarizes your choices. Click Launch Agent-X."

---

**[4:00-5:00] The Dashboard and providers**

**Visual:** Dashboard, then Settings > AI Providers

**Audio:**
"This is the Dashboard. The top card shows which AI provider is active and whether it's
reachable, and the navigation rail on the left leads to every page.

To change where the AI runs, open Settings with Control plus comma. Under AI Providers,
choose the Active Provider: the built-in model, Ollama, OpenAI or Anthropic. Then click Save
Settings at the bottom."

---

**[5:00-6:30] Import documents**

**Visual:** Knowledge Vault, Import Files, badges changing to Indexed

**Audio:**
"Open the Knowledge Vault from the rail, or press Control plus I. Click Import Files and pick
a few documents: PDFs, Word files, Markdown, text or code. Import Folder brings in a whole
folder with its subfolders.

Agent-X keeps the text and a link to each file; your files stay where they are. Each document
is indexed in the background, and its badge goes from Pending to Processing to Indexed. After
indexing, the AI model adds a few tags."

---

**[6:30-8:00] Ask Your Files**

**Visual:** Ask Your Files, a question, the answer streaming, the Sources panel

**Audio:**
"Now let's ask about those documents. Open Ask Your Files, or press Control plus 3. Type a
question and press Enter.

Agent-X finds the passages that match the question best, gives them to the AI model, and the
answer comes back with numbered citations. The Sources panel on the right shows each cited
passage with its document and page. Open source document shows the file in File Explorer."

---

**[8:00-9:15] AI Chat and Semantic Search**

**Visual:** AI Chat with an empty conversation, then Semantic Search

**Audio:**
"AI Chat, on Control plus 2, is a general conversation with the model. Notice the line under
Start a Conversation: it tells you where your messages go. With a local model they stay on
this computer. Chat doesn't search your documents; for that, use Ask Your Files.

Semantic Search, on Control plus F, finds passages by meaning. Try a phrase that isn't in the
documents word for word. Switch the search mode to Keyword for exact names, or Hybrid for
both."

---

**[9:15-10:00] Next Steps**

**Visual:** Rail, Command Palette, User Guide

**Audio:**
"That's the basics. From here, try Collections to group documents, Compare Documents, Quick
Actions for summaries, and Workflows. Press Control plus K for the Command Palette, and F1 for
every keyboard shortcut. The User Guide at the end of the rail explains every page.

Thanks for watching."

---

## Video 2: Inside Ask Your Files (12 minutes)

**Target Audience:** Users who know the basics and want better answers from their documents

**Learning Objectives:**
- Understand how Ask Your Files finds and uses passages
- Scope questions with collections
- Tune the settings that affect retrieval
- Read and check citations

### Script Outline

**[0:00-1:00] Intro**
- What Retrieval-Augmented Generation (RAG) means: the answer is written from passages
  retrieved from your documents
- Ask Your Files versus AI Chat: only Ask Your Files reads the vault

**[1:00-3:30] How a question is answered**
- The question is rephrased a few times by the AI model (multi-query), so different wordings
  are searched
- Questions of 80 characters or more also get a drafted hypothetical answer (HyDE) that is
  searched as well
- Each version runs a semantic search and a keyword (FTS5) search, merged with Reciprocal
  Rank Fusion (k=60)
- The passages are deduplicated and reranked (first a heuristic pass that spreads results
  across documents, then a pass by the AI model), expanded with neighboring text and
  compressed
- The answer streams in; each `[n]` that matches a passage becomes a card in **Sources**
- Point out the cost: the rephrasings, the hypothetical answer and the model-based reranking
  are extra model calls, which a cloud provider bills

**[3:30-5:30] Scoping with collections**
- Create a collection on the **Collections** page and add documents with **Add Documents**
- Pick it in the collection list at the top of Ask Your Files; **All Collections** searches
  everything
- Show the same question with and without a scope

**[5:30-8:00] Settings that matter**
- **Settings > Knowledge Vault > Top-K Results** (5 by default): how many passages the answer
  can use
- **Chunk Size (tokens)** and **Chunk Overlap**: how documents are split; re-index documents
  after changing them
- The **Embedding Model** in the **Ollama (Local)** card: with the default, the built-in model
  embeds documents when it is installed, otherwise Ollama's `all-minilm`; change the embedding
  model only together with a re-index
- The chat model answering the question is the active provider's model

**[8:00-10:30] Reading citations**
- The **Sources** panel: number, document, page (for formats with pages), match percentage,
  excerpt
- **Open source document** opens File Explorer at the file; it does not jump to the passage
- Numbers in the answer with no matching passage are ignored
- Demonstrate checking an answer against the cited page

**[10:30-12:00] Tips and Best Practices**
- Use the words your documents use
- Ask one thing per question
- When the answer says the passages are not enough, narrow the question or the collection
- For a whole-document summary, use Quick Actions > **Summarize** instead

---

## Video 3: Knowledge Graph Visualization (8 minutes)

**Target Audience:** Users who want to see how their documents relate

**Learning Objectives:**
- Understand the graph's nodes and edges
- Select nodes and highlight clusters
- Filter, search and zoom
- Use the graph to spot structure and gaps

### Script Outline

**[0:00-1:00] Intro**
- The Knowledge Graph draws the vault as a network (`Ctrl+G`)
- It is built from collections and tags, not from how similar document contents are

**[1:00-3:00] Graph elements**
- Node types: documents, collections and tags, each in its color in the **LEGEND**
- Document nodes are sized by chunk count
- Edges join a document to its collections and tags, and two documents that share a
  collection or tag (thicker when they share more)

**[3:00-5:00] Interaction**
- Hover a node for its name, type and connections; click it for **Node Details**
- Click a collection or tag to highlight its cluster (**Cluster Active**); click again or use
  the clear button to reset
- **Search nodes...** highlights matching nodes
- The **SHOW** check boxes hide or show documents, collections and tags
- Nodes do not open documents

**[5:00-6:30] Layout and zoom**
- The layout is a force-directed simulation of 100 steps, run when the graph is built
- Zoom with the mouse wheel or **Zoom In** and **Zoom Out** (0.25x to 4x), and go back with
  **Reset Zoom**; the view cannot be panned
- **Refresh** rebuilds the graph from the current vault

**[6:30-8:00] Use Cases**
- Finding documents that belong together
- Spotting documents with no collection or tag
- Checking a collection structure after reorganizing (see Quick Actions > **Organize**)

---

## Video 4: Workflows (10 minutes)

**Target Audience:** Users who repeat the same multi-step AI tasks

**Learning Objectives:**
- Run a built-in workflow template
- Copy and customize a template
- Build a workflow with different step types
- Reuse results and share workflows

### Script Outline

**[0:00-1:00] Intro**
- A workflow runs a fixed sequence of steps on text you give it; each step's output feeds the
  next
- Workflows run when you click **Run**; there are no schedules or triggers

**[1:00-3:00] Built-in templates**
- **Summarize & Act**, **Research Brief**, **Document Review**, **Content Repurpose**
- **Select Template**, the **Template Guide** (Best For, You'll Get, Try It With)
- Paste text into **Input**, click **Run**, watch the **Step Outputs** and the
  **Final Output**

**[3:00-5:30] Customizing**
- **Use Template** copies a template into your own workflow in the **Workflow Editor**
- **Add Step**, **Move Up**, **Move Down**, **Remove Step**, **Save Workflow**
- Prompt placeholders: `{{input}}` and `{{previous_output}}`

**[5:30-7:30] Step types**
- `AiPrompt`: a prompt to the active model
- `DocumentLookup`: a question to your Knowledge Vault, optionally limited to one collection
- `TextTransform`, `ConditionalBranch` and `OutputFormat`: text operations without AI,
  configured in **Step settings (JSON)**, which the editor checks as you type

**[7:30-9:00] Results and history**
- **Copy to Clipboard**, **Save as Document** (into the Knowledge Vault), **Export Result**
- **Recent Runs** keeps earlier results to reopen without running again
- The **Workflow** button in the Knowledge Vault and in Semantic Search results starts a
  workflow from a document or a result

**[9:00-10:00] Sharing**
- **Export JSON** copies a workflow to the clipboard
- **Import Workflow** pastes it into another installation

---

## Video 5: GPU Acceleration for the Built-in Model (7 minutes)

**Target Audience:** Users with an NVIDIA graphics card

**Learning Objectives:**
- Check what hardware Agent-X detected
- Understand what automatic GPU layers do
- Set the layer count by hand
- Troubleshoot a GPU that is not used

### Script Outline

**[0:00-1:00] Intro**
- The built-in model can run part of its layers on an NVIDIA GPU; the rest runs on the CPU
- These settings apply to the built-in model only; Ollama manages its own GPU use

**[1:00-2:30] What Agent-X detected**
- The setup wizard's line under the built-in model, for example "CUDA acceleration available"
  or "GPU acceleration needs the NVIDIA CUDA 12 Toolkit"
- The **Hardware Advisor** page: graphics card, video memory, processor, RAM, a performance
  tier and recommended models

**[2:30-4:00] Requirements**
- An NVIDIA GPU with at least 2 GB of video memory
- The NVIDIA CUDA 12 Toolkit installed (Agent-X does not ship the CUDA runtime); without it,
  the model runs on the CPU

**[4:00-5:30] Settings**
- **Settings > AI Providers > Built-in LLM (Local)**
- **Automatic GPU layers** (on by default): 16 layers for 2 to 4 GB of video memory, 28 for 4
  to 6 GB, 33 for 6 GB or more, none below 2 GB
- Turn it off to set **GPU Layers** yourself; 0 keeps the model on the CPU
- **Save Settings** reloads the built-in model; no restart needed

**[5:30-7:00] Troubleshooting**
- GPU not used: check the toolkit is installed (the `CUDA_PATH` environment variable points to
  it), then restart Agent-X
- Out of video memory: lower **GPU Layers**
- Compare response speed with **GPU Layers** at 0 and with automatic layers

---

## Production Guidelines

### Recording Guidelines

- **Resolution:** 1920x1080 minimum
- **Frame rate:** 30fps
- **Format:** MP4 (H.264)
- **Audio:** Clear voiceover, minimal background music
- **Captions:** Include for accessibility

### Editing Guidelines

- **Zoom in:** On UI elements for clarity
- **Highlight:** Cursor with a visible circle
- **Text overlays:** Key shortcuts on screen
- **Chapters:** Include markers for navigation
- **Duration:** Keep focused, avoid filler

### Thumbnail Guidelines

- **Title:** Clear, bold text
- **Image:** Screenshot of the feature
- **Branding:** Include the Agent-X logo
- **Style:** Consistent across the series

---

## Additional Video Topics

Future video ideas:

- **Browser extension and the Smart Inbox**: pairing with the token from Settings >
  Connections, clipping pages, triage
- **Plugin development**: building a document processor from the sample plugin
- **Cloud provider setup**: OpenAI and Anthropic keys, and where content goes with each
- **Web Import**: URLs, YouTube videos, feeds and sitemaps
- **Security and backups**: database encryption, DPAPI-protected keys, encrypted backups and
  restore
