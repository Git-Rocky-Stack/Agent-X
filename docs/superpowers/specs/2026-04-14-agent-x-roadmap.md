# Agent-X Prioritized Roadmap

**Date:** 2026-04-14
**Strategy:** Value Ladder — Foundation → Expansion → Enrichment
**Current Version:** v1.3.0
**Reference:** [Strategic Gap Analysis](2026-04-14-agent-x-gap-analysis.md) | [Competitive Analysis](../../competitive-analysis-2026-04.md)

---

## v1.4 — Foundation + First Expansion

**Theme:** Trust, capture, and seamlessness. Fix what undermines the $249 price point, then add the #1 capture channel.

### Feature 1: DPAPI API Key Encryption

**Gap:** Plaintext API keys in `settings.json` — any process can read OpenAI/Anthropic keys. Security dealbreaker at $249.

**Description:** Encrypt all API keys using Windows DPAPI (Data Protection API). Keys are encrypted per-user, per-machine. Only Agent-X running as the current Windows user can decrypt them. The plaintext keys are never written to disk.

**User Stories:**
- As a user, I want my API keys encrypted at rest so that no other application or process can read them.
- As a user, I want Agent-X to automatically migrate my existing plaintext keys to encrypted storage on first launch.
- As a user, I want to see a security status indicator showing my keys are protected.

**Acceptance Criteria:**
- All API keys (OpenAI, Anthropic, Ollama custom endpoints) stored via DPAPI
- Automatic migration of existing plaintext keys on first launch
- `SettingsService` reads/writes only through DPAPI wrapper
- No plaintext keys ever written to disk after migration
- Unit tests for encryption, decryption, and migration
- Settings UI shows lock icon next to encrypted key fields

**Effort:** Medium (2-3 weeks)
**Persona Impact:** All three personas (power researcher, knowledge worker, developer)
**Competitive Justification:** No local AI desktop app encrypts API keys with DPAPI. This becomes a differentiator in security trust.

---

### Feature 2: System Tray + Global Hotkey

**Gap:** Agent-X is "just another window." Planned for v1.3.0 but not built. Users cannot quickly invoke Agent-X without switching to it.

**Description:** Add system tray icon with status indicator (AI connected/disconnected/indexing). `Win+Shift+A` global hotkey opens a quick-chat overlay that appears over any application. The overlay supports rapid Q&A against the knowledge vault without full window activation.

**User Stories:**
- As a user, I want Agent-X running in my system tray so it's always available without taking up taskbar space.
- As a user, I want to press `Win+Shift+A` and immediately ask a question against my knowledge base from any application.
- As a user, I want to see whether my AI provider is connected and how many documents are indexed at a glance from the tray icon.

**Acceptance Criteria:**
- System tray icon with tooltip showing AI status, model name, document count
- Right-click context menu: Open, Quick Chat, Settings, Exit
- `Win+Shift+A` global hotkey opens Quick Chat overlay (always-on-top, semi-transparent)
- Quick Chat overlay: text input + streaming response, references current knowledge base
- Overlay dismisses with Escape, minimizes to tray on close
- App minimizes to tray instead of closing (configurable in Settings)
- Single instance enforcement — hotkey activates existing instance

**Effort:** Medium-Large (3-4 weeks)
**Persona Impact:** All three personas — transforms daily usage pattern
**Competitive Justification:** ChatGPT Desktop has a similar quick-access pattern. This makes Agent-X feel like a native OS utility, not "another app."

---

### Feature 3: Browser Extension (Chrome/Edge)

**Gap:** The #1 way users build knowledge bases is by clipping web content. Without this, users must manually copy-paste, creating friction that kills the capture habit.

**Description:** Chrome/Edge extension that clips web pages (full article, selection, or simplified reader mode) and sends them to Agent-X's Smart Inbox via local HTTP API. The extension supports: one-click clip to vault, highlight-and-clip, batch clip multiple tabs, and automatic metadata extraction (title, author, date, source URL).

**User Stories:**
- As a researcher, I want to one-click clip any web article into my knowledge vault so I can query it later.
- As a knowledge worker, I want to highlight a passage on a web page and clip just that selection.
- As a user, I want clipped pages to appear in my Smart Inbox for triage before they're indexed.
- As a user, I want metadata (title, author, date, source URL) preserved with every clip.

