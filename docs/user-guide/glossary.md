# Agent-X Glossary

**Terminology and definitions**

Names in **bold** inside a definition are the labels the app shows.

---

## A

| Term | Definition |
|------|------------|
| **AES-256** | Advanced Encryption Standard with 256-bit keys. SQLCipher uses it (in CBC mode) for database encryption; password-protected backups and Collaborative Sync packages use AES-256-GCM. |
| **Agent-X** | Local-first AI document intelligence application for Windows. |
| **AI Title** | A short title the active AI model suggests for a document, on demand (**AI Title** on a Knowledge Vault row, **Generate Title** in the Document Preview). It is shown under the file name; the file itself is not renamed. |
| **Annotation** | A highlight on a passage of a vault document, with an optional note. Made by selecting text in the Document Preview's DOCUMENT TEXT section; listed, edited (note and color) and exported to Markdown on the Annotations page. Colors: yellow (the default), green, blue, red, purple. |
| **Anthropic** | AI company providing Claude models. Listed as **Anthropic Claude** under Settings > AI Providers; needs an API key. |
| **API Key** | Secret that authenticates you to a cloud service (OpenAI, Anthropic, Brave Search, Serper). Agent-X stores it in `settings.json`, encrypted with Windows DPAPI for your account. |
| **API Token** | The bearer token of the local API, shown masked under Settings > Connections. The browser extension and the Android companion need it; **Regenerate** revokes the old token at once. |
| **Application Settings** | Preferences stored in `%LocalAppData%\AgentX\settings.json`, edited on the Settings page. |
| **Ask Your Files** | The Retrieval-Augmented Generation page: it answers a question from passages of your indexed documents, with numbered citations. |
| **Attribution** | Naming the source a statement came from; in Agent-X, the numbered citations of an answer. |
| **Auto-Routing** | **Enable Auto-Routing** under Settings > Multi-Model Routing: each chat reply goes to a provider picked from the task type and the **Routing Profile** (Cost Optimized, Quality Optimized or Balanced). With a cloud API key set, replies can go to that cloud provider. Off by default. |
| **Auto-Tagging** | After a document is indexed, the active AI model suggests up to five tags from its first 2,000 characters. |

## B

| Term | Definition |
|------|------------|
| **Backup** | A `.agentxbak` file (a ZIP archive) made on the Backup & Restore page. It holds the database and, with **Include indexed documents**, the pages saved by Web Import; it can be protected with a password. Settings, API keys, models and plugins are not included. |
| **Batch Import** | Importing several files at once: multi-select in Import Files, Import Folder, or dropping files and folders on the Knowledge Vault page. |
| **BM25** | The ranking function SQLite FTS5 uses for keyword search. Keyword scores are shown relative to the best hit. |
| **Built-in Model** | **Built-in LLM (Local)**, the default AI provider: Llama 3.2 3B Instruct in GGUF format (Q4_K_M, about 2 GB), run on your computer by LLamaSharp. The OFFLINE installer includes it; with the SLIM installer it is downloaded on first run. When installed it also produces the embeddings, unless you chose another Embedding Model. |

## C

| Term | Definition |
|------|------------|
| **Chat** | **AI Chat**, the page where you talk to the active AI model. |
| **Chunking** | Splitting a document into overlapping passages for embedding and retrieval: **Chunk Size (tokens)** 512 and **Chunk Overlap** 50 by default, under Settings > Knowledge Vault. The overlap must be smaller than the chunk size. |
| **Citation** | A numbered reference such as [1] in an answer. Ask Your Files lists its sources in the Sources panel; Research Mode answers list their web sources as numbered chips. |
| **Cloud Provider** | An AI service reached over the internet with an API key: OpenAI or Anthropic. |
| **Collaborative Sync** | Syncing between your Agent-X installations through encrypted `.axs` package files in a shared folder (a network share, OneDrive, Dropbox, a NAS or a USB drive). No server or account. |
| **Collection** | A user-made group of vault documents. A document can be in several Collections; Collections nest one level deep (**Move into...**). |
| **Command Palette** | `Ctrl+K`: every page on the navigation rail, three actions (New Conversation, Import Files, Toggle Theme) and the current page's shortcuts. |
| **Context Inspection** | The AI Chat panel opened with **Inspect Context**: what was assembled for a reply, the conversation's Durable Summary, recalled messages, and the **Memories** card. |
| **Context Window** | The most tokens a model can consider at once. **Context Window** under Settings > Inference (8,192 by default) limits how much conversation is assembled for a reply. |
| **Conversation** | A chat thread with its messages, saved in the local database. |
| **Cosine Similarity** | A measure of how close two embedding vectors point, from -1 to 1. Semantic Search shows it as a relevance percentage. |
| **CUDA** | NVIDIA's GPU computing platform. The built-in model offloads layers to an NVIDIA GPU only when the NVIDIA CUDA 12 Toolkit is installed. |

