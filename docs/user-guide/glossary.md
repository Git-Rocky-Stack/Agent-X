# Agent-X Glossary

**Terminology and definitions**

---

## A

| Term | Definition |
|------|------------|
| **AES-256-CBC** | Advanced Encryption Standard with 256-bit keys in Cipher Block Chaining mode. Used by Agent-X for at-rest database encryption. |
| **Agent-X** | Local-first AI-powered document intelligence application for Windows. |
| **Anthropic** | AI company providing Claude models. Configurable as a cloud provider in Agent-X. |
| **API Key** | Authentication token used for accessing cloud AI provider services. Stored encrypted in Windows Credential Manager. |
| **Application Settings** | User preferences stored in `%LocalAppData%\AgentX\settings.json`. |
| **Async** | Asynchronous operations that run in the background without blocking the UI. |
| **Attention** | Mechanism in transformer models that allows focusing on relevant parts of the input. |
| **Attribution** | Citation of source documents used in AI-generated responses. |
| **Auto-Tagging** | Automatic assignment of tags to documents based on AI analysis of content. |
| **Auto-Titling** | Automatic generation of descriptive document titles based on content analysis. |

## B

| Term | Definition |
|------|------------|
| **Bundled Model** | Llama 3.2 3B Instruct model included with Agent-X installation (~2 GB). |
| **Batch Import** | Importing multiple documents simultaneously. |
| **Chunking** | Splitting large documents into smaller segments for embedding and retrieval. |
| **CUDA** | NVIDIA's parallel computing platform. Agent-X uses CUDA 12 for GPU acceleration. |
| **Collection** | User-defined grouping of related documents for organization. |

## C

| Term | Definition |
|------|------------|
| **Chat** | Conversational interface for interacting with AI models. |
| **Citation** | Reference to a specific source document used in AI response generation. |
| **Cloud Provider** | External AI service (OpenAI, Anthropic) accessible via API. |
| **Clipboard** | System buffer for copy/paste operations. |
| **Context Window** | Maximum number of tokens an AI model can consider in a single request. |
| **Conversation** | Chat session containing user messages and AI responses. |
| **Conversation Memory** | Extracted facts and topics from conversations for persistent context. |
| **Cosine Similarity** | Metric for measuring similarity between vector embeddings (0-1 range). |
| **CUDA** | Parallel computing platform and API model created by NVIDIA. |

## D