**Acceptance Criteria:**
- Chrome Web Store and Edge Add-ons listing (Manifest V3)
- Three clip modes: full page, selection, reader mode (simplified extraction)
- Clips sent to Agent-X Smart Inbox via local REST API (`/api/inbox/clip`)
- Metadata extraction: title, author, published date, source URL, word count
- Batch clip: clip all open tabs at once
- Extension popup shows clip history (last 10 clips)
- Smart Inbox shows "via Browser Extension" badge on clipped items
- Agent-X must be running for extension to work (graceful error message if not)
- No cloud intermediary — all communication is localhost

**Effort:** Large (4-5 weeks, includes extension + REST API endpoint)
**Persona Impact:** Power researcher (primary), knowledge worker (primary), developer (secondary)
**Competitive Justification:** Mem, Reflect, Notion, Obsidian, and ChatGPT all have web clippers. This is table stakes for a knowledge hub. Not having one is the single biggest adoption blocker.

---

### Feature 4: Multi-Model Routing

**Gap:** Users must manually switch between Ollama/OpenAI/Anthropic. Competitors like Notion AI and Jan auto-select the best model per task.

**Description:** Add an intelligent model router that automatically selects the best available model based on task type, cost, latency, and quality. Users configure routing rules (e.g., "use local model for extraction, use GPT-4o for generation, use Claude for analysis") or enable auto-routing.

**User Stories:**
- As a user, I want Agent-X to automatically use my local model for fast extraction tasks and my cloud model for complex analysis, without me switching manually.
- As a user, I want to define routing rules: which model handles which task type.
- As a user, I want to see which model was selected for each response and why.
- As a developer, I want the plugin API to support model routing so custom workflows can specify model preferences.

**Acceptance Criteria:**
- `ModelRouterService` with configurable routing rules (task type → model)
- Default routing profiles: "Cost Optimized" (local first), "Quality Optimized" (cloud first), "Balanced"
- Task type detection: extraction, summarization, analysis, generation, code, creative
- Fallback chain: if preferred model is unavailable, try next in chain
- Per-response indicator showing which model was used and routing reason
- Settings UI for managing routing rules and profiles
- Workflow steps can specify model preference (overrides auto-routing)
- Cost tracking per model per task type in existing `CostTracker`

**Effort:** Medium-Large (3-4 weeks)
**Persona Impact:** Developer (primary — wants control), knowledge worker (secondary — wants seamlessness)
**Competitive Justification:** Notion AI auto-selects models. Jan has hybrid mode. Agent-X's multi-provider architecture makes this a natural extension that no single-provider competitor can match.

---

## v1.5 — Expansion

**Theme:** Depth of knowledge. Deeper ingestion, deeper exploration, deeper output.

### Feature 5: Web Content Ingestion Depth

**Gap:** `WebImportPage` exists but scraping is surface-level. Users import URLs but get shallow extraction.

**Description:** Overhaul web content ingestion with: JavaScript rendering (headless browser), structured data extraction (article body, author, date, tables), pagination handling for multi-page articles, PDF-from-URL download and ingestion, and sitemap/RSS feed bulk import.

**User Stories:**
- As a researcher, I want to import a web article and get the full text, not just the navigation and footer.
- As a user, I want to import an entire sitemap and have all pages indexed into my vault.
- As a user, I want to subscribe to RSS feeds and have new articles auto-import via Smart Inbox.

**Acceptance Criteria:**
- Headless browser rendering for JavaScript-heavy pages
- Readability algorithm (Mozilla Readability or equivalent) for article extraction
- Structured metadata: title, author, published date, description, canonical URL
- Table extraction from HTML tables into structured data
- Sitemap.xml parser for bulk site import
- RSS/Atom feed subscription with auto-import to Smart Inbox
- PDF-from-URL detection and download
- Rate limiting and robots.txt respect
- Progress indicator for bulk imports

**Effort:** Large (4-5 weeks)
**Persona Impact:** Power researcher (primary), knowledge worker (primary)
**Competitive Justification:** AnythingLLM and NotebookLM have deeper web ingestion. This closes the gap and makes the knowledge vault genuinely comprehensive.