## D

| Term | Definition |
|------|------------|
| **Data Directory** | `%LocalAppData%\AgentX\`, where the database, `settings.json`, models, logs and other data files live. |
| **Database** | `agentx.db`, a SQLite database holding documents' text, chunks and embeddings, conversations, memories and everything else Agent-X records. It can be encrypted with SQLCipher. |
| **Dense Retrieval** | Vector-based search with embeddings; Semantic mode on the Search page. |
| **Document** | A file imported into the Knowledge Vault. The vault keeps its extracted text and the path to the original file. |
| **DPAPI** | Windows Data Protection API. Agent-X uses it, for the current Windows user, to encrypt the secrets in `settings.json`, the stored Google and Microsoft sign-in tokens, and the database encryption key. |
| **Draft As Me** | On the Past Self page: the active AI provider writes a draft from your description, guided by your measured writing style and the views you had recorded by the chosen time. |
| **Durable Summary** | A stored summary of a long conversation, refreshed by the active AI model and used when assembling context. Shown in Context Inspection and on the Analytics page. |

## E

| Term | Definition |
|------|------------|
| **Embedding** | A vector of numbers that represents the meaning of a piece of text. |
| **Embedding Model** | The model that turns chunks and queries into embeddings, set under Settings > AI Providers > Ollama (Local). With the default (`all-minilm`) Agent-X uses the built-in model when it is installed and Ollama's `all-minilm` otherwise; an OpenAI `text-embedding-` model sends the text to OpenAI. |
| **Encryption** | Database encryption (Settings > Database Encryption, off by default, cannot be turned off once on), backup passwords, and the Collaborative Sync **Encryption Key**. |
| **Export** | Writing data to a file: a conversation (**Export conversation**: Markdown, HTML, PDF, JSON, plain text, CSV, Word or PowerPoint), all conversations, a Collection, annotations, or a comparison report. |

## F

| Term | Definition |
|------|------------|
| **FAQ** | Frequently Asked Questions. |
| **Feature Flag** | An internal on/off switch. Three exist (auto-tagging, search caching, duplicate detection), all on, with no page to change them. |
| **Filter** | A rule that narrows a list, such as the Knowledge Vault's file type, status, tag, Collection and date filters. |
| **Fine-Tuning** | Adapting a pre-trained model to specific tasks. Not supported in Agent-X. |
| **Folder** | A label for conversations in AI Chat: Work, Research, Personal, Archive or a name you type. Not to be confused with a Watch Folder. |
| **Force-Directed Graph** | The Knowledge Graph's layout: a 100-step simulation in which linked nodes pull together and all nodes push apart. |
| **FTS5** | SQLite's full-text search extension, used for keyword search. |

## G

| Term | Definition |
|------|------------|
| **GGUF** | The model file format of llama.cpp. The built-in model's files (`.gguf`) are kept in `%LocalAppData%\AgentX\Models`. |
| **GPU** | Graphics Processing Unit, used to speed up model inference. |
| **GPU Layers** | How many layers of the built-in model run on the GPU. **Automatic GPU layers** (on by default) gives an NVIDIA GPU 16, 28 or 33 layers by its video memory (none below 2 GB); turned off, **GPU Layers** takes 0 to 999, where 0 keeps the model on the CPU. |
| **Grounding** | Basing an answer on retrieved passages from your documents. |

## H

| Term | Definition |
|------|------------|
| **Hardware Advisor** | The page that reads your GPU, processor, memory and NPU and suggests Ollama models that fit. |
| **HNSW** | Hierarchical Navigable Small World, an approximate nearest-neighbour index. Agent-X uses it for vaults with more than about 10,000 embedded chunks and scans smaller ones exactly. |
| **Hybrid Search** | Semantic and keyword search run together and merged with Reciprocal Rank Fusion. |
| **HyDE** | Hypothetical Document Embeddings: Ask Your Files has the model draft a hypothetical answer to questions of 80 characters or more and searches with it too. |

## I

| Term | Definition |
|------|------------|
| **Import** | Adding files to the Knowledge Vault: Import Files, Import Folder, drag and drop, watch folders, Web Import, or accepting Smart Inbox items. |
| **Index** | A data structure that makes search fast: the vector index and the FTS5 keyword index. |
| **Indexing** | The background work after an import: extract the text, split it into chunks, embed them, add them to the keyword index, then auto-tag the document. |
| **Inference** | Running a model to produce output. |
| **Insight** | A moment Temporal Identity keeps: an AI reply with marker words such as "key insight", or a new annotation. Listed on the Past Self page by **Get Relevant Insights**. |
| **Instruct Model** | A model tuned to follow instructions, such as Llama 3.2 3B Instruct. |

## J

| Term | Definition |
|------|------------|
| **Jump To** | `Ctrl+P`: open a page, a document or a conversation by name. |

## K

| Term | Definition |
|------|------------|
| **KB** | Knowledge Base: the collection of indexed documents. |
| **Keyword Search** | FTS5 full-text search for exact words, names and codes. |
| **Knowledge Graph** | The page that draws documents, Collections and tags as nodes, with lines for membership and for documents that share a Collection or tag. |
| **Knowledge Vault** | The page that lists and manages imported documents. |

## L

| Term | Definition |
|------|------------|
| **Llama** | Meta's family of open-weight language models. |
| **LLamaSharp** | The .NET library that runs the built-in model (llama.cpp). |
| **LLM** | Large Language Model: an AI model trained on large amounts of text. |
| **Local API** | The HTTP service Agent-X runs on `http://localhost:9846` for the browser extension and the Android companion. It answers only on this computer and needs the API Token. **Enable Local API** is under Settings > Connections. |
| **Local-First** | Design that keeps processing and data on your device, using the network only for services you set up. |
| **LOCAL/NET Lamp** | The status strip lamp that reads NET when the privacy check (the same one behind the Dashboard's privacy line) finds a service that sends data off the computer, and LOCAL otherwise. |
| **Log** | The daily log files in `%LocalAppData%\AgentX\Logs`, kept for seven days. |

## M

| Term | Definition |
|------|------------|
| **Markdown** | Lightweight markup language for formatted text. |
| **Memory** | A fact Agent-X noted from your chats with a background model call after a reply. Up to eight that closely match a new message are added to the model's context. The **Memories** card in Context Inspection lists them, deletes one, or clears all. |
| **Metadata** | Data about data: file size, type, import date, tags and so on. |
| **Model** | An AI system that generates text or embeddings. |
| **Model Manager** | The page that lists, downloads, removes and activates the active provider's models, and installs the speech-to-text model. |
| **Multi-Query** | Ask Your Files asks the model for rephrasings of your question and searches each one. |

## N

| Term | Definition |
|------|------------|
| **Neural Network** | Machine learning model made of layers of connected units. |
| **Node** | A point in the Knowledge Graph: a document, Collection or tag. |
| **Notification** | A message that appears over the page for a moment. |
| **NPU** | Neural Processing Unit. The Hardware Advisor reports whether one is present; Agent-X does not run models on it. |

## O

| Term | Definition |
|------|------------|
| **OAuth App Credentials** | The section of the Calendar and Email pages where you enter your own Google and Microsoft OAuth client, which connecting an account requires. |
| **Ollama** | A local model server. Agent-X can use its models as **Ollama (Local)** (default address `http://localhost:11434`). |
| **OpenAI** | AI company providing GPT models. Listed as **OpenAI** under Settings > AI Providers; needs an API key. |
| **Operations** | The page (`Ctrl+Shift+O`) that shows the status of conversation summaries, sync, the ingestion backlog, workflows and connectors, with suggested fixes. |
| **Orchestration** | Coordinating several model calls for one answer; the **Multi** and **Debate** modes of AI Chat. |

## P

| Term | Definition |
|------|------------|
| **Passphrase** | A secret you type. The Collaborative Sync **Encryption Key** is one. Databases encrypted by earlier versions with a passphrase ask for it at startup; encryption turned on now uses a key tied to your Windows account instead. |
| **Past Self** | The page that answers what you thought about a topic at a chosen time, from the record Temporal Identity keeps, and hosts Draft As Me. |
| **PBKDF2** | Password-Based Key Derivation Function, which turns a password into an encryption key (backup passwords, the sync Encryption Key). |
| **PII Redaction** | Ask Your Files replaces e-mail addresses, phone numbers, social security and credit card numbers, API keys and IP addresses in retrieved passages before any model sees them. |
| **Plugin** | A `.agentx-plugin` package installed on the Plugin Manager page. Plugins can add file formats and push items into the Smart Inbox; they are not sandboxed. |
| **Prompt** | Input text provided to an AI model. |
| **Provider** | Where AI models come from: **Built-in LLM (Local)**, **Ollama (Local)**, **OpenAI** or **Anthropic Claude**, chosen under **Active Provider** in Settings. |

## Q

| Term | Definition |
|------|------------|
| **Query** | Text you search with. |
| **Question Answering** | Answering a natural-language question, as Ask Your Files does from your documents. |
| **Quick Actions** | The page that summarizes a document, extracts key points, translates pasted text, scans for duplicates and suggests Collections and tags. |
| **Quick Chat** | A small window for a one-off question, opened with `Win+Shift+A` or from the tray icon. |

## R

| Term | Definition |
|------|------------|
| **RAG** | Retrieval-Augmented Generation: answers grounded in retrieved documents (Ask Your Files). |
| **Recall** | In search, the share of relevant results that are found. |
| **Re-index** | Reading a document's file again and rebuilding its chunks, embeddings and keyword entries. |
| **Reranking** | Re-ordering retrieved passages: Ask Your Files spreads them across documents, then has the model score their relevance. |
| **Research Mode** | Web search results added to AI Chat answers. Needs **Enable Research Mode** in Settings, a search provider (Brave, Serper or SearXNG) and the globe button in the chat. |
| **Rolling Window** | A fixed-size span of recent items, such as the last seven days of logs. |
| **RRF** | Reciprocal Rank Fusion (k=60), which merges the semantic and keyword result lists in Hybrid mode. |
| **Runtime** | The environment a model runs in (CPU or GPU). |

## S

| Term | Definition |
|------|------------|
| **Saved Filters** | Searches saved on the Search page with their mode, advanced settings and sort order. |
| **Semantic Search** | Vector-based search that finds text by meaning. Also the name of the search page. |
| **Settings** | The page for AI providers, appearance and language, model routing, cost tracking, inference, the Knowledge Vault, Research Mode, storage, database encryption and the local API. |
| **Smart Inbox** | The triage queue for pages clipped with the browser extension; you accept, defer or reject each one. Calendar and email items are accepted into the vault automatically and appear under the accepted status; a plugin connector can add items either for review or as already accepted. |
| **Sparse Retrieval** | Keyword-based search that matches exact terms. |
| **Speech-to-Text Model** | The Whisper base model (about 142 MB) that transcribes imported audio and voice input on your computer. Installed with **Download** on the Model Manager page. |
| **SQLCipher** | The SQLite build that encrypts the whole database file with AES-256. |
| **SQLite** | Embedded SQL database engine used by Agent-X. |
| **Streaming** | Showing an answer as it is generated. |
| **Summary** | A condensed version of longer content, such as a Quick Actions layered summary or a conversation's Durable Summary. |
| **System Prompt** | Instructions that set the model's behavior. AI Chat offers ten built-in ones, such as Code Helper and Summarizer. |

## T

| Term | Definition |
|------|------------|
| **Tag** | A label on a document, assigned by auto-tagging. |
| **Temperature** | How varied the model's wording is: **Temperature** under Settings > Inference, 0 to 2, 0.7 by default. Anthropic receives at most 1, and some newer models (such as Claude Sonnet 5 and OpenAI's reasoning models) are sent no temperature at all. |
| **Template** | A predefined structure: the four built-in workflow templates, or the three Markdown templates of conversation export. |
| **Temporal Identity** | Agent-X's local record of beliefs you state in chat, insights, your writing voice and reading time, read by Past Self and the Dashboard. |
| **Token** | The unit of text models work in; roughly four characters of English text. |
| **Tokenization** | Splitting text into tokens for a model. |
| **Top-K Results** | How many passages Ask Your Files keeps for an answer: 5 by default, 1 to 20, under Settings > Knowledge Vault. |
| **Transformer** | The neural network architecture of modern LLMs. |
| **Troubleshooting** | Systematic approach to diagnosing and resolving issues. |