| Term | Definition |
|------|------------|
| **Database** | SQLite database storing documents, embeddings, tags, and metadata. |
| **Data Directory** | `%LocalAppData%\AgentX\` — location of all Agent-X data files. |
| **Debug Log** | Detailed logging output for troubleshooting. Located in `logs/` subdirectory. |
| **Dense Retrieval** | Vector-based semantic search using embeddings. |
| **Dialog** | Modal window for user interaction (confirmations, settings, etc.). |
| **Document** | Imported file (PDF, DOCX, TXT, etc.) stored and indexed by Agent-X. |
| **DPAPI** | Windows Data Protection API for secure credential storage. |

## E

| Term | Definition |
|------|------------|
| **Embedding** | Vector representation of text capturing semantic meaning. |
| **Encryption** | Process of encoding data to prevent unauthorized access. |
| **Entity** | Named object in the system (document, tag, collection, conversation). |
| **Event Log** | Append-only record of system events for audit trail. |
| **Extractor** | Component that extracts text content from various file formats. |

## F

| Term | Definition |
|------|------------|
| **FAQ** | Frequently Asked Questions. |
| **Feature Flag** | Toggle for enabling/disabling experimental features. |
| **File Vault** | Secure storage for imported documents and generated content. |
| **Filter** | Rule for restricting displayed items based on criteria. |
| **Fine-Tuning** | Process of adapting a pre-trained model to specific tasks (not supported in v1). |
| **Folder** | Organizational container for conversations (Work, Research, Personal). |
| **FTS5** | SQLite Full-Text Search extension for keyword search. |
| **Force-Directed Graph** | Knowledge graph visualization using spring-electric physics simulation. |

## G

| Term | Definition |
|------|------------|
| **GPU** | Graphics Processing Unit. Used for accelerated AI inference. |
| **GPU Acceleration** | Using GPU instead of CPU for AI model computations. |
| **Graph** | Network visualization showing relationships between documents, tags, and collections. |
| **Grounding** | Basing AI responses in retrieved factual context from documents. |

## H

| Term | Definition |
|------|------------|
| **HyDE** | Hypothetical Document Embeddings — retrieval technique using AI-generated ideal answers. |
| **Hybrid Search** | Combined semantic and keyword search with merged results. |
| **Hierarchy** | Organizational structure (collections → documents → tags). |
| **HNSW** | Hierarchical Navigable Small World — efficient ANN (Approximate Nearest Neighbor) index. |

## I

| Term | Definition |
|------|------------|
| **Import** | Process of adding external files to Agent-X knowledge base. |
| **Index** | Data structure enabling fast search and retrieval. |
| **Indexing** | Process of creating embeddings and search indexes for documents. |
| **Inference** | Process of generating AI model outputs. |
| **Instruct Model** | AI model fine-tuned for following instructions (e.g., Llama 3.2 Instruct). |

## K

| Term | Definition |
|------|------------|
| **KB** | Knowledge Base — collection of indexed documents. |
| **Keyword Search** | Text-based search finding exact word/phrase matches. |
| **Knowledge Graph** | Visual representation of relationships between entities. |
| **Knowledge Vault** | Primary interface for managing imported documents. |

## L

| Term | Definition |
|------|------------|
| **Llama** | Meta's family of open-source large language models. |
| **Local-First** | Design philosophy prioritizing on-device processing over cloud services. |
| **LLM** | Large Language Model — AI model trained on vast text corpora. |
| **Log** | Record of system events and operations. |

## M

| Term | Definition |
|------|------------|
| **Markdown** | Lightweight markup language for formatted text. |
| **Memory** | Persistent context extracted from conversations for personalized AI interactions. |
| **Metadata** | Data about data (file size, type, import date, tags, etc.). |
| **Model** | AI system for generating human-like text. |
| **Multi-Query** | Retrieval technique generating multiple search queries from a single user query. |

## N

| Term | Definition |
|------|------------|
| **Neural Network** | Machine learning model inspired by biological neurons. |
| **Node** | Entity in the knowledge graph (document, tag, collection). |
| **Notification** | System alert or status message displayed to user. |
| **NPU** | Neural Processing Unit — specialized hardware for AI (not yet supported). |

## O

| Term | Definition |
|------|------------|
| **Ollama** | Local LLM management tool. Agent-X can use Ollama-hosted models. |
| **OpenAI** | AI company providing GPT models. Configurable as a cloud provider in Agent-X. |
| **Operator** | Human user interacting with Agent-X. |
| **Orchestration** | Coordination of multiple AI components for complex tasks. |

## P

| Term | Definition |
|------|------------|
| **Passphrase** | User-chosen string for database encryption. |
| **PBKDF2** | Password-Based Key Derivation Function — secure key derivation from passphrase. |
| **Plugin** | Extensible component adding custom functionality to Agent-X. |
| **Precision** | Measure of search result relevance (in information retrieval). |
| **Prompt** | Input text provided to AI model. |
| **Provider** | Source of AI models (Bundled, Ollama, OpenAI, Anthropic). |

## Q

| Term | Definition |
|------|------------|
| **Query** | Search request submitted by user. |
| **Question Answering** | AI task of providing answers to natural language questions. |

## R

| Term | Definition |
|------|------------|
| **RAG** | Retrieval-Augmented Generation — AI responses grounded in retrieved documents. |
| **Recall** | Measure of search completeness (fraction of relevant results found). |
| **Re-ranking** | Re-ordering search results using AI for improved relevance. |
| **REST API** | HTTP-based API for programmatic access to Agent-X functionality. |
| **Reranking** | Second-pass retrieval improvement using LLM scoring. |
| **Rolling Window** | Fixed-size buffer of recent items (e.g., last 7 days of logs). |
| **RRF** | Reciprocal Rank Fusion — algorithm for combining multiple ranked result lists. |
| **Runtime** | Execution environment for AI models (CPU or GPU). |

## S

| Term | Definition |
|------|------------|
| **SQLCipher** | SQLite extension providing transparent AES-256 encryption. |
| **SQLite** | Embedded SQL database engine used by Agent-X. |
| **Semantic Search** | Vector-based search finding conceptually similar content. |
| **Settings** | User-configurable application preferences. |
| **Sparse Retrieval** | Keyword-based search finding exact term matches. |
| **Streaming** | Real-time display of AI-generated text as it's produced. |
| **Summary** | Condensed representation of longer content. |
| **System Prompt** | Instructions defining AI behavior and persona. |

## T

| Term | Definition |
|------|------------|
| **Tag** | Descriptive label assigned to documents for organization and filtering. |
| **Temperature** | Parameter controlling AI response randomness (0 = deterministic, 1 = creative). |
| **Template** | Pre-defined structure for documents or prompts. |
| **Token** | Atomic unit of text processed by language models (~4 characters). |
| **Tokenization** | Splitting text into tokens for model processing. |
| **Transformer** | Neural network architecture underlying modern LLMs. |
| **Troubleshooting** | Systematic approach to diagnosing and resolving issues. |

## V

| Term | Definition |
|------|------------|
| **Vector** | Numerical representation of text embedding. |
| **Vector Database** | Specialized database for efficient vector similarity search. |
| **Visualization** | Graphical representation of data or relationships. |
| **Vault** | Secure storage for documents and generated content. |
| **VRAM** | Video RAM — memory on GPU for graphics and computation. |

## W

| Term | Definition |
|------|------------|
| **Workflow** | Automated sequence of operations (import, digest, re-index, etc.). |
| **Windows App SDK** | Microsoft framework for building Windows applications (formerly Project Reunion). |
| **WinUI 3** | Native UI platform for Windows 11 and Windows 10. |

---

## Keyboard Shortcuts Reference

| Shortcut | Action |
|----------|--------|
| `Ctrl+K` | Open command palette |
| `Ctrl+I` | Open AI Chat |
| `Ctrl+L` | Open Knowledge Vault |
| `Ctrl+F` | Open Search |
| `Ctrl+G` | Open Knowledge Graph |
| `Ctrl+W` | Open Workflows |
| `Ctrl+A` | Open Analytics |
| `Ctrl+M` | Open Model Manager |
| `Ctrl+,` | Open Settings |
| `Ctrl+Q` | Quick actions |
| `F5` | Refresh current view |
| `Ctrl+N` | New conversation |
| `Ctrl+S` | Save current document/conversation |
| `Ctrl+C` | Copy selected text |
| `Ctrl+V` | Paste text |
| `Ctrl+X` | Cut selected text |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` | Redo |
| `Ctrl+F` | Find in current view |
| `Escape` | Close dialog or cancel operation |
| `Delete` | Delete selected item |
| `F2` | Rename selected item |
| `Enter` | Open selected item |
| `Ctrl+A` | Select all |
| `Ctrl+Shift+A` | Clear selection |

---

*Last updated: 2026-05-03*