---

### Feature 6: Conversation Branching

**Gap:** Cannot explore alternate AI responses without losing the original thread. Power researchers need to compare paths.

**Description:** Add branching to conversations. Users can fork from any message to explore an alternate direction, then merge back or keep branches separate. Branch tree is visualized in a sidebar. Each branch maintains its own context but shares the common prefix.

**User Stories:**
- As a researcher, I want to fork a conversation at any point to explore a different angle without losing the original thread.
- As a user, I want to see a tree view of all branches from a conversation.
- As a user, I want to compare branches side-by-side to see which path produced better results.
- As a user, I want to merge insights from a branch back into the main thread.

**Acceptance Criteria:**
- "Branch from here" action on any user message
- Branch tree visualization in conversation sidebar
- Side-by-side branch comparison view
- Each branch has independent context after the fork point
- Shared prefix is stored once (not duplicated)
- Branches can be named for easy identification
- "Merge to main" action copies selected insights from branch to main thread
- Branching works with all AI providers
- Conversation export includes branch structure

**Effort:** Medium-Large (3-4 weeks)
**Persona Impact:** Power researcher (primary), developer (secondary)
**Competitive Justification:** ChatGPT has conversation forking. Claude has branching in Projects. This is expected functionality for a $249 intelligence tool.

---

### Feature 7: Export Format Expansion

**Gap:** Export exists but format coverage is narrow. Users cannot get work product out in shareable formats.

**Description:** Expand export to support: PDF (with citations), DOCX (with formatting), Markdown (with frontmatter), HTML (standalone page), PowerPoint (slide decks from analysis), and CSV/Excel (tabular data from analytics). Add export templates (research report, executive summary, annotated bibliography).

**User Stories:**
- As a researcher, I want to export a conversation with full citations as a formatted PDF.
- As a knowledge worker, I want to export a comparative analysis as a PowerPoint deck.
- As a user, I want to export search results as a structured CSV for further analysis in Excel.
- As a user, I want to use pre-built export templates (research report, executive summary) so I don't have to format from scratch.

**Acceptance Criteria:**
- PDF export with embedded citations, page numbers, and header/footer
- DOCX export with headings, tables, and citations
- Markdown export with YAML frontmatter (title, date, sources)
- HTML export as standalone page with embedded styles
- PowerPoint export (title slide + content slides from analysis)
- CSV/Excel export for search results and analytics data
- 3 export templates: Research Report, Executive Summary, Annotated Bibliography
- Batch export: select multiple conversations → export all
- Export preserves knowledge graph references where applicable

**Effort:** Medium (3-4 weeks)
**Persona Impact:** Knowledge worker (primary), power researcher (primary)
**Competitive Justification:** NotebookLM exports to audio. Notion exports to multiple formats. Agent-X's export must validate the entire pipeline — "in and out."

---

### Feature 8: Deep Research Mode

**Gap:** No optional web search when vault knowledge isn't sufficient. Users hit the boundary of their local files.

**Description:** Add an optional "Research Mode" that supplements local vault knowledge with web search results. When enabled, the RAG pipeline queries both the local vault and web sources (via configurable search API — Brave Search, Serper, or SearXNG for self-hosted). Results are clearly marked as "local" vs. "web" with attribution. Web sources can be optionally saved to the vault.

**User Stories:**
- As a researcher, I want to ask a question and get answers from both my local knowledge base and the web, with clear attribution for each source.
- As a user, I want to toggle Research Mode on/off for individual queries.
- As a user, I want web-sourced results to be marked so I can distinguish verified local knowledge from external web results.
- As a user, I want to save useful web results to my vault with one click for future reference.

**Acceptance Criteria:**
- Research Mode toggle in chat input (per-query or persistent)
- Configurable search API: Brave Search API, Serper API, or self-hosted SearXNG
- Results clearly tagged: [Vault] for local, [Web] for external
- Citations include source URL for web results
- "Save to Vault" action on web results → Smart Inbox for triage
- Research Mode respects offline setting — disabled when user has chosen offline-only
- Cost tracking for web search API calls
- Search results cached locally (configurable TTL)