## V

| Term | Definition |
|------|------------|
| **Vector** | A list of numbers; an embedding. |
| **Vector Store** | Where embeddings are kept: the `vec_embeddings` table in the database, searched exactly or through the HNSW index. |
| **Visualization** | Graphical representation of data or relationships. |
| **Voice Profile** | Your measured writing style (average sentence length and a formality score) from your chat messages, shown on Past Self and used by Draft As Me. |
| **VRAM** | Video RAM, the GPU's own memory. |

## W

| Term | Definition |
|------|------------|
| **Watch Folder** | A folder listed under Settings > Knowledge Vault > Watch Folders. While **Auto-index watch folders** is on, Agent-X imports its supported files, and new or changed ones, straight into the vault. |
| **Web Import** | The page that imports web pages, YouTube transcripts, the items of an RSS or Atom feed, or up to 100 pages from a sitemap. |
| **Whisper** | OpenAI's open speech recognition model; Agent-X runs its base size locally. |
| **Windows App SDK** | Microsoft framework for building Windows applications (formerly Project Reunion). |
| **WinUI 3** | Native UI platform for Windows 11 and Windows 10. |
| **Workflow** | A multi-step AI text pipeline built on the Workflows page from five step types (AI Prompt, Document Lookup, Text Transform, Conditional Branch, Output Format) and run when you start it. |
| **Workspace Profile** | A saved record of a name, description, Ollama model name, Collection IDs and notes. Profiles are not applied to the app. |