**Effort:** Medium-Large (3-4 weeks)
**Persona Impact:** Power researcher (primary), knowledge worker (secondary)
**Competitive Justification:** ChatGPT has web search. Perplexity is built on it. NotebookLM sources web results. This extends the vault beyond local files without compromising the local-first promise.

---

## v2.0 — Enrichment Begins

**Theme:** Scale, connect, and expand. Performance at scale, external integrations, and platform expansion.

### Feature 9: ANN Vector Scaling (HNSW)

**Gap:** Full C# cosine similarity scan hits ~500ms at 100K embeddings. No approximate nearest neighbor index.

**Description:** Replace linear scan with HNSW (Hierarchical Navigable Small World) index for vector search. Use a .NET HNSW library (or bind to hnswlib via P/Invoke). Support both in-memory and disk-backed indices. Automatic index build during document ingestion. Configurable parameters for recall vs. speed tradeoff.

**Acceptance Criteria:**
- Sub-50ms vector search at 100K+ embeddings
- HNSW index auto-built during ingestion pipeline
- Configurable M (connections per node) and ef_construction parameters
- Index persistence to disk (load on startup, not rebuild)
- Graceful fallback to linear scan for small collections (<10K embeddings)
- Benchmark: 100K embeddings, p95 latency <50ms, recall >95%
- Migration path from existing SqliteVecStore to HNSW index

**Effort:** Large (4-6 weeks)
**Persona Impact:** All personas (affects everyone at scale)
**Competitive Justification:** Performance is trust. Users who hit scaling walls lose confidence in the product.

---

### Feature 10: Calendar/Email Integration (via Plugin API)

**Gap:** Knowledge hub is disconnected from calendar and email — the two places users spend the most time.

**Description:** Build two first-party plugins using the existing Plugin API: (1) Outlook/Google Calendar connector that syncs upcoming meetings and their context into the vault, (2) Outlook/Gmail connector that triages email into Smart Inbox. Both use OAuth2 for secure access.

**Acceptance Criteria:**
- Calendar plugin: syncs upcoming meetings, attendees, agendas into vault as structured documents
- Email plugin: triages email into Smart Inbox with AI-powered categorization
- Both plugins use OAuth2 (no password storage)
- Plugins appear in Plugin Manager with enable/disable/settings
- Calendar events become searchable in knowledge vault
- Email content searchable alongside documents
- Privacy controls: per-folder/per-calendar sync scope

**Effort:** Very Large (6-8 weeks for both)
**Persona Impact:** Knowledge worker (primary), power researcher (secondary)
**Competitive Justification:** Reflect, Capacities, Notion AI, and ChatGPT all connect to calendar/email. A knowledge hub disconnected from these is incomplete.

---

### Feature 11: macOS Support

**Gap:** Windows-only excludes ~35% of power users (Mac).

**Description:** Port Agent-X to macOS using .NET 8 + Mac Catalyst or Avalonia UI. Priority: core functionality (chat, RAG, search, vault management, settings). Secondary: system tray, global hotkey, native integrations. The port maintains feature parity for all v1.4 and v1.5 features.

**Acceptance Criteria:**
- Full chat, RAG, search, vault, settings functionality on macOS
- System tray and global hotkey (Cmd+Shift+A)
- Ollama integration (already cross-platform)
- Local LLM via LLamaSharp on macOS (Metal GPU acceleration)
- Installer (DMG) with bundled model
- Feature parity for v1.4-v1.5 features
- CI/CD pipeline for macOS builds

**Effort:** Very Large (8-12 weeks)
**Persona Impact:** All personas on Mac — unlocks 35% of addressable market
**Competitive Justification:** Jan, LM Studio, AnythingLLM, GPT4All, Obsidian, and Logseq all support macOS. Not supporting Mac is leaving money on the table.

---

### Feature 12: Screen Awareness

**Gap:** Cannot "see" what the user is working on. ChatGPT Desktop and Claude Desktop have screen reading.

**Description:** Add optional screen awareness mode. When enabled, Agent-X can capture the current screen or active window, perform OCR, and make the content available as context for AI queries. User controls what's captured (full screen, active window, selected region). Captured content is temporary — not saved to vault unless user chooses to.

**Acceptance Criteria:**
- Screen capture: full screen, active window, or user-selected region
- OCR extraction from captured screen content
- "Ask about screen" Quick Chat command
- Screen content used as context but NOT auto-saved to vault
- "Save screen capture to vault" action available
- Privacy controls: per-session enable/disable, automatic purge of screen captures
- Works with system tray Quick Chat overlay

**Effort:** Large (4-6 weeks)
**Persona Impact:** Knowledge worker (primary), developer (secondary)
**Competitive Justification:** ChatGPT Desktop and Claude Desktop both have screen awareness. AnythingLLM Assistant does too. This is becoming table stakes for desktop AI.

---

## v2.5+ — Enrichment Continues

**Theme:** Platform and ecosystem. Turn Agent-X from a tool into a platform.

### Feature 13: Mobile Companion

**Description:** .NET MAUI companion app (iOS + Android) for reviewing conversations, searching the vault, and receiving notifications. Not a full client — a companion for when users are away from their desk.

**Acceptance Criteria:** Browse conversations, search vault, view annotations, push notifications for Smart Inbox items, sync via Collaborative Sync protocol.

**Effort:** Very Large (8-12 weeks)
**Persona Impact:** All personas on mobile

---

### Feature 14: Plugin Marketplace

**Description:** Distribution platform for community plugins. Developers submit plugins, users discover and install them from within Agent-X. Marketplace handles discovery, ratings, version management, and auto-updates.

**Acceptance Criteria:** Plugin submission workflow, discovery UI with categories/search, ratings/reviews, auto-update mechanism, sandboxed execution.

**Effort:** Very Large (8-12 weeks)
**Persona Impact:** Developer (primary — builds ecosystem), all users (secondary — more capabilities)

---

### Feature 15: Full i18n

**Description:** Complete localization for UI, error messages, and documentation. Target languages: Spanish, French, German, Japanese, Chinese (Simplified), Korean. Leverage existing `LocalizationService` foundation.

**Acceptance Criteria:** All UI strings externalized, RTL support for Arabic/Hebrew, date/number formatting per locale, language switcher in Settings, community translation framework.

**Effort:** Large (4-6 weeks per language batch)
**Persona Impact:** All non-English-speaking personas

---

### Feature 16: Audio Summaries

**Description:** Text-to-speech summaries of collections, conversations, and search results. Inspired by NotebookLM's Audio Overview. Uses Windows built-in TTS or cloud TTS APIs.

**Acceptance Criteria:** Generate audio summary from collection/conversation, adjustable length, voice selection, offline TTS support, save as audio file.

**Effort:** Medium (3-4 weeks)
**Persona Impact:** Knowledge worker (primary — consume during commute)

---

### Feature 17: Team/Multi-User Mode

**Description:** Shared workspaces with user accounts, permissions, and collaborative editing. Builds on the existing Collaborative Sync infrastructure.

**Acceptance Criteria:** User accounts, role-based access control, shared workspace, real-time collaboration indicators, admin dashboard.

**Effort:** Very Large (12-16 weeks)
**Persona Impact:** Enterprise knowledge workers, teams

---

## Roadmap Timeline

| Version | Theme | Features | Estimated Duration |
|---------|-------|----------|--------------------|
| **v1.4** | Foundation + First Expansion | DPAPI Encryption, System Tray, Browser Extension, Multi-Model Routing | 10-14 weeks |
| **v1.5** | Expansion | Web Ingestion Depth, Conversation Branching, Export Expansion, Deep Research | 13-17 weeks |
| **v2.0** | Enrichment Begins | ANN Vector Scaling, Calendar/Email, macOS, Screen Awareness | 22-34 weeks |
| **v2.5+** | Enrichment Continues | Mobile Companion, Plugin Marketplace, i18n, Audio Summaries, Team Mode | 35-50 weeks |

**Total estimated roadmap:** 18-24 months for full enrichment.

**Critical path:** v1.4 features (especially Browser Extension and System Tray) should ship together or within weeks of each other. They form the "capture habit" that makes the rest of the roadmap viable.

---

*Gap analysis: [`docs/superpowers/specs/2026-04-14-agent-x-gap-analysis.md`](2026-04-14-agent-x-gap-analysis.md)*
*Competitive analysis: [`docs/competitive-analysis-2026-04.md`](../../competitive-analysis-2026-04.md)*