---

## Keyboard Shortcuts Reference

| Shortcut | Action |
|----------|--------|
| `Ctrl+K` | Open the Command Palette |
| `Ctrl+P` | Jump To (a page, document, or conversation) |
| `F1` | Keyboard Shortcuts dialog |
| `Ctrl+N` | New conversation |
| `Ctrl+2` | Open AI Chat |
| `Ctrl+I` | Open Knowledge Vault |
| `Ctrl+F` | Open Semantic Search |
| `Ctrl+G` | Open Knowledge Graph |
| `Ctrl+Shift+W` | Open Workflows |
| `Ctrl+Shift+A` | Open Analytics |
| `Ctrl+8` | Open Model Manager |
| `Ctrl+,` | Open Settings |
| `Win+Shift+A` | Quick Chat |
| `F5` | Refresh the document list (Knowledge Vault) |
| `Ctrl+S` | Save settings (Settings page) |
| `Ctrl+C` | Copy selected text |
| `Ctrl+V` | Paste text |
| `Ctrl+X` | Cut selected text |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` | Redo |
| `Ctrl+A` | Select all text in a text box |
| `Escape` | Close the Command Palette, Jump To or a dialog |
| `Enter` | Send a chat message, run a search, or open the selected palette item |

Shortcuts are fixed and cannot be remapped. See [Keyboard Shortcuts](keyboard-shortcuts.md) for the full list.

---

*Last updated: 2026-09-27*